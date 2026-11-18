using System.Text;
using UniversalParser.Core.Formatting;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// ID3v2 string decoding. The declared encoding byte controls the decoder; UTF-16 text may carry
/// a byte-order mark, and all text is preserved exactly as stored (no transliteration).
/// </summary>
internal static class Id3String
{
    public static readonly Encoding EncodingIso88591 = Encoding.Latin1;

    public static string Decode(ReadOnlySpan<byte> data, Encoding encoding) =>
        TextDecoding.Display(data, encoding);

    public static string Decode(ReadOnlySpan<byte> data, byte encoding) => encoding switch
    {
        0 => Decode(data, EncodingIso88591),
        1 => DecodeUtf16WithBom(data),
        2 => Decode(data, Encoding.BigEndianUnicode),
        3 => Decode(data, TextEncodings.Utf8),
        _ => Decode(data, EncodingIso88591),
    };

    public static string[] DecodeTextValues(ReadOnlySpan<byte> data, byte encoding)
    {
        var width = encoding is 1 or 2 ? 2 : 1;
        var values = new List<string>();
        var start = 0;
        for (var index = 0; index + width <= data.Length; index += width)
        {
            if (data.Slice(index, width).IndexOfAnyExcept((byte)0) >= 0)
            {
                continue;
            }

            if (index > start)
            {
                values.Add(Decode(data[start..index], encoding));
            }

            start = index + width;
        }

        if (start < data.Length)
        {
            values.Add(Decode(data[start..], encoding));
        }

        return values.Count == 0 ? [] : [.. values];
    }

    public static string EncodingName(byte encoding) => encoding switch
    {
        0 => "ISO-8859-1",
        1 => "UTF-16",
        2 => "UTF-16BE",
        3 => "UTF-8",
        _ => "UNKNOWN",
    };

    private static string DecodeUtf16WithBom(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2)
        {
            if (data[0] == 0xFF && data[1] == 0xFE)
            {
                return Decode(data[2..], Encoding.Unicode);
            }

            if (data[0] == 0xFE && data[1] == 0xFF)
            {
                return Decode(data[2..], Encoding.BigEndianUnicode);
            }
        }

        return Decode(data, Encoding.Unicode);
    }
}
