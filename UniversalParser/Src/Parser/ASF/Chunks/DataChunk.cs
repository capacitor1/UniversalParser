using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ASF.Chunks
{
    /// <summary>
    /// Data Object（MS-ASF 2.3.6）。
    /// 固定头部：FileID(GUID,16) + TotalDataPackets(QWORD,8) + Reserved(WORD,2)。
    /// 头部之后是数据包流（PES/压缩 payload），属"独立二进制数据"，不解析，
    /// 仅以 &lt;PayloadLength&gt; 呈现其长度。
    /// </summary>
    internal static class DataChunk
    {
        public static ParseResult Parse(ASFParser parser, Node node, ASFObjectHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            var reader = parser.CreatePayloadReader(header);

            if (!reader.TryReadGuid(out Guid fileId) ||
                !reader.TryReadUInt64(out ulong totalDataPackets) ||
                !reader.TryReadUInt16(out ushort reserved))
            {
                dataLines.Add(("<Error>", "Failed to read the Data Object fields (26 bytes required)."));
                AddRemaining(reader, dataLines);
                return Build(parser, node, header, dataLines);
            }

            dataLines.Add(("FileID", ASFUtil.GuidDisplay(fileId)));
            dataLines.Add(("<FileID>", "File identifier (same value as in the File Properties Object)."));
            dataLines.Add(("TotalDataPackets", totalDataPackets.ToString()));
            dataLines.Add(("<TotalDataPackets>", $"{totalDataPackets:N0} packets"));
            dataLines.Add(("Reserved", $"0x{reserved:X4}"));
            dataLines.Add(("<Reserved>", "Reserved; conformant ASF writers write 0x0101."));

            // 数据包流：不解析，只给长度
            if (reader.Remaining is long remaining && remaining > 0)
            {
                dataLines.Add(("<PayloadLength>", ASFUtil.FormatBytes(remaining)));
                dataLines.Add(("<Note>", "Data packets (opaque media data); not parsed."));
                reader.Skip(remaining);
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "Declared object size exceeds the available range; content was clamped."));

            return Build(parser, node, header, dataLines);
        }

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
                Title = ASFUtil.MakeTitle("Data", ASFUtil.GuidDisplay(header.Guid)),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ObjectStart, (long)node.Length),
            };
    }
}