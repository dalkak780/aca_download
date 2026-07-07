using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ArcaDownloader.Core.Services;

public static partial class TextHelpers
{
    public static string SanitizeFileName(string name, int maxLength = 80)
    {
        var cleaned = InvalidFileNameChars().Replace(name, " ").Trim();
        cleaned = Whitespace().Replace(cleaned, "-");
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength];
        }

        return string.IsNullOrWhiteSpace(cleaned) ? "post" : cleaned;
    }

    public static string EscapeHtml(string value) => WebUtility.HtmlEncode(value);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "?";
        var units = new[] { "B", "KB", "MB", "GB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    public static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null || duration.Value <= TimeSpan.Zero) return "";
        var value = duration.Value;
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours}시간 {value.Minutes}분";
        if (value.TotalMinutes >= 1) return $"{value.Minutes}분 {value.Seconds}초";
        return $"{Math.Max(1, value.Seconds)}초";
    }

    [GeneratedRegex(@"[\\/:*?""<>|]+")]
    private static partial Regex InvalidFileNameChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

