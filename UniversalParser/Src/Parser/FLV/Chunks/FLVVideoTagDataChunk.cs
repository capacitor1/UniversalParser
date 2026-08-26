using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.FLV.Chunks
{
    internal static class FLVVideoTagDataChunk
    {
        private static readonly Dictionary<byte, string> FrameTypes = new()
        {
            [1] = "Key frame",
            [2] = "Inter frame",
            [3] = "Disposable inter frame",
            [4] = "Generated key frame",
            [5] = "Video info or command frame"
        };

        private static readonly Dictionary<byte, string> CodecIds = new()
        {
            [1] = "JPEG",
            [2] = "Sorenson H.263",
            [3] = "Screen video",
            [4] = "On2 VP6",
            [5] = "On2 VP6 with alpha channel",
            [6] = "Screen video version 2",
            [7] = "AVC",
            [12] = "HEVC" //unofficial
        };

        public static ParseResult Parse(
            FLVParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            long payloadLength = (long)node.Length;
            var dataLines = new List<(string K, string V)>();

            if (payloadLength < 1)
            {
                dataLines.Add((
                    "<Error>",
                    "VideoTagData does not contain the mandatory video header."));

                return Build(parser, node, dataLines);
            }

            Span<byte> header = stackalloc byte[5];
            int wanted = (int)Math.Min(header.Length, payloadLength);
            int read = parser.ReadAt(
                (long)node.Position,
                header[..wanted]);

            if (read < 1)
            {
                dataLines.Add((
                    "<Error>",
                    "Unable to read the video header."));
                dataLines.Add((
                    "<PayloadLength>",
                    FLVUtil.FormatBytes(payloadLength)));

                return Build(parser, node, dataLines);
            }

            byte first = header[0];
            byte frameType = (byte)(first >> 4);
            byte codecId = (byte)(first & 0x0F);

            dataLines.Add(("FrameType", frameType.ToString()));
            dataLines.Add((
                "<FrameType>",
                FrameTypes.TryGetValue(frameType, out string? frame)
                    ? frame
                    : "Unknown or experimental frame type"));

            dataLines.Add(("CodecID", codecId.ToString()));
            dataLines.Add((
                "<CodecID>",
                CodecIds.TryGetValue(codecId, out string? codec)
                    ? codec
                    : "Unknown or experimental codec"));

            long parsedBytes = 1;

            // AVCVIDEOPACKET
            if (codecId == 7)
            {
                if (read >= 5)
                {
                    byte avcPacketType = header[1];
                    int compositionTime = FLVUtil.ReadInt24BE(header.Slice(2, 3));

                    dataLines.Add((
                        "AVCPacketType",
                        avcPacketType.ToString()));

                    dataLines.Add((
                        "<AVCPacketType>",
                        avcPacketType switch
                        {
                            0 => "AVC sequence header",
                            1 => "AVC NALU",
                            2 => "AVC end of sequence",
                            _ => "Unknown or experimental AVC packet type"
                        }));

                    dataLines.Add((
                        "CompositionTime",
                        compositionTime.ToString()));

                    dataLines.Add((
                        "<CompositionTime>",
                        $"{compositionTime} ms"));

                    parsedBytes = 5;
                }
                else
                {
                    dataLines.Add((
                        "<Warning>",
                        "AVC packet header is truncated."));
                }
            }

            /*
             * 编码图像、AVCDecoderConfigurationRecord、NALU 等属于对应编解码器层，
             * 不由 FLV 层继续解释。
             */
            long unparsed = Math.Max(0, payloadLength - parsedBytes);

            if (unparsed > 0)
            {
                dataLines.Add((
                    "<PayloadLength>",
                    FLVUtil.FormatBytes(unparsed)));
            }

            return Build(parser, node, dataLines);
        }

        private static ParseResult Build(
            FLVParser parser,
            Node node,
            List<(string K, string V)> dataLines) =>
            new()
            {
                Title = FLVUtil.MakeTitle("VideoTagData", node.NodeName),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(
                    (long)node.Position,
                    (long)node.Length)
            };
    }
}