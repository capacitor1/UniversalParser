using System;
using System.Collections.Generic;
using System.IO;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Elst
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

            uint entryCount = reader.ReadUInt32BE();
            dataLines.Add(("entry_count", entryCount.ToString()));

            if(entryCount > 0)
            {
                ulong segmentDuration = version == 0
                        ? reader.ReadUInt32BE()
                        : reader.ReadUInt64BE();

                long mediaTime = version == 0
                    ? (int)reader.ReadUInt32BE()
                    : (long)reader.ReadUInt64BE();

                ushort rateInt = reader.ReadUInt16BE();
                ushort rateFrac = reader.ReadUInt16BE();
                dataLines.Add(($"(segment_duration,media_time,media_rate)[{entryCount}]", $"{segmentDuration},{mediaTime},{rateInt}.{rateFrac}"));
                for (int i = 1; i < entryCount; i++)
                {
                    segmentDuration = version == 0
                        ? reader.ReadUInt32BE()
                        : reader.ReadUInt64BE();

                    mediaTime = version == 0
                        ? (int)reader.ReadUInt32BE()
                        : (long)reader.ReadUInt64BE();

                    rateInt = reader.ReadUInt16BE();
                    rateFrac = reader.ReadUInt16BE();
                    dataLines.Add((string.Empty, $"{segmentDuration},{mediaTime},{rateInt}.{rateFrac}"));
                }
            }
            return new ParseResult
            {
                
                Title = $"EditList '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}