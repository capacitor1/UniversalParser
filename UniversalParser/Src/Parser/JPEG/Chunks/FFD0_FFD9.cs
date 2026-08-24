namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFD0_FFD9
    {
        public static ParseResult Parse(JPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            // NodeName = "FFD0" / "FFD9" etc.
            if (!TryParseMarker(node.NodeName, out ushort marker))
            {
                dataLines.Add(("<Error>", $"Invalid marker name: {node.NodeName}"));
                return Build(parser, node, dataLines, "Unknown Marker");
            }

            string title;

            if (marker >= 0xFFD0 && marker <= 0xFFD7)
            {
                int rstIndex = marker - 0xFFD0;

                title = $"RestartMarker (RST{rstIndex}) '{node.NodeName}'";

                dataLines.Add(("RestartIndex", rstIndex.ToString()));
            }
            else if (marker == 0xFFD8)
            {
                title = $"StartOfImage (SOI) '{node.NodeName}'";
            }
            else if (marker == 0xFFD9)
            {
                title = $"EndOfImage (EOI) '{node.NodeName}'";
            }
            else
            {
                title = $"UnknownMarker '{node.NodeName}'";
            }
            dataLines.Add(("<PayloadLength>", "0"));

            return new ParseResult
            {
                Title = title,
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

        /// <summary>
        /// 将 "FFD0" -> 0xFFD0
        /// </summary>
        private static bool TryParseMarker(string nodeName, out ushort marker)
        {
            marker = 0;

            if (string.IsNullOrWhiteSpace(nodeName) || nodeName.Length != 4)
                return false;

            return ushort.TryParse(
                nodeName,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out marker
            );
        }

        private static ParseResult Build(
            JPEGParser parser,
            Node node,
            List<(string K, string V)> dataLines,
            string title)
        {
            return new ParseResult
            {
                Title = title,
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