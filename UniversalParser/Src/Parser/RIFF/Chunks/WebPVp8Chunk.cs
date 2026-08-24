using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WebP 有损位流 'VP8 '（第四个字符为 ASCII 空格）。
    /// 仅解析 VP8 未压缩数据块：3 字节 frame tag，关键帧再加 3 字节起始码与两个 16 位尺寸码，
    /// 共 10 字节。其后的熵编码分区按设计不解析。
    /// 字段名沿用 RFC 6386（VP8 Data Format and Decoding Guide）的命名。
    /// </summary>
    internal static class WebPVp8Chunk
    {
        private const int FrameTagSize = 3;
        private const int KeyFrameHeaderSize = 10;

        private static readonly byte[] StartCode = [0x9D, 0x01, 0x2A];

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            Span<byte> buffer = stackalloc byte[KeyFrameHeaderSize];
            int want = (int)Math.Min(KeyFrameHeaderSize, Math.Max(0, header.PayloadLength));
            int read = want > 0 ? parser.ReadAt(header.PayloadStart, buffer[..want]) : 0;

            if (read < FrameTagSize)
            {
                dataLines.Add(("<Error>",
                    $"The VP8 frame tag requires {FrameTagSize} bytes, {read} available."));
                WebPUtil.AddUnparsedLength(dataLines, header, 0);
                return WebPUtil.Build(parser, node, header, "LossyBitstream", dataLines);
            }

            uint frameTag = WebPUtil.ReadUInt24LE(buffer[..FrameTagSize]);

            int frameType = (int)(frameTag & 0x1);
            int version = (int)((frameTag >> 1) & 0x7);
            int showFrame = (int)((frameTag >> 4) & 0x1);
            uint firstPartSize = (frameTag >> 5) & 0x7FFFF;

            dataLines.Add(("frame_type", frameType.ToString()));
            dataLines.Add(("version", version.ToString()));
            dataLines.Add(("show_frame", showFrame.ToString()));
            dataLines.Add(("first_part_size", firstPartSize.ToString()));

            dataLines.Add(("<frame_type>", frameType == 0 ? "0: key frame" : "1: interframe"));
            dataLines.Add(("<version>", DescribeVersion(version)));

            long parsedBytes = FrameTagSize;

            if (frameType != 0)
            {
                dataLines.Add(("<Warning>",
                    "A WebP 'VP8 ' chunk must contain a key frame; interframe headers carry no size fields."));
            }
            else if (read < KeyFrameHeaderSize)
            {
                dataLines.Add(("<Error>",
                    $"The key frame header requires {KeyFrameHeaderSize} bytes, {read} available."));
            }
            else
            {
                ReadOnlySpan<byte> startCode = buffer.Slice(3, 3);
                ushort horizontalSizeCode = (ushort)(buffer[6] | (buffer[7] << 8));
                ushort verticalSizeCode = (ushort)(buffer[8] | (buffer[9] << 8));

                dataLines.Add(("start_code",
                    $"0x{startCode[0]:X2}{startCode[1]:X2}{startCode[2]:X2}"));
                dataLines.Add(("horizontal_size_code", $"0x{horizontalSizeCode:X4}"));
                dataLines.Add(("vertical_size_code", $"0x{verticalSizeCode:X4}"));

                int width = horizontalSizeCode & 0x3FFF;
                int horizontalScale = horizontalSizeCode >> 14;
                int height = verticalSizeCode & 0x3FFF;
                int verticalScale = verticalSizeCode >> 14;

                dataLines.Add(("<width>", width.ToString()));
                dataLines.Add(("<horizontal_scale>", DescribeScale(horizontalScale)));
                dataLines.Add(("<height>", height.ToString()));
                dataLines.Add(("<vertical_scale>", DescribeScale(verticalScale)));

                if (!startCode.SequenceEqual(StartCode))
                {
                    dataLines.Add(("<Warning>",
                        "The key frame start code must be 0x9D012A; the size fields are therefore unreliable."));
                }

                parsedBytes = KeyFrameHeaderSize;
            }

            if (version > 3)
                dataLines.Add(("<Warning>", "Versions above 3 are reserved."));

            if (showFrame == 0)
                dataLines.Add(("<Note>", "show_frame is 0; the frame is not intended for display."));

            if (firstPartSize + parsedBytes > header.PayloadLength)
            {
                dataLines.Add(("<Warning>",
                    $"first_part_size ({firstPartSize:N0}) extends beyond the chunk payload."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'VP8 ' chunk is truncated."));

            WebPUtil.AddUnparsedLength(dataLines, header, parsedBytes);
            return WebPUtil.Build(parser, node, header, "LossyBitstream", dataLines);
        }

        /// <summary>RFC 6386 将 version 定义为重建滤波器与环路滤波器的组合选择。</summary>
        private static string DescribeVersion(int version) => version switch
        {
            0 => "0: bicubic reconstruction filter, normal loop filter",
            1 => "1: bilinear reconstruction filter, simple loop filter",
            2 => "2: bilinear reconstruction filter, no loop filter",
            3 => "3: no reconstruction filter (full pixel only), no loop filter",
            _ => $"{version}: reserved",
        };

        /// <summary>尺寸码高 2 位为放大比例，解码器可据此在显示前上采样。</summary>
        private static string DescribeScale(int scale) => scale switch
        {
            0 => "0: no upscaling",
            1 => "1: upscale by 5/4",
            2 => "2: upscale by 5/3",
            3 => "3: upscale by 2",
            _ => scale.ToString(),
        };
    }
}