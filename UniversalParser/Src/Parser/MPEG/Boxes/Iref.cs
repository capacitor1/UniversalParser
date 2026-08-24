namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Iref
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            string boxType = reader.ReadFourCC();

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}")
            };

            int index = 0;

            while (fs.Position < (long)(node.Position + node.Length))
            {
                uint _len = reader.ReadUInt32BE();
                // reference_type (4CC)
                string referenceType = reader.ReadFourCC();

                // version决定 item id width
                if (version == 0)
                {
                    ushort fromItemId = reader.ReadUInt16BE();
                    ushort referenceCount = reader.ReadUInt16BE();

                    dataLines.Add(($"ref[{index}]", referenceType));
                    dataLines.Add((string.Empty, $"from_item_id= {fromItemId}"));
                    dataLines.Add((string.Empty, $"reference_count= {referenceCount}"));
                    ushort toItemId;
                    for (int j = 0; j < referenceCount; j++)
                    {
                        toItemId = reader.ReadUInt16BE();
                        dataLines.Add((string.Empty,$"to[{j}] = {toItemId}"));
                    }
                }
                else
                {
                    uint fromItemId = reader.ReadUInt32BE();
                    ushort referenceCount = reader.ReadUInt16BE();

                    dataLines.Add(($"ref[{index}]", referenceType));
                    dataLines.Add((string.Empty, $"from_item_id= {fromItemId}"));
                    dataLines.Add((string.Empty, $"reference_count= {referenceCount}"));
                    uint toItemId;
                    for (int j = 0; j < referenceCount; j++)
                    {
                        toItemId = reader.ReadUInt32BE();
                        dataLines.Add((string.Empty, $"to[{j}] = {toItemId}"));
                    }
                }

                index++;
            }

            return new ParseResult
            {
                
                Title = $"ItemReferenceBox '{boxType}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}