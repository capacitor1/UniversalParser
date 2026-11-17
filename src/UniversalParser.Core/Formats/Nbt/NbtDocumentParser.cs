using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Nbt;

/// <summary>
/// Builds the uncompressed NBT tree. Tree names are the canonical tag type names (Compound,
/// Byte, List, ...); tag names and list indexes live in the parsed view. Compressed NBT and MCA
/// region files are not matched by the probe and remain Raw Data.
/// </summary>
internal sealed class NbtDocumentParser
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly DiagnosticBag _diagnostics;

    public NbtDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _source = context.Source;
        _diagnostics = context.Diagnostics;
    }

    public FormatNode Parse()
    {
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        if (_source.Length > 0)
        {
            _context.ThrowIfCancellationRequested();
            if (!NbtRead.TryReadTagHeader(_source, 0, _source.Length, out var rootHeader, out var failure))
            {
                AddUnparsed(
                    children,
                    0,
                    _source.Length,
                    NbtRead.GetFailureCode(failure),
                    NbtRead.GetFailureMessage(failure));
            }
            else
            {
                var rootTag = BuildNamedTag(rootHeader, _source.Length);
                children.Add(rootTag);
                if (rootTag.Range.End < _source.Length)
                {
                    AddTrailing(children, rootTag.Range.End, _source.Length);
                }
            }
        }

        var range = new SourceRange(0, _source.Length);
        return new FormatNode(
            NbtConstants.DisplayName,
            NodeKind.Container,
            range,
            range,
            children.ToImmutable(),
            NbtConstants.ParserId,
            NbtConstants.RootStableId);
    }

    private FormatNode BuildNamedTag(NbtTagHeader header, long limit) =>
        BuildTag(
            header.Type,
            header.Start,
            header.PayloadStart,
            limit,
            NbtConstants.FormatNamedStableId(header.Start));

    private FormatNode BuildElement(byte type, long payloadStart, long limit, long index) =>
        BuildTag(
            type,
            payloadStart,
            payloadStart,
            limit,
            NbtConstants.FormatElementStableId(payloadStart, type, index));

    private FormatNode BuildTag(byte type, long nodeStart, long payloadStart, long limit, string stableId)
    {
        _context.ThrowIfCancellationRequested();
        _context.ReportProgress("NBT", nodeStart, _source.Length);
        return type switch
        {
            NbtConstants.ByteType => BuildFixed(type, nodeStart, payloadStart, limit, 1, stableId),
            NbtConstants.ShortType => BuildFixed(type, nodeStart, payloadStart, limit, 2, stableId),
            NbtConstants.IntType => BuildFixed(type, nodeStart, payloadStart, limit, 4, stableId),
            NbtConstants.LongType => BuildFixed(type, nodeStart, payloadStart, limit, 8, stableId),
            NbtConstants.FloatType => BuildFixed(type, nodeStart, payloadStart, limit, 4, stableId),
            NbtConstants.DoubleType => BuildFixed(type, nodeStart, payloadStart, limit, 8, stableId),
            NbtConstants.StringType => BuildString(nodeStart, payloadStart, limit, stableId),
            NbtConstants.ByteArrayType => BuildArray(type, nodeStart, payloadStart, limit, 1, stableId),
            NbtConstants.IntArrayType => BuildArray(type, nodeStart, payloadStart, limit, 4, stableId),
            NbtConstants.LongArrayType => BuildArray(type, nodeStart, payloadStart, limit, 8, stableId),
            NbtConstants.ListType => BuildList(nodeStart, payloadStart, limit, stableId),
            NbtConstants.CompoundType => BuildCompound(nodeStart, payloadStart, limit, stableId),
            _ => BuildUnparsedTag(type, nodeStart, limit, stableId),
        };
    }

    private FormatNode BuildFixed(
        byte type,
        long nodeStart,
        long payloadStart,
        long limit,
        int byteCount,
        string stableId)
    {
        if (byteCount > limit - payloadStart)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"{NbtConstants.GetSpecName(type)} is truncated.",
                SourceRange.FromBounds(nodeStart, limit));
            return new FormatNode(
                NbtConstants.GetTypeName(type),
                NodeKind.Unparsed,
                SourceRange.FromBounds(nodeStart, limit),
                formatId: NbtConstants.ParserId,
                stableId: stableId);
        }

        return new FormatNode(
            NbtConstants.GetTypeName(type),
            NodeKind.Leaf,
            SourceRange.FromBounds(nodeStart, payloadStart + byteCount),
            formatId: NbtConstants.ParserId,
            stableId: stableId);
    }

    private FormatNode BuildString(long nodeStart, long payloadStart, long limit, string stableId)
    {
        if (!NbtRead.TryReadUInt16BE(_source, payloadStart, limit, out var length))
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedHeader,
                "The TAG_String length is truncated.",
                SourceRange.FromBounds(nodeStart, limit));
            return new FormatNode(
                NbtConstants.GetTypeName(NbtConstants.StringType),
                NodeKind.Unparsed,
                SourceRange.FromBounds(nodeStart, limit),
                formatId: NbtConstants.ParserId,
                stableId: stableId);
        }

        var dataStart = payloadStart + 2;
        if (length > limit - dataStart)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The TAG_String payload is truncated.",
                SourceRange.FromBounds(nodeStart, limit));
            return new FormatNode(
                NbtConstants.GetTypeName(NbtConstants.StringType),
                NodeKind.Unparsed,
                SourceRange.FromBounds(nodeStart, limit),
                formatId: NbtConstants.ParserId,
                stableId: stableId);
        }

        return new FormatNode(
            NbtConstants.GetTypeName(NbtConstants.StringType),
            NodeKind.Leaf,
            SourceRange.FromBounds(nodeStart, dataStart + length),
            formatId: NbtConstants.ParserId,
            stableId: stableId);
    }

    private FormatNode BuildArray(
        byte type,
        long nodeStart,
        long payloadStart,
        long limit,
        int elementSize,
        string stableId)
    {
        var typeName = NbtConstants.GetTypeName(type);
        if (!NbtRead.TryReadInt32BE(_source, payloadStart, limit, out var length))
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"{NbtConstants.GetSpecName(type)} length is truncated.",
                SourceRange.FromBounds(nodeStart, limit));
            return new FormatNode(
                typeName,
                NodeKind.Unparsed,
                SourceRange.FromBounds(nodeStart, limit),
                formatId: NbtConstants.ParserId,
                stableId: stableId);
        }

        if (length < 0)
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidLength,
                $"{NbtConstants.GetSpecName(type)} declares a negative length.",
                SourceRange.FromBounds(nodeStart, limit));
            return new FormatNode(
                typeName,
                NodeKind.Unparsed,
                SourceRange.FromBounds(nodeStart, limit),
                formatId: NbtConstants.ParserId,
                stableId: stableId);
        }

        var dataStart = payloadStart + 4;
        var byteLength = (long)length * elementSize;
        if (byteLength > limit - dataStart)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"{NbtConstants.GetSpecName(type)} payload is truncated.",
                SourceRange.FromBounds(nodeStart, limit));
            return new FormatNode(
                typeName,
                NodeKind.Unparsed,
                SourceRange.FromBounds(nodeStart, limit),
                formatId: NbtConstants.ParserId,
                stableId: stableId);
        }

        return new FormatNode(
            typeName,
            NodeKind.Leaf,
            SourceRange.FromBounds(nodeStart, dataStart + byteLength),
            formatId: NbtConstants.ParserId,
            stableId: stableId);
    }

    private FormatNode BuildList(long nodeStart, long payloadStart, long limit, string stableId)
    {
        var builder = ImmutableArray.CreateBuilder<FormatNode>();
        if (!NbtRead.TryReadByte(_source, payloadStart, limit, out var elementType))
        {
            return AddInvalidList(nodeStart, limit, stableId, DiagnosticCodes.TruncatedHeader, "The TAG_List element type is truncated.");
        }

        if (!NbtRead.TryReadInt32BE(_source, payloadStart + 1, limit, out var length))
        {
            return AddInvalidList(nodeStart, limit, stableId, DiagnosticCodes.TruncatedHeader, "The TAG_List length is truncated.");
        }

        if (length < 0)
        {
            return AddInvalidList(nodeStart, limit, stableId, DiagnosticCodes.InvalidLength, "TAG_List declares a negative length.");
        }

        if (elementType > NbtConstants.MaxTagType
            || (elementType == NbtConstants.EndType && length != 0))
        {
            return AddInvalidList(nodeStart, limit, stableId, DiagnosticCodes.InvalidTagType, "TAG_List has an invalid element type.");
        }

        var elementsStart = payloadStart + 5;
        var position = elementsStart;
        var parsed = 0L;
        while (parsed < length && position < limit)
        {
            _context.ThrowIfCancellationRequested();
            var child = BuildElement(elementType, position, limit, parsed);
            builder.Add(child);
            if (child.Range.End <= position)
            {
                AddUnparsed(builder, position, limit, DiagnosticCodes.InvalidLength, "The list element did not advance its position.");
                position = limit;
                break;
            }

            position = child.Range.End;
            parsed++;
            if (child.Kind == NodeKind.Unparsed && position >= limit)
            {
                break;
            }
        }

        if (parsed < length)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedChild,
                $"TAG_List declares {length} element(s) but only {parsed} could be read.",
                SourceRange.FromBounds(nodeStart, limit));
            if (position < limit)
            {
                AddUnparsed(builder, position, limit, DiagnosticCodes.TruncatedChild, "The remaining list elements are not available.");
                position = limit;
            }
        }

        var payloadRange = SourceRange.FromBounds(elementsStart, position);
        return new FormatNode(
            NbtConstants.GetTypeName(NbtConstants.ListType),
            NodeKind.Container,
            SourceRange.FromBounds(nodeStart, position),
            payloadRange,
            builder.ToImmutable(),
            NbtConstants.ParserId,
            stableId);
    }

    private FormatNode BuildCompound(long nodeStart, long payloadStart, long limit, string stableId)
    {
        var builder = ImmutableArray.CreateBuilder<FormatNode>();
        var position = payloadStart;
        var sawEnd = false;
        var endTagStart = payloadStart;

        while (position < limit)
        {
            _context.ThrowIfCancellationRequested();
            if (!NbtRead.TryReadByte(_source, position, limit, out var childType))
            {
                AddUnparsed(builder, position, limit, DiagnosticCodes.TruncatedHeader, "The compound child tag type is truncated.");
                position = limit;
                break;
            }

            if (childType == NbtConstants.EndType)
            {
                sawEnd = true;
                endTagStart = position;
                position++;
                break;
            }

            if (!NbtConstants.IsValidTagType(childType))
            {
                AddUnparsed(
                    builder,
                    position,
                    limit,
                    DiagnosticCodes.InvalidTagType,
                    "The compound child tag type is outside the range 1..12.");
                position = limit;
                break;
            }

            if (!NbtRead.TryReadTagHeader(_source, position, limit, out var childHeader, out var failure))
            {
                AddUnparsed(
                    builder,
                    position,
                    limit,
                    NbtRead.GetFailureCode(failure),
                    NbtRead.GetFailureMessage(failure));
                position = limit;
                break;
            }

            var child = BuildNamedTag(childHeader, limit);
            builder.Add(child);
            if (child.Range.End <= position)
            {
                AddUnparsed(builder, position, limit, DiagnosticCodes.InvalidLength, "The compound child did not advance its position.");
                position = limit;
                break;
            }

            position = child.Range.End;
        }

        if (!sawEnd)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedChild,
                "TAG_Compound has no TAG_End before the end of its range.",
                SourceRange.FromBounds(nodeStart, limit));
        }

        var payloadRange = sawEnd
            ? SourceRange.FromBounds(payloadStart, endTagStart)
            : SourceRange.FromBounds(payloadStart, limit);
        return new FormatNode(
            NbtConstants.GetTypeName(NbtConstants.CompoundType),
            NodeKind.Container,
            SourceRange.FromBounds(nodeStart, position),
            payloadRange,
            builder.ToImmutable(),
            NbtConstants.ParserId,
            stableId);
    }

    private FormatNode AddInvalidList(
        long nodeStart,
        long limit,
        string stableId,
        string code,
        string message)
    {
        _diagnostics.Warning(code, message, SourceRange.FromBounds(nodeStart, limit));
        return new FormatNode(
            NbtConstants.GetTypeName(NbtConstants.ListType),
            NodeKind.Unparsed,
            SourceRange.FromBounds(nodeStart, limit),
            formatId: NbtConstants.ParserId,
            stableId: stableId);
    }

    private FormatNode BuildUnparsedTag(byte type, long nodeStart, long limit, string stableId)
    {
        _diagnostics.Warning(
            DiagnosticCodes.InvalidTagType,
            "The NBT tag type is not supported.",
            SourceRange.FromBounds(nodeStart, limit));
        return new FormatNode(
            NbtConstants.GetTypeName(type),
            NodeKind.Unparsed,
            SourceRange.FromBounds(nodeStart, limit),
            formatId: NbtConstants.ParserId,
            stableId: stableId);
    }

    private void AddTrailing(ImmutableArray<FormatNode>.Builder children, long start, long end)
    {
        var range = SourceRange.FromBounds(start, end);
        children.Add(new FormatNode(
            "<trailing data>",
            NodeKind.Synthetic,
            range,
            formatId: NbtConstants.ParserId,
            stableId: "nbt:trailing"));
        _diagnostics.Warning(
            DiagnosticCodes.TrailingData,
            $"{end - start} byte(s) after the root NBT tag are not interpreted.",
            range);
    }

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
            formatId: NbtConstants.ParserId,
            stableId: NbtConstants.FormatUnparsedStableId(start)));
        _diagnostics.Warning(code, message, range);
    }
}
