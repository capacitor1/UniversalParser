using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// TrackReferenceTypeBox -- children of 'tref' (ISO/IEC 14496-12).
// Plain box; the 4CC is the reference type itself.
//
//   size       4 bytes
//   type       4 bytes
//   track_ID   4 bytes, repeated until the end of the box
namespace UniversalParser.Src.Parser.MPEG.Boxes;
internal static class TrackReferenceType
{
    public static ParseResult Parse(MPEGParser parser, Node node, string fourCC)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        string title = $"TrackReferenceTypeBox '{fourCC}'";

        fs.Position = start;
        Seek(reader, start);

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

        int index = 0;
        while (end - Pos(reader) >= 4)
        {
            dataLines.Add(($"track_ID[{index}]", reader.ReadUInt32BE().ToString()));
            index++;
        }

        dataLines.Add(("track_ID_count", index.ToString()));

        long unread = end - Pos(reader);
        if (unread > 0)
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");

        return Build(parser, node, title, dataLines);
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

// Six 4CCs are registered both as item reference types and as track reference types:
// 'auxl', 'cdsc', 'dpnd', 'font', 'tbas', 'thmb'. The two layouts are not reliably
// distinguishable from the payload size alone, so the containing box decides.
internal static class ReferenceBox
{
    public static ParseResult Parse(MPEGParser parser, Node node, string fourCC)
    {
        string? parentType = parser.GetParentType(node);

        if (parentType == "tref")
            return TrackReferenceType.Parse(parser, node, fourCC);

        if (parentType == "iref")
            return SingleItemTypeReference.Parse(parser, node, fourCC);

        // Unknown parent: the payload length is the only remaining hint. A track
        // reference is a whole number of 32-bit IDs; an item reference is
        // 4 + 2 * reference_count bytes.
        long payloadLength = (long)node.Length - 8;

        if (payloadLength > 0 && payloadLength % 4 != 0)
            return SingleItemTypeReference.Parse(parser, node, fourCC);

        return TrackReferenceType.Parse(parser, node, fourCC);
    }
}