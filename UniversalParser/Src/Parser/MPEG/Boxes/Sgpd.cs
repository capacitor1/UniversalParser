using static System.Runtime.InteropServices.JavaScript.JSType;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Sgpd
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

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            string groupingType = reader.ReadFourCC();

            uint defaultLength = 0;
            uint defaultSampleDescriptionIndex = 0;

            if (version == 1)
            {
                defaultLength = reader.ReadUInt32BE();
            }

            if (version >= 2)
            {
                defaultSampleDescriptionIndex = reader.ReadUInt32BE();
            }

            uint entryCount = reader.ReadUInt32BE();

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}"),
                ("grouping_type", groupingType)
            };
            if (version == 1)
            {
                dataLines.Add(("default_length", defaultLength.ToString()));
            }

            if (version >= 2)
            {
                dataLines.Add(("default_sample_description_index", defaultSampleDescriptionIndex.ToString()));
            }
            dataLines.Add(("entry_count", entryCount.ToString()));
            //
            for (int i = 0; i < entryCount; i++)
            {
                if (fs.Position >= end)
                    break;

                uint descriptionLength = 0;
                bool hasExplicitLength = false;
                dataLines.Add(($"entry[{i}]", $"[{i}]"));
                // ===== version 1 special rule =====
                if (version == 1 && defaultLength == 0)
                {
                    if (fs.Position + 4 > end)
                        break;

                    descriptionLength = reader.ReadUInt32BE();
                    hasExplicitLength = true;

                    dataLines.Add((string.Empty, $"description_length= {descriptionLength}"));
                }

                uint entryLength = defaultLength;

                if (hasExplicitLength)
                {
                    entryLength = descriptionLength;
                }

                if (entryLength == 0)
                {
                    dataLines.Add((string.Empty, "payload_length= 0"));
                    continue;
                }

                if (fs.Position + entryLength > end)
                    entryLength = (uint)(end - fs.Position);

                dataLines.Add((string.Empty, $"payload_length= {entryLength}"));
                byte[] entryDataT = reader.ReadBytes(Math.Min(32, (int)entryLength));//max 32 bytes
                bool istruncated = entryDataT.Length < entryLength;

                //render
                dataLines.Add((string.Empty, $"<{(istruncated ? "truncated_" : string.Empty)}payload> [..{entryDataT.Length - 1}] = 0x{Convert.ToHexString(entryDataT)}"));
            }

            return new ParseResult
            {
                
                Title = $"SampleGroupDescriptionBox '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, start, (long)node.Length)
            };
        }
    }
}