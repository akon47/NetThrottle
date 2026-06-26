using System.Globalization;

namespace NetThrottle.App.Common;

/// <summary>Human-readable formatting for network rates in bits per second.</summary>
public static class ByteFormat
{
    private static readonly string[] RateUnits = { "bps", "Kbps", "Mbps", "Gbps" };

    /// <summary>Formats a byte/second value as a bit rate, e.g. "9.6 Mbps".
    /// Network units use decimal (1000) steps.</summary>
    public static string Rate(double bytesPerSec)
    {
        double bits = bytesPerSec * 8;
        if (bits <= 0) return "0 bps";
        int unit = 0;
        double value = bits;
        while (value >= 1000 && unit < RateUnits.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {RateUnits[unit]}");
    }
}
