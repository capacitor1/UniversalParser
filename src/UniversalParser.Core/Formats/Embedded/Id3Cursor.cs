using System.Text;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>Bounded cursor over a decoded ID3 frame or tag body.</summary>
internal ref struct Id3Cursor
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public Id3Cursor(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public int Position => _position;

    public int Remaining => _data.Length - _position;

    public bool End => _position >= _data.Length;

    public byte PeekByte(int relativeIndex = 0)
    {
        var index = _position + relativeIndex;
        return index >= 0 && index < _data.Length ? _data[index] : (byte)0;
    }

    public byte ReadByte()
    {
        if (End)
        {
            return 0;
        }

        return _data[_position++];
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count <= 0 || End)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        var actual = Math.Min(count, Remaining);
        var result = _data.Slice(_position, actual);
        _position += actual;
        return result;
    }

    public ReadOnlySpan<byte> ReadRest()
    {
        var result = _data[_position..];
        _position = _data.Length;
        return result;
    }

    public int ReadUInt(int byteCount)
    {
        byteCount = Math.Clamp(byteCount, 0, 4);
        var value = 0;
        for (var index = 0; index < byteCount; index++)
        {
            value = (value << 8) | ReadByte();
        }

        return value;
    }

    public int ReadInt16()
    {
        var value = ReadUInt(2);
        return value >= 0x8000 ? value - 0x10000 : value;
    }

    public int ReadSyncSafe32() => Id3Number.SyncSafe32(ReadBytes(4));

    public long ReadUIntVariable()
    {
        long value = 0;
        while (!End)
        {
            var current = ReadByte();
            value = (value << 7) | (uint)(current & 0x7F);
            if ((current & 0x80) == 0)
            {
                break;
            }
        }

        return value;
    }

    public string ReadLatin1String()
    {
        var zero = IndexOfZero();
        if (zero < 0)
        {
            zero = Remaining;
        }

        var result = Id3String.Decode(ReadBytes(zero), Id3String.EncodingIso88591);
        if (!End && PeekByte() == 0)
        {
            ReadByte();
        }

        return result;
    }

    public string ReadLatin1String(int count) =>
        Id3String.Decode(ReadBytes(count), Id3String.EncodingIso88591);

    public string ReadTerminatedText(byte encoding)
    {
        var terminatorLength = encoding is 1 or 2 ? 2 : 1;
        var index = FindTextTerminator(terminatorLength);
        if (index < 0)
        {
            return Id3String.Decode(ReadRest(), encoding);
        }

        var result = Id3String.Decode(ReadBytes(index), encoding);
        ReadBytes(terminatorLength);
        return result;
    }

    public string ReadBytesAsText(int count, Encoding encoding) =>
        Id3String.Decode(ReadBytes(count), encoding);

    public int IndexOfZero()
    {
        for (var index = _position; index < _data.Length; index++)
        {
            if (_data[index] == 0)
            {
                return index - _position;
            }
        }

        return -1;
    }

    private int FindTextTerminator(int terminatorLength)
    {
        if (terminatorLength == 1)
        {
            return IndexOfZero();
        }

        for (var index = _position; index + 1 < _data.Length; index++)
        {
            if (_data[index] == 0 && _data[index + 1] == 0)
            {
                return index - _position;
            }
        }

        return -1;
    }
}
