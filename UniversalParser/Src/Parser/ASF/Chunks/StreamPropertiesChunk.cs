using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ASF.Chunks
{
    /// <summary>
    /// Stream Properties Object（MS-ASF 2.3.5）。
    /// 固定 54 字节头：StreamType(16) + ErrorCorrectionType(16) + TimeOffset(8)
    /// + TypeSpecificDataLength(4) + ErrorCorrectionDataLength(4)
    /// + Flags(2) + Reserved(4)，随后是 Type-Specific Data 与 Error Correction Data。
    /// Type-Specific Data 按 StreamType 分为 Audio/Video/Command/JFIF 等格式分别解析；
    /// 其余未解析区域（codec 私有数据、Format Data、Error Correction Data）以 PayloadLength 呈现。
    /// </summary>
    internal static class StreamPropertiesChunk
    {
        private const int FixedHeaderSize = 54; // 16+16+8+4+4+2+4

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

        private static readonly Dictionary<Guid, string> ErrorCorrectionTypeNames = new()
        {
            [ASFUtil.NoErrorCorrection] = "No error correction",
            [ASFUtil.AudioSpread] = "Audio spread",
        };

        private static readonly Dictionary<ushort, string> AudioTagNames = new()
        {
            [0x0001] = "PCM",
            [0x0002] = "Microsoft ADPCM",
            [0x0003] = "IEEE float",
            [0x0006] = "ITU G.711 A-law",
            [0x0007] = "ITU G.711 mu-law",
            [0x0011] = "IMA ADPCM",
            [0x0031] = "GSM 6.10",
            [0x0040] = "ITU G.721 ADPCM",
            [0x0050] = "MPEG",
            [0x0055] = "MPEG Layer-3",
            [0x0092] = "Dolby AC-3 over S/PDIF",
            [0x00FF] = "Raw AAC",
            [0x0160] = "Windows Media Audio 2",
            [0x0161] = "Windows Media Audio 7/8/9",
            [0x0162] = "Windows Media Audio 9 Lossless",
            [0x0163] = "Windows Media Speech",
            [0x2000] = "Dolby AC-3",
            [0x2001] = "DTS",
            [0xFFFE] = "Extensible",
        };

        public static ParseResult Parse(ASFParser parser, Node node, ASFObjectHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            var reader = parser.CreatePayloadReader(header);

            if (!reader.TryReadGuid(out Guid streamType) ||
                !reader.TryReadGuid(out Guid errorCorrectionType) ||
                !reader.TryReadUInt64(out ulong timeOffset) ||
                !reader.TryReadUInt32(out uint typeSpecificDataLength) ||
                !reader.TryReadUInt32(out uint errorCorrectionDataLength) ||
                !reader.TryReadUInt16(out ushort flags) ||
                !reader.TryReadUInt32(out uint reserved))
            {
                dataLines.Add(("<Error>", "Failed to read the Stream Properties fields (54 bytes required)."));
                AddRemaining(reader, dataLines);
                return Build(parser, node, header, dataLines);
            }

            // ---- 固定字段 ----
            dataLines.Add(("StreamType", ASFUtil.GuidDisplay(streamType)));
            dataLines.Add(("<StreamType>", Describe(StreamTypeNames, streamType)));

            dataLines.Add(("ErrorCorrectionType", ASFUtil.GuidDisplay(errorCorrectionType)));
            dataLines.Add(("<ErrorCorrectionType>", Describe(ErrorCorrectionTypeNames, errorCorrectionType)));

            dataLines.Add(("TimeOffset", timeOffset.ToString()));
            dataLines.Add(("<TimeOffset>", FormatDuration(timeOffset)));

            dataLines.Add(("TypeSpecificDataLength", typeSpecificDataLength.ToString()));
            dataLines.Add(("ErrorCorrectionDataLength", errorCorrectionDataLength.ToString()));

            int streamNumber = flags & 0x7F;
            bool encrypted = (flags & 0x8000) != 0;
            dataLines.Add(("Flags", $"0x{flags:X4}"));
            dataLines.Add(("<Flags>", $"Stream #{streamNumber}" + (encrypted ? ", encrypted content" : string.Empty)));

            dataLines.Add(("Reserved", $"0x{reserved:X8}"));

            // ---- Type-Specific Data ----
            long available = reader.Remaining ?? 0;
            long tsdLength = Math.Min(typeSpecificDataLength, available);
            if (typeSpecificDataLength > available)
                dataLines.Add(("<Warning>", $"Type-Specific Data is truncated ({available} of {typeSpecificDataLength} bytes available)."));

            long tsdStart = reader.Position;
            long tsdEnd = tsdStart + tsdLength;

            ParseTypeSpecificData(reader, streamType, tsdEnd, dataLines);
            reader.Skip(Math.Max(0, tsdEnd - reader.Position));

            // ---- Error Correction Data（opaque，不解析）----
            if (errorCorrectionDataLength > 0)
            {
                available = reader.Remaining ?? 0;
                long ecLength = Math.Min(errorCorrectionDataLength, available);
                if (errorCorrectionDataLength > available)
                    dataLines.Add(("<Warning>",
                        $"Error Correction Data is truncated ({available} of {errorCorrectionDataLength} bytes available)."));

                if (ecLength > 0)
                {
                    dataLines.Add(("<ErrorCorrectionData>", "Codec-specific error correction data; not decoded."));
                    dataLines.Add(("<PayloadLength>", ASFUtil.FormatBytes(ecLength)));
                    reader.Skip(ecLength);
                }
            }

            // ---- 对象末尾多余字节 ----
            if (reader.Remaining is long trailing && trailing > 0)
            {
                // 若 ErrorCorrectionData 已覆盖，这里通常为 0；否则表示对象尾有未说明数据
                dataLines.Add(("<PayloadLength>", ASFUtil.FormatBytes(trailing)));
                dataLines.Add(("<Note>", "Unexplained bytes after the error correction data; not parsed."));
                reader.Skip(trailing);
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "Declared object size exceeds the available range; content was clamped."));

            return Build(parser, node, header, dataLines);
        }

        // ============================================================
        // Type-Specific Data 各流类型解析（MS-ASF 2.3.5.1 - 2.3.5.8）
        // ============================================================

        private static void ParseTypeSpecificData(
            ASFReader reader, Guid streamType, long end, List<(string K, string V)> lines)
        {
            if (streamType == ASFUtil.AudioMedia)
            {
                ParseAudioData(reader, end, lines);
                return;
            }

            if (streamType == ASFUtil.VideoMedia)
            {
                ParseVideoData(reader, end, lines);
                return;
            }

            if (streamType == ASFUtil.CommandMedia)
            {
                if (RegionRemaining(reader, end) < 16)
                {
                    lines.Add(("<Warning>", "Command media type-specific data is too short (16 bytes required)."));
                    AddRegionRemaining(reader, end, lines);
                    return;
                }

                Guid commandType = reader.ReadGuid();
                lines.Add(("CommandType", ASFUtil.GuidDisplay(commandType)));
                lines.Add(("<CommandType>", "Identifies the command type; see MS-ASF 2.3.5.3."));
                AddRegionRemaining(reader, end, lines);
                return;
            }

            if (streamType == ASFUtil.JFIFMedia)
            {
                if (RegionRemaining(reader, end) < 2)
                {
                    lines.Add(("<Warning>", "JFIF media type-specific data is too short (2 bytes required)."));
                    AddRegionRemaining(reader, end, lines);
                    return;
                }

                byte resolutionX = reader.ReadByte();
                byte resolutionY = reader.ReadByte();
                lines.Add(("ResolutionX", resolutionX.ToString()));
                lines.Add(("ResolutionY", resolutionY.ToString()));
                AddRegionRemaining(reader, end, lines);
                return;
            }

            // Degradable JPEG / File Transfer / Binary / Web Stream / 未知类型：整段未解析
            if (streamType == ASFUtil.BinaryMedia && RegionRemaining(reader, end) == 0)
            {
                lines.Add(("<Note>", "Binary media has no type-specific data."));
                return;
            }

            AddRegionRemaining(reader, end, lines);
        }

        private static void ParseAudioData(ASFReader reader, long end, List<(string K, string V)> lines)
        {
            long remaining = RegionRemaining(reader, end);
            if (remaining < 16)
            {
                lines.Add(("<Warning>", $"Audio type-specific data is too short ({remaining} bytes; at least 16 required)."));
                AddRegionRemaining(reader, end, lines);
                return;
            }

            ushort codecId = reader.ReadUInt16();
            ushort channels = reader.ReadUInt16();
            uint samplesPerSec = reader.ReadUInt32();
            uint avgBytesPerSec = reader.ReadUInt32();
            ushort blockAlign = reader.ReadUInt16();
            ushort bitsPerSample = reader.ReadUInt16();

            lines.Add(("CodecID", $"0x{codecId:X4}"));
            lines.Add(("<CodecID>", Describe(AudioTagNames, codecId)));
            lines.Add(("NumberOfChannels", channels.ToString()));
            lines.Add(("<NumberOfChannels>", $"{channels} channel(s)"));
            lines.Add(("SamplesPerSecond", samplesPerSec.ToString()));
            lines.Add(("<SamplesPerSecond>", $"{samplesPerSec:N0} Hz"));
            lines.Add(("AverageBytesPerSecond", avgBytesPerSec.ToString()));
            lines.Add(("<AverageBytesPerSecond>", FormatBitrate((long)avgBytesPerSec * 8)));
            lines.Add(("BlockAlignment", blockAlign.ToString()));
            lines.Add(("<BlockAlignment>", $"{blockAlign} bytes per block"));
            lines.Add(("BitsPerSample", bitsPerSample.ToString()));

            // Codec Specific Data（opaque）
            remaining = RegionRemaining(reader, end);
            if (remaining < 2)
            {
                AddRegionRemaining(reader, end, lines);
                return;
            }

            ushort cbSize = reader.ReadUInt16();
            lines.Add(("CodecSpecificDataSize", cbSize.ToString()));

            remaining = RegionRemaining(reader, end);
            long consumable = Math.Min(cbSize, remaining);
            if (cbSize > remaining)
                lines.Add(("<Warning>", $"Codec specific data is truncated ({remaining} of {cbSize} bytes available)."));

            if (consumable > 0)
            {
                lines.Add(("<PayloadLength>", ASFUtil.FormatBytes(consumable)));
                lines.Add(("<Note>", "Codec-specific data (opaque); see the raw data view."));
                reader.Skip(consumable);
            }

            AddRegionRemaining(reader, end, lines);
        }

        private static void ParseVideoData(ASFReader reader, long end, List<(string K, string V)> lines)
        {
            long remaining = RegionRemaining(reader, end);
            if (remaining < 11) // 4 + 4 + 1 + 2
            {
                lines.Add(("<Warning>", $"Video type-specific data is too short ({remaining} bytes; at least 11 required)."));
                AddRegionRemaining(reader, end, lines);
                return;
            }

            uint width = reader.ReadUInt32();
            uint height = reader.ReadUInt32();
            byte reservedFlags = reader.ReadByte();
            ushort formatDataSize = reader.ReadUInt16();

            lines.Add(("EncodedImageWidth", width.ToString()));
            lines.Add(("<EncodedImageWidth>", $"{width} px"));
            lines.Add(("EncodedImageHeight", height.ToString()));
            lines.Add(("<EncodedImageHeight>", $"{height} px"));
            lines.Add(("ReservedFlags", $"0x{reservedFlags:X2}"));
            lines.Add(("<ReservedFlags>", "Frame property flags; see MS-ASF 2.3.5.2."));
            lines.Add(("FormatDataSize", formatDataSize.ToString()));

            remaining = RegionRemaining(reader, end);
            long consumable = Math.Min(formatDataSize, remaining);
            if (formatDataSize > remaining)
                lines.Add(("<Warning>", $"Format data is truncated ({remaining} of {formatDataSize} bytes available)."));

            if (consumable > 0)
            {
                lines.Add(("<PayloadLength>", ASFUtil.FormatBytes(consumable)));
                lines.Add(("<Note>", "Format data (BITMAPINFOHEADER or codec private); not decoded."));
                reader.Skip(consumable);
            }

            AddRegionRemaining(reader, end, lines);
        }

        // ============================================================
        // 工具
        // ============================================================

        private static long RegionRemaining(ASFReader reader, long end) =>
            Math.Max(0, end - reader.Position);

        private static void AddRegionRemaining(ASFReader reader, long end, List<(string K, string V)> lines)
        {
            long remaining = RegionRemaining(reader, end);
            if (remaining <= 0) return;
            lines.Add(("<PayloadLength>", ASFUtil.FormatBytes(remaining)));
            lines.Add(("<Note>", "Unparsed bytes in this region."));
        }

        private static string Describe(Dictionary<Guid, string> map, Guid guid) =>
            map.TryGetValue(guid, out string? name) ? name : "Unknown";

        private static string Describe(Dictionary<ushort, string> map, ushort tag) =>
            map.TryGetValue(tag, out string? name) ? name : $"Unknown tag (0x{tag:X4})";

        private static string FormatDuration(ulong hundredNanos)
        {
            if (hundredNanos == 0) return "0";
            long ticks = (long)Math.Min(hundredNanos, (ulong)TimeSpan.MaxValue.Ticks);
            var ts = TimeSpan.FromTicks(ticks);
            if (ts.TotalMilliseconds < 1000) return $"{ts.TotalMilliseconds:0.##} ms";
            if (ts.TotalSeconds < 60) return $"{ts.TotalSeconds:0.###} s";
            return ts.ToString(@"hh\:mm\:ss\.fff");
        }

        private static string FormatBitrate(long bitsPerSecond)
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
                Title = ASFUtil.MakeTitle("StreamProperties", ASFUtil.GuidDisplay(header.Guid)),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ObjectStart, (long)node.Length),
            };
    }
}