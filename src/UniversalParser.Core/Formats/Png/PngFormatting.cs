using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Unicode;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;

namespace UniversalParser.Core.Formats.Png;

/// <summary>
/// PNG display helpers. Enum values are shown as the original number plus a short meaning; text is
/// decoded with the encoding declared by the chunk and escaped so control bytes cannot break the
/// field layout.
/// </summary>
internal static class PngFormatting
{
    private static readonly FrozenDictionary<int, string> ColorTypes = new Dictionary<int, string>()
    {
        [0] = "Greyscale",
        [2] = "Truecolor",
        [3] = "Indexed-color",
        [4] = "Greyscale with alpha",
        [6] = "Truecolor with alpha",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> RenderingIntents = new Dictionary<int, string>()
    {
        [0] = "Perceptual",
        [1] = "Relative colorimetric",
        [2] = "Saturation",
        [3] = "Absolute colorimetric",
    }.ToFrozenDictionary();

    // ITU-T H.273 code points used by the cICP chunk. Only assigned values get a name; reserved
    // code points stay as their raw number so a file cannot make the field list claim a meaning.
    private static readonly FrozenDictionary<int, string> ColorPrimaries = new Dictionary<int, string>()
    {
        [1] = "BT.709",
        [2] = "Unspecified",
        [4] = "BT.470 System M",
        [5] = "BT.470 System B, G",
        [6] = "BT.601",
        [7] = "SMPTE 240M",
        [8] = "Generic film",
        [9] = "BT.2020",
        [10] = "SMPTE ST 428-1 (CIE 1931 XYZ)",
        [11] = "SMPTE RP 431-2 (DCI-P3)",
        [12] = "SMPTE EG 432-1 (Display P3)",
        [22] = "EBU Tech 3213-E",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> TransferFunctions = new Dictionary<int, string>()
    {
        [1] = "BT.709",
        [2] = "Unspecified",
        [4] = "BT.470 System M",
        [5] = "BT.470 System B, G",
        [6] = "BT.601",
        [7] = "SMPTE 240M",
        [8] = "Linear",
        [9] = "Logarithmic (100:1)",
        [10] = "Logarithmic (100*sqrt(10):1)",
        [11] = "IEC 61966-2-4",
        [12] = "BT.1361",
        [13] = "IEC 61966-2-1 (sRGB)",
        [14] = "BT.2020 10-bit",
        [15] = "BT.2020 12-bit",
        [16] = "SMPTE ST 2084 (PQ)",
        [17] = "SMPTE ST 428-1",
        [18] = "ARIB STD-B67 (HLG)",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> MatrixCoefficients = new Dictionary<int, string>()
    {
        [0] = "Identity",
        [1] = "BT.709",
        [2] = "Unspecified",
        [4] = "US FCC 73.628",
        [5] = "BT.470 System B, G",
        [6] = "BT.601",
        [7] = "SMPTE 240M",
        [8] = "YCgCo",
        [9] = "BT.2020 non-constant luminance",
        [10] = "BT.2020 constant luminance",
        [11] = "SMPTE ST 2085",
        [12] = "Chromaticity-derived non-constant luminance",
        [13] = "Chromaticity-derived constant luminance",
        [14] = "ICtCp",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> DisposeOperations = new Dictionary<int, string>()
    {
        [0] = "APNG_DISPOSE_OP_NONE",
        [1] = "APNG_DISPOSE_OP_BACKGROUND",
        [2] = "APNG_DISPOSE_OP_PREVIOUS",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> BlendOperations = new Dictionary<int, string>()
    {
        [0] = "APNG_BLEND_OP_SOURCE",
        [1] = "APNG_BLEND_OP_OVER",
    }.ToFrozenDictionary();

    public static string DecodeChunkType(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    public static string FormatChunkType(string chunkType)
    {
        ArgumentNullException.ThrowIfNull(chunkType);
        var builder = new StringBuilder(chunkType.Length);
        foreach (var value in chunkType)
        {
            if (value is >= (char)0x20 and <= (char)0x7E)
            {
                builder.Append(value);
            }
            else
            {
                builder.Append("\\x").Append(((int)value).ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    public static string FormatColorType(int value) => FormatEnum(value, ColorTypes);

    public static string FormatBitDepth(int value) =>
        value is 1 or 2 or 4 or 8 or 16
            ? string.Create(CultureInfo.InvariantCulture, $"{value} bit(s) per sample")
            : string.Create(CultureInfo.InvariantCulture, $"{value} (not a PNG bit depth)");

    public static string FormatCompressionMethod(int value) => value switch
    {
        0 => "0 (Deflate/Inflate)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string FormatFilterMethod(int value) => value switch
    {
        0 => "0 (Adaptive filtering)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string FormatInterlaceMethod(int value) => value switch
    {
        0 => "0 (None)",
        1 => "1 (Adam7)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string FormatPhysicalUnit(int value) => value switch
    {
        0 => "0 (Unknown)",
        1 => "1 (Metre)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string FormatOffsetUnit(int value) => value switch
    {
        0 => "0 (Pixel)",
        1 => "1 (Micrometre)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string FormatRenderingIntent(int value) => FormatEnum(value, RenderingIntents);

    public static string FormatColorPrimaries(int value) => FormatEnum(value, ColorPrimaries);

    public static string FormatTransferFunction(int value) => FormatEnum(value, TransferFunctions);

    public static string FormatMatrixCoefficients(int value) => FormatEnum(value, MatrixCoefficients);

    public static string FormatVideoFullRangeFlag(int value) => value switch
    {
        0 => "0 (Narrow range)",
        1 => "1 (Full range)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string FormatDisposeOperation(int value) => FormatEnum(value, DisposeOperations);

    public static string FormatBlendOperation(int value) => FormatEnum(value, BlendOperations);

    public static string FormatFixed100000(uint value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{value} ({(value / 100000d).ToString("0.#####", CultureInfo.InvariantCulture)})");

    public static string FormatDelay(ushort numerator, ushort denominator)
    {
        var effectiveDenominator = denominator == 0 ? 100 : denominator;
        return (numerator / (double)effectiveDenominator).ToString("R", CultureInfo.InvariantCulture);
    }

    public static string FormatTimestamp(ushort year, int month, int day, int hour, int minute, int second)
    {
        var valid = year is >= 1 and <= 9999
            && month is >= 1 and <= 12
            && day is >= 1 and <= 31
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59
            && second is >= 0 and <= 60;
        if (!valid)
        {
            return "invalid";
        }

        var secondText = second == 60 ? "59" : second.ToString("00", CultureInfo.InvariantCulture);
        var timestamp = string.Create(
            CultureInfo.InvariantCulture,
            $"{year:0000}-{month:00}-{day:00}T{hour:00}:{minute:00}:{secondText}Z");
        return second == 60 ? timestamp + " (leap second)" : timestamp;
    }

    public static bool IsValidColorType(int colorType) => ColorTypes.ContainsKey(colorType);

    public static bool IsValidBitDepth(int colorType, int bitDepth) => colorType switch
    {
        0 => bitDepth is 1 or 2 or 4 or 8 or 16,
        2 => bitDepth is 8 or 16,
        3 => bitDepth is 1 or 2 or 4 or 8,
        4 => bitDepth is 8 or 16,
        6 => bitDepth is 8 or 16,
        _ => false,
    };

    public static string FormatText(ReadOnlySpan<byte> bytes, Encoding encoding) => TextDecoding.Escaped(bytes, encoding);

    public static bool IsValidUtf8(ReadOnlySpan<byte> bytes) => Utf8.IsValid(bytes);

    public static string EscapeText(string value) => TextEscaping.Escape(value);

    public static string FormatHex(ReadOnlySpan<byte> bytes, int maxBytes) => HexFormatting.Format(bytes, maxBytes);

    private static string FormatEnum(int value, IReadOnlyDictionary<int, string> names) =>
        ValueFormat.Enum(value, names);
}
