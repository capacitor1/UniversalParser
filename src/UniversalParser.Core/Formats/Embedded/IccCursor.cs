using System.Buffers.Binary;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Bounded big-endian reader over one ICC tag element or the profile header. ICC offsets are
/// relative to the profile start, so positions stay absolute inside the supplied profile buffer.
/// </summary>
internal sealed class IccCursor
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _position;

    public IccCursor(byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (offset > data.Length || length > data.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _data = data;
        _position = offset;
        _end = offset + length;
        LastOffset = offset;
        LastLength = 0;
    }

    public int Position => _position;
    public int End => _end;
    public int Remaining => _end - _position;
    public int LastOffset { get; private set; }
    public int LastLength { get; private set; }

    public bool TryByte(out byte value)
    {
        var start = _position;
        if (Remaining < 1)
        {
            value = 0;
            return false;
        }

        value = _data[_position++];
        SetLast(start, 1);
        return true;
    }

    public bool TryUInt16BE(out ushort value)
    {
        var start = _position;
        if (Remaining < 2)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_position, 2));
        _position += 2;
        SetLast(start, 2);
        return true;
    }

    public bool TryUInt32BE(out uint value)
    {
        var start = _position;
        if (Remaining < 4)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(_position, 4));
        _position += 4;
        SetLast(start, 4);
        return true;
    }

    public bool TryInt32BE(out int value)
    {
        var start = _position;
        if (Remaining < 4)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_position, 4));
        _position += 4;
        SetLast(start, 4);
        return true;
    }

    public bool TryUInt64BE(out ulong value)
    {
        var start = _position;
        if (Remaining < 8)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(_position, 8));
        _position += 8;
        SetLast(start, 8);
        return true;
    }

    public bool TryReadBytes(Span<byte> destination)
    {
        var start = _position;
        if (Remaining < destination.Length)
        {
            return false;
        }

        _data.AsSpan(_position, destination.Length).CopyTo(destination);
        _position += destination.Length;
        SetLast(start, destination.Length);
        return true;
    }

    public byte[] ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Remaining);

        var bytes = _data.AsSpan(_position, count).ToArray();
        SetLast(_position, count);
        _position += count;
        return bytes;
    }

    /// <summary>
    /// Returns at most <paramref name="maxBytes"/> of the remaining data and advances to the end.
    /// The last range still covers the full remaining span so a truncated display does not shrink
    /// the source range.
    /// </summary>
    public byte[] ReadRemainingPrefix(int maxBytes, out int totalLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var start = _position;
        totalLength = Remaining;
        var count = Math.Min(totalLength, maxBytes);
        var bytes = _data.AsSpan(_position, count).ToArray();
        SetLast(start, totalLength);
        _position = _end;
        return bytes;
    }

    public void Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Remaining);
        _position += count;
    }

    public void SkipRemaining() => _position = _end;

    public void Seek(int position)
    {
        if (position < _position || position > _end)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        _position = position;
    }

    public IccCursor Slice(int offset, int length) => new(_data, offset, length);

    private void SetLast(int offset, int length)
    {
        LastOffset = offset;
        LastLength = length;
    }
}
