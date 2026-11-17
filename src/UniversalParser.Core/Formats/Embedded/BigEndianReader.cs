using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Bounded big-endian reader over one block payload. Reading never crosses the block range;
/// callers turn a false return into a truncation diagnostic instead of an exception.
/// </summary>
internal sealed class BigEndianReader
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
    public SourceRange LastRange { get; private set; }

    public bool TryByte(out byte value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 1)
        {
            value = 0;
            return false;
        }

        value = _reader.ReadByte();
        LastRange = new SourceRange(start, 1);
        return true;
    }

    public bool TryInt16BE(out short value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 2)
        {
            value = 0;
            return false;
        }

        value = unchecked((short)_reader.ReadUInt16(Endian.Big));
        LastRange = new SourceRange(start, 2);
        return true;
    }

    public bool TryUInt16BE(out ushort value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 2)
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt16(Endian.Big);
        LastRange = new SourceRange(start, 2);
        return true;
    }

    public bool TryUInt32BE(out uint value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 4)
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt32(Endian.Big);
        LastRange = new SourceRange(start, 4);
        return true;
    }

    public bool TryUInt64BE(out ulong value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 8)
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt64(Endian.Big);
        LastRange = new SourceRange(start, 8);
        return true;
    }

    public bool TryReadBytes(Span<byte> destination)
    {
        var start = _reader.Position;
        if (_reader.Remaining < destination.Length)
        {
            return false;
        }

        _reader.ReadExactly(destination);
        LastRange = new SourceRange(start, destination.Length);
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
        if (length > _reader.Remaining) throw new ArgumentOutOfRangeException(nameof(length));
        range = new SourceRange(_reader.Position, length);
        var count = checked((int)Math.Min(length, maxBytes));
        var bytes = _reader.ReadBytes(count);
        _reader.Skip(length - count);
        LastRange = range;
        return bytes;
    }

    public byte[] ReadRemainingPrefix(int maxBytes, out long totalLength, out SourceRange range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var start = _reader.Position;
        totalLength = _reader.Remaining;
        range = new SourceRange(start, totalLength);
        var count = (int)Math.Min(totalLength, maxBytes);
        if (count == 0)
        {
            _reader.Skip(totalLength);
            return [];
        }

        var bytes = new byte[count];
        _reader.ReadExactly(bytes);
        _reader.Skip(totalLength - count);
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
        if (!TryReadBytes(bytes))
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
        if (!TryReadBytes(bytes))
        {
            value = 0;
            return false;
        }

        value = System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(bytes);
        return true;
    }
}
