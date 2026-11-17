using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Riff;

internal delegate void RiffChunkHandler(RiffChunkContext context);

/// <summary>
/// Registry of per-chunk field emitters. Adding support for a new chunk means adding one
/// handler and one registration; the container walker and tree contract stay unchanged.
/// </summary>
internal static class RiffChunkHandlers
{
    private static readonly Dictionary<string, RiffChunkHandler> Handlers = new(StringComparer.Ordinal)
    {
        ["fmt "] = Format,
        ["fact"] = Fact,
        ["cue "] = CuePoints,
        ["bext"] = BroadcastExtension,
        ["ds64"] = Ds64,
        ["avih"] = AviMainHeader,
        ["strh"] = AviStreamHeader,
        ["strf"] = AviStreamFormat,
        ["idx1"] = AviIndex,
        ["dmlh"] = RiffOpenDmlParser.ParseHeader,
        ["indx"] = RiffOpenDmlParser.ParseIndex,
        ["VP8X"] = Vp8x,
        ["ANIM"] = WebPAnim,
        ["anih"] = AniHeader,
        ["icon"] = AniIcon,
        ["colh"] = CollectionHeader,
        ["vers"] = DlsVersion,
        ["msyn"] = MasterSync,
        ["insh"] = InstrumentHeader,
        ["labl"] = Label,
        ["BKHD"] = BankHeader,
        ["DIDX"] = DataIndex,
        ["HIRC"] = HircSection,
        ["id3 "] = Id3Tag,
    };

    static RiffChunkHandlers()
    {
        foreach (var id in new[]
                 {
                     "IARL", "IART", "ICMS", "ICMT", "ICNM", "ICOP", "ICRD", "ICRP", "IDIM", "IDIT",
                     "IDPI", "IENG", "IGNR", "IKEY", "ILGT", "ILNG", "IMED", "INAM", "IMUS", "IPLT",
                     "IPRD", "IPRO", "IPRT", "ISBJ", "ISFT", "ISHP", "ISMP", "ISRC", "ISRF", "ITCH", "ITRK",
                 })
        {
            Handlers[id] = InfoText;
        }
    }

    public static bool IsKnown(string id) => Handlers.ContainsKey(id) || RiffOpenDmlParser.IsIndexId(id);

    public static void Parse(RiffChunkContext context)
    {
        if (Handlers.TryGetValue(context.Id, out var handler))
        {
            handler(context);
        }
        else if (RiffOpenDmlParser.IsIndexId(context.Id))
        {
            RiffOpenDmlParser.ParseIndex(context);
        }
    }

    private static void Format(RiffChunkContext context) => RiffWaveFormat.Populate(context);

    private static void Fact(RiffChunkContext context)
    {
        var data = context.ReadPayload(4);
        if (data.Length < 4)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'fact' requires 4 bytes.", context.Layout.PayloadRange);
            return;
        }

        context.Fields.Raw("dwSampleLength", RiffRead.UInt32(data, 0, context.Endian), context.Range(0, 4));
        context.AddTruncationWarning();
    }

    private static void CuePoints(RiffChunkContext context)
    {
        const int entrySize = 24;
        var data = context.ReadPayload(4 + entrySize * Math.Min(context.Limits.MaxDataLines, 4096));
        if (data.Length < 4)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'cue ' requires 4 bytes.", context.Layout.PayloadRange);
            return;
        }

        var count = RiffRead.UInt32(data, 0, context.Endian);
        context.Fields.Raw("dwCuePoints", count, context.Range(0, 4));

        var available = (data.Length - 4) / entrySize;
        var rows = (int)Math.Min(Math.Min((long)count, available), context.Limits.MaxDataLines);
        if (count > rows)
        {
            context.Fields.Warning(
                DiagnosticCodes.UnparsedPayload,
                $"Only {rows} of {count} cue points are shown.",
                context.Layout.PayloadRange);
        }

        for (var index = 0; index < rows; index++)
        {
            var offset = 4 + index * entrySize;
            var name = RiffRead.UInt32(data, offset, context.Endian);
            var position = RiffRead.UInt32(data, offset + 4, context.Endian);
            var chunk = RiffRead.UInt32(data, offset + 8, context.Endian);
            var chunkStart = RiffRead.UInt32(data, offset + 12, context.Endian);
            var blockStart = RiffRead.UInt32(data, offset + 16, context.Endian);
            var sampleOffset = RiffRead.UInt32(data, offset + 20, context.Endian);
            var chunkId = RiffFormatting.DecodeFourCc(data.AsSpan(offset + 8, 4));
            context.Fields.Raw(
                $"CuePoint[{index}]",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{name},{position},{RiffFormatting.FormatFourCc(chunkId)},0x{chunk:X8},{chunkStart},{blockStart},{sampleOffset}"),
                context.Range(offset, entrySize));
        }

        context.AddTruncationWarning();
    }

    private static void BroadcastExtension(RiffChunkContext context)
    {
        var data = context.ReadPayload(602 + 4096);
        if (data.Length < 602)
        {
            context.Fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"'bext' is defined as 602 fixed bytes plus coding history; only {data.Length} bytes are available.",
                context.Layout.PayloadRange);
        }

        if (data.Length >= 256) context.Fields.Raw("Description", DecodeFixedText(data, 0, 256), context.Range(0, 256));
        if (data.Length >= 288) context.Fields.Raw("Originator", DecodeFixedText(data, 256, 32), context.Range(256, 32));
        if (data.Length >= 320) context.Fields.Raw("OriginatorReference", DecodeFixedText(data, 288, 32), context.Range(288, 32));
        if (data.Length >= 330) context.Fields.Raw("OriginationDate", DecodeFixedText(data, 320, 10), context.Range(320, 10));
        if (data.Length >= 338) context.Fields.Raw("OriginationTime", DecodeFixedText(data, 330, 8), context.Range(330, 8));
        if (data.Length >= 346)
        {
            context.Fields.Raw("TimeReferenceLow", RiffRead.UInt32(data, 338, context.Endian), context.Range(338, 4));
            context.Fields.Raw("TimeReferenceHigh", RiffRead.UInt32(data, 342, context.Endian), context.Range(342, 4));
        }

        if (data.Length >= 348) context.Fields.Raw("Version", RiffRead.UInt16(data, 346, context.Endian), context.Range(346, 2));
        if (data.Length >= 412) context.Fields.Raw("UMID", RiffFormatting.FormatHex(data.AsSpan(348, 64), 64), context.Range(348, 64));
        if (data.Length >= 420)
        {
            context.Fields.Raw("LoudnessValue", RiffRead.UInt16(data, 412, context.Endian), context.Range(412, 2));
            context.Fields.Raw("LoudnessRange", RiffRead.UInt16(data, 414, context.Endian), context.Range(414, 2));
            context.Fields.Raw("MaxTruePeakLevel", RiffRead.UInt16(data, 416, context.Endian), context.Range(416, 2));
            context.Fields.Raw("MaxMomentaryLoudness", RiffRead.UInt16(data, 418, context.Endian), context.Range(418, 2));
        }

        if (data.Length >= 602)
        {
            context.Fields.Raw("CodingHistory", RiffFormatting.DecodeText(data.AsSpan(602)), context.Range(602, data.Length - 602));
        }

        if (context.PayloadLength > data.Length)
        {
            context.Fields.Warning(
                DiagnosticCodes.UnparsedPayload,
                $"Only the first {data.Length} of {context.PayloadLength} 'bext' bytes are shown.",
                context.Layout.PayloadRange);
        }

        context.AddTruncationWarning();
    }

    private static void Ds64(RiffChunkContext context)
    {
        var data = context.ReadPayload(4096);
        if (data.Length < 28)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'ds64' requires at least 28 bytes.", context.Layout.PayloadRange);
            return;
        }

        var riffSize = RiffRead.UInt64(data, 0, Endian.Little);
        var dataSize = RiffRead.UInt64(data, 8, Endian.Little);
        var sampleCount = RiffRead.UInt64(data, 16, Endian.Little);
        var tableLength = RiffRead.UInt32(data, 24, Endian.Little);

        context.Fields.Raw("riffSize", riffSize, context.Range(0, 8));
        context.Fields.Raw("dataSize", dataSize, context.Range(8, 8));
        context.Fields.Raw("sampleCount", sampleCount, context.Range(16, 8));
        context.Fields.Raw("tableLength", tableLength, context.Range(24, 4));

        var available = (data.Length - 28) / 12;
        var rows = (int)Math.Min(Math.Min((long)tableLength, available), context.Limits.MaxDataLines);
        if (tableLength > rows)
        {
            context.Fields.Warning(
                DiagnosticCodes.UnparsedPayload,
                $"Only {rows} of {tableLength} ds64 table entries are shown.",
                context.Layout.PayloadRange);
        }

        for (var index = 0; index < rows; index++)
        {
            var offset = 28 + index * 12;
            var id = RiffFormatting.DecodeFourCc(data.AsSpan(offset, 4));
            var size = RiffRead.UInt64(data, offset + 4, Endian.Little);
            context.Fields.Raw(
                $"ds64Table[{index}]",
                $"{RiffFormatting.FormatFourCc(id)},{size}",
                context.Range(offset, 12));
        }

        context.AddTruncationWarning();
    }

    private static void AviMainHeader(RiffChunkContext context)
    {
        var data = context.ReadPayload(56);
        if (data.Length < 40)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "AVIMAINHEADER requires at least 40 bytes.", context.Layout.PayloadRange);
            return;
        }

        var endian = context.Endian;
        var flags = RiffRead.UInt32(data, 12, endian);
        context.Fields.Raw("dwMicroSecPerFrame", RiffRead.UInt32(data, 0, endian), context.Range(0, 4));
        context.Fields.Raw("dwMaxBytesPerSec", RiffRead.UInt32(data, 4, endian), context.Range(4, 4));
        context.Fields.Raw("dwPaddingGranularity", RiffRead.UInt32(data, 8, endian), context.Range(8, 4));
        context.Fields.RawHex("dwFlags", flags, 8, context.Range(12, 4));
        context.Fields.Derived("<dwFlags>", DescribeFlags(flags, AviMainFlags), context.Range(12, 4));
        context.Fields.Raw("dwTotalFrames", RiffRead.UInt32(data, 16, endian), context.Range(16, 4));
        context.Fields.Raw("dwInitialFrames", RiffRead.UInt32(data, 20, endian), context.Range(20, 4));
        context.Fields.Raw("dwStreams", RiffRead.UInt32(data, 24, endian), context.Range(24, 4));
        context.Fields.Raw("dwSuggestedBufferSize", RiffRead.UInt32(data, 28, endian), context.Range(28, 4));
        context.Fields.Raw("dwWidth", RiffRead.UInt32(data, 32, endian), context.Range(32, 4));
        context.Fields.Raw("dwHeight", RiffRead.UInt32(data, 36, endian), context.Range(36, 4));

        if (data.Length >= 56)
        {
            var r0 = RiffRead.UInt32(data, 40, endian);
            var r1 = RiffRead.UInt32(data, 44, endian);
            var r2 = RiffRead.UInt32(data, 48, endian);
            var r3 = RiffRead.UInt32(data, 52, endian);
            context.Fields.Raw("dwReserved", $"0x{r0:X8}, 0x{r1:X8}, 0x{r2:X8}, 0x{r3:X8}", context.Range(40, 16));
            if ((r0 | r1 | r2 | r3) != 0)
            {
                context.Fields.Note(
                    "dwReserved is non-zero; the legacy MainAVIHeader layout defined these fields as dwScale, dwRate, dwStart and dwLength.",
                    context.Range(40, 16));
            }
        }
        else
        {
            context.Fields.Warning(DiagnosticCodes.TruncatedPayload, "AVIMAINHEADER is 56 bytes; dwReserved is missing.", context.Layout.PayloadRange);
        }

        context.AddTruncationWarning();
    }

    private static void AviStreamHeader(RiffChunkContext context)
    {
        var data = context.ReadPayload(56);
        if (data.Length < 48)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "AVISTREAMHEADER requires at least 48 bytes.", context.Layout.PayloadRange);
            return;
        }

        var endian = context.Endian;
        var typeBytes = data.AsSpan(0, 4);
        var handlerBytes = data.AsSpan(4, 4);
        var flags = RiffRead.UInt32(data, 8, endian);
        var language = RiffRead.UInt16(data, 14, endian);

        context.Fields.Raw("fccType", RiffFormatting.FormatFourCc(typeBytes), context.Range(0, 4));
        context.Fields.Derived("<fccType>", DescribeStreamType(typeBytes), context.Range(0, 4));
        context.Fields.Raw("fccHandler", RiffFormatting.FormatFourCc(handlerBytes), context.Range(4, 4));
        context.Fields.RawHex("dwFlags", flags, 8, context.Range(8, 4));
        context.Fields.Derived("<dwFlags>", DescribeFlags(flags, AviStreamFlags), context.Range(8, 4));
        context.Fields.Raw("wPriority", RiffRead.UInt16(data, 12, endian), context.Range(12, 2));
        context.Fields.RawHex("wLanguage", language, 4, context.Range(14, 2));
        context.Fields.Raw("dwInitialFrames", RiffRead.UInt32(data, 16, endian), context.Range(16, 4));
        context.Fields.Raw("dwScale", RiffRead.UInt32(data, 20, endian), context.Range(20, 4));
        context.Fields.Raw("dwRate", RiffRead.UInt32(data, 24, endian), context.Range(24, 4));
        context.Fields.Raw("dwStart", RiffRead.UInt32(data, 28, endian), context.Range(28, 4));
        context.Fields.Raw("dwLength", RiffRead.UInt32(data, 32, endian), context.Range(32, 4));
        context.Fields.Raw("dwSuggestedBufferSize", RiffRead.UInt32(data, 36, endian), context.Range(36, 4));

        var quality = RiffRead.UInt32(data, 40, endian);
        context.Fields.Raw("dwQuality", quality == uint.MaxValue ? "0xFFFFFFFF" : quality.ToString(CultureInfo.InvariantCulture), context.Range(40, 4));
        if (quality == uint.MaxValue)
        {
            context.Fields.Derived("<dwQuality>", "-1: use the codec default quality", context.Range(40, 4));
        }

        context.Fields.Raw("dwSampleSize", RiffRead.UInt32(data, 44, endian), context.Range(44, 4));

        if (data.Length >= 56)
        {
            var left = (short)RiffRead.UInt16(data, 48, endian);
            var top = (short)RiffRead.UInt16(data, 50, endian);
            var right = (short)RiffRead.UInt16(data, 52, endian);
            var bottom = (short)RiffRead.UInt16(data, 54, endian);
            context.Fields.Raw("rcFrame", $"left={left}, top={top}, right={right}, bottom={bottom}", context.Range(48, 8));
        }
        else
        {
            context.Fields.Warning(DiagnosticCodes.TruncatedPayload, "AVISTREAMHEADER is 56 bytes; rcFrame is missing.", context.Layout.PayloadRange);
        }

        context.AddTruncationWarning();
    }

    private static void AviStreamFormat(RiffChunkContext context)
    {
        var data = context.ReadPayload(128);
        if (data.Length >= 40)
        {
            var size = RiffRead.UInt32(data, 0, context.Endian);
            if (size is 40 or 52 or 56 or 108 or 124)
            {
                ParseBitmapInfoHeader(context, data, size);
                return;
            }
        }

        context.Fields.Note("Payload does not look like a BITMAPINFOHEADER; it is interpreted as WAVEFORMAT.", context.Layout.PayloadRange);
        RiffWaveFormat.Populate(context);
    }

    private static void ParseBitmapInfoHeader(RiffChunkContext context, byte[] data, uint size)
    {
        var endian = context.Endian;
        context.Fields.Raw("biSize", size, context.Range(0, 4));
        context.Fields.Raw("biWidth", (int)RiffRead.UInt32(data, 4, endian), context.Range(4, 4));
        context.Fields.Raw("biHeight", (int)RiffRead.UInt32(data, 8, endian), context.Range(8, 4));
        context.Fields.Raw("biPlanes", RiffRead.UInt16(data, 12, endian), context.Range(12, 2));
        context.Fields.Raw("biBitCount", RiffRead.UInt16(data, 14, endian), context.Range(14, 2));
        context.Fields.Raw("biCompression", RiffFormatting.FormatFourCc(data.AsSpan(16, 4)), context.Range(16, 4));
        context.Fields.Raw("biSizeImage", RiffRead.UInt32(data, 20, endian), context.Range(20, 4));
        context.Fields.Raw("biXPelsPerMeter", (int)RiffRead.UInt32(data, 24, endian), context.Range(24, 4));
        context.Fields.Raw("biYPelsPerMeter", (int)RiffRead.UInt32(data, 28, endian), context.Range(28, 4));
        context.Fields.Raw("biClrUsed", RiffRead.UInt32(data, 32, endian), context.Range(32, 4));
        context.Fields.Raw("biClrImportant", RiffRead.UInt32(data, 36, endian), context.Range(36, 4));

        if (context.PayloadLength > size)
        {
            context.Fields.Derived("<UnparsedPayloadLength>", context.PayloadLength - size, context.Range(size, context.PayloadLength - size));
        }

        context.AddTruncationWarning();
    }

    private static void AviIndex(RiffChunkContext context)
    {
        var data = context.ReadPayload(16 * 4096);
        if (data.Length < 16)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'idx1' requires at least one 16-byte entry.", context.Layout.PayloadRange);
            return;
        }

        var rows = Math.Min(data.Length / 16, context.Limits.MaxDataLines);

        for (var index = 0; index < rows; index++)
        {
            var offset = index * 16;
            var id = data.AsSpan(offset, 4);
            var flags = RiffRead.UInt32(data, offset + 4, context.Endian);
            var chunkOffset = RiffRead.UInt32(data, offset + 8, context.Endian);
            var size = RiffRead.UInt32(data, offset + 12, context.Endian);
            context.Fields.Raw(
                $"idx1Entry[{index}]",
                $"{RiffFormatting.FormatFourCc(id)},0x{flags:X8},{chunkOffset},{size}",
                context.Range(offset, 16));
        }

        if (data.Length % 16 != 0)
        {
            context.Fields.Warning(
                DiagnosticCodes.InvalidLength,
                $"'idx1' payload length {context.PayloadLength} is not a multiple of 16.",
                context.Layout.PayloadRange);
        }

        if (context.PayloadLength > data.Length)
        {
            context.Fields.Warning(
                DiagnosticCodes.UnparsedPayload,
                $"Only {rows} of {context.PayloadLength / 16} index entries are shown.",
                context.Layout.PayloadRange);
        }

        context.AddTruncationWarning();
    }

    private static void Vp8x(RiffChunkContext context)
    {
        var data = context.ReadPayload(10);
        if (data.Length < 10)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'VP8X' requires 10 bytes.", context.Layout.PayloadRange);
            return;
        }

        var flags = data[0];
        var reserved = RiffRead.UInt24(data, 1, Endian.Little);
        var widthMinusOne = RiffRead.UInt24(data, 4, Endian.Little);
        var heightMinusOne = RiffRead.UInt24(data, 7, Endian.Little);

        context.Fields.RawHex("Flags", flags, 2, context.Range(0, 1));
        context.Fields.Derived("<Flags>", DescribeWebPFlags(flags), context.Range(0, 1));
        context.Fields.Raw("Reserved", reserved, context.Range(1, 3));
        context.Fields.Raw("Canvas Width Minus One", widthMinusOne, context.Range(4, 3));
        context.Fields.Raw("Canvas Height Minus One", heightMinusOne, context.Range(7, 3));
        context.Fields.Derived("<CanvasWidth>", widthMinusOne + 1L, context.Range(4, 3));
        context.Fields.Derived("<CanvasHeight>", heightMinusOne + 1L, context.Range(7, 3));

        if ((flags & 0xC1) != 0) context.Fields.Warning(DiagnosticCodes.InvalidSignature, "Reserved VP8X flag bits must be 0.", context.Range(0, 1));
        if (reserved != 0) context.Fields.Warning(DiagnosticCodes.InvalidSignature, "The 24-bit Reserved field must be 0.", context.Range(1, 3));
        if ((widthMinusOne + 1L) * (heightMinusOne + 1L) > uint.MaxValue)
        {
            context.Fields.Warning(DiagnosticCodes.InvalidLength, "Canvas width * height exceeds 2^32-1.", context.Layout.PayloadRange);
        }

        context.AddTruncationWarning();
    }

    private static void WebPAnim(RiffChunkContext context)
    {
        var data = context.ReadPayload(6);
        if (data.Length < 6)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'ANIM' requires 6 bytes.", context.Layout.PayloadRange);
            return;
        }

        var background = RiffRead.UInt32(data, 0, Endian.Little);
        var loopCount = RiffRead.UInt16(data, 4, Endian.Little);
        context.Fields.RawHex("Background Color", background, 8, context.Range(0, 4));
        context.Fields.Derived(
            "<Background Color>",
            $"blue {data[0]}, green {data[1]}, red {data[2]}, alpha {data[3]}",
            context.Range(0, 4));
        context.Fields.Raw("Loop Count", loopCount, context.Range(4, 2));
        if (loopCount == 0)
        {
            context.Fields.Derived("<Loop Count>", "0: loop indefinitely", context.Range(4, 2));
        }

        context.AddTruncationWarning();
    }

    /// <summary>ID3v2 metadata attached to a WAVE file (RIFF <c>id3 </c> chunk).</summary>
    private static void Id3Tag(RiffChunkContext context)
    {
        var payload = context.Layout.PayloadRange;
        Id3Parser.Parse(
            context.Source,
            payload.Offset,
            payload.Length,
            context.Limits,
            context.Fields,
            string.Empty,
            context.CancellationToken);
    }

    public static void AnmfHeader(RiffChunkContext context)
    {
        var data = context.ReadPayload(16);
        if (data.Length < 16)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'ANMF' header requires 16 bytes.", context.Layout.PayloadRange);
            return;
        }

        var frameX = RiffRead.UInt24(data, 0, Endian.Little);
        var frameY = RiffRead.UInt24(data, 3, Endian.Little);
        var widthMinusOne = RiffRead.UInt24(data, 6, Endian.Little);
        var heightMinusOne = RiffRead.UInt24(data, 9, Endian.Little);
        var duration = RiffRead.UInt24(data, 12, Endian.Little);
        var flags = data[15];

        context.Fields.Raw("Frame X", frameX, context.Range(0, 3));
        context.Fields.Raw("Frame Y", frameY, context.Range(3, 3));
        context.Fields.Raw("Frame Width Minus One", widthMinusOne, context.Range(6, 3));
        context.Fields.Raw("Frame Height Minus One", heightMinusOne, context.Range(9, 3));
        context.Fields.Raw("Frame Duration", duration, context.Range(12, 3));
        context.Fields.RawHex("Flags", flags, 2, context.Range(15, 1));
        context.Fields.Derived("<FrameOffsetX>", frameX * 2L, context.Range(0, 3));
        context.Fields.Derived("<FrameOffsetY>", frameY * 2L, context.Range(3, 3));
        context.Fields.Derived("<FrameWidth>", widthMinusOne + 1L, context.Range(6, 3));
        context.Fields.Derived("<FrameHeight>", heightMinusOne + 1L, context.Range(9, 3));
        context.Fields.Derived("<Blending method (B)>", (flags & 0x02) != 0 ? "1: overwrite" : "0: alpha-blend", context.Range(15, 1));
        context.Fields.Derived("<Disposal method (D)>", (flags & 0x01) != 0 ? "1: dispose to background" : "0: leave canvas", context.Range(15, 1));

        if ((flags & 0xFC) != 0) context.Fields.Warning(DiagnosticCodes.InvalidSignature, "The 6-bit Reserved field must be 0.", context.Range(15, 1));
        if (duration == 0) context.Fields.Note("Frame Duration 0 has implementation-defined behaviour.", context.Range(12, 3));

        context.AddTruncationWarning();
    }

    private static void AniHeader(RiffChunkContext context)
    {
        var data = context.ReadPayload(36);
        if (data.Length < 36)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "ANIHEADER requires 36 bytes.", context.Layout.PayloadRange);
            return;
        }

        var endian = context.Endian;
        var size = RiffRead.UInt32(data, 0, endian);
        var frames = RiffRead.UInt32(data, 4, endian);
        var steps = RiffRead.UInt32(data, 8, endian);
        var cx = RiffRead.UInt32(data, 12, endian);
        var cy = RiffRead.UInt32(data, 16, endian);
        var bitCount = RiffRead.UInt32(data, 20, endian);
        var planes = RiffRead.UInt32(data, 24, endian);
        var rate = RiffRead.UInt32(data, 28, endian);
        var flags = RiffRead.UInt32(data, 32, endian);

        context.Fields.Raw("cbSizeOf", size, context.Range(0, 4));
        context.Fields.Raw("cFrames", frames, context.Range(4, 4));
        context.Fields.Raw("cSteps", steps, context.Range(8, 4));
        context.Fields.Raw("cx", cx, context.Range(12, 4));
        context.Fields.Raw("cy", cy, context.Range(16, 4));
        context.Fields.Raw("cBitCount", bitCount, context.Range(20, 4));
        context.Fields.Raw("cPlanes", planes, context.Range(24, 4));
        context.Fields.Raw("JifRate", rate, context.Range(28, 4));
        context.Fields.RawHex("flags", flags, 8, context.Range(32, 4));
        context.Fields.Derived("<flags>", DescribeFlags(flags, AniFlags), context.Range(32, 4));

        if (size != 36) context.Fields.Warning(DiagnosticCodes.InvalidLength, $"cbSizeOf should be 36, found {size}.", context.Range(0, 4));
        if ((cx | cy | bitCount | planes) != 0) context.Fields.Note("cx, cy, cBitCount and cPlanes are reserved and should be zero.", context.Range(12, 16));
        if ((flags & 0x02) == 0 && steps != frames) context.Fields.Warning(DiagnosticCodes.InvalidLength, "AF_SEQUENCE is clear, so cSteps should equal cFrames.", context.Range(4, 8));

        context.AddTruncationWarning();
    }

    private static void AniIcon(RiffChunkContext context)
    {
        var header = context.ReadPayload(6);
        if (header.Length < 6)
        {
            context.Fields.Note("The payload is too short for an ICONDIR header.", context.Layout.PayloadRange);
            context.AddTruncationWarning();
            return;
        }

        var reserved = RiffRead.UInt16(header, 0, context.Endian);
        var type = RiffRead.UInt16(header, 2, context.Endian);
        var count = RiffRead.UInt16(header, 4, context.Endian);
        if (reserved != 0 || type is not (1 or 2) || count == 0)
        {
            context.Fields.Note("The payload carries no valid ICONDIR header; it is left undecoded.", context.Layout.PayloadRange);
            context.AddTruncationWarning();
            return;
        }

        context.Fields.Raw("idReserved", reserved, context.Range(0, 2));
        context.Fields.Raw("idType", type, context.Range(2, 2));
        context.Fields.Derived("<idType>", type == 1 ? "Icon resource (ICO)" : "Cursor resource (CUR)", context.Range(2, 2));
        context.Fields.Raw("idCount", count, context.Range(4, 2));

        var directorySize = 6 + count * 16L;
        if (context.PayloadLength < directorySize)
        {
            context.Fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"ICONDIR declares {count} entries ({directorySize} bytes); only {context.PayloadLength} bytes are available.",
                context.Layout.PayloadRange);
        }

        var data = context.ReadPayload((int)Math.Min(directorySize, context.Limits.MaxBlobBytes));
        var entries = Math.Min(count, Math.Max(0, (data.Length - 6) / 16));

        for (var index = 0; index < entries; index++)
        {
            var offset = 6 + index * 16;
            var width = data[offset] == 0 ? "0 (256)" : data[offset].ToString(CultureInfo.InvariantCulture);
            var height = data[offset + 1] == 0 ? "0 (256)" : data[offset + 1].ToString(CultureInfo.InvariantCulture);
            var colours = data[offset + 2] == 0 ? "0 (256 or more)" : data[offset + 2].ToString(CultureInfo.InvariantCulture);
            var planes = RiffRead.UInt16(data, offset + 4, context.Endian);
            var bits = RiffRead.UInt16(data, offset + 6, context.Endian);
            var bytes = RiffRead.UInt32(data, offset + 8, context.Endian);
            var imageOffset = RiffRead.UInt32(data, offset + 12, context.Endian);
            context.Fields.Raw(
                $"idEntries[{index}]",
                $"{width},{height},{colours},{data[offset + 3]},{planes},{bits},{bytes},{imageOffset}",
                context.Range(offset, 16));
        }

        context.AddTruncationWarning();
    }

    private static void CollectionHeader(RiffChunkContext context)
    {
        var data = context.ReadPayload(4);
        if (data.Length < 4)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'colh' requires 4 bytes.", context.Layout.PayloadRange);
            return;
        }

        context.Fields.Raw("cInstruments", RiffRead.UInt32(data, 0, context.Endian), context.Range(0, 4));
        context.AddTruncationWarning();
    }

    private static void DlsVersion(RiffChunkContext context)
    {
        var data = context.ReadPayload(8);
        if (data.Length < 8)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'vers' requires 8 bytes.", context.Layout.PayloadRange);
            return;
        }

        context.Fields.RawHex("dwVersion", RiffRead.UInt32(data, 0, context.Endian), 8, context.Range(0, 4));
        context.Fields.RawHex("dwFormat", RiffRead.UInt32(data, 4, context.Endian), 8, context.Range(4, 4));
        context.AddTruncationWarning();
    }

    private static void MasterSync(RiffChunkContext context)
    {
        var data = context.ReadPayload(4);
        if (data.Length < 4)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'msyn' requires 4 bytes.", context.Layout.PayloadRange);
            return;
        }

        context.Fields.Raw("dwMasterSync", RiffRead.UInt32(data, 0, context.Endian), context.Range(0, 4));
        context.AddTruncationWarning();
    }

    private static void InstrumentHeader(RiffChunkContext context)
    {
        var data = context.ReadPayload(12);
        if (data.Length < 12)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "DLS instrument header requires 12 bytes.", context.Layout.PayloadRange);
            return;
        }

        context.Fields.Raw("cRegions", RiffRead.UInt32(data, 0, context.Endian), context.Range(0, 4));
        context.Fields.RawHex("ulBank", RiffRead.UInt32(data, 4, context.Endian), 8, context.Range(4, 4));
        context.Fields.Raw("ulInstrument", RiffRead.UInt32(data, 8, context.Endian), context.Range(8, 4));
        context.AddTruncationWarning();
    }

    private static void Label(RiffChunkContext context)
    {
        var data = context.ReadPayload((int)Math.Min((long)context.Limits.MaxStringBytes + 4, int.MaxValue));
        if (data.Length < 4)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'labl' requires a 4-byte cue point ID.", context.Layout.PayloadRange);
            return;
        }

        context.Fields.Raw("dwCuePointID", RiffRead.UInt32(data, 0, context.Endian), context.Range(0, 4));
        var textBytes = data.AsSpan(4);
        var text = RiffFormatting.DecodeText(textBytes);
        context.Fields.Raw("Text", text, context.Range(4, textBytes.Length));
        if (textBytes.Length > 0 && textBytes.IndexOf((byte)0) < 0)
        {
            context.Fields.Note("Text should be NUL-terminated; terminator is missing.", context.Range(4, textBytes.Length));
        }

        if (context.PayloadLength > data.Length)
        {
            context.Fields.Warning(
                DiagnosticCodes.LimitString,
                $"Only the first {data.Length} of {context.PayloadLength} label bytes are shown.",
                context.Layout.PayloadRange);
        }

        context.AddTruncationWarning();
    }

    private static void BankHeader(RiffChunkContext context)
    {
        var data = context.ReadPayload(20);
        if (data.Length < 4)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'BKHD' requires at least 4 bytes.", context.Layout.PayloadRange);
            return;
        }

        context.Fields.Raw("dwBankGeneratorVersion", RiffRead.UInt32(data, 0, context.Endian), context.Range(0, 4));
        if (data.Length >= 8) context.Fields.Raw("dwSoundBankID", RiffRead.UInt32(data, 4, context.Endian), context.Range(4, 4));
        if (data.Length >= 12) context.Fields.Raw("dwLanguageID", RiffRead.UInt32(data, 8, context.Endian), context.Range(8, 4));
        if (data.Length >= 16) context.Fields.Raw("dwFeedback", RiffRead.UInt32(data, 12, context.Endian), context.Range(12, 4));
        if (data.Length >= 20) context.Fields.Raw("dwProjectID", RiffRead.UInt32(data, 16, context.Endian), context.Range(16, 4));
        context.AddUnparsedPayloadNote(Math.Min(data.Length, 20));
        context.AddTruncationWarning();
    }

    private static void DataIndex(RiffChunkContext context)
    {
        var data = context.ReadPayload(12 * 4096);
        if (data.Length < 12)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'DIDX' requires at least one 12-byte entry.", context.Layout.PayloadRange);
            return;
        }

        var rows = Math.Min(data.Length / 12, context.Limits.MaxDataLines);
        for (var index = 0; index < rows; index++)
        {
            var offset = index * 12;
            context.Fields.Raw(
                $"MediaIndex[{index}]",
                $"{RiffRead.UInt32(data, offset, context.Endian)},{RiffRead.UInt32(data, offset + 4, context.Endian)},{RiffRead.UInt32(data, offset + 8, context.Endian)}",
                context.Range(offset, 12));
        }

        if (context.PayloadLength > data.Length)
        {
            context.Fields.Warning(
                DiagnosticCodes.UnparsedPayload,
                $"Only {rows} of {context.PayloadLength / 12} media index entries are shown.",
                context.Layout.PayloadRange);
        }

        context.AddTruncationWarning();
    }

    private static void HircSection(RiffChunkContext context)
    {
        var data = context.ReadPayload(4);
        if (data.Length < 4)
        {
            context.Fields.Error(DiagnosticCodes.TruncatedPayload, "'HIRC' requires a 4-byte object count.", context.Layout.PayloadRange);
            return;
        }

        context.Fields.Raw("dwNumObjects", RiffRead.UInt32(data, 0, context.Endian), context.Range(0, 4));
        context.AddUnparsedPayloadNote(4);
        context.Fields.Note("HIRC object records are not decoded in this build.", context.Layout.PayloadRange);
        context.AddTruncationWarning();
    }

    private static void InfoText(RiffChunkContext context)
    {
        var data = context.ReadPayload(context.Limits.MaxStringBytes);
        var text = RiffFormatting.DecodeText(data);
        context.Fields.Raw("Text", text, context.Range(0, data.Length));
        if (data.Length > 0 && data.AsSpan().IndexOf((byte)0) < 0)
        {
            context.Fields.Note("INFO text should be NUL-terminated; terminator is missing.", context.Layout.PayloadRange);
        }

        if (context.PayloadLength > data.Length)
        {
            context.Fields.Warning(
                DiagnosticCodes.LimitString,
                $"Only the first {data.Length} of {context.PayloadLength} text bytes are shown.",
                context.Layout.PayloadRange);
        }

        context.AddTruncationWarning();
    }

    private static string DecodeFixedText(byte[] data, int offset, int length)
    {
        var span = data.AsSpan(offset, Math.Min(length, data.Length - offset));
        return RiffFormatting.DecodeText(span);
    }

    private static string DescribeFlags(uint flags, IReadOnlyList<(uint Mask, string Name)> table)
    {
        if (flags == 0)
        {
            return "None";
        }

        var names = table.Where(flag => (flags & flag.Mask) != 0).Select(flag => flag.Name).ToArray();
        var unknown = flags & ~table.Aggregate(0u, static (mask, flag) => mask | flag.Mask);
        return unknown == 0
            ? string.Join(" | ", names)
            : $"{string.Join(" | ", names)} | 0x{unknown:X8}";
    }

    private static string DescribeStreamType(ReadOnlySpan<byte> bytes) =>
        RiffFormatting.FormatFourCc(bytes) switch
        {
            "vids" => "Video stream",
            "auds" => "Audio stream",
            "txts" => "Subtitle / text stream",
            "mids" => "MIDI stream",
            "iavs" => "Interleaved audio and video stream (DV type-1)",
            _ => "Unknown stream type",
        };

    private static string DescribeWebPFlags(byte flags)
    {
        var names = new List<string>();
        if ((flags & 0x20) != 0) names.Add("ICC profile");
        if ((flags & 0x10) != 0) names.Add("Alpha");
        if ((flags & 0x08) != 0) names.Add("Exif metadata");
        if ((flags & 0x04) != 0) names.Add("XMP metadata");
        if ((flags & 0x02) != 0) names.Add("Animation");
        return names.Count == 0 ? "None" : string.Join(" | ", names);
    }

    private static readonly (uint Mask, string Name)[] AviMainFlags =
    [
        (0x00000010, "AVIF_HASINDEX"), (0x00000020, "AVIF_MUSTUSEINDEX"),
        (0x00000100, "AVIF_ISINTERLEAVED"), (0x00000800, "AVIF_TRUSTCKTYPE"),
        (0x00010000, "AVIF_WASCAPTUREFILE"), (0x00020000, "AVIF_COPYRIGHTED"),
    ];

    private static readonly (uint Mask, string Name)[] AviStreamFlags =
    [
        (0x00000001, "AVISF_DISABLED"), (0x00010000, "AVISF_VIDEO_PALCHANGES"),
    ];

    private static readonly (uint Mask, string Name)[] AniFlags =
    [
        (0x00000001, "AF_ICON"), (0x00000002, "AF_SEQUENCE"),
    ];
}
