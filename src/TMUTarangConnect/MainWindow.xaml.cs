using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TMUTarangConnect.Models;
using TMUTarangConnect.Services;
using TMUTarangConnect.Views;
using MediaBrush = System.Windows.Media.SolidColorBrush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfMessageBox = System.Windows.MessageBox;

namespace TMUTarangConnect;

public partial class MainWindow : Window
{
    private readonly AppConfig _config;
    private readonly ConnectionStats _stats;
    private readonly WifiAuthService _authService;
    private readonly DispatcherTimer _ticker;
    private WifiAdapterStatus? _wifiStatus;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MainWindow(AppConfig config, ConnectionStats stats, WifiAuthService authService)
    {
        InitializeComponent();
        _config = config;
        _stats = stats;
        _authService = authService;

        // Set App and Window Icon safely from crisp PNG (embedded or disk)
        try
        {
            var bmp = ResourceHelper.LoadBitmap("wifi_connected.png");
            if (bmp != null)
            {
                Icon = bmp;
                ImgAppIcon.Source = bmp;
            }
        }
        catch { }

        SourceInitialized += (s, e) =>
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                int darkMode = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 11 / modern Windows 10)
                DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
                // 19 = older Windows 10 build fallback
                DwmSetWindowAttribute(handle, 19, ref darkMode, sizeof(int));
            }
            catch { }
        };

        _authService.StatusChanged += OnStatusChanged;

        LoadFormData();

        // 1-second ticker for live telemetry
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (s, e) => UpdateLiveStats();
        _ticker.Start();

        UpdateLiveStats();
        RefreshLogFeed();

        // Bandwidth & Speed Test Initialization
        BandwidthTrackerService.UsageUpdated += OnBandwidthUsageUpdated;
        UpdateBandwidthUI();
        SpeedHistoryList.ItemsSource = SpeedTestService.History;

        // Initial Wi-Fi check
        _ = RefreshWifiStatusAsync();

        // First run check: Prompt user to configure credentials if none are saved
        Loaded += (s, e) =>
        {
            if (!_config.HasValidCredentials)
            {
                OpenAccountModal(null);
            }
        };
    }

    private void OnBandwidthUsageUpdated()
    {
        Dispatcher.Invoke(() => UpdateBandwidthUI());
    }

    private void UpdateBandwidthUI()
    {
        var snap = BandwidthTrackerService.GetSnapshot();
        TxtTopDataToday.Text = snap.TodayDisplay;
        TxtUsageToday.Text = snap.TodayDisplay;
        TxtUsageTodayBreakdown.Text = snap.TodayBreakdown;
        TxtUsageMonth.Text = snap.MonthDisplay;
        TxtUsageMonthBreakdown.Text = snap.MonthBreakdown;
        TxtUsageSession.Text = snap.SessionDisplay;
        TxtUsageSessionBreakdown.Text = snap.SessionBreakdown;
    }

    private void LoadFormData()
    {
        ChkAutoRotate.IsChecked = _config.AutoRotateOnLimit;
        ChkNotifications.IsChecked = _config.ShowNotifications;
        ChkStartup.IsChecked = StartupService.IsAutoStartEnabled();

        TxtInterval.Text = _config.CheckIntervalSeconds.ToString();
        TxtMaxRetries.Text = _config.MaxRetries.ToString();
        TxtRetryDelay.Text = _config.RetryDelaySeconds.ToString();
        TxtTestUrl.Text = _config.TestUrl;

        RefreshAccountsList();
    }

    private void RefreshAccountsList()
    {
        AccountsList.ItemsSource = null;
        AccountsList.ItemsSource = _config.Accounts;
        TxtActiveAccountBadge.Text = _config.HasValidCredentials ? _config.GetActiveAccount().Label : "No Credentials";
        if (TxtPoolCountBadge != null)
        {
            TxtPoolCountBadge.Text = $"{_config.Accounts.Count} ACCOUNTS";
        }
    }

    private void UpdateLiveStats()
    {
        _stats.NotifyUptimeTick();
        TxtActiveAccountBadge.Text = _config.HasValidCredentials ? _config.GetActiveAccount().Label : "No Credentials";

        if (_stats.IsConnected)
        {
            StatusDot.Fill = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#10B981"));
            StatusDotGlow.Background = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#133626"));
            TxtStatus.Text = "Online (Connected)";
            TxtUptime.Text = $"Connected for: {_stats.UptimeDisplay}";
            BtnConnect.Content = "Connect Now";
        }
        else if (_stats.IsPaused)
        {
            StatusDot.Fill = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#F59E0B"));
            StatusDotGlow.Background = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#36250B"));
            TxtStatus.Text = "Logged Out (Auto-login paused)";
            TxtUptime.Text = "Paused";
            BtnConnect.Content = "Resume & Connect";
        }
        else
        {
            StatusDot.Fill = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#EF4444"));
            StatusDotGlow.Background = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#361414"));
            TxtStatus.Text = string.IsNullOrWhiteSpace(_stats.CurrentStatusText) ? "Offline (Disconnected)" : _stats.CurrentStatusText;
            TxtUptime.Text = "Offline";
            BtnConnect.Content = "Connect Now";
        }

        TxtAssignedIp.Text = _stats.AssignedIp;
        TxtLatency.Text = _stats.LatencyDisplay;
        TxtSpeed.Text = _stats.SpeedDisplay;
        TxtTopDataToday.Text = BandwidthTrackerService.GetSnapshot().TodayDisplay;
    }

    private void OnStatusChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateLiveStats();
            RefreshLogFeed();
        });
    }

    // ──────────────────────────────────────────
    // Custom Title Bar Controls
    // ──────────────────────────────────────────
    private void OnMinimizeWindowClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseToTrayClicked(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Minimize to tray instead of quitting
        e.Cancel = true;
        Hide();
    }

    // ──────────────────────────────────────────
    // Modal Dialog: Add / Edit Account
    // ──────────────────────────────────────────
    private string? _modalEditingAccountId = null;
    private bool _isModalPasswordVisible = false;

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape && AccountModalOverlay.Visibility == Visibility.Visible)
        {
            CloseAccountModal();
        }
    }

    private void OpenAccountModal(UserCredential? acc = null)
    {
        _modalEditingAccountId = acc?.Id;
        _isModalPasswordVisible = false;

        if (acc != null)
        {
            TxtModalTitle.Text = $"✏️ Edit Account: {acc.Label}";
            TxtModalSubtitle.Text = "Enter university Wi-Fi portal login credentials";
            TxtModalLabel.Text = acc.Label;
            TxtModalUser.Text = acc.Username;
            TxtModalPassBox.Password = acc.Password;
            TxtModalPassText.Text = acc.Password;
            BtnModalSave.Content = "💾 Update Account";
        }
        else
        {
            bool isFirstSetup = !_config.HasValidCredentials;
            TxtModalTitle.Text = isFirstSetup ? "👋 Welcome to TMU Tarang Connect" : "➕ Add Campus Account";
            TxtModalSubtitle.Text = isFirstSetup
                ? "Please enter your campus Wi-Fi credentials to get started"
                : "Enter university Wi-Fi portal login credentials";
            TxtModalLabel.Text = _config.Accounts.Count == 0 ? "Primary Account" : $"Account {_config.Accounts.Count + 1}";
            TxtModalUser.Text = "";
            TxtModalPassBox.Password = "";
            TxtModalPassText.Text = "";
            BtnModalSave.Content = isFirstSetup ? "💾 Save & Connect" : "+ Add to Pool";
        }

        TxtModalPassBox.Visibility = Visibility.Visible;
        TxtModalPassText.Visibility = Visibility.Collapsed;
        BtnToggleModalPass.Content = "👁";

        AccountModalOverlay.Visibility = Visibility.Visible;
        TxtModalUser.Focus();
    }

    private void CloseAccountModal()
    {
        AccountModalOverlay.Visibility = Visibility.Collapsed;
        _modalEditingAccountId = null;
    }

    private void OnCloseModalClicked(object sender, RoutedEventArgs e)
    {
        CloseAccountModal();
    }

    private void OnModalBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == AccountModalOverlay)
        {
            CloseAccountModal();
        }
    }

    private void OnAddNewAccountClicked(object sender, RoutedEventArgs e)
    {
        OpenAccountModal(null);
    }

    private void OnToggleModalPasswordVisibility(object sender, RoutedEventArgs e)
    {
        _isModalPasswordVisible = !_isModalPasswordVisible;
        if (_isModalPasswordVisible)
        {
            TxtModalPassText.Text = TxtModalPassBox.Password;
            TxtModalPassBox.Visibility = Visibility.Collapsed;
            TxtModalPassText.Visibility = Visibility.Visible;
            BtnToggleModalPass.Content = "🔒";
        }
        else
        {
            TxtModalPassBox.Password = TxtModalPassText.Text;
            TxtModalPassText.Visibility = Visibility.Collapsed;
            TxtModalPassBox.Visibility = Visibility.Visible;
            BtnToggleModalPass.Content = "👁";
        }
    }

    private void OnModalSaveClicked(object sender, RoutedEventArgs e)
    {
        var label = TxtModalLabel.Text.Trim();
        var user = TxtModalUser.Text.Trim();
        var pass = _isModalPasswordVisible ? TxtModalPassText.Text.Trim() : TxtModalPassBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            WpfMessageBox.Show("Please enter both Username and Password for the account.", "Incomplete Credentials", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            label = $"Account {_config.Accounts.Count + 1}";
        }

        if (!string.IsNullOrEmpty(_modalEditingAccountId))
        {
            var target = _config.Accounts.FirstOrDefault(a => a.Id == _modalEditingAccountId);
            if (target != null)
            {
                target.Label = label;
                target.Username = user;
                target.Password = pass;
                target.LastStatus = "Updated";

                ConfigService.SaveConfig(_config);
                RefreshAccountsList();
                CloseAccountModal();
                ShowToast("Account Updated", $"Credentials for '{label}' updated without modifying active session.", "wifi_connected.png");
                return;
            }
        }

        bool isFirstAccount = _config.Accounts.Count == 0;
        var existing = _config.Accounts.FirstOrDefault(a => a.Username.Equals(user, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Label = label;
            existing.Password = pass;
            existing.LastStatus = "Updated";
            ShowToast("Account Updated", $"Credentials for '{label}' ({user}) updated.", "wifi_connected.png");
        }
        else
        {
            _config.Accounts.Add(new UserCredential
            {
                Label = label,
                Username = user,
                Password = pass,
                IsActive = isFirstAccount,
                LastStatus = "Standby"
            });
            ShowToast("Account Added", $"Account '{label}' configured successfully.", "wifi_connected.png");
        }

        ConfigService.SaveConfig(_config);
        RefreshAccountsList();
        CloseAccountModal();

        // If this was first setup and user isn't connected yet, start login attempt right away!
        if (isFirstAccount && !_stats.IsConnected)
        {
            _ = _authService.LoginAsync();
        }
    }

    private void OnCardRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindParent<System.Windows.Controls.Button>(dep) != null)
        {
            return;
        }

        if (sender is FrameworkElement elem && elem.Tag is string accId)
        {
            var acc = _config.Accounts.FirstOrDefault(a => a.Id == accId);
            if (acc != null)
            {
                OpenAccountModal(acc);
            }
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private async void OnSwitchAccountClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string accId)
        {
            var targetAcc = _config.Accounts.FirstOrDefault(a => a.Id == accId);
            if (targetAcc == null) return;

            if (targetAcc.IsActive && _stats.IsConnected)
            {
                ShowToast("Already Active", $"{targetAcc.Label} is already your active connected account.", "wifi_connected.png");
                return;
            }

            btn.IsEnabled = false;
            btn.Content = "⏳...";

            try
            {
                _config.SetActiveAccount(accId);
                ConfigService.SaveConfig(_config);
                RefreshAccountsList();

                var active = _config.GetActiveAccount();
                ConfigService.Log("INFO", $"Switching active credentials to '{active.Label}' ({active.Username}). Handing over session...");

                if (_stats.IsConnected)
                {
                    ShowToast("Switching Session", $"Logging out from old ID and authenticating as {active.Label}...", "wifi_connected.png");
                    await _authService.LogoutAsync();
                    await Task.Delay(400);
                }

                var (success, msg) = await _authService.LoginAsync();
                ShowToast(success ? "Account Switched" : "Login Failed", 
                          success ? $"Connected as {active.Label} ({active.Username})!" : msg, 
                          success ? "wifi_connected.png" : "wifi_error.png");

                UpdateLiveStats();
            }
            finally
            {
                btn.IsEnabled = true;
                RefreshAccountsList();
            }
        }
    }

    private void OnDeleteAccountClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string accId)
        {
            if (_config.Accounts.Count <= 1)
            {
                WpfMessageBox.Show("You cannot delete the only remaining account in your pool.", "TMU Tarang Connect", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var toRemove = _config.Accounts.FirstOrDefault(a => a.Id == accId);
            if (toRemove != null)
            {
                var res = WpfMessageBox.Show($"Remove account '{toRemove.Label}' ({toRemove.Username}) from your pool?", "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    if (_modalEditingAccountId == toRemove.Id)
                    {
                        CloseAccountModal();
                    }

                    _config.Accounts.Remove(toRemove);
                    _config.EnsureAccounts();
                    ConfigService.SaveConfig(_config);
                    RefreshAccountsList();
                    ShowToast("Account Removed", $"Removed '{toRemove.Label}' from pool.", "wifi_connected.png");
                }
            }
        }
    }

    private async void OnCheckConnectionClicked(object sender, RoutedEventArgs e)
    {
        BtnCheck.IsEnabled = false;
        BtnCheck.Content = "⏳ Testing...";
        TxtStatus.Text = "Checking latency to server...";

        try
        {
            await _authService.RunSingleCheckAsync(isManual: true);
            UpdateLiveStats();
        }
        finally
        {
            BtnCheck.IsEnabled = true;
            BtnCheck.Content = "Check Connection";
            UpdateLiveStats();
        }
    }

    private async void OnConnectNowClicked(object sender, RoutedEventArgs e)
    {
        BtnConnect.IsEnabled = false;
        BtnConnect.Content = "⏳ Connecting...";
        TxtStatus.Text = "Verifying network...";

        try
        {
            var (success, msg) = await _authService.LoginAsync();
            UpdateLiveStats();
            ShowToast(success ? "Connection Verified" : "Authentication Failed", msg, success ? "wifi_connected.png" : "wifi_error.png");
        }
        finally
        {
            BtnConnect.IsEnabled = true;
            UpdateLiveStats();
        }
    }

    private async void OnLogoutClicked(object sender, RoutedEventArgs e)
    {
        var result = WpfMessageBox.Show(
            "Are you sure you want to log out from the FortiGate network portal? Auto-login will be paused until you reconnect.",
            "Confirm FortiGate Logout",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.Yes)
        {
            await _authService.LogoutAsync();
            UpdateLiveStats();
        }
    }

    private async void OnSpeedTestClicked(object sender, RoutedEventArgs e)
    {
        BtnSpeedTest.IsEnabled = false;
        BtnRunSpeedTestDetailed.IsEnabled = false;
        BtnSpeedTest.Content = "⏳ Testing...";
        BtnRunSpeedTestDetailed.Content = "⏳ Testing in progress...";
        TxtSpeedTestStatus.Text = "Starting benchmark...";

        try
        {
            var (success, result, err) = await SpeedTestService.RunFullSpeedTestAsync(status =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtSpeedTestStatus.Text = status;
                });
            });

            if (success)
            {
                _stats.DownloadSpeedMbps = result.DownloadMbps;
                _stats.UploadSpeedMbps = result.UploadMbps;
                _stats.LatencyMs = result.PingMs;

                TxtSpeed.Text = _stats.SpeedDisplay;
                TxtSpeedDl.Text = $"{result.DownloadMbps:F1} Mbps";
                TxtSpeedUl.Text = $"{result.UploadMbps:F1} Mbps";
                TxtSpeedPing.Text = $"{result.PingMs} ms";
                TxtSpeedTestStatus.Text = "Benchmark Completed";

                SpeedHistoryList.ItemsSource = null;
                SpeedHistoryList.ItemsSource = SpeedTestService.History;

                ConfigService.Log("OK", $"Speed benchmark: ↓ {result.DownloadMbps:F1} Mbps, ↑ {result.UploadMbps:F1} Mbps ({result.PingMs} ms).");
                ShowToast("Speed Test Complete", $"↓ {result.DownloadMbps:F1} Mbps  •  ↑ {result.UploadMbps:F1} Mbps", "wifi_connected.png");
            }
            else
            {
                TxtSpeedTestStatus.Text = $"Failed: {err}";
                ShowToast("Speed Test Failed", $"Error: {err}", "wifi_error.png");
            }
        }
        catch (Exception ex)
        {
            TxtSpeedTestStatus.Text = "Error during test";
            ShowToast("Speed Test Error", ex.Message, "wifi_error.png");
        }
        finally
        {
            BtnSpeedTest.IsEnabled = true;
            BtnRunSpeedTestDetailed.IsEnabled = true;
            BtnSpeedTest.Content = "⚡ Speed Test";
            BtnRunSpeedTestDetailed.Content = "⚡ Start Full Speed Test (Download + Upload)";
            UpdateLiveStats();
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        _config.AutoRotateOnLimit = ChkAutoRotate.IsChecked == true;
        _config.ShowNotifications = ChkNotifications.IsChecked == true;
        _config.AutoStartOnBoot = ChkStartup.IsChecked == true;

        if (int.TryParse(TxtInterval.Text.Trim(), out var interval) && interval >= 5)
            _config.CheckIntervalSeconds = interval;

        if (int.TryParse(TxtMaxRetries.Text.Trim(), out var retries) && retries >= 1)
            _config.MaxRetries = retries;

        if (int.TryParse(TxtRetryDelay.Text.Trim(), out var delay) && delay >= 2)
            _config.RetryDelaySeconds = delay;

        if (!string.IsNullOrWhiteSpace(TxtTestUrl.Text))
            _config.TestUrl = TxtTestUrl.Text.Trim();

        ConfigService.SaveConfig(_config);
        StartupService.SetAutoStart(_config.AutoStartOnBoot);
        RefreshAccountsList();

        ShowToast("Settings Saved", "Configuration applied and auto-login updated.", "wifi_connected.png");
    }

    private void OnTestToastClicked(object sender, RoutedEventArgs e)
    {
        ShowToast("TMU Tarang Notification", "Floating notifications are working with zero glitches!", "wifi_connected.png");
    }

    private void ShowToast(string title, string msg, string icon)
    {
        Dispatcher.Invoke(() =>
        {
            var toast = new ToastWindow(title, msg, icon, () =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
            toast.Show();
        });
    }

    // ──────────────────────────────────────────
    // Campus Wi-Fi Management
    // ──────────────────────────────────────────
    private async Task RefreshWifiStatusAsync()
    {
        _wifiStatus = await WifiManagerService.GetStatusAsync();

        Dispatcher.Invoke(() =>
        {
            if (_wifiStatus == null) return;

            if (!_wifiStatus.HasAdapter)
            {
                TxtWifiAdapterName.Text = "Ethernet / LAN Active";
                TxtWifiStateBadge.Text = "LAN MODE";
                TxtWifiStateBadge.Foreground = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#38BDF8"));
                BadgeWifiState.Background = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#0C243B"));
                TxtWifiSummary.Text = "Connected via wired Ethernet cable. Auto-login handles LAN connections automatically. No wireless card active.";
                BtnToggleWifi.IsEnabled = false;
                BtnQuickConnectTmu.Content = "⚡ Authenticate FortiGate Over LAN";
            }
            else
            {
                TxtWifiAdapterName.Text = _wifiStatus.InterfaceName;
                BtnToggleWifi.IsEnabled = true;

                if (_wifiStatus.IsConnected)
                {
                    TxtWifiStateBadge.Text = "CONNECTED";
                    TxtWifiStateBadge.Foreground = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#10B981"));
                    BadgeWifiState.Background = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#133626"));
                }
                else if (!_wifiStatus.IsRadioOn)
                {
                    TxtWifiStateBadge.Text = "RADIO OFF";
                    TxtWifiStateBadge.Foreground = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#94A3B8"));
                    BadgeWifiState.Background = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#1E2536"));
                }
                else
                {
                    TxtWifiStateBadge.Text = "DISCONNECTED";
                    TxtWifiStateBadge.Foreground = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#F59E0B"));
                    BadgeWifiState.Background = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#36250B"));
                }

                TxtWifiSummary.Text = _wifiStatus.StatusSummary;
                BtnQuickConnectTmu.Content = "⚡ 1-Click Connect to TMU-Tarang & Login";
            }
        });
    }

    private async void OnScanWifiClicked(object sender, RoutedEventArgs e)
    {
        await RefreshWifiStatusAsync();
        var networks = await WifiManagerService.ScanNetworksAsync();
        WifiNetworksList.ItemsSource = networks;

        if (networks.Count == 0 && _wifiStatus?.HasAdapter == false)
        {
            ShowToast("LAN Active", "Currently running on wired Ethernet. No Wi-Fi networks in range.", "wifi_info.png");
        }
    }

    private async void OnToggleWifiClicked(object sender, RoutedEventArgs e)
    {
        if (_wifiStatus == null || !_wifiStatus.HasAdapter) return;

        bool targetState = !_wifiStatus.IsRadioOn;
        var (success, msg) = await WifiManagerService.ToggleWifiAsync(targetState, _wifiStatus.InterfaceName);
        ShowToast(success ? "Wi-Fi Updated" : "Wi-Fi Toggle Failed", msg, success ? "wifi_connected.png" : "wifi_error.png");
        await RefreshWifiStatusAsync();
    }

    private async void OnQuickConnectTmuClicked(object sender, RoutedEventArgs e)
    {
        BtnQuickConnectTmu.IsEnabled = false;
        var originalText = BtnQuickConnectTmu.Content;
        BtnQuickConnectTmu.Content = "⏳ Processing...";

        try
        {
            if (_wifiStatus?.HasAdapter == true)
            {
                BtnQuickConnectTmu.Content = "⏳ Connecting to TMU Wi-Fi...";
                var (connected, msg) = await WifiManagerService.ConnectSsidAsync("TMU-Tarang");
                if (!connected)
                {
                    (connected, msg) = await WifiManagerService.ConnectSsidAsync("TMU-TARANG");
                }
                await Task.Delay(2000);
            }

            BtnQuickConnectTmu.Content = "⏳ Authenticating...";
            var (authSuccess, authMsg) = await _authService.LoginAsync();

            ShowToast(
                authSuccess ? "Connection Verified" : "Authentication Result",
                authMsg,
                authSuccess ? "wifi_connected.png" : "wifi_error.png"
            );

            await RefreshWifiStatusAsync();
            UpdateLiveStats();
        }
        finally
        {
            BtnQuickConnectTmu.IsEnabled = true;
            BtnQuickConnectTmu.Content = originalText;
        }
    }

    private async void OnConnectSpecificSsidClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string ssid && !string.IsNullOrWhiteSpace(ssid))
        {
            ShowToast("Connecting Wi-Fi", $"Associating with {ssid}...", "wifi_connected.png");
            var (connected, msg) = await WifiManagerService.ConnectSsidAsync(ssid);
            if (connected)
            {
                await Task.Delay(2000);
                await _authService.LoginAsync();
            }
            else
            {
                ShowToast("Wi-Fi Connection Failed", msg, "wifi_error.png");
            }
            await RefreshWifiStatusAsync();
        }
    }

    // ──────────────────────────────────────────
    // About & Credits Actions
    // ──────────────────────────────────────────
    private void OnOpenGithubClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/rohitj921/TMU-Tarang-Connect-") { UseShellExecute = true });
        }
        catch { }
    }

    private void OnSendEmailClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("mailto:rohitjainofficial@gmail.com") { UseShellExecute = true });
        }
        catch { }
    }

    private void OnCopyEmailClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText("rohitjainofficial@gmail.com");
            ShowToast("Copied to Clipboard", "rohitjainofficial@gmail.com copied!", "wifi_connected.png");
        }
        catch { }
    }

    // ──────────────────────────────────────────
    // Activity Log & App Exit
    // ──────────────────────────────────────────
    private void RefreshLogFeed()
    {
        try
        {
            if (File.Exists(ConfigService.LogPath))
            {
                var lines = File.ReadAllLines(ConfigService.LogPath);
                var entries = new List<LogEntry>();
                int start = Math.Max(0, lines.Length - 40);
                for (int i = lines.Length - 1; i >= start; i--)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        entries.Add(LogEntry.Parse(lines[i]));
                    }
                }
                FeedList.ItemsSource = entries;
            }
        }
        catch { }
    }

    private void OnRefreshLogClicked(object sender, RoutedEventArgs e) => RefreshLogFeed();

    private void OnOpenLogInNotepadClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(ConfigService.LogPath))
            {
                File.WriteAllText(ConfigService.LogPath, "");
            }
            Process.Start("notepad.exe", $"\"{ConfigService.LogPath}\"");
        }
        catch { }
    }

    private void OnExitApplicationClicked(object sender, RoutedEventArgs e)
    {
        var res = WpfMessageBox.Show(
            "Do you want to completely stop TMU Tarang Connect and exit?",
            "Exit TMU Tarang Connect",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (res == MessageBoxResult.Yes)
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}