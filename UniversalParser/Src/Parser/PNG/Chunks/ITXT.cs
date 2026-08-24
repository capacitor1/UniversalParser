namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class ITXt
    {
        public static ParseResult Parse(PNGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var fs = parser.FileStream;
            var reader = new PngReader(fs);

            long pos = (long)node.Position;
            reader.Seek(pos);

            uint length = reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            if (type != "iTXt")
                throw new InvalidDataException($"Expected iTXt but got '{type}'");

            byte[] data = reader.ReadBytes((int)length);

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            int offset = 0;

            // -------------------------
            // 1. Keyword (ASCII, null-terminated)
            // -------------------------
            int keywordEnd = Array.IndexOf(data, (byte)0x00, offset);
            if (keywordEnd < 0)
                throw new InvalidDataException("Invalid iTXt: missing keyword terminator");

            string keyword = System.Text.Encoding.ASCII.GetString(data, offset, keywordEnd - offset);
            offset = keywordEnd + 1;

            // -------------------------
            // 2. Compression flag
            // -------------------------
            byte compressionFlag = data[offset++];
            byte compressionMethod = data[offset++];

            // -------------------------
            // 3. Language tag (null-terminated)
            // -------------------------
            int langEnd = Array.IndexOf(data, (byte)0x00, offset);
            if (langEnd < 0)
                throw new InvalidDataException("Invalid iTXt: missing language tag terminator");

            string languageTag = System.Text.Encoding.ASCII.GetString(data, offset, langEnd - offset);
            offset = langEnd + 1;

            // -------------------------
            // 4. Translated keyword (UTF-8, null-terminated)
            // -------------------------
            int translatedEnd = Array.IndexOf(data, (byte)0x00, offset);
            if (translatedEnd < 0)
                throw new InvalidDataException("Invalid iTXt: missing translated keyword terminator");

            string translatedKeyword = System.Text.Encoding.UTF8.GetString(data, offset, translatedEnd - offset);
            offset = translatedEnd + 1;

            // -------------------------
            // 5. Text (UTF-8, possibly compressed)
            // -------------------------
            byte[] textBytes = data.Skip(offset).ToArray();

            string text;

            if (compressionFlag == 0)
            {
                text = System.Text.Encoding.UTF8.GetString(textBytes);
            }
            else
            {
                // PNG uses zlib (same as IDAT)
                text = ZlibDecompress(textBytes);
            }

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("Keyword", keyword),
                ("CompressionFlag", compressionFlag.ToString()),
                ("CompressionMethod", compressionMethod.ToString()),
                ("LanguageTag", languageTag),
                ("TranslatedKeyword", translatedKeyword),
                ("Text", text)
            };

            return new ParseResult
            {
                Title = "InternationalText 'iTXt'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }

        // ---------------------------------
        // Minimal zlib placeholder
        // ---------------------------------
        private static string ZlibDecompress(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var zlib = new System.IO.Compression.ZLibStream(
                input,
                System.IO.Compression.CompressionMode.Decompress);

            using var output = new MemoryStream();
            zlib.CopyTo(output);

            return System.Text.Encoding.UTF8.GetString(output.ToArray());
        }
    }
}