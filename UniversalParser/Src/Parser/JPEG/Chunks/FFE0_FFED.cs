namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFE0_FFED
    {
        public static ParseResult Parse(JPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (!TryParseMarker(node.NodeName, out ushort marker))
            {
                dataLines.Add(("<Error>", $"Invalid marker name: {node.NodeName}"));
                return Build(parser, node, dataLines, "Unknown APP Segment");
            }

            // 只处理 APP0–APP13
            if (marker < 0xFFE0 || marker > 0xFFED)
            {
                dataLines.Add(("<Error>", "Not an APP segment"));
                return Build(parser, node, dataLines, "Non-APP Segment");
            }

            string appName = GetAppName(marker);

            ulong payloadLength = node.Length > 4 ? node.Length - 4 : 0;

            dataLines.Add(("<PayloadLength>", payloadLength.ToString()));

            return Build(parser, node, dataLines, appName);
        }

        private static string GetAppName(ushort marker)
        {
            int appIndex = marker - 0xFFE0;

            return marker switch
            {
                0xFFE0 => "ApplicationMarker(APP0) 'FFE0'",
                0xFFE2 => "ApplicationMarker(APP2) 'FFE2'",
                0xFFE3 => "ApplicationMarker(APP3) 'FFE3'",
                0xFFE4 => "ApplicationMarker(APP4) 'FFE4'",
                0xFFE5 => "ApplicationMarker(APP5) 'FFE5'",
                0xFFE6 => "ApplicationMarker(APP6) 'FFE6'",
                0xFFE7 => "ApplicationMarker(APP7) 'FFE7'",
                0xFFE8 => "ApplicationMarker(APP8) 'FFE8'",
                0xFFE9 => "ApplicationMarker(APP9) 'FFE9'",
                0xFFEA => "ApplicationMarker(APP10) 'FFEA'",
                0xFFEB => "ApplicationMarker(APP11) 'FFEB'",
                0xFFEC => "ApplicationMarker(APP12) 'FFEC'",
                0xFFED => "ApplicationMarker(APP13) 'FFED'",
                _ => $"ApplicationMarker(APP{appIndex}) 'FF{appIndex + 0xE0:X2}'"
            };
        }

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