using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace UniversalParser.Src.Parser.FLV
{
    /// <summary>
    /// FLV/AMF 使用大端字节序。
    /// </summary>
    internal sealed class FLVReader
    {
        private readonly Stream _stream;

        public FLVReader(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            if (!stream.CanRead)
                throw new ArgumentException("Stream must be readable.", nameof(stream));

            _stream = stream;
        }

        public Stream BaseStream => _stream;

        public long Position => _stream.CanSeek ? _stream.Position : -1;

        public long? Remaining =>
            _stream.CanSeek ? Math.Max(0, _stream.Length - _stream.Position) : null;

        public bool TryReadExactly(Span<byte> destination)
        {
            int total = 0;

            while (total < destination.Length)
            {
                int read = _stream.Read(destination[total..]);

                if (read <= 0)
                    return false;

                total += read;
            }

            return true;
        }

        public byte ReadUInt8()
        {
            int value = _stream.ReadByte();

            if (value < 0)
                throw new EndOfStreamException();

            return (byte)value;
        }

        public sbyte ReadInt8() => unchecked((sbyte)ReadUInt8());

        public ushort ReadUInt16BE()
        {
            Span<byte> buffer = stackalloc byte[2];

            if (!TryReadExactly(buffer))
                throw new EndOfStreamException();

            return BinaryPrimitives.ReadUInt16BigEndian(buffer);
        }

        public short ReadInt16BE() => unchecked((short)ReadUInt16BE());

        public uint ReadUInt24BE()
        {
            Span<byte> buffer = stackalloc byte[3];

            if (!TryReadExactly(buffer))
                throw new EndOfStreamException();

            return FLVUtil.ReadUInt24BE(buffer);
        }

        public int ReadInt24BE()
        {
            Span<byte> buffer = stackalloc byte[3];

            if (!TryReadExactly(buffer))
                throw new EndOfStreamException();

            return FLVUtil.ReadInt24BE(buffer);
        }

        public uint ReadUInt32BE()
        {
            Span<byte> buffer = stackalloc byte[4];

            if (!TryReadExactly(buffer))
                throw new EndOfStreamException();

            return BinaryPrimitives.ReadUInt32BigEndian(buffer);
        }

        public int ReadInt32BE() => unchecked((int)ReadUInt32BE());

        public ulong ReadUInt64BE()
        {
            Span<byte> buffer = stackalloc byte[8];

            if (!TryReadExactly(buffer))
                throw new EndOfStreamException();

            return BinaryPrimitives.ReadUInt64BigEndian(buffer);
        }

        public long ReadInt64BE() => unchecked((long)ReadUInt64BE());

        public double ReadDoubleBE() =>
            BitConverter.Int64BitsToDouble(ReadInt64BE());

        public string ReadUtf8(int byteLength)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(byteLength);

            if (byteLength == 0)
                return string.Empty;

            byte[] buffer = new byte[byteLength];

            if (!TryReadExactly(buffer))
                throw new EndOfStreamException();

            return Encoding.UTF8.GetString(buffer);
        }

        public string ReadUtf8WithUInt16Length()
        {
            ushort length = ReadUInt16BE();
            return ReadUtf8(length);
        }

        public string ReadUtf8WithUInt32Length()
        {
            uint length = ReadUInt32BE();

            if (length > int.MaxValue)
                throw new InvalidDataException("AMF string is too large.");

            return ReadUtf8((int)length);
        }

        public byte[] ReadBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            if (count == 0)
                return [];

            byte[] result = new byte[count];

            if (!TryReadExactly(result))
                throw new EndOfStreamException();

            return result;
        }

        public void Skip(long bytes)
        {
            if (bytes <= 0)
                return;

            if (_stream.CanSeek)
            {
                long remaining = Math.Max(0, _stream.Length - _stream.Position);
                _stream.Seek(Math.Min(bytes, remaining), SeekOrigin.Current);
                return;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

            try
            {
                long remaining = bytes;

                while (remaining > 0)
                {
                    int wanted = (int)Math.Min(buffer.Length, remaining);
                    int read = _stream.Read(buffer, 0, wanted);

                    if (read <= 0)
                        break;

                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}