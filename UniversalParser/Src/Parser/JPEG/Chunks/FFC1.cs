namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFC1
    {
        public static ParseResult Parse(JPEGParser parser, Node node)
        {
            // TODO：待测试
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
            // Precision / Size / Components
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
                byte sampling = raw[offset++];
                byte qtId = raw[offset++];

                int h = (sampling >> 4) & 0x0F;
                int v = sampling & 0x0F;

                string key = $"Component[{i}]";

                dataLines.Add((key, $"Sampling= {h}x{v}"));
                dataLines.Add((string.Empty, $"QuantTable= {qtId}"));
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
                Title = $"StartOfFrame(SOF1) '{node.NodeName}'",
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