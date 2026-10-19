using System.Globalization;

namespace Tidegate.Core;

public static class RateText
{
    public static string Format(double bytesPerSecond)
    {
        if (double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond) || bytesPerSecond < 0.5)
            return "0 B/s";

        var units = new[] { "B/s", "KB/s", "MB/s", "GB/s" };
        var value = bytesPerSecond;
        var index = 0;
        while (value >= 1000 && index < units.Length - 1)
        {
            value /= 1000;
            index++;
        }

        var digits = value >= 100 || index == 0 ? "0" : "0.0";
        return $"{value.ToString(digits, CultureInfo.InvariantCulture)} {units[index]}";
    }

    public static bool TryParseKilobytes(string? text, out double? bytesPerSecond, out string? error)
    {
        bytesPerSecond = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var trimmed = text.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out var kilobytes)
            && !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out kilobytes))
        {
            error = "上限要是數字，單位是 KB/s。留空代表不限制。";
            return false;
        }

        if (double.IsNaN(kilobytes) || double.IsInfinity(kilobytes) || kilobytes < 1 || kilobytes > 10_000_000)
        {
            error = "上限要介於 1 KB/s 和 10000000 KB/s，留空代表不限制。";
            return false;
        }

        bytesPerSecond = kilobytes * 1000d;
        return true;
    }
}
