using System.Globalization;

namespace UniversalParser.Core.Formats.Ogg;

/// <summary>Human-readable interpretations kept next to the official raw values.</summary>
internal static class OggFormatting
{
    public static string FormatHeaderType(byte value)
    {
        if (value == 0)
        {
            return "no flags";
        }

        var parts = new List<string>(3);
        if ((value & OggPage.ContinuedFlag) != 0)
        {
            parts.Add("continued packet");
        }

        if ((value & OggPage.BosFlag) != 0)
        {
            parts.Add("beginning of stream");
        }

        if ((value & OggPage.EosFlag) != 0)
        {
            parts.Add("end of stream");
        }

        if (parts.Count == 0)
        {
            parts.Add("reserved flags");
        }

        return string.Join(" | ", parts);
    }

    public static string FormatGranulePosition(ulong value) =>
        value == ulong.MaxValue
            ? "-1 (no packet finishes on this page)"
            : value.ToString(CultureInfo.InvariantCulture);

    public static string FormatColorSpace(byte value) => value switch
    {
        0 => "0 (undefined)",
        1 => "1 (Rec. 470M)",
        2 => "2 (Rec. 470BG)",
        _ => $"{value.ToString(CultureInfo.InvariantCulture)} (reserved)",
    };

    public static string FormatPixelFormat(byte value) => value switch
    {
        0 => "0 (4:2:0)",
        1 => "1 (reserved)",
        2 => "2 (4:2:2)",
        3 => "3 (4:4:4)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string FormatRatio(uint numerator, uint denominator)
    {
        if (denominator == 0)
        {
            return numerator.ToString(CultureInfo.InvariantCulture);
        }

        var value = (double)numerator / denominator;
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static string FormatQ78(short gain)
    {
        var decibels = gain / 256.0 * 20.0 * Math.Log10(2.0);
        return decibels.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
