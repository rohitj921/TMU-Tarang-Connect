namespace TMUTarangConnect.Models;

public class LogEntry
{
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = "";
    public string BadgeColor { get; set; } = "#38BDF8";
    public string BadgeBg { get; set; } = "#0D263B";

    public static LogEntry Parse(string rawLine)
    {
        // Format: "2026-10-14 17:00:58 [OK] Login POST sent successfully."
        var entry = new LogEntry();
        if (string.IsNullOrWhiteSpace(rawLine)) return entry;

        var timeEnd = rawLine.IndexOf('[');
        if (timeEnd > 0)
        {
            var timePart = rawLine[..timeEnd].Trim();
            // Show only HH:mm:ss for clean look
            if (timePart.Length >= 19)
            {
                entry.Timestamp = timePart[11..19];
            }
            else
            {
                entry.Timestamp = timePart;
            }

            var levelEnd = rawLine.IndexOf(']', timeEnd);
            if (levelEnd > timeEnd)
            {
                entry.Level = rawLine[(timeEnd + 1)..levelEnd].Trim();
                entry.Message = rawLine[(levelEnd + 1)..].Trim();
            }
            else
            {
                entry.Message = rawLine[timeEnd..].Trim();
            }
        }
        else
        {
            entry.Message = rawLine;
        }

        switch (entry.Level.ToUpperInvariant())
        {
            case "OK":
                entry.BadgeColor = "#10B981";
                entry.BadgeBg = "#06291C";
                break;
            case "WARN":
                entry.BadgeColor = "#F59E0B";
                entry.BadgeBg = "#301F08";
                break;
            case "ERROR":
                entry.BadgeColor = "#EF4444";
                entry.BadgeBg = "#381313";
                break;
            default:
                entry.BadgeColor = "#38BDF8";
                entry.BadgeBg = "#0B263D";
                break;
        }

        return entry;
    }
}
