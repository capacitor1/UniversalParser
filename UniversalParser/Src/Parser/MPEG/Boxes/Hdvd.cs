using System.Text;
namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Hdvd
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            var dataLines = new List<(string K, string V)>
            {

            };

            long end = (long)node.Position + (long)node.Length;
            while (fs.Position + 8 <= end)
            {
                long childPos = fs.Position;

                uint size = reader.ReadUInt32BE();
                string childType = reader.ReadFourCC();

                if (childType == "data")
                {
                    ParseDataBox(fs, reader, size, dataLines);
                }

                //fs.Position = childPos + size;
            }

            return new ParseResult
            {
                
                Title = "(QuickTime)Hdvd 'hdvd'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
        // =========================
        // data box parser
        // =========================
        private static void ParseDataBox(FileStream fs, MpegReader reader, uint size,
            List<(string K, string V)> dataLines)
        {
            uint dataType = reader.ReadUInt32BE();
            uint locale = reader.ReadUInt32BE();

            var value = reader.ReadByte();

            dataLines.Add(("data.type", $"0x{dataType:X8}"));
            dataLines.Add(("data.locale", $"0x{locale:X8}"));
            dataLines.Add(("$value", value.ToString()));
        }
    }
}