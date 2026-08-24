namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Data
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;
            long end = start + (long)node.Length;

            reader.ReadUInt32BE();
            reader.ReadFourCC(); // "data"


            var dataLines = new List<(string K, string V)>
            {

            };

            uint typeIndicator = reader.ReadUInt32BE();
            uint locale = reader.ReadUInt32BE();

            dataLines.Add(("type_indicator", typeIndicator.ToString()));
            dataLines.Add(("locale", locale.ToString()));

            long payloadSize = end - fs.Position;

            if (payloadSize > 0)
            {
                byte[] payload = reader.ReadBytes((int)payloadSize);

                dataLines.Add(("<payload_length>", payload.Length.ToString()));

                string decoded = DecodeValue(typeIndicator, payload);
                dataLines.Add(("$value", decoded));
            }

            return new ParseResult
            {
                Title = "(QuickTime)Data 'data'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }

        private static string DecodeValue(uint typeIndicator, byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            return typeIndicator switch
            {
                1 => TryUtf8(data),          // UTF-8 string
                13 => $"<jpeg:{data.Length}>",
                14 => $"<png:{data.Length}>",
                21 => DecodeInt(data),
                _ => $"<raw:{data.Length}>"
            };
        }

        private static string TryUtf8(byte[] data)
        {
            try
            {
                return System.Text.Encoding.UTF8.GetString(data).Trim('\0');
            }
            catch
            {
                return $"<binary:{data.Length}>";
            }
        }

        private static string DecodeInt(byte[] data)
        {
            if (data.Length == 0) return "0";

            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);

            return BitConverter.ToInt32(data, 0).ToString();
        }
    }
}