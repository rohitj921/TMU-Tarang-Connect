using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TMUTarangConnect.Models;

public class ConnectionStats : INotifyPropertyChanged
{
    private bool _isConnected;
    private bool _isPaused;
    private DateTime? _connectedSince;
    private DateTime? _lastOutageTime;
    private TimeSpan _lastOutageDuration = TimeSpan.Zero;
    private int _autoLoginsToday;
    private string _currentStatusText = "Initializing...";
    private string _assignedIp = "Detecting...";
    private string _gateway = "172.14.0.1:1000";
    private int _latencyMs = -1;
    private double _downloadSpeedMbps = -1;
    private double _uploadSpeedMbps = -1;
    private string _activeAccountDisplay = "Primary Account";

    public string ActiveAccountDisplay
    {
        get => _activeAccountDisplay;
        set => SetField(ref _activeAccountDisplay, value);
    }

    public double DownloadSpeedMbps
    {
        get => _downloadSpeedMbps;
        set
        {
            SetField(ref _downloadSpeedMbps, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpeedDisplay)));
        }
    }

    public double UploadSpeedMbps
    {
        get => _uploadSpeedMbps;
        set
        {
            SetField(ref _uploadSpeedMbps, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpeedDisplay)));
        }
    }

    public string SpeedDisplay
    {
        get
        {
            if (_downloadSpeedMbps > 0 && _uploadSpeedMbps > 0)
                return $"↓ {_downloadSpeedMbps:F0} / ↑ {_uploadSpeedMbps:F0}";
            if (_downloadSpeedMbps > 0)
                return $"↓ {_downloadSpeedMbps:F1} Mbps";
            return "--";
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetField(ref _isConnected, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        set => SetField(ref _isPaused, value);
    }

    public DateTime? ConnectedSince
    {
        get => _connectedSince;
        set => SetField(ref _connectedSince, value);
    }

    public DateTime? LastOutageTime
    {
        get => _lastOutageTime;
        set => SetField(ref _lastOutageTime, value);
    }

    public TimeSpan LastOutageDuration
    {
        get => _lastOutageDuration;
        set => SetField(ref _lastOutageDuration, value);
    }

    public int AutoLoginsToday
    {
        get => _autoLoginsToday;
        set => SetField(ref _autoLoginsToday, value);
    }

    public string CurrentStatusText
    {
        get => _currentStatusText;
        set => SetField(ref _currentStatusText, value);
    }

    public string AssignedIp
    {
        get => _assignedIp;
        set => SetField(ref _assignedIp, value);
    }

    public string Gateway
    {
        get => _gateway;
        set => SetField(ref _gateway, value);
    }

    public int LatencyMs
    {
        get => _latencyMs;
        set => SetField(ref _latencyMs, value);
    }

    public string LatencyDisplay => _latencyMs >= 0 ? $"{_latencyMs} ms" : "--";

    public string UptimeDisplay
    {
        get
        {
            if (!_isConnected || !_connectedSince.HasValue)
                return _isPaused ? "Paused" : "Offline";

            var elapsed = DateTime.Now - _connectedSince.Value;
            if (elapsed.TotalDays >= 1)
                return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h {elapsed.Minutes}m";
            if (elapsed.TotalHours >= 1)
                return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
            if (elapsed.TotalMinutes >= 1)
                return $"{elapsed.Minutes}m {elapsed.Seconds}s";

            return $"{elapsed.Seconds}s";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyUptimeTick()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UptimeDisplay)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LatencyDisplay)));
    }

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
