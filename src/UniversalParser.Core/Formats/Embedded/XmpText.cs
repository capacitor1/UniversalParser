using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// XMP is a readable XML document but the product boundary says not to build an XML tree. This
/// helper exposes the complete packet text instead of leaving the containing block unparsed.
/// </summary>
internal static class XmpText
{
    public static void Parse(ParseContext context, FieldList fields, BigEndianReader data, string prefix = "")
    {
        var bytes = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out var length, out var range);
        var text = TextDecoding.Utf8(bytes, bytes.Length == length, out var invalidEncoding);
        if (invalidEncoding)
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                "The XMP packet is not valid UTF-8; raw bytes are shown as hex.",
                range);
        }

        fields.Raw(
            prefix + "Text",
            invalidEncoding ? TextDecoding.Hex(bytes) : TextEscaping.Escape(text),
            range);
        fields.Derived($"<{prefix}PacketByteLength>", length, range);
        if (length > bytes.Length)
        {
            fields.Warning(
                DiagnosticCodes.LimitString,
                $"The XMP packet is {length} byte(s); only the first {bytes.Length} are shown.",
                range);
        }
    }
}
