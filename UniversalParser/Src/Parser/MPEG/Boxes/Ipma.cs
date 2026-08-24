namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Ipma
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            bool largeItemId = version == 1;
            bool use16BitPropertyIndex = (flags & 0x1) != 0;

            uint entryCount = reader.ReadUInt32BE();

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}"),
                ("entry_count", entryCount.ToString()),
                ("large_item_id", largeItemId.ToString()),
                ("property_index_16bit", use16BitPropertyIndex.ToString())
            };

            for (int i = 0; i < entryCount; i++)
            {
                uint itemId = largeItemId
                    ? reader.ReadUInt32BE()
                    : reader.ReadUInt16BE();

                byte associationCount = reader.ReadByte();

                dataLines.Add(($"item[{i}]", $"id= {itemId},association_count= {associationCount}"));

                for (int j = 0; j < associationCount; j++)
                {
                    if (use16BitPropertyIndex)
                    {
                        ushort value = reader.ReadUInt16BE();

                        bool essential = (value & 0x8000) != 0;
                        ushort propertyIndex = (ushort)(value & 0x7FFF);

                        dataLines.Add((string.Empty, $"assoc[{j}] essential= {essential},property_index= {propertyIndex}"));
                    }
                    else
                    {
                        byte value = reader.ReadByte();

                        bool essential = (value & 0x80) != 0;
                        byte propertyIndex = (byte)(value & 0x7F);

                        dataLines.Add((string.Empty, $"assoc[{j}] essential= {essential},property_index= {propertyIndex}"));
                    }
                }
            }


            return new ParseResult
            {
                
                Title = $"ItemPropertyAssociation '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}