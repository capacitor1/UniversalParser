using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Png;

/// <summary>
/// Builds the flat PNG chunk tree from chunk headers only. Chunk payloads are never read here, so
/// even a gigabyte IDAT costs one header read. Truncated, unknown, and trailing ranges stay visible
/// as explicit nodes.
/// </summary>
internal sealed class PngDocumentParser
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly DiagnosticBag _diagnostics;

    public PngDocumentParser(ParseContext context)
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
        var signatureLength = Math.Min(PngConstants.SignatureLength, _source.Length);
        if (signatureLength > 0)
        {
            children.Add(new FormatNode(
                "Signature",
                NodeKind.Leaf,
                new SourceRange(0, signatureLength),
                formatId: PngParser.IdValue,
                stableId: "png:signature"));
        }

        if (signatureLength < PngConstants.SignatureLength)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The PNG signature needs {PngConstants.SignatureLength} bytes but only {_source.Length} are available.",
                fileRange);
            return CreateRoot(fileRange, children.ToImmutable());
        }

        if (!HasValidSignature())
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidSignature,
                "The file does not start with the 8-byte PNG signature.",
                new SourceRange(0, PngConstants.SignatureLength));
        }

        var position = (long)PngConstants.SignatureLength;
        var chunkCount = 0;
        var seenHeader = false;
        var seenEnd = false;
        while (position < _source.Length)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("PNG", position, _source.Length);

            if (_source.Length - position < PngConstants.ChunkHeaderLength)
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<trailing data>",
                    DiagnosticCodes.TruncatedHeader,
                    $"{_source.Length - position} trailing byte(s) cannot form a PNG chunk header.");
                break;
            }

            if (!PngChunkHeader.TryRead(_source, position, _source.Length, out var header))
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<trailing data>",
                    DiagnosticCodes.TruncatedHeader,
                    "The PNG chunk header could not be read.");
                break;
            }

            // An incomplete download leaves a run of 0x00 bytes. Without this guard every 12 zero
            // bytes would become an empty chunk node.
            if (IsZeroHeader(position) && LooksLikeZeroFill(position))
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<zero fill>",
                    DiagnosticCodes.InvalidLength,
                    $"{_source.Length - position:N0} byte(s) of zero fill (probably an incomplete download) cannot form a PNG chunk; parsing stopped.");
                break;
            }

            var name = PngFormatting.FormatChunkType(header.Type);
            if (!header.IsLengthValid)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.InvalidLength,
                    $"Chunk length {header.Length} exceeds the PNG maximum of {PngConstants.MaxChunkLength}.",
                    new SourceRange(position, PngConstants.ChunkHeaderLength));
                AddChunk(children, name, NodeKind.Unparsed, position, _source.Length);
                break;
            }

            if (!header.IsComplete)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Chunk '{header.Type}' declares {header.Length} data byte(s), but only {_source.Length - header.DataOffset} are available."),
                    new SourceRange(position, _source.Length - position));
                AddChunk(children, name, NodeKind.Unparsed, position, _source.Length);
                break;
            }

            if (!seenHeader)
            {
                seenHeader = true;
                if (header.Type != "IHDR")
                {
                    _diagnostics.Warning(
                        DiagnosticCodes.InvalidSignature,
                        $"The first PNG chunk is '{PngFormatting.FormatChunkType(header.Type)}' instead of 'IHDR'.",
                        new SourceRange(position, PngConstants.ChunkHeaderLength));
                }
            }

            var kind = PngConstants.IsKnown(header.Type) ? NodeKind.Leaf : NodeKind.Unparsed;
            AddChunk(children, name, kind, position, header.End);
            chunkCount++;
            position = header.End;

            if (header.Type == "IEND")
            {
                seenEnd = true;
                break;
            }
        }

        if (!seenEnd)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedChild,
                "The PNG datastream ended without an IEND chunk.",
                new SourceRange(_source.Length, 0));
        }

        if (seenEnd && position < _source.Length)
        {
            AddUnparsed(
                children,
                position,
                _source.Length,
                "<trailing data>",
                DiagnosticCodes.TrailingData,
                $"{_source.Length - position} byte(s) follow the IEND chunk.");
        }

        if (chunkCount == 0 && _source.Length > PngConstants.SignatureLength)
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidSignature,
                "The PNG datastream contains no readable chunks.",
                new SourceRange(PngConstants.SignatureLength, _source.Length - PngConstants.SignatureLength));
        }

        return CreateRoot(fileRange, children.ToImmutable());
    }

    private bool IsZeroHeader(long position)
    {
        Span<byte> header = stackalloc byte[PngConstants.ChunkHeaderLength];
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

    private bool HasValidSignature()
    {
        Span<byte> signature = stackalloc byte[PngConstants.SignatureLength];
        return SourceRead.ReadAtMost(_source, 0, signature) == signature.Length
            && signature.SequenceEqual(PngConstants.Signature);
    }

    private static void AddChunk(
        ImmutableArray<FormatNode>.Builder children,
        string name,
        NodeKind kind,
        long start,
        long end)
    {
        if (start >= end)
        {
            return;
        }

        children.Add(new FormatNode(
            name,
            kind,
            SourceRange.FromBounds(start, end),
            formatId: PngParser.IdValue,
            stableId: string.Create(System.Globalization.CultureInfo.InvariantCulture, $"png:chunk:{start}")));
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
            "Portable Network Graphics",
            NodeKind.Container,
            range,
            range,
            children,
            PngParser.IdValue,
            "png-root");
}
