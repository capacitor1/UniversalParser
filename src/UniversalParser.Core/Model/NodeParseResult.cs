using System.Collections.Immutable;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Model;

/// <summary>
/// Immutable result of parsing one node. RawRange defaults to the node range and is never
/// synthesized from parser error text.
/// </summary>
public sealed class NodeParseResult
{
    public NodeParseResult(
        string title,
        SourceRange range,
        SourceRange? rawRange = null,
        ImmutableArray<Field>? fields = null,
        ImmutableArray<Diagnostic>? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var effectiveRawRange = rawRange ?? range;
        if (!range.Contains(effectiveRawRange))
        {
            throw new ArgumentException("Raw range must be contained by the result range.", nameof(rawRange));
        }

        var fieldArray = fields.GetValueOrDefault();
        if (fieldArray.IsDefault)
        {
            fieldArray = ImmutableArray<Field>.Empty;
        }

        var diagnosticArray = diagnostics.GetValueOrDefault();
        if (diagnosticArray.IsDefault)
        {
            diagnosticArray = ImmutableArray<Diagnostic>.Empty;
        }

        if (fieldArray.Any(static field => field is null))
        {
            throw new ArgumentException("Fields must not contain null entries.", nameof(fields));
        }

        if (diagnosticArray.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Diagnostics must not contain null entries.", nameof(diagnostics));
        }

        Title = title;
        Range = range;
        RawRange = effectiveRawRange;
        Fields = fieldArray;
        Diagnostics = diagnosticArray;
    }

    public string Title { get; }
    public SourceRange Range { get; }
    public SourceRange RawRange { get; }
    public ImmutableArray<Field> Fields { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}
