using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// AVI 流头部 'strh'（AVISTREAMHEADER，负载 56 字节）。
    /// 字段名与官方 AVISTREAMHEADER 结构一致。
    /// </summary>
    internal static class AviStreamHeaderChunk
    {
        private const int StructSize = 56;
        private const uint QualityDefault = 0xFFFFFFFF;

        private static readonly (uint Mask, string Name)[] StreamFlags =
        [
            (0x00000001, "AVISF_DISABLED"),
            (0x00010000, "AVISF_VIDEO_PALCHANGES"),
        ];

        private static readonly Dictionary<string, string> StreamTypes = new(StringComparer.Ordinal)
        {
            ["vids"] = "Video stream",
            ["auds"] = "Audio stream",
            ["txts"] = "Subtitle / text stream",
            ["mids"] = "MIDI stream",
            ["iavs"] = "Interleaved audio and video stream (DV type-1)",
            ["ivas"] = "Interleaved audio and video stream (non-standard spelling of 'iavs')",
        };

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            int read = AviUtil.ReadPayload(parser, header, StructSize, out byte[] payload);

            if (read < 48)
            {
                dataLines.Add(("<Error>", $"AVISTREAMHEADER requires at least 48 bytes, {read} available."));
                AviUtil.AddUnparsedLength(dataLines, header, 0);
                return AviUtil.Build(parser, node, header, "AviStreamHeader", dataLines);
            }

            bool be = parser.IsBigEndian;
            var span = new ReadOnlySpan<byte>(payload, 0, read);

            // ---- fccType ----
            dataLines.Add(("fccType", AviUtil.FormatFourCCField(span.Slice(0, 4), be)));
            string? typeCode = AviUtil.TryReadFourCCField(span.Slice(0, 4));
            dataLines.Add(("<fccType>",
                typeCode is not null && StreamTypes.TryGetValue(typeCode, out string? typeName)
                    ? typeName
                    : "Unknown stream type"));

            // ---- fccHandler ----
            dataLines.Add(("fccHandler", AviUtil.FormatFourCCField(span.Slice(4, 4), be)));
            if (AviUtil.TryReadFourCCField(span.Slice(4, 4)) is null)
            {
                uint handler = RIFFUtil.ReadUInt32(span.Slice(4, 4), be);
                dataLines.Add(("<fccHandler>", handler == 0
                    ? "Not specified; the system selects the handler"
                    : $"Numeric handler value {handler}"));
            }

            // ---- dwFlags ----
            uint flags = RIFFUtil.ReadUInt32(span.Slice(8, 4), be);
            dataLines.Add(("dwFlags", $"0x{flags:X8}"));
            dataLines.Add(("<dwFlags>", AviUtil.DescribeFlags(flags, StreamFlags)));

            dataLines.Add(("wPriority", RIFFUtil.ReadUInt16(span.Slice(12, 2), be).ToString()));

            ushort language = RIFFUtil.ReadUInt16(span.Slice(14, 2), be);
            dataLines.Add(("wLanguage", $"0x{language:X4}"));
            dataLines.Add(("<wLanguage>", AviUtil.DescribeLangId(language)));

            dataLines.Add(("dwInitialFrames", RIFFUtil.ReadUInt32(span.Slice(16, 4), be).ToString()));
            dataLines.Add(("dwScale", RIFFUtil.ReadUInt32(span.Slice(20, 4), be).ToString()));
            dataLines.Add(("dwRate", RIFFUtil.ReadUInt32(span.Slice(24, 4), be).ToString()));
            dataLines.Add(("dwStart", RIFFUtil.ReadUInt32(span.Slice(28, 4), be).ToString()));
            dataLines.Add(("dwLength", RIFFUtil.ReadUInt32(span.Slice(32, 4), be).ToString()));
            dataLines.Add(("dwSuggestedBufferSize", RIFFUtil.ReadUInt32(span.Slice(36, 4), be).ToString()));

            uint quality = RIFFUtil.ReadUInt32(span.Slice(40, 4), be);
            dataLines.Add(("dwQuality", quality == QualityDefault ? "0xFFFFFFFF" : quality.ToString()));
            if (quality == QualityDefault)
                dataLines.Add(("<dwQuality>", "-1: use the default quality setting for the codec"));
            else if (quality > 10000)
                dataLines.Add(("<Warning>", "dwQuality is outside the documented range 0-10000."));

            dataLines.Add(("dwSampleSize", RIFFUtil.ReadUInt32(span.Slice(44, 4), be).ToString()));

            long parsedBytes = 48;

            if (read >= StructSize)
            {
                short left = AviUtil.ReadInt16(span.Slice(48, 2), be);
                short top = AviUtil.ReadInt16(span.Slice(50, 2), be);
                short right = AviUtil.ReadInt16(span.Slice(52, 2), be);
                short bottom = AviUtil.ReadInt16(span.Slice(54, 2), be);

                dataLines.Add(("rcFrame", $"left={left}, top={top}, right={right}, bottom={bottom}"));
                parsedBytes = StructSize;
            }
            else
            {
                dataLines.Add(("<Warning>",
                    $"AVISTREAMHEADER is {StructSize} bytes; only {read} available (rcFrame is missing)."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'strh' chunk is truncated."));

            AviUtil.AddUnparsedLength(dataLines, header, parsedBytes);
            return AviUtil.Build(parser, node, header, "AviStreamHeader", dataLines);
        }
    }
}