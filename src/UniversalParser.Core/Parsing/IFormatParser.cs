using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Parsing;

/// <summary>
/// A format parser. Probe is cheap and bounded; Parse builds the immutable tree;
/// ParseNode produces fields for one selected node.
/// </summary>
public interface IFormatParser
{
    string Id { get; }
    string DisplayName { get; }
    int ProbePriority { get; }
    bool IsFallback { get; }

    ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken);

    FormatNode Parse(ParseContext context);

    NodeParseResult ParseNode(ParseContext context, FormatNode node) => new(node.Name, node.Range);
}
