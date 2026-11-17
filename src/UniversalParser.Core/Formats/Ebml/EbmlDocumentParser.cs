using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Ebml;

/// <summary>
/// Builds the EBML element tree from headers only. Payload values are produced lazily by
/// <see cref="EbmlNodeParser"/>; unknown-sized masters follow RFC 8794 section 6.2.
/// Block payloads are high-entropy data and stay unparsed.
/// </summary>
internal sealed class EbmlDocumentParser : IDisposable
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly BufferedReadSource? _window;
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<ulong, string> _stableIds = [];

    public EbmlDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _window = context.Source.Length >= BufferedReadSource.MinimumLength
            ? new BufferedReadSource(context.Source)
            : null;
        _source = _window ?? context.Source;
        _diagnostics = context.Diagnostics;
    }

    public void Dispose() => _window?.Dispose();

    public FormatNode Parse()
    {
        var fileRange = new SourceRange(0, _source.Length);
        var sequence = ParseSequence(0, _source.Length, parentId: null, stopAtSibling: false);
        return new FormatNode(
            "Extensible Binary Meta Language",
            NodeKind.Container,
            fileRange,
            fileRange,
            sequence.Children,
            EbmlParser.IdValue,
            "ebml-root");
    }

    private SequenceResult ParseSequence(long start, long end, ulong? parentId, bool stopAtSibling)
    {
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var position = start;
        var checkCounter = 0;

        while (position < end)
        {
            // Cancellation and progress are cheap enough per element, but on multi-million
            // element files checking every 256 elements avoids redundant clock lookups.
            if ((checkCounter++ & 0xFF) == 0)
            {
                _context.ThrowIfCancellationRequested();
                _context.ReportProgress("EBML", position, _source.Length);
            }

            if (end - position < 2)
            {
                AddUnparsed(children, position, end, "<trailing data>", DiagnosticCodes.TruncatedHeader,
                    $"{end - position} trailing byte(s) cannot form an EBML element header.");
                return new SequenceResult(children.ToImmutable(), end);
            }

            if (!EbmlRead.TryReadHeader(_source, position, end, out var header, out var failure))
            {
                var (code, message) = DescribeFailure(failure, position);
                AddUnparsed(children, position, end, "<unparsed data>", code, message);
                return new SequenceResult(children.ToImmutable(), end);
            }

            if (stopAtSibling && EbmlSchema.EndsUnknownSizedElement(parentId, header.Id))
            {
                return new SequenceResult(children.ToImmutable(), position);
            }

            var node = BuildNode(header, end, parentId, out var nodeEnd);
            if (nodeEnd <= position)
            {
                AddUnparsed(children, position, end, "<unparsed data>", DiagnosticCodes.InvalidLength,
                    "The element end does not advance past its header.");
                return new SequenceResult(children.ToImmutable(), end);
            }

            children.Add(node);
            position = nodeEnd;
        }

        return new SequenceResult(children.ToImmutable(), position);
    }

    private FormatNode BuildNode(
        EbmlElementHeader header,
        long parentEnd,
        ulong? parentId,
        out long nodeEnd)
    {
        nodeEnd = header.Offset;
        var known = EbmlSchema.TryGet(header.Id, out var definition);
        var isMaster = known && definition.Type == ElementType.Master;
        var payloadStart = header.PayloadOffset;
        if (payloadStart > parentEnd)
        {
            // TryReadHeader is bounded by parentEnd, so this is defensive only.
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedHeader,
                "The element header extends beyond its parent range.",
                SourceRange.FromBounds(header.Offset, parentEnd));
            payloadStart = parentEnd;
        }

        long payloadEnd;
        ImmutableArray<FormatNode> children = [];

        if (isMaster)
        {
            if (header.IsUnknownSize)
            {
                if (header.Id is not (EbmlSchema.SegmentId or EbmlSchema.ClusterId))
                {
                    _diagnostics.Warning(
                        DiagnosticCodes.UnknownSize,
                        $"Element '{definition.Name}' is unknown-sized but the schema does not allow it; ending at the parent boundary.",
                        SourceRange.FromBounds(header.Offset, parentEnd));
                }

                // The parent ID is deliberately passed through: the stop set belongs to the
                // unknown-sized element's parent, not to the element itself.
                var sequence = ParseSequence(payloadStart, parentEnd, parentId, stopAtSibling: true);
                children = sequence.Children;
                payloadEnd = sequence.End;
            }
            else
            {
                var declaredEnd = header.PayloadEnd!.Value;
                payloadEnd = Math.Min(declaredEnd, parentEnd);
                if (declaredEnd > parentEnd)
                {
                    _diagnostics.Warning(
                        DiagnosticCodes.TruncatedPayload,
                        $"Element '{definition.Name}' declares {header.DataSize} byte(s) but only {parentEnd - payloadStart} are available.",
                        SourceRange.FromBounds(header.Offset, parentEnd));
                }

                var sequence = ParseSequence(payloadStart, payloadEnd, header.Id, stopAtSibling: false);
                children = sequence.Children;
            }
        }
        else
        {
            var elementName = known ? definition.Name : "UnknownElement";
            payloadEnd = header.IsUnknownSize ? parentEnd : Math.Min(header.PayloadEnd!.Value, parentEnd);
            if (header.PayloadEnd is { } declaredEnd && declaredEnd > parentEnd)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"Element '{elementName}' declares {header.DataSize} byte(s) but only {parentEnd - payloadStart} are available.",
                    SourceRange.FromBounds(header.Offset, parentEnd));
            }

            if (header.IsUnknownSize)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.UnknownSize,
                    $"Element '{elementName}' has an unknown size but is not a master element; using the parent boundary.",
                    SourceRange.FromBounds(header.Offset, parentEnd));
            }
        }

        nodeEnd = payloadEnd;

        var kind = !known
            ? NodeKind.Unparsed
            : isMaster
                ? NodeKind.Container
                : header.Id == EbmlSchema.VoidId
                    ? NodeKind.Padding
                    : EbmlSchema.IsHighEntropyBlock(header.Id)
                        ? NodeKind.Unparsed
                        : NodeKind.Leaf;
        var name = known
            ? definition.Name
            : EbmlFormatting.FormatId(header.Id);
        var payloadRange = SourceRange.FromBounds(payloadStart, payloadEnd);
        return new FormatNode(
            name,
            kind,
            SourceRange.FromBounds(header.Offset, payloadEnd),
            payloadRange,
            children,
            EbmlParser.IdValue,
            GetStableId(header.Id));
    }

    /// <summary>
    /// Stable IDs repeat for every element of the same kind; caching them avoids one string
    /// allocation per node in large Matroska files.
    /// </summary>
    private string GetStableId(ulong id)
    {
        if (!_stableIds.TryGetValue(id, out var value))
        {
            value = $"ebml:{id:X}";
            _stableIds.Add(id, value);
        }

        return value;
    }

    private void AddUnparsed(
        ImmutableArray<FormatNode>.Builder children,
        long start,
        long end,
        string name,
        string code,
        string message)
    {
        if (start >= end)
        {
            return;
        }

        var range = SourceRange.FromBounds(start, end);
        children.Add(new FormatNode(name, NodeKind.Unparsed, range, range));
        _diagnostics.Warning(code, message, range);
    }

    private static (string Code, string Message) DescribeFailure(EbmlHeaderFailure failure, long position) => failure switch
    {
        EbmlHeaderFailure.InvalidVint => (DiagnosticCodes.InvalidVint, $"Invalid EBML VINT at offset {position}."),
        EbmlHeaderFailure.LengthOverflow => (DiagnosticCodes.LengthOverflow, $"EBML element at offset {position} has a size that overflows Int64."),
        _ => (DiagnosticCodes.TruncatedHeader, $"Incomplete EBML element header at offset {position}."),
    };

    private readonly record struct SequenceResult(ImmutableArray<FormatNode> Children, long End);
}
