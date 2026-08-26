using System;
using System.Text;

namespace UniversalParser.Src.Parser.ID3
{
    internal ref struct ID3Cursor(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _data = data;
        private int _position = 0;

        public int Position => _position;
        public int Remaining => _data.Length - _position;
        public bool End => _position >= _data.Length;

        public byte PeekByte(int relativeIndex = 0)
        {
            int index = _position + relativeIndex;
            return index >= 0 && index < _data.Length ? _data[index] : (byte)0;
        }

        public byte ReadByte()
        {
            if (End)
                return 0;

            return _data[_position++];
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count <= 0 || End)
                return ReadOnlySpan<byte>.Empty;

            int actual = Math.Min(count, Remaining);
            ReadOnlySpan<byte> result = _data.Slice(_position, actual);
            _position += actual;
            return result;
        }

        public ReadOnlySpan<byte> ReadRest()
        {
            ReadOnlySpan<byte> result = _data[_position..];
            _position = _data.Length;
            return result;
        }

        public int ReadUInt(int byteCount)
        {
            byteCount = Math.Clamp(byteCount, 0, 4);

            int value = 0;
            for (int i = 0; i < byteCount; i++)
                value = (value << 8) | ReadByte();

            return value;
        }

        public int ReadInt16()
        {
            int value = ReadUInt(2);
            return value >= 0x8000 ? value - 0x10000 : value;
        }

        public int ReadSyncSafe32()
        {
            return ID3Number.SyncSafe32(ReadBytes(4));
        }

        public long ReadUIntVariable()
        {
            long value = 0;

            while (!End)
            {
                byte b = ReadByte();
                value = (value << 7) | (uint)(b & 0x7F);

                if ((b & 0x80) == 0)
                    break;
            }

            return value;
        }

        public string ReadLatin1String()
        {
            int zero = IndexOfZero();
            if (zero < 0)
                zero = Remaining;

            string result = ID3String.Decode(
                ReadBytes(zero),
                ID3String.EncodingIso88591);

            if (!End && PeekByte() == 0)
                ReadByte();

            return result;
        }

        public string ReadLatin1String(int count)
        {
            return ID3String.Decode(
                ReadBytes(count),
                ID3String.EncodingIso88591);
        }

        public string ReadTerminatedText(byte encoding)
        {
            int terminatorLength = encoding == 1 || encoding == 2 ? 2 : 1;
            int index = FindTextTerminator(terminatorLength);

            if (index < 0)
            {
                return ID3String.Decode(
                    ReadRest(),
                    encoding);
            }

            string result = ID3String.Decode(
                ReadBytes(index),
                encoding);

            ReadBytes(terminatorLength);
            return result;
        }

        public string ReadBytesAsText(int count, Encoding encoding)
        {
            return ID3String.Decode(ReadBytes(count), encoding);
        }

        public int IndexOfZero()
        {
            for (int i = _position; i < _data.Length; i++)
            {
                if (_data[i] == 0)
                    return i - _position;
            }

            return -1;
        }

        private int FindTextTerminator(int terminatorLength)
        {
            if (terminatorLength == 1)
                return IndexOfZero();

            for (int i = _position; i + 1 < _data.Length; i++)
            {
                if (_data[i] == 0 && _data[i + 1] == 0)
                    return i - _position;
            }

            return -1;
        }
    }
}