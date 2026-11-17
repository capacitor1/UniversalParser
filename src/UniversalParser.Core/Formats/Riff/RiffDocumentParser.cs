using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>
/// Builds the immutable RIFF tree. The document root is synthetic and spans the whole file;
/// real root chunks and any trailing bytes are explicit children, so there are no invisible gaps.
/// </summary>
internal sealed class RiffDocumentParser
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly DiagnosticBag _diagnostics;
    private Endian _endian = Endian.Little;
    private RiffDs64Info? _ds64;

    public RiffDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _source = context.Source;
        _diagnostics = context.Diagnostics;
    }

    public FormatNode Parse()
    {
        _endian = DetectRootEndian();
        var fileRange = new SourceRange(0, _source.Length);
        var children = ParseRange(0, _source.Length, _endian, parentType: null, isTopLevel: true);
        return new FormatNode(
            "Resource Interchange File Format",
            NodeKind.Container,
            fileRange,
            fileRange,
            children,
            RiffParser.IdValue,
            "riff-root");
    }

    private Endian DetectRootEndian()
    {
        if (!RiffRead.TryReadChunkHeader(_source, 0, _source.Length, Endian.Little, out var id, out _))
        {
            return Endian.Little;
        }

        return id == "RIFX" ? Endian.Big : Endian.Little;
    }

    private ImmutableArray<FormatNode> ParseRange(long start, long end, Endian endian, string? parentType, bool isTopLevel)
    {
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        var position = start;

        while (position < end)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("RIFF", position, _source.Length);

            if (end - position < 8)
            {
                AddTrailing(children, position, end, isTopLevel);
                break;
            }

            var layout = ResolveChunk(position, end, endian);
            if (layout is null)
            {
                AddTrailing(children, position, end, isTopLevel);
                break;
            }

            // An incomplete download leaves a run of 0x00 bytes. Treating that as an 8-byte chunk
            // would create millions of empty nodes, so stop at the zero fill instead.
            if (IsZeroId(layout.Value.Id) && LooksLikeZeroFill(position, end))
            {
                AddZeroFill(children, position, end);
                break;
            }

            var node = BuildChunkNode(layout.Value, endian, parentType);

            if (layout.Value.NodeEnd <= position)
            {
                AddTrailing(children, position, end, isTopLevel);
                break;
            }

            children.Add(node);
            position = layout.Value.NodeEnd;
        }

        return children.ToImmutable();
    }

    private RiffChunkLayout? ResolveChunk(long position, long containerEnd, Endian endian)
    {
        if (!RiffRead.TryReadChunkHeader(_source, position, containerEnd, endian, out var id, out var rawSize))
        {
            return null;
        }

        var resolvedSize = (long)rawSize;
        if (id is "RF64" or "BW64")
        {
            _ds64 = RiffDs64Info.TryRead(_source, position + 12, containerEnd, _diagnostics);
        }
        else if (id is "RIFF" or "RIFX")
        {
            _ds64 = null;
        }

        if (rawSize == uint.MaxValue)
        {
            if (id is "RF64" or "BW64")
            {
                if (_ds64 is not null)
                {
                    resolvedSize = ClampToLong(_ds64.RiffSize);
                }
                else
                {
                    resolvedSize = Math.Max(0, containerEnd - (position + 8));
                    _diagnostics.Warning(
                        DiagnosticCodes.TruncatedPayload,
                        $"'{id}' has a 0xFFFFFFFF size but no usable 'ds64' chunk; falling back to the available file length.",
                        SourceRange.FromBounds(position, containerEnd));
                }
            }
            else if (id == "data" && _ds64 is not null)
            {
                resolvedSize = ClampToLong(_ds64.DataSize);
                if (resolvedSize == 0 && _ds64.TryGetSize("data", out var tableSize))
                {
                    resolvedSize = ClampToLong(tableSize);
                }
            }
            else
            {
                resolvedSize = Math.Max(0, containerEnd - (position + 8));
                _diagnostics.Warning(
                    DiagnosticCodes.InvalidLength,
                    $"Chunk '{RiffFormatting.FormatFourCc(id)}' has a 0xFFFFFFFF size but no applicable ds64 entry; using the available range.",
                    SourceRange.FromBounds(position, containerEnd));
            }
        }

        var layout = RiffChunkLayout.Create(id, position, rawSize, resolvedSize, containerEnd);
        return layout;
    }

    private FormatNode BuildChunkNode(RiffChunkLayout layout, Endian endian, string? parentType)
    {
        var isRoot = IsRootChunk(layout.Id);
        var isList = layout.Id == "LIST";
        var isAnmf = layout.Id == "ANMF";
        var isNestedContainer = layout.Id is "ins " or "lrgn" or "rgn " or "rgn2" or "wave" or "wavl";
        var typeLength = isRoot || isList ? 4 : isAnmf ? 16 : 0;
        var type = isRoot || isList ? ReadFourCc(layout.PayloadStart, layout.PayloadEnd) : null;
        // AVI movie data is a huge sequence of chunks that is never interpreted, so keep the whole
        // LIST:movi as one unparsed block instead of building thousands of useless nodes.
        var isMoviList = isList && type == "movi";
        var isContainer = isRoot || (isList && !isMoviList) || isAnmf || isNestedContainer;

        if (layout.Truncated)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"Chunk '{RiffFormatting.FormatFourCc(layout.Id)}' declares {layout.ResolvedSize} byte(s) but only {layout.PayloadRange.Length} are available.",
                layout.NodeRange);
        }

        var childStart = Math.Min(layout.PayloadStart + typeLength, layout.PayloadEnd);
        var childEnd = layout.PayloadEnd;

        if ((isRoot || isList) && type is null)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"'{layout.Id}' payload is too short for its 4-byte type field.",
                layout.NodeRange);
        }

        var childEndian = isRoot ? (layout.Id == "RIFX" ? Endian.Big : Endian.Little) : endian;
        ImmutableArray<FormatNode> children = [];
        if (isContainer && childStart < childEnd)
        {
            children = ParseRange(childStart, childEnd, childEndian, type, isTopLevel: false);
        }

        var kind = isMoviList
            ? NodeKind.Unparsed
            : isContainer
                ? NodeKind.Container
                : RiffChunkHandlers.IsKnown(layout.Id)
                    ? NodeKind.Leaf
                    : RiffChunkNames.IsKnown(layout.Id) ? NodeKind.Raw : NodeKind.Unparsed;
        var name = BuildName(layout.Id, type);
        var payloadRange = isContainer
            ? SourceRange.FromBounds(childStart, childEnd)
            : layout.PayloadRange;
        var stableId = string.Create(
            CultureInfo.InvariantCulture,
            $"riff:{(childEndian == Endian.Big ? "be" : "le")}:{layout.Id}:{parentType ?? string.Empty}");

        return new FormatNode(name, kind, layout.NodeRange, payloadRange, children, RiffParser.IdValue, stableId);
    }

    private static bool IsZeroId(string id)
    {
        foreach (var value in id)
        {
            if (value != '\0')
            {
                return false;
            }
        }

        return id.Length > 0;
    }

    private bool LooksLikeZeroFill(long position, long end)
    {
        var count = (int)Math.Min(512, end - position);
        if (count <= 0)
        {
            return false;
        }

        Span<byte> probe = stackalloc byte[512];
        var read = RiffRead.ReadAtMost(_source, position, probe[..count]);
        return read > 0 && probe[..read].IndexOfAnyExcept((byte)0) < 0;
    }

    private void AddZeroFill(ImmutableArray<FormatNode>.Builder children, long position, long end)
    {
        var range = SourceRange.FromBounds(position, end);
        children.Add(new FormatNode("<zero fill>", NodeKind.Unparsed, range, range));
        _diagnostics.Warning(
            DiagnosticCodes.InvalidLength,
            $"{range.Length:N0} byte(s) of zero fill (probably an incomplete download) cannot form a RIFF chunk; parsing stopped.",
            range);
    }

    private void AddTrailing(ImmutableArray<FormatNode>.Builder children, long position, long end, bool isTopLevel)
    {
        if (position >= end)
        {
            return;
        }

        var range = SourceRange.FromBounds(position, end);
        children.Add(new FormatNode("<trailing data>", NodeKind.Unparsed, range, range));
        _diagnostics.Warning(
            isTopLevel ? DiagnosticCodes.TruncatedHeader : DiagnosticCodes.TruncatedChild,
            isTopLevel
                ? $"{range.Length} trailing byte(s) do not form a complete RIFF chunk header."
                : $"{range.Length} trailing byte(s) do not form a complete child chunk header.",
            range);
    }

    private string? ReadFourCc(long position, long end)
    {
        if (end - position < 4)
        {
            return null;
        }

        Span<byte> bytes = stackalloc byte[4];
        return RiffRead.ReadAtMost(_source, position, bytes) == 4
            ? RiffFormatting.DecodeFourCc(bytes)
            : null;
    }

    private static string BuildName(string id, string? type)
    {
        if (IsRootChunk(id))
        {
            return type is null ? id : $"{id}:{RiffFormatting.FormatFourCc(type)}";
        }

        if (id == "LIST")
        {
            return type is null ? "LIST" : $"LIST:{RiffFormatting.FormatFourCc(type)}";
        }

        if (id == "ANMF")
        {
            return "ANMF";
        }

        return RiffFormatting.FormatFourCc(id);
    }

    internal static string BuildTitle(string id, string? parentType)
    {
        if (IsRootChunk(id))
        {
            var name = id switch
            {
                "RIFX" => "Rifx",
                "RF64" => "Rf64",
                "BW64" => "Bw64",
                _ => "Riff",
            };
            return $"{name} '{RiffFormatting.FormatFourCc(id)}'";
        }

        if (id == "LIST")
        {
            return $"List '{RiffFormatting.FormatFourCc(id)}'";
        }

        if (id == "ANMF")
        {
            return "AnimationFrame 'ANMF'";
        }

        return $"{RiffChunkNames.Get(id, parentType)} '{RiffFormatting.FormatFourCc(id)}'";
    }

    internal static bool IsRootChunk(string id) => id is "RIFF" or "RF64" or "BW64" or "RIFX";

    private static long ClampToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;
}
