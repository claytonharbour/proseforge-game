using System.Globalization;

namespace ProseForge.Game.Presentation;

// The header's coin count, short enough that a long title like SANTA'S WORKSHOP still fits beside
// it. Under 10,000 the exact number shows; above that, three figures and a suffix. It rounds
// down, so the header never shows more than you have.
public static class Count
{
    private static readonly (long Size, string Suffix)[] Steps = [(1_000_000_000_000, "T"), (1_000_000_000, "B"), (1_000_000, "M"), (1_000, "k")];

    public static string Short(long n)
    {
        if (n < 10_000) return n.ToString(CultureInfo.InvariantCulture);
        foreach (var (size, suffix) in Steps)
        {
            if (n < size) continue;
            var whole = n / size;
            if (whole >= 100) return whole.ToString(CultureInfo.InvariantCulture) + suffix;
            var tenths = n / (size / 10) % 10;
            return whole.ToString(CultureInfo.InvariantCulture) + (tenths > 0 ? "." + tenths : "") + suffix;
        }
        return n.ToString(CultureInfo.InvariantCulture);
    }
}
