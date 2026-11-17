using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.IsoBmff;

/// <summary>
/// Builds the ISO BMFF box tree. Only box headers are read here; payload fields are read lazily
/// when a node is selected. Containers with a FullBox or entry-count prefix expose only the child
/// area as their payload range, so tree validation never reports invisible holes.
/// </summary>
internal sealed class IsoBmffDocumentParser
{
    private const int MinBoxSize = 8;

    private static readonly HashSet<string> ContainerBoxes = new(StringComparer.Ordinal)
    {
        "moov", "trak", "edts", "mdia", "minf", "dinf", "stbl", "mvex", "moof", "traf",
        "mfra", "udta", "ilst", "ipro", "sinf", "schi", "rinf", "fiin", "paen", "meco",
        "iprp", "ipco", "grpl", "strk", "strd", "cinf", "tref", "trgr", "hnti", "hinf",
        "----", "tapt", "meta", "iinf", "iref", "dref", "trep", "gmhd",
    };

    private static readonly HashSet<string> ForcedLeafBoxes = new(StringComparer.Ordinal)
    {
        "stsd", "mdat", "free", "skip", "wide", "uuid", "mere", "infe",
    };

    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly DiagnosticBag _diagnostics;

    public IsoBmffDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _source = context.Source;
        _diagnostics = context.Diagnostics;
    }

    public FormatNode Parse()
    {
        var fileRange = new SourceRange(0, _source.Length);
        var children = ParseBoxes(0, _source.Length, null, topLevel: true);
        return CreateRoot(fileRange, children);
    }

    private ImmutableArray<FormatNode> ParseBoxes(
        long start,
        long end,
        string? parentType,
        bool topLevel)
    {
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var position = start;
        var allowNumericType = parentType == "ilst";

        while (position + MinBoxSize <= end)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("ISOBMFF", position, _source.Length);

            if (!IsoBmffBoxHeader.TryRead(
                    _source,
                    position,
                    end,
                    allowNumericType,
                    out var header,
                    out var error))
            {
                AddUnparsedRemainder(children, position, end, error, topLevel);
                return children.ToImmutable();
            }

            var range = SourceRange.FromBounds(header.Start, header.End);
            if (header.Truncated)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Box '{header.Type}' declares a size beyond its parent; the range is clamped to the parent end."),
                    range);
            }

            var isContainer = !header.Truncated && IsContainer(header.Type, parentType);
            var payloadRange = new SourceRange(header.PayloadOffset, header.PayloadLength);
            var childNodes = ImmutableArray<FormatNode>.Empty;

            if (isContainer)
            {
                var childStart = ResolveChildStart(header, end);
                if (childStart < header.PayloadOffset)
                {
                    childStart = header.PayloadOffset;
                }

                if (childStart > header.End)
                {
                    childStart = header.End;
                }

                payloadRange = new SourceRange(childStart, header.End - childStart);
                if (childStart < header.End)
                {
                    childNodes = ParseBoxes(childStart, header.End, header.Type, topLevel: false);
                }
            }

            var kind = isContainer
                ? NodeKind.Container
                : IsoBmffBoxCatalog.IsKnown(header.Type, parentType)
                    ? NodeKind.Leaf
                    : NodeKind.Unparsed;

            children.Add(new FormatNode(
                header.Type,
                kind,
                range,
                payloadRange,
                childNodes,
                IsoBmffParser.IdValue,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"isobmff:{header.Start}|{parentType ?? string.Empty}")));

            position = header.End;
            if (header.Truncated)
            {
                return children.ToImmutable();
            }
        }

        if (position < end)
        {
            if (topLevel)
            {
                AddSynthetic(children, "<trailing data>", position, end, NodeKind.Unparsed);
                _diagnostics.Warning(
                    DiagnosticCodes.TrailingData,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{end - position} byte(s) follow the last complete box."),
                    SourceRange.FromBounds(position, end));
            }
            else
            {
                _diagnostics.Warning(
                    DiagnosticCodes.TruncatedHeader,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{end - position} byte(s) at the end of container '{parentType}' are too short for a box header."),
                    SourceRange.FromBounds(position, end));
                AddSynthetic(children, "<unparsed data>", position, end, NodeKind.Unparsed);
            }
        }

        return children.ToImmutable();
    }

    private long ResolveChildStart(IsoBmffBoxHeader header, long parentEnd)
    {
        var payloadStart = header.PayloadOffset;
        var boxEnd = Math.Min(header.End, parentEnd);

        switch (header.Type)
        {
            case "meta":
                if (LooksLikeBox(payloadStart, boxEnd))
                {
                    return payloadStart;
                }

                return payloadStart + 4 <= boxEnd ? payloadStart + 4 : boxEnd;

            case "iinf":
                {
                    var version = ReadByte(payloadStart);
                    return payloadStart + 4 + (version == 0 ? 2 : 4);
                }

            case "iref":
                return payloadStart + 4;

            case "dref":
                return payloadStart + 8;

            case "ipro":
            case "fiin":
                return payloadStart + 6;

            case "trep":
                return payloadStart + 8;

            default:
                return payloadStart;
        }
    }

    private bool LooksLikeBox(long position, long limit)
    {
        if (limit - position < MinBoxSize)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[8];
        if (SourceRead.ReadAtMost(_source, position, header) < header.Length)
        {
            return false;
        }

        var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
        if (!IsoBmffFourCc.IsValidType(header[4..8]))
        {
            return false;
        }

        if (size == 0)
        {
            return true;
        }

        if (size == 1)
        {
            return limit - position >= 16;
        }

        return size >= MinBoxSize && position + size <= limit;
    }

    private byte ReadByte(long position)
    {
        Span<byte> value = stackalloc byte[1];
        return SourceRead.ReadAtMost(_source, position, value) == 1 ? value[0] : (byte)0;
    }

    private void AddUnparsedRemainder(
        ImmutableArray<FormatNode>.Builder children,
        long start,
        long end,
        IsoBmffBoxHeaderError error,
        bool topLevel)
    {
        var code = error switch
        {
            IsoBmffBoxHeaderError.Truncated => DiagnosticCodes.TruncatedHeader,
            IsoBmffBoxHeaderError.InvalidSize => DiagnosticCodes.InvalidLength,
            IsoBmffBoxHeaderError.InvalidType => DiagnosticCodes.InvalidSignature,
            _ => DiagnosticCodes.TruncatedHeader,
        };
        var message = error switch
        {
            IsoBmffBoxHeaderError.Truncated => "A box header is truncated.",
            IsoBmffBoxHeaderError.InvalidSize => "A box declares a size smaller than its header.",
            IsoBmffBoxHeaderError.InvalidType => "A box type is not a printable four-character code.",
            _ => "The box header could not be read.",
        };

        _diagnostics.Warning(code, message, SourceRange.FromBounds(start, end));
        AddSynthetic(
            children,
            topLevel ? "<trailing data>" : "<unparsed data>",
            start,
            end,
            NodeKind.Unparsed);
    }

    private void AddSynthetic(
        ImmutableArray<FormatNode>.Builder children,
        string name,
        long start,
        long end,
        NodeKind kind)
    {
        if (start >= end)
        {
            return;
        }

        children.Add(new FormatNode(name, kind, SourceRange.FromBounds(start, end)));
    }

    private static FormatNode CreateRoot(SourceRange range, ImmutableArray<FormatNode> children) =>
        new(
            "ISO Base Media File Format",
            NodeKind.Container,
            range,
            range,
            children,
            IsoBmffParser.IdValue,
            "isobmff-root");

    private static bool IsContainer(string type, string? parentType)
    {
        if (ForcedLeafBoxes.Contains(type))
        {
            return false;
        }

        if (ContainerBoxes.Contains(type))
        {
            return true;
        }

        // Every child of 'ilst' is a metadata key atom (©nam, covr, ----, key[N]) that
        // itself contains 'data'/'mean'/'name' atoms.
        return parentType == "ilst";
    }
}
