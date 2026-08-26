using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace UniversalParser.Src.Parser.ASF
{
    /// <summary>
    /// ASF 对象负载读取器。ASF 所有数值一律小端；GUID 为 Windows 混合端序布局，
    /// 直接读 16 字节交给 <see cref="Guid(ReadOnlySpan{byte})"/> 即可得到正确值。
    /// 提供 Try* 方法以便在文件被截断时优雅降级。
    /// </summary>
    internal sealed class ASFReader
    {
        private readonly Stream _stream;

        public ASFReader(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));

            _stream = stream;
        }

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
                throw new EndOfStreamException("Unexpected end of ASF object payload.");
            return buffer;
        }

        // ---------- 数值（一律 Little Endian） ----------

        public byte ReadByte() => ReadExactlyOrThrow(stackalloc byte[1])[0];

        public bool TryReadByte(out byte value)
        {
            Span<byte> b = stackalloc byte[1];
            if (!TryReadExactly(b)) { value = 0; return false; }
            value = b[0];
            return true;
        }

        public ushort ReadUInt16() { Span<byte> b = stackalloc byte[2]; ReadExactlyOrThrow(b); return BinaryPrimitives.ReadUInt16LittleEndian(b); }
        public short  ReadInt16()  => unchecked((short)ReadUInt16());
        public uint   ReadUInt32() { Span<byte> b = stackalloc byte[4]; ReadExactlyOrThrow(b); return BinaryPrimitives.ReadUInt32LittleEndian(b); }
        public int    ReadInt32()  => unchecked((int)ReadUInt32());
        public ulong  ReadUInt64() { Span<byte> b = stackalloc byte[8]; ReadExactlyOrThrow(b); return BinaryPrimitives.ReadUInt64LittleEndian(b); }
        public long   ReadInt64()  => unchecked((long)ReadUInt64());

        public bool TryReadUInt16(out ushort value)
        {
            Span<byte> b = stackalloc byte[2];
            if (!TryReadExactly(b)) { value = 0; return false; }
            value = BinaryPrimitives.ReadUInt16LittleEndian(b);
            return true;
        }

        public bool TryReadUInt32(out uint value)
        {
            Span<byte> b = stackalloc byte[4];
            if (!TryReadExactly(b)) { value = 0; return false; }
            value = BinaryPrimitives.ReadUInt32LittleEndian(b);
            return true;
        }

        public bool TryReadUInt64(out ulong value)
        {
            Span<byte> b = stackalloc byte[8];
            if (!TryReadExactly(b)) { value = 0; return false; }
            value = BinaryPrimitives.ReadUInt64LittleEndian(b);
            return true;
        }

        // ---------- GUID / 字节 / 字符串 ----------

        public Guid ReadGuid()
        {
            Span<byte> b = stackalloc byte[16];
            ReadExactlyOrThrow(b);
            return new Guid(b);
        }

        public bool TryReadGuid(out Guid guid)
        {
            Span<byte> b = stackalloc byte[16];
            if (!TryReadExactly(b)) { guid = Guid.Empty; return false; }
            guid = new Guid(b);
            return true;
        }

        public byte[] ReadBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count == 0) return [];

            byte[] result = new byte[count];
            if (!TryReadExactly(result))
                throw new EndOfStreamException("Unexpected end of ASF object payload.");
            return result;
        }

        /// <summary>
        /// 按字符数读取 UTF-16LE 字符串（字节数 = characterCount × 2），
        /// 解码时截断到第一个 NUL。MS-ASF 中的字符串长度字段均以字符计。
        /// </summary>
        public string ReadWideString(int characterCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(characterCount);
            byte[] raw = ReadBytes(checked(characterCount * 2));
            return ASFUtil.DecodeWide(raw);
        }

        // ---------- 跳过 ----------

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
                    if (read <= 0) return;
                    left -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }
    }
}