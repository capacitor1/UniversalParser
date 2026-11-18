using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Model;

/// <summary>
/// Immutable parse document produced by a format parser.
/// </summary>
public sealed class ParseDocument
{
    public ParseDocument(
        IFormatParser parser,
        FormatNode root,
        ImmutableArray<Diagnostic> diagnostics,
        ParseLimits limits,
        string? fileName,
        long sourceLength,
        ProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceLength);

        if (diagnostics.IsDefault)
        {
            diagnostics = ImmutableArray<Diagnostic>.Empty;
        }

        diagnostics.ThrowIfAnyNull(nameof(diagnostics), "Diagnostics must not contain null entries.");

        if (!new SourceRange(0, sourceLength).Contains(root.Range))
        {
            throw new ArgumentException("Root range must be contained by the source.", nameof(root));
        }

        Parser = parser;
        Root = root;
        Diagnostics = diagnostics;
        Limits = limits;
        FileName = fileName;
        SourceLength = sourceLength;
        Probe = probe;
    }

    public IFormatParser Parser { get; }
    public string ParserId => Parser.Id;
    public string ParserDisplayName => Parser.DisplayName;
    public FormatNode Root { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public ParseLimits Limits { get; }
    public string? FileName { get; }
    public long SourceLength { get; }
    public ProbeResult Probe { get; }
}
