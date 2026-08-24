using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// RIFF / RIFX / RF64 / BW64 / LIST 容器。
    /// 规范：容器只呈现关于自身的最基本信息，不列子块、不列任何长度。
    /// 容器的数据全部由子节点承载，因此不出现 &lt;PayloadLength&gt;。
    /// </summary>
    internal static class ContainerChunk
    {
        /// <summary>已知 listType 对应的可读名（无空格）。</summary>
        private static readonly Dictionary<string, string> ListNames = new(StringComparer.Ordinal)
        {
            ["INFO"] = "InfoList",
            ["adtl"] = "AssociatedDataList",
            ["wavl"] = "WaveDataList",
            ["hdrl"] = "HeaderList",
            ["strl"] = "StreamList",
            ["movi"] = "MovieDataList",
            ["odml"] = "OpenDmlList",
            ["rec "] = "RecordList",
            ["Tdat"] = "TimeDataList",   // Adobe CS3 Bridge 写入的时间码 / 卷名列表，无公开规范
            ["fram"] = "FrameList",
        };

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            bool isRootSignature = RIFFUtil.RootSignatures.Contains(header.Id);
            var dataLines = new List<(string K, string V)>();

            if (header.TypeCode is null)
            {
                dataLines.Add(("<Warning>", "Container type code is missing or unreadable."));
                return Build(parser, node, header, isRootSignature ? "RiffContainer" : "List", dataLines);
            }

            string typeCode = RIFFUtil.Sanitize(header.TypeCode);
            string readableName;

            if (isRootSignature)
            {
                // RIFF 规范中该字段名为 formType
                readableName = "RiffContainer";
                dataLines.Add(("formType", typeCode));
                dataLines.Add(("<ByteOrder>", parser.IsBigEndian ? "Big endian" : "Little endian"));

                if (header.ChunkStart == 0)
                {
                    if (header.DeclaredSize == uint.MaxValue)
                    {
                        dataLines.Add(("<Note>",
                            "ckSize is 0xFFFFFFFF (RF64/BW64 placeholder); the actual size is stored in the 'ds64' chunk."));
                    }
                    else if ((long)header.DeclaredSize + RIFFUtil.ChunkHeaderSize > parser.FileStream.Length)
                    {
                        dataLines.Add(("<Warning>",
                            "Declared ckSize exceeds the physical file size; the file is truncated."));
                    }
                }
            }
            else
            {
                // LIST 规范中该字段名为 listType
                readableName = ListNames.TryGetValue(header.TypeCode, out string? name) ? name : "List";
                dataLines.Add(("listType", typeCode));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "Declared size exceeds the available range; child chunks were clamped."));

            return Build(parser, node, header, readableName, dataLines);
        }

        private static ParseResult Build(
            RIFFParser parser,
            Node node,
            RIFFChunkHeader header,
            string readableName,
            List<(string K, string V)> dataLines) =>
            new()
            {
                Title = RIFFUtil.MakeTitle(readableName, header.Id),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ChunkStart, (long)node.Length),
            };
    }
}