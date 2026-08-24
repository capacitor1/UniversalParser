namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Iloc
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE();
            reader.ReadFourCC();

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            // ===== 4-bit packed header =====
            byte packed = reader.ReadByte();

            int offsetSize = (packed & 0xF0) >> 4;
            int lengthSize = packed & 0x0F;

            packed = reader.ReadByte();

            int baseOffsetSize = (packed & 0xF0) >> 4;
            int indexSize = (packed & 0x0F);

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}"),
                ("offset_size", offsetSize.ToString()),
                ("length_size", lengthSize.ToString()),
                ("base_offset_size", baseOffsetSize.ToString()),
                ("index_size", indexSize.ToString())
            };

            // ===== item_count =====
            uint itemCount = version switch
            {
                < 2 => reader.ReadUInt16BE(),
                2 => reader.ReadUInt32BE(),
                _ => reader.ReadUInt32BE()
            };

            dataLines.Add(("item_count", itemCount.ToString()));

            for (int i = 0; i < itemCount; i++)
            {
                uint itemId = version switch
                {
                    < 2 => reader.ReadUInt16BE(),
                    2 => reader.ReadUInt32BE(),
                    _ => reader.ReadUInt32BE()
                };

                dataLines.Add(($"item[{i}]", $"id= {itemId}"));

                uint constructionMethod = 0;

                if (version == 1 || version == 2)
                {
                    ushort tmp = reader.ReadUInt16BE();
                    constructionMethod = (uint)(tmp & 0x0F);
                    // upper 12 bits reserved
                }

                ushort dataRefIndex = reader.ReadUInt16BE();

                dataLines.Add((string.Empty, $"data_ref_index= {dataRefIndex}"));

                ulong baseOffset = ReadUIntVar(reader, baseOffsetSize);
                dataLines.Add((string.Empty,$"base_offset= {baseOffset}"));

                ushort extentCount = reader.ReadUInt16BE();
                dataLines.Add((string.Empty, $"extent_count= {extentCount}"));

                for (int j = 0; j < extentCount; j++)
                {
                    if ((version == 1 || version == 2) && indexSize > 0)
                    {
                        ulong extentIndex = ReadUIntVar(reader, indexSize);
                        dataLines.Add((string.Empty, $"extent[{j}] index= {extentIndex}"));
                    }

                    ulong extentOffset = ReadUIntVar(reader, offsetSize);
                    ulong extentLength = ReadUIntVar(reader, lengthSize);

                    dataLines.Add((string.Empty, $"extent[{j}] offset= {extentOffset},length= {extentLength}"));
                }
            }

            return new ParseResult
            {
                
                Title = "ItemLocationBox 'iloc'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }

        private static ulong ReadUIntVar(MpegReader reader, int size)
        {
            return size switch
            {
                0 => 0,
                1 => reader.ReadByte(),
                2 => reader.ReadUInt16BE(),
                4 => reader.ReadUInt32BE(),
                8 => reader.ReadUInt64BE(),
                _ => throw new Exception($"Invalid size: {size}")
            };
        }
    }
}