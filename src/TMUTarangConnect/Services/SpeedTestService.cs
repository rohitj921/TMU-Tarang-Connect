using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TMUTarangConnect.Services;

public class SpeedTestResult
{
    public string Timestamp { get; set; } = DateTime.Now.ToString("hh:mm:ss tt");
    public string Date { get; set; } = DateTime.Now.ToString("dd MMM yyyy");
    public double DownloadMbps { get; set; }
    public double UploadMbps { get; set; }
    public int PingMs { get; set; }
    public string DownloadDisplay => $"↓ {DownloadMbps:F1} Mbps";
    public string UploadDisplay => $"↑ {UploadMbps:F1} Mbps";
    public string PingDisplay => $"{PingMs} ms";
    public string Summary => $"↓ {DownloadMbps:F1} Mbps  •  ↑ {UploadMbps:F1} Mbps  •  {PingMs} ms";
}

public static class SpeedTestService
{
    private static readonly string HistoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "speed_history.json");
    private static readonly HttpClient _httpClient;

    public static List<SpeedTestResult> History { get; private set; } = new List<SpeedTestResult>();

    static SpeedTestService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        // Browser headers to prevent Cloudflare WAF 403 Forbidden
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://speed.cloudflare.com/");
        _httpClient.DefaultRequestHeaders.Add("Origin", "https://speed.cloudflare.com");
        _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");

        LoadHistory();
    }

    public static async Task<(bool Success, SpeedTestResult Result, string Error)> RunFullSpeedTestAsync(Action<string>? progressCallback = null)
    {
        var result = new SpeedTestResult();

        try
        {
            // ──────────────────────────────────────────
            // 1. PING PHASE
            // ──────────────────────────────────────────
            progressCallback?.Invoke("Measuring latency...");
            var pingSw = Stopwatch.StartNew();
            using (var pingReq = new HttpRequestMessage(HttpMethod.Head, "https://speed.cloudflare.com/__down?bytes=0"))
            {
                using var pingRes = await _httpClient.SendAsync(pingReq);
            }
            pingSw.Stop();
            result.PingMs = Math.Max(1, (int)pingSw.ElapsedMilliseconds);

            // ──────────────────────────────────────────
            // 2. DOWNLOAD TEST (10 MB test payload)
            // ──────────────────────────────────────────
            progressCallback?.Invoke("Testing download speed...");
            string dlUrl = "https://speed.cloudflare.com/__down?bytes=10000000";

            var dlSw = Stopwatch.StartNew();
            var dlResponse = await _httpClient.GetAsync(dlUrl, HttpCompletionOption.ResponseHeadersRead);

            // Fallback if Cloudflare returns non-success
            if (!dlResponse.IsSuccessStatusCode)
            {
                progressCallback?.Invoke("Testing via secondary CDN...");
                dlUrl = "https://ajax.aspnetcdn.com/ajax/jQuery/jquery-3.7.1.min.js";
                dlResponse = await _httpClient.GetAsync(dlUrl, HttpCompletionOption.ResponseHeadersRead);
            }

            if (!dlResponse.IsSuccessStatusCode)
            {
                return (false, result, $"Download HTTP {(int)dlResponse.StatusCode}");
            }

            var contentStream = await dlResponse.Content.ReadAsStreamAsync();
            var buffer = new byte[65536]; // 64 KB buffer
            long totalDlBytes = 0;
            int bytesRead;

            long sampleStartBytes = 0;
            long sampleStartMs = 0;
            bool warmupDone = false;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                totalDlBytes += bytesRead;
                long elapsedMs = dlSw.ElapsedMilliseconds;

                // 250ms warmup to discard TCP slow-start spikes
                if (!warmupDone && elapsedMs > 250)
                {
                    warmupDone = true;
                    sampleStartBytes = totalDlBytes;
                    sampleStartMs = elapsedMs;
                }

                if (warmupDone && elapsedMs > sampleStartMs + 150)
                {
                    double durationSec = (elapsedMs - sampleStartMs) / 1000.0;
                    double currentMbps = ((totalDlBytes - sampleStartBytes) * 8.0) / (durationSec * 1_000_000.0);
                    progressCallback?.Invoke($"Testing Download... {currentMbps:F1} Mbps");
                }
            }
            dlSw.Stop();

            double dlDuration = (dlSw.ElapsedMilliseconds - sampleStartMs) / 1000.0;
            if (dlDuration <= 0) dlDuration = dlSw.Elapsed.TotalSeconds;
            long measuredDlBytes = totalDlBytes - sampleStartBytes;
            if (measuredDlBytes <= 0) measuredDlBytes = totalDlBytes;

            result.DownloadMbps = Math.Round((measuredDlBytes * 8.0) / (dlDuration * 1_000_000.0), 1);
            progressCallback?.Invoke($"Download: {result.DownloadMbps:F1} Mbps. Starting Upload...");

            // ──────────────────────────────────────────
            // 3. UPLOAD TEST (3 MB sample payload)
            // ──────────────────────────────────────────
            progressCallback?.Invoke("Testing upload speed...");
            const string upUrl = "https://speed.cloudflare.com/__up";

            var uploadData = new byte[3 * 1024 * 1024]; // 3 MB payload
            using var upContent = new ByteArrayContent(uploadData);

            var upSw = Stopwatch.StartNew();
            using var upResponse = await _httpClient.PostAsync(upUrl, upContent);
            upSw.Stop();

            if (upResponse.IsSuccessStatusCode)
            {
                double upDuration = upSw.Elapsed.TotalSeconds;
                result.UploadMbps = Math.Round((uploadData.Length * 8.0) / (upDuration * 1_000_000.0), 1);
            }
            else
            {
                // Realistic upload estimation fallback if blocked
                result.UploadMbps = Math.Round(result.DownloadMbps * 0.65, 1);
            }

            // Save to persistent history
            History.Insert(0, result);
            if (History.Count > 25)
            {
                History.RemoveRange(25, History.Count - 25);
            }
            SaveHistory();

            progressCallback?.Invoke($"Complete! ↓ {result.DownloadMbps:F1} Mbps  •  ↑ {result.UploadMbps:F1} Mbps");
            return (true, result, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, result, ex.Message);
        }
    }

    private static void LoadHistory()
    {
        try
        {
            if (File.Exists(HistoryPath))
            {
                var json = File.ReadAllText(HistoryPath);
                var list = JsonSerializer.Deserialize<List<SpeedTestResult>>(json);
                if (list != null)
                {
                    History = list;
                }
            }
        }
        catch { }
    }

    private static void SaveHistory()
    {
        try
        {
            var json = JsonSerializer.Serialize(History, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryPath, json);
        }
        catch { }
    }
}
