using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.FLV.Chunks
{
    internal static class FLVHeaderChunk
    {
        public static ParseResult Parse(
            FLVParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            Span<byte> header = stackalloc byte[FLVUtil.MinimumHeaderSize];

            int read = parser.ReadAt((long)node.Position, header);

            if (read < FLVUtil.MinimumHeaderSize)
            {
                return new ParseResult
                {
                    Title = FLVUtil.MakeTitle("FlashVideoHeader", node.NodeName),
                    Position = node.Position,
                    Length = node.Length,
                    DataLines =
                    [
                        ("<Error>", "The FLV header is truncated."),
                        ("<PayloadLength>", FLVUtil.FormatBytes((long)node.Length))
                    ],
                    RawData = parser.CreateRawStream(
                        (long)node.Position,
                        (long)node.Length)
                };
            }

            string signature = string.Create(
                3,
                header.ToArray(),
                static (characters, bytes) =>
                {
                    characters[0] = (char)bytes[0];
                    characters[1] = (char)bytes[1];
                    characters[2] = (char)bytes[2];
                });

            byte version = header[3];
            byte typeFlags = header[4];

            byte typeFlagsReserved = (byte)(typeFlags >> 3);
            byte typeFlagsAudio = (byte)((typeFlags >> 2) & 0x01);
            byte typeFlagsReserved2 = (byte)((typeFlags >> 1) & 0x01);
            byte typeFlagsVideo = (byte)(typeFlags & 0x01);

            uint dataOffset = FLVUtil.ReadUInt32BE(header.Slice(5, 4));

            var dataLines = new List<(string K, string V)>
            {
                // 以下名称来自 FLV Header 语法定义
                ("Signature", signature),
                ("Version", version.ToString()),
                ("TypeFlagsReserved", typeFlagsReserved.ToString()),
                ("TypeFlagsAudio", typeFlagsAudio.ToString()),
                ("<TypeFlagsAudio>", typeFlagsAudio != 0 ? "Audio tags are present" : "No audio tags declared"),
                ("TypeFlagsReserved2", typeFlagsReserved2.ToString()),
                ("TypeFlagsVideo", typeFlagsVideo.ToString()),
                ("<TypeFlagsVideo>", typeFlagsVideo != 0 ? "Video tags are present" : "No video tags declared"),
                ("DataOffset", dataOffset.ToString())
            };

            long extensionLength = Math.Max(
                0,
                Math.Min((long)dataOffset, (long)node.Length)
                - FLVUtil.MinimumHeaderSize);

            if (extensionLength > 0)
            {
                // DataOffset > 9 的可选头扩展未定义，因此它是未解析数据。
                dataLines.Add((
                    "<PayloadLength>",
                    FLVUtil.FormatBytes(extensionLength)));
            }

            long previousTagSize0Offset = dataOffset;

            if (previousTagSize0Offset + 4 <= (long)node.Position + (long)node.Length)
            {
                Span<byte> previousTagSizeBuffer = stackalloc byte[4];

                if (parser.ReadAt(previousTagSize0Offset, previousTagSizeBuffer) == 4)
                {
                    uint previousTagSize0 =
                        FLVUtil.ReadUInt32BE(previousTagSizeBuffer);

                    dataLines.Add((
                        "PreviousTagSize0",
                        previousTagSize0.ToString()));

                    if (previousTagSize0 != 0)
                    {
                        dataLines.Add((
                            "<Warning>",
                            $"PreviousTagSize0 should be 0, found {previousTagSize0}."));
                    }
                }
            }
            else
            {
                dataLines.Add((
                    "<Warning>",
                    "PreviousTagSize0 is missing or truncated."));
            }

            if (signature != "FLV")
            {
                dataLines.Add((
                    "<Warning>",
                    $"Signature should be FLV, found {FLVUtil.Sanitize(signature)}."));
            }

            if (version != 1)
            {
                dataLines.Add((
                    "<Note>",
                    $"FLV version {version} is not the commonly documented version 1."));
            }

            if (typeFlagsReserved != 0 || typeFlagsReserved2 != 0)
            {
                dataLines.Add((
                    "<Warning>",
                    "One or more reserved TypeFlags bits are non-zero."));
            }

            return new ParseResult
            {
                Title = FLVUtil.MakeTitle("FlashVideoHeader", node.NodeName),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(
                    (long)node.Position,
                    (long)node.Length)
            };
        }
    }
}