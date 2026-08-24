using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Stts
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
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

            uint entryCount = reader.ReadUInt32BE();
            dataLines.Add(("entry_count", entryCount.ToString()));

            if (entryCount > 0)
            {
                uint sampleCount = reader.ReadUInt32BE();
                uint sampleDelta = reader.ReadUInt32BE();
                dataLines.Add(($"(sample_count,sample_delta)[{entryCount}]", $"{sampleCount},{sampleDelta}"));
                for (int i = 1; i < entryCount; i++)
                {
                    sampleCount = reader.ReadUInt32BE();
                    sampleDelta = reader.ReadUInt32BE();

                    dataLines.Add((string.Empty, $"{sampleCount},{sampleDelta}"));
                }
            }


            return new ParseResult
            {
                
                Title = $"TimeToSample '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}