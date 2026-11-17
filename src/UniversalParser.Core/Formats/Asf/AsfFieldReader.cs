using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Asf;

/// <summary>
/// Bounded reader that emits one raw field per successful read and converts truncation into
/// warnings instead of exceptions. All lengths are checked against the current region.
/// </summary>
internal sealed class AsfFieldReader
{
    private readonly SourceReader _reader;
    private readonly FieldList _fields;
    private readonly ParseLimits _limits;

    public AsfFieldReader(SourceReader reader, FieldList fields, ParseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(limits);
        _reader = reader;
        _fields = fields;
        _limits = limits;
        LastRange = SourceRange.Empty;
    }

    public long Position => _reader.Position;
    public long Remaining => _reader.Remaining;
    public SourceRange CurrentRange => _reader.CurrentRange;
    public SourceRange LastRange { get; private set; }

    public bool TryByte(string key, out byte value) => TryByte(key, out value, format: null);

    public bool TryByte(string key, out byte value, Func<byte, string>? format)
    {
        var start = _reader.Position;
        if (!Ensure(1, key))
        {
            value = 0;
            return false;
        }

        value = _reader.ReadByte();
        LastRange = new SourceRange(start, 1);
        _fields.Raw(key, format is null ? value.ToString(CultureInfo.InvariantCulture) : format(value), LastRange);
        return true;
    }

    public bool TryUInt16(string key, out ushort value, Func<ushort, string>? format = null)
    {
        var start = _reader.Position;
        if (!Ensure(2, key))
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt16();
        LastRange = new SourceRange(start, 2);
        _fields.Raw(key, format is null ? value.ToString(CultureInfo.InvariantCulture) : format(value), LastRange);
        return true;
    }

    public bool TryUInt32(string key, out uint value, Func<uint, string>? format = null)
    {
        var start = _reader.Position;
        if (!Ensure(4, key))
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt32();
        LastRange = new SourceRange(start, 4);
        _fields.Raw(key, format is null ? value.ToString(CultureInfo.InvariantCulture) : format(value), LastRange);
        return true;
    }

    public bool TryUInt64(string key, out ulong value)
    {
        var start = _reader.Position;
        if (!Ensure(8, key))
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt64();
        LastRange = new SourceRange(start, 8);
        _fields.Raw(key, value, LastRange);
        return true;
    }

    public bool TryInt32(string key, out int value)
    {
        var start = _reader.Position;
        if (!Ensure(4, key))
        {
            value = 0;
            return false;
        }

        value = unchecked((int)_reader.ReadUInt32());
        LastRange = new SourceRange(start, 4);
        _fields.Raw(key, value, LastRange);
        return true;
    }

    public bool TryGuid(string key, out Guid value, Func<Guid, string>? format = null)
    {
        var start = _reader.Position;
        if (!Ensure(16, key))
        {
            value = Guid.Empty;
            return false;
        }

        var bytes = _reader.ReadBytes(16);
        value = new Guid(bytes);
        LastRange = new SourceRange(start, 16);
        _fields.Raw(key, format is null ? AsfFormatting.FormatGuid(value) : format(value), LastRange);
        return true;
    }

    /// <summary>Reads a GUID without emitting a field; used when the title already shows it.</summary>
    public bool TryGuidRaw(out Guid value)
    {
        if (!Ensure(16, "Object GUID"))
        {
            value = Guid.Empty;
            return false;
        }

        var start = _reader.Position;
        var bytes = _reader.ReadBytes(16);
        value = new Guid(bytes);
        LastRange = new SourceRange(start, 16);
        return true;
    }

    /// <summary>
    /// Reads a UTF-16LE string whose length is declared in bytes. Odd, truncated, and oversized
    /// strings are clamped, warned about, and skipped without leaving the reader inside the field.
    /// </summary>
    public bool TryUtf16(string key, long byteLength)
    {
        var start = _reader.Position;
        var available = _reader.Remaining;
        var actual = Math.Min(byteLength, available);
        if (byteLength > available)
        {
            Warning(DiagnosticCodes.TruncatedPayload, $"{key} declares {byteLength} byte(s) but only {available} remain.", start, available);
        }

        if (available > 0 && actual == 0 && byteLength > 0)
        {
            return false;
        }

        var readable = actual - (actual & 1);
        if ((actual & 1) != 0)
        {
            Warning(DiagnosticCodes.InvalidLength, $"{key} has an odd byte length ({actual}); the final byte is skipped.", start, actual);
        }

        var maxBytes = Math.Max(2, _limits.MaxStringBytes & ~1);
        var readCount = (int)Math.Min(readable, maxBytes);
        var bytes = readCount == 0 ? [] : _reader.ReadBytes(readCount);
        var consumed = actual;
        var skip = consumed - readCount;
        if (skip > 0)
        {
            _reader.Skip(skip);
        }

        var range = new SourceRange(start, consumed);
        _fields.Raw(key, AsfFormatting.FormatText(bytes), range);
        LastRange = range;
        if (readable > readCount)
        {
            Warning(
                DiagnosticCodes.LimitString,
                $"{key} is {readable} byte(s); only the first {readCount} are shown.",
                start,
                consumed);
        }

        return true;
    }

    /// <summary>
    /// Reads up to <paramref name="maxRead"/> bytes of a declared value and advances past the
    /// rest. The returned range always spans the consumed value.
    /// </summary>
    public byte[] ReadValue(string key, long byteLength, int maxRead, out SourceRange range)
    {
        var start = _reader.Position;
        var available = _reader.Remaining;
        var actual = Math.Min(byteLength, available);
        if (byteLength > available)
        {
            Warning(DiagnosticCodes.TruncatedPayload, $"{key} declares {byteLength} byte(s) but only {available} remain.", start, available);
        }

        var readCount = (int)Math.Min(actual, Math.Max(0, maxRead));
        var bytes = readCount == 0 ? [] : _reader.ReadBytes(readCount);
        var skip = actual - readCount;
        if (skip > 0)
        {
            _reader.Skip(skip);
        }

        range = new SourceRange(start, actual);
        LastRange = range;
        return bytes;
    }

    public bool TryHex(string key, long byteLength, int maxPreview)
    {
        var bytes = ReadValue(key, byteLength, maxPreview, out var range);
        _fields.Raw(key, AsfFormatting.FormatHex(bytes, maxPreview), range);
        if (range.Length > bytes.Length)
        {
            _fields.Note($"{key} shows the first {bytes.Length} of {range.Length} byte(s).", range);
        }

        return true;
    }

    public bool Skip(long count, string what)
    {
        if (count > _reader.Remaining)
        {
            Warning(DiagnosticCodes.TruncatedPayload, $"{what} declares {count} byte(s) but only {_reader.Remaining} remain.", _reader.Position, _reader.Remaining);
            _reader.Skip(_reader.Remaining);
            return false;
        }

        _reader.Skip(count);
        return true;
    }

    public void Warning(string code, string message, long offset, long length) =>
        _fields.Warning(code, message, new SourceRange(offset, length));

    private bool Ensure(int count, string key)
    {
        if (_reader.Remaining >= count)
        {
            return true;
        }

        Warning(DiagnosticCodes.TruncatedPayload, $"The {key} field is truncated.", _reader.Position, _reader.Remaining);
        return false;
    }
}
