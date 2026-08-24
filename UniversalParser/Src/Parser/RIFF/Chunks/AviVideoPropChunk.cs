using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// AVI 视频属性块 'vprp'（VideoPropHeader，OpenDML AVI 扩展定义）。
    /// 固定部分 36 字节，其后是 nbFieldPerFrame 个 VIDEO_FIELD_DESC，每项 32 字节。
    /// </summary>
    internal static class AviVideoPropChunk
    {
        private const int HeaderSize = 36;
        private const int FieldDescSize = 32;

        private const string FieldColumns =
            "CompressedBMHeight,CompressedBMWidth,ValidBMHeight,ValidBMWidth,"
            + "ValidBMXOffset,ValidBMYOffset,VideoXOffsetInT,VideoYValidStartLine";

        private static readonly Dictionary<uint, string> FormatTokens = new()
        {
            [0] = "FORMAT_UNKNOWN",
            [1] = "FORMAT_PAL_SQUARE",
            [2] = "FORMAT_PAL_CCIR_601",
            [3] = "FORMAT_NTSC_SQUARE",
            [4] = "FORMAT_NTSC_CCIR_601",
        };

        private static readonly Dictionary<uint, string> VideoStandards = new()
        {
            [0] = "STANDARD_UNKNOWN",
            [1] = "STANDARD_PAL",
            [2] = "STANDARD_NTSC",
            [3] = "STANDARD_SECAM",
        };

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            bool be = parser.IsBigEndian;

            long payloadLength = Math.Max(0, header.PayloadLength);
            if (payloadLength < HeaderSize)
            {
                dataLines.Add(("<Error>",
                    $"VideoPropHeader requires {HeaderSize} bytes, {payloadLength} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "VideoPropHeader", dataLines);
            }

            // 先按声明值确定要读多少，再由负载可用量裁剪
            Span<byte> headerBytes = stackalloc byte[HeaderSize];
            if (parser.ReadAt(header.PayloadStart, headerBytes) < HeaderSize)
            {
                dataLines.Add(("<Error>", "Unable to read the fixed part of VideoPropHeader."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "VideoPropHeader", dataLines);
            }

            uint formatToken = RIFFUtil.ReadUInt32(headerBytes.Slice(0, 4), be);
            uint videoStandard = RIFFUtil.ReadUInt32(headerBytes.Slice(4, 4), be);
            uint verticalRefreshRate = RIFFUtil.ReadUInt32(headerBytes.Slice(8, 4), be);
            uint hTotalInT = RIFFUtil.ReadUInt32(headerBytes.Slice(12, 4), be);
            uint vTotalInLines = RIFFUtil.ReadUInt32(headerBytes.Slice(16, 4), be);
            uint frameAspectRatio = RIFFUtil.ReadUInt32(headerBytes.Slice(20, 4), be);
            uint frameWidthInPixels = RIFFUtil.ReadUInt32(headerBytes.Slice(24, 4), be);
            uint frameHeightInLines = RIFFUtil.ReadUInt32(headerBytes.Slice(28, 4), be);
            uint nbFieldPerFrame = RIFFUtil.ReadUInt32(headerBytes.Slice(32, 4), be);

            dataLines.Add(("VideoFormatToken", formatToken.ToString()));
            dataLines.Add(("<VideoFormatToken>", FormatTokens.TryGetValue(formatToken, out string? formatName)
                ? formatName
                : "Unknown video format token"));

            dataLines.Add(("VideoStandard", videoStandard.ToString()));
            dataLines.Add(("<VideoStandard>", VideoStandards.TryGetValue(videoStandard, out string? standardName)
                ? standardName
                : "Unknown video standard"));

            dataLines.Add(("dwVerticalRefreshRate", verticalRefreshRate.ToString()));
            dataLines.Add(("dwHTotalInT", hTotalInT.ToString()));
            dataLines.Add(("dwVTotalInLines", vTotalInLines.ToString()));

            dataLines.Add(("dwFrameAspectRatio", $"0x{frameAspectRatio:X8}"));
            dataLines.Add(("<dwFrameAspectRatio>", DescribeAspectRatio(frameAspectRatio)));

            dataLines.Add(("dwFrameWidthInPixels", frameWidthInPixels.ToString()));
            dataLines.Add(("dwFrameHeightInLines", frameHeightInLines.ToString()));
            dataLines.Add(("nbFieldPerFrame", nbFieldPerFrame.ToString()));

            // ---- 校验（必须排在数组之前）----
            if (nbFieldPerFrame is not (1 or 2))
            {
                dataLines.Add(("<Warning>",
                    "nbFieldPerFrame is expected to be 1 for progressive frames or 2 for interlaced fields."));
            }

            long availableFields = (payloadLength - HeaderSize) / FieldDescSize;
            long listedFields = Math.Min(nbFieldPerFrame, availableFields);

            if (listedFields < nbFieldPerFrame)
            {
                dataLines.Add(("<Warning>",
                    $"nbFieldPerFrame declares {nbFieldPerFrame} field descriptor(s) but only {availableFields} "
                    + $"fit in the payload."));
            }
            else if (availableFields > nbFieldPerFrame)
            {
                dataLines.Add(("<Note>",
                    $"The payload has room for {availableFields} field descriptor(s); "
                    + $"only the {nbFieldPerFrame} declared by nbFieldPerFrame are listed."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'vprp' chunk is truncated."));

            long parsedBytes = HeaderSize + listedFields * FieldDescSize;
            ChunkUtil.AddUnparsedLength(dataLines, header, parsedBytes);

            if (listedFields > 0)
                AppendFieldInfo(parser, header, dataLines, listedFields, be);

            return ChunkUtil.Build(parser, node, header, "VideoPropHeader", dataLines);
        }

        private static void AppendFieldInfo(
            RIFFParser parser,
            in RIFFChunkHeader header,
            List<(string K, string V)> dataLines,
            long fieldCount,
            bool bigEndian)
        {
            // nbFieldPerFrame 实际只会是 1 或 2，且已按负载裁剪，可一次读完
            byte[] buffer = new byte[fieldCount * FieldDescSize];
            int read = parser.ReadAt(header.PayloadStart + HeaderSize, buffer);
            read -= read % FieldDescSize;

            if (read <= 0)
            {
                dataLines.Add(("<Warning>", "Unable to read the VIDEO_FIELD_DESC array."));
                return;
            }

            int usable = read / FieldDescSize;
            dataLines.Add(($"FieldInfo[{usable}]", FieldColumns));

            var block = new ReadOnlySpan<byte>(buffer, 0, read);
            for (int offset = 0; offset + FieldDescSize <= read; offset += FieldDescSize)
            {
                ReadOnlySpan<byte> entry = block.Slice(offset, FieldDescSize);

                dataLines.Add((string.Empty, string.Join(',',
                    RIFFUtil.ReadUInt32(entry.Slice(0, 4), bigEndian),
                    RIFFUtil.ReadUInt32(entry.Slice(4, 4), bigEndian),
                    RIFFUtil.ReadUInt32(entry.Slice(8, 4), bigEndian),
                    RIFFUtil.ReadUInt32(entry.Slice(12, 4), bigEndian),
                    RIFFUtil.ReadUInt32(entry.Slice(16, 4), bigEndian),
                    RIFFUtil.ReadUInt32(entry.Slice(20, 4), bigEndian),
                    RIFFUtil.ReadUInt32(entry.Slice(24, 4), bigEndian),
                    RIFFUtil.ReadUInt32(entry.Slice(28, 4), bigEndian))));
            }

            if (usable < fieldCount)
                dataLines.Add(("<Warning>", "The VIDEO_FIELD_DESC array is shorter than expected."));
        }

        /// <summary>dwFrameAspectRatio 打包为两个 WORD：高位字为 X，低位字为 Y。</summary>
        private static string DescribeAspectRatio(uint value)
        {
            ushort x = (ushort)(value >> 16);
            ushort y = (ushort)(value & 0xFFFF);

            if (x == 0 && y == 0) return "Not specified";
            if (x == 0 || y == 0) return $"{x}:{y} (degenerate ratio)";

            uint gcd = Gcd(x, y);
            string reduced = $"{x / gcd}:{y / gcd}";
            return reduced == $"{x}:{y}" ? reduced : $"{x}:{y} (reduces to {reduced})";
        }

        private static uint Gcd(uint a, uint b)
        {
            while (b != 0) (a, b) = (b, a % b);
            return a;
        }
    }
}