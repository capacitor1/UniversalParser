using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ASF.Chunks
{
    /// <summary>
    /// Codec List Object（MS-ASF 2.3.7）。
    /// 结构：Reserved(GUID,16) + Codec Entries Count(DWORD,4) + Codec Entries[...]
    /// 每个 Codec Entry（2.3.7.1）：
    ///   Type(WORD,0=Video/1=Audio)
    ///   + Codec Name Length(WORD,字符数) + Codec Name(WCHAR×n)
    ///   + Codec Description Length(WORD,字符数) + Codec Description(WCHAR×n)
    ///   + Codec Information Length(WORD,字节数) + Codec Information(BYTE×n)
    /// 规范：原生字段名与数值保持官方定义；不可读项附加同名 &lt;&gt; 可读项。
    /// </summary>
    internal static class CodecListChunk
    {
        public static ParseResult Parse(ASFParser parser, Node node, ASFObjectHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            var reader = parser.CreatePayloadReader(header);

            // ---- Reserved（规范：必须是 Codec List Object 自身的 GUID）----
            if (!reader.TryReadGuid(out Guid reserved))
            {
                dataLines.Add(("<Error>", "Failed to read the Reserved GUID."));
                return Build(parser, node, header, dataLines);
            }

            dataLines.Add(("Reserved", ASFUtil.GuidDisplay(reserved)));
            dataLines.Add(("<Reserved>", "MUST be the Codec List Object GUID"));

            if (reserved != ASFUtil.CodecListObject)
                dataLines.Add(("<Warning>", "Reserved GUID does not match the Codec List Object GUID."));

            // ---- Codec Entries Count ----
            if (!reader.TryReadUInt32(out uint count))
            {
                dataLines.Add(("<Error>", "Failed to read Codec Entries Count."));
                return Build(parser, node, header, dataLines);
            }

            dataLines.Add(("CodecEntriesCount", count.ToString()));

            // ---- Codec Entries ----
            bool truncated = false;
            for (uint i = 0; i < count; i++)
            {
                // 每个 entry 至少 8 字节：Type(2)+NameLen(2)+DescLen(2)+InfoLen(2)
                if (reader.Remaining is < 8)
                {
                    dataLines.Add(("<Warning>", $"Codec entry {i}: fewer than 8 bytes remain, entry header is truncated."));
                    truncated = true;
                    break;
                }

                dataLines.Add(($"<CodecEntry[{i}]>", $"Codec entry #{i}"));

                // Type
                if (!reader.TryReadUInt16(out ushort type))
                {
                    truncated = true;
                    break;
                }
                dataLines.Add(("Type", $"0x{type:X4}"));
                dataLines.Add(("<Type>", DescribeType(type)));

                // Codec Name
                if (!reader.TryReadUInt16(out ushort nameLength))
                {
                    truncated = true;
                    break;
                }
                dataLines.Add(("CodecNameLength", nameLength.ToString()));
                dataLines.Add(("<CodecNameLength>", $"{nameLength} characters ({nameLength * 2} bytes UTF-16LE)"));

                if (!TryReadWide(reader, nameLength, out string name))
                {
                    dataLines.Add(("<Warning>", $"Codec entry {i}: Codec Name is truncated."));
                    truncated = true;
                    break;
                }
                dataLines.Add(("CodecName", ASFUtil.Sanitize(name)));

                // Codec Description
                if (!reader.TryReadUInt16(out ushort descriptionLength))
                {
                    truncated = true;
                    break;
                }
                dataLines.Add(("CodecDescriptionLength", descriptionLength.ToString()));
                dataLines.Add(("<CodecDescriptionLength>", $"{descriptionLength} characters ({descriptionLength * 2} bytes UTF-16LE)"));

                if (!TryReadWide(reader, descriptionLength, out string description))
                {
                    dataLines.Add(("<Warning>", $"Codec entry {i}: Codec Description is truncated."));
                    truncated = true;
                    break;
                }
                dataLines.Add(("CodecDescription", ASFUtil.Sanitize(description)));

                // Codec Information（opaque 字节数组）
                if (!reader.TryReadUInt16(out ushort informationLength))
                {
                    truncated = true;
                    break;
                }
                dataLines.Add(("CodecInformationLength", informationLength.ToString()));

                if (reader.Remaining is long infoRemaining && infoRemaining < informationLength)
                {
                    dataLines.Add(("<Warning>",
                        $"Codec entry {i}: Codec Information is truncated ({informationLength} declared, {infoRemaining} available)."));
                    truncated = true;
                    break;
                }

                // 内容 opaque，不 dump（GUI 有二进制预览）；Skip 推进位置
                reader.Skip(informationLength);
                dataLines.Add(("CodecInformation", $"(binary, {informationLength} bytes)"));
                dataLines.Add(("<CodecInformation>", "Opaque codec-specific byte array; see the raw data view."));
            }

            if (truncated)
            {
                dataLines.Add(("<Warning>", "The Codec List Object is truncated; remaining payload is not parsed."));
                if (reader.Remaining is long tail && tail > 0)
                    dataLines.Add(("<PayloadLength>", ASFUtil.FormatBytes(tail)));
            }
            else if (reader.Remaining is long extra && extra > 0)
            {
                // 声明的 entry 都解析完了但还有剩余：按规范用 PayloadLength 呈现未解析部分
                dataLines.Add(("<PayloadLength>", ASFUtil.FormatBytes(extra)));
                dataLines.Add(("<Note>", "Unexplained bytes remain after the last codec entry."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "Declared object size exceeds the available range; content was clamped."));

            return Build(parser, node, header, dataLines);
        }

        private static string DescribeType(ushort type) => type switch
        {
            0x0000 => "Video codec",
            0x0001 => "Audio codec",
            _ => $"Reserved type (0x{type:X4})",
        };

        /// <summary>按字符数读取 UTF-16LE 字符串；长度超出剩余载荷时返回 false（不抛异常）。</summary>
        private static bool TryReadWide(ASFReader reader, ushort characterCount, out string text)
        {
            text = string.Empty;

            long bytesNeeded = (long)characterCount * 2;
            if (reader.Remaining is long remaining && remaining < bytesNeeded)
                return false;
            if (characterCount == 0)
                return true;

            text = ASFUtil.DecodeWide(reader.ReadBytes((int)bytesNeeded));
            return true;
        }

        private static ParseResult Build(
            ASFParser parser,
            Node node,
            ASFObjectHeader header,
            List<(string K, string V)> dataLines) =>
            new()
            {
                Title = ASFUtil.MakeTitle("CodecList", ASFUtil.GuidDisplay(header.Guid)),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ObjectStart, (long)node.Length),
            };
    }
}