using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ASF.Chunks
{
    /// <summary>
    /// Content Description Object（MS-ASF 2.3.8）。
    /// 五个长度字段（WORD，**单位：字节**）+ 五个 UTF-16LE 字符串。
    /// 长度为 0 表示对应字段不存在；所有存在的字段都会被完全解析。
    /// 注意：长度字段是字节数，不是字符数——多读一倍的字节会导致整个对象错位。
    /// </summary>
    internal static class ContentDescriptionChunk
    {
        public static ParseResult Parse(ASFParser parser, Node node, ASFObjectHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            var reader = parser.CreatePayloadReader(header);

            // 五个长度字段，共 10 字节
            if (!reader.TryReadUInt16(out ushort titleLength) ||
                !reader.TryReadUInt16(out ushort authorLength) ||
                !reader.TryReadUInt16(out ushort copyrightLength) ||
                !reader.TryReadUInt16(out ushort descriptionLength) ||
                !reader.TryReadUInt16(out ushort ratingLength))
            {
                dataLines.Add(("<Error>", "Failed to read the five length fields (10 bytes required)."));
                AddRemaining(reader, dataLines);
                return Build(parser, node, header, dataLines);
            }

            dataLines.Add(("TitleLength", titleLength.ToString()));
            dataLines.Add(("<TitleLength>", $"{titleLength} bytes (UTF-16LE)"));
            dataLines.Add(("AuthorLength", authorLength.ToString()));
            dataLines.Add(("<AuthorLength>", $"{authorLength} bytes (UTF-16LE)"));
            dataLines.Add(("CopyrightLength", copyrightLength.ToString()));
            dataLines.Add(("<CopyrightLength>", $"{copyrightLength} bytes (UTF-16LE)"));
            dataLines.Add(("DescriptionLength", descriptionLength.ToString()));
            dataLines.Add(("<DescriptionLength>", $"{descriptionLength} bytes (UTF-16LE)"));
            dataLines.Add(("RatingLength", ratingLength.ToString()));
            dataLines.Add(("<RatingLength>", $"{ratingLength} bytes (UTF-16LE)"));

            // 按规范顺序读取五个字符串（按字节单位直接读取）
            ReadStringField(reader, "Title", titleLength, dataLines);
            ReadStringField(reader, "Author", authorLength, dataLines);
            ReadStringField(reader, "Copyright", copyrightLength, dataLines);
            ReadStringField(reader, "Description", descriptionLength, dataLines);
            ReadStringField(reader, "Rating", ratingLength, dataLines);

            // 五个字段之外的多余字节
            if (reader.Remaining is long remaining && remaining > 0)
            {
                dataLines.Add(("<PayloadLength>", ASFUtil.FormatBytes(remaining)));
                dataLines.Add(("<Note>", "Unexplained bytes after the last string field; not parsed."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "Declared object size exceeds the available range; content was clamped."));

            return Build(parser, node, header, dataLines);
        }

        private static void ReadStringField(
            ASFReader reader, string fieldName, ushort byteLength, List<(string K, string V)> lines)
        {
            if (byteLength == 0)
            {
                lines.Add(($"<{fieldName}>", "(not present)"));
                return;
            }

            if (reader.Remaining is long remaining && remaining < byteLength)
            {
                lines.Add(($"<{fieldName}>", $"(truncated: {remaining} of {byteLength} bytes available)"));
                lines.Add(("<Warning>", $"{fieldName} string is truncated."));
                reader.Skip(remaining);
                return;
            }

            string text = ASFUtil.DecodeWide(reader.ReadBytes(byteLength));
            lines.Add((fieldName, ASFUtil.Sanitize(text)));
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
                Title = ASFUtil.MakeTitle("ContentDescription", ASFUtil.GuidDisplay(header.Guid)),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ObjectStart, (long)node.Length),
            };
    }
}