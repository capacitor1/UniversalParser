using System.Buffers.Binary;
using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Flv;

/// <summary>
/// Builds the FLV header, PreviousTagSize0, and tag tree from tag headers only. Tag bodies are
/// re-read lazily by <see cref="FlvNodeParser"/>; no payload is copied into the tree.
/// </summary>
internal sealed class FlvDocumentParser : IDisposable
{
    private readonly ParseContext _context;
    private readonly IRandomAccessSource _source;
    private readonly BufferedReadSource? _window;
    private readonly DiagnosticBag _diagnostics;

    public FlvDocumentParser(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        // Tag headers are tiny and frequent; a read-ahead window keeps the scan sequential.
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
        if (_source.Length < FlvConstants.FileHeaderLength)
        {
            AddUnparsed(
                children,
                0,
                _source.Length,
                "<unparsed data>",
                DiagnosticCodes.TruncatedHeader,
                $"The file is shorter than the {FlvConstants.FileHeaderLength}-byte FLV header.");
            return CreateRoot(fileRange, children.ToImmutable());
        }

        Span<byte> header = stackalloc byte[FlvConstants.FileHeaderLength];
        if (SourceRead.ReadAtMost(_source, 0, header) < FlvConstants.FileHeaderLength)
        {
            AddUnparsed(
                children,
                0,
                _source.Length,
                "<unparsed data>",
                DiagnosticCodes.TruncatedHeader,
                "The FLV header could not be read completely.");
            return CreateRoot(fileRange, children.ToImmutable());
        }

        if (!header[..3].SequenceEqual("FLV"u8))
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidSignature,
                "The file does not start with the FLV signature.",
                new SourceRange(0, 3));
        }

        var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(header[5..9]);
        var headerEnd = Math.Min(_source.Length, Math.Max(FlvConstants.FileHeaderLength, (long)dataOffset));
        if (dataOffset < FlvConstants.FileHeaderLength)
        {
            _diagnostics.Warning(
                DiagnosticCodes.InvalidLength,
                $"DataOffset ({dataOffset}) is smaller than the minimum FLV header size ({FlvConstants.FileHeaderLength}); using {FlvConstants.FileHeaderLength}.",
                new SourceRange(5, 4));
        }
        else if (dataOffset > _source.Length)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"DataOffset ({dataOffset}) points beyond the end of the file ({_source.Length}).",
                new SourceRange(5, 4));
        }

        children.Add(new FormatNode(
            "FLVHeader",
            NodeKind.Leaf,
            SourceRange.FromBounds(0, headerEnd),
            stableId: "flv:header"));

        var position = headerEnd;
        if (position + FlvConstants.PreviousTagSizeLength <= _source.Length)
        {
            children.Add(new FormatNode(
                "PreviousTagSize0",
                NodeKind.Leaf,
                new SourceRange(position, FlvConstants.PreviousTagSizeLength),
                stableId: "flv:prev0"));
            position += FlvConstants.PreviousTagSizeLength;
        }
        else
        {
            AddUnparsed(
                children,
                position,
                _source.Length,
                "<trailing data>",
                DiagnosticCodes.TruncatedHeader,
                "PreviousTagSize0 is missing or truncated.");
            return CreateRoot(fileRange, children.ToImmutable());
        }

        while (position < _source.Length)
        {
            _context.ThrowIfCancellationRequested();
            _context.ReportProgress("FLV", position, _source.Length);

            if (_source.Length - position < FlvConstants.TagHeaderLength)
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<trailing data>",
                    DiagnosticCodes.TruncatedHeader,
                    $"{_source.Length - position} trailing byte(s) cannot form an FLV tag header.");
                break;
            }

            // An incomplete download leaves a run of 0x00 bytes. A zero tag header would otherwise
            // advance by 15 bytes and create tens of thousands of empty tag nodes.
            if (IsZeroTagHeader(position) && LooksLikeZeroFill(position))
            {
                AddUnparsed(
                    children,
                    position,
                    _source.Length,
                    "<zero fill>",
                    DiagnosticCodes.InvalidLength,
                    $"{_source.Length - position:N0} byte(s) of zero fill (probably an incomplete download) cannot form an FLV tag; parsing stopped.");
                break;
            }

            var reader = new SourceReader(_source, position, FlvConstants.TagHeaderLength);
            var tagTypeByte = reader.ReadByte();
            var tagType = (byte)(tagTypeByte & 0x1F);
            var filter = (byte)((tagTypeByte >> 5) & 0x01);
            var dataSize = reader.ReadUInt24(Endian.Big);
            _ = reader.ReadUInt24(Endian.Big);
            _ = reader.ReadByte();
            _ = reader.ReadUInt24(Endian.Big);

            var tag = BuildTag(
                position,
                tagType,
                filter,
                dataSize,
                out var tagEnd);
            children.Add(tag);
            if (tagEnd <= position)
            {
                _diagnostics.Warning(
                    DiagnosticCodes.InvalidLength,
                    "The FLV tag end does not advance past its header.",
                    SourceRange.FromBounds(position, _source.Length));
                break;
            }

            position = tagEnd;
        }

        return CreateRoot(fileRange, children.ToImmutable());
    }

    private FormatNode BuildTag(
        long offset,
        byte tagType,
        byte filter,
        uint dataSize,
        out long tagEnd)
    {
        var dataStart = offset + FlvConstants.TagHeaderLength;
        var declaredDataEnd = dataStart + dataSize;
        var dataEnd = Math.Min(declaredDataEnd, _source.Length);
        var truncatedPayload = declaredDataEnd > _source.Length;
        var hasPreviousTagSize = dataEnd + FlvConstants.PreviousTagSizeLength <= _source.Length;
        tagEnd = hasPreviousTagSize
            ? dataEnd + FlvConstants.PreviousTagSizeLength
            : dataEnd;

        if (truncatedPayload)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"The tag declares {dataSize} data byte(s) but only {_source.Length - dataStart} are available.",
                SourceRange.FromBounds(offset, _source.Length));
        }
        else if (!hasPreviousTagSize)
        {
            _diagnostics.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The tag's PreviousTagSize field is missing or truncated.",
                SourceRange.FromBounds(dataEnd, _source.Length));
        }

        var expectedPreviousTagSize = FlvConstants.TagHeaderLength + (long)dataSize;
        var children = ImmutableArray.CreateBuilder<FormatNode>();
        children.Add(new FormatNode(
            "TagHeader",
            NodeKind.Leaf,
            new SourceRange(offset, FlvConstants.TagHeaderLength),
            stableId: $"flv:tag-header:{tagType}:{offset}"));

        if (dataEnd > dataStart)
        {
            var known = tagType is FlvConstants.TagTypeAudio or FlvConstants.TagTypeVideo or FlvConstants.TagTypeScript;
            children.Add(new FormatNode(
                BuildDataName(tagType),
                known ? NodeKind.Leaf : NodeKind.Unparsed,
                SourceRange.FromBounds(dataStart, dataEnd),
                stableId: $"flv:tag-data:{tagType}:{filter}:{offset}"));
        }

        if (hasPreviousTagSize)
        {
            children.Add(new FormatNode(
                "PreviousTagSize",
                NodeKind.Leaf,
                new SourceRange(dataEnd, FlvConstants.PreviousTagSizeLength),
                stableId: $"flv:prev-size:{expectedPreviousTagSize}:{offset}"));
        }

        return CreateTagNode(offset, tagEnd, tagType, children.ToImmutable());
    }

    private static FormatNode CreateTagNode(long offset, long end, byte tagType, ImmutableArray<FormatNode> children) =>
        new(
            BuildTagName(tagType),
            NodeKind.Container,
            SourceRange.FromBounds(offset, end),
            children: children,
            formatId: FlvParser.IdValue,
            stableId: $"flv:tag:{tagType}:{offset}");

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

    private bool IsZeroTagHeader(long position)
    {
        Span<byte> header = stackalloc byte[FlvConstants.TagHeaderLength];
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
            "Flash Video",
            NodeKind.Container,
            range,
            range,
            children,
            FlvParser.IdValue,
            "flv-root");

    internal static string BuildTagName(byte tagType) => tagType switch
    {
        FlvConstants.TagTypeAudio => "AudioTag",
        FlvConstants.TagTypeVideo => "VideoTag",
        FlvConstants.TagTypeScript => "ScriptTag",
        _ => $"0x{tagType:X2}",
    };

    internal static string BuildDataName(byte tagType) => tagType switch
    {
        FlvConstants.TagTypeAudio => "AudioTagData",
        FlvConstants.TagTypeVideo => "VideoTagData",
        FlvConstants.TagTypeScript => "ScriptTagData",
        _ => "UnknownTagData",
    };

    internal static string BuildTagTitle(byte tagType) => tagType switch
    {
        FlvConstants.TagTypeAudio => "AudioTag '0x08'",
        FlvConstants.TagTypeVideo => "VideoTag '0x09'",
        FlvConstants.TagTypeScript => "ScriptTag '0x12'",
        _ => $"UnknownTag '0x{tagType:X2}'",
    };

    internal static string BuildDataTitle(byte tagType) => tagType switch
    {
        FlvConstants.TagTypeAudio => "AudioTagData '0x08'",
        FlvConstants.TagTypeVideo => "VideoTagData '0x09'",
        FlvConstants.TagTypeScript => "ScriptTagData '0x12'",
        _ => $"UnknownTagData '0x{tagType:X2}'",
    };
}
