using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ASF.Chunks
{
    /// <summary>
    /// File Properties Object（MS-ASF 2.3.4）。
    /// 固定 72 字节负载，描述整个 ASF 文件的全局属性。
    /// 只呈现本对象自身的字段，不做任何跨对象/与物理文件的交叉验证。
    /// </summary>
    internal static class FilePropertiesChunk
    {
        public static ParseResult Parse(ASFParser parser, Node node, ASFObjectHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            var reader = parser.CreatePayloadReader(header);

            // 16 + 5*8 + 4*4 = 72 字节固定负载
            if (!reader.TryReadGuid(out Guid fileId) ||
                !reader.TryReadUInt64(out ulong fileSize) ||
                !reader.TryReadUInt64(out ulong creationDate) ||
                !reader.TryReadUInt64(out ulong dataPacketsCount) ||
                !reader.TryReadUInt64(out ulong playDuration) ||
                !reader.TryReadUInt64(out ulong sendDuration) ||
                !reader.TryReadUInt64(out ulong preroll) ||
                !reader.TryReadUInt32(out uint flags) ||
                !reader.TryReadUInt32(out uint minimumDataPacketSize) ||
                !reader.TryReadUInt32(out uint maximumDataPacketSize) ||
                !reader.TryReadUInt32(out uint maximumBitrate))
            {
                dataLines.Add(("<Error>", "Failed to read the File Properties fields (72 bytes required)."));
                AddRemaining(reader, dataLines);
                return Build(parser, node, header, dataLines);
            }

            // File ID
            dataLines.Add(("FileID", ASFUtil.GuidDisplay(fileId)));
            dataLines.Add(("<FileID>", "Globally unique identifier for this file (also referenced by the Data Object)."));

            // File Size（只展示读到值本身，不与物理文件大小做对比）
            dataLines.Add(("FileSize", fileSize.ToString()));
            dataLines.Add(("<FileSize>", ASFUtil.FormatBytes((long)fileSize)));

            // Creation Date（FILETIME：自 1601-01-01 UTC 起 100ns 间隔）
            dataLines.Add(("CreationDate", creationDate.ToString()));
            dataLines.Add(("<CreationDate>", FormatFileTime(creationDate)));

            // Data Packets Count
            dataLines.Add(("DataPacketsCount", dataPacketsCount.ToString()));
            dataLines.Add(("<DataPacketsCount>", $"{dataPacketsCount:N0} packets"));

            // Play Duration / Send Duration（100ns 间隔）
            dataLines.Add(("PlayDuration", playDuration.ToString()));
            dataLines.Add(("<PlayDuration>", FormatHundredNanos(playDuration)));
            dataLines.Add(("SendDuration", sendDuration.ToString()));
            dataLines.Add(("<SendDuration>", FormatHundredNanos(sendDuration)));

            // Preroll（毫秒）
            dataLines.Add(("Preroll", preroll.ToString()));
            dataLines.Add(("<Preroll>", $"{preroll} ms"));

            // Flags：bit0 = Broadcast，bit1 = Seekable
            dataLines.Add(("Flags", $"0x{flags:X8}"));
            dataLines.Add(("<Flags>", DescribeFlags(flags)));

            // Packet 大小范围
            dataLines.Add(("MinimumDataPacketSize", minimumDataPacketSize.ToString()));
            dataLines.Add(("<MinimumDataPacketSize>", $"{minimumDataPacketSize} bytes"));
            dataLines.Add(("MaximumDataPacketSize", maximumDataPacketSize.ToString()));
            dataLines.Add(("<MaximumDataPacketSize>", $"{maximumDataPacketSize} bytes"));

            // Maximum Bitrate
            dataLines.Add(("MaximumBitrate", maximumBitrate.ToString()));
            dataLines.Add(("<MaximumBitrate>", FormatBitrate(maximumBitrate)));

            // 剩余未解析字节
            if (reader.Remaining is long remaining && remaining > 0)
            {
                dataLines.Add(("<PayloadLength>", ASFUtil.FormatBytes(remaining)));
                dataLines.Add(("<Note>", "Unexplained bytes after the File Properties fields; not parsed."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "Declared object size exceeds the available range; content was clamped."));

            return Build(parser, node, header, dataLines);
        }

        private static string FormatFileTime(ulong fileTime)
        {
            if (fileTime == 0) return "(not set)";
            if (fileTime > (ulong)DateTime.MaxValue.ToFileTimeUtc())
                return $"(out of DateTime range: {fileTime})";

            try
            {
                return DateTime.FromFileTimeUtc((long)fileTime).ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");
            }
            catch (ArgumentOutOfRangeException)
            {
                return $"(invalid FILETIME value: {fileTime})";
            }
        }

        private static string FormatHundredNanos(ulong value)
        {
            if (value == 0) return "0";
            if (value > (ulong)TimeSpan.MaxValue.Ticks)
                return $"{(value / 10000.0):0.##} ms (overflow)";

            var ts = TimeSpan.FromTicks((long)value);
            if (ts.TotalMilliseconds < 1000) return $"{ts.TotalMilliseconds:0.##} ms";
            if (ts.TotalSeconds < 60) return $"{ts.TotalSeconds:0.###} s";
            return ts.ToString(@"hh\:mm\:ss\.fff");
        }

        private static string DescribeFlags(uint flags)
        {
            var parts = new List<string>();
            if ((flags & 0x1) != 0) parts.Add("Broadcast");
            if ((flags & 0x2) != 0) parts.Add("Seekable");
            uint reserved = flags & ~0x3u;
            if (reserved != 0) parts.Add($"reserved bits 0x{reserved:X}");

            return parts.Count > 0 ? string.Join(", ", parts) : "(none set)";
        }

        private static string FormatBitrate(uint bitsPerSecond)
        {
            if (bitsPerSecond >= 1_000_000) return $"{bitsPerSecond / 1_000_000.0:0.###} Mbit/s";
            if (bitsPerSecond >= 1_000) return $"{bitsPerSecond / 1_000.0:0.##} kbit/s";
            return $"{bitsPerSecond} bit/s";
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
                Title = ASFUtil.MakeTitle("FileProperties", ASFUtil.GuidDisplay(header.Guid)),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ObjectStart, (long)node.Length),
            };
    }
}