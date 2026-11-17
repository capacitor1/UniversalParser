using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>
/// RIFF-specific text and number formatting. FourCC bytes are never forced through ASCII,
/// so non-ASCII identifiers keep their original byte value instead of becoming '?'.
/// </summary>
internal static class RiffFormatting
{
    public static string DecodeFourCc(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    public static string FormatFourCc(string id) => FormatFourCc(Encoding.Latin1.GetBytes(id));

    public static string SanitizeFourCc(string id) => SanitizeFourCc(Encoding.Latin1.GetBytes(id));

    /// <summary>Escapes control bytes and keeps printable characters, including spaces, verbatim.</summary>
    public static string FormatFourCc(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        foreach (var value in bytes)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                builder.Append((char)value);
            }
            else
            {
                builder.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    /// <summary>Title-safe FourCC: control characters become '.', printable characters are kept.</summary>
    public static string SanitizeFourCc(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        foreach (var value in bytes)
        {
            builder.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');
        }

        return builder.ToString();
    }

    /// <summary>
    /// RIFF text is usually CP1252, but many real files carry UTF-8. Try strict UTF-8 first,
    /// then fall back to Latin1 so no byte is lost.
    /// </summary>
    public static string DecodeText(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)0);
        if (end >= 0)
        {
            data = data[..end];
        }

        if (data.IsEmpty)
        {
            return string.Empty;
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 0 => bytes.ToString(CultureInfo.InvariantCulture),
        < 1024 => $"{bytes} B",
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.##} KiB ({bytes:N0} B)"),
    };

    public static string FormatHex(ReadOnlySpan<byte> bytes, int maxBytes) => HexFormatting.Format(bytes, maxBytes);
}
