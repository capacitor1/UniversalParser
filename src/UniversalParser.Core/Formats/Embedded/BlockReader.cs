using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Bounded reader for one embedded block or codec packet. Big- and little-endian accessors are
/// both available because FLAC is big endian while Ogg page headers and Vorbis comments are
/// little endian. Reads never cross the block range.
/// </summary>
internal sealed class BlockReader : IBoundedReader
{
    private readonly SourceReader _reader;

    public BlockReader(IRandomAccessSource source, long offset, long length)
    {
        ArgumentNullException.ThrowIfNull(source);
        _reader = new SourceReader(source, offset, length);
        LastRange = SourceRange.Empty;
    }

    public long Position => _reader.Position;
    public long Remaining => _reader.Remaining;
    public SourceRange CurrentRange => new(_reader.Position, _reader.Remaining);
    public SourceRange LastRange { get; set; }

    public byte ReadByteCore() => _reader.ReadByte();

    public ushort ReadUInt16Core(Endian endian) => _reader.ReadUInt16(endian);

    public uint ReadUInt32Core(Endian endian) => _reader.ReadUInt32(endian);

    public ulong ReadUInt64Core(Endian endian) => _reader.ReadUInt64(endian);

    public void ReadExactlyCore(Span<byte> destination) => _reader.ReadExactly(destination);

    public void SkipCore(long count) => _reader.Skip(count);

    public bool TryUInt16LE(out ushort value) => this.TryUInt16(Endian.Little, out value);
    public bool TryUInt16BE(out ushort value) => this.TryUInt16(Endian.Big, out value);

    public bool TryInt16LE(out short value)
    {
        if (!TryUInt16LE(out var raw))
        {
            value = 0;
            return false;
        }

        value = unchecked((short)raw);
        return true;
    }

    public bool TryUInt24LE(out uint value) => TryUInt24(out value, Endian.Little);
    public bool TryUInt24BE(out uint value) => TryUInt24(out value, Endian.Big);

    public bool TryUInt32LE(out uint value) => this.TryUInt32(Endian.Little, out value);
    public bool TryUInt32BE(out uint value) => this.TryUInt32(Endian.Big, out value);

    public bool TryUInt64LE(out ulong value) => this.TryUInt64(Endian.Little, out value);

    public bool TryUInt64BE(out ulong value) => this.TryUInt64(Endian.Big, out value);

    public bool TryInt64LE(out long value)
    {
        if (!TryUInt64LE(out var raw))
        {
            value = 0;
            return false;
        }

        value = unchecked((long)raw);
        return true;
    }

    /// <summary>Clamps to the packet end; callers report truncation from the following reads.</summary>
    public void Skip(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _reader.Skip(Math.Min(count, _reader.Remaining));
    }

    public void SkipRemaining() => _reader.Skip(_reader.Remaining);

    public string ReadText(long length, int maxBytes, out SourceRange range, out bool truncated)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        if (length > _reader.Remaining)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Text length exceeds the packet.");
        }

        range = new SourceRange(_reader.Position, length);
        var count = checked((int)Math.Min(length, maxBytes));
        var bytes = _reader.ReadBytes(count);
        _reader.Skip(length - count);
        LastRange = range;
        truncated = length > maxBytes;
        return TextDecoding.Display(bytes, TextEncodings.Utf8);
    }

    /// <summary>Reads a NUL-terminated string without crossing the block or exceeding the cap.</summary>
    public bool TryReadNullTerminatedString(int maxBytes, out string value, out SourceRange range)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        var start = _reader.Position;
        var buffer = new byte[Math.Min(maxBytes, 256)];
        var length = 0;
        while (length < maxBytes)
        {
            if (!this.TryByte(out var item))
            {
                value = string.Empty;
                range = SourceRange.Empty;
                return false;
            }

            if (item == 0)
            {
                range = new SourceRange(start, length + 1);
                value = Encoding.UTF8.GetString(buffer, 0, length);
                return true;
            }

            if (length == buffer.Length)
            {
                Array.Resize(ref buffer, Math.Min(maxBytes, buffer.Length * 2));
            }

            buffer[length++] = item;
        }

        value = string.Empty;
        range = SourceRange.Empty;
        return false;
    }

    private bool TryUInt24(out uint value, Endian endian)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 3)
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt24(endian);
        LastRange = new SourceRange(start, 3);
        return true;
    }
}
