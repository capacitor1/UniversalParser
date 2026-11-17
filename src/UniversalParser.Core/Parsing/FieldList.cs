using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Parsing;

/// <summary>
/// Ordered field collector shared by format field emitters. Raw fields are emitted immediately; notes and diagnostics are
/// deferred to the end so a parser error cannot push the official field order around.
/// </summary>
internal sealed class FieldList
{
    private readonly int _maxFields;
    private readonly List<Field> _fields = [];
    private readonly List<Field> _deferred = [];
    private readonly List<Diagnostic> _diagnostics = [];
    private bool _limitReported;

    public FieldList(int maxFields)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFields);
        _maxFields = maxFields;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
    public bool IsFull => _fields.Count + _deferred.Count >= _maxFields;

    public void Raw(string key, string value, SourceRange? range = null) =>
        Add(Field.Raw(key, value, range));

    public void Raw(string key, long value, SourceRange? range = null) =>
        Raw(key, value.ToString(CultureInfo.InvariantCulture), range);

    public void Raw(string key, ulong value, SourceRange? range = null) =>
        Raw(key, value.ToString(CultureInfo.InvariantCulture), range);

    public void Raw(string key, uint value, SourceRange? range = null) =>
        Raw(key, value.ToString(CultureInfo.InvariantCulture), range);

    public void Raw(string key, ushort value, SourceRange? range = null) =>
        Raw(key, value.ToString(CultureInfo.InvariantCulture), range);

    public void RawHex(string key, uint value, int digits, SourceRange? range = null) =>
        Raw(key, $"0x{value.ToString($"X{digits}", CultureInfo.InvariantCulture)}", range);

    public void Derived(string key, string value, SourceRange? range = null) =>
        Add(Field.Derived(key, value, range));

    public void Derived(string key, long value, SourceRange? range = null) =>
        Derived(key, value.ToString(CultureInfo.InvariantCulture), range);

    public void Derived(string key, uint value, SourceRange? range = null) =>
        Derived(key, value.ToString(CultureInfo.InvariantCulture), range);

    public void Derived(string key, ulong value, SourceRange? range = null) =>
        Derived(key, value.ToString(CultureInfo.InvariantCulture), range);

    public void Derived(string key, ushort value, SourceRange? range = null) =>
        Derived(key, value.ToString(CultureInfo.InvariantCulture), range);

    public void Note(string message, SourceRange? range = null, string code = DiagnosticCodes.UnparsedPayload) =>
        AddDiagnostic(DiagnosticSeverity.Note, code, message, range);

    public void Warning(string code, string message, SourceRange? range = null) =>
        AddDiagnostic(DiagnosticSeverity.Warning, code, message, range);

    public void Error(string code, string message, SourceRange? range = null) =>
        AddDiagnostic(DiagnosticSeverity.Error, code, message, range);

    public void AddDiagnostic(DiagnosticSeverity severity, string code, string message, SourceRange? range = null)
    {
        _diagnostics.Add(new Diagnostic(severity, code, message, range));
        var field = severity switch
        {
            DiagnosticSeverity.Note => Field.Note(message, range: range),
            DiagnosticSeverity.Warning => Field.Warning(message, range: range),
            _ => Field.Error(message, range: range),
        };
        _deferred.Add(field);
    }

    public void Flush()
    {
        foreach (var field in _deferred)
        {
            if (_fields.Count >= _maxFields)
            {
                break;
            }

            _fields.Add(field);
        }

        _deferred.Clear();
    }

    public ImmutableArray<Field> ToImmutable()
    {
        Flush();
        return [.. _fields];
    }

    private void Add(Field field)
    {
        if (_fields.Count >= _maxFields)
        {
            if (!_limitReported)
            {
                _limitReported = true;
                Warning(DiagnosticCodes.LimitRecords, "Field limit reached; remaining entries are omitted.");
            }

            return;
        }

        _fields.Add(field);
    }
}
