using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TMUTarangConnect.Services;

public class WifiNetworkInfo
{
    public string Ssid { get; set; } = string.Empty;
    public string Signal { get; set; } = "100%";
    public string Auth { get; set; } = "Open";
    public bool IsCampusNetwork { get; set; }
}

public class WifiAdapterStatus
{
    public bool HasAdapter { get; set; }
    public string InterfaceName { get; set; } = "Wi-Fi";
    public bool IsConnected { get; set; }
    public bool IsRadioOn { get; set; } = true;
    public string CurrentSsid { get; set; } = string.Empty;
    public string Signal { get; set; } = "--";
    public string StatusSummary { get; set; } = "Checking...";
    public bool IsLanActive { get; set; }
}

public static class WifiManagerService
{
    private static readonly string[] CampusKeywords = new[] { "TMU", "TARANG", "TMU-TARANG", "TMU TARANG", "TMU_STUDENT", "TMU-HOSTEL" };

    public static async Task<WifiAdapterStatus> GetStatusAsync()
    {
        return await Task.Run(() =>
        {
            var status = new WifiAdapterStatus();
            try
            {
                var output = RunNetsh("wlan show interfaces");

                if (string.IsNullOrWhiteSpace(output) ||
                    output.Contains("is not running", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("no wireless interface", StringComparison.OrdinalIgnoreCase))
                {
                    status.HasAdapter = false;
                    status.IsLanActive = true;
                    status.StatusSummary = "LAN / Ethernet Active (No Wireless Adapter)";
                    return status;
                }

                status.HasAdapter = true;

                // Parse Interface Name
                var nameMatch = Regex.Match(output, @"Name\s*:\s*(.+)$", RegexOptions.Multiline);
                if (nameMatch.Success)
                    status.InterfaceName = nameMatch.Groups[1].Value.Trim();

                // Parse State
                var stateMatch = Regex.Match(output, @"State\s*:\s*(.+)$", RegexOptions.Multiline);
                if (stateMatch.Success)
                {
                    var state = stateMatch.Groups[1].Value.Trim();
                    status.IsConnected = state.Equals("connected", StringComparison.OrdinalIgnoreCase);
                }

                // Parse Radio State
                var radioMatch = Regex.Match(output, @"Radio status\s*:\s*(.+)$", RegexOptions.Multiline);
                if (radioMatch.Success)
                {
                    status.IsRadioOn = !radioMatch.Groups[1].Value.Contains("Hardware Off", StringComparison.OrdinalIgnoreCase)
                                    && !radioMatch.Groups[1].Value.Contains("Software Off", StringComparison.OrdinalIgnoreCase);
                }

                // Parse SSID
                var ssidMatch = Regex.Match(output, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
                if (ssidMatch.Success)
                    status.CurrentSsid = ssidMatch.Groups[1].Value.Trim();

                // Parse Signal
                var signalMatch = Regex.Match(output, @"Signal\s*:\s*(.+)$", RegexOptions.Multiline);
                if (signalMatch.Success)
                    status.Signal = signalMatch.Groups[1].Value.Trim();

                if (status.IsConnected)
                    status.StatusSummary = $"Connected to {status.CurrentSsid} ({status.Signal})";
                else if (!status.IsRadioOn)
                    status.StatusSummary = "Wi-Fi is Disabled (Radio Off)";
                else
                    status.StatusSummary = "Wi-Fi Disconnected (Ready to connect)";
            }
            catch (Exception ex)
            {
                status.StatusSummary = "Error checking Wi-Fi status: " + ex.Message;
            }

            return status;
        });
    }

    public static async Task<List<WifiNetworkInfo>> ScanNetworksAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<WifiNetworkInfo>();
            try
            {
                var output = RunNetsh("wlan show networks mode=bssid");
                if (string.IsNullOrWhiteSpace(output))
                    return list;

                var blocks = output.Split(new[] { "SSID " }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var block in blocks)
                {
                    var lines = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length == 0) continue;

                    var firstLine = lines[0];
                    var colonIdx = firstLine.IndexOf(':');
                    if (colonIdx < 0) continue;

                    var ssid = firstLine.Substring(colonIdx + 1).Trim();
                    if (string.IsNullOrWhiteSpace(ssid)) continue;

                    string signal = "80%";
                    string auth = "Open";

                    foreach (var line in lines)
                    {
                        if (line.Contains("Signal", StringComparison.OrdinalIgnoreCase))
                        {
                            var m = Regex.Match(line, @":\s*(.+)$");
                            if (m.Success) signal = m.Groups[1].Value.Trim();
                        }
                        if (line.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
                        {
                            var m = Regex.Match(line, @":\s*(.+)$");
                            if (m.Success) auth = m.Groups[1].Value.Trim();
                        }
                    }

                    bool isCampus = CampusKeywords.Any(k => ssid.Contains(k, StringComparison.OrdinalIgnoreCase));

                    list.Add(new WifiNetworkInfo
                    {
                        Ssid = ssid,
                        Signal = signal,
                        Auth = auth,
                        IsCampusNetwork = isCampus
                    });
                }
            }
            catch { }

            // Sort campus networks to the top
            return list.OrderByDescending(n => n.IsCampusNetwork).ThenByDescending(n => n.Signal).ToList();
        });
    }

    public static async Task<(bool success, string message)> ConnectSsidAsync(string ssid)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Ensure profile exists for open Wi-Fi
                EnsureOpenProfileExists(ssid);

                var output = RunNetsh($"wlan connect name=\"{ssid}\"");
                ConfigService.Log("INFO", $"netsh connect result: {output.Trim()}");

                if (output.Contains("Connection request was completed successfully", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("completed successfully", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, $"Connecting to {ssid}...");
                }

                return (false, output.Trim());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }

    public static async Task<(bool success, string message)> ToggleWifiAsync(bool enable, string interfaceName = "Wi-Fi")
    {
        return await Task.Run(() =>
        {
            try
            {
                var state = enable ? "enabled" : "disabled";
                var output = RunNetsh($"interface set interface name=\"{interfaceName}\" admin={state}");
                if (string.IsNullOrWhiteSpace(output))
                {
                    return (true, $"Wi-Fi adapter {state} successfully.");
                }
                return (false, output.Trim());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }

    private static void EnsureOpenProfileExists(string ssid)
    {
        try
        {
            var profilesOutput = RunNetsh("wlan show profiles");
            if (profilesOutput.Contains($"\"{ssid}\"", StringComparison.OrdinalIgnoreCase) ||
                profilesOutput.Contains($": {ssid}", StringComparison.OrdinalIgnoreCase))
            {
                return; // Profile already exists
            }

            var xml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <name>{ssid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>manual</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>open</authentication>
                <encryption>none</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
        </security>
    </MSM>
</WLANProfile>";

            var tempXml = Path.Combine(Path.GetTempPath(), $"wifi_profile_{Guid.NewGuid():N}.xml");
            File.WriteAllText(tempXml, xml);
            RunNetsh($"wlan add profile filename=\"{tempXml}\"");
            try { File.Delete(tempXml); } catch { }
        }
        catch { }
    }

    private static string RunNetsh(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return string.Empty;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(4000);
        return output;
    }
}
