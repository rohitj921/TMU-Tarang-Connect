using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TMUTarangConnect.Services;

public class BandwidthRecord
{
    public string DateKey { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
    public string MonthKey { get; set; } = DateTime.Now.ToString("yyyy-MM");
    public long TodayBytesRx { get; set; }
    public long TodayBytesTx { get; set; }
    public long MonthBytesRx { get; set; }
    public long MonthBytesTx { get; set; }
    public long TotalBytesRx { get; set; }
    public long TotalBytesTx { get; set; }
}

public class BandwidthSnapshot
{
    public string TodayDisplay { get; set; } = "0 MB";
    public string TodayBreakdown { get; set; } = "↓ 0 MB  ↑ 0 MB";
    public string MonthDisplay { get; set; } = "0 MB";
    public string MonthBreakdown { get; set; } = "↓ 0 MB  ↑ 0 MB";
    public string SessionDisplay { get; set; } = "0 MB";
    public string SessionBreakdown { get; set; } = "↓ 0 MB  ↑ 0 MB";
}

public static class BandwidthTrackerService
{
    private static readonly string StoragePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bandwidth_usage.json");
    private static BandwidthRecord _record = new BandwidthRecord();
    private static long _lastTotalRx = -1;
    private static long _lastTotalTx = -1;
    private static long _sessionRx = 0;
    private static long _sessionTx = 0;
    private static readonly object _lock = new object();
    private static System.Threading.Timer? _pollTimer;

    public static event Action? UsageUpdated;

    public static void Initialize()
    {
        LoadRecord();

        // Sample every 4 seconds
        _pollTimer = new System.Threading.Timer(_ => SampleUsage(), null, 1000, 4000);
    }

    private static void SampleUsage()
    {
        try
        {
            long currentRx = 0;
            long currentTx = 0;

            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var nic in interfaces)
            {
                if (nic.OperationalStatus == OperationalStatus.Up &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                     nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                {
                    try
                    {
                        var stats = nic.GetIPStatistics();
                        currentRx += stats.BytesReceived;
                        currentTx += stats.BytesSent;
                    }
                    catch { }
                }
            }

            lock (_lock)
            {
                if (_lastTotalRx >= 0 && _lastTotalTx >= 0)
                {
                    long deltaRx = currentRx - _lastTotalRx;
                    long deltaTx = currentTx - _lastTotalTx;

                    // Filter out negative or absurd anomalies (e.g. adapter reboot)
                    if (deltaRx >= 0 && deltaRx < 500_000_000 && deltaTx >= 0 && deltaTx < 500_000_000)
                    {
                        string todayKey = DateTime.Now.ToString("yyyy-MM-dd");
                        string monthKey = DateTime.Now.ToString("yyyy-MM");

                        // Daily roll
                        if (_record.DateKey != todayKey)
                        {
                            _record.DateKey = todayKey;
                            _record.TodayBytesRx = 0;
                            _record.TodayBytesTx = 0;
                        }

                        // Monthly roll
                        if (_record.MonthKey != monthKey)
                        {
                            _record.MonthKey = monthKey;
                            _record.MonthBytesRx = 0;
                            _record.MonthBytesTx = 0;
                        }

                        _record.TodayBytesRx += deltaRx;
                        _record.TodayBytesTx += deltaTx;
                        _record.MonthBytesRx += deltaRx;
                        _record.MonthBytesTx += deltaTx;
                        _record.TotalBytesRx += deltaRx;
                        _record.TotalBytesTx += deltaTx;

                        _sessionRx += deltaRx;
                        _sessionTx += deltaTx;

                        SaveRecord();
                    }
                }

                _lastTotalRx = currentRx;
                _lastTotalTx = currentTx;
            }

            UsageUpdated?.Invoke();
        }
        catch { }
    }

    public static BandwidthSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            long todayTotal = _record.TodayBytesRx + _record.TodayBytesTx;
            long monthTotal = _record.MonthBytesRx + _record.MonthBytesTx;
            long sessionTotal = _sessionRx + _sessionTx;

            return new BandwidthSnapshot
            {
                TodayDisplay = FormatBytes(todayTotal),
                TodayBreakdown = $"↓ {FormatBytes(_record.TodayBytesRx)}  •  ↑ {FormatBytes(_record.TodayBytesTx)}",
                MonthDisplay = FormatBytes(monthTotal),
                MonthBreakdown = $"↓ {FormatBytes(_record.MonthBytesRx)}  •  ↑ {FormatBytes(_record.MonthBytesTx)}",
                SessionDisplay = FormatBytes(sessionTotal),
                SessionBreakdown = $"↓ {FormatBytes(_sessionRx)}  •  ↑ {FormatBytes(_sessionTx)}"
            };
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 MB";
        double kb = bytes / 1024.0;
        double mb = kb / 1024.0;
        double gb = mb / 1024.0;

        if (gb >= 1.0)
            return $"{gb:F2} GB";
        if (mb >= 1.0)
            return $"{mb:F1} MB";
        return $"{kb:F0} KB";
    }

    private static void LoadRecord()
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                var json = File.ReadAllText(StoragePath);
                var loaded = JsonSerializer.Deserialize<BandwidthRecord>(json);
                if (loaded != null)
                {
                    _record = loaded;
                    return;
                }
            }
        }
        catch { }

        _record = new BandwidthRecord();
    }

    private static void SaveRecord()
    {
        try
        {
            var json = JsonSerializer.Serialize(_record, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StoragePath, json);
        }
        catch { }
    }
}
