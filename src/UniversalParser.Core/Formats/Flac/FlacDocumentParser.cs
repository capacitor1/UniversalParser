using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Flac;

/// <summary>
/// Builds the native FLAC tree: metadata block nodes plus one explicit audio frame region.
/// Only block headers and the cuesheet/seek table framing are read here; block fields are read
/// lazily when a node is selected.
/// </summary>
internal sealed class FlacDocumentParser
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly DiagnosticBag _diagnostics;

    public FlacDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _source = context.Source;
        _diagnostics = context.Diagnostics;
    }

    public FormatNode Parse()
    {
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var position = (long)FlacConstants.SignatureLength;
        var blockCount = 0;
        var seenStreamInfo = false;
        var sawLastBlock = false;

        while (position < _source.Length)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("FLAC", position, _source.Length);

            if (!FlacBlock.TryReadHeader(_source, position, _source.Length, out var header, out _))
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    DiagnosticCodes.TruncatedHeader,
                    "The FLAC metadata block header is truncated.");
                position = _source.Length;
                break;
            }

            if (header.Type == FlacConstants.ForbiddenType)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    "Metadata block type 127 is forbidden.",
                    SourceRange.FromBounds(header.Start, header.End));
            }

            if (blockCount == 0 && header.Type != FlacConstants.StreamInfoType)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    "The first FLAC metadata block is not STREAMINFO.",
                    SourceRange.FromBounds(header.Start, header.End));
            }

            if (header.Type == FlacConstants.StreamInfoType)
            {
                if (seenStreamInfo)
                {
                    _diagnostics.Warning(
                        DiagnosticCodes.SchemaMismatch,
                        "A FLAC stream must not contain more than one STREAMINFO block.",
                        SourceRange.FromBounds(header.Start, header.End));
                }

                seenStreamInfo = true;
            }

            var (blockChildren, payloadRange) = BuildBlock(header);
            var isContainer = payloadRange is not null;
            if (header.Truncated)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    "The FLAC metadata block extends beyond the end of the file; its range is clamped.",
                    SourceRange.FromBounds(header.Start, header.End));
            }

            children.Add(new FormatNode(
                FlacConstants.GetBlockName(header.Type),
                isContainer ? NodeKind.Container : NodeKind.Leaf,
                SourceRange.FromBounds(header.Start, header.End),
                payloadRange,
                blockChildren,
                FlacParser.IdValue,
                string.Create(CultureInfo.InvariantCulture, $"flac:block:{header.Start}")));

            blockCount++;
            position = header.End;

            if (header.LastBlock)
            {
                sawLastBlock = true;
                if (position < _source.Length)
                {
                    AddAudioFrames(children, position, _source.Length);
                }

                position = _source.Length;
                break;
            }

            if (header.Truncated)
            {
                position = _source.Length;
                break;
            }
        }

        if (position < _source.Length)
        {
            AddUnparsed(
                children,
                position,
                _source.Length,
                DiagnosticCodes.TrailingData,
                $"{_source.Length - position} byte(s) after the last metadata block are not interpreted.");
        }

        if (!seenStreamInfo)
        {
            _diagnostics.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The FLAC stream has no STREAMINFO metadata block.",
                new SourceRange(0, _source.Length));
        }

        if (!sawLastBlock && blockCount > 0)
        {
            _diagnostics.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The last-metadata-block flag was not set before the end of the file.",
                new SourceRange(0, _source.Length));
        }

        var range = new SourceRange(0, _source.Length);
        var rootPayload = new SourceRange(
            Math.Min(FlacConstants.SignatureLength, _source.Length),
            Math.Max(0, _source.Length - FlacConstants.SignatureLength));
        return new FormatNode(
            FlacParser.DisplayNameValue,
            NodeKind.Container,
            range,
            rootPayload,
            children.ToImmutable(),
            FlacParser.IdValue,
            "flac-root");
    }

    private (ImmutableArray<FormatNode> Children, SourceRange? PayloadRange) BuildBlock(FlacBlockHeader header)
    {
        if (header.Type == FlacConstants.SeekTableType && header.DataLength > 0)
        {
            return BuildSeekTable(header);
        }

        if (header.Type == FlacConstants.CuesheetType && header.DataLength >= FlacConstants.CuesheetHeaderLength)
        {
            return BuildCuesheet(header);
        }

        return (ImmutableArray<FormatNode>.Empty, null);
    }

    private (ImmutableArray<FormatNode> Children, SourceRange? PayloadRange) BuildSeekTable(FlacBlockHeader header)
    {
        var builder = ImmutableArray.CreateBuilder<FormatNode>();
        var pointCount = header.DataLength / FlacConstants.SeekPointLength;
        for (var index = 0L; index < pointCount; index++)
        {
            _context.ThrowIfCancellationRequested();
            var offset = header.DataStart + index * FlacConstants.SeekPointLength;
            builder.Add(new FormatNode(
                "SEEKPOINT",
                NodeKind.Leaf,
                new SourceRange(offset, FlacConstants.SeekPointLength),
                formatId: FlacParser.IdValue,
                stableId: string.Create(CultureInfo.InvariantCulture, $"flac:seekpoint:{offset}")));
        }

        var covered = pointCount * FlacConstants.SeekPointLength;
        if (covered < header.DataLength)
        {
            var start = header.DataStart + covered;
            AddUnparsed(
                builder,
                start,
                header.End,
                DiagnosticCodes.InvalidLength,
                "The seek table length is not a multiple of 18 bytes.");
        }

        var payloadRange = new SourceRange(header.DataStart, header.DataLength);
        return (builder.ToImmutable(), payloadRange);
    }

    private (ImmutableArray<FormatNode> Children, SourceRange? PayloadRange) BuildCuesheet(FlacBlockHeader header)
    {
        var builder = ImmutableArray.CreateBuilder<FormatNode>();
        var trackCount = ReadByte(header.DataStart + 395);
        var tracksStart = header.DataStart + FlacConstants.CuesheetHeaderLength;
        var cursor = tracksStart;
        var parsedTracks = 0;

        while (parsedTracks < trackCount && cursor + FlacConstants.CuesheetTrackHeaderLength <= header.End)
        {
            _context.ThrowIfCancellationRequested();
            var indexCount = ReadByte(cursor + FlacConstants.CuesheetTrackHeaderLength - 1);
            var trackEnd = cursor + FlacConstants.CuesheetTrackHeaderLength + indexCount * (long)FlacConstants.CuesheetIndexLength;
            if (trackEnd > header.End)
            {
                break;
            }

            var indexes = ImmutableArray.CreateBuilder<FormatNode>();
            for (var index = 0; index < indexCount; index++)
            {
                var indexStart = cursor + FlacConstants.CuesheetTrackHeaderLength + index * (long)FlacConstants.CuesheetIndexLength;
                indexes.Add(new FormatNode(
                    "CUESHEET_TRACK_INDEX",
                    NodeKind.Leaf,
                    new SourceRange(indexStart, FlacConstants.CuesheetIndexLength),
                    formatId: FlacParser.IdValue,
                    stableId: string.Create(CultureInfo.InvariantCulture, $"flac:index:{indexStart}")));
            }

            var indexPayload = indexCount > 0
                ? new SourceRange(cursor + FlacConstants.CuesheetTrackHeaderLength, trackEnd - cursor - FlacConstants.CuesheetTrackHeaderLength)
                : (SourceRange?)null;
            builder.Add(new FormatNode(
                "CUESHEET_TRACK",
                indexCount > 0 ? NodeKind.Container : NodeKind.Leaf,
                SourceRange.FromBounds(cursor, trackEnd),
                indexPayload,
                indexes.ToImmutable(),
                FlacParser.IdValue,
                string.Create(CultureInfo.InvariantCulture, $"flac:track:{cursor}")));

            cursor = trackEnd;
            parsedTracks++;
        }

        if (parsedTracks < trackCount)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedChild,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cuesheet declares {trackCount} track(s) but only {parsedTracks} could be read."),
                SourceRange.FromBounds(header.Start, header.End));
        }

        if (cursor < header.End)
        {
            AddUnparsed(
                builder,
                cursor,
                header.End,
                DiagnosticCodes.InvalidLength,
                $"{header.End - cursor} byte(s) after the cuesheet tracks are not interpreted.");
        }

        var payloadRange = new SourceRange(tracksStart, Math.Max(0, header.End - tracksStart));
        return (builder.ToImmutable(), payloadRange);
    }

    private void AddAudioFrames(ImmutableArray<FormatNode>.Builder children, long start, long end)
    {
        if (start >= end)
        {
            return;
        }

        children.Add(new FormatNode(
            "<audio frames>",
            NodeKind.Unparsed,
            SourceRange.FromBounds(start, end),
            formatId: FlacParser.IdValue,
            stableId: "flac:audio"));
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

        children.Add(new FormatNode(
            "<unparsed data>",
            NodeKind.Unparsed,
            SourceRange.FromBounds(start, end)));
        _diagnostics.Warning(code, message, SourceRange.FromBounds(start, end));
    }

    private byte ReadByte(long position)
    {
        Span<byte> value = stackalloc byte[1];
        return SourceRead.ReadAtMost(_source, position, value) == 1 ? value[0] : (byte)0;
    }
}
