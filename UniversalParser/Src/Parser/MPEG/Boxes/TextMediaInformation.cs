using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// Text media information atom 'text' -- child of 'gmhd' (QuickTime File Format).
//
//   size    4 bytes
//   type    4 bytes   'text'
//   matrix  36 bytes  a 3x3 matrix structure
//
// This is a plain Box: there is no version/flags field.
// Matrix layout, in stored order:
//   a  b  u
//   c  d  v
//   tx ty w
// a, b, c, d, tx, ty are 16.16 fixed-point; u, v, w are 2.30 fixed-point.
namespace UniversalParser.Src.Parser.MPEG.Boxes;
internal static class TextMediaInformation
{
    private const int MatrixEntries = 9;

    private static readonly string[] EntryNames =
        { "a", "b", "u", "c", "d", "v", "tx", "ty", "w" };

    // 2.30 for u, v, w (indices 2, 5, 8); 16.16 for the rest.
    private static readonly double[] EntryScales =
        { 65536.0, 65536.0, 1073741824.0,
          65536.0, 65536.0, 1073741824.0,
          65536.0, 65536.0, 1073741824.0 };

    public static ParseResult Parse(MPEGParser parser, Node node)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        const string title = "TextMediaInformationAtom 'text'";

        fs.Position = start;
        Seek(reader, start);

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

        for (int i = 0; i < MatrixEntries; i++)
        {
            if (end - Pos(reader) < 4)
            {
                Warn(dataLines, $"Matrix truncated: read {i} of {MatrixEntries} entries.");
                return Build(parser, node, title, dataLines);
            }

            uint raw = reader.ReadUInt32BE();
            dataLines.Add(($"matrix[{i}] {EntryNames[i]}", FormatFixed(raw, EntryScales[i])));
        }

        long unread = end - Pos(reader);
        if (unread > 0)
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");

        return Build(parser, node, title, dataLines);
    }

    // The decimal form is shown because the stored bit pattern is not directly
    // readable as a matrix coefficient.
    private static string FormatFixed(uint raw, double scale)
    {
        double value = unchecked((int)raw) / scale;

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