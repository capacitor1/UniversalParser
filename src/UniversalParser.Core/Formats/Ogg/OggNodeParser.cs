using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Ogg;

/// <summary>
/// Re-reads one Ogg page on demand. Page header fields use RFC 3533 names; each complete packet
/// whose magic is a known media mapping is interpreted as <c>Packet[n].</c> fields. Audio and
/// video packet payloads are never decoded.
/// </summary>
internal static class OggNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "ogg-root")
        {
            return ParseRoot(context, node);
        }

        return ParsePage(context, node);
    }

    private static NodeParseResult ParseRoot(ParseContext context, FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<FileSize>", node.Range.Length, node.Range);

        var pages = node.Children
            .Where(static child => child.StableId?.StartsWith("ogg:page:", StringComparison.Ordinal) == true)
            .ToArray();
        fields.Derived("<PageCount>", pages.Length, node.Range);

        var streams = new List<StreamSummary>();
        Span<byte> prefix = stackalloc byte[16];
        for (var index = 0; index < pages.Length; index++)
        {
            context.ThrowIfCancellationRequested();
            var pageNode = pages[index];
            if (!OggPage.TryRead(context.Source, pageNode.Range.Offset, pageNode.Range.End, out var page, out _))
            {
                continue;
            }

            var isBos = (page.HeaderType & OggPage.BosFlag) != 0;
            var stream = FindStream(streams, page.SerialNumber);
            if (stream is null || (isBos && stream.SeenEos))
            {
                stream = new StreamSummary(page.SerialNumber, index);
                streams.Add(stream);
            }

            if (isBos && stream.Codec == "Unknown" && (page.HeaderType & OggPage.ContinuedFlag) == 0)
            {
                var count = OggPage.ReadPayloadPrefix(context.Source, page, prefix);
                var kind = OggCodec.Identify(prefix[..count]);
                if (kind != OggHeaderKind.Unknown)
                {
                    stream.Codec = OggCodec.GetDisplayName(OggCodec.GetCodec(kind));
                }
            }

            stream.PageCount++;
            stream.LastPage = index;
            if ((page.HeaderType & OggPage.EosFlag) != 0)
            {
                stream.SeenEos = true;
            }
        }

        fields.Derived("<LogicalStreamCount>", streams.Count, node.Range);
        for (var index = 0; index < streams.Count; index++)
        {
            var stream = streams[index];
            fields.Derived(
                $"<Stream[{index}].SerialNumber>",
                string.Create(CultureInfo.InvariantCulture, $"0x{stream.SerialNumber:X8}"),
                node.Range);
            fields.Derived($"<Stream[{index}].Codec>", stream.Codec, node.Range);
            fields.Derived($"<Stream[{index}].PageCount>", stream.PageCount, node.Range);
            fields.Derived($"<Stream[{index}].FirstPage>", stream.FirstPage, node.Range);
            fields.Derived($"<Stream[{index}].LastPage>", stream.LastPage, node.Range);
            fields.Derived($"<Stream[{index}].EndOfStream>", stream.SeenEos ? "yes" : "no", node.Range);
        }

        fields.Note("Synthetic document root; select a page to inspect its fields.", node.Range);
        return Result(node, fields);
    }

    private static NodeParseResult ParsePage(ParseContext context, FormatNode node)
    {
        var fields = context.CreateFieldList();
        if (!OggPage.TryRead(context.Source, node.Range.Offset, node.Range.End, out var page, out var error))
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The Ogg page header could not be read ({error}).",
                node.Range);
            return Result(node, fields);
        }

        var start = page.Start;
        fields.Raw("stream_structure_version", page.Version.ToString(CultureInfo.InvariantCulture), new SourceRange(start + 4, 1));
        fields.Raw(
            "header_type_flag",
            string.Create(
                CultureInfo.InvariantCulture,
                $"0x{page.HeaderType:X2} ({OggFormatting.FormatHeaderType(page.HeaderType)})"),
            new SourceRange(start + 5, 1));
        fields.Raw(
            "granule_position",
            OggFormatting.FormatGranulePosition(page.GranulePosition),
            new SourceRange(start + 6, 8));
        fields.Raw(
            "bitstream_serial_number",
            string.Create(CultureInfo.InvariantCulture, $"0x{page.SerialNumber:X8}"),
            new SourceRange(start + 14, 4));
        fields.Raw("page_sequence_number", page.SequenceNumber, new SourceRange(start + 18, 4));
        fields.Raw(
            "CRC_checksum",
            string.Create(CultureInfo.InvariantCulture, $"0x{page.Checksum:X8}"),
            new SourceRange(start + 22, 4));
        fields.Raw("number_page_segments", page.PageSegments.ToString(CultureInfo.InvariantCulture), new SourceRange(start + 26, 1));

        Span<byte> lacing = stackalloc byte[OggPage.MaxSegments];
        var lacingCount = OggPage.ReadSegmentTable(context.Source, page, lacing);
        var lacingValues = lacing[..lacingCount];
        for (var index = 0; index < lacingValues.Length; index++)
        {
            fields.Raw(
                $"segment_table[{index}]",
                lacingValues[index].ToString(CultureInfo.InvariantCulture),
                new SourceRange(start + OggPage.FixedHeaderLength + index, 1));
        }

        fields.Derived("<PayloadLength>", page.End - page.PayloadOffset, new SourceRange(page.PayloadOffset, Math.Max(0, page.End - page.PayloadOffset)));
        fields.Derived("<PacketCount>", CountCompletedPackets(lacingValues), node.Range);

        if (page.Version != 0)
        {
            fields.Warning(DiagnosticCodes.UnsupportedVersion, "The Ogg stream structure version is not 0.", new SourceRange(start + 4, 1));
        }

        if ((page.HeaderType & OggPage.ContinuedFlag) != 0)
        {
            fields.Note("The first packet on this page continues from the previous page.", new SourceRange(start + 5, 1));
        }

        if (page.Truncated)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The Ogg page payload is truncated; packet headers are not interpreted.",
                SourceRange.FromBounds(page.Start, page.End));
            return Result(node, fields);
        }

        if (OggCrc.TryComputePage(context.Source, page, out var computed))
        {
            var matches = computed == page.Checksum;
            fields.Derived(
                "<CRC check>",
                matches
                    ? "OK"
                    : string.Create(CultureInfo.InvariantCulture, $"mismatch (computed 0x{computed:X8})"),
                new SourceRange(start + 22, 4));
            if (!matches)
            {
                fields.Warning(
                    DiagnosticCodes.CrcMismatch,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The page CRC is 0x{page.Checksum:X8} but the computed value is 0x{computed:X8}."),
                    SourceRange.FromBounds(page.Start, page.End));
            }
        }

        ParsePackets(context, fields, page, lacingValues);
        return Result(node, fields);
    }

    private static void ParsePackets(
        ParseContext context,
        FieldList fields,
        in OggPageHeader page,
        ReadOnlySpan<byte> lacing)
    {
        var cursor = page.PayloadOffset;
        var packetStart = cursor;
        long packetLength = 0;
        var packetIndex = 0;
        var continued = (page.HeaderType & OggPage.ContinuedFlag) != 0;
        var lastLacing = -1;

        foreach (var value in lacing)
        {
            lastLacing = value;
            packetLength += value;
            cursor += value;
            if (value == OggPage.MaxSegments)
            {
                continue;
            }

            if (!continued)
            {
                ParsePacket(context, fields, packetIndex, packetStart, packetLength);
            }

            packetIndex++;
            packetStart = cursor;
            packetLength = 0;
            continued = false;
        }

        if (lastLacing == OggPage.MaxSegments)
        {
            fields.Note(
                "A packet continues on the next page.",
                new SourceRange(packetStart, Math.Max(0, cursor - packetStart)));
        }
    }

    private static void ParsePacket(
        ParseContext context,
        FieldList fields,
        int packetIndex,
        long start,
        long length)
    {
        if (length <= 0)
        {
            return;
        }

        Span<byte> prefix = stackalloc byte[16];
        var count = (int)Math.Min(length, prefix.Length);
        var read = SourceRead.ReadAtMost(context.Source, start, prefix[..count]);
        var kind = OggCodec.Identify(prefix[..read]);
        if (kind == OggHeaderKind.Unknown)
        {
            return;
        }

        var range = new SourceRange(start, length);
        fields.Derived(
            $"<Packet[{packetIndex}].Header>",
            OggCodec.GetHeaderName(kind),
            range);
        var reader = new BlockReader(context.Source, start, length);
        OggCodecHeaders.Parse(fields, reader, kind, $"Packet[{packetIndex}].", context.Limits.MaxStringBytes);
    }

    private static int CountCompletedPackets(ReadOnlySpan<byte> lacing)
    {
        var count = 0;
        foreach (var value in lacing)
        {
            if (value != OggPage.MaxSegments)
            {
                count++;
            }
        }

        return count;
    }

    private static StreamSummary? FindStream(List<StreamSummary> streams, uint serialNumber)
    {
        for (var index = streams.Count - 1; index >= 0; index--)
        {
            if (streams[index].SerialNumber == serialNumber)
            {
                return streams[index];
            }
        }

        return null;
    }

    private static NodeParseResult Result(FormatNode node, FieldList fields, string? title = null) =>
        new(
            title ?? node.Name,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            [.. fields.Diagnostics]);

    private sealed class StreamSummary(uint serialNumber, int firstPage)
    {
        public uint SerialNumber { get; } = serialNumber;
        public int FirstPage { get; } = firstPage;
        public string Codec { get; set; } = "Unknown";
        public int PageCount { get; set; }
        public int LastPage { get; set; } = firstPage;
        public bool SeenEos { get; set; }
    }
}
