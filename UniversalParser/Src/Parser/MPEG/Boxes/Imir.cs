using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// ImageMirror 'imir' -- item property inside 'iprp'/'ipco' (ISO/IEC 23008-12).
// Derives from ItemProperty, which is a plain box, so there is no version/flags field.
//
//   size      4 bytes
//   type      4 bytes   'imir'
//   reserved  7 bits
//   axis      1 bit
namespace UniversalParser.Src.Parser.MPEG.Boxes;
internal static class Imir
{
    public static ParseResult Parse(MPEGParser parser, Node node)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        const string title = "ImageMirror 'imir'";

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

        if (end - Pos(reader) < 1)
        {
            Warn(dataLines, "Cannot read reserved/axis: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }

        byte packed = reader.ReadByte();

        dataLines.Add(("reserved", $"0x{(packed >> 1) & 0x7F:X2}"));
        dataLines.Add(("axis", (packed & 0x01).ToString()));

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