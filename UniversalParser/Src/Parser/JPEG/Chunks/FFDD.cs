namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFDD
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

            dataLines.Add(("<PayloadLength>", (raw.Length - 4).ToString()));

            // =========================
            // Restart Interval (2 bytes)
            // =========================
            if (offset + 2 > raw.Length)
            {
                dataLines.Add(("<Error>", "Missing restart interval"));
                return Build(parser, node, dataLines);
            }

            ushort restartInterval = ReadUInt16(raw, ref offset);

            if (restartInterval == 0)
            {
                dataLines.Add(("RestartInterval", "0 (disabled)"));
            }
            else
            {
                dataLines.Add(("RestartInterval", restartInterval.ToString()));
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
                Title = $"RestartIntervalDefinition(DRI) '{node.NodeName}'",
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