namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFC3
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

            int offset = 4;

            // =========================
            // Basic info
            // =========================
            byte precision = raw[offset++];
            ushort height = ReadUInt16(raw, ref offset);
            ushort width = ReadUInt16(raw, ref offset);
            byte componentCount = raw[offset++];

            dataLines.Add(("Precision", precision.ToString()));
            dataLines.Add(("Width", width.ToString()));
            dataLines.Add(("Height", height.ToString()));
            dataLines.Add(("ComponentCount", componentCount.ToString()));

            // =========================
            // Components
            // =========================
            for (int i = 0; i < componentCount; i++)
            {
                byte componentId = raw[offset++];
                byte huffmanTable = raw[offset++];

                string key = $"Component[{i}]";
                dataLines.Add((key, $"HuffmanTable= {huffmanTable}"));
            }

            // =========================
            // Lossless specific (simplified)
            // =========================
            if (offset < raw.Length)
            {
                byte predictor = raw[offset++];
                dataLines.Add(("Predictor", predictor.ToString()));
            }

            if (offset < raw.Length)
            {
                byte pointTransform = raw[offset++];
                dataLines.Add(("PointTransform", pointTransform.ToString()));
            }

            return Build(parser, node, dataLines);
        }

        // =========================
        // helpers
        // =========================

        private static ushort ReadUInt16(byte[] data, ref int offset)
        {
            return (ushort)((data[offset++] << 8) | data[offset++]);
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
                Title = $"StartOfFrame(SOF3) '{node.NodeName}'",
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