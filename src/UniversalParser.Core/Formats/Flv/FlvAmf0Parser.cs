using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Flv;

/// <summary>
/// Bounded AMF0 reader for FLV script tags and Enhanced FLV metadata frames. It emits flat,
/// path-based fields (<c>onMetaData.duration</c>, <c>keyframes.times[0]</c>) and never decodes
/// media payloads.
/// </summary>
internal sealed class FlvAmf0Parser
{
    private readonly ParseContext _context;
    private readonly SourceReader _reader;
    private readonly FieldList _fields;
    private int _valueCount;
    private bool _aborted;
    private bool _limitReported;
    private FlvAmf3Parser? _amf3;
    private string _metadataPrefix = string.Empty;

    private FlvAmf0Parser(ParseContext context, SourceReader reader, FieldList fields)
    {
        _context = context;
        _reader = reader;
        _fields = fields;
    }

    public static void ParseScript(ParseContext context, SourceReader reader, FieldList fields)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(fields);
        new FlvAmf0Parser(context, reader, fields).ParseScriptBody();
    }

    public static void ParseMetadata(ParseContext context, SourceReader reader, FieldList fields, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(fields);
        new FlvAmf0Parser(context, reader, fields) { _metadataPrefix = prefix }.ParseMetadataBody();
    }

    private void ParseScriptBody()
    {
        if (_reader.Remaining == 0)
        {
            _fields.Note("The script tag body is empty.", _reader.CurrentRange);
            return;
        }

        var method = ReadValue(string.Empty);
        if (method is not { } name || name.IsComplex)
        {
            if (!_aborted)
            {
                _fields.Warning(
                    DiagnosticCodes.InvalidEncoding,
                    "The script tag does not start with an AMF0 string method name.",
                    _reader.CurrentRange);
            }

            return;
        }

        _fields.Raw("MethodName", name.Text, name.Range);
        if (_aborted)
        {
            return;
        }

        var argumentIndex = 0;
        while (_reader.Remaining > 0 && !_aborted)
        {
            _context.ThrowIfCancellationRequested();
            var value = ReadValue(string.Empty);
            if (value is not { } argument)
            {
                break;
            }

            if (!argument.IsComplex)
            {
                _fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"Argument{argumentIndex}"),
                    argument.Text,
                    argument.Range);
            }

            argumentIndex++;
        }
    }

    private void ParseMetadataBody()
    {
        Span<byte> firstByte = stackalloc byte[1];
        var typedNames = _reader.Remaining > 0
            && SourceRead.ReadAtMost(_context.Source, _reader.Position, firstByte) == 1 && firstByte[0] == 0x02;
        while (_reader.Remaining > 0 && !_aborted)
        {
            _context.ThrowIfCancellationRequested();
            if (typedNames)
            {
                var property = ReadValue(string.Empty);
                if (property is not { IsComplex: false } value || _aborted) return;
                _ = ReadValue(CombinePath(_metadataPrefix, FlvFormatting.SanitizeKey(value.Text)));
                continue;
            }

            // Retain the legacy length-only property-name form accepted by earlier builds.
            if (_reader.Remaining < 2)
            {
                _fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    "The metadata frame ends in the middle of a property name length.",
                    _reader.CurrentRange);
                return;
            }

            var name = ReadString(_reader.ReadUInt16(Endian.Big), _reader.Position - 2);
            if (name is null)
            {
                return;
            }

            var key = CombinePath(_metadataPrefix, FlvFormatting.SanitizeKey(name));
            _ = ReadValue(key);
        }
    }

    private AmfValue? ReadValue(string path)
    {
        _context.ThrowIfCancellationRequested();
        if (_aborted)
        {
            return null;
        }

        _valueCount++;
        if (_valueCount > _context.Limits.MaxDataLines || _fields.IsFull)
        {
            ReportLimit(
                DiagnosticCodes.LimitRecords,
                $"AMF0 value limit ({_context.Limits.MaxDataLines}) reached.",
                _reader.CurrentRange);
            return null;
        }

        if (_reader.Remaining < 1)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The AMF0 value is missing its type marker.",
                _reader.CurrentRange);
            _aborted = true;
            return null;
        }

        var markerOffset = _reader.Position;
        var marker = _reader.ReadByte();
        var range = new SourceRange(markerOffset, 1);
        switch (marker)
        {
            case 0x00:
                return AddPrimitive(path, ReadNumber(), range);
            case 0x01:
                return AddPrimitive(path, ReadBoolean(), range);
            case 0x02:
                if (_reader.Remaining < 2)
                {
                    _fields.Warning(
                        DiagnosticCodes.TruncatedPayload,
                        "The AMF0 string is missing its length.",
                        _reader.CurrentRange);
                    _aborted = true;
                    return null;
                }

                return AddPrimitive(path, ReadString(_reader.ReadUInt16(Endian.Big), _reader.Position - 2), range);
            case 0x03:
                ReadObject(path);
                return new AmfValue(true, "object", range);
            case 0x05:
                return AddPrimitive(path, "null", range);
            case 0x06:
                return AddPrimitive(path, "undefined", range);
            case 0x07:
                return AddPrimitive(path, ReadReference(), range);
            case 0x08:
                ReadEcmaArray(path);
                return new AmfValue(true, "ECMA array", range);
            case 0x0A:
                ReadStrictArray(path);
                return new AmfValue(true, "strict array", range);
            case 0x0B:
                return AddPrimitive(path, ReadDate(), range);
            case 0x0C:
                return AddPrimitive(path, ReadLongString(), range);
            case 0x0F:
                return AddPrimitive(path, ReadXmlDocument(), range);
            case 0x10:
                ReadTypedObject(path);
                return new AmfValue(true, "typed object", range);
            case 0x11:
                _amf3 ??= new FlvAmf3Parser(_context, _reader, _fields);
                var value = _amf3.ReadValue(path);
                if (_amf3.Aborted)
                {
                    _aborted = true;
                }

                return value is { } amf3Value ? new AmfValue(amf3Value.IsComplex, amf3Value.Text, amf3Value.Range) : null;
            default:
                _fields.Warning(
                    DiagnosticCodes.InvalidEncoding,
                    string.Create(CultureInfo.InvariantCulture, $"Unknown AMF0 type marker 0x{marker:X2}."),
                    range);
                _aborted = true;
                return null;
        }
    }

    private void ReadObject(string path)
    {
        while (!_aborted)
        {
            _context.ThrowIfCancellationRequested();
            if (_reader.Remaining < 2)
            {
                _fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    "The AMF0 object ends in the middle of a property name length.",
                    _reader.CurrentRange);
                _aborted = true;
                return;
            }

            var lengthOffset = _reader.Position;
            var nameLength = _reader.ReadUInt16(Endian.Big);
            if (nameLength == 0)
            {
                if (_reader.Remaining < 1)
                {
                    _fields.Warning(
                        DiagnosticCodes.TruncatedPayload,
                        "The AMF0 object is missing its end marker.",
                        new SourceRange(lengthOffset, 2));
                    _aborted = true;
                    return;
                }

                var endMarker = _reader.ReadByte();
                if (endMarker != 0x09)
                {
                    _fields.Warning(
                        DiagnosticCodes.InvalidEncoding,
                        string.Create(CultureInfo.InvariantCulture, $"Expected AMF0 object end marker 0x09 but found 0x{endMarker:X2}."),
                        new SourceRange(_reader.Position - 1, 1));
                    _aborted = true;
                }

                return;
            }

            var name = ReadString(nameLength, lengthOffset);
            if (name is null)
            {
                return;
            }

            var key = CombinePath(path, FlvFormatting.SanitizeKey(name));
            _ = ReadValue(key);
        }
    }

    private void ReadEcmaArray(string path)
    {
        if (_reader.Remaining < 4)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The AMF0 ECMA array is missing its count.",
                _reader.CurrentRange);
            _aborted = true;
            return;
        }

        var count = _reader.ReadUInt32(Endian.Big);
        if (count > _context.Limits.MaxDataLines)
        {
            _fields.Warning(
                DiagnosticCodes.LimitRecords,
                $"The AMF0 ECMA array declares {count} entries; only the object end marker determines the actual end.",
                _reader.CurrentRange);
        }

        ReadObject(path);
    }

    private void ReadStrictArray(string path)
    {
        if (_reader.Remaining < 4)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The AMF0 strict array is missing its count.",
                _reader.CurrentRange);
            _aborted = true;
            return;
        }

        var count = _reader.ReadUInt32(Endian.Big);
        if (count > _context.Limits.MaxDataLines)
        {
            _fields.Warning(
                DiagnosticCodes.LimitRecords,
                $"The AMF0 strict array declares {count} entries; parsing stops at {_context.Limits.MaxDataLines}.",
                _reader.CurrentRange);
        }

        var limit = Math.Min(count, (uint)_context.Limits.MaxDataLines);
        for (var index = 0u; index < limit && !_aborted; index++)
        {
            _ = ReadValue(string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]"));
        }
        if (count > limit)
        {
            _aborted = true; // Remaining values have not been consumed; never treat them as the next AMF0 value.
        }
    }

    private void ReadTypedObject(string path)
    {
        if (_reader.Remaining < 2)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The AMF0 typed object is missing its class name length.",
                _reader.CurrentRange);
            _aborted = true;
            return;
        }

        var lengthOffset = _reader.Position;
        var className = ReadString(_reader.ReadUInt16(Endian.Big), lengthOffset);
        if (className is null)
        {
            return;
        }

        _fields.Derived(
            CombinePath(path, "<ClassName>"),
            FlvFormatting.EscapeText(className),
            new SourceRange(lengthOffset, _reader.Position - lengthOffset));
        ReadObject(path);
    }

    private AmfValue AddPrimitive(string path, string? value, SourceRange range)
    {
        if (value is null)
        {
            return new AmfValue(false, "<truncated>", range);
        }

        var text = FlvFormatting.EscapeText(value);
        if (path.Length > 0)
        {
            _fields.Raw(path, text, range);
        }

        return new AmfValue(false, text, range);
    }

    private string ReadNumber()
    {
        if (_reader.Remaining < 8)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The AMF0 number is truncated.",
                _reader.CurrentRange);
            _aborted = true;
            return "<truncated>";
        }

        var bits = _reader.ReadUInt64(Endian.Big);
        return FlvFormatting.FormatNumber(BitConverter.Int64BitsToDouble(unchecked((long)bits)));
    }

    private string ReadBoolean()
    {
        if (_reader.Remaining < 1)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The AMF0 boolean is truncated.",
                _reader.CurrentRange);
            _aborted = true;
            return "<truncated>";
        }

        return _reader.ReadByte() == 0 ? "false" : "true";
    }

    private string ReadReference()
    {
        if (_reader.Remaining < 2)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The AMF0 reference is truncated.",
                _reader.CurrentRange);
            _aborted = true;
            return "<truncated>";
        }

        return string.Create(CultureInfo.InvariantCulture, $"@ref {_reader.ReadUInt16(Endian.Big)}");
    }

    private string ReadDate()
    {
        if (_reader.Remaining < 10)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The AMF0 date is truncated.",
                _reader.CurrentRange);
            _aborted = true;
            return "<truncated>";
        }

        var bits = _reader.ReadUInt64(Endian.Big);
        var milliseconds = BitConverter.Int64BitsToDouble(unchecked((long)bits));
        var timezone = _reader.ReadUInt16(Endian.Big);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{FlvFormatting.FormatNumber(milliseconds)} ({FlvFormatting.FormatDateMilliseconds(milliseconds)}; timezone {timezone})");
    }

    private string? ReadLongString()
    {
        if (_reader.Remaining < 4)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The AMF0 long string is missing its length.",
                _reader.CurrentRange);
            _aborted = true;
            return null;
        }

        var lengthOffset = _reader.Position;
        var length = _reader.ReadUInt32(Endian.Big);
        if (length > int.MaxValue)
        {
            _fields.Warning(
                DiagnosticCodes.LengthOverflow,
                $"The AMF0 long string declares {length} bytes, which exceeds the supported range.",
                new SourceRange(lengthOffset, 4));
            _aborted = true;
            return null;
        }

        return ReadString((int)length, lengthOffset);
    }

    private string? ReadXmlDocument()
    {
        if (_reader.Remaining < 4)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The AMF0 XML document is missing its length.",
                _reader.CurrentRange);
            _aborted = true;
            return null;
        }

        var lengthOffset = _reader.Position;
        var length = _reader.ReadUInt32(Endian.Big);
        if (length > int.MaxValue)
        {
            _fields.Warning(
                DiagnosticCodes.LengthOverflow,
                $"The AMF0 XML document declares {length} bytes, which exceeds the supported range.",
                new SourceRange(lengthOffset, 4));
            _aborted = true;
            return null;
        }

        return ReadString((int)length, lengthOffset);
    }

    private string? ReadString(int length, long lengthOffset)
    {
        if (length < 0 || length > _reader.Remaining)
        {
            _fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                string.Create(CultureInfo.InvariantCulture, $"The AMF0 string declares {length} bytes but only {_reader.Remaining} are available."),
                SourceRange.FromBounds(lengthOffset, _reader.Position));
            _aborted = true;
            return null;
        }

        if (length > _context.Limits.MaxStringBytes)
        {
            _reader.Skip(length);
            _fields.Warning(
                DiagnosticCodes.LimitString,
                $"The AMF0 string is {length} bytes, which exceeds the {_context.Limits.MaxStringBytes}-byte limit; it was skipped.",
                new SourceRange(lengthOffset, length));
            return "<string limit reached>";
        }

        if (length == 0)
        {
            return string.Empty;
        }

        var bytes = _reader.ReadBytes(length);
        return TextDecoding.Display(bytes, TextEncodings.Utf8);
    }

    private void ReportLimit(string code, string message, SourceRange range)
    {
        if (_limitReported)
        {
            _aborted = true;
            return;
        }

        _limitReported = true;
        _fields.Warning(code, message, range);
        _aborted = true;
    }

    private static string CombinePath(string path, string key) =>
        path.Length == 0 ? key : $"{path}.{key}";

    private readonly record struct AmfValue(bool IsComplex, string Text, SourceRange Range);
}
