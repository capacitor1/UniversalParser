using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.IsoBmff;

/// <summary>
/// Bounded big-endian reader over one box payload. Every helper emits the raw field and turns a
/// short read into a TRUNCATED_PAYLOAD diagnostic instead of an exception, so a damaged box still
/// produces the fields read so far.
/// </summary>
internal sealed class IsoBmffBoxReader
{
    private readonly SourceReader _reader;
    private readonly FieldList _fields;
    private readonly IRandomAccessSource _source;

    public IsoBmffBoxReader(IRandomAccessSource source, long offset, long length, FieldList fields)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fields);
        _source = source;
        _reader = new SourceReader(source, offset, length);
        _fields = fields;
    }

    public long Position => _reader.Position;
    public long Remaining => _reader.Remaining;
    public SourceRange CurrentRange => new(_reader.Position, _reader.Remaining);
    public SourceRange LastRange { get; private set; } = SourceRange.Empty;

    public bool TryByte(string key, out byte value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 1)
        {
            value = 0;
            return Truncated(key);
        }

        value = _reader.ReadByte();
        LastRange = new SourceRange(start, 1);
        _fields.Raw(key, value, LastRange);
        return true;
    }

    public bool TryUInt16(string key, out ushort value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 2)
        {
            value = 0;
            return Truncated(key);
        }

        value = _reader.ReadUInt16(Endian.Big);
        LastRange = new SourceRange(start, 2);
        _fields.Raw(key, value, LastRange);
        return true;
    }

    public bool TryUInt32(string key, out uint value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 4)
        {
            value = 0;
            return Truncated(key);
        }

        value = _reader.ReadUInt32(Endian.Big);
        LastRange = new SourceRange(start, 4);
        _fields.Raw(key, value, LastRange);
        return true;
    }

    /// <summary>Reads a 24-bit big-endian unsigned integer (used by MPEG-4 descriptor fields).</summary>
    public bool TryUInt24(string key, out uint value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 3)
        {
            value = 0;
            return Truncated(key);
        }

        value = _reader.ReadUInt24(Endian.Big);
        LastRange = new SourceRange(start, 3);
        _fields.Raw(key, value, LastRange);
        return true;
    }

    public bool TryInt32(string key, out int value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 4)
        {
            value = 0;
            return Truncated(key);
        }

        value = unchecked((int)_reader.ReadUInt32(Endian.Big));
        LastRange = new SourceRange(start, 4);
        _fields.Raw(key, value, LastRange);
        return true;
    }

    public bool TryUInt64(string key, out ulong value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 8)
        {
            value = 0;
            return Truncated(key);
        }

        value = _reader.ReadUInt64(Endian.Big);
        LastRange = new SourceRange(start, 8);
        _fields.Raw(key, value, LastRange);
        return true;
    }

    public bool TryInt64(string key, out long value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 8)
        {
            value = 0;
            return Truncated(key);
        }

        value = unchecked((long)_reader.ReadUInt64(Endian.Big));
        LastRange = new SourceRange(start, 8);
        _fields.Raw(key, value, LastRange);
        return true;
    }

    public bool TryFourCc(string key, out string value)
    {
        var start = _reader.Position;
        if (_reader.Remaining < 4)
        {
            value = string.Empty;
            return Truncated(key);
        }

        Span<byte> bytes = stackalloc byte[4];
        _reader.ReadExactly(bytes);
        value = IsoBmffFourCc.Read(bytes);
        LastRange = new SourceRange(start, 4);
        _fields.Raw(key, value, LastRange);
        return true;
    }

    public bool TryBytes(string key, Span<byte> destination)
    {
        var start = _reader.Position;
        if (_reader.Remaining < destination.Length)
        {
            return Truncated(key);
        }

        _reader.ReadExactly(destination);
        LastRange = new SourceRange(start, destination.Length);
        _fields.Raw(key, IsoBmffFormatting.Hex(destination), LastRange);
        return true;
    }

    public bool TryReadBytes(Span<byte> destination)
    {
        var start = _reader.Position;
        if (_reader.Remaining < destination.Length)
        {
            return false;
        }

        _reader.ReadExactly(destination);
        LastRange = new SourceRange(start, destination.Length);
        return true;
    }

    /// <summary>Reads a value without emitting a field; used by table loops that emit their own rows.</summary>
    public bool TryReadUInt8Raw(out byte value)
    {
        if (_reader.Remaining < 1)
        {
            value = 0;
            return false;
        }

        value = _reader.ReadByte();
        return true;
    }

    public bool TryReadUInt16Raw(out ushort value)
    {
        if (_reader.Remaining < 2)
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt16(Endian.Big);
        return true;
    }

    public bool TryReadUInt32Raw(out uint value)
    {
        if (_reader.Remaining < 4)
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt32(Endian.Big);
        return true;
    }

    public bool TryReadUInt64Raw(out ulong value)
    {
        if (_reader.Remaining < 8)
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt64(Endian.Big);
        return true;
    }

    public bool TryReadFourCcRaw(out string value)
    {
        if (_reader.Remaining < 4)
        {
            value = string.Empty;
            return false;
        }

        Span<byte> bytes = stackalloc byte[4];
        _reader.ReadExactly(bytes);
        value = IsoBmffFourCc.Read(bytes);
        return true;
    }

    public bool TryReadBytesRaw(Span<byte> destination)
    {
        if (_reader.Remaining < destination.Length)
        {
            return false;
        }

        _reader.ReadExactly(destination);
        return true;
    }

    public bool TryFullBox(out byte version, out uint flags)
    {
        version = 0;
        flags = 0;
        if (_reader.Remaining < 4)
        {
            Truncated("version/flags");
            return false;
        }

        var start = _reader.Position;
        version = _reader.ReadByte();
        var b1 = _reader.ReadByte();
        var b2 = _reader.ReadByte();
        var b3 = _reader.ReadByte();
        flags = (uint)(b1 << 16 | b2 << 8 | b3);
        LastRange = new SourceRange(start, 4);
        _fields.Raw("version", version, new SourceRange(start, 1));
        _fields.Raw("flags", $"0x{flags:X6}", new SourceRange(start + 1, 3));
        return true;
    }

    public bool TrySkip(long count, string label)
    {
        if (count < 0 || _reader.Remaining < count)
        {
            return Truncated(label);
        }

        var start = _reader.Position;
        _reader.Skip(count);
        LastRange = new SourceRange(start, count);
        return true;
    }

    public void SkipRemaining() => _reader.Skip(_reader.Remaining);

    public ulong ReadVariableUInt(int byteCount, string key)
    {
        if (byteCount == 0)
        {
            _fields.Raw(key, 0);
            return 0;
        }

        if (byteCount is not (1 or 2 or 4 or 8))
        {
            _fields.Warning(
                DiagnosticCodes.InvalidLength,
                $"Field '{key}' declares an unsupported width of {byteCount} byte(s).",
                CurrentRange);
            return 0;
        }

        if (_reader.Remaining < byteCount)
        {
            Truncated(key);
            return 0;
        }

        var start = _reader.Position;
        ulong value = 0;
        for (var index = 0; index < byteCount; index++)
        {
            value = value << 8 | _reader.ReadByte();
        }

        LastRange = new SourceRange(start, byteCount);
        _fields.Raw(key, value, LastRange);
        return value;
    }

    public byte[] ReadRemainingPrefix(int maxBytes, out long totalLength, out SourceRange range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        var start = _reader.Position;
        totalLength = _reader.Remaining;
        range = new SourceRange(start, totalLength);
        var count = (int)Math.Min(totalLength, maxBytes);
        if (count == 0)
        {
            _reader.Skip(totalLength);
            return [];
        }

        var bytes = new byte[count];
        _reader.ReadExactly(bytes);
        _reader.Skip(totalLength - count);
        LastRange = range;
        return bytes;
    }

    /// <summary>
    /// Consumes exactly <paramref name="length"/> bytes but returns at most
    /// <paramref name="maxBytes"/> of them, so large payloads are reported as a bounded hex prefix.
    /// </summary>
    public byte[] ReadBlock(long length, int maxBytes, out SourceRange range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        if (length > _reader.Remaining)
        {
            Truncated("payload");
            length = _reader.Remaining;
        }

        var start = _reader.Position;
        range = new SourceRange(start, length);
        var count = (int)Math.Min(length, maxBytes);
        var bytes = new byte[count];
        if (count > 0)
        {
            _reader.ReadExactly(bytes);
        }

        _reader.Skip(length - count);
        LastRange = range;
        return bytes;
    }

    /// <summary>Creates a bounded sub-reader over the next <paramref name="length"/> bytes and advances past them.</summary>
    public IsoBmffBoxReader Slice(long length, FieldList fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > _reader.Remaining)
        {
            Truncated("payload");
            length = _reader.Remaining;
        }

        var start = _reader.Position;
        var sub = new IsoBmffBoxReader(_source, start, length, fields);
        _reader.Skip(length);
        LastRange = new SourceRange(start, length);
        return sub;
    }

    public void Note(string message) => _fields.Note(message, CurrentRange);
    public void Warn(string code, string message) => _fields.Warning(code, message, CurrentRange);

    public void Warn(string code, string message, SourceRange range) => _fields.Warning(code, message, range);

    public bool RequireEmpty(string label)
    {
        if (_reader.Remaining <= 0)
        {
            return true;
        }

        _fields.Warning(
            DiagnosticCodes.SchemaMismatch,
            string.Create(CultureInfo.InvariantCulture, $"The {label} has {_reader.Remaining} unexpected trailing byte(s)."),
            CurrentRange);
        SkipRemaining();
        return false;
    }

    private bool Truncated(string key)
    {
        _fields.Warning(
            DiagnosticCodes.TruncatedPayload,
            $"Field '{key}' is truncated.",
            CurrentRange);
        return false;
    }
}
