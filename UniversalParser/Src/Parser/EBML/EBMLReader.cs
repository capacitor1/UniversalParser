using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace UniversalParser.Src.Parser.EBML
{
    /// <summary>
    /// EBML Element 负载读取器。
    /// EBML 的整数、浮点和日期字段均采用大端字节序。
    /// </summary>
    internal sealed class EBMLReader : IDisposable
    {
        private readonly Stream _stream;

        public EBMLReader(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            if (!stream.CanRead)
                throw new ArgumentException("Stream must be readable.", nameof(stream));

            _stream = stream;
        }

        public Stream BaseStream => _stream;

        public long Position =>
            _stream.CanSeek ? _stream.Position : -1;

        public long? Remaining =>
            _stream.CanSeek ? _stream.Length - _stream.Position : null;

        public bool TryReadExactly(Span<byte> buffer)
        {
            int total = 0;

            while (total < buffer.Length)
            {
                int read = _stream.Read(buffer[total..]);
                if (read <= 0)
                    return false;

                total += read;
            }

            return true;
        }

        public void ReadExactly(Span<byte> buffer)
        {
            if (!TryReadExactly(buffer))
                throw new EndOfStreamException("Unexpected end of EBML element payload.");
        }

        public byte ReadByte()
        {
            int value = _stream.ReadByte();

            if (value < 0)
                throw new EndOfStreamException("Unexpected end of EBML element payload.");

            return (byte)value;
        }

        public byte[] ReadBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            if (count == 0)
                return [];

            byte[] result = new byte[count];
            ReadExactly(result);
            return result;
        }

        /// <summary>读取 Element ID。ID 的长度标记位保留。</summary>
        public ulong ReadElementId(out int encodedLength)
        {
            byte firstByte = ReadByte();
            int length = EBMLUtil.GetVIntLength(firstByte, EBMLUtil.MaxElementIdLength);

            if (length == 0)
                throw new InvalidDataException("Invalid EBML Element ID.");

            Span<byte> buffer = stackalloc byte[EBMLUtil.MaxElementIdLength];
            buffer[0] = firstByte;

            if (length > 1)
                ReadExactly(buffer.Slice(1, length - 1));

            if (!EBMLUtil.TryDecodeElementId(buffer[..length], out ulong id, out encodedLength))
                throw new InvalidDataException("Invalid EBML Element ID.");

            return id;
        }

        /// <summary>读取 Data Size。长度标记位会被清除。</summary>
        public ulong ReadDataSize(out int encodedLength, out bool isUnknown)
        {
            byte firstByte = ReadByte();
            int length = EBMLUtil.GetVIntLength(firstByte, EBMLUtil.MaxDataSizeLength);

            if (length == 0)
                throw new InvalidDataException("Invalid EBML Data Size.");

            Span<byte> buffer = stackalloc byte[EBMLUtil.MaxDataSizeLength];
            buffer[0] = firstByte;

            if (length > 1)
                ReadExactly(buffer.Slice(1, length - 1));

            if (!EBMLUtil.TryDecodeDataSize(
                    buffer[..length],
                    out ulong value,
                    out encodedLength,
                    out isUnknown))
            {
                throw new InvalidDataException("Invalid EBML Data Size.");
            }

            return value;
        }

        /// <summary>读取 EBML lacing 使用的有符号 VINT。</summary>
        public long ReadSignedVInt(out int encodedLength)
        {
            byte firstByte = ReadByte();
            int length = EBMLUtil.GetVIntLength(firstByte, EBMLUtil.MaxDataSizeLength);

            if (length == 0)
                throw new InvalidDataException("Invalid signed EBML VINT.");

            Span<byte> buffer = stackalloc byte[EBMLUtil.MaxDataSizeLength];
            buffer[0] = firstByte;

            if (length > 1)
                ReadExactly(buffer.Slice(1, length - 1));

            if (!EBMLUtil.TryDecodeSignedVInt(buffer[..length], out long value, out encodedLength))
                throw new InvalidDataException("Invalid signed EBML VINT.");

            return value;
        }

        public ulong ReadUnsignedInteger(int byteCount)
        {
            if (byteCount is < 1 or > 8)
                throw new ArgumentOutOfRangeException(
                    nameof(byteCount),
                    "An EBML unsigned integer must contain between 1 and 8 bytes.");

            Span<byte> buffer = stackalloc byte[8];
            ReadExactly(buffer[..byteCount]);
            return EBMLUtil.ReadUnsignedInteger(buffer[..byteCount]);
        }

        public long ReadSignedInteger(int byteCount)
        {
            if (byteCount is < 1 or > 8)
                throw new ArgumentOutOfRangeException(
                    nameof(byteCount),
                    "An EBML signed integer must contain between 1 and 8 bytes.");

            Span<byte> buffer = stackalloc byte[8];
            ReadExactly(buffer[..byteCount]);
            return EBMLUtil.ReadSignedInteger(buffer[..byteCount]);
        }

        public double ReadFloat(int byteCount)
        {
            Span<byte> buffer = stackalloc byte[8];

            switch (byteCount)
            {
                case 0:
                    // EBML 默认浮点值为 0.0
                    return 0.0;

                case 4:
                    ReadExactly(buffer[..4]);
                    return EBMLUtil.ReadFloat32(buffer[..4]);

                case 8:
                    ReadExactly(buffer);
                    return EBMLUtil.ReadFloat64(buffer);

                default:
                    throw new InvalidDataException(
                        $"An EBML floating-point value must contain 0, 4 or 8 bytes, found {byteCount}.");
            }
        }

        /// <summary>
        /// 读取 EBML Date。
        /// 原始值是相对于 2001-01-01T00:00:00 UTC 的有符号纳秒数。
        /// </summary>
        public DateTimeOffset ReadDate()
        {
            Span<byte> buffer = stackalloc byte[8];
            ReadExactly(buffer);

            long nanoseconds = BinaryPrimitives.ReadInt64BigEndian(buffer);

            // DateTime tick = 100 ns。不能整除的亚 tick 部分无法由 DateTimeOffset 表示。
            long ticks = nanoseconds / 100L;

            var epoch = new DateTimeOffset(
                2001, 1, 1,
                0, 0, 0,
                TimeSpan.Zero);

            return epoch.AddTicks(ticks);
        }

        public string ReadAsciiString(int byteCount)
        {
            byte[] data = ReadBytes(byteCount);
            int end = Array.IndexOf(data, (byte)0);

            if (end < 0)
                end = data.Length;

            return Encoding.ASCII.GetString(data, 0, end);
        }

        public string ReadUtf8String(int byteCount)
        {
            byte[] data = ReadBytes(byteCount);
            int end = Array.IndexOf(data, (byte)0);

            if (end < 0)
                end = data.Length;

            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(data, 0, end);
        }

        public void Skip(long bytes)
        {
            if (bytes <= 0)
                return;

            if (_stream.CanSeek)
            {
                _stream.Seek(bytes, SeekOrigin.Current);
                return;
            }

            byte[] scratch = ArrayPool<byte>.Shared.Rent(64 * 1024);

            try
            {
                long remaining = bytes;

                while (remaining > 0)
                {
                    int requested = (int)Math.Min(scratch.Length, remaining);
                    int read = _stream.Read(scratch, 0, requested);

                    if (read <= 0)
                        return;

                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}