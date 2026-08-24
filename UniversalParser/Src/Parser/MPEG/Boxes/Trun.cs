namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Trun
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            string t = reader.ReadFourCC();

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | (f3 << 0));

            uint sampleCount = reader.ReadUInt32BE();

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}"),
                ("sample_count", sampleCount.ToString())
            };

            if ((flags & 0x000001) != 0)
                dataLines.Add(("data_offset", reader.ReadInt32BE().ToString()));

            if ((flags & 0x000004) != 0)
                dataLines.Add(("first_sample_flags", $"0x{reader.ReadUInt32BE():X8}"));

            bool hasDuration = (flags & 0x000100) != 0;
            bool hasSize = (flags & 0x000200) != 0;
            bool hasFlags = (flags & 0x000400) != 0;
            bool hasCto = (flags & 0x000800) != 0;

            if (sampleCount > 0)
            {
                string f = "(";
                string v = string.Empty;

                //firstline
                if (hasDuration)
                {
                    f += "duration,";
                    v += $"{reader.ReadUInt32BE()},";
                }
                if (hasSize)
                {
                    f += "size,";
                    v += $"{reader.ReadUInt32BE()},";
                }
                if (hasFlags)
                {
                    f += "flags,";
                    v += $"{reader.ReadUInt32BE()},";
                }
                if (hasCto)
                {
                    int cto = version == 1
                            ? reader.ReadInt32BE()
                            : (int)reader.ReadUInt32BE();
                    f += "composition_time_offset,";
                    v += $"{cto},";
                }
                f = f.TrimEnd(',');
                v = v.TrimEnd(',');
                f += $")[{sampleCount}]";
                dataLines.Add((f, v));
                //remain
                for (int i = 1; i < sampleCount; i++)
                {
                    v = string.Empty;
                    if (hasDuration)
                        v += $"{reader.ReadUInt32BE()},";

                    if (hasSize)
                        v += $"{reader.ReadUInt32BE()},";

                    if (hasFlags)
                        v += $"{reader.ReadUInt32BE()},";

                    if (hasCto)
                    {
                        int cto = version == 1
                            ? reader.ReadInt32BE()
                            : (int)reader.ReadUInt32BE();

                        v += $"{cto},";
                    }
                    v = v.TrimEnd(',');
                    dataLines.Add((string.Empty, v));
                }
                //
            }

            return new ParseResult
            {
                
                Title = $"TrackRun '{t}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}