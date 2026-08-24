namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Xtra
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;
            long end = start + (long)node.Length;

            reader.ReadUInt32BE();
            reader.ReadFourCC();

            var dataLines = new List<(string K, string V)>
            {

            };

            long remaining = end - fs.Position;

            byte[] payload = reader.ReadBytes((int)remaining);

            dataLines.Add(("<payload_length>", payload.Length.ToString()));

            // best-effort detect string
            string text = TryDetectText(payload);
            if (!string.IsNullOrEmpty(text))
                dataLines.Add(("<as_text>", text));

            return new ParseResult
            {
                
                Title = "(Extension)Xtra 'Xtra'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }

        private static string TryDetectText(byte[] data)
        {
            try
            {
                var s = System.Text.Encoding.UTF8.GetString(data);
                if (s.Contains("\0")) s = s.Replace("\0", "");
                return s.Length > 200 ? s[..200] : s;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}