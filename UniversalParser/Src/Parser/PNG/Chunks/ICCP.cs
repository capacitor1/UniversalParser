namespace UniversalParser.Src.Parser.PNG.Chunks
{
    internal static class ICCP
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

            if (type != "iCCP")
                throw new InvalidDataException($"Expected iCCP but got '{type}'");

            byte[] data = reader.ReadBytes((int)length);

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            int offset = 0;

            // -----------------------------
            // 1. Profile name (ASCII, null terminated)
            // -----------------------------
            int nameEnd = Array.IndexOf(data, (byte)0x00, offset);
            if (nameEnd < 0)
                throw new InvalidDataException("Invalid iCCP: missing profile name terminator");

            string profileName = System.Text.Encoding.ASCII.GetString(data, offset, nameEnd - offset);
            offset = nameEnd + 1;

            // -----------------------------
            // 2. Compression method
            // -----------------------------
            byte compressionMethod = data[offset++];

            if (compressionMethod != 0)
                throw new NotSupportedException($"Unsupported iCCP compression method: {compressionMethod}");

            // -----------------------------
            // 3. Compressed ICC profile (zlib)
            // -----------------------------
            byte[] compressedProfile = data.Skip(offset).ToArray();
            byte[] decompressedProfile = ZlibDecompressToBytes(compressedProfile);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("ProfileName", profileName),
                ("CompressionMethod", compressionMethod.ToString()),
                ("CompressedSize", compressedProfile.Length.ToString()),
                ("DecompressedSize", decompressedProfile.Length.ToString()),
                ("ProfileSignature", GetICCSignature(decompressedProfile))
            };
            //File.WriteAllBytes("decompressedProfile.icc", decompressedProfile);
            return new ParseResult
            {
                Title = $"ICCProfile 'iCCP'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }

        // -----------------------------
        // zlib → raw bytes
        // -----------------------------
        private static byte[] ZlibDecompressToBytes(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var zlib = new System.IO.Compression.ZLibStream(
                input,
                System.IO.Compression.CompressionMode.Decompress);

            using var output = new MemoryStream();
            zlib.CopyTo(output);

            return output.ToArray();
        }

        // -----------------------------
        // ICC profile signature check
        // -----------------------------
        private static string GetICCSignature(byte[] profile)
        {
            if (profile.Length < 4)
                return "Invalid";

            return System.Text.Encoding.ASCII.GetString(profile, 36, 4);
        }
    }
}