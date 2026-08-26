using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ASF.Chunks
{
    /// <summary>
    /// Streaming Media Properties Object（MS-ASF 2.3.9）。
    /// 描述单个流在播放期间的实时传输属性。
    /// Decoder Complexity Profile 是 codec 私有的 opaque 数据，不解析 → &lt;PayloadLength&gt;。
    /// </summary>
    internal static class StreamingMediaPropertiesChunk
    {
        private static readonly Dictionary<Guid, string> StreamTypeNames = new()
        {
            [ASFUtil.AudioMedia] = "Audio Media",
            [ASFUtil.VideoMedia] = "Video Media",
            [ASFUtil.CommandMedia] = "Command Media",
            [ASFUtil.JFIFMedia] = "JFIF Media",
            [ASFUtil.DegradableJPEGMedia] = "Degradable JPEG Media",
            [ASFUtil.FileTransferMedia] = "File Transfer Media",
            [ASFUtil.BinaryMedia] = "Binary Media",
            [ASFUtil.WebStreamMedia] = "Web Stream Media",
        };

        public static ParseResult Parse(ASFParser parser, Node node, ASFObjectHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            var reader = parser.CreatePayloadReader(header);

            // 固定字段：4+2+16+4+4+4 = 34 字节
            if (!reader.TryReadUInt32(out uint dataBitrate) ||
                !reader.TryReadUInt16(out ushort streamNumber) ||
                !reader.TryReadGuid(out Guid streamType) ||
                !reader.TryReadUInt32(out uint preDecoderBufferSize) ||
                !reader.TryReadUInt32(out uint postDecoderBufferSize) ||
                !reader.TryReadUInt32(out uint decoderComplexityProfileSize))
            {
                dataLines.Add(("<Error>", "Failed to read the Streaming Media Properties fields (34 bytes required)."));
                AddRemaining(reader, dataLines);
                return Build(parser, node, header, dataLines);
            }

            // Data Bitrate
            dataLines.Add(("DataBitrate", dataBitrate.ToString()));
            dataLines.Add(("<DataBitrate>", FormatBitrate(dataBitrate)));

            // Stream Number：bit0-6 = 流号，bit15 = 加密标志
            int streamId = streamNumber & 0x7F;
            bool encrypted = (streamNumber & 0x8000) != 0;
            dataLines.Add(("StreamNumber", streamNumber.ToString()));
            dataLines.Add(("<StreamNumber>", $"Stream #{streamId}" + (encrypted ? " (encrypted)" : string.Empty)));

            // Stream Type
            dataLines.Add(("StreamType", ASFUtil.GuidDisplay(streamType)));
            dataLines.Add(("<StreamType>", DescribeStreamType(streamType)));

            // 缓冲大小（毫秒）
            dataLines.Add(("PreDecoderBufferSize", preDecoderBufferSize.ToString()));
            dataLines.Add(("<PreDecoderBufferSize>", $"{preDecoderBufferSize} ms"));
            dataLines.Add(("PostDecoderBufferSize", postDecoderBufferSize.ToString()));
            dataLines.Add(("<PostDecoderBufferSize>", $"{postDecoderBufferSize} ms"));

            // Decoder Complexity Profile（opaque，codec 私有）
            dataLines.Add(("DecoderComplexityProfileSize", decoderComplexityProfileSize.ToString()));

            long available = reader.Remaining ?? 0;
            if (available < decoderComplexityProfileSize)
            {
                dataLines.Add(("<Warning>",
                    $"Decoder complexity profile is truncated ({available} of {decoderComplexityProfileSize} bytes available)."));
                if (available > 0)
                {
                    dataLines.Add(("<PayloadLength>", ASFUtil.FormatBytes(available)));
                    reader.Skip(available);
                }
            }
            else
            {
                dataLines.Add(("<PayloadLength>", ASFUtil.FormatBytes(decoderComplexityProfileSize)));
                dataLines.Add(("<Note>", "Decoder complexity profile is codec-specific opaque data; see the raw data view."));
                reader.Skip(decoderComplexityProfileSize);

                if (reader.Remaining is long trailing && trailing > 0)
                    dataLines.Add(("<Note>", $"{ASFUtil.FormatBytes(trailing)} of unexplained bytes follow the profile."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "Declared object size exceeds the available range; content was clamped."));

            return Build(parser, node, header, dataLines);
        }

        private static string FormatBitrate(uint bitsPerSecond)
        {
            if (bitsPerSecond >= 1_000_000) return $"{bitsPerSecond / 1_000_000.0:0.###} Mbit/s";
            if (bitsPerSecond >= 1_000) return $"{bitsPerSecond / 1_000.0:0.##} kbit/s";
            return $"{bitsPerSecond} bit/s";
        }

        private static string DescribeStreamType(Guid guid) =>
            StreamTypeNames.TryGetValue(guid, out string? name) ? name : "Unknown stream type";

        private static void AddRemaining(ASFReader reader, List<(string K, string V)> lines)
        {
            if (reader.Remaining is not long remaining || remaining <= 0) return;
            lines.Add(("<PayloadLength>", ASFUtil.FormatBytes(remaining)));
            lines.Add(("<Note>", "Bytes remaining after the last declared field; not parsed."));
        }

        private static ParseResult Build(
            ASFParser parser, Node node, ASFObjectHeader header, List<(string K, string V)> dataLines) =>
            new()
            {
                Title = ASFUtil.MakeTitle("StreamingMediaProperties", ASFUtil.GuidDisplay(header.Guid)),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ObjectStart, (long)node.Length),
            };
    }
}