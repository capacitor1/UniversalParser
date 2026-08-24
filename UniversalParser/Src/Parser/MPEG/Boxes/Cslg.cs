namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Cslg
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;
            long end = start + (long)node.Length;

            reader.ReadUInt32BE();
            reader.ReadFourCC(); // cslg

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

            bool isV0 = version == 0;

            int fieldSize = isV0 ? 4 : 8;

            // ===== safe lambda reader =====
            long ReadSigned()
            {
                if (fs.Position + fieldSize > end)
                    return 0;

                return isV0
                    ? reader.ReadInt32BE()
                    : reader.ReadInt64BE();
            }

            long compositionToDTSShift = ReadSigned();
            long leastDecodeToDisplayDelta = ReadSigned();
            long greatestDecodeToDisplayDelta = ReadSigned();
            long compositionStartTime = ReadSigned();
            long compositionEndTime = ReadSigned();

            dataLines.Add(("compositionTo_dts_shift", compositionToDTSShift.ToString()));
            dataLines.Add(("least_decode_to_display_delta", leastDecodeToDisplayDelta.ToString()));
            dataLines.Add(("greatest_decode_to_display_delta", greatestDecodeToDisplayDelta.ToString()));
            dataLines.Add(("composition_start_time", compositionStartTime.ToString()));
            dataLines.Add(("composition_end_time", compositionEndTime.ToString()));

            return new ParseResult
            {
                
                Title = "CompositionToDecodeBox 'cslg'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }
    }
}