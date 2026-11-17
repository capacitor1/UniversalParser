using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Tests;

internal sealed class TestFormatParser : IFormatParser
{
    private readonly ProbeResult _probe;
    private readonly Func<ParseContext, FormatNode> _parse;
    private readonly Func<ParseContext, FormatNode, NodeParseResult>? _parseNode;

    public TestFormatParser(
        string id,
        ProbeResult probe,
        int priority = 0,
        bool fallback = false,
        Func<ParseContext, FormatNode>? parse = null,
        Func<ParseContext, FormatNode, NodeParseResult>? parseNode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        _probe = probe;
        ProbePriority = priority;
        IsFallback = fallback;
        _parse = parse ?? CreateRoot;
        _parseNode = parseNode;
    }

    public string Id { get; }
    public string DisplayName => Id;
    public int ProbePriority { get; }
    public bool IsFallback { get; }

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        _ = source;
        _ = limits;
        cancellationToken.ThrowIfCancellationRequested();
        return _probe;
    }

    public FormatNode Parse(ParseContext context) => _parse(context);

    public NodeParseResult ParseNode(ParseContext context, FormatNode node) =>
        _parseNode is null ? new NodeParseResult(node.Name, node.Range) : _parseNode(context, node);

    private static FormatNode CreateRoot(ParseContext context)
    {
        var range = new SourceRange(0, context.Source.Length);
        return new FormatNode("Test", NodeKind.Container, range, range);
    }
}
