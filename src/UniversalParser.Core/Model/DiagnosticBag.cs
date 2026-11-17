using System.Collections.Immutable;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Model;

/// <summary>
/// Mutable collector used while a parser runs; results expose an immutable snapshot.
/// </summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = [];

    public int Count => _items.Count;
    public IReadOnlyList<Diagnostic> Items => _items;

    public void Add(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _items.Add(diagnostic);
    }

    public void Note(string code, string message, SourceRange? range = null, string? detail = null) =>
        Add(new Diagnostic(DiagnosticSeverity.Note, code, message, range, detail));

    public void Warning(string code, string message, SourceRange? range = null, string? detail = null) =>
        Add(new Diagnostic(DiagnosticSeverity.Warning, code, message, range, detail));

    public void Error(string code, string message, SourceRange? range = null, string? detail = null) =>
        Add(new Diagnostic(DiagnosticSeverity.Error, code, message, range, detail));

    public ImmutableArray<Diagnostic> ToImmutable() => ImmutableArray.CreateRange(_items);
}
