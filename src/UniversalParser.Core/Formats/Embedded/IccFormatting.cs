using System.Globalization;
using System.Text;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Human-readable forms for the ICC profile format. Names follow ICC.1:2022; values that the
/// specification leaves unnamed keep descriptive keys at the call site. Raw values stay first and
/// the interpretation is only appended in parentheses.
/// </summary>
internal static class IccFormatting
{
    private static readonly Dictionary<string, string> ProfileClasses = new(StringComparer.Ordinal)
    {
        ["scnr"] = "Input device profile",
        ["mntr"] = "Display device profile",
        ["prtr"] = "Output device profile",
        ["link"] = "DeviceLink profile",
        ["spac"] = "ColorSpace conversion profile",
        ["abst"] = "Abstract profile",
        ["nmcl"] = "Named colour profile",
    };

    private static readonly Dictionary<string, string> ColorSpaces = new(StringComparer.Ordinal)
    {
        ["XYZ "] = "XYZ",
        ["Lab "] = "Lab",
        ["Luv "] = "Luv",
        ["YCbr"] = "YCbCr",
        ["Yxy "] = "Yxy",
        ["RGB "] = "RGB",
        ["GRAY"] = "Gray",
        ["HSV "] = "HSV",
        ["HLS "] = "HLS",
        ["CMYK"] = "CMYK",
        ["CMY "] = "CMY",
        ["2CLR"] = "2 colour",
        ["3CLR"] = "3 colour",
        ["4CLR"] = "4 colour",
        ["5CLR"] = "5 colour",
        ["6CLR"] = "6 colour",
        ["7CLR"] = "7 colour",
        ["8CLR"] = "8 colour",
        ["9CLR"] = "9 colour",
        ["ACLR"] = "10 colour",
        ["BCLR"] = "11 colour",
        ["CCLR"] = "12 colour",
        ["DCLR"] = "13 colour",
        ["ECLR"] = "14 colour",
        ["FCLR"] = "15 colour",
    };

    private static readonly Dictionary<string, string> Platforms = new(StringComparer.Ordinal)
    {
        ["APPL"] = "Apple Computer Inc.",
        ["MSFT"] = "Microsoft Corporation",
        ["SGI "] = "Silicon Graphics Inc.",
        ["SUNW"] = "Sun Microsystems Inc.",
        ["TGNT"] = "Taligent Inc.",
    };

    private static readonly Dictionary<string, string> RenderingIntents = new(StringComparer.Ordinal)
    {
        ["0"] = "Perceptual",
        ["1"] = "Media-relative colorimetric",
        ["2"] = "Saturation",
        ["3"] = "ICC-absolute colorimetric",
    };

    private static readonly Dictionary<string, string> Observers = new(StringComparer.Ordinal)
    {
        ["0"] = "Unknown",
        ["1"] = "CIE 1931 standard colorimetric observer",
        ["2"] = "CIE 1964 standard colorimetric observer",
    };

    private static readonly Dictionary<string, string> Geometries = new(StringComparer.Ordinal)
    {
        ["0"] = "Unknown",
        ["1"] = "45/0 or 0/45",
        ["2"] = "0d or d/0",
    };

    private static readonly Dictionary<string, string> Illuminants = new(StringComparer.Ordinal)
    {
        ["0"] = "Unknown",
        ["1"] = "D50",
        ["2"] = "D65",
        ["3"] = "D93",
        ["4"] = "F2",
        ["5"] = "D55",
        ["6"] = "A",
        ["7"] = "Equi-Power (E)",
        ["8"] = "F8",
    };

    private static readonly Dictionary<string, string> ColorantTypes = new(StringComparer.Ordinal)
    {
        ["0"] = "Unknown",
        ["1"] = "ITU-R BT.709",
        ["2"] = "SMPTE RP145",
        ["3"] = "EBU Tech.3213-E",
        ["4"] = "P22",
    };

    private static readonly Dictionary<string, string> TagNames = new(StringComparer.Ordinal)
    {
        ["A2B0"] = "AToB0",
        ["A2B1"] = "AToB1",
        ["A2B2"] = "AToB2",
        ["B2A0"] = "BToA0",
        ["B2A1"] = "BToA1",
        ["B2A2"] = "BToA2",
        ["D2B0"] = "DToB0",
        ["D2B1"] = "DToB1",
        ["D2B2"] = "DToB2",
        ["D2B3"] = "DToB3",
        ["B2D0"] = "BToD0",
        ["B2D1"] = "BToD1",
        ["B2D2"] = "BToD2",
        ["B2D3"] = "BToD3",
        ["bXYZ"] = "Blue colorant",
        ["bTRC"] = "Blue tone reproduction curve",
        ["bkpt"] = "Media black point",
        ["calt"] = "Calibration date and time",
        ["chad"] = "Chromatic adaptation",
        ["chrm"] = "Chromaticity",
        ["clro"] = "Colorant order",
        ["clrt"] = "Colorant table",
        ["cprt"] = "Copyright",
        ["desc"] = "Profile description",
        ["devs"] = "Device settings",
        ["dmdd"] = "Device model description",
        ["dmnd"] = "Device manufacturer description",
        ["gXYZ"] = "Green colorant",
        ["gTRC"] = "Green tone reproduction curve",
        ["gamt"] = "Gamut",
        ["kTRC"] = "Gray tone reproduction curve",
        ["lumi"] = "Luminance",
        ["meas"] = "Measurement",
        ["ncl2"] = "Named color2",
        ["pseq"] = "Profile sequence description",
        ["psid"] = "Profile sequence identifier",
        ["rXYZ"] = "Red colorant",
        ["rTRC"] = "Red tone reproduction curve",
        ["targ"] = "Characterization target",
        ["tech"] = "Technology",
        ["view"] = "Viewing conditions",
        ["vued"] = "Viewing conditions description",
        ["wtpt"] = "Media white point",
    };

    private static readonly Dictionary<string, string> TypeNames = new(StringComparer.Ordinal)
    {
        ["XYZ "] = "XYZType",
        ["curv"] = "curveType",
        ["para"] = "parametricCurveType",
        ["text"] = "textType",
        ["desc"] = "textDescriptionType",
        ["mluc"] = "multiLocalizedUnicodeType",
        ["sig "] = "signatureType",
        ["sf32"] = "s15Fixed16ArrayType",
        ["ui32"] = "uInt32ArrayType",
        ["ui16"] = "uInt16ArrayType",
        ["ui08"] = "uInt8ArrayType",
        ["dtim"] = "dateTimeType",
        ["meas"] = "measurementType",
        ["view"] = "viewingConditionsType",
        ["chrm"] = "chromaticityType",
        ["mft1"] = "lut8Type",
        ["mft2"] = "lut16Type",
        ["mAB "] = "lutAtoBType",
        ["mBA "] = "lutBtoAType",
        ["mpet"] = "multiProcessElementType",
    };

    public static string Signature(ReadOnlySpan<byte> bytes) => RawIdentifier.FormatBytes(bytes);

    public static string SignatureWithName(ReadOnlySpan<byte> bytes, IReadOnlyDictionary<string, string> names)
    {
        var signature = Signature(bytes);
        if (signature.Length == 0)
        {
            return signature;
        }

        var key = Encoding.Latin1.GetString(bytes);
        return names.TryGetValue(key, out var name) ? $"{signature} ({name})" : signature;
    }

    public static string ProfileClass(ReadOnlySpan<byte> bytes) => SignatureWithName(bytes, ProfileClasses);

    public static string ColorSpace(ReadOnlySpan<byte> bytes) => SignatureWithName(bytes, ColorSpaces);

    public static string Platform(ReadOnlySpan<byte> bytes) => SignatureWithName(bytes, Platforms);

    public static string RenderingIntent(uint value) =>
        RenderingIntents.TryGetValue(value.ToString(CultureInfo.InvariantCulture), out var name)
            ? $"{value} ({name})"
            : value.ToString(CultureInfo.InvariantCulture);

    public static string Observer(uint value) => Named(Observers, value);

    public static string Geometry(uint value) => Named(Geometries, value);

    public static string Illuminant(uint value) => Named(Illuminants, value);

    public static string ColorantType(ushort value) => Named(ColorantTypes, value);

    public static string? TagName(string signature) =>
        TagNames.TryGetValue(signature, out var name) ? name : null;

    public static string? TypeName(string signature) =>
        TypeNames.TryGetValue(signature, out var name) ? name : null;

    public static string Version(ReadOnlySpan<byte> bytes) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"0x{Convert.ToHexString(bytes)} ({bytes[0]}.{bytes[1] >> 4}.{bytes[1] & 0x0F})");

    public static string Fixed(int value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{value} ({(value / 65536m).ToString("0.#####", CultureInfo.InvariantCulture)})");

    public static string U8Fixed8(ushort value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{value} ({(value / 256m).ToString("0.#####", CultureInfo.InvariantCulture)})");

    public static string DateTime(ushort year, int month, int day, int hour, int minute, int second)
    {
        var valid = year is >= 1 and <= 9999
            && month is >= 1 and <= 12
            && day is >= 1 and <= 31
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59
            && second is >= 0 and <= 59;
        return valid
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{year:0000}-{month:00}-{day:00} {hour:00}:{minute:00}:{second:00}")
            : "invalid";
    }

    public static string Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\r' or '\n' or '\t')
            {
                builder.Append(character == '\r' ? "\\r" : character == '\n' ? "\\n" : "\\t");
            }
            else if (character < 0x20 || character == 0x7F)
            {
                builder.Append("\\x").Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    public static string Latin1(ReadOnlySpan<byte> bytes) => Text(Encoding.Latin1.GetString(bytes));

    public static string Utf16Be(ReadOnlySpan<byte> bytes) => Text(Encoding.BigEndianUnicode.GetString(bytes));

    private static string Named(IReadOnlyDictionary<string, string> names, uint value)
    {
        var key = value.ToString(CultureInfo.InvariantCulture);
        return names.TryGetValue(key, out var name) ? $"{value} ({name})" : key;
    }

    private static string Named(IReadOnlyDictionary<string, string> names, ushort value)
    {
        var key = value.ToString(CultureInfo.InvariantCulture);
        return names.TryGetValue(key, out var name) ? $"{value} ({name})" : key;
    }
}
