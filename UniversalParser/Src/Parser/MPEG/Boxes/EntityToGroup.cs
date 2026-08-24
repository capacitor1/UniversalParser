using System.Text;
using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// EntityToGroupBox -- children of 'grpl' (ISO/IEC 14496-12).
// The 4CC is the grouping_type itself.
//
//   size                    4 bytes
//   type                    4 bytes
//   version                 1 byte
//   flags                   3 bytes
//   group_id                4 bytes
//   num_entities_in_group   4 bytes
//   entity_id               4 bytes, repeated num_entities_in_group times
//   trailing data           bytes, defined per grouping_type
namespace UniversalParser.Src.Parser.MPEG.Boxes;
internal static class EntityToGroup
{
    private const int TrailingHexLimit = 64;

    public static ParseResult Parse(MPEGParser parser, Node node, string fourCC)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        string title = $"EntityToGroupBox '{fourCC}'";

        fs.Position = start;
        Seek(reader, start);

        // ---- Box header: size/type were already validated by MPEGParser,
        //      so only the correct header length has to be skipped here.
        if (end - start < 8)
        {
            Warn(dataLines, $"Box is only {end - start} byte(s) long, too short for a box header.");
            return Build(parser, node, title, dataLines);
        }

        int headerSize = 8;
        uint size32 = reader.ReadUInt32BE();
        reader.ReadFourCC();

        if (size32 == 1 && end - start >= 16)
        {
            reader.ReadUInt64BE();
            headerSize = 16;
        }

        Seek(reader, start + headerSize);

        // ---- version / flags ----
        if (end - Pos(reader) < 4)
        {
            Warn(dataLines, "Cannot read version/flags: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }

        byte version = reader.ReadByte();
        byte f1 = reader.ReadByte();
        byte f2 = reader.ReadByte();
        byte f3 = reader.ReadByte();

        uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

        dataLines.Add(("version", version.ToString()));
        dataLines.Add(("flags", $"0x{flags:X6}"));

        // ---- group_id / num_entities_in_group ----
        if (end - Pos(reader) < 8)
        {
            Warn(dataLines, $"Cannot read group_id/num_entities_in_group: " +
                            $"{end - Pos(reader)} byte(s) remaining, 8 required.");
            return Build(parser, node, title, dataLines);
        }

        uint groupId = reader.ReadUInt32BE();
        uint entityCount = reader.ReadUInt32BE();

        dataLines.Add(("group_id", groupId.ToString()));
        dataLines.Add(("num_entities_in_group", entityCount.ToString()));

        for (uint i = 0; i < entityCount; i++)
        {
            if (end - Pos(reader) < 4)
            {
                Warn(dataLines, $"Read {i} of {entityCount} entity_id value(s); the box ends early.");
                break;
            }

            dataLines.Add(($"entity_id[{i}]", reader.ReadUInt32BE().ToString()));
        }

        // ---- Trailing data: some grouping types define further fields here ----
        long trailing = end - Pos(reader);
        if (trailing > 0)
        {
            dataLines.Add(("trailing_data_size", trailing.ToString()));

            int take = (int)Math.Min(trailing, TrailingHexLimit);
            var bytes = new byte[take];

            for (int i = 0; i < take; i++)
                bytes[i] = reader.ReadByte();

            string hex = ToHex(bytes);
            if (trailing > take)
                hex += $" ... ({trailing} bytes total)";

            dataLines.Add(("trailing_data", hex));

            Seek(reader, end);
        }

        return Build(parser, node, title, dataLines);
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(bytes[i].ToString("X2"));
        }
        return sb.ToString();
    }

    private static void Seek(MpegReader reader, long pos) => reader.Seek(pos, SeekOrigin.Begin);
    private static long Pos(MpegReader reader) => (long)reader.Position;
    private static void Warn(List<(string K, string V)> lines, string msg) => lines.Add(("warning", msg));

    private static ParseResult Build(MPEGParser parser, Node node, string title,
                                     List<(string K, string V)> dataLines)
        => new ParseResult
        {
            Title = title,
            Position = node.Position,
            Length = node.Length,
            DataLines = dataLines,
            RawData = new OffsetStream(parser.FileStream, (long)node.Position, (long)node.Length)
        };
}