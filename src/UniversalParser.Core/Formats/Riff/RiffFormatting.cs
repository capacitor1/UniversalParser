using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>
/// RIFF-specific text and number formatting. FourCC bytes are never forced through ASCII,
/// so non-ASCII identifiers keep their original byte value instead of becoming '?'.
/// </summary>
internal static class RiffFormatting
{
    public static string DecodeFourCc(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    public static string FormatFourCc(string id) => RawIdentifier.FormatText(id);

    public static string SanitizeFourCc(string id) => SanitizeFourCc(Encoding.Latin1.GetBytes(id));

    /// <summary>Escapes control bytes and keeps printable characters, including spaces, verbatim.</summary>
    public static string FormatFourCc(ReadOnlySpan<byte> bytes) => RawIdentifier.FormatBytes(bytes);

    /// <summary>Title-safe FourCC: control characters become '.', printable characters are kept.</summary>
    public static string SanitizeFourCc(ReadOnlySpan<byte> bytes)
    {
        return string.Create(bytes.Length, bytes, static (destination, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var value = source[index];
                destination[index] = value is >= 0x20 and <= 0x7E ? (char)value : '.';
            }
        });
    }

    /// <summary>
    /// RIFF text is usually CP1252, but many real files carry UTF-8. The shared predictor tries
    /// strict UTF-8 first, then Windows-1252, and keeps a NUL terminator as <c>\0</c>.
    /// </summary>
    public static string DecodeText(ReadOnlySpan<byte> data) =>
        TextDecoding.DisplayPredictedNullTerminated(data);

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 0 => bytes.ToString(CultureInfo.InvariantCulture),
        < 1024 => $"{bytes} B",
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.##} KiB ({bytes:N0} B)"),
    };

    public static string FormatHex(ReadOnlySpan<byte> bytes, int maxBytes) => HexFormatting.Format(bytes, maxBytes);
}
