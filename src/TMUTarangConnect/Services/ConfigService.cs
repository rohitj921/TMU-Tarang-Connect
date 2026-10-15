using System;
using System.IO;
using System.Text.Json;
using TMUTarangConnect.Models;

namespace TMUTarangConnect.Services;

public static class ConfigService
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TMUTarangConnect"
    );

    public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
    public static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmu_wifi_autologin.log");

    public static AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config != null)
                {
                    config.EnsureAccounts();
                    return config;
                }
            }
        }
        catch { }

        var fallback = new AppConfig();
        fallback.EnsureAccounts();
        SaveConfig(fallback);
        return fallback;
    }

    public static void SaveConfig(AppConfig config)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }

    public static void Log(string level, string message)
    {
        try
        {
            // Auto-rotate if > 1MB
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1048576)
            {
                var lines = File.ReadAllLines(LogPath);
                if (lines.Length > 200)
                {
                    var tail = lines[^200..];
                    File.WriteAllLines(LogPath, tail);
                }
            }

            var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, entry);
        }
        catch { }
    }
}
