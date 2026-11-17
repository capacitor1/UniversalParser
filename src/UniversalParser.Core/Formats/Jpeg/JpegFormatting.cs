using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;

namespace UniversalParser.Core.Formats.Jpeg;

/// <summary>Display helpers for JPEG marker values and text payloads.</summary>
internal static class JpegFormatting
{
    public static string FormatMarker(byte code) => JpegConstants.FormatMarker(code);

    public static string FormatName(byte code) => JpegConstants.GetName(code);

    public static string FormatHex(ReadOnlySpan<byte> bytes, int maxBytes) => HexFormatting.Format(bytes, maxBytes);

    public static string FormatVersion(byte major, byte minor) =>
        string.Create(CultureInfo.InvariantCulture, $"{major}.{minor:00}");

    public static string FormatDensityUnit(int value) => value switch
    {
        0 => "0 (No units)",
        1 => "1 (Pixels per inch)",
        2 => "2 (Pixels per centimetre)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string FormatTableClass(int value) => value switch
    {
        0 => "0 (DC)",
        1 => "1 (AC)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string FormatPrecision(int value) => value switch
    {
        8 => "8 (8-bit samples)",
        12 => "12 (12-bit samples)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string FormatText(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return EscapeText(encoding.GetString(bytes));
    }

    public static string EscapeText(string value) => TextEscaping.Escape(value);
}
