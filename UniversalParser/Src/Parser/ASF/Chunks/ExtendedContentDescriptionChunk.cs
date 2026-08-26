using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ASF.Chunks
{
    /// <summary>
    /// Extended Content Description Object（MS-ASF 2.3.10）。
    /// 结构：Descriptor Count(WORD) + Descriptor×N。
    /// 每个 Descriptor（2.3.10.1）：
    ///   Descriptor Name Length(WORD, **字节数**) + Descriptor Name(UTF-16LE, 按字节读取)
    ///   + Descriptor Value Data Type(WORD) + Descriptor Value Length(WORD, **字节数**)
    ///   + Descriptor Value(BYTE×n)
    /// 注意：Name Length 单位是字节（与 Codec List 的字符单位不同，勿混淆）；
    ///       Value Length 单位也是字节。
    /// </summary>
    internal static class ExtendedContentDescriptionChunk
    {
        private const ushort TypeUnicode = 0x0000;
        private const ushort TypeByteArray = 0x0001;
        private const ushort TypeBool = 0x0002;   // DWORD：0 或 1
        private const ushort TypeDword = 0x0003;
        private const ushort TypeQword = 0x0004;
        private const ushort TypeWord = 0x0005;

        public static ParseResult Parse(ASFParser parser, Node node, ASFObjectHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            var reader = parser.CreatePayloadReader(header);

            if (!reader.TryReadUInt16(out ushort descriptorCount))
            {
                dataLines.Add(("<Error>", "Failed to read the Descriptor Count field."));
                AddRemaining(reader, dataLines);
                return Build(parser, node, header, dataLines);
            }

            dataLines.Add(("DescriptorCount", descriptorCount.ToString()));

            bool truncated = false;
            for (int i = 0; i < descriptorCount; i++)
            {
                if (reader.Remaining is < 2)
                {
                    dataLines.Add(("<Warning>", $"Descriptor {i}: fewer than 2 bytes remain for the name length."));
                    truncated = true;
                    break;
                }

                if (!reader.TryReadUInt16(out ushort nameLength))
                {
                    truncated = true;
                    break;
                }

                // MS-ASF 2.3.10.1：Descriptor Name Length 的单位是【字节】，不是字符
                if ((nameLength & 1) != 0)
                    dataLines.Add(("<Warning>",
                        $"Descriptor {i}: name length is odd ({nameLength} bytes); UTF-16LE strings should have even byte length."));

                if (reader.Remaining is long remName && remName < nameLength)
                {
                    dataLines.Add(("<Warning>", $"Descriptor {i}: name is truncated."));
                    truncated = true;
                    break;
                }

                string name = nameLength == 0
                    ? string.Empty
                    : ASFUtil.DecodeWide(reader.ReadBytes(nameLength));

                if (!reader.TryReadUInt16(out ushort valueType) ||
                    !reader.TryReadUInt16(out ushort valueLength))
                {
                    dataLines.Add(("<Warning>", $"Descriptor {i}: failed to read the value type/length."));
                    truncated = true;
                    break;
                }

                dataLines.Add(($"<Descriptor[{i}]>", $"Attribute \"{ASFUtil.Sanitize(name)}\""));

                dataLines.Add(("DescriptorNameLength", nameLength.ToString()));
                dataLines.Add(("<DescriptorNameLength>", $"{nameLength} bytes (UTF-16LE)"));
                dataLines.Add(("DescriptorName", ASFUtil.Sanitize(name)));
                dataLines.Add(("DescriptorValueDataType", $"0x{valueType:X4}"));
                dataLines.Add(("<DescriptorValueDataType>", DescribeValueType(valueType)));
                dataLines.Add(("DescriptorValueLength", valueLength.ToString()));

                if (reader.Remaining is long remVal && remVal < valueLength)
                {
                    dataLines.Add(("<Warning>", $"Descriptor {i}: value is truncated."));
                    truncated = true;
                    break;
                }

                if (!ReadValue(reader, valueType, valueLength, dataLines))
                {
                    truncated = true;
                    break;
                }
            }

            if (truncated)
            {
                dataLines.Add(("<Warning>", "The Extended Content Description Object is truncated."));
                AddRemaining(reader, dataLines);
            }
            else if (reader.Remaining is long extra && extra > 0)
            {
                dataLines.Add(("<PayloadLength>", ASFUtil.FormatBytes(extra)));
                dataLines.Add(("<Note>", "Unexplained bytes after the last descriptor; not parsed."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "Declared object size exceeds the available range; content was clamped."));

            return Build(parser, node, header, dataLines);
        }

        private static bool ReadValue(ASFReader reader, ushort type, ushort length, List<(string K, string V)> lines)
        {
            switch (type)
            {
                case TypeUnicode:
                {
                    if ((length & 1) != 0)
                        lines.Add(("<Warning>", "Unicode string value has an odd byte length."));

                    string text = length == 0 ? string.Empty : ASFUtil.DecodeWide(reader.ReadBytes(length));
                    lines.Add(("DescriptorValue", ASFUtil.Sanitize(text)));
                    return true;
                }

                case TypeByteArray:
                {
                    if (length == 0)
                    {
                        lines.Add(("<DescriptorValue>", "BYTE array (empty)."));
                        return true;
                    }

                    reader.Skip(length);
                    lines.Add(("<PayloadLength>", ASFUtil.FormatBytes(length)));
                    lines.Add(("<DescriptorValue>", "BYTE array; not decoded (see the raw data view)."));
                    return true;
                }

                case TypeBool:
                {
                    if (length < 4)
                    {
                        lines.Add(("<Warning>", "BOOL value length is smaller than 4 bytes."));
                        reader.Skip(length);
                        return true;
                    }
                    if (length != 4)
                        lines.Add(("<Warning>", "BOOL value length should be 4 bytes."));

                    uint raw = reader.ReadUInt32();
                    lines.Add(("DescriptorValue", raw.ToString()));
                    lines.Add(("<DescriptorValue>", raw != 0 ? "True" : "False"));
                    reader.Skip(length - 4);
                    return true;
                }

                case TypeDword:
                {
                    if (length < 4)
                    {
                        lines.Add(("<Warning>", "DWORD value length is smaller than 4 bytes."));
                        reader.Skip(length);
                        return true;
                    }
                    if (length != 4)
                        lines.Add(("<Warning>", "DWORD value length should be 4 bytes."));

                    uint raw = reader.ReadUInt32();
                    lines.Add(("DescriptorValue", raw.ToString()));
                    lines.Add(("<DescriptorValue>", $"0x{raw:X8}"));
                    reader.Skip(length - 4);
                    return true;
                }

                case TypeQword:
                {
                    if (length < 8)
                    {
                        lines.Add(("<Warning>", "QWORD value length is smaller than 8 bytes."));
                        reader.Skip(length);
                        return true;
                    }
                    if (length != 8)
                        lines.Add(("<Warning>", "QWORD value length should be 8 bytes."));

                    ulong raw = reader.ReadUInt64();
                    lines.Add(("DescriptorValue", raw.ToString()));
                    lines.Add(("<DescriptorValue>", $"0x{raw:X16}"));
                    reader.Skip(length - 8);
                    return true;
                }

                case TypeWord:
                {
                    if (length < 2)
                    {
                        lines.Add(("<Warning>", "WORD value length is smaller than 2 bytes."));
                        reader.Skip(length);
                        return true;
                    }
                    if (length != 2)
                        lines.Add(("<Warning>", "WORD value length should be 2 bytes."));

                    ushort raw = reader.ReadUInt16();
                    lines.Add(("DescriptorValue", raw.ToString()));
                    lines.Add(("<DescriptorValue>", $"0x{raw:X4}"));
                    reader.Skip(length - 2);
                    return true;
                }

                default:
                {
                    if (length == 0)
                    {
                        lines.Add(("<DescriptorValue>", $"Unknown type 0x{type:X4} with zero length."));
                        return true;
                    }

                    reader.Skip(length);
                    lines.Add(("<PayloadLength>", ASFUtil.FormatBytes(length)));
                    lines.Add(("<DescriptorValue>", $"Unknown value type 0x{type:X4}; raw bytes not decoded."));
                    return true;
                }
            }
        }

        private static string DescribeValueType(ushort type) => type switch
        {
            TypeUnicode => "Unicode string (UTF-16LE)",
            TypeByteArray => "BYTE array",
            TypeBool => "BOOL (DWORD, 0 or 1)",
            TypeDword => "DWORD (4 bytes)",
            TypeQword => "QWORD (8 bytes)",
            TypeWord => "WORD (2 bytes)",
            _ => $"Unknown type (0x{type:X4})",
        };

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
                Title = ASFUtil.MakeTitle("ExtendedContentDescription", ASFUtil.GuidDisplay(header.Guid)),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ObjectStart, (long)node.Length),
            };
    }
}