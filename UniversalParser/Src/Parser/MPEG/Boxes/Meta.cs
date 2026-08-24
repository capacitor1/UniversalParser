using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Meta
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            // =========================
            // FullBox header
            // =========================
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

            // =========================
            // meta is container AFTER fullbox header
            // =========================
            long end = (long)node.Position + (long)node.Length;

            while (fs.Position + 8 <= end)
            {
                long childPos = fs.Position;

                uint size = reader.ReadUInt32BE();
                _ = reader.ReadFourCC();

                // fallback skip (you already have global dispatcher)
                fs.Position = childPos + size;
            }

            return new ParseResult
            {
                
                Title = $"MetaData '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}