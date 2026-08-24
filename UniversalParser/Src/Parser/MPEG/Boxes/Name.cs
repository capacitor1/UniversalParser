using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Name
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            long remaining = (long)node.Length - 8;

            byte[] buffer = new byte[remaining];
            fs.ReadExactly(buffer);

            string text = Encoding.UTF8.GetString(buffer).Trim('\0');

            var dataLines = new List<(string K, string V)>
            {
                ("name", text)
            };

            return new ParseResult
            {
                
                Title = $"Name '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}