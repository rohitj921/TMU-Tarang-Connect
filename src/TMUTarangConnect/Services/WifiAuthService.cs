using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TMUTarangConnect.Models;

namespace TMUTarangConnect.Services;

public class WifiAuthService
{
    private readonly AppConfig _config;
    private readonly ConnectionStats _stats;
    private CancellationTokenSource? _workerCts;
    private DateTime? _outageStartTime;
    private string? _currentMagicToken;
    private DateTime _lastKeepalivePing = DateTime.MinValue;

    public event Action<string, string, string>? NotificationRequested;
    public event Action? StatusChanged;

    public WifiAuthService(AppConfig config, ConnectionStats stats)
    {
        _config = config;
        _stats = stats;
        _stats.Gateway = GetGatewayHost();
    }

    private string GetGatewayHost()
    {
        try
        {
            var uri = new Uri(_config.LoginUrl);
            return $"{uri.Host}:{uri.Port}";
        }
        catch
        {
            return "172.14.0.1:1000";
        }
    }

    public async Task<bool> CheckInternetAsync(int timeoutMs = 4000)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var request = new HttpRequestMessage(HttpMethod.Head, _config.TestUrl);
            using var response = await client.SendAsync(request);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                _stats.LatencyMs = (int)sw.ElapsedMilliseconds;
                return true;
            }
            return false;
        }
        catch
        {
            sw.Stop();
            _stats.LatencyMs = -1;
            return false;
        }
    }

    public void DetectLocalIp()
    {
        try
        {
            var uri = new Uri(_config.LoginUrl);
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect(uri.Host, uri.Port > 0 ? uri.Port : 1000);
            if (socket.LocalEndPoint is IPEndPoint ep)
            {
                _stats.AssignedIp = ep.Address.ToString();
            }
        }
        catch
        {
            _stats.AssignedIp = "172.14.x.x";
        }
    }

    public async Task<(bool Success, string Message)> LoginAsync()
    {
        _stats.IsPaused = false; // Un-pause if manually connecting

        if (string.IsNullOrWhiteSpace(_config.Username) || string.IsNullOrWhiteSpace(_config.Password))
        {
            return (false, "Please configure your Campus Wi-Fi Username and Password first.");
        }

        // 0. FIRST: Check if internet is ALREADY active!
        _stats.CurrentStatusText = "Verifying internet connection...";
        StatusChanged?.Invoke();

        if (await CheckInternetAsync(timeoutMs: 3000))
        {
            DetectLocalIp();
            UpdateStateOnConnect(false);
            ConfigService.Log("OK", $"Connection is already active! Latency: {_stats.LatencyDisplay}.");
            return (true, $"Already connected to TMU network! ({_stats.LatencyDisplay})");
        }

        if (!_config.HasValidCredentials)
        {
            ConfigService.Log("WARN", "No credentials configured. Please add an account in Credentials to authenticate.");
            _stats.CurrentStatusText = "Credentials required";
            StatusChanged?.Invoke();
            return (false, "No credentials configured. Please add an account in Credentials.");
        }

        _stats.CurrentStatusText = "Connecting to FortiGate portal...";
        StatusChanged?.Invoke();

        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            _stats.CurrentStatusText = $"Authenticating (Attempt {attempt}/{_config.MaxRetries})...";
            StatusChanged?.Invoke();
            ConfigService.Log("INFO", $"Login attempt {attempt} / {_config.MaxRetries}...");

            try
            {
                // 1. Detect captive portal redirect
                var cookieContainer = new CookieContainer();
                using var noRedirectHandler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    CookieContainer = cookieContainer
                };
                using var redirectClient = new HttpClient(noRedirectHandler)
                {
                    Timeout = TimeSpan.FromSeconds(6)
                };

                string? redirectUrl = null;
                try
                {
                    var triggerRes = await redirectClient.GetAsync("http://www.msftconnecttest.com/redirect");
                    if (triggerRes.Headers.Location != null)
                    {
                        redirectUrl = triggerRes.Headers.Location.ToString();
                    }
                    else
                    {
                        var html = await triggerRes.Content.ReadAsStringAsync();
                        var match = Regex.Match(html, @"window\.location=""([^""]+)""");
                        if (match.Success) redirectUrl = match.Groups[1].Value;
                    }
                }
                catch (Exception ex)
                {
                    ConfigService.Log("WARN", $"Captive trigger exception: {ex.Message}");
                }

                // If redirected to standard web url (e.g. go.microsoft.com), verify if internet is online!
                if (!string.IsNullOrEmpty(redirectUrl) && !redirectUrl.Contains("fgtauth", StringComparison.OrdinalIgnoreCase))
                {
                    if (await CheckInternetAsync(timeoutMs: 3000))
                    {
                        DetectLocalIp();
                        UpdateStateOnConnect(false);
                        ConfigService.Log("OK", $"Connected! Active internet verified ({_stats.LatencyDisplay}).");
                        return (true, $"Connected! Latency: {_stats.LatencyDisplay}");
                    }
                }

                if (string.IsNullOrEmpty(redirectUrl))
                {
                    if (await CheckInternetAsync(timeoutMs: 3000))
                    {
                        DetectLocalIp();
                        UpdateStateOnConnect(false);
                        return (true, $"Already connected to internet! ({_stats.LatencyDisplay})");
                    }

                    ConfigService.Log("WARN", "No captive portal redirect received. Are you connected to university Wi-Fi/LAN?");
                    await Task.Delay(1500);
                    continue;
                }

                // 2. Extract magic token
                var magicMatch = Regex.Match(redirectUrl, @"fgtauth\?([a-fA-F0-9]+)");
                if (!magicMatch.Success)
                {
                    magicMatch = Regex.Match(redirectUrl, @"fgtauth\?(.+)$");
                }

                if (!magicMatch.Success)
                {
                    if (await CheckInternetAsync(timeoutMs: 3000))
                    {
                        DetectLocalIp();
                        UpdateStateOnConnect(false);
                        return (true, $"Connected! Latency: {_stats.LatencyDisplay}");
                    }

                    ConfigService.Log("WARN", $"Magic token not found in URL: {redirectUrl}");
                    await Task.Delay(1500);
                    continue;
                }

                _currentMagicToken = magicMatch.Groups[1].Value;
                ConfigService.Log("OK", $"Magic token acquired: {_currentMagicToken}");

                // Construct initial standard FortiGate logout URL from magic token
                _config.LastLogoutUrl = $"{_config.LoginUrl.TrimEnd('/')}/logout?{_currentMagicToken}";
                ConfigService.SaveConfig(_config);

                // 3. Bind session cookies
                using var sessionClient = new HttpClient(noRedirectHandler)
                {
                    Timeout = TimeSpan.FromSeconds(8)
                };
                sessionClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

                try
                {
                    await sessionClient.GetAsync($"{_config.LoginUrl}/fgtauth?{_currentMagicToken}");
                }
                catch { }

                // 4. Send POST credentials
                var postContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("4Tredir", "http://www.msftconnecttest.com/redirect"),
                    new KeyValuePair<string, string>("magic", _currentMagicToken),
                    new KeyValuePair<string, string>("username", _config.Username),
                    new KeyValuePair<string, string>("password", _config.Password)
                });

                var loginRequest = new HttpRequestMessage(HttpMethod.Post, $"{_config.LoginUrl}/")
                {
                    Content = postContent
                };
                loginRequest.Headers.Add("Origin", _config.LoginUrl);
                loginRequest.Headers.Add("Referer", $"{_config.LoginUrl}/fgtauth?{_currentMagicToken}");

                var loginResponse = await sessionClient.SendAsync(loginRequest);
                var responseHtml = await loginResponse.Content.ReadAsStringAsync();

                var activeAcc = _config.GetActiveAccount();

                // Check for FortiGate Concurrent Device Limit (e.g. 2 devices limit reached)
                if (responseHtml.Contains("concurrent authentication is over limit", StringComparison.OrdinalIgnoreCase) ||
                    responseHtml.Contains("over limit", StringComparison.OrdinalIgnoreCase))
                {
                    activeAcc.LastStatus = "Over Limit";
                    ConfigService.Log("WARN", $"Account '{activeAcc.Label}' ({activeAcc.Username}) hit FortiGate concurrent device limit.");

                    if (_config.AutoRotateOnLimit && _config.Accounts.Count > 1)
                    {
                        var nextAcc = _config.RotateToNextAccount(activeAcc.Username, "Over Limit");
                        if (nextAcc != null && nextAcc.Username != activeAcc.Username)
                        {
                            ConfigService.SaveConfig(_config);
                            ConfigService.Log("OK", $"🔄 Auto-rotating to alternate account '{nextAcc.Label}' ({nextAcc.Username})...");
                            _stats.CurrentStatusText = $"Switching to {nextAcc.Label}...";
                            _stats.ActiveAccountDisplay = nextAcc.Label;
                            StatusChanged?.Invoke();
                            NotificationRequested?.Invoke("Account Auto-Rotated", $"Limit reached for {activeAcc.Label}. Switched to {nextAcc.Label}.", "wifi_connected.png");

                            await Task.Delay(800);
                            return await LoginAsync();
                        }
                    }

                    _stats.IsConnected = false;
                    _stats.CurrentStatusText = $"Device Limit ({activeAcc.Label})";
                    StatusChanged?.Invoke();
                    return (false, $"Device Limit Reached for {activeAcc.Label} ({activeAcc.Username}): Maximum devices already active.");
                }

                // Check for FortiGate Invalid Username or Password
                if (responseHtml.Contains("Please Check User Name & Password", StringComparison.OrdinalIgnoreCase) ||
                    responseHtml.Contains("Authentication Failed", StringComparison.OrdinalIgnoreCase))
                {
                    activeAcc.LastStatus = "Invalid";
                    ConfigService.Log("WARN", $"Account '{activeAcc.Label}' ({activeAcc.Username}) credentials rejected by FortiGate.");

                    if (_config.AutoRotateOnLimit && _config.Accounts.Count > 1)
                    {
                        var nextAcc = _config.RotateToNextAccount(activeAcc.Username, "Invalid");
                        if (nextAcc != null && nextAcc.Username != activeAcc.Username)
                        {
                            ConfigService.SaveConfig(_config);
                            ConfigService.Log("OK", $"🔄 Auto-rotating to alternate account '{nextAcc.Label}' ({nextAcc.Username})...");
                            _stats.CurrentStatusText = $"Switching to {nextAcc.Label}...";
                            _stats.ActiveAccountDisplay = nextAcc.Label;
                            StatusChanged?.Invoke();
                            NotificationRequested?.Invoke("Account Auto-Rotated", $"Invalid credentials on {activeAcc.Label}. Switched to {nextAcc.Label}.", "wifi_connected.png");

                            await Task.Delay(800);
                            return await LoginAsync();
                        }
                    }

                    _stats.IsConnected = false;
                    _stats.CurrentStatusText = $"Invalid Creds ({activeAcc.Label})";
                    StatusChanged?.Invoke();
                    return (false, $"Authentication Failed: Incorrect credentials for '{activeAcc.Label}' ({activeAcc.Username}).");
                }

                activeAcc.LastStatus = "Active";
                _stats.ActiveAccountDisplay = activeAcc.Label;

                // 5. Look for exact logout link in response HTML
                var logoutMatch = Regex.Match(responseHtml, @"(?:href=[\""']?|location[\s=:]+[\""']?)([^\""'>\s]*logout\?[a-fA-F0-9]+)[\""']?", RegexOptions.IgnoreCase);
                if (logoutMatch.Success)
                {
                    var foundLogout = logoutMatch.Groups[1].Value;
                    if (!foundLogout.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        foundLogout = $"{_config.LoginUrl.TrimEnd('/')}/{foundLogout.TrimStart('/')}";
                    }
                    _config.LastLogoutUrl = foundLogout;
                    ConfigService.SaveConfig(_config);
                    ConfigService.Log("OK", $"Dynamic logout link stored: {foundLogout}");
                }

                // Look for dynamic keepalive renewal link
                var keepaliveMatch = Regex.Match(responseHtml, @"(?:href=[\""']?|location[\s=:]+[\""']?)([^\""'>\s]*keepalive\?[a-fA-F0-9]+)[\""']?", RegexOptions.IgnoreCase);
                if (keepaliveMatch.Success)
                {
                    var foundKeepalive = keepaliveMatch.Groups[1].Value;
                    if (!foundKeepalive.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        foundKeepalive = $"{_config.LoginUrl.TrimEnd('/')}/{foundKeepalive.TrimStart('/')}";
                    }
                    _config.LastKeepaliveUrl = foundKeepalive;
                    _lastKeepalivePing = DateTime.Now;
                    ConfigService.SaveConfig(_config);
                    ConfigService.Log("OK", $"Dynamic keepalive link stored: {foundKeepalive}");
                }

                ConfigService.Log("OK", "Login POST sent. Verifying connectivity...");
                await Task.Delay(1500);

                if (await CheckInternetAsync(timeoutMs: 4000))
                {
                    DetectLocalIp();
                    UpdateStateOnConnect(true);
                    return (true, $"Internet access restored successfully! ({_stats.LatencyDisplay})");
                }
            }
            catch (Exception ex)
            {
                ConfigService.Log("ERROR", $"Login attempt error: {ex.Message}");
            }

            await Task.Delay(1500);
        }

        UpdateStateOnDisconnect("Login failed after retries.");
        return (false, "Could not authenticate with university network after multiple attempts.");
    }

    public async Task<(bool Success, string Message)> LogoutAsync()
    {
        try
        {
            // Stop/pause background worker from immediately re-authenticating
            _stats.IsPaused = true;

            string logoutUrl;
            if (!string.IsNullOrWhiteSpace(_config.LastLogoutUrl))
            {
                logoutUrl = _config.LastLogoutUrl;
            }
            else if (!string.IsNullOrWhiteSpace(_currentMagicToken))
            {
                logoutUrl = $"{_config.LoginUrl.TrimEnd('/')}/logout?{_currentMagicToken}";
            }
            else
            {
                logoutUrl = $"{_config.LoginUrl.TrimEnd('/')}/logout";
            }

            ConfigService.Log("INFO", $"Executing FortiGate logout: {logoutUrl}");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            await client.GetAsync(logoutUrl);

            _stats.IsConnected = false;
            _stats.ConnectedSince = null;
            _stats.LatencyMs = -1;
            _stats.CurrentStatusText = "Logged Out (Auto-login paused)";
            StatusChanged?.Invoke();

            ConfigService.Log("OK", "Logged out from FortiGate successfully. Auto-login is paused.");
            NotificationRequested?.Invoke("TMU WiFi Logged Out", "You have disconnected. Auto-login is paused until you click Connect.", "wifi_disconnected.png");
            return (true, "Logged out successfully.");
        }
        catch (Exception ex)
        {
            ConfigService.Log("WARN", $"Logout request error: {ex.Message}");
            return (false, $"Logout error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> KickDeviceAsync(string tokenOrUrl)
    {
        if (string.IsNullOrWhiteSpace(tokenOrUrl))
        {
            return (false, "Please provide a valid session token (e.g. 060303000b0000c2) or full logout URL.");
        }

        string kickUrl;
        var tokenMatch = Regex.Match(tokenOrUrl.Trim(), @"[a-fA-F0-9]{16}");
        if (tokenMatch.Success)
        {
            kickUrl = $"{_config.LoginUrl.TrimEnd('/')}/logout?{tokenMatch.Value}";
        }
        else if (tokenOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            kickUrl = tokenOrUrl.Trim();
        }
        else
        {
            return (false, "Unrecognized format. Enter the 16-hex token or full logout URL.");
        }

        try
        {
            ConfigService.Log("INFO", $"Transmitting remote session kick: {kickUrl}");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            var res = await client.GetAsync(kickUrl);
            ConfigService.Log("OK", $"Kick signal delivered. HTTP {(int)res.StatusCode}");
            return (true, $"Remote kick sent for session {tokenMatch.Value}!");
        }
        catch (Exception ex)
        {
            ConfigService.Log("WARN", $"Kick attempt failed: {ex.Message}");
            return (false, $"Kick failed: {ex.Message}");
        }
    }

    public async Task SendKeepalivePingIfNeededAsync()
    {
        if (!_stats.IsConnected || string.IsNullOrWhiteSpace(_config.LastKeepaliveUrl))
        {
            return;
        }

        // FortiGate sessions timeout in 3600s (60 min). We renew every 25 minutes.
        if ((DateTime.Now - _lastKeepalivePing).TotalMinutes < 25)
        {
            return;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            var res = await client.GetAsync(_config.LastKeepaliveUrl);
            _lastKeepalivePing = DateTime.Now;

            if (res.IsSuccessStatusCode)
            {
                var html = await res.Content.ReadAsStringAsync();
                var nextKeepalive = Regex.Match(html, @"(?:href=[\""']?|location[\s=:]+[\""']?)([^\""'>\s]*keepalive\?[a-fA-F0-9]+)[\""']?", RegexOptions.IgnoreCase);
                if (nextKeepalive.Success)
                {
                    var nextUrl = nextKeepalive.Groups[1].Value;
                    if (!nextUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        nextUrl = $"{_config.LoginUrl.TrimEnd('/')}/{nextUrl.TrimStart('/')}";
                    }
                    _config.LastKeepaliveUrl = nextUrl;
                    ConfigService.SaveConfig(_config);
                }
                ConfigService.Log("OK", "FortiGate keepalive heartbeat successfully renewed (3600s lease extended).");
            }
        }
        catch (Exception ex)
        {
            ConfigService.Log("WARN", $"Keepalive renewal error: {ex.Message}");
        }
    }

    public async Task RunSingleCheckAsync(bool isManual = false)
    {
        if (_stats.IsPaused && !isManual)
        {
            // Do not auto-reconnect if user explicitly logged out
            return;
        }

        bool hasInternet = await CheckInternetAsync();

        if (hasInternet)
        {
            DetectLocalIp();
            if (!_stats.IsConnected)
            {
                UpdateStateOnConnect(isAutoRestore: true);
            }
            else if (isManual)
            {
                NotificationRequested?.Invoke("Connection Active", $"Internet is working! Latency: {_stats.LatencyDisplay}", "wifi_connected.png");
            }

            // Perform automatic keepalive renewal to prevent FortiGate 3600s session timeout
            await SendKeepalivePingIfNeededAsync();
        }
        else
        {
            if (_stats.IsPaused)
            {
                return; // User intentionally logged out
            }

            if (_outageStartTime == null)
            {
                _outageStartTime = DateTime.Now;
                _stats.LastOutageTime = _outageStartTime;
            }

            _stats.IsConnected = false;
            _stats.LatencyMs = -1;
            _stats.CurrentStatusText = "Captive Blocked - Reconnecting...";
            StatusChanged?.Invoke();

            var (success, msg) = await LoginAsync();
            if (!success && isManual)
            {
                NotificationRequested?.Invoke("Login Failed", msg, "wifi_error.png");
            }
        }
    }

    private void UpdateStateOnConnect(bool isAutoRestore)
    {
        _stats.IsConnected = true;
        _stats.IsPaused = false;
        _stats.ConnectedSince = DateTime.Now;
        _stats.CurrentStatusText = "Online (Connected)";
        _stats.ActiveAccountDisplay = _config.GetActiveAccount().Label;

        if (_outageStartTime.HasValue)
        {
            _stats.LastOutageDuration = DateTime.Now - _outageStartTime.Value;
            _outageStartTime = null;
        }

        if (isAutoRestore)
        {
            _stats.AutoLoginsToday++;
            ConfigService.Log("OK", $"Connection active! Latency: {_stats.LatencyDisplay}. Restores today: {_stats.AutoLoginsToday}");
            NotificationRequested?.Invoke("TMU WiFi Connected", "Authenticated with FortiGate! Internet access is ready.", "wifi_connected.png");
        }

        StatusChanged?.Invoke();
    }

    private void UpdateStateOnDisconnect(string reason)
    {
        _stats.IsConnected = false;
        _stats.CurrentStatusText = reason;
        StatusChanged?.Invoke();
    }

    public void StartBackgroundWorker()
    {
        StopBackgroundWorker();
        _workerCts = new CancellationTokenSource();
        var token = _workerCts.Token;

        _ = Task.Run(async () =>
        {
            ConfigService.Log("INFO", "Background monitoring engine started.");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RunSingleCheckAsync();
                }
                catch (Exception ex)
                {
                    ConfigService.Log("WARN", $"Background check error: {ex.Message}");
                }

                try
                {
                    int delay = Math.Max(5, _config.CheckIntervalSeconds);
                    await Task.Delay(delay * 1000, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void StopBackgroundWorker()
    {
        if (_workerCts != null)
        {
            _workerCts.Cancel();
            _workerCts.Dispose();
            _workerCts = null;
        }
    }
}
