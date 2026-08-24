namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFEE
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

            if (raw == null || raw.Length < 12)
            {
                dataLines.Add(("<Error>", "Segment too small"));
                return Build(parser, node, dataLines);
            }

            // "Adobe" signature
            bool isAdobe =
                raw[0] == 0x41 && // A
                raw[1] == 0x64 && // d
                raw[2] == 0x6F && // o
                raw[3] == 0x62 && // b
                raw[4] == 0x65;   // e

            if (isAdobe)
            {
                dataLines.Add(("Format", "Adobe APP14"));

                ushort version = (ushort)((raw[5] << 8) | raw[6]);
                ushort flags0 = (ushort)((raw[7] << 8) | raw[8]);
                ushort flags1 = (ushort)((raw[9] << 8) | raw[10]);

                byte colorTransform = raw[11];

                dataLines.Add(("Version", version.ToString()));
                dataLines.Add(("Flags0", flags0.ToString()));
                dataLines.Add(("Flags1", flags1.ToString()));
                dataLines.Add(("ColorTransform", colorTransform switch
                {
                    0 => "Unknown",
                    1 => "YCbCr",
                    2 => "YCCK (CMYK)",
                    _ => $"Reserved ({colorTransform})"
                }));
            }
            else
            {
                dataLines.Add(("<Format>", "Unknown APP14"));
            }

            dataLines.Add(("<PayloadLength>", node.Length.ToString()));

            return Build(parser, node, dataLines);
        }

        private static byte[] ReadNodeBytes(JPEGParser parser, Node node)
        {
            byte[] buffer = new byte[node.Length];

            lock (parser.FileStream)
            {
                parser.FileStream.Seek((long)node.Position + 4, System.IO.SeekOrigin.Begin);
                parser.FileStream.ReadExactly(buffer);
            }

            return buffer;
        }

        private static bool TryParseMarker(string name, out ushort marker)
        {
            marker = 0;

            if (string.IsNullOrWhiteSpace(name) || name.Length != 4)
                return false;

            return ushort.TryParse(name,
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
                Title = $"ApplicationMarker(APP14) '{node.NodeName}'",
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