namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFDB
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
            // FIX OFFSET MODEL
            // =========================
            int offset = 4;
            int payloadLength = raw.Length - 4;

            int tableIndex = 0;

            while (offset < raw.Length)
            {
                if (offset >= raw.Length)
                    break;

                byte qtInfo = raw[offset++];
                int pq = (qtInfo >> 4) & 0x0F;
                int tq = qtInfo & 0x0F;

                int tableSize = pq == 0 ? 64 : 128;

                string tableKey = $"Table[{tq}]";

                dataLines.Add((tableKey, $"Precision= {pq}"));

                // =========================
                // stats
                // =========================
                byte min = byte.MaxValue;
                byte max = byte.MinValue;
                long sum = 0;

                for (int i = 0; i < tableSize; i++)
                {
                    byte v = raw[offset + i];

                    if (v < min) min = v;
                    if (v > max) max = v;

                    sum += v;
                }

                dataLines.Add((string.Empty, $"Min= {min}"));
                dataLines.Add((string.Empty, $"Max= {max}"));
                dataLines.Add((string.Empty, $"Avg= {(sum / (double)tableSize):F2}"));

                // preview
                string preview = "";
                for (int i = 0; i < tableSize; i++)
                {
                    preview += raw[offset + i];
                    preview += ",";
                }
                preview = preview.TrimEnd(',');
                dataLines.Add((string.Empty, $"Table= [{preview}]"));

                offset += tableSize;
                tableIndex++;
            }

            dataLines.Insert(0,("TableCount", tableIndex.ToString()));

            return Build(parser, node, dataLines);
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
                Title = $"DefineQuantizationTable(DQT) '{node.NodeName}'",
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