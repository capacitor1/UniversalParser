namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Av1C
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;
            long end = start + (long)node.Length;

            reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            byte markerAndVersion = reader.ReadByte();

            byte marker = (byte)(markerAndVersion >> 7);
            byte version = (byte)(markerAndVersion & 0x7F);

            byte seqProfileAndLevel = reader.ReadByte();
            byte seqProfile = (byte)(seqProfileAndLevel >> 5);
            byte seqLevelIdx0 = (byte)(seqProfileAndLevel & 0x1F);

            byte byte3 = reader.ReadByte();
            byte byte4 = reader.ReadByte();

            byte seqTier0 = (byte)(byte3 >> 7);
            byte highBitdepth = (byte)((byte3 >> 6) & 0x01);
            byte twelveBit = (byte)((byte3 >> 5) & 0x01);
            byte monochrome = (byte)((byte3 >> 4) & 0x01);
            byte chromaSubX = (byte)((byte3 >> 3) & 0x01);
            byte chromaSubY = (byte)((byte3 >> 2) & 0x01);
            byte chromaSamplePosition = (byte)(byte3 & 0x03);

            byte reserved = (byte)(byte4 >> 5);
            byte initialPresentationDelayPresent = (byte)((byte4 >> 4) & 0x01);

            var dataLines = new List<(string K, string V)>
            {
                ("marker", marker.ToString()),
                ("version", version.ToString()),
                ("seq_profile", seqProfile.ToString()),
                ("seq_level_idx_0", seqLevelIdx0.ToString()),
                ("seq_tier_0", seqTier0.ToString()),
                ("high_bitdepth", highBitdepth.ToString()),
                ("twelve_bit", twelveBit.ToString()),
                ("monochrome", monochrome.ToString()),
                ("chroma_subsampling_x", chromaSubX.ToString()),
                ("chroma_subsampling_y", chromaSubY.ToString()),
                ("chroma_sample_position", chromaSamplePosition.ToString()),
                ("reserved", reserved.ToString()),
                ("initial_presentation_delay_present", initialPresentationDelayPresent.ToString())
            };

            if (initialPresentationDelayPresent == 1)
            {
                if (fs.Position + 1 <= end)
                {
                    byte v = reader.ReadByte();
                    byte delay = (byte)(v >> 4);

                    dataLines.Add(("initial_presentation_delay_minus_one", delay.ToString()));
                }
            }

            return new ParseResult
            {
                
                Title = $"AV1ConfigurationBox '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }
    }
}