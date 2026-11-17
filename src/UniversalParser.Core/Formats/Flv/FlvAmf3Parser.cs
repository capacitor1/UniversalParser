using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Flv;

/// <summary>
/// Adobe AMF3 (January 2013). Strings, object instances and traits have separate, per-tag tables.
/// Object references are displayed, not recursively expanded: cycles must never grow a field graph.
/// No ActionScript class is instantiated, and externalizable bodies are left opaque.
/// </summary>
internal sealed class FlvAmf3Parser
{
    private readonly ParseContext _context;
    private readonly SourceReader _reader;
    private readonly FieldList _fields;
    private readonly List<string> _strings = [];
    private readonly List<ObjectReference> _objects = [];
    private readonly List<Traits> _traits = [];
    private int _values;
    private long _stringBytes;

    public FlvAmf3Parser(ParseContext context, SourceReader reader, FieldList fields)
    {
        _context = context;
        _reader = reader;
        _fields = fields;
    }

    public bool Aborted { get; private set; }

    public Value? ReadValue(string path)
    {
        if (!ReserveValue() || !Require(1, "AMF3 type marker")) return null;
        var start = _reader.Position;
        var marker = _reader.ReadByte();
        var complex = false;
        string? text;
        switch (marker)
        {
            case 0x00: text = "undefined"; break;
            case 0x01: text = "null"; break;
            case 0x02: text = "false"; break;
            case 0x03: text = "true"; break;
            case 0x04:
                text = ReadU29(out var integer)
                    ? ((integer & 0x10000000) == 0 ? (long)integer : integer - 0x20000000L).ToString(CultureInfo.InvariantCulture)
                    : null;
                break;
            case 0x05:
                text = ReadDouble(out var number) ? FlvFormatting.FormatNumber(number) : null;
                break;
            case 0x06:
                text = ReadString();
                break;
            case 0x07:
            case 0x0B:
                text = ReadXml(path, marker, start);
                break;
            case 0x08:
                text = ReadDate(path, start);
                break;
            case 0x09:
                text = ReadArray(path, start, out complex);
                break;
            case 0x0A:
                text = ReadObject(path, start, out complex);
                break;
            case 0x0C:
                text = ReadByteArray(path, start);
                break;
            case 0x0D:
            case 0x0E:
            case 0x0F:
            case 0x10:
                text = ReadVector(path, marker, start, out complex);
                break;
            case 0x11:
                text = ReadDictionary(path, start, out complex);
                break;
            default:
                Fail(DiagnosticCodes.InvalidEncoding,
                    string.Create(CultureInfo.InvariantCulture, $"Unknown AMF3 type marker 0x{marker:X2}."),
                    new SourceRange(start, 1));
                return null;
        }

        if (text is null) return null;
        var range = SourceRange.FromBounds(start, _reader.Position);
        var escaped = FlvFormatting.EscapeText(text);
        if (!complex && path.Length > 0) _fields.Raw(path, escaped, range);
        return new Value(complex, escaped, range);
    }

    private string? ReadDate(string path, long start)
    {
        if (!ReadU29(out var header)) return null;
        if ((header & 1) == 0) return Reference(header >> 1);
        RegisterObject("Date", path, start);
        if (!ReadDouble(out var milliseconds)) return null;
        return string.Create(CultureInfo.InvariantCulture,
            $"{FlvFormatting.FormatNumber(milliseconds)} ({FlvFormatting.FormatDateMilliseconds(milliseconds)})");
    }

    private string? ReadXml(string path, byte marker, long start)
    {
        if (!ReadU29(out var header)) return null;
        if ((header & 1) == 0) return Reference(header >> 1);
        RegisterObject(marker == 7 ? "XMLDocument" : "XML", path, start);
        // XML is just counted UTF-8 text. Do not build a DOM or execute entities.
        return ReadUtf8(header >> 1);
    }

    private string? ReadArray(string path, long start, out bool complex)
    {
        complex = false;
        if (!ReadU29(out var header)) return null;
        if ((header & 1) == 0) return Reference(header >> 1);
        complex = true;
        RegisterObject("Array", path, start);
        var denseCount = header >> 1;
        _fields.Derived(DerivedKey(path, "DenseCount"), denseCount, SourceRange.FromBounds(start + 1, _reader.Position));

        ReadAssociativeMembers(path);
        for (var index = 0u; index < denseCount && !Aborted; index++)
        {
            _ = ReadValue(IndexKey(path, index));
        }
        return "array";
    }

    private string? ReadObject(string path, long start, out bool complex)
    {
        complex = false;
        if (!ReadU29(out var header)) return null;
        if ((header & 1) == 0) return Reference(header >> 1);
        complex = true;
        RegisterObject("Object", path, start);

        Traits traits;
        if ((header & 2) == 0)
        {
            var index = header >> 2;
            if (index >= _traits.Count)
            {
                // Without the traits, the number/order of following fields cannot be determined.
                Fail(DiagnosticCodes.InvalidEncoding, $"AMF3 traits reference {index} is unavailable.",
                    SourceRange.FromBounds(start + 1, _reader.Position));
                return null;
            }
            traits = _traits[checked((int)index)];
        }
        else
        {
            var classStart = _reader.Position;
            var className = ReadString();
            if (className is null) return null;
            var classRange = SourceRange.FromBounds(classStart, _reader.Position);
            var externalizable = (header & 4) != 0;
            var dynamic = (header & 8) != 0;
            // For externalizable traits, the upper bits are not a sealed-member count (§3.12).
            var count = externalizable ? 0 : header >> 4;
            if (count > _context.Limits.MaxDataLines)
            {
                Limit(DiagnosticCodes.LimitRecords, $"AMF3 traits declare {count} sealed members.");
                return null;
            }

            var members = new string[checked((int)count)];
            for (var index = 0; index < members.Length; index++)
            {
                _context.ThrowIfCancellationRequested();
                var name = ReadString();
                if (name is null) return null;
                members[index] = name;
            }

            traits = new Traits(className, classRange, externalizable, dynamic, members);
            if (_traits.Count >= _context.Limits.MaxDataLines)
            {
                Limit(DiagnosticCodes.LimitRecords, "AMF3 traits table limit reached.");
                return null;
            }
            _traits.Add(traits);
        }

        if (traits.ClassName.Length > 0)
        {
            _fields.Derived(DerivedKey(path, "ClassName"), FlvFormatting.EscapeText(traits.ClassName), traits.ClassRange);
        }

        if (traits.Externalizable)
        {
            _fields.Note("AMF3 externalizable data has a class-specific, unframed length; its body is not interpreted.",
                _reader.CurrentRange);
            Aborted = true;
            return "externalizable object";
        }

        foreach (var name in traits.SealedMembers)
        {
            if (Aborted) break;
            _ = ReadValue(MemberKey(path, name));
        }
        if (traits.Dynamic && !Aborted) ReadAssociativeMembers(path);
        return "object";
    }

    private void ReadAssociativeMembers(string path)
    {
        while (!Aborted)
        {
            _context.ThrowIfCancellationRequested();
            var name = ReadString();
            if (name is null || name.Length == 0) return;
            _ = ReadValue(MemberKey(path, name));
        }
    }

    private string? ReadByteArray(string path, long start)
    {
        if (!ReadU29(out var header)) return null;
        if ((header & 1) == 0) return Reference(header >> 1);
        RegisterObject("ByteArray", path, start);
        var length = header >> 1;
        _fields.Derived(DerivedKey(path, "ByteLength"), length, SourceRange.FromBounds(start + 1, _reader.Position));
        if (!Require(length, "AMF3 ByteArray")) return null;

        // The bytes are not another AMF graph or a compressed-text stream unless an external
        // schema says so. Read a small raw prefix, then skip by the counted length.
        var previewLength = checked((int)Math.Min(length, Math.Min(64L, _context.Limits.MaxBlobBytes)));
        var bytes = _reader.ReadBytes(previewLength);
        _reader.Skip(length - previewLength);
        return string.Create(CultureInfo.InvariantCulture,
            $"{length} byte(s): {FlvFormatting.FormatHex(bytes, previewLength)}{(length > previewLength ? " ..." : string.Empty)}");
    }

    private string? ReadVector(string path, byte marker, long start, out bool complex)
    {
        complex = false;
        if (!ReadU29(out var header)) return null;
        if ((header & 1) == 0) return Reference(header >> 1);
        complex = true;
        RegisterObject("Vector", path, start);
        var count = header >> 1;
        _fields.Derived(DerivedKey(path, "VectorCount"), count, SourceRange.FromBounds(start + 1, _reader.Position));
        if (!Require(1, "AMF3 fixed-vector")) return null;
        var flagStart = _reader.Position;
        var fixedFlag = _reader.ReadByte();
        _fields.Raw(MemberKey(path, "fixed-vector"), FormatFlag(fixedFlag), new SourceRange(flagStart, 1));
        if (marker == 0x10)
        {
            var typeStart = _reader.Position;
            var typeName = ReadString();
            if (typeName is null) return null;
            _fields.Raw(MemberKey(path, "object-type-name"), FlvFormatting.EscapeText(typeName),
                SourceRange.FromBounds(typeStart, _reader.Position));
        }

        for (var index = 0u; index < count && !Aborted; index++)
        {
            var itemPath = IndexKey(path, index);
            if (marker == 0x10)
            {
                _ = ReadValue(itemPath);
                continue;
            }

            if (!ReserveValue()) break;
            var itemStart = _reader.Position;
            string text;
            if (marker == 0x0F)
            {
                if (!ReadDouble(out var number)) return null;
                text = FlvFormatting.FormatNumber(number);
            }
            else
            {
                if (!Require(4, "AMF3 Vector item")) return null;
                var number = _reader.ReadUInt32(Endian.Big);
                text = marker == 0x0D ? unchecked((int)number).ToString(CultureInfo.InvariantCulture)
                    : number.ToString(CultureInfo.InvariantCulture);
            }
            _fields.Raw(itemPath, text, SourceRange.FromBounds(itemStart, _reader.Position));
        }
        return "vector";
    }

    private string? ReadDictionary(string path, long start, out bool complex)
    {
        complex = false;
        if (!ReadU29(out var header)) return null;
        if ((header & 1) == 0) return Reference(header >> 1);
        complex = true;
        RegisterObject("Dictionary", path, start);
        var count = header >> 1;
        _fields.Derived(DerivedKey(path, "DictionaryCount"), count, SourceRange.FromBounds(start + 1, _reader.Position));
        if (!Require(1, "AMF3 weak-keys")) return null;
        var flagStart = _reader.Position;
        _fields.Raw(MemberKey(path, "weak-keys"), FormatFlag(_reader.ReadByte()), new SourceRange(flagStart, 1));
        for (var index = 0u; index < count && !Aborted; index++)
        {
            _ = ReadValue(IndexKey(path, index) + ".key");
            if (!Aborted) _ = ReadValue(IndexKey(path, index) + ".value");
        }
        return "dictionary";
    }

    private string? ReadString()
    {
        if (Aborted || !ReadU29(out var header)) return null;
        if ((header & 1) == 0)
        {
            var index = header >> 1;
            if (index < _strings.Count) return _strings[checked((int)index)];
            Fail(DiagnosticCodes.InvalidEncoding, $"AMF3 string reference {index} is unavailable.", _reader.CurrentRange);
            return null;
        }

        var length = header >> 1;
        if (length == 0) return string.Empty; // Empty strings NEVER enter the reference table (§3.8).
        if (_strings.Count >= _context.Limits.MaxDataLines)
        {
            Limit(DiagnosticCodes.LimitRecords, "AMF3 string reference table limit reached.");
            return null;
        }
        if (Math.Min(length, _context.Limits.MaxStringBytes) > _context.Limits.MaxBlobBytes - _stringBytes)
        {
            Limit(DiagnosticCodes.LimitBlob, "AMF3 string reference table budget reached.");
            return null;
        }
        var text = ReadUtf8(length);
        if (text is not null)
        {
            _strings.Add(text);
            _stringBytes += Math.Min(length, _context.Limits.MaxStringBytes);
        }
        return text;
    }

    private string? ReadUtf8(uint length)
    {
        if (!Require(length, "AMF3 UTF-8 string")) return null;
        var start = _reader.Position;
        var count = checked((int)Math.Min(length, _context.Limits.MaxStringBytes));
        var bytes = _reader.ReadBytes(count);
        _reader.Skip(length - count);
        var range = new SourceRange(start, length);
        if (length > count)
        {
            _fields.Warning(DiagnosticCodes.LimitString, $"AMF3 text is {length} bytes; only its prefix is shown.", range);
        }
        var text = TextDecoding.Utf8(bytes, bytes.Length == length, out var invalidEncoding);
        if (invalidEncoding)
        {
            _fields.Warning(DiagnosticCodes.InvalidEncoding, "AMF3 text contains invalid UTF-8.", range);
        }
        return text;
    }

    private bool ReadU29(out uint value)
    {
        value = 0;
        for (var index = 0; index < 4; index++)
        {
            if (!Require(1, "AMF3 U29")) return false;
            var next = _reader.ReadByte();
            if (index == 3)
            {
                value = (value << 8) | next;
                return true;
            }
            value = (value << 7) | (uint)(next & 0x7F);
            if ((next & 0x80) == 0) return true;
        }
        return true;
    }

    private bool ReadDouble(out double number)
    {
        number = 0;
        if (!Require(8, "AMF3 double")) return false;
        number = BitConverter.Int64BitsToDouble(unchecked((long)_reader.ReadUInt64(Endian.Big)));
        return true;
    }

    private string Reference(uint index)
    {
        // Object reference correctness is an object-graph semantic, not a byte-layout check.
        // Known targets are described; unknown indices still keep their raw value.
        var target = index < _objects.Count ? _objects[checked((int)index)] : (ObjectReference?)null;
        return target is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"@ref {index} ({value.Type}, {value.Path})")
            : string.Create(CultureInfo.InvariantCulture, $"@ref {index}");
    }

    private void RegisterObject(string type, string path, long start) =>
        _objects.Add(new ObjectReference(type, path.Length == 0 ? $"offset {start}" : path));

    private bool ReserveValue()
    {
        _context.ThrowIfCancellationRequested();
        if (Aborted) return false;
        if (_values >= _context.Limits.MaxDataLines || _fields.IsFull)
        {
            Limit(DiagnosticCodes.LimitRecords, "AMF3 value/field budget reached.");
            return false;
        }
        _values++;
        return true;
    }

    private bool Require(long length, string label)
    {
        if (_reader.Remaining >= length) return true;
        Fail(DiagnosticCodes.TruncatedPayload, $"{label} is truncated.", _reader.CurrentRange);
        return false;
    }

    private void Limit(string code, string message) => Fail(code, message, _reader.CurrentRange);

    private void Fail(string code, string message, SourceRange range)
    {
        if (!Aborted) _fields.Warning(code, message, range);
        Aborted = true;
    }

    private static string MemberKey(string path, string name) => path.Length == 0
        ? FlvFormatting.SanitizeKey(name) : $"{path}.{FlvFormatting.SanitizeKey(name)}";
    private static string DerivedKey(string path, string name) => path.Length == 0 ? $"<{name}>" : $"<{path}.{name}>";
    private static string IndexKey(string path, uint index) => string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]");
    private static string FormatFlag(byte value) => string.Create(CultureInfo.InvariantCulture,
        $"{value} ({(value == 0 ? "false" : value == 1 ? "true" : "unassigned")})");

    internal readonly record struct Value(bool IsComplex, string Text, SourceRange Range);
    private readonly record struct ObjectReference(string Type, string Path);
    private readonly record struct Traits(string ClassName, SourceRange ClassRange, bool Externalizable, bool Dynamic, string[] SealedMembers);
}
