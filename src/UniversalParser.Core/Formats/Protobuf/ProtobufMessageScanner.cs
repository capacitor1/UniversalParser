using System.Buffers;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Protobuf;

internal enum ProtobufPayloadKind
{
    None,
    Empty,
    Text,
    Message,
    Bytes,
}

/// <summary>
/// Structural validation used by format probing and by length-delimited payload classification.
/// The scanner never creates nodes and never copies payload bytes; it only reads protobuf framing.
/// </summary>
internal static class ProtobufMessageScanner
{
    public static bool TryScan(
        IRandomAccessSource source,
        long start,
        long end,
        CancellationToken cancellationToken,
        bool inspectPayloads,
        out ProtobufScanStats stats) =>
        TryScan(
            source,
            start,
            end,
            cancellationToken,
            inspectPayloads,
            allowIncompleteTail: false,
            out stats);

    public static bool TryScan(
        IRandomAccessSource source,
        long start,
        long end,
        CancellationToken cancellationToken,
        bool inspectPayloads,
        bool allowIncompleteTail,
        out ProtobufScanStats stats)
    {
        stats = new ProtobufScanStats();
        if (start < 0 || end < start || end > source.Length)
        {
            return false;
        }

        if (!ScanRange(
                source,
                start,
                end,
                expectedEndGroup: null,
                0,
                cancellationToken,
                inspectPayloads,
                stats,
                out var next))
        {
            return allowIncompleteTail && stats.IncompleteTail && stats.FieldCount > 0;
        }

        return next == end && stats.FieldCount > 0;
    }

    public static bool TryFindGroupEnd(
        IRandomAccessSource source,
        long start,
        long limit,
        uint fieldNumber,
        CancellationToken cancellationToken,
        out long endKeyStart,
        out long endKeyEnd)
    {
        endKeyStart = 0;
        endKeyEnd = 0;
        var position = start;
        while (position < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ProtobufRead.TryReadFieldHeader(source, position, limit, out var header, out _))
            {
                return false;
            }

            if (header.WireType == ProtobufConstants.EndGroupWireType)
            {
                if (header.FieldNumber != fieldNumber)
                {
                    return false;
                }

                endKeyStart = header.Start;
                endKeyEnd = header.End;
                return true;
            }

            if (header.WireType == ProtobufConstants.StartGroupWireType)
            {
                if (!TryFindGroupEnd(
                        source,
                        header.ValueStart,
                        limit,
                        header.FieldNumber,
                        cancellationToken,
                        out _,
                        out var nestedEnd))
                {
                    return false;
                }

                position = nestedEnd;
                continue;
            }

            if (header.Truncated)
            {
                return false;
            }

            position = header.ValueEnd;
        }

        return false;
    }

    private static bool ScanRange(
        IRandomAccessSource source,
        long start,
        long end,
        uint? expectedEndGroup,
        int depth,
        CancellationToken cancellationToken,
        bool inspectPayloads,
        ProtobufScanStats stats,
        out long next)
    {
        next = start;
        var position = start;
        while (position < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ProtobufRead.TryReadFieldHeader(source, position, end, out var header, out _))
            {
                if (end - position <= ProtobufConstants.MaxVarintBytes)
                {
                    stats.IncompleteTail = true;
                }

                return false;
            }

            if (header.WireType == ProtobufConstants.EndGroupWireType)
            {
                if (expectedEndGroup is { } expected && header.FieldNumber == expected)
                {
                    next = header.End;
                    return true;
                }

                return false;
            }

            stats.FieldCount++;
            if (header.FieldNumber > stats.MaxFieldNumber)
            {
                stats.MaxFieldNumber = header.FieldNumber;
            }

            if (header.WireType == ProtobufConstants.StartGroupWireType)
            {
                if (depth >= ProtobufConstants.MaxScanDepth)
                {
                    return false;
                }

                if (!ScanRange(
                        source,
                        header.ValueStart,
                        end,
                        header.FieldNumber,
                        depth + 1,
                        cancellationToken,
                        inspectPayloads,
                        stats,
                        out var groupEnd))
                {
                    return false;
                }

                position = groupEnd;
                continue;
            }

            if (header.Truncated)
            {
                stats.IncompleteTail = true;
                return false;
            }

            if (header.WireType == ProtobufConstants.LengthDelimitedWireType)
            {
                stats.LengthDelimitedCount++;
                if (inspectPayloads
                    && stats.LengthDelimitedCount <= ProtobufConstants.MaxPayloadInspections
                    && header.ValueEnd > header.ValueStart)
                {
                    if (ProtobufPayloadClassifier.LooksLikeText(
                            source,
                            header.ValueStart,
                            header.ValueEnd - header.ValueStart,
                            cancellationToken))
                    {
                        stats.TextPayloadCount++;
                    }
                    else if (header.ValueEnd - header.ValueStart <= ParseThresholds.LargeOperationBytes
                        && depth < 2
                        && TryScan(
                            source,
                            header.ValueStart,
                            header.ValueEnd,
                            cancellationToken,
                            inspectPayloads: false,
                            out _))
                    {
                        stats.NestedPayloadCount++;
                    }
                }
            }

            position = header.ValueEnd;
        }

        next = position;
        if (expectedEndGroup is not null)
        {
            stats.IncompleteTail = true;
            return false;
        }

        return true;
    }
}

internal sealed class ProtobufScanStats
{
    public int FieldCount { get; set; }
    public int LengthDelimitedCount { get; set; }
    public int TextPayloadCount { get; set; }
    public int NestedPayloadCount { get; set; }
    public uint MaxFieldNumber { get; set; }
    public bool IncompleteTail { get; set; }
}

/// <summary>
/// Length-delimited payloads have no schema-derived type. The classifier prefers readable UTF-8
/// text, then a fully valid nested message, and otherwise keeps the bytes opaque. This is a
/// display heuristic only; the encoded range and bytes are never changed.
/// </summary>
internal static class ProtobufPayloadClassifier
{
    public static ProtobufPayloadKind Classify(
        IRandomAccessSource source,
        long start,
        long length,
        CancellationToken cancellationToken)
    {
        if (length <= 0)
        {
            return ProtobufPayloadKind.Empty;
        }

        if (LooksLikeText(source, start, length, cancellationToken))
        {
            return ProtobufPayloadKind.Text;
        }

        if (ProtobufMessageScanner.TryScan(
                source,
                start,
                start + length,
                cancellationToken,
                inspectPayloads: false,
                out _))
        {
            return ProtobufPayloadKind.Message;
        }

        return ProtobufPayloadKind.Bytes;
    }

    public static bool LooksLikeText(
        IRandomAccessSource source,
        long start,
        long length,
        CancellationToken cancellationToken)
    {
        if (length <= 0)
        {
            return false;
        }

        var sampleLength = (int)Math.Min(length, ProtobufConstants.TextSampleBytes);
        var buffer = ArrayPool<byte>.Shared.Rent(sampleLength);
        try
        {
            var read = SourceRead.ReadAtMost(source, start, buffer.AsSpan(0, sampleLength));
            cancellationToken.ThrowIfCancellationRequested();
            if (read <= 0)
            {
                return false;
            }

            var bytes = buffer.AsSpan(0, read);
            foreach (var item in bytes)
            {
                if (item == 0)
                {
                    return false;
                }
            }

            var text = TextDecoding.Utf8(bytes, complete: read == length, out var invalidEncoding);
            if (invalidEncoding || text.Length == 0)
            {
                return false;
            }

            foreach (var character in text)
            {
                if (char.IsControl(character)
                    && character is not '\t' and not '\r' and not '\n')
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
