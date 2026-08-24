using System.Buffers.Binary;
using System.Text;

namespace UniversalParser.Src.Parser.PNG
{
    internal sealed class PngReader(Stream stream)
    {
        private readonly Stream _stream = stream;

        /// <summary>PNG 规定 chunk 长度上限为 2^31-1。</summary>
        public const uint MaxChunkLength = 0x7FFFFFFF;

        public long Position => _stream.Position;

        // =========================
        // BIG ENDIAN (PNG standard)
        // =========================
        public ushort ReadUInt16BE()
        {
            Span<byte> buffer = stackalloc byte[2];
            _stream.ReadExactly(buffer);
            return BinaryPrimitives.ReadUInt16BigEndian(buffer);
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

        public byte ReadByte()
        {
            int value = _stream.ReadByte();
            if (value < 0)
                throw new EndOfStreamException();
            return (byte)value;
        }

        // =========================
        // STRINGS
        // =========================

        /// <summary>
        /// 读取 4 字节 chunk 类型。使用 Latin-1 而非 ASCII，
        /// 以保证损坏的类型名在显示时仍能一一区分。
        /// </summary>
        public string ReadFourCC()
        {
            Span<byte> buffer = stackalloc byte[4];
            _stream.ReadExactly(buffer);
            return Encoding.Latin1.GetString(buffer);
        }

        public string ReadLatin1(int count) => Encoding.Latin1.GetString(ReadBytes(count));

        /// <summary>
        /// 读取以 NUL 结尾的 Latin-1 字符串。
        /// </summary>
        /// <param name="maxBytes">最多消耗的字节数（含 NUL）。</param>
        /// <returns>
        /// Value        - 不含 NUL 的字符串；
        /// Consumed     - 实际消耗的字节数（含 NUL，若找到）；
        /// Terminated   - 是否在限额内找到了 NUL。
        /// </returns>
        public (string Value, int Consumed, bool Terminated) ReadNullTerminatedLatin1(int maxBytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

            var buf = new List<byte>(Math.Min(maxBytes, 80));

            for (int i = 0; i < maxBytes; i++)
            {
                byte b = ReadByte();
                if (b == 0)
                    return (Encoding.Latin1.GetString(buf.ToArray()), i + 1, true);
                buf.Add(b);
            }

            return (Encoding.Latin1.GetString(buf.ToArray()), maxBytes, false);
        }

        // =========================
        // BULK
        // =========================
        public byte[] ReadBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            if (count == 0)
                return [];

            byte[] buffer = new byte[count];
            ReadExactly(buffer);
            return buffer;
        }

        public void ReadExactly(Span<byte> buffer) => _stream.ReadExactly(buffer);

        // =========================
        // POSITIONING
        // =========================
        public void Seek(long offset) => _stream.Seek(offset, SeekOrigin.Begin);

        public void Skip(long count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            _stream.Seek(count, SeekOrigin.Current);
        }
    }
}