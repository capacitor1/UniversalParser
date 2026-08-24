namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFC4
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

            int tableIndex = 0;

            while (offset < raw.Length)
            {
                if (offset >= raw.Length)
                    break;

                byte tableInfo = raw[offset++];

                int tableClass = (tableInfo >> 4) & 0x0F; // 0=DC,1=AC
                int tableId = tableInfo & 0x0F;

                string tableKey = $"Table[{tableId}]";

                dataLines.Add((tableKey, $"Type= {(tableClass == 0 ? "DC" : "AC")}"));

                // =========================
                // Code lengths (16 bytes)
                // =========================

                int symbolCount = 0;
                int[] codeLengths = new int[16];

                for (int i = 0; i < 16; i++)
                {
                    codeLengths[i] = raw[offset++];
                    symbolCount += codeLengths[i];
                }

                dataLines.Add((string.Empty, $"SymbolCount= {symbolCount}"));

                string lenSummary = "";
                for (int i = 0; i < 16; i++)
                {
                    if (codeLengths[i] > 0)
                        lenSummary += $"{i + 1}:{codeLengths[i]} ";
                }

                dataLines.Add((string.Empty, $"CodeLengths= {lenSummary.Trim()}"));

                // =========================
                // Symbols
                // =========================

                string preview = "";

                for (int i = 0; i < symbolCount; i++)
                {
                    byte sym = raw[offset++];
                    preview += sym.ToString();
                    preview += ",";
                }
                preview = preview.TrimEnd(',');
                dataLines.Add((string.Empty, $"SymbolsPreview= [{preview}]"));

                tableIndex++;
            }

            dataLines.Insert(0,("TableCount", tableIndex.ToString()));

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
                Title = $"HuffmanTable(DHT) '{node.NodeName}'",
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