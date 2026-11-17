using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Protobuf;

/// <summary>
/// Builds a raw protobuf field tree without loading a .proto descriptor. Each encoded field is
/// one node; repeated field numbers stay separate siblings. Length-delimited payloads become
/// nested children only when they are structurally valid messages and are not readable text.
/// </summary>
internal sealed class ProtobufDocumentParser
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly DiagnosticBag _diagnostics;

    public ProtobufDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _source = context.Source;
        _diagnostics = context.Diagnostics;
    }

    public FormatNode Parse()
    {
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        ParseMessage(0, _source.Length, children);

        var range = new SourceRange(0, _source.Length);
        return new FormatNode(
            ProtobufConstants.DisplayName,
            NodeKind.Container,
            range,
            range,
            children.ToImmutable(),
            ProtobufConstants.ParserId,
            ProtobufConstants.RootStableId);
    }

    private void ParseMessage(long start, long end, ImmutableArray<FormatNode>.Builder children)
    {
        var position = start;
        while (position < end)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("Protocol Buffers", position, _source.Length);

            if (!ProtobufRead.TryReadFieldHeader(_source, position, end, out var header, out var failure))
            {
                AddUnparsed(
                    children,
                    position,
                    end,
                    GetFailureCode(failure),
                    ProtobufRead.GetFailureMessage(failure));
                return;
            }

            if (header.WireType == ProtobufConstants.EndGroupWireType)
            {
                AddUnparsed(
                    children,
                    position,
                    end,
                    DiagnosticCodes.SchemaMismatch,
                    "An end-group key appeared without a matching start-group.");
                return;
            }

            var node = header.WireType == ProtobufConstants.StartGroupWireType
                ? BuildGroup(header, end)
                : BuildField(header, end);
            children.Add(node);

            position = header.Truncated ? end : node.Range.End;
        }
    }

    private FormatNode BuildField(ProtobufFieldHeader header, long limit)
    {
        if (header.WireType != ProtobufConstants.LengthDelimitedWireType)
        {
            var name = ProtobufConstants.FormatFieldNodeName(header.WireType);
            return new FormatNode(
                name,
                NodeKind.Leaf,
                SourceRange.FromBounds(header.Start, header.ValueEnd),
                formatId: ProtobufConstants.ParserId,
                stableId: ProtobufConstants.FormatStableId(header.Start));
        }

        if (header.Truncated)
        {
            var range = SourceRange.FromBounds(header.Start, limit);
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"Field {header.FieldNumber} declares {header.DeclaredLength} payload byte(s) but only {limit - header.ValueStart} are available.",
                range);
            return new FormatNode(
                ProtobufConstants.FormatFieldNodeName(header.WireType),
                NodeKind.Unparsed,
                range,
                formatId: ProtobufConstants.ParserId,
                stableId: ProtobufConstants.FormatStableId(header.Start));
        }

        var payloadRange = SourceRange.FromBounds(header.ValueStart, header.ValueEnd);
        var payloadKind = ProtobufPayloadClassifier.Classify(
            _source,
            payloadRange.Offset,
            payloadRange.Length,
            _context.CancellationToken);
        var nodeName = ProtobufConstants.FormatFieldNodeName(header.WireType);
        var nodeRange = SourceRange.FromBounds(header.Start, header.ValueEnd);

        if (payloadKind != ProtobufPayloadKind.Message)
        {
            return new FormatNode(
                nodeName,
                NodeKind.Leaf,
                nodeRange,
                formatId: ProtobufConstants.ParserId,
                stableId: ProtobufConstants.FormatStableId(header.Start));
        }

        var children = ImmutableArray.CreateBuilder<FormatNode>();
        ParseMessage(payloadRange.Offset, payloadRange.End, children);
        return new FormatNode(
            nodeName,
            NodeKind.Container,
            nodeRange,
            payloadRange,
            children.ToImmutable(),
            ProtobufConstants.ParserId,
            ProtobufConstants.FormatStableId(header.Start));
    }

    private FormatNode BuildGroup(ProtobufFieldHeader header, long limit)
    {
        var bodyEnd = limit;
        var nodeEnd = limit;
        if (ProtobufMessageScanner.TryFindGroupEnd(
                _source,
                header.ValueStart,
                limit,
                header.FieldNumber,
                _context.CancellationToken,
                out var endKeyStart,
                out var endKeyEnd))
        {
            bodyEnd = endKeyStart;
            nodeEnd = endKeyEnd;
        }
        else
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"Start-group field {header.FieldNumber} has no matching end-group key.",
                SourceRange.FromBounds(header.Start, limit));
        }

        var payloadRange = SourceRange.FromBounds(header.ValueStart, bodyEnd);
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        ParseMessage(payloadRange.Offset, payloadRange.End, children);
        return new FormatNode(
            ProtobufConstants.FormatGroupNodeName(),
            NodeKind.Container,
            SourceRange.FromBounds(header.Start, nodeEnd),
            payloadRange,
            children.ToImmutable(),
            ProtobufConstants.ParserId,
            ProtobufConstants.FormatStableId(header.Start));
    }

    private static string GetFailureCode(ProtobufReadFailure failure) => failure switch
    {
        ProtobufReadFailure.TruncatedHeader => DiagnosticCodes.TruncatedHeader,
        ProtobufReadFailure.InvalidVarint => DiagnosticCodes.InvalidVint,
        ProtobufReadFailure.InvalidFieldNumber => DiagnosticCodes.InvalidFieldNumber,
        ProtobufReadFailure.InvalidWireType => DiagnosticCodes.InvalidWireType,
        _ => DiagnosticCodes.SchemaMismatch,
    };

    private void AddUnparsed(
        ImmutableArray<FormatNode>.Builder children,
        long start,
        long end,
        string code,
        string message)
    {
        if (start >= end)
        {
            return;
        }

        var range = SourceRange.FromBounds(start, end);
        children.Add(new FormatNode(
            "<unparsed data>",
            NodeKind.Unparsed,
            range,
            formatId: ProtobufConstants.ParserId,
            stableId: ProtobufConstants.FormatStableId(start)));
        _diagnostics.Warning(code, message, range);
    }
}
