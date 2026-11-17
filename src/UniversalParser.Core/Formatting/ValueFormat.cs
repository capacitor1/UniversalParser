using System.Globalization;

namespace UniversalParser.Core.Formatting;

/// <summary>
/// Process-wide numeric presentation shared by parsers and the GUI. Percentage precision is a
/// user setting (Settings → Percentage decimals), so every percentage shown to the user — including
/// values formatted while parsing — goes through this type instead of hard-coding "P1".
/// </summary>
public static class ValueFormat
{
    public const int MinPercentageDecimals = 1;
    public const int MaxPercentageDecimals = 6;
    public const int DefaultPercentageDecimals = 3;

    private static int _percentageDecimals = DefaultPercentageDecimals;

    /// <summary>
    /// Fraction digits used by <see cref="Percentage(decimal)"/>. Values outside
    /// <see cref="MinPercentageDecimals"/>..<see cref="MaxPercentageDecimals"/> are clamped; the
    /// setting is process-local and never persisted.
    /// </summary>
    public static int PercentageDecimals
    {
        get => Volatile.Read(ref _percentageDecimals);
        set => Volatile.Write(
            ref _percentageDecimals,
            Math.Clamp(value, MinPercentageDecimals, MaxPercentageDecimals));
    }

    /// <summary>Formats a 0..1 ratio with the configured number of fraction digits.</summary>
    public static string Percentage(decimal ratio) =>
        ratio.ToString(
            string.Create(CultureInfo.InvariantCulture, $"P{PercentageDecimals}"),
            CultureInfo.InvariantCulture);

    /// <summary>Formats <paramref name="value"/> as a share of <paramref name="total"/>.</summary>
    public static string Percentage(long value, long total) =>
        Percentage(total == 0 ? 0 : (decimal)value / total);
}
