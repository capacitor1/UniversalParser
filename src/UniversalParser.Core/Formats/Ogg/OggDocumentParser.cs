using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Ogg;

/// <summary>
/// Builds the physical Ogg page tree. Only the fixed page header and the segment table are
/// read here; payload bytes are read lazily. A capture pattern mismatch is recovered by
/// scanning forward for the next "OggS" page, and the skipped bytes stay visible as an
/// explicit unparsed node.
/// </summary>
internal sealed class OggDocumentParser : IDisposable
{
    private const int ScanBufferSize = 64 * 1024;

    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly BufferedReadSource? _window;
    private readonly DiagnosticBag _diagnostics;

    public OggDocumentParser(ParseContext context)
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
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var position = 0L;

        while (position < _source.Length)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("Ogg", position, _source.Length);

            if (!OggPage.TryRead(_source, position, _source.Length, out var page, out var error))
            {
                if (error == OggPageReadError.NotPage)
                {
                    var next = FindNextPage(position);
                    if (next < 0)
                    {
                        AddUnparsed(
                            children,
                            position,
                            _source.Length,
                            DiagnosticCodes.InvalidSignature,
                            "No Ogg page capture pattern remains in the rest of the file.");
                        position = _source.Length;
                        break;
                    }

                    AddUnparsed(
                        children,
                        position,
                        next,
                        DiagnosticCodes.InvalidSignature,
                        $"{next - position} byte(s) before the next Ogg page are not part of a page.");
                    position = next;
                    continue;
                }

                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    DiagnosticCodes.TruncatedHeader,
                    "The Ogg page header is truncated.");
                position = _source.Length;
                break;
            }

            if (page.Version != 0)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.UnsupportedVersion,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The Ogg stream structure version is {page.Version} instead of 0."),
                    new SourceRange(page.Start + 4, 1));
            }

            if ((page.HeaderType & OggPage.BosFlag) != 0 && (page.HeaderType & OggPage.ContinuedFlag) != 0)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    "A beginning-of-stream page also has the continued-packet flag.",
                    new SourceRange(page.Start + 5, 1));
            }

            if (page.Truncated)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    "The Ogg page payload extends beyond the end of the file; the page range is clamped.",
                    SourceRange.FromBounds(page.Start, page.End));
            }

            children.Add(new FormatNode(
                "Page",
                NodeKind.Leaf,
                SourceRange.FromBounds(page.Start, page.End),
                formatId: OggParser.IdValue,
                stableId: string.Create(CultureInfo.InvariantCulture, $"ogg:page:{page.Start}")));

            position = page.End;
            if (page.Truncated)
            {
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
                $"{_source.Length - position} byte(s) after the last complete page are not interpreted.");
        }

        var range = new SourceRange(0, _source.Length);
        return new FormatNode(
            OggParser.DisplayNameValue,
            NodeKind.Container,
            range,
            range,
            children.ToImmutable(),
            OggParser.IdValue,
            "ogg-root");
    }

    private long FindNextPage(long start)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ScanBufferSize);
        try
        {
            var position = start;
            while (position + OggPage.CapturePatternLength <= _source.Length)
            {
                _context.ThrowIfCancellationRequested();
                _context.ReportProgress("Ogg scan", position, _source.Length);

                var count = (int)Math.Min(buffer.Length, _source.Length - position);
                var read = SourceRead.ReadAtMost(_source, position, buffer.AsSpan(0, count));
                if (read < OggPage.CapturePatternLength)
                {
                    return -1;
                }

                var span = buffer.AsSpan(0, read);
                var index = span.IndexOf(OggPage.CapturePattern);
                if (index < 0)
                {
                    position += read - (OggPage.CapturePatternLength - 1);
                    continue;
                }

                var candidate = position + index;
                if (IsPlausiblePage(candidate))
                {
                    return candidate;
                }

                position = candidate + 1;
            }

            return -1;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private bool IsPlausiblePage(long position) =>
        OggPage.TryRead(_source, position, _source.Length, out var page, out var error)
        && error != OggPageReadError.NotPage
        && page.Version == 0;

    private void AddUnparsed(
        ImmutableArray<FormatNode>.Builder children,
        long start,
        long end,
        string code,
        string message)
    {
        if (start < end)
        {
            children.Add(new FormatNode(
                "<unparsed data>",
                NodeKind.Unparsed,
                SourceRange.FromBounds(start, end)));
        }

        _diagnostics.Warning(code, message, SourceRange.FromBounds(start, end));
    }
}
