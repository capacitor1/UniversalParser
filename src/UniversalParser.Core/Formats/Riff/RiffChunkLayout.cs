using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>
/// Resolved layout of one RIFF chunk. NodeRange includes the one-byte pad required after an
/// odd-sized payload; PayloadRange excludes both the header and the pad.
/// </summary>
internal readonly record struct RiffChunkLayout(
    string Id,
    long Start,
    uint RawSize,
    long ResolvedSize,
    long PayloadStart,
    long PayloadEnd,
    long NodeEnd,
    bool Truncated,
    bool HasPadding)
{
    public SourceRange NodeRange => new(Start, NodeEnd - Start);
    public SourceRange PayloadRange => new(PayloadStart, PayloadEnd - PayloadStart);

    public static RiffChunkLayout Create(
        string id,
        long start,
        uint rawSize,
        long resolvedSize,
        long containerEnd)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(resolvedSize);
        ArgumentOutOfRangeException.ThrowIfNegative(containerEnd);

        var payloadStart = containerEnd - start >= 8 ? start + 8 : containerEnd;
        var declaredEnd = resolvedSize > long.MaxValue - payloadStart
            ? long.MaxValue
            : payloadStart + resolvedSize;
        var payloadEnd = Math.Min(declaredEnd, containerEnd);
        var truncated = payloadEnd < declaredEnd;
        var hasPadding = !truncated && (resolvedSize & 1) != 0 && payloadEnd < containerEnd;
        var nodeEnd = hasPadding ? payloadEnd + 1 : payloadEnd;

        return new RiffChunkLayout(
            id,
            start,
            rawSize,
            resolvedSize,
            payloadStart,
            payloadEnd,
            nodeEnd,
            truncated,
            hasPadding);
    }
}
