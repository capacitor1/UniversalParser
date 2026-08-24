using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// Apple QuickTime track aperture mode dimension atoms -- children of 'tapt'.
// All three share an identical layout (QuickTime File Format Specification):
//
//   size     4 bytes
//   type     4 bytes   'clef' / 'prof' / 'enof'
//   version  1 byte
//   flags    3 bytes
//   width    4 bytes   32-bit fixed-point, 16.16
//   height   4 bytes   32-bit fixed-point, 16.16
namespace UniversalParser.Src.Parser.MPEG.Boxes;
internal static class Clef
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ApertureDimensions.Parse(parser, node, "(QuickTime)TrackCleanApertureDimensions 'clef'");
}

internal static class Prof
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ApertureDimensions.Parse(parser, node, "(QuickTime)TrackProductionApertureDimensions 'prof'");
}

internal static class Enof
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ApertureDimensions.Parse(parser, node, "(QuickTime)TrackEncodedPixelsDimensions 'enof'");
}

internal static class ApertureDimensions
{
    private const double FixedOne = 65536.0;

    public static ParseResult Parse(MPEGParser parser, Node node, string title)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();

        fs.Position = start;
        Seek(reader, start);

        // ---- Box header: size/type were already validated by MPEGParser,
        //      so only the correct header length has to be skipped here.
        if (end - start < 8)
        {
            Warn(dataLines, $"Atom is only {end - start} byte(s) long, too short for an atom header.");
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

        // ---- width / height ----
        if (end - Pos(reader) < 8)
        {
            Warn(dataLines, $"Cannot read width/height: {end - Pos(reader)} byte(s) remaining, 8 required.");
            return Build(parser, node, title, dataLines);
        }

        uint rawWidth = reader.ReadUInt32BE();
        uint rawHeight = reader.ReadUInt32BE();

        dataLines.Add(("width", FormatFixed(rawWidth)));
        dataLines.Add(("height", FormatFixed(rawHeight)));

        long unread = end - Pos(reader);
        if (unread > 0)
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");

        return Build(parser, node, title, dataLines);
    }

    // QuickTime 'Fixed' is a signed 32-bit 16.16 value. The decimal form is shown
    // because the stored bit pattern is not directly readable as a pixel count.
    private static string FormatFixed(uint raw)
    {
        double value = unchecked((int)raw) / FixedOne;

        string text = Math.Abs(value - Math.Round(value)) < 1e-9
            ? Math.Round(value).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

        return $"{text} (0x{raw:X8})";
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