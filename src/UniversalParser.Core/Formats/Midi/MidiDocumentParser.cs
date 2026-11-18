using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Midi;

/// <summary>
/// Builds the Standard MIDI File chunk tree. Header and track payloads are never copied into the
/// tree; only event boundaries and names are recorded. Track events are listed as child nodes so
/// the full note/controller/meta event sequence stays readable instead of being collapsed.
/// </summary>
internal sealed class MidiDocumentParser
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly DiagnosticBag _diagnostics;

    public MidiDocumentParser(ParseContext context)
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
        if (_source.Length < MidiConstants.ChunkHeaderLength)
        {
            AddUnparsed(
                children,
                0,
                _source.Length,
                "<unparsed data>",
                DiagnosticCodes.TruncatedHeader,
                $"The file is shorter than the {MidiConstants.ChunkHeaderLength}-byte MIDI chunk header.");
            return CreateRoot(fileRange, children.ToImmutable());
        }

        var position = 0L;
        var headerSeen = false;
        var trackCount = 0;
        while (position < _source.Length)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("MIDI", position, _source.Length);

            if (_source.Length - position < MidiConstants.ChunkHeaderLength)
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<trailing data>",
                    DiagnosticCodes.TruncatedHeader,
                    $"{_source.Length - position} trailing byte(s) cannot form a MIDI chunk header.");
                break;
            }

            // An interrupted download often leaves a run of zero bytes. Without this guard every
            // eight zero bytes would become an empty chunk.
            if (IsZeroHeader(position) && LooksLikeZeroFill(position))
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<zero fill>",
                    DiagnosticCodes.InvalidLength,
                    $"{_source.Length - position:N0} byte(s) of zero fill (probably an incomplete download) cannot form a MIDI chunk; parsing stopped.");
                break;
            }

            if (!TryReadChunkHeader(position, out var id, out var declaredLength))
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<trailing data>",
                    DiagnosticCodes.TruncatedHeader,
                    "The MIDI chunk header could not be read.");
                break;
            }

            var dataStart = position + MidiConstants.ChunkHeaderLength;
            var declaredEnd = dataStart + declaredLength;
            var complete = declaredEnd <= _source.Length;
            var chunkEnd = complete ? declaredEnd : _source.Length;
            var idName = RawIdentifier.FormatBytes(id);

            if (id.AsSpan().SequenceEqual(MidiConstants.HeaderChunkId))
            {
                if (headerSeen)
                {
                    AddUnparsed(
                        children,
                        position,
                        chunkEnd,
                        idName,
                        DiagnosticCodes.SchemaMismatch,
                        "A second MThd chunk was found; the Standard MIDI File format allows only one header chunk.");
                }
                else
                {
                    headerSeen = true;
                    children.Add(BuildHeader(position, dataStart, chunkEnd));
                }
            }
            else if (id.AsSpan().SequenceEqual(MidiConstants.TrackChunkId))
            {
                trackCount++;
                children.Add(BuildTrack(position, dataStart, chunkEnd));
            }
            else
            {
                AddUnparsed(
                    children,
                    position,
                    chunkEnd,
                    idName,
                    DiagnosticCodes.SchemaMismatch,
                    $"Unknown MIDI chunk type '{idName}'.");
            }

            if (!complete)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Chunk '{idName}' declares {declaredLength} data byte(s), but only {_source.Length - dataStart} are available."),
                    SourceRange.FromBounds(position, _source.Length));
            }

            if (chunkEnd <= position)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.InvalidLength,
                    "The MIDI chunk end does not advance past its header.",
                    SourceRange.FromBounds(position, _source.Length));
                break;
            }

            position = chunkEnd;
        }

        if (!headerSeen)
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidSignature,
                "The file contains no MThd header chunk.",
                fileRange);
        }

        if (trackCount == 0)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedChild,
                "The file contains no MTrk track chunks.",
                fileRange);
        }

        return CreateRoot(fileRange, children.ToImmutable());
    }

    private static FormatNode BuildHeader(long start, long dataStart, long end)
    {
        var dataRange = new SourceRange(dataStart, end - dataStart);
        return new FormatNode(
            "MThd",
            NodeKind.Leaf,
            SourceRange.FromBounds(start, end),
            dataRange,
            formatId: MidiParser.IdValue,
            stableId: string.Create(CultureInfo.InvariantCulture, $"midi:header:{start}"));
    }

    private FormatNode BuildTrack(long start, long dataStart, long end)
    {
        var dataRange = new SourceRange(dataStart, end - dataStart);
        var events = ImmutableArray.CreateBuilder<FormatNode>();
        var reader = new SourceReader(_source, dataStart, dataRange.Length);
        var runningStatus = 0;
        var sawEndOfTrack = false;
        while (reader.Remaining > 0)
        {
            _context.ThrowIfCancellationRequested();
            var eventStart = reader.Position;
            if (!MidiEventScanner.TryScan(reader, ref runningStatus, out var scan, out var error))
            {
                AddUnparsed(
                    events,
                    eventStart,
                    end,
                    "<unparsed track data>",
                    DiagnosticCodes.TruncatedPayload,
                    error);
                break;
            }

            events.Add(new FormatNode(
                scan.Name,
                NodeKind.Leaf,
                SourceRange.FromBounds(scan.Start, scan.End),
                formatId: MidiParser.IdValue,
                stableId: string.Create(
                    CultureInfo.InvariantCulture,
                    $"midi:event:{scan.Start}:{scan.Status:X2}:{(scan.RunningStatus ? 1 : 0)}")));

            if (scan.IsMeta && scan.MetaType == MidiConstants.EndOfTrackMetaType)
            {
                sawEndOfTrack = true;
                if (reader.Remaining > 0)
                {
                    AddUnparsed(
                        events,
                        reader.Position,
                        end,
                        "<trailing data>",
                        DiagnosticCodes.TrailingData,
                        $"{reader.Remaining} byte(s) follow the End of Track meta event.");
                }

                break;
            }
        }

        if (!sawEndOfTrack)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedChild,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Track at offset {start} ended without an End of Track meta event."),
                dataRange);
        }

        return new FormatNode(
            "MTrk",
            NodeKind.Container,
            SourceRange.FromBounds(start, end),
            dataRange,
            events.ToImmutable(),
            MidiParser.IdValue,
            string.Create(CultureInfo.InvariantCulture, $"midi:track:{start}"));
    }

    private bool TryReadChunkHeader(long position, out byte[] id, out uint length)
    {
        id = [];
        length = 0;
        if (_source.Length - position < MidiConstants.ChunkHeaderLength)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[MidiConstants.ChunkHeaderLength];
        if (SourceRead.ReadAtMost(_source, position, header) != header.Length)
        {
            return false;
        }

        id = header[..4].ToArray();
        length = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        return true;
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
            var range = SourceRange.FromBounds(start, end);
            children.Add(new FormatNode(name, NodeKind.Unparsed, range, range));
        }

        _diagnostics.Warning(code, message, SourceRange.FromBounds(start, end));
    }

    private bool IsZeroHeader(long position)
    {
        Span<byte> header = stackalloc byte[MidiConstants.ChunkHeaderLength];
        return SourceRead.ReadAtMost(_source, position, header) == header.Length
            && header.IndexOfAnyExcept((byte)0) < 0;
    }

    private bool LooksLikeZeroFill(long position)
    {
        var count = (int)Math.Min(512, _source.Length - position);
        if (count <= 0)
        {
            return false;
        }

        Span<byte> probe = stackalloc byte[512];
        var read = SourceRead.ReadAtMost(_source, position, probe[..count]);
        return read > 0 && probe[..read].IndexOfAnyExcept((byte)0) < 0;
    }

    private static FormatNode CreateRoot(SourceRange range, ImmutableArray<FormatNode> children) =>
        new(
            MidiParser.DisplayNameValue,
            NodeKind.Container,
            range,
            range,
            children,
            MidiParser.IdValue,
            "midi-root");
}
