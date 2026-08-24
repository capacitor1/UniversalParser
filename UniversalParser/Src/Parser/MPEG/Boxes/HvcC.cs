namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class HvcC
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;
            long end = start + (long)node.Length;

            reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            byte configurationVersion = reader.ReadByte();

            byte generalProfileSpaceTierFlag = reader.ReadByte();
            byte generalProfileIdc = reader.ReadByte();

            uint generalProfileCompatibilityFlags = reader.ReadUInt32BE();
            ulong generalConstraintIndicator = reader.ReadUInt64BE();

            byte levelIdc = reader.ReadByte();

            ushort minSpatialSegmentation = reader.ReadUInt16BE();

            byte parallelismType = reader.ReadByte();
            byte chromaFormat = reader.ReadByte();

            byte bitDepthLumaMinus8 = reader.ReadByte();
            byte bitDepthChromaMinus8 = reader.ReadByte();

            ushort avgFrameRate = reader.ReadUInt16BE();

            byte constantFrameRate = reader.ReadByte();
            byte numTemporalLayers = reader.ReadByte();
            byte temporalIdNested = reader.ReadByte();

            byte lengthSizeMinusOne = reader.ReadByte();

            byte numOfArrays = reader.ReadByte();

            var dataLines = new List<(string K, string V)>
            {
                ("configurationVersion", configurationVersion.ToString()),
                ("generalProfileSpaceTierFlag", $"0x{generalProfileSpaceTierFlag:X2}"),
                ("generalProfileIdc", generalProfileIdc.ToString()),
                ("generalProfileCompatibilityFlags", $"0x{generalProfileCompatibilityFlags:X8}"),
                ("generalConstraintIndicator", $"0x{generalConstraintIndicator:X16}"),
                ("levelIdc", levelIdc.ToString()),
                ("minSpatialSegmentation", minSpatialSegmentation.ToString()),
                ("parallelismType", parallelismType.ToString()),
                ("chromaFormat", chromaFormat.ToString()),
                ("bitDepthLumaMinus8", bitDepthLumaMinus8.ToString()),
                ("bitDepthChromaMinus8", bitDepthChromaMinus8.ToString()),
                ("avgFrameRate", avgFrameRate.ToString()),
                ("constantFrameRate", constantFrameRate.ToString()),
                ("numTemporalLayers", numTemporalLayers.ToString()),
                ("temporalIdNested", temporalIdNested.ToString()),
                ("lengthSizeMinusOne", lengthSizeMinusOne.ToString()),
                ("numOfArrays", numOfArrays.ToString())
            };

            // ===== NAL arrays =====
            for (int i = 0; i < numOfArrays; i++)
            {
                if (fs.Position + 3 > end)
                    break;

                byte arrayHeader = reader.ReadByte();

                bool arrayCompleteness = (arrayHeader & 0x80) != 0;
                bool reserved = (arrayHeader & 0x40) != 0;
                byte nalUnitType = (byte)(arrayHeader & 0x3F);

                ushort nalCount = reader.ReadUInt16BE();

                dataLines.Add(($"nal_array[{i}]", $"nal_type= {nalUnitType}"));
                dataLines.Add((string.Empty, $"complete= {arrayCompleteness}"));
                dataLines.Add((string.Empty, $"nal_count= {nalCount}"));

                for (int j = 0; j < nalCount; j++)
                {
                    if (fs.Position + 2 > end)
                        break;

                    ushort nalSize = reader.ReadUInt16BE();

                    dataLines.Add((string.Empty, $"nal[{j}] size= {nalSize}"));

                    if (fs.Position + nalSize > end)
                        break;

                    reader.ReadBytes(nalSize);
                }
            }

            return new ParseResult
            {
                
                Title = $"HEVCConfigurationBox '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }
    }
}