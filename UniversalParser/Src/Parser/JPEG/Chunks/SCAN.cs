using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class SCAN
    {
        public static ParseResult Parse(JPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);
            ulong length = node.Length;

            var dataLines = new List<(string K, string V)>
            {
                ("<PayloadLength>", length.ToString())
            };

            return new ParseResult
            {
                Title = $"(EntropyCodedData) 'SCAN'",
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
