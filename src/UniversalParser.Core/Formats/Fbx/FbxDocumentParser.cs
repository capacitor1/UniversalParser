using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Fbx;

/// <summary>
/// Builds the FBX node tree from record headers only. Property lists are skipped by their declared
/// length, so the tree phase never reads geometry data. The 27-byte header and the footer are
/// explicit nodes; confirmed footer framing is expanded and unfamiliar footer variants stay unparsed.
/// </summary>
internal sealed class FbxDocumentParser : IDisposable
{
    private const int FooterMagicLength = 16;

    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly BufferedReadSource? _window;
    private readonly DiagnosticBag _diagnostics;
    private readonly uint _version;
    private readonly bool _wide;
    private readonly int _headerSize;
    private readonly int _nullSize;

    public FbxDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        // FBX tree building is a forward-only scan over many tiny record headers. A read-ahead
        // window turns millions of per-header reads into sequential file I/O.
        _window = context.Source.Length >= BufferedReadSource.MinimumLength
            ? new BufferedReadSource(context.Source)
            : null;
        _source = _window ?? context.Source;
        _diagnostics = context.Diagnostics;
        _version = FbxConstants.ReadVersion(_source);
        _wide = FbxConstants.IsWide(_version);
        _headerSize = FbxConstants.RecordHeaderLength(_version);
        _nullSize = FbxConstants.NullRecordLength(_version);
    }

    public void Dispose() => _window?.Dispose();

    public FormatNode Parse()
    {
        var fileRange = new SourceRange(0, _source.Length);
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var headerLength = Math.Min(FbxConstants.HeaderLength, _source.Length);
        children.Add(new FormatNode(
            "FBXHeader",
            NodeKind.Leaf,
            new SourceRange(0, headerLength),
            formatId: FbxParser.IdValue,
            stableId: "fbx:header"));

        if (headerLength < FbxConstants.HeaderLength)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The FBX header needs {FbxConstants.HeaderLength} bytes but only {_source.Length} are available.",
                fileRange);
            return CreateRoot(fileRange, children.ToImmutable());
        }

        var position = (long)FbxConstants.HeaderLength;
        var footerStart = _source.Length;
        while (position < _source.Length)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("FBX", position, _source.Length);

            if (IsNullRecord(position) || IsFooterMagic(position))
            {
                footerStart = position;
                break;
            }

            if (_source.Length - position < _headerSize)
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<trailing data>",
                    DiagnosticCodes.TruncatedHeader,
                    $"{_source.Length - position} trailing byte(s) cannot form an FBX node record.");
                footerStart = _source.Length;
                break;
            }

            var node = BuildNodeAt(position, _source.Length, out var next);
            children.Add(node);
            if (next <= position)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.InvalidLength,
                    "The FBX node end does not advance past its record header.",
                    SourceRange.FromBounds(position, _source.Length));
                footerStart = _source.Length;
                break;
            }

            position = next;
        }

        if (footerStart < _source.Length)
        {
            var footerRange = SourceRange.FromBounds(footerStart, _source.Length);
            if (FbxFooterLayout.TryRead(_source, footerStart, _source.Length, _nullSize, out var layout))
            {
                children.Add(BuildFooter(footerRange, layout));
            }
            else
            {
                children.Add(new FormatNode(
                    "FBXFooter",
                    NodeKind.Unparsed,
                    footerRange,
                    footerRange,
                    formatId: FbxParser.IdValue,
                    stableId: "fbx:footer"));
            }
        }

        return CreateRoot(fileRange, children.ToImmutable());
    }

    private static FormatNode BuildFooter(SourceRange footerRange, FbxFooterLayout layout)
    {
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        AddFooterChild(children, "Null Record", NodeKind.Padding, layout.NullRecord, "fbx:footer:null");
        AddFooterChild(children, "Footer ID", NodeKind.Leaf, layout.Id, "fbx:footer:id");
        AddFooterChild(children, "Footer padding", NodeKind.Padding, layout.Padding, "fbx:footer:padding");
        AddFooterChild(children, "Footer version", NodeKind.Leaf, layout.Version, "fbx:footer:version");
        AddFooterChild(children, "Footer zero fill", NodeKind.Padding, layout.ZeroFill, "fbx:footer:zeros");
        AddFooterChild(children, "Footer trailer", NodeKind.Leaf, layout.Trailer, "fbx:footer:trailer");

        return new FormatNode(
            "FBXFooter",
            NodeKind.Container,
            footerRange,
            footerRange,
            children.ToImmutable(),
            FbxParser.IdValue,
            "fbx:footer");
    }

    private static void AddFooterChild(
        ImmutableArray<FormatNode>.Builder children,
        string name,
        NodeKind kind,
        SourceRange range,
        string stableId)
    {
        if (range.IsEmpty)
        {
            return;
        }

        children.Add(new FormatNode(name, kind, range, range, formatId: FbxParser.IdValue, stableId: stableId));
    }

    private ImmutableArray<FormatNode> BuildChildren(long start, long end)
    {
        if (start >= end)
        {
            return [];
        }

        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var position = start;
        while (position < end)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("FBX", position, _source.Length);

            if (IsNullRecord(position))
            {
                var paddingRange = SourceRange.FromBounds(position, end);
                _diagnostics.Note(
                    DiagnosticCodes.UnparsedPayload,
                    "A null record ends the child list before the declared end offset.",
                    paddingRange);
                children.Add(new FormatNode("Null Record", NodeKind.Padding, paddingRange, paddingRange));
                break;
            }

            if (end - position < _headerSize)
            {
                AddUnparsed(
                    children,
                    position,
                    end,
                    "<trailing data>",
                    DiagnosticCodes.TruncatedChild,
                    $"{end - position} trailing byte(s) cannot form a child FBX node record.");
                break;
            }

            var node = BuildNodeAt(position, end, out var next);
            children.Add(node);
            if (next <= position)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.InvalidLength,
                    "The child FBX node end does not advance past its record header.",
                    SourceRange.FromBounds(position, end));
                break;
            }

            position = next;
        }

        return children.ToImmutable();
    }

    /// <summary>Builds one node record.</summary>
    private FormatNode BuildNodeAt(long position, long limit, out long next)
    {
        next = position;
        var header = ReadNodeHeader(position, limit);
        if (header is null)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The FBX node record at {position} cannot be read completely.",
                SourceRange.FromBounds(position, limit));
            next = limit;
            return CreateUnparsed("<unparsed data>", position, limit);
        }

        var record = header.Value;
        var nodeEnd = record.EndOffset;
        if (nodeEnd > limit)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"EndOffset is {nodeEnd}, but only {limit} byte(s) are available; the node is clamped.",
                SourceRange.FromBounds(position, limit));
            nodeEnd = limit;
        }

        if (nodeEnd <= position + _headerSize)
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidLength,
                $"EndOffset is {record.EndOffset}, which does not cover the {_headerSize}-byte node header.",
                SourceRange.FromBounds(position, limit));
            next = limit;
            return CreateUnparsed("<unparsed data>", position, limit);
        }

        if (record.PropertyOffset > nodeEnd
            || record.PropertyListLength > (ulong)(nodeEnd - record.PropertyOffset))
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidLength,
                $"PropertyListLen is {record.PropertyListLength}, which does not fit before EndOffset {record.EndOffset}.",
                SourceRange.FromBounds(position, nodeEnd));
            next = nodeEnd;
            return CreateUnparsed("<unparsed data>", position, nodeEnd);
        }

        var propertyEnd = record.PropertyOffset + (long)record.PropertyListLength;
        var leftover = nodeEnd - propertyEnd;
        var nodeChildren = ImmutableArray<FormatNode>.Empty;
        SourceRange? payloadRange = null;
        var kind = NodeKind.Leaf;

        if (leftover >= _nullSize)
        {
            var childEnd = nodeEnd - _nullSize;
            nodeChildren = BuildChildren(propertyEnd, childEnd);
            payloadRange = SourceRange.FromBounds(propertyEnd, childEnd);
            kind = NodeKind.Container;
        }
        else if (leftover > 0)
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidLength,
                $"{leftover} byte(s) remain after the property list, which is too few for a child-list terminator.",
                SourceRange.FromBounds(propertyEnd, nodeEnd));
            var range = SourceRange.FromBounds(propertyEnd, nodeEnd);
            nodeChildren = [CreateUnparsed("<unparsed data>", propertyEnd, nodeEnd)];
            payloadRange = range;
            kind = NodeKind.Container;
        }

        var name = record.NameLength == 0 ? "<unnamed node>" : record.Name;
        next = nodeEnd;
        return new FormatNode(
            name,
            kind,
            new SourceRange(position, nodeEnd - position),
            payloadRange,
            nodeChildren,
            FbxParser.IdValue,
            string.Create(CultureInfo.InvariantCulture, $"fbx:node:{position}"));
    }

    private FbxNodeHeader? ReadNodeHeader(long offset, long limit)
    {
        if (limit - offset < _headerSize)
        {
            return null;
        }

        Span<byte> buffer = stackalloc byte[25];
        if (SourceRead.ReadAtMost(_source, offset, buffer[.._headerSize]) < _headerSize)
        {
            return null;
        }

        var endOffset = _wide
            ? BinaryPrimitives.ReadUInt64LittleEndian(buffer[..8])
            : BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]);
        var numProperties = _wide
            ? BinaryPrimitives.ReadUInt64LittleEndian(buffer[8..16])
            : BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..8]);
        var propertyListLength = _wide
            ? BinaryPrimitives.ReadUInt64LittleEndian(buffer[16..24])
            : BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..12]);
        var nameLength = buffer[_headerSize - 1];

        if (nameLength > limit - offset - _headerSize)
        {
            return null;
        }

        byte[] nameBytes = [];
        if (nameLength > 0)
        {
            nameBytes = new byte[nameLength];
            if (SourceRead.ReadAtMost(_source, offset + _headerSize, nameBytes) < nameLength)
            {
                return null;
            }
        }

        var clampedEnd = endOffset > long.MaxValue ? long.MaxValue : (long)endOffset;
        if (!FbxFormatting.IsValidUtf8(nameBytes))
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidEncoding,
                $"The node name at {offset} is not valid UTF-8; invalid bytes are shown as replacement characters.",
                new SourceRange(offset + _headerSize, nameLength));
        }

        return new FbxNodeHeader(
            offset,
            clampedEnd,
            numProperties,
            propertyListLength,
            nameLength,
            offset + _headerSize,
            FbxFormatting.FormatText(nameBytes),
            offset + _headerSize + nameLength,
            IsNullRecord: false);
    }

    private bool IsNullRecord(long offset)
    {
        if (_source.Length - offset < _headerSize)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[25];
        if (SourceRead.ReadAtMost(_source, offset, buffer[.._headerSize]) < _headerSize)
        {
            return false;
        }

        foreach (var value in buffer[.._headerSize])
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsFooterMagic(long offset)
    {
        if (_source.Length - offset < FooterMagicLength)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[FooterMagicLength];
        return SourceRead.ReadAtMost(_source, offset, buffer) == FooterMagicLength
            && buffer.SequenceEqual(FbxConstants.BlenderFooterCode);
    }

    private void AddUnparsed(
        ImmutableArray<FormatNode>.Builder children,
        long start,
        long end,
        string name,
        string code,
        string message)
    {
        if (start < end)
        {
            children.Add(CreateUnparsed(name, start, end));
        }

        _diagnostics.Warning(code, message, SourceRange.FromBounds(start, end));
    }

    private static FormatNode CreateUnparsed(string name, long start, long end) =>
        new(
            name,
            NodeKind.Unparsed,
            SourceRange.FromBounds(start, end),
            SourceRange.FromBounds(start, end));

    private static FormatNode CreateRoot(SourceRange range, ImmutableArray<FormatNode> children) =>
        new(
            "Filmbox",
            NodeKind.Container,
            range,
            range,
            children,
            FbxParser.IdValue,
            "fbx-root");
}
