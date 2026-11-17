using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Psd;

/// <summary>
/// Builds the PSD/PSB section tree. Only section and record headers are read while building; the
/// pixel planes, layer channel data and opaque tagged blocks stay in the source and are read when
/// their node is selected. Every byte of the file belongs to exactly one node.
/// </summary>
internal sealed class PsdDocumentParser
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly DiagnosticBag _diagnostics;

    public PsdDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _source = context.Source;
        _diagnostics = context.Diagnostics;
    }

    public FormatNode Parse()
    {
        var fileRange = new SourceRange(0, _source.Length);
        var children = ImmutableArray.CreateBuilder<FormatNode>();

        if (_source.Length < PsdConstants.HeaderLength)
        {
            AddUnparsed(
                children,
                0,
                _source.Length,
                "<truncated header>",
                DiagnosticCodes.TruncatedHeader,
                $"The file is shorter than the {PsdConstants.HeaderLength}-byte Photoshop document header.");
            return CreateRoot(fileRange, children.ToImmutable());
        }

        if (!PsdLayout.TryReadHeader(_source, out var header, out var headerError))
        {
            AddUnparsed(
                children,
                0,
                _source.Length,
                "<unparsed data>",
                DiagnosticCodes.InvalidSignature,
                headerError);
            return CreateRoot(fileRange, children.ToImmutable());
        }

        var psb = header.Version == PsdConstants.VersionPsb;
        children.Add(new FormatNode(
            "FileHeader",
            NodeKind.Leaf,
            new SourceRange(0, PsdConstants.HeaderLength),
            formatId: PsdParser.IdValue,
            stableId: "psd:header"));

        var position = (long)PsdConstants.HeaderLength;
        if (!AddColorModeData(children, ref position)
            || !AddImageResources(children, ref position)
            || !AddLayerAndMaskInformation(children, ref position, psb))
        {
            return CreateRoot(fileRange, children.ToImmutable());
        }

        AddImageData(children, position);
        return CreateRoot(fileRange, children.ToImmutable());
    }

    private bool AddColorModeData(ImmutableArray<FormatNode>.Builder children, ref long position)
    {
        _context.ThrowIfCancellationRequested();
        _context.ReportProgress("PSD", position, _source.Length);
        if (position >= _source.Length)
        {
            return true;
        }

        if (!PsdLayout.TryReadSection(
                _source,
                position,
                _source.Length,
                psb: false,
                out _,
                out _,
                out var end,
                out _,
                out var complete,
                out var error))
        {
            AddUnparsed(children, position, _source.Length, "<unparsed data>", DiagnosticCodes.TruncatedHeader, error);
            return false;
        }

        children.Add(new FormatNode(
            "ColorModeData",
            NodeKind.Leaf,
            SourceRange.FromBounds(position, end),
            formatId: PsdParser.IdValue,
            stableId: string.Create(CultureInfo.InvariantCulture, $"psd:cmd:{position}")));
        ReportTruncation(position, end, complete, "Color mode data", error);
        position = end;
        return true;
    }

    private bool AddImageResources(ImmutableArray<FormatNode>.Builder children, ref long position)
    {
        _context.ThrowIfCancellationRequested();
        _context.ReportProgress("PSD", position, _source.Length);
        if (position >= _source.Length)
        {
            return true;
        }

        if (!PsdLayout.TryReadSection(
                _source,
                position,
                _source.Length,
                psb: false,
                out _,
                out var dataStart,
                out var end,
                out _,
                out var complete,
                out var error))
        {
            AddUnparsed(children, position, _source.Length, "<unparsed data>", DiagnosticCodes.TruncatedHeader, error);
            return false;
        }

        var resources = ImmutableArray.CreateBuilder<FormatNode>();
        var cursor = dataStart;
        var index = 0;
        while (cursor < end)
        {
            _context.ThrowIfCancellationRequested();
            if (!PhotoshopImageResources.TryReadBlock(_source, cursor, end, out var block, out var blockError))
            {
                AddUnparsed(
                    resources,
                    cursor,
                    end,
                    "<unparsed resource data>",
                    DiagnosticCodes.InvalidSignature,
                    blockError);
                break;
            }

            resources.Add(new FormatNode(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Resource[{index}] '{PhotoshopImageResources.FormatResourceId(block.Id)}'"),
                NodeKind.Leaf,
                SourceRange.FromBounds(cursor, block.End),
                formatId: PsdParser.IdValue,
                stableId: string.Create(CultureInfo.InvariantCulture, $"psd:res:{cursor}")));

            if (block.End <= cursor)
            {
                break;
            }

            cursor = block.End;
            index++;
            if (!block.Complete)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    block.Error ?? "The image resource data is truncated.",
                    new SourceRange(block.Start, block.End - block.Start));
                break;
            }
        }

        children.Add(new FormatNode(
            "ImageResources",
            NodeKind.Container,
            SourceRange.FromBounds(position, end),
            SourceRange.FromBounds(dataStart, end),
            resources.ToImmutable(),
            PsdParser.IdValue,
            string.Create(CultureInfo.InvariantCulture, $"psd:ir:{position}")));
        ReportTruncation(position, end, complete, "Image resources", error);
        position = end;
        return true;
    }

    private bool AddLayerAndMaskInformation(
        ImmutableArray<FormatNode>.Builder children,
        ref long position,
        bool psb)
    {
        _context.ThrowIfCancellationRequested();
        _context.ReportProgress("PSD", position, _source.Length);
        if (position >= _source.Length)
        {
            return true;
        }

        if (!PsdLayout.TryReadSection(
                _source,
                position,
                _source.Length,
                psb,
                out _,
                out var dataStart,
                out var end,
                out _,
                out var complete,
                out var error))
        {
            AddUnparsed(children, position, _source.Length, "<unparsed data>", DiagnosticCodes.TruncatedHeader, error);
            return false;
        }

        var parts = ImmutableArray.CreateBuilder<FormatNode>();
        var cursor = dataStart;
        if (cursor < end)
        {
            if (!AddLayerInfo(parts, ref cursor, end, psb))
            {
                AddUnparsed(parts, cursor, end, "<unparsed data>", DiagnosticCodes.TruncatedHeader, "The layer info section could not be read.");
                cursor = end;
            }
            else
            {
                AddGlobalLayerMaskInfo(parts, ref cursor, end);
                AddAdditionalInfoBlocks(parts, ref cursor, end, psb);
            }
        }

        children.Add(new FormatNode(
            "LayerAndMaskInformation",
            NodeKind.Container,
            SourceRange.FromBounds(position, end),
            SourceRange.FromBounds(dataStart, end),
            parts.ToImmutable(),
            PsdParser.IdValue,
            string.Create(CultureInfo.InvariantCulture, $"psd:lmi:{position}")));
        ReportTruncation(position, end, complete, "Layer and mask information", error);
        position = end;
        return true;
    }

    private bool AddLayerInfo(
        ImmutableArray<FormatNode>.Builder parts,
        ref long cursor,
        long limit,
        bool psb)
    {
        _context.ThrowIfCancellationRequested();
        if (!PsdLayout.TryReadSection(
                _source,
                cursor,
                limit,
                psb,
                out _,
                out var dataStart,
                out var end,
                out _,
                out _,
                out _))
        {
            return false;
        }

        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var position = dataStart;
        if (end - dataStart >= 2)
        {
            var countBytes = ReadInt16(dataStart);
            children.Add(new FormatNode(
                "LayerCount",
                NodeKind.Leaf,
                new SourceRange(dataStart, 2),
                formatId: PsdParser.IdValue,
                stableId: string.Create(CultureInfo.InvariantCulture, $"psd:lc:{dataStart}")));

            var recordsStart = dataStart + 2;
            var layerCount = Math.Abs((int)countBytes);
            var records = ImmutableArray.CreateBuilder<FormatNode>();
            var layouts = new List<PsdLayerRecordLayout>(layerCount);
            var recordCursor = recordsStart;
            for (var index = 0; index < layerCount; index++)
            {
                _context.ThrowIfCancellationRequested();
                if (!PsdLayout.TryReadLayerRecord(_source, recordCursor, end, psb, out var layout, out var error))
                {
                    AddUnparsed(
                        records,
                        recordCursor,
                        end,
                        "<unparsed layer records>",
                        DiagnosticCodes.TruncatedPayload,
                        error);
                    recordCursor = end;
                    break;
                }

                layouts.Add(layout);
                records.Add(BuildLayerRecord(layout, index, psb));
                recordCursor = layout.End;
                if (!layout.Complete)
                {
                    _diagnostics.Warning(
                        DiagnosticCodes.TruncatedPayload,
                        layout.Error ?? "The layer record is truncated.",
                        SourceRange.FromBounds(layout.Start, layout.End));
                    break;
                }
            }

            children.Add(new FormatNode(
                "LayerRecords",
                NodeKind.Container,
                SourceRange.FromBounds(recordsStart, recordCursor),
                SourceRange.FromBounds(recordsStart, recordCursor),
                records.ToImmutable(),
                PsdParser.IdValue,
                string.Create(CultureInfo.InvariantCulture, $"psd:lrs:{recordsStart}")));

            children.Add(BuildChannelImageData(recordCursor, end, layouts, psb));
        }
        else
        {
            if (end > dataStart)
            {
                AddUnparsed(
                    children,
                    dataStart,
                    end,
                    "<unparsed layer info>",
                    DiagnosticCodes.TruncatedPayload,
                    "The layer info section is too short to contain the layer count.");
            }
        }

        parts.Add(new FormatNode(
            "LayerInfo",
            NodeKind.Container,
            SourceRange.FromBounds(cursor, end),
            SourceRange.FromBounds(dataStart, end),
            children.ToImmutable(),
            PsdParser.IdValue,
            string.Create(CultureInfo.InvariantCulture, $"psd:li:{cursor}")));
        cursor = end;
        return true;
    }

    private FormatNode BuildLayerRecord(PsdLayerRecordLayout layout, int index, bool psb)
    {
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        children.Add(new FormatNode(
            "LayerRecordHeader",
            NodeKind.Leaf,
            SourceRange.FromBounds(layout.Start, layout.FixedEnd),
            formatId: PsdParser.IdValue,
            stableId: string.Create(CultureInfo.InvariantCulture, $"psd:lrh:{layout.Start}")));

        AddLeaf(children, "LayerMaskData", layout.MaskStart, layout.MaskEnd, "psd:lm");
        AddLeaf(children, "LayerBlendingRanges", layout.BlendingStart, layout.BlendingEnd, "psd:lbr");
        AddLeaf(children, "LayerName", layout.NameStart, layout.NameEnd, "psd:ln");

        var cursor = layout.AdditionalStart;
        while (cursor < layout.AdditionalEnd)
        {
            _context.ThrowIfCancellationRequested();
            if (!PsdLayout.TryReadAdditionalInfoBlock(_source, cursor, layout.AdditionalEnd, psb, out var block, out var error))
            {
                AddUnparsed(
                    children,
                    cursor,
                    layout.AdditionalEnd,
                    "<unparsed additional info>",
                    DiagnosticCodes.InvalidSignature,
                    error);
                break;
            }

            children.Add(new FormatNode(
                $"AdditionalInfo '{block.Key}'",
                NodeKind.Leaf,
                SourceRange.FromBounds(block.Start, block.End),
                formatId: PsdParser.IdValue,
                stableId: string.Create(CultureInfo.InvariantCulture, $"psd:ai:{block.Start}")));
            if (block.End <= cursor)
            {
                break;
            }

            cursor = block.End;
            if (!block.Complete)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    block.Error ?? "The additional layer information block is truncated.",
                    SourceRange.FromBounds(block.Start, block.End));
                break;
            }
        }

        return new FormatNode(
            string.Create(CultureInfo.InvariantCulture, $"LayerRecord[{index}]"),
            NodeKind.Container,
            SourceRange.FromBounds(layout.Start, layout.End),
            SourceRange.FromBounds(layout.Start, layout.End),
            children.ToImmutable(),
            PsdParser.IdValue,
            string.Create(CultureInfo.InvariantCulture, $"psd:lr:{layout.Start}"));
    }

    private FormatNode BuildChannelImageData(
        long start,
        long end,
        IReadOnlyList<PsdLayerRecordLayout> layouts,
        bool psb)
    {
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var cursor = start;
        var index = 0;
        foreach (var layout in layouts)
        {
            var channels = PsdLayout.ReadChannels(_source, layout.ChannelsStart, layout.ChannelCount, psb);
            foreach (var channel in channels)
            {
                _context.ThrowIfCancellationRequested();
                if (cursor >= end)
                {
                    break;
                }

                var length = channel.DataLength > (ulong)(end - cursor)
                    ? end - cursor
                    : (long)channel.DataLength;
                var channelEnd = cursor + length;
                children.Add(new FormatNode(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"ChannelData[{index}] '{PsdConstants.FormatChannelId(channel.Id)}'"),
                    NodeKind.Leaf,
                    SourceRange.FromBounds(cursor, channelEnd),
                    formatId: PsdParser.IdValue,
                    stableId: string.Create(CultureInfo.InvariantCulture, $"psd:cd:{cursor}")));
                cursor = channelEnd;
                index++;
            }
        }

        if (cursor < end)
        {
            AddUnparsed(
                children,
                cursor,
                end,
                "<unparsed channel data>",
                DiagnosticCodes.TruncatedPayload,
                $"{end - cursor} byte(s) of channel image data do not belong to a declared channel.");
        }

        return new FormatNode(
            "ChannelImageData",
            NodeKind.Container,
            SourceRange.FromBounds(start, end),
            SourceRange.FromBounds(start, end),
            children.ToImmutable(),
            PsdParser.IdValue,
            string.Create(CultureInfo.InvariantCulture, $"psd:cds:{start}"));
    }

    private void AddGlobalLayerMaskInfo(ImmutableArray<FormatNode>.Builder parts, ref long cursor, long limit)
    {
        _context.ThrowIfCancellationRequested();
        if (cursor >= limit)
        {
            return;
        }

        if (!PsdLayout.TryReadSection(
                _source,
                cursor,
                limit,
                psb: false,
                out _,
                out _,
                out var end,
                out _,
                out var complete,
                out var error))
        {
            AddUnparsed(parts, cursor, limit, "<unparsed data>", DiagnosticCodes.TruncatedHeader, error);
            cursor = limit;
            return;
        }

        parts.Add(new FormatNode(
            "GlobalLayerMaskInfo",
            NodeKind.Leaf,
            SourceRange.FromBounds(cursor, end),
            formatId: PsdParser.IdValue,
            stableId: string.Create(CultureInfo.InvariantCulture, $"psd:glm:{cursor}")));
        ReportTruncation(cursor, end, complete, "Global layer mask info", error);
        cursor = end;
    }

    private void AddAdditionalInfoBlocks(
        ImmutableArray<FormatNode>.Builder parts,
        ref long cursor,
        long limit,
        bool psb)
    {
        while (cursor < limit)
        {
            _context.ThrowIfCancellationRequested();
            if (!PsdLayout.TryReadAdditionalInfoBlock(_source, cursor, limit, psb, out var block, out var error))
            {
                AddUnparsed(
                    parts,
                    cursor,
                    limit,
                    "<unparsed additional info>",
                    DiagnosticCodes.InvalidSignature,
                    error);
                cursor = limit;
                return;
            }

            parts.Add(new FormatNode(
                $"AdditionalInfo '{block.Key}'",
                NodeKind.Leaf,
                SourceRange.FromBounds(block.Start, block.End),
                formatId: PsdParser.IdValue,
                stableId: string.Create(CultureInfo.InvariantCulture, $"psd:ai:{block.Start}")));
            if (block.End <= cursor)
            {
                cursor = limit;
                return;
            }

            cursor = block.End;
            if (!block.Complete)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    block.Error ?? "The additional layer information block is truncated.",
                    SourceRange.FromBounds(block.Start, block.End));
                return;
            }
        }
    }

    private void AddImageData(ImmutableArray<FormatNode>.Builder children, long position)
    {
        _context.ThrowIfCancellationRequested();
        if (position >= _source.Length)
        {
            return;
        }

        children.Add(new FormatNode(
            "ImageData",
            NodeKind.Leaf,
            SourceRange.FromBounds(position, _source.Length),
            formatId: PsdParser.IdValue,
            stableId: string.Create(CultureInfo.InvariantCulture, $"psd:id:{position}")));
    }

    private void AddLeaf(
        ImmutableArray<FormatNode>.Builder children,
        string name,
        long start,
        long end,
        string stablePrefix)
    {
        if (start >= end)
        {
            return;
        }

        children.Add(new FormatNode(
            name,
            NodeKind.Leaf,
            SourceRange.FromBounds(start, end),
            formatId: PsdParser.IdValue,
            stableId: string.Create(CultureInfo.InvariantCulture, $"{stablePrefix}:{start}")));
    }

    private short ReadInt16(long offset)
    {
        Span<byte> bytes = stackalloc byte[2];
        return SourceRead.ReadAtMost(_source, offset, bytes) == 2
            ? BinaryPrimitives.ReadInt16BigEndian(bytes)
            : (short)0;
    }

    private void ReportTruncation(long start, long end, bool complete, string name, string error)
    {
        if (!complete)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                string.IsNullOrEmpty(error)
                    ? $"{name} is truncated."
                    : error,
                SourceRange.FromBounds(start, end));
        }
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
            children.Add(new FormatNode(
                name,
                NodeKind.Unparsed,
                SourceRange.FromBounds(start, end),
                SourceRange.FromBounds(start, end)));
        }

        _diagnostics.Warning(code, message, SourceRange.FromBounds(start, end));
    }

    private static FormatNode CreateRoot(SourceRange range, ImmutableArray<FormatNode> children) =>
        new(
            PsdParser.DisplayNameValue,
            NodeKind.Container,
            range,
            range,
            children,
            PsdParser.IdValue,
            "psd-root");
}

