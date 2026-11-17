using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Protobuf;

/// <summary>
/// Raw Protocol Buffers parser. Protobuf has no file magic, so probing validates a bounded
/// protobuf field structure: a complete structure is a weak match, and text/nested-message
/// evidence with at least two fields is a strong match. Magic-based formats keep their higher
/// probe priority. No .proto descriptor is loaded.
/// </summary>
public sealed class ProtobufParser : IFormatParser
{
    public const string IdValue = ProtobufConstants.ParserId;
    public const string DisplayNameValue = ProtobufConstants.DisplayName;

    public string Id => IdValue;
    public string DisplayName => DisplayNameValue;
    public int ProbePriority => 90;
    public bool IsFallback => false;

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        if (source.Length == 0)
        {
            return ProbeResult.NoMatch;
        }

        var window = (long)Math.Min(source.Length, ParseThresholds.LargeOperationBytes);
        if (!ProtobufMessageScanner.TryScan(
                source,
                0,
                window,
                cancellationToken,
                inspectPayloads: true,
                allowIncompleteTail: window < source.Length,
                out var stats))
        {
            return ProbeResult.NoMatch;
        }

        var complete = window == source.Length;
        var hasStringEvidence = stats.TextPayloadCount > 0;
        var hasNestedEvidence = stats.NestedPayloadCount > 0;
        var hasPayloadEvidence = hasStringEvidence || hasNestedEvidence;
        var hasIncompleteWindowTail = !complete && stats.IncompleteTail;
        var description = string.Create(
            CultureInfo.InvariantCulture,
            $"protobuf wire structure: {stats.FieldCount} field(s), {stats.LengthDelimitedCount} length-delimited payload(s).");

        if (complete && stats.FieldCount >= 2 && hasPayloadEvidence)
        {
            return ProbeResult.Strong(description);
        }

        if (complete || hasPayloadEvidence || hasIncompleteWindowTail)
        {
            return ProbeResult.Weak(description);
        }

        return ProbeResult.NoMatch;
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        return new ProtobufDocumentParser(context).Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return ProtobufNodeParser.Parse(context, node);
    }
}
