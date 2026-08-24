namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Colr
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            string colourType = reader.ReadFourCC();

            var dataLines = new List<(string K, string V)>
            {
                ("colour_type", colourType)
            };

            if (colourType == "nclx")
            {
                ushort primaries = reader.ReadUInt16BE();
                ushort transfer = reader.ReadUInt16BE();
                ushort matrix = reader.ReadUInt16BE();
                byte fullRange = reader.ReadByte();

                byte fullRangeFlag = (byte)((fullRange & 0x80) >> 7);
                byte reserved = (byte)(fullRange & 0x7F);

                dataLines.Add(("colour_primaries", primaries.ToString()));
                dataLines.Add(("transfer_characteristics", transfer.ToString()));
                dataLines.Add(("matrix_coefficients", matrix.ToString()));
                dataLines.Add(("full_range_flag", fullRangeFlag.ToString()));
                dataLines.Add(("reserved", $"0x{reserved:X2}"));
            }
            else if (colourType == "rICC" || colourType == "prof")
            {
                byte[] remaining = reader.ReadBytes((int)((long)node.Length - (fs.Position - (long)node.Position)));
                dataLines.Add(("<payload_length>", remaining.Length.ToString()));
            }

            return new ParseResult
            {
                
                Title = $"ColourInformation '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}