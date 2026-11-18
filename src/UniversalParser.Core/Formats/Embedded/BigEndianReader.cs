using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Bounded big-endian reader over one block payload. Reading never crosses the block range;
/// callers turn a false return into a truncation diagnostic instead of an exception.
/// </summary>
internal sealed class BigEndianReader : IBoundedReader
{
    private readonly SourceReader _reader;

    public BigEndianReader(IRandomAccessSource source, long offset, long length)
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

    public bool TryInt16BE(out short value)
    {
        if (!this.TryUInt16BE(out var raw))
        {
            value = 0;
            return false;
        }

        value = unchecked((short)raw);
        return true;
    }

    public void SkipRemaining() => _reader.Skip(_reader.Remaining);

    public void Skip(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _reader.Skip(count);
    }

    public byte[] ReadPrefix(long length, int maxBytes, out SourceRange range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _reader.Remaining);
        range = new SourceRange(_reader.Position, length);
        var count = checked((int)Math.Min(length, maxBytes));
        var bytes = _reader.ReadBytes(count);
        _reader.Skip(length - count);
        LastRange = range;
        return bytes;
    }

    /// <summary>
    /// Reads a big-endian double. Callers must check <see cref="Remaining"/> or use a preceding
    /// <c>Require</c> helper; a short block is a truncation, not an exception.
    /// </summary>
    public bool TryDoubleBE(out double value)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (!this.TryReadBytes(bytes))
        {
            value = 0;
            return false;
        }

        value = System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(bytes);
        return true;
    }

    public bool TrySingleBE(out float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!this.TryReadBytes(bytes))
        {
            value = 0;
            return false;
        }

        value = System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(bytes);
        return true;
    }
}
