using System;

namespace UniversalParser.Src.Parser.FLV.Chunks
{
    /// <summary>
    /// FLV 理论上只有已定义节点类型，但仍需处理未知 TagType、实验性数据和合成损坏节点。
    /// </summary>
    internal static class FLVDefaultChunk
    {
        public static ParseResult Parse(
            FLVParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string readableName = node.NodeName switch
            {
                "UnknownTag" => "UnknownTag",
                "UnknownTagData" => "UnknownTagData",
                _ when node.NodeName.StartsWith('<') => "UnparsedData",
                _ => "Unknown"
            };

            return new ParseResult
            {
                Title = FLVUtil.MakeTitle(
                    readableName,
                    node.NodeName),

                Position = node.Position,
                Length = node.Length,

                DataLines =
                [
                    ("<PayloadLength>", FLVUtil.FormatBytes((long)node.Length))
                ],

                RawData = parser.CreateRawStream(
                    (long)node.Position,
                    (long)node.Length)
            };
        }
    }
}