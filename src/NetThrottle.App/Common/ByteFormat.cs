using System.Globalization;

namespace NetThrottle.App.Common;

/// <summary>Human-readable formatting for byte rates and sizes.</summary>
public static class ByteFormat
{
    private static readonly string[] RateUnits = { "B/s", "KB/s", "MB/s", "GB/s" };

    /// <summary>Formats a byte/second value as e.g. "1.2 MB/s". Uses 1024 steps.</summary>
    public static string Rate(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0 B/s";
        int unit = 0;
        double value = bytesPerSec;
        while (value >= 1024 && unit < RateUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {RateUnits[unit]}");
    }
}
