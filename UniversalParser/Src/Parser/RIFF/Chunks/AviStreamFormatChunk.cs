using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// AVI 流格式 'strf'。其布局由所属流的类型决定，但本解析器不做跨块关联：
    /// 布局仅依据本块自身的自描述字段判定（BITMAPINFOHEADER 的 biSize 是固定的结构长度常量，
    /// 而 WAVEFORMATEX 同位置为 wFormatTag|nChannels&lt;&lt;16，两者不可能取到相同值）。
    /// 证据不足时不做任何推测，整块作为未解析数据呈现并说明原因。
    /// </summary>
    internal static class AviStreamFormatChunk
    {
        private enum Layout
        {
            Unknown,
            BitmapInfoHeader,
            WaveFormatEx,
        }

        private const int BitmapInfoHeaderSize = 40;
        private const int BitmapV4HeaderSize = 108;
        private const int BitmapV5HeaderSize = 124;

        private static readonly Dictionary<uint, string> BitmapCompressions = new()
        {
            [0] = "BI_RGB: uncompressed RGB",
            [1] = "BI_RLE8: 8 bpp run-length encoded",
            [2] = "BI_RLE4: 4 bpp run-length encoded",
            [3] = "BI_BITFIELDS: uncompressed with explicit colour masks",
            [4] = "BI_JPEG: JPEG-compressed",
            [5] = "BI_PNG: PNG-compressed",
        };

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            bool be = parser.IsBigEndian;

            switch (DetectLayout(parser, header, out uint firstDword))
            {
                case Layout.BitmapInfoHeader:
                    dataLines.Add(("<LayoutSource>",
                        $"BITMAPINFOHEADER, determined from this chunk's own biSize field ({firstDword})"));
                    return ParseBitmapInfoHeader(parser, node, header, dataLines, be);

                case Layout.WaveFormatEx:
                    dataLines.Add(("<LayoutSource>",
                        "WAVEFORMATEX, determined from this chunk's own wFormatTag and nChannels fields"));
                    long parsed = WaveFormatEx.Populate(
                        parser, header.PayloadStart, header.PayloadLength, be, dataLines);

                    if (header.IsTruncated)
                        dataLines.Add(("<Warning>", "The 'strf' chunk is truncated."));

                    AviUtil.AddUnparsedLength(dataLines, header, parsed);
                    return AviUtil.Build(parser, node, header, "AudioStreamFormat", dataLines);

                default:
                    dataLines.Add(("<Note>",
                        "The 'strf' layout is selected by fccType in the sibling 'strh' chunk. This parser treats "
                        + "every chunk independently and this chunk carries no self-describing layout marker, "
                        + "so the payload is left undecoded."));

                    if (header.IsTruncated)
                        dataLines.Add(("<Warning>", "The 'strf' chunk is truncated."));

                    AviUtil.AddUnparsedLength(dataLines, header, 0);
                    return AviUtil.Build(parser, node, header, "StreamFormat", dataLines);
            }
        }

        private static Layout DetectLayout(RIFFParser parser, in RIFFChunkHeader header, out uint firstDword)
        {
            firstDword = 0;
            if (header.PayloadLength < 14) return Layout.Unknown;

            Span<byte> probe = stackalloc byte[4];
            if (parser.ReadAt(header.PayloadStart, probe) < 4) return Layout.Unknown;

            bool be = parser.IsBigEndian;
            firstDword = RIFFUtil.ReadUInt32(probe, be);

            // BITMAPINFOHEADER 家族：biSize 是结构长度常量，且负载必须容纳该结构。
            if (firstDword is BitmapInfoHeaderSize or BitmapV4HeaderSize or BitmapV5HeaderSize
                && header.PayloadLength >= BitmapInfoHeaderSize)
            {
                return Layout.BitmapInfoHeader;
            }

            // WAVEFORMATEX：wFormatTag 非零，nChannels 在合理范围内。
            ushort formatTag = RIFFUtil.ReadUInt16(probe[..2], be);
            ushort channels = RIFFUtil.ReadUInt16(probe.Slice(2, 2), be);
            if (formatTag != 0 && channels is >= 1 and <= 256)
                return Layout.WaveFormatEx;

            return Layout.Unknown;
        }

        private static ParseResult ParseBitmapInfoHeader(
            RIFFParser parser,
            Node node,
            in RIFFChunkHeader header,
            List<(string K, string V)> dataLines,
            bool be)
        {
            int read = AviUtil.ReadPayload(parser, header, BitmapInfoHeaderSize, out byte[] payload);
            if (read < BitmapInfoHeaderSize)
            {
                dataLines.Add(("<Error>",
                    $"BITMAPINFOHEADER requires {BitmapInfoHeaderSize} bytes, {read} available."));
                AviUtil.AddUnparsedLength(dataLines, header, 0);
                return AviUtil.Build(parser, node, header, "VideoStreamFormat", dataLines);
            }

            var span = new ReadOnlySpan<byte>(payload, 0, read);

            uint biSize = RIFFUtil.ReadUInt32(span.Slice(0, 4), be);
            int biWidth = AviUtil.ReadInt32(span.Slice(4, 4), be);
            int biHeight = AviUtil.ReadInt32(span.Slice(8, 4), be);
            ushort biPlanes = RIFFUtil.ReadUInt16(span.Slice(12, 2), be);
            ushort biBitCount = RIFFUtil.ReadUInt16(span.Slice(14, 2), be);
            uint biCompressionValue = RIFFUtil.ReadUInt32(span.Slice(16, 4), be);
            uint biClrUsed = RIFFUtil.ReadUInt32(span.Slice(32, 4), be);

            dataLines.Add(("biSize", biSize.ToString()));
            dataLines.Add(("biWidth", biWidth.ToString()));
            dataLines.Add(("biHeight", biHeight.ToString()));
            if (biHeight < 0)
            {
                dataLines.Add(("<biHeight>",
                    $"Negative height: top-down DIB, {Math.Abs((long)biHeight)} rows, origin at the upper-left corner"));
            }

            dataLines.Add(("biPlanes", biPlanes.ToString()));
            dataLines.Add(("biBitCount", biBitCount.ToString()));
            dataLines.Add(("biCompression", AviUtil.FormatFourCCField(span.Slice(16, 4), be)));
            dataLines.Add(("<biCompression>", DescribeCompression(span.Slice(16, 4), biCompressionValue, be)));
            dataLines.Add(("biSizeImage", RIFFUtil.ReadUInt32(span.Slice(20, 4), be).ToString()));
            dataLines.Add(("biXPelsPerMeter", AviUtil.ReadInt32(span.Slice(24, 4), be).ToString()));
            dataLines.Add(("biYPelsPerMeter", AviUtil.ReadInt32(span.Slice(28, 4), be).ToString()));
            dataLines.Add(("biClrUsed", biClrUsed.ToString()));
            dataLines.Add(("biClrImportant", RIFFUtil.ReadUInt32(span.Slice(36, 4), be).ToString()));

            if (biPlanes != 1)
                dataLines.Add(("<Warning>", "biPlanes must be 1 for BITMAPINFOHEADER."));

            long parsedBytes = BitmapInfoHeaderSize;
            long remaining = header.PayloadLength - parsedBytes;

            if (remaining > 0)
            {
                if (biBitCount is > 0 and <= 8)
                {
                    long entries = biClrUsed != 0 ? biClrUsed : 1L << biBitCount;
                    dataLines.Add(("<Note>",
                        $"Trailing bytes are expected to hold a colour table of {entries} RGBQUAD entries "
                        + $"({entries * 4} bytes); palette entries are not decoded."));
                }
                else if (biCompressionValue == 3)
                {
                    dataLines.Add(("<Note>",
                        "BI_BITFIELDS: trailing bytes hold the red, green and blue colour masks; not decoded."));
                }
                else
                {
                    dataLines.Add(("<Note>",
                        "Trailing bytes hold codec-specific extra data; not decoded by design."));
                }
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'strf' chunk is truncated."));

            AviUtil.AddUnparsedLength(dataLines, header, parsedBytes);
            return AviUtil.Build(parser, node, header, "VideoStreamFormat", dataLines);
        }

        private static string DescribeCompression(ReadOnlySpan<byte> raw, uint value, bool bigEndian)
        {
            if (BitmapCompressions.TryGetValue(value, out string? name))
                return name;

            string? fourCC = AviUtil.TryReadFourCCField(raw);
            return fourCC is not null
                ? $"Codec FourCC '{RIFFUtil.Sanitize(fourCC)}'"
                : $"Unknown compression value {value}";
        }
    }
}