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

    /// <summary>
    /// Fraction digits used by <see cref="Percentage(decimal)"/>. Values outside
    /// <see cref="MinPercentageDecimals"/>..<see cref="MaxPercentageDecimals"/> are clamped; the
    /// setting is process-local and never persisted.
    /// </summary>
    public static int PercentageDecimals
    {
        get => Volatile.Read(ref field);
        set => Volatile.Write(
            ref field,
            Math.Clamp(value, MinPercentageDecimals, MaxPercentageDecimals));
    } = DefaultPercentageDecimals;

    /// <summary>Formats a 0..1 ratio with the configured number of fraction digits.</summary>
    public static string Percentage(decimal ratio) =>
        ratio.ToString(
            PercentageDecimals switch
            {
                1 => "P1",
                2 => "P2",
                3 => "P3",
                4 => "P4",
                5 => "P5",
                _ => "P6",
            },
            CultureInfo.InvariantCulture);

    /// <summary>Formats <paramref name="value"/> as a share of <paramref name="total"/>.</summary>
    public static string Percentage(long value, long total) =>
        Percentage(total == 0 ? 0 : (decimal)value / total);

    /// <summary>Formats floating-point values with invariant non-finite names and precision.</summary>
    internal static string FloatingPoint(float value, string format = "R") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    internal static string FloatingPoint(double value, string format = "R") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Formats an enum value as <c>value (name)</c>, or as a bare number when unknown.</summary>
    internal static string Enum(int value, IReadOnlyDictionary<int, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var key = value.ToString(CultureInfo.InvariantCulture);
        return names.TryGetValue(value, out var name) ? $"{key} ({name})" : key;
    }
}
