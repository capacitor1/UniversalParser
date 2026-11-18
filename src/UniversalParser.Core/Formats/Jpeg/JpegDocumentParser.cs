using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Jpeg;

/// <summary>
/// Builds the JPEG marker tree from marker headers only. Entropy-coded scan data becomes one
/// explicit SCAN range per SOS segment; it is never decoded. Truncated segments and bytes after
/// EOI stay visible as explicit nodes.
/// </summary>
internal sealed class JpegDocumentParser : IDisposable
{
    private const int ScanBufferSize = 64 * 1024;

    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly BufferedReadSource? _window;
    private readonly DiagnosticBag _diagnostics;

    public JpegDocumentParser(ParseContext context)
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
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var position = 0L;
        var seenSoi = false;
        var seenEoi = false;

        while (position < _source.Length)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("JPEG", position, _source.Length);

            if (!JpegMarker.TryRead(_source, position, _source.Length, out var marker, out var error))
            {
                AddMarkerError(children, position, marker, error);
                position = _source.Length;
                break;
            }

            if (marker.Code == JpegConstants.Soi)
            {
                if (seenSoi)
                {
                    _diagnostics.Warning(
                        DiagnosticCodes.InvalidSignature,
                        "A second SOI marker was found.",
                        new SourceRange(marker.Start, marker.End - marker.Start));
                }

                seenSoi = true;
            }
            else if (!seenSoi)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.InvalidSignature,
                    "The JPEG stream does not start with an SOI marker.",
                    new SourceRange(marker.Start, marker.End - marker.Start));
            }

            var parsed = JpegConstants.IsApplication(marker.Code)
                ? JpegApplicationDispatcher.HasParser(_context, new SourceRange(marker.PayloadOffset, marker.PayloadLength))
                : JpegConstants.IsKnown(marker.Code);
            var kind = parsed ? NodeKind.Leaf : NodeKind.Unparsed;
            AddNode(
                children,
                JpegConstants.GetName(marker.Code),
                kind,
                marker.Start,
                marker.End,
                "jpeg:marker",
                marker.Start);
            position = marker.End;

            if (marker.Code == JpegConstants.Eoi)
            {
                seenEoi = true;
                break;
            }

            if (marker.Code == JpegConstants.Sos)
            {
                var scanStart = marker.End;
                var scanEnd = FindScanEnd(scanStart);
                if (scanEnd > scanStart)
                {
                    AddNode(children, "SCAN", NodeKind.Leaf, scanStart, scanEnd, "jpeg:scan", scanStart);
                }

                position = scanEnd;
            }
        }

        if (!seenSoi)
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidSignature,
                "The JPEG stream contains no SOI marker.",
                fileRange);
        }

        if (!seenEoi)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedChild,
                "The JPEG stream ended without an EOI marker.",
                new SourceRange(_source.Length, 0));
        }

        if (seenEoi && position < _source.Length)
        {
            AddUnparsed(
                children,
                position,
                _source.Length,
                "<trailing data>",
                DiagnosticCodes.TrailingData,
                $"{_source.Length - position} byte(s) follow the EOI marker.");
        }

        return CreateRoot(fileRange, children.ToImmutable());
    }

    private void AddMarkerError(
        ImmutableArray<FormatNode>.Builder children,
        long position,
        JpegMarker marker,
        JpegMarkerError error)
    {
        var start = error is JpegMarkerError.NotMarker or JpegMarkerError.FillOnly or JpegMarkerError.StuffedByte
            ? position
            : marker.Start;
        var end = _source.Length;
        var (code, message) = error switch
        {
            JpegMarkerError.NotMarker =>
                (DiagnosticCodes.InvalidSignature, "Expected a 0xFF JPEG marker prefix."),
            JpegMarkerError.FillOnly =>
                (DiagnosticCodes.TruncatedHeader, "The stream ends with 0xFF fill bytes and no marker code."),
            JpegMarkerError.StuffedByte =>
                (DiagnosticCodes.InvalidSignature, "Unexpected stuffed 0xFF00 byte outside entropy-coded data."),
            JpegMarkerError.MissingLength =>
                (DiagnosticCodes.TruncatedHeader, "The marker is missing its two-byte length field."),
            JpegMarkerError.InvalidLength =>
                (DiagnosticCodes.InvalidLength, "The segment length is smaller than the two-byte length field."),
            JpegMarkerError.Truncated =>
                (DiagnosticCodes.TruncatedPayload, "The segment extends beyond the end of the file."),
            _ => (DiagnosticCodes.TruncatedHeader, "The marker could not be read."),
        };
        AddUnparsed(children, start, end, "<unparsed data>", code, message);
    }

    private long FindScanEnd(long start)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ScanBufferSize);
        try
        {
            var position = start;
            var candidate = -1L;
            while (position < _source.Length)
            {
                _context.ThrowIfCancellationRequested();
                _context.ReportProgress("JPEG scan", position, _source.Length);
                var count = (int)Math.Min(buffer.Length, _source.Length - position);
                var read = SourceRead.ReadAtMost(_source, position, buffer.AsSpan(0, count));
                if (read <= 0)
                {
                    return _source.Length;
                }

                var span = buffer.AsSpan(0, read);
                var index = 0;
                while (index < read)
                {
                    if (candidate < 0)
                    {
                        var found = span[index..].IndexOf(JpegConstants.MarkerPrefix);
                        if (found < 0)
                        {
                            break;
                        }

                        index += found;
                        candidate = position + index;
                    }

                    while (index < read && span[index] == JpegConstants.MarkerPrefix)
                    {
                        index++;
                    }

                    if (index >= read)
                    {
                        // The fill-byte run continues in the next window; keep its start.
                        break;
                    }

                    var code = span[index];
                    if (code == 0x00 || code is >= 0xD0 and <= 0xD7)
                    {
                        // Stuffed byte or restart marker: entropy data continues after it.
                        index++;
                        candidate = -1;
                        continue;
                    }

                    return candidate;
                }

                position += read;
            }

            return _source.Length;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void AddNode(
        ImmutableArray<FormatNode>.Builder children,
        string name,
        NodeKind kind,
        long start,
        long end,
        string stableIdPrefix,
        long stableOffset)
    {
        if (start >= end)
        {
            return;
        }

        children.Add(new FormatNode(
            name,
            kind,
            SourceRange.FromBounds(start, end),
            formatId: JpegParser.IdValue,
            stableId: string.Create(
                CultureInfo.InvariantCulture,
                $"{stableIdPrefix}:{stableOffset}")));
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
            "JPEG Image",
            NodeKind.Container,
            range,
            range,
            children,
            JpegParser.IdValue,
            "jpeg-root");
}
