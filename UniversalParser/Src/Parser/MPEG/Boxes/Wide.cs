using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Wide
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>
        {
            ("<payload_length>", (node.Length - 8).ToString())
        };

            return new ParseResult
            {
                
                Title = $"Wide '{node.NodeName}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(parser.FileStream, (long)node.Position, (long)node.Length)
            };
        }
    }
}