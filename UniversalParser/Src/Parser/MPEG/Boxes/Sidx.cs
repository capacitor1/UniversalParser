namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Sidx
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            long start = (long)node.Position;
            long end = start + (long)node.Length;

            reader.ReadUInt32BE();
            reader.ReadFourCC(); // sidx

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

            if (fs.Position + 8 > end)
            {
                return BuildResult(parser, node, start, dataLines);
            }

            uint referenceId = reader.ReadUInt32BE();
            uint timescale = reader.ReadUInt32BE();

            dataLines.Add(("reference_ID", referenceId.ToString()));
            dataLines.Add(("timescale", timescale.ToString()));

            ulong earliestPresentationTime;
            ulong firstOffset;

            if (version == 0)
            {
                if (fs.Position + 8 > end)
                    return BuildResult(parser, node, start, dataLines);

                earliestPresentationTime = reader.ReadUInt32BE();
                firstOffset = reader.ReadUInt32BE();
            }
            else
            {
                if (fs.Position + 16 > end)
                    return BuildResult(parser, node, start, dataLines);

                earliestPresentationTime = reader.ReadUInt64BE();
                firstOffset = reader.ReadUInt64BE();
            }

            dataLines.Add(("earliest_presentation_time", earliestPresentationTime.ToString()));
            dataLines.Add(("first_offset", firstOffset.ToString()));

            if (fs.Position + 4 > end)
                return BuildResult(parser, node, start, dataLines);

            ushort reserved = reader.ReadUInt16BE();
            ushort referenceCount = reader.ReadUInt16BE();

            dataLines.Add(("reserved", $"0x{reserved:X4}"));
            dataLines.Add(("reference_count", referenceCount.ToString()));

            for (int i = 0; i < referenceCount; i++)
            {
                if (fs.Position + 12 > end)
                    break;

                uint refInfo = reader.ReadUInt32BE();

                uint referenceType = refInfo >> 31;
                uint referencedSize = refInfo & 0x7FFFFFFF;

                uint subsegmentDuration = reader.ReadUInt32BE();

                uint sapInfo = reader.ReadUInt32BE();

                uint startsWithSap = sapInfo >> 31;
                uint sapType = (sapInfo >> 28) & 0x7;
                uint sapDeltaTime = sapInfo & 0x0FFFFFFF;

                dataLines.Add((string.Empty,
                    $"reference_type= {referenceType}"));

                dataLines.Add((string.Empty,
                    $"referenced_size= {referencedSize}"));

                dataLines.Add((string.Empty,
                    $"subsegment_duration= {subsegmentDuration}"));
                dataLines.Add((string.Empty,
                    $"starts_with_SAP= {startsWithSap}"));

                dataLines.Add((string.Empty,
                    $"SAP_type= {sapType}"));
                dataLines.Add((string.Empty,
                    $"SAP_delta_time= {sapDeltaTime}"));
            }

            return BuildResult(parser, node, start, dataLines);
        }

        private static ParseResult BuildResult(
            MPEGParser parser,
            Node node,
            long start,
            List<(string K, string V)> dataLines)
        {
            return new ParseResult
            {
                Title = "SegmentIndexBox 'sidx'",

                Position = node.Position,
                Length = node.Length,

                DataLines = dataLines,

                RawData = new OffsetStream(
                    parser.FileStream,
                    start,
                    (long)node.Length)
            };
        }
    }
}