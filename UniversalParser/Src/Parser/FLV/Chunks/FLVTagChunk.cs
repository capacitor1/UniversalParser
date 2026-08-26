using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.FLV.Chunks
{
    /// <summary>
    /// 解析所有 FLV Tag 的统一 11 字节 Tag Header，以及 Tag 后方的 PreviousTagSize。
    /// Tag Data 本身由唯一的子节点负责解析。
    /// </summary>
    internal static class FLVTagChunk
    {
        public static ParseResult Parse(
            FLVParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            if (!parser.TryGetTagHeader(node, out FLVTagHeader header))
                return FLVDefaultChunk.Parse(parser, node);

            string readableName = header.TagType switch
            {
                8 => "AudioTag",
                9 => "VideoTag",
                18 => "ScriptDataTag",
                _ => "UnknownTag"
            };

            var dataLines = new List<(string K, string V)>
            {
                // 名称与 FLV Tag Header 语法定义保持一致
                ("Reserved", header.Reserved.ToString()),
                ("Filter", header.Filter ? "1" : "0"),
                ("<Filter>", header.Filter ? "Pre-processing is required" : "No pre-processing"),
                ("TagType", header.TagType.ToString()),
                ("<TagType>", FLVUtil.DescribeTagType(header.TagType)),
                ("DataSize", header.DataSize.ToString()),
                ("Timestamp", header.Timestamp.ToString()),
                ("TimestampExtended", header.TimestampExtended.ToString()),
                ("<Timestamp>", FLVUtil.FormatTimestamp(header.CompleteTimestamp)),
                ("StreamID", header.StreamID.ToString())
            };

            if (header.HasPreviousTagSize)
            {
                dataLines.Add((
                    "PreviousTagSize",
                    header.PreviousTagSize.ToString()));

                if (header.PreviousTagSize != header.ExpectedPreviousTagSize)
                {
                    dataLines.Add((
                        "<Warning>",
                        $"PreviousTagSize should be {header.ExpectedPreviousTagSize}, " +
                        $"found {header.PreviousTagSize}."));
                }
            }
            else
            {
                dataLines.Add((
                    "<Warning>",
                    "PreviousTagSize is missing or truncated."));
            }

            if (header.Reserved != 0)
            {
                dataLines.Add((
                    "<Warning>",
                    "Reserved bits are non-zero."));
            }

            if (header.StreamID != 0)
            {
                dataLines.Add((
                    "<Warning>",
                    $"StreamID should be 0, found {header.StreamID}."));
            }

            if (header.IsTruncated)
            {
                dataLines.Add((
                    "<Warning>",
                    $"Tag data is truncated: {header.DataSize} bytes declared, " +
                    $"{header.ActualDataSize} bytes available."));
            }

            return new ParseResult
            {
                Title = FLVUtil.MakeTitle(readableName, node.NodeName),
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