namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFFE
    {
        public static ParseResult Parse(JPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (!TryParseMarker(node.NodeName, out ushort marker))
            {
                dataLines.Add(("<Error>", $"Invalid marker name: {node.NodeName}"));
                return Build(parser, node, dataLines);
            }

            byte[] raw = ReadNodeBytes(parser, node);


            // =========================
            // FIX OFFSET RULE
            // FFXX + Length = 4 bytes
            // =========================
            int offset = 4;

            // =========================
            // COMMENT TEXT
            // =========================
            if (offset > raw.Length)
            {
                return Build(parser, node, dataLines);
            }

            int textLength = raw.Length - offset;

            if (textLength <= 0)
            {
                dataLines.Add(("Comment", "(empty)"));
                return Build(parser, node, dataLines);
            }

            string text = DecodeText(raw, offset, textLength);

            dataLines.Add(("Comment", text));

            return Build(parser, node, dataLines);
        }

        // =========================
        // helpers
        // =========================

        private static string DecodeText(byte[] data, int offset, int length)
        {
            // JPEG comment通常是 ASCII / Latin1
            try
            {
                return System.Text.Encoding.UTF8.GetString(data, offset, length)
                    .TrimEnd('\0');
            }
            catch
            {
                return System.Text.Encoding.ASCII.GetString(data, offset, length)
                    .TrimEnd('\0');
            }
        }

        private static byte[] ReadNodeBytes(JPEGParser parser, Node node)
        {
            byte[] buffer = new byte[node.Length];

            lock (parser.FileStream)
            {
                parser.FileStream.Seek((long)node.Position, System.IO.SeekOrigin.Begin);
                parser.FileStream.ReadExactly(buffer);
            }

            return buffer;
        }

        private static bool TryParseMarker(string name, out ushort marker)
        {
            marker = 0;

            if (string.IsNullOrWhiteSpace(name) || name.Length != 4)
                return false;

            return ushort.TryParse(
                name,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out marker);
        }

        private static ParseResult Build(
            JPEGParser parser,
            Node node,
            List<(string K, string V)> dataLines)
        {
            return new ParseResult
            {
                Title = $"Comment(COM) '{node.NodeName}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(
                    parser.FileStream,
                    (long)node.Position,
                    (long)node.Length
                )
            };
        }
    }
}