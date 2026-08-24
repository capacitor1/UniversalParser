using System;
using System.Collections.Generic;
using System.IO;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Vmhd
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE(); // size ignored
            string type = reader.ReadFourCC();

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}")
            };

            ushort graphicsMode = reader.ReadUInt16BE();
            ushort opcolorR = reader.ReadUInt16BE();
            ushort opcolorG = reader.ReadUInt16BE();
            ushort opcolorB = reader.ReadUInt16BE();

            dataLines.Add(("graphics_mode", graphicsMode.ToString()));

            dataLines.Add(("opcolor[3]", opcolorR.ToString()));
            dataLines.Add(("", opcolorG.ToString()));
            dataLines.Add(("", opcolorB.ToString()));
            dataLines.Add(("<opcolor>", $"R = {opcolorR}, G = {opcolorG}, B = {opcolorB}"));

            return new ParseResult
            {
                
                Title = $"VideoMediaHeader '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}