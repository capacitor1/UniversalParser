using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Hdlr
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE(); // size
            string type = reader.ReadFourCC();

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{(f1<<16|f2<<8|f3):X6}")
            };

            uint preDefined = reader.ReadUInt32BE();
            string handlerType = reader.ReadFourCC();

            dataLines.Add(("pre_defined", preDefined.ToString()));
            dataLines.Add(("<handler_type>", handlerType));

            uint r1 = reader.ReadUInt32BE();
            uint r2 = reader.ReadUInt32BE();
            uint r3 = reader.ReadUInt32BE();

            dataLines.Add(("reserved[3]", r1.ToString()));
            dataLines.Add(("", r2.ToString()));
            dataLines.Add(("", r3.ToString()));

            // name (null terminated string)
            var sb = new StringBuilder();
            int b;
            while ((b = reader.ReadByte()) != 0)
            {
                if (b < 0) break;
                sb.Append((char)b);
            }

            string name = sb.ToString();
            dataLines.Add(("<name>", name));

            return new ParseResult
            {
                
                Title = $"Handler '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}