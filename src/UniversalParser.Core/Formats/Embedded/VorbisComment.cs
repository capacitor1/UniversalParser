using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Embedded;

internal enum VorbisCommentNaming
{
    LowerCase,
    UpperCase,
}

/// <summary>
/// Shared Vorbis comment structure (Vorbis I specification section 5, RFC 7845 section 5.2,
/// FLAC RFC 9639 section 8.6). Lengths are little endian; the magic signature is consumed by
/// the caller. FLAC uses uppercase keys, the Ogg codecs use the specification's lowercase names.
/// </summary>
internal static class VorbisComment
{
    public static void Parse(
        FieldList fields,
        BlockReader reader,
        string prefix,
        VorbisCommentNaming naming,
        bool hasFramingBit,
        int maxStringBytes)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(reader);

        var upper = naming == VorbisCommentNaming.UpperCase;
        var vendorLengthKey = prefix + (upper ? "VENDOR_LENGTH" : "vendor_length");
        var vendorStringKey = prefix + (upper ? "VENDOR_STRING" : "vendor_string");
        var listLengthKey = prefix + (upper ? "USER_COMMENT_LIST_LENGTH" : "user_comment_list_length");
        var framingKey = prefix + (upper ? "FRAMING_BIT" : "framing_bit");

        if (!reader.TryUInt32LE(out var vendorLength))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The comment vendor length is truncated.", reader.CurrentRange);
            return;
        }

        fields.Raw(vendorLengthKey, vendorLength, reader.LastRange);
        if (vendorLength > reader.Remaining)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The comment vendor string extends beyond the block.",
                new SourceRange(reader.Position, reader.Remaining));
            reader.SkipRemaining();
            return;
        }

        var vendor = reader.ReadText(vendorLength, maxStringBytes, out var vendorRange, out var vendorTruncated);
        fields.Raw(vendorStringKey, vendor, vendorRange);
        if (vendorTruncated)
        {
            fields.Warning(DiagnosticCodes.LimitString, $"'{vendorStringKey}' is shown as a prefix.", vendorRange);
        }

        if (!reader.TryUInt32LE(out var commentCount))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The user comment count is truncated.", reader.CurrentRange);
            return;
        }

        fields.Raw(listLengthKey, commentCount, reader.LastRange);
        var maximumPossible = (uint)Math.Min(reader.Remaining / 4, uint.MaxValue);
        if (commentCount > maximumPossible)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The user comment count exceeds the remaining block bytes; the list is clamped.",
                reader.LastRange);
            commentCount = maximumPossible;
        }

        for (var index = 0u; index < commentCount; index++)
        {
            if (!reader.TryUInt32LE(out var length))
            {
                break;
            }

            var lengthKey = upper
                ? $"{prefix}USER_COMMENT[{index}].LENGTH"
                : $"{prefix}user_comment[{index}].length";
            var commentKey = upper
                ? $"{prefix}USER_COMMENT[{index}]"
                : $"{prefix}user_comment[{index}]";
            fields.Raw(lengthKey, length, reader.LastRange);
            if (length > reader.Remaining)
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"The user comment [{index}] extends beyond the block.",
                    new SourceRange(reader.Position, reader.Remaining));
                reader.SkipRemaining();
                return;
            }

            var comment = reader.ReadText(length, maxStringBytes, out var range, out var truncated);
            fields.Raw(commentKey, comment, range);
            if (truncated)
            {
                fields.Warning(DiagnosticCodes.LimitString, $"The user comment [{index}] is shown as a prefix.", range);
            }
        }

        if (hasFramingBit)
        {
            if (reader.TryByte(out var framing))
            {
                fields.Raw(framingKey, framing.ToString(System.Globalization.CultureInfo.InvariantCulture), reader.LastRange);
                if (framing == 0)
                {
                    fields.Warning(DiagnosticCodes.SchemaMismatch, "The Vorbis comment framing bit is not set.", reader.LastRange);
                }
            }
            else
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, "The Vorbis comment framing bit is missing.", reader.CurrentRange);
            }
        }

        if (reader.Remaining > 0)
        {
            fields.Note(
                $"{reader.Remaining} byte(s) after the comment list are not interpreted.",
                reader.CurrentRange);
            reader.SkipRemaining();
        }
    }
}
