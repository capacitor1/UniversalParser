using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Raw;

/// <summary>
/// Fallback parser. It exposes the entire file as a single raw Data node without reading it.
/// </summary>
public sealed class RawParser : IFormatParser
{
    public const string IdValue = "raw";

    public string Id => IdValue;
    public string DisplayName => "Raw Data";
    public int ProbePriority => int.MinValue;
    public bool IsFallback => true;

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        _ = source;
        _ = limits;
        cancellationToken.ThrowIfCancellationRequested();
        return ProbeResult.NoMatch;
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();

        var range = new SourceRange(0, context.Source.Length);
        var data = new FormatNode("Data", NodeKind.Raw, range, range, formatId: Id, stableId: "raw-data");
        return new FormatNode(
            "RawData",
            NodeKind.Container,
            range,
            range,
            ImmutableArray.Create(data),
            Id,
            "raw-root");
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();

        var length = node.Range.Length.ToString(CultureInfo.InvariantCulture);
        return node.Name switch
        {
            "RawData" => new NodeParseResult(
                "RawData",
                node.Range,
                node.Range,
                ImmutableArray.Create(
                    Field.Derived("<FileSize>", length),
                    Field.Note("No known container format matched; the entire file is shown as raw data."))),
            "Data" => new NodeParseResult(
                "Data",
                node.Range,
                node.Range,
                ImmutableArray.Create(
                    Field.Derived("<RawLength>", length),
                    Field.Note("Raw data is not parsed."))),
            _ => new NodeParseResult(
                node.Name,
                node.Range,
                node.Range,
                ImmutableArray.Create(Field.Note("Unknown raw node."))),
        };
    }
}
