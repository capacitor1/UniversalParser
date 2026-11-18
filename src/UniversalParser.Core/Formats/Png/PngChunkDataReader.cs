using System.Buffers.Binary;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Png;

/// <summary>
/// Bounded big-endian reader over one chunk's data. Reading never crosses the chunk data range;
/// callers turn a false return into a truncation diagnostic instead of an exception.
/// </summary>
internal sealed class PngChunkDataReader : IBoundedReader
{
    private readonly SourceReader _reader;

    public PngChunkDataReader(IRandomAccessSource source, long offset, long length)
    {
        ArgumentNullException.ThrowIfNull(source);
        _reader = new SourceReader(source, offset, length);
        LastRange = SourceRange.Empty;
    }

    public long Position => _reader.Position;
    public long Remaining => _reader.Remaining;
    public SourceRange CurrentRange => new(_reader.Position, _reader.Remaining);
    public SourceRange LastRange { get; set; }

    public byte ReadByteCore() => _reader.ReadByte();

    public ushort ReadUInt16Core(Endian endian) => _reader.ReadUInt16(endian);

    public uint ReadUInt32Core(Endian endian) => _reader.ReadUInt32(endian);

    public ulong ReadUInt64Core(Endian endian) => _reader.ReadUInt64(endian);

    public void ReadExactlyCore(Span<byte> destination) => _reader.ReadExactly(destination);

    public void SkipCore(long count) => _reader.Skip(count);

    public bool TryInt32BE(out int value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 4)
        {
            value = 0;
            return false;
        }

        Span<byte> buffer = stackalloc byte[4];
        _reader.ReadExactly(buffer);
        value = BinaryPrimitives.ReadInt32BigEndian(buffer);
        LastRange = new SourceRange(start, 4);
        return true;
    }

    /// <summary>
    /// Reads a null-terminated byte string up to <paramref name="maxBytes"/>. Returns false when the
    /// terminator is missing or the budget is exhausted; <paramref name="exceeded"/> distinguishes a
    /// too-long string from a string that ran into the end of the chunk.
    /// </summary>
    public bool TryReadNullTerminated(
        int maxBytes,
        out byte[] bytes,
        out SourceRange range,
        out bool exceeded)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        var start = _reader.Position;
        var buffer = new byte[Math.Min(64, maxBytes)];
        var length = 0;
        exceeded = false;
        while (_reader.Remaining > 0)
        {
            var value = _reader.ReadByte();
            if (value == 0)
            {
                bytes = buffer.AsSpan(0, length).ToArray();
                range = new SourceRange(start, length);
                return true;
            }

            if (length == maxBytes)
            {
                bytes = buffer.AsSpan(0, length).ToArray();
                range = new SourceRange(start, length);
                exceeded = true;
                return false;
            }

            if (length == buffer.Length)
            {
                var next = (int)Math.Min((long)buffer.Length * 2, maxBytes);
                Array.Resize(ref buffer, next);
            }

            buffer[length++] = value;
        }

        bytes = buffer.AsSpan(0, length).ToArray();
        range = new SourceRange(start, length);
        return false;
    }

    /// <summary>Advances to the end of the chunk without materializing the remaining bytes.</summary>
    public void SkipRemaining() => _reader.Skip(_reader.Remaining);

}
