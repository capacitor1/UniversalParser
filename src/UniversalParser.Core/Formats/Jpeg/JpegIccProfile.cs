using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Jpeg;

/// <summary>
/// ICC APP2 framing defined by the ICC profile embedding rules: after "ICC_PROFILE\0" there is a
/// one-byte marker sequence number and a one-byte marker count, followed by a slice of the profile.
/// Cross-segment assembly is deliberately not performed; only a complete single-segment profile is
/// parsed, because parsing must stay inside the current block.
/// </summary>
internal static class JpegIccProfile
{
    public static void Parse(ParseContext context, FieldList fields, BigEndianReader data)
    {
        if (!data.TryByte(out var sequenceNumber))
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The ICC APP2 segment is missing its marker sequence number.",
                data.CurrentRange);
            data.SkipRemaining();
            return;
        }

        fields.Raw("Marker sequence number", sequenceNumber, data.LastRange);

        if (!data.TryByte(out var markerCount))
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The ICC APP2 segment is missing its marker count.",
                data.CurrentRange);
            data.SkipRemaining();
            return;
        }

        fields.Raw("Number of markers", markerCount, data.LastRange);

        var chunkLength = data.Remaining;
        fields.Derived("<ProfileChunkLength>", chunkLength, data.CurrentRange);

        if (markerCount == 0 || sequenceNumber == 0 || sequenceNumber > markerCount)
        {
            fields.Warning(
                DiagnosticCodes.InvalidEncoding,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"ICC marker sequence {sequenceNumber} of {markerCount} is outside the defined 1..count range."),
                data.CurrentRange);
            KeepUnparsed(fields, data);
            return;
        }

        if (markerCount > 1)
        {
            fields.Derived("<MarkerCount>", markerCount, data.CurrentRange);
            fields.Note(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The ICC profile is split across {markerCount} APP2 segment(s); cross-segment assembly is not performed."),
                data.CurrentRange);
            KeepUnparsed(fields, data);
            return;
        }

        var limit = (int)Math.Min(context.Limits.MaxBlobBytes, int.MaxValue);
        var profile = data.ReadRemainingPrefix(limit, out var totalLength, out var range);
        if (totalLength > profile.Length)
        {
            fields.Warning(
                DiagnosticCodes.LimitBlob,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The ICC profile chunk is {totalLength} byte(s); only the first {profile.Length} are read."),
                range);
        }

        IccProfileParser.Parse(
            profile,
            fields,
            context.Limits,
            context.CancellationToken,
            profileOffset: range.Offset,
            fallbackRange: range);
    }

    private static void KeepUnparsed(FieldList fields, BigEndianReader data)
    {
        fields.Derived("<UnparsedLength>", data.Remaining, data.CurrentRange);
        data.SkipRemaining();
    }
}
