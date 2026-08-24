using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class Default
    {
        public static ParseResult Parse(JPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string chunkType = node.NodeName;
            ulong length = node.Length;

            var dataLines = new List<(string K, string V)>
            {
                ("<PayloadLength>", (length - 2).ToString())
            };

            return new ParseResult
            {
                Title = $"Unknown '{chunkType}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(
                    parser.FileStream,
                    (long)node.Position,
                    (long)node.Length
                )
            };
        }
    }
}