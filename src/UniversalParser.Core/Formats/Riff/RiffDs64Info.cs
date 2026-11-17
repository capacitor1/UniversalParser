using System.Buffers.Binary;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>Parsed contents of the RF64/BW64 'ds64' chunk.</summary>
internal sealed class RiffDs64Info
{
    private const int FixedSize = 28;
    private const int TableEntrySize = 12;
    private const int MaxTableEntries = 4096;

    private RiffDs64Info(ulong riffSize, ulong dataSize, ulong sampleCount, Dictionary<string, ulong> chunkSizes)
    {
        RiffSize = riffSize;
        DataSize = dataSize;
        SampleCount = sampleCount;
        ChunkSizes = chunkSizes;
    }

    public ulong RiffSize { get; }
    public ulong DataSize { get; }
    public ulong SampleCount { get; }
    public IReadOnlyDictionary<string, ulong> ChunkSizes { get; }

    public bool TryGetSize(string id, out ulong size) => ChunkSizes.TryGetValue(id, out size);

    public static RiffDs64Info? TryRead(
        IRandomAccessSource source,
        long rootPayloadStart,
        long rootPayloadEnd,
        DiagnosticBag? diagnostics = null)
    {
        if (!RiffRead.TryReadChunkHeader(source, rootPayloadStart, rootPayloadEnd, Endian.Little, out var id, out var rawSize)
            || id != "ds64")
        {
            return null;
        }

        var payloadStart = rootPayloadStart + 8;
        var available = Math.Min(source.Length, rootPayloadEnd) - payloadStart;
        if (available < FixedSize)
        {
            diagnostics?.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"'ds64' requires at least {FixedSize} bytes, {available} available.",
                new SourceRange(payloadStart, Math.Max(0, available)));
            return null;
        }

        var want = (int)Math.Min(rawSize, 4096L);
        want = (int)Math.Min(want, available);
        var payload = new byte[want];
        var read = RiffRead.ReadAtMost(source, payloadStart, payload);
        if (read < FixedSize)
        {
            diagnostics?.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"'ds64' requires at least {FixedSize} bytes, {read} available.",
                new SourceRange(payloadStart, read));
            return null;
        }

        payload = payload[..read];
        var riffSize = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(0, 8));
        var dataSize = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(8, 8));
        var sampleCount = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(16, 8));
        var tableLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(24, 4));
        var sizes = new Dictionary<string, ulong>(StringComparer.Ordinal);

        var availableEntries = (payload.Length - FixedSize) / TableEntrySize;
        var usableEntries = (int)Math.Min(Math.Min(tableLength, (uint)availableEntries), MaxTableEntries);
        for (var index = 0; index < usableEntries; index++)
        {
            var offset = FixedSize + index * TableEntrySize;
            var chunkId = RiffFormatting.DecodeFourCc(payload.AsSpan(offset, 4));
            var chunkSize = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(offset + 4, 8));
            sizes.TryAdd(chunkId, chunkSize);
        }

        if (tableLength > usableEntries)
        {
            diagnostics?.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"'ds64' declares {tableLength} table entries; only {usableEntries} are available.",
                new SourceRange(payloadStart, payload.Length));
        }

        return new RiffDs64Info(riffSize, dataSize, sampleCount, sizes);
    }
}
