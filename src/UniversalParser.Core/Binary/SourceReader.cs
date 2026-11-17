using System.Buffers.Binary;
using System.Text;

namespace UniversalParser.Core.Binary;

/// <summary>
/// Bounded, endian-aware reader over a source region. The reader owns a local position only.
/// </summary>
public sealed class SourceReader
{
    private readonly IRandomAccessSource _source;
    private readonly long _end;
    private long _position;

    public SourceReader(IRandomAccessSource source, long offset, long length)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (offset > source.Length || length > source.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Reader range exceeds the source length.");
        }

        _source = source;
        _position = offset;
        _end = offset + length;
    }

    public long Position => _position;
    public long End => _end;
    public long Remaining => _end - _position;
    public SourceRange CurrentRange => new(_position, Remaining);

    public byte ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        ReadExactly(buffer);
        return buffer[0];
    }

    public ushort ReadUInt16(Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactly(buffer);
        return endian == Endian.Little
            ? BinaryPrimitives.ReadUInt16LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    public uint ReadUInt24(Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[3];
        ReadExactly(buffer);
        return endian == Endian.Little
            ? (uint)(buffer[0] | buffer[1] << 8 | buffer[2] << 16)
            : (uint)(buffer[0] << 16 | buffer[1] << 8 | buffer[2]);
    }

    public uint ReadUInt32(Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactly(buffer);
        return endian == Endian.Little
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    public ulong ReadUInt64(Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactly(buffer);
        return endian == Endian.Little
            ? BinaryPrimitives.ReadUInt64LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    public void ReadExactly(Span<byte> destination)
    {
        EnsureAvailable(destination.Length);
        _source.ReadExactly(_position, destination);
        _position += destination.Length;
    }

    public byte[] ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return [];
        }

        EnsureAvailable(count);
        var bytes = new byte[count];
        ReadExactly(bytes);
        return bytes;
    }

    public string ReadString(int count, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.GetString(ReadBytes(count));
    }

    /// <summary>
    /// Reads a null-terminated string. Throws when the terminator is not found within
    /// <paramref name="maxBytes"/> or before the bounded region ends.
    /// </summary>
    public string ReadNullTerminatedString(Encoding encoding, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        var buffer = new byte[Math.Min(maxBytes, 256)];
        var length = 0;
        while (length < maxBytes)
        {
            if (Remaining == 0)
            {
                throw new SourceReadException(_position, 1, 0, "Null-terminated string reached the end of the source region.");
            }

            var value = ReadByte();
            if (value == 0)
            {
                return encoding.GetString(buffer, 0, length);
            }

            if (length == buffer.Length)
            {
                var nextLength = buffer.Length >= maxBytes / 2 ? maxBytes : buffer.Length * 2;
                Array.Resize(ref buffer, nextLength);
            }

            buffer[length++] = value;
        }

        throw new SourceReadException(_position, 1, Remaining, $"Null-terminated string exceeded {maxBytes} bytes.");
    }

    public void Skip(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > Remaining)
        {
            throw new SourceReadException(_position, (int)Math.Min(count, int.MaxValue), Remaining);
        }

        _position += count;
    }

    public SourceReader Slice(SourceRange range)
    {
        if (!CurrentRange.Contains(range))
        {
            throw new ArgumentOutOfRangeException(nameof(range), "Slice exceeds the reader's current region.");
        }

        return new SourceReader(_source, range.Offset, range.Length);
    }

    private void EnsureAvailable(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > Remaining)
        {
            throw new SourceReadException(_position, count, Remaining);
        }
    }
}
