namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFDA
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
            // Number of components
            // =========================

            byte componentCount = raw[offset++];
            dataLines.Add(("ComponentCount", componentCount.ToString()));

            // =========================
            // Component specs
            // =========================
            for (int i = 0; i < componentCount; i++)
            {

                byte componentId = raw[offset++];
                byte tableInfo = raw[offset++];

                int dcTable = (tableInfo >> 4) & 0x0F;
                int acTable = tableInfo & 0x0F;

                string key = $"Component[{i}]";

                dataLines.Add((key, $"DCTable= {dcTable}"));
                dataLines.Add((string.Empty, $"ACTable= {acTable}"));
            }

            // =========================
            // Spectral selection (3 bytes)
            // =========================

            byte Ss = raw[offset++];
            byte Se = raw[offset++];
            byte AhAl = raw[offset++];

            int Ah = (AhAl >> 4) & 0x0F;
            int Al = AhAl & 0x0F;

            dataLines.Add(("SpectralStart(Ss)", Ss.ToString()));
            dataLines.Add(("SpectralEnd(Se)", Se.ToString()));
            dataLines.Add(("ApproxHigh(Ah)", Ah.ToString()));
            dataLines.Add(("ApproxLow(Al)", Al.ToString()));

            // ⚠️ IMPORTANT:
            // DO NOT READ ANYTHING AFTER THIS POINT
            // (bitstream begins here in real JPEG)

            return Build(parser, node, dataLines);
        }

        // =========================
        // helpers
        // =========================

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
                Title = $"StartOfScan(SOS) '{node.NodeName}'",
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