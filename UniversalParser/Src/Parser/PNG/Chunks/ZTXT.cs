//TODO:unchecked 
namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class ZTXt
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

            if (type != "zTXt")
                throw new InvalidDataException($"Expected zTXt but got '{type}'");

            byte[] data = reader.ReadBytes((int)length);

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            int offset = 0;

            int keywordEnd = Array.IndexOf(data, (byte)0x00, offset);
            if (keywordEnd < 0)
                throw new InvalidDataException("Invalid zTXt: missing keyword terminator");

            string keyword = System.Text.Encoding.ASCII.GetString(data, offset, keywordEnd - offset);
            offset = keywordEnd + 1;

            byte compressionMethod = data[offset++];
            if (compressionMethod != 0)
                throw new InvalidDataException("Invalid zTXt compression method");

            byte[] compressed = data.Skip(offset).ToArray();
            string text = DecompressZlib(compressed);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("Keyword", keyword),
                ("Text", text),
                ("Compression", compressionMethod.ToString())
            };

            return new ParseResult
            {
                Title = "CompressedText 'zTXt'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }

        private static string DecompressZlib(byte[] data)
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