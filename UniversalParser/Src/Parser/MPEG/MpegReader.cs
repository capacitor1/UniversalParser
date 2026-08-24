using System.Buffers.Binary;
using System.Text;
using UniversalParser.Src.Parser.MPEG;

internal sealed class MpegReader(Stream stream)
{
    private readonly Stream _stream = stream;
    public long Position => _stream.Position;

    public void Seek(long offset, SeekOrigin origin)
    {
        _stream.Seek(offset, origin);
    }

    public uint ReadUInt32BE()
    {
        Span<byte> buffer = stackalloc byte[4];

        _stream.ReadExactly(buffer);

        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }
    public int ReadInt32BE()
    {
        Span<byte> buffer = stackalloc byte[4];

        _stream.ReadExactly(buffer);

        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    public ulong ReadUInt64BE()
    {
        Span<byte> buffer = stackalloc byte[8];

        _stream.ReadExactly(buffer);

        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }
    public long ReadInt64BE()
    {
        Span<byte> buffer = stackalloc byte[8];

        _stream.ReadExactly(buffer);

        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    public string ReadFourCC()
    {
        Span<byte> buffer = stackalloc byte[4];

        _stream.ReadExactly(buffer);

        return BoxName.ReadBoxType(buffer, 0);
    }
    public byte[] ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0)
            return Array.Empty<byte>();

        byte[] buffer = new byte[count];

        ReadExactly(buffer);

        return buffer;
    }

    public void ReadExactly(Span<byte> buffer)
    {
        int offset = 0;
        int remaining = buffer.Length;

        while (remaining > 0)
        {
            int read = _stream.Read(buffer.Slice(offset, remaining));

            if (read <= 0)
                throw new EndOfStreamException("Unexpected end of stream.");

            offset += read;
            remaining -= read;
        }
    }
    public byte ReadByte()
    {
        int value = _stream.ReadByte();

        if (value < 0)
            throw new EndOfStreamException();

        return (byte)value;
    }

    public ushort ReadUInt16BE()
    {
        Span<byte> buffer = stackalloc byte[2];

        _stream.ReadExactly(buffer);

        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }
    public string ReadNullTerminatedString()
    {
        long start = _stream.Position;

        int len = 0;
        while (true)
        {
            int b = _stream.ReadByte();
            if (b == -1 || b == 0x00)
                break;

            len++;
        }

        if (len == 0)
            return string.Empty;

        _stream.Position = start;

        Span<byte> buffer = len <= 256
            ? stackalloc byte[len]
            : new byte[len];

        _stream.ReadExactly(buffer);

        _stream.ReadByte(); // skip null terminator

        return Encoding.UTF8.GetString(buffer);
    }
}