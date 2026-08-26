using System;

namespace UniversalParser.Src.Parser.RawData
{
    /// <summary>
    /// Raw Data 解析器唯一的块解析实现。
    /// </summary>
    internal static class Data
    {
        public static ParseResult Parse(RawDataParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            return new ParseResult
            {
                Title = "RawData 'Data'",
                Position = node.Position,
                Length = node.Length,
                DataLines =
                [
                    ("<Note>", "File type detection failed; the entire file is displayed as raw data."),
                ],
                RawData = new OffsetStream(
                    parser.FileStream,
                    (long)node.Position,
                    (long)node.Length),
            };
        }
    }
}