namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Mean
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;
            long end = start + (long)node.Length;

            reader.ReadUInt32BE();
            reader.ReadFourCC(); // "mean"
            uint locale = reader.ReadUInt32BE();

            var dataLines = new List<(string K, string V)>
            {
               ("locale", locale.ToString())
            };

            // ===== domain string (null-terminated UTF8) =====
            long remaining = end - fs.Position;

            if (remaining > 0)
            {
                byte[] raw = reader.ReadBytes((int)remaining);

                string domain = DecodeNullTerminatedUtf8(raw);

                dataLines.Add(("domain", domain));
            }

            return new ParseResult
            {
                
                Title = "(QuickTime)Mean 'mean'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }

        private static string DecodeNullTerminatedUtf8(byte[] data)
        {
            int len = Array.IndexOf(data, (byte)0);
            if (len < 0) len = data.Length;

            return System.Text.Encoding.UTF8.GetString(data, 0, len);
        }
    }
}