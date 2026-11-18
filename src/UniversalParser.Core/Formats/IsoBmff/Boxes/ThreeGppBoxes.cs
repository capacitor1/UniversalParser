using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

#pragma warning disable IDE0060 // Uniform box-parser signature keeps the dispatcher simple.

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>3GPP TS 26.244 asset meta-data boxes inside 'udta'.</summary>
internal static class ThreeGppBoxes
{
    public static string Asset(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields,
        string title,
        string textKey,
        bool hasTrackNumber)
    {
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        if (data.TryUInt16("language", out var packed))
        {
            fields.Derived("<language>", IsoBmffFieldHelpers.Language((ushort)(packed & 0x7FFF)));
        }

        var payload = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out var payloadLength, out _);
        if (payloadLength == 0)
        {
            fields.Raw(textKey, "<empty>");
            return title;
        }

        var encoding = "UTF-8";
        ReadOnlySpan<byte> textBytes = payload;
        if (payload.Length >= 2 && payload[0] == 0xFE && payload[1] == 0xFF)
        {
            encoding = "UTF-16BE (BOM 0xFEFF)";
            textBytes = payload.AsSpan(2);
        }
        else if (payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0xFE)
        {
            encoding = "UTF-16LE (BOM 0xFFFE)";
            textBytes = payload.AsSpan(2);
        }

        fields.Raw("text_encoding", encoding);

        var isUtf16 = encoding.StartsWith("UTF-16", StringComparison.Ordinal);
        var terminator = -1;
        if (isUtf16)
        {
            for (var index = 0; index + 1 < textBytes.Length; index += 2)
            {
                if (textBytes[index] == 0 && textBytes[index + 1] == 0)
                {
                    terminator = index;
                    break;
                }
            }
        }
        else
        {
            terminator = textBytes.IndexOf((byte)0);
        }

        ReadOnlySpan<byte> text;
        ReadOnlySpan<byte> tail;
        if (terminator < 0)
        {
            text = textBytes;
            tail = [];
        }
        else
        {
            text = textBytes[..terminator];
            tail = textBytes[(terminator + (isUtf16 ? 2 : 1))..];
        }

        var decoded = encoding.StartsWith("UTF-16BE", StringComparison.Ordinal)
            ? Encoding.BigEndianUnicode.GetString(text)
            : encoding.StartsWith("UTF-16LE", StringComparison.Ordinal)
                ? Encoding.Unicode.GetString(text)
                : Encoding.UTF8.GetString(text);
        fields.Raw(textKey, IsoBmffFormatting.Display(decoded));

        if (hasTrackNumber)
        {
            if (tail.Length >= 1)
            {
                fields.Raw("track_number", tail[0]);
            }
            else
            {
                fields.Derived("<track_number>", "<absent>");
            }
        }

        return title;
    }
}
