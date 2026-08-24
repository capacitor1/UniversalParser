using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF
{
    /// <summary>
    /// 块负载读取器。支持 RIFF(小端) 与 RIFX(大端)，并提供“不够就返回 false”的 Try* 方法，
    /// 以便在文件被截断时优雅降级而不是抛异常。
    /// </summary>
    internal sealed class RIFFReader
    {
        private readonly Stream _stream;

        public RIFFReader(Stream stream, bool isBigEndian = false)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));

            _stream = stream;
            IsBigEndian = isBigEndian;
        }

        public bool IsBigEndian { get; }
        public Stream BaseStream => _stream;
        public long Position => _stream.CanSeek ? _stream.Position : -1;
        public long? Remaining => _stream.CanSeek ? _stream.Length - _stream.Position : null;

        // ---------- 基础 ----------

        /// <summary>读满则 true，数据不足则 false（不抛异常）。</summary>
        public bool TryReadExactly(Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = _stream.Read(buffer[total..]);
                if (read <= 0) return false;
                total += read;
            }
            return true;
        }

        private Span<byte> ReadExactlyOrThrow(Span<byte> buffer)
        {
            if (!TryReadExactly(buffer))
                throw new EndOfStreamException("Unexpected end of RIFF chunk payload.");
            return buffer;
        }

        // ---------- 字节序自适应 ----------

        public ushort ReadUInt16() { Span<byte> b = stackalloc byte[2]; ReadExactlyOrThrow(b); return RIFFUtil.ReadUInt16(b, IsBigEndian); }
        public short  ReadInt16()  => unchecked((short)ReadUInt16());
        public uint   ReadUInt32() { Span<byte> b = stackalloc byte[4]; ReadExactlyOrThrow(b); return RIFFUtil.ReadUInt32(b, IsBigEndian); }
        public int    ReadInt32()  => unchecked((int)ReadUInt32());

        public ulong ReadUInt64()
        {
            Span<byte> b = stackalloc byte[8];
            ReadExactlyOrThrow(b);
            return IsBigEndian ? BinaryPrimitives.ReadUInt64BigEndian(b) : BinaryPrimitives.ReadUInt64LittleEndian(b);
        }

        public long   ReadInt64()  => unchecked((long)ReadUInt64());
        public float  ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());
        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

        public bool TryReadUInt16(out ushort value)
        {
            Span<byte> b = stackalloc byte[2];
            if (!TryReadExactly(b)) { value = 0; return false; }
            value = RIFFUtil.ReadUInt16(b, IsBigEndian);
            return true;
        }

        public bool TryReadUInt32(out uint value)
        {
            Span<byte> b = stackalloc byte[4];
            if (!TryReadExactly(b)) { value = 0; return false; }
            value = RIFFUtil.ReadUInt32(b, IsBigEndian);
            return true;
        }

        // ---------- 显式字节序（结构体内部字段有时固定字节序） ----------

        public uint  ReadUInt32LE() { Span<byte> b = stackalloc byte[4]; ReadExactlyOrThrow(b); return BinaryPrimitives.ReadUInt32LittleEndian(b); }
        public int   ReadInt32LE()  { Span<byte> b = stackalloc byte[4]; ReadExactlyOrThrow(b); return BinaryPrimitives.ReadInt32LittleEndian(b); }
        public ulong ReadUInt64LE() { Span<byte> b = stackalloc byte[8]; ReadExactlyOrThrow(b); return BinaryPrimitives.ReadUInt64LittleEndian(b); }
        public long  ReadInt64LE()  { Span<byte> b = stackalloc byte[8]; ReadExactlyOrThrow(b); return BinaryPrimitives.ReadInt64LittleEndian(b); }
        public uint  ReadUInt32BE() { Span<byte> b = stackalloc byte[4]; ReadExactlyOrThrow(b); return BinaryPrimitives.ReadUInt32BigEndian(b); }
        public ulong ReadUInt64BE() { Span<byte> b = stackalloc byte[8]; ReadExactlyOrThrow(b); return BinaryPrimitives.ReadUInt64BigEndian(b); }

        // ---------- 4CC / 字符串 / GUID ----------

        public string ReadFourCC()
        {
            Span<byte> b = stackalloc byte[RIFFUtil.FourCCSize];
            ReadExactlyOrThrow(b);
            return RIFFUtil.DecodeFourCC(b);
        }

        public bool TryReadFourCC(out string fourCC)
        {
            Span<byte> b = stackalloc byte[RIFFUtil.FourCCSize];
            if (!TryReadExactly(b)) { fourCC = string.Empty; return false; }
            fourCC = RIFFUtil.DecodeFourCC(b);
            return true;
        }

        public byte[] ReadBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count == 0) return [];

            byte[] result = new byte[count];
            if (!TryReadExactly(result))
                throw new EndOfStreamException("Unexpected end of RIFF chunk payload.");
            return result;
        }

        /// <summary>定长字符串，截断到第一个 NUL。</summary>
        public string ReadFixedString(int length)
        {
            byte[] raw = ReadBytes(length);
            return RIFFUtil.DecodeText(raw);
        }

        /// <summary>WAVEFORMATEXTENSIBLE 的 SubFormat 用的是 Windows GUID 内存布局。</summary>
        public Guid ReadGuid()
        {
            Span<byte> b = stackalloc byte[16];
            ReadExactlyOrThrow(b);
            return new Guid(b);
        }

        // ---------- 跳过 / 对齐 ----------

        /// <summary>Skip 对不可 seek 的流退化为读丢弃，不再抛 NotSupportedException。</summary>
        public void Skip(long bytes)
        {
            if (bytes <= 0) return;

            if (_stream.CanSeek)
            {
                _stream.Seek(bytes, SeekOrigin.Current);
                return;
            }

            byte[] scratch = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                long left = bytes;
                while (left > 0)
                {
                    int want = (int)Math.Min(scratch.Length, left);
                    int read = _stream.Read(scratch, 0, want);
                    if (read <= 0) return;   // 已到结尾，静默返回
                    left -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        /// <summary>RIFF 的 2 字节对齐（相对于负载起点）。</summary>
        public void AlignToEven()
        {
            if (_stream.CanSeek && (_stream.Position & 1) != 0) Skip(1);
        }
    }
}