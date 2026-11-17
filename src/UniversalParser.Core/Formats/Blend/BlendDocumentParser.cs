using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Blend;

/// <summary>
/// Builds the uncompressed blend block tree. Block headers are scanned first so that the DNA1
/// table (which may appear after the blocks it describes) can name every block. Payload fields
/// are read lazily.
/// </summary>
internal sealed class BlendDocumentParser
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly DiagnosticBag _diagnostics;

    public BlendDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _source = context.Source;
        _diagnostics = context.Diagnostics;
    }

    public FormatNode Parse()
    {
        if (!BlendHeaderReader.TryRead(_source, out var header, out var headerError))
        {
            _diagnostics.Error(
                DiagnosticCodes.InvalidSignature,
                $"The Blender file header could not be read ({headerError}).",
                new SourceRange(0, _source.Length));
            return FailureRoot();
        }

        var blocks = ScanBlocks(
            header,
            out var dnaBlock,
            out var remainderStart,
            out var remainderCode,
            out var remainderMessage);

        BlendDna? dna = null;
        if (dnaBlock is { } dnaHeader && !dnaHeader.Truncated)
        {
            dna = BlendDna.TryParse(
                _source,
                dnaHeader.DataStart,
                dnaHeader.DataLength,
                header.Endian,
                _context.CancellationToken,
                out var dnaError);
            if (dna is null)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.SchemaMismatch,
                    $"The DNA1 block could not be parsed: {dnaError}",
                    SourceRange.FromBounds(dnaHeader.Start, dnaHeader.End));
            }
        }
        else
        {
            _diagnostics.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The blend file has no DNA1 block; block structures cannot be named.",
                new SourceRange(0, _source.Length));
        }

        var dnaStart = dnaBlock?.DataStart ?? 0;
        var dnaLength = dnaBlock?.DataLength ?? 0;
        var children = ImmutableArray.CreateBuilder<FormatNode>(blocks.Count + 1);

        foreach (var block in blocks)
        {
            var kind = block.Code switch
            {
                "DNA1" or "GLOB" or "TEST" or "ENDB" => NodeKind.Leaf,
                _ => NodeKind.Unparsed,
            };
            var stableId = block.Code switch
            {
                "DNA1" => string.Create(CultureInfo.InvariantCulture, $"blend:dna:{block.Start}"),
                "GLOB" => string.Create(
                    CultureInfo.InvariantCulture,
                    $"blend:glob:{block.Start}:{dnaStart}:{dnaLength}"),
                "TEST" => string.Create(CultureInfo.InvariantCulture, $"blend:test:{block.Start}"),
                "ENDB" => string.Create(CultureInfo.InvariantCulture, $"blend:endb:{block.Start}"),
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"blend:block:{block.Start}:{block.SdnaIndex}:{dna?.GetStructName(block.SdnaIndex) ?? string.Empty}"),
            };

            children.Add(new FormatNode(
                block.Code,
                kind,
                SourceRange.FromBounds(block.Start, block.End),
                formatId: BlendParser.IdValue,
                stableId: stableId));
        }

        if (remainderStart < _source.Length)
        {
            var name = remainderCode == DiagnosticCodes.TrailingData ? "<trailing data>" : "<unparsed data>";
            children.Add(new FormatNode(
                name,
                NodeKind.Unparsed,
                SourceRange.FromBounds(remainderStart, _source.Length)));
            _diagnostics.Warning(
                remainderCode ?? DiagnosticCodes.TruncatedHeader,
                remainderMessage ?? "The remaining blend data is not interpreted.",
                SourceRange.FromBounds(remainderStart, _source.Length));
        }

        var range = new SourceRange(0, _source.Length);
        var payloadRange = new SourceRange(
            Math.Min(header.HeaderSize, _source.Length),
            Math.Max(0, _source.Length - header.HeaderSize));
        return new FormatNode(
            BlendParser.DisplayNameValue,
            NodeKind.Container,
            range,
            payloadRange,
            children.ToImmutable(),
            BlendParser.IdValue,
            "blend-root");
    }

    private List<BlendBlockHeader> ScanBlocks(
        BlendHeader header,
        out BlendBlockHeader? dnaBlock,
        out long remainderStart,
        out string? remainderCode,
        out string? remainderMessage)
    {
        var blocks = new List<BlendBlockHeader>();
        dnaBlock = null;
        remainderStart = _source.Length;
        remainderCode = null;
        remainderMessage = null;
        var position = (long)header.HeaderSize;
        var sawEnd = false;

        while (position < _source.Length)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("Blender", position, _source.Length);

            if (!BlendBlockReader.TryRead(_source, position, _source.Length, header, out var block, out var error))
            {
                remainderStart = position;
                remainderCode = error == BlendBlockReadError.InvalidLength
                    ? DiagnosticCodes.InvalidLength
                    : DiagnosticCodes.TruncatedHeader;
                remainderMessage = error == BlendBlockReadError.InvalidLength
                    ? "A blend block declares a negative length or count."
                    : "A blend block header is truncated.";
                break;
            }

            blocks.Add(block);
            if (block.Code == "DNA1" && dnaBlock is null)
            {
                dnaBlock = block;
            }

            if (block.Code == "ENDB" && block.Length != 0)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.InvalidLength,
                    "The ENDB block has a non-zero data length.",
                    SourceRange.FromBounds(block.Start, block.End));
            }

            if (block.Truncated)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    "The blend block extends beyond the end of the file; its range is clamped.",
                    SourceRange.FromBounds(block.Start, block.End));
                position = _source.Length;
                break;
            }

            position = block.End;
            if (block.Code == "ENDB")
            {
                sawEnd = true;
                break;
            }
        }

        if (!sawEnd)
        {
            _diagnostics.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The blend file has no ENDB block.",
                new SourceRange(0, _source.Length));
        }

        if (position < _source.Length)
        {
            remainderStart = position;
            remainderCode = sawEnd ? DiagnosticCodes.TrailingData : remainderCode ?? DiagnosticCodes.TruncatedHeader;
            remainderMessage = sawEnd
                ? $"{_source.Length - position} byte(s) follow the ENDB block."
                : remainderMessage ?? "The remaining blend data is not interpreted.";
        }

        return blocks;
    }

    private FormatNode FailureRoot()
    {
        var range = new SourceRange(0, _source.Length);
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        if (_source.Length > 0)
        {
            children.Add(new FormatNode(
                "<unparsed data>",
                NodeKind.Unparsed,
                range));
        }

        return new FormatNode(
            BlendParser.DisplayNameValue,
            NodeKind.Container,
            range,
            range,
            children.ToImmutable(),
            BlendParser.IdValue,
            "blend-root");
    }
}
