using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ASF.Chunks
{
    /// <summary>
    /// ASF 容器对象：Header Object 与 Header Extension Object。
    /// 规范：容器只呈现关于自身的最基本信息（对象自身的结构字段），不列子对象、不列长度。
    /// 容器的数据全部由子节点承载，因此不出现 &lt;PayloadLength&gt;。
    /// </summary>
    internal static class ContainerChunk
    {
        public static ParseResult Parse(ASFParser parser, Node node, ASFObjectHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            string readableName;

            if (header.Guid == ASFUtil.HeaderObject)
            {
                readableName = "Header";
                var reader = parser.CreatePayloadReader(header);

                if (!reader.TryReadUInt32(out uint numberOfHeaderObjects))
                {
                    dataLines.Add(("<Error>", "Failed to read the header structure."));
                }
                else
                {
                    dataLines.Add(("NumberOfHeaderObjects", numberOfHeaderObjects.ToString()));

                    byte reserved1 = reader.TryReadByte(out byte r1) ? r1 : (byte)0;
                    byte reserved2 = reader.TryReadByte(out byte r2) ? r2 : (byte)0;
                    dataLines.Add(("Reserved1", $"0x{reserved1:X2}"));
                    dataLines.Add(("Reserved2", $"0x{reserved2:X2}"));

                    if (reserved1 != 0x01 || reserved2 != 0x02)
                        dataLines.Add(("<Warning>", "Unexpected reserved field values (expected 0x01 0x02)."));
                }
            }
            else if (header.Guid == ASFUtil.HeaderExtensionObject)
            {
                readableName = "HeaderExtension";
                var reader = parser.CreatePayloadReader(header);

                ushort reserved1 = reader.TryReadUInt16(out ushort v1) ? v1 : (ushort)0;
                ushort reserved2 = reader.TryReadUInt16(out ushort v2) ? v2 : (ushort)0;
                bool haveSize = reader.TryReadUInt32(out uint extensionDataSize);

                dataLines.Add(("Reserved1", $"0x{reserved1:X4}"));
                dataLines.Add(("Reserved2", $"0x{reserved2:X4}"));
                dataLines.Add(("ExtensionDataSize", haveSize ? extensionDataSize.ToString() : "(unreadable)"));

                if (reserved1 != 0xABAB || reserved2 != 0xCDCD)
                    dataLines.Add(("<Warning>", "Unexpected reserved field values (expected 0xABAB 0xCDCD)."));

                if (haveSize && extensionDataSize != header.PayloadLength - 8)
                {
                    dataLines.Add(("<Warning>",
                        $"ExtensionDataSize ({extensionDataSize}) does not match the remaining payload ({header.PayloadLength - 8} bytes)."));
                }
            }
            else
            {
                readableName = header.Name ?? "Container";
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "Declared object size exceeds the available range; content was clamped."));

            return new ParseResult
            {
                Title = ASFUtil.MakeTitle(readableName, ASFUtil.GuidDisplay(header.Guid)),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ObjectStart, (long)node.Length),
            };
        }
    }
}