using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Stsz
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

            uint sampleSize = reader.ReadUInt32BE();
            uint sampleCount = reader.ReadUInt32BE();

            dataLines.Add(("sample_size", sampleSize.ToString()));
            dataLines.Add(("sample_count", sampleCount.ToString()));

            if (sampleSize == 0 && sampleCount > 0)
            {
                uint size = reader.ReadUInt32BE();
                dataLines.Add(($"sample[{sampleCount}]", size.ToString()));
                for (int i = 1; i < sampleCount; i++)
                {
                    size = reader.ReadUInt32BE();
                    dataLines.Add((string.Empty, size.ToString()));
                }
            }

            return new ParseResult
            {
                
                Title = $"SampleSize '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}
