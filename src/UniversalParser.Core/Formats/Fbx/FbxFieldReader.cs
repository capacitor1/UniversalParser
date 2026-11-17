using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Fbx;

/// <summary>
/// Bounded little-endian reader for FBX node headers and properties. Values are returned to the
/// caller, which decides how to display them; truncation becomes a warning instead of an exception.
/// </summary>
internal sealed class FbxFieldReader
{
    private readonly SourceReader _reader;
    private readonly FieldList _fields;

    public FbxFieldReader(SourceReader reader, FieldList fields)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(fields);
        _reader = reader;
        _fields = fields;
        LastRange = SourceRange.Empty;
    }

    public long Position => _reader.Position;
    public long Remaining => _reader.Remaining;
    public SourceRange LastRange { get; private set; }

    public bool TryByte(out byte value)
    {
        var start = _reader.Position;
        if (!Ensure(1))
        {
            value = 0;
            return false;
        }

        value = _reader.ReadByte();
        LastRange = new SourceRange(start, 1);
        return true;
    }

    public bool TryUInt16(out ushort value)
    {
        var start = _reader.Position;
        if (!Ensure(2))
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt16();
        LastRange = new SourceRange(start, 2);
        return true;
    }

    public bool TryInt16(out short value)
    {
        var start = _reader.Position;
        if (!Ensure(2))
        {
            value = 0;
            return false;
        }

        value = unchecked((short)_reader.ReadUInt16());
        LastRange = new SourceRange(start, 2);
        return true;
    }

    public bool TryUInt32(out uint value)
    {
        var start = _reader.Position;
        if (!Ensure(4))
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt32();
        LastRange = new SourceRange(start, 4);
        return true;
    }

    public bool TryInt32(out int value)
    {
        var start = _reader.Position;
        if (!Ensure(4))
        {
            value = 0;
            return false;
        }

        value = unchecked((int)_reader.ReadUInt32());
        LastRange = new SourceRange(start, 4);
        return true;
    }

    public bool TryUInt64(out ulong value)
    {
        var start = _reader.Position;
        if (!Ensure(8))
        {
            value = 0;
            return false;
        }

        value = _reader.ReadUInt64();
        LastRange = new SourceRange(start, 8);
        return true;
    }

    public bool TryInt64(out long value)
    {
        var start = _reader.Position;
        if (!Ensure(8))
        {
            value = 0;
            return false;
        }

        value = unchecked((long)_reader.ReadUInt64());
        LastRange = new SourceRange(start, 8);
        return true;
    }

    public bool TrySingle(out float value)
    {
        var start = _reader.Position;
        if (!Ensure(4))
        {
            value = 0;
            return false;
        }

        value = BitConverter.Int32BitsToSingle(unchecked((int)_reader.ReadUInt32()));
        LastRange = new SourceRange(start, 4);
        return true;
    }

    public bool TryDouble(out double value)
    {
        var start = _reader.Position;
        if (!Ensure(8))
        {
            value = 0;
            return false;
        }

        value = BitConverter.Int64BitsToDouble(unchecked((long)_reader.ReadUInt64()));
        LastRange = new SourceRange(start, 8);
        return true;
    }

    public bool TryBytes(long count, out byte[] bytes)
    {
        var start = _reader.Position;
        if (count < 0 || count > int.MaxValue)
        {
            Warning(DiagnosticCodes.InvalidLength, $"{count} byte(s) cannot be read into one buffer.", start, Remaining);
            bytes = [];
            return false;
        }

        if (!Ensure((int)count))
        {
            bytes = [];
            return false;
        }

        bytes = _reader.ReadBytes((int)count);
        LastRange = new SourceRange(start, count);
        return true;
    }

    /// <summary>Reads a byte range without copying more than <paramref name="maxBytes"/>.</summary>
    public byte[] ReadPrefix(long count, int maxBytes, out SourceRange range)
    {
        var start = _reader.Position;
        var available = _reader.Remaining;
        var actual = Math.Min(count, available);
        if (count > available)
        {
            Warning(
                DiagnosticCodes.TruncatedPayload,
                $"The value declares {count} byte(s) but only {available} remain.",
                start,
                available);
        }

        var readCount = (int)Math.Min(actual, Math.Max(0, maxBytes));
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

    public bool Skip(long count)
    {
        if (count < 0)
        {
            Warning(DiagnosticCodes.InvalidLength, "A negative skip length was requested.", Position, Remaining);
            return false;
        }

        if (count > Remaining)
        {
            Warning(
                DiagnosticCodes.TruncatedPayload,
                $"The value declares {count} byte(s) but only {Remaining} remain.",
                Position,
                Remaining);
            _reader.Skip(Remaining);
            return false;
        }

        _reader.Skip(count);
        return true;
    }

    private bool Ensure(int count)
    {
        if (Remaining >= count)
        {
            return true;
        }

        Warning(
            DiagnosticCodes.TruncatedPayload,
            $"A field needs {count} byte(s) but only {Remaining} remain.",
            Position,
            Remaining);
        return false;
    }

    private void Warning(string code, string message, long offset, long length) =>
        _fields.Warning(code, message, SourceRange.FromBounds(offset, offset + length));
}
