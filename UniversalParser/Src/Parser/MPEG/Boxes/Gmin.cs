using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// Base media info atom 'gmin' -- child of 'gmhd' (QuickTime File Format).
//
//   size          4 bytes
//   type          4 bytes   'gmin'
//   version       1 byte
//   flags         3 bytes
//   graphics mode 2 bytes
//   opcolor       6 bytes   three 16-bit values: red, green, blue
//   balance       2 bytes   16-bit fixed-point, 8.8
//   reserved      2 bytes
namespace UniversalParser.Src.Parser.MPEG.Boxes;
internal static class Gmin
{
    public static ParseResult Parse(MPEGParser parser, Node node)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        const string title = "BaseMediaInfoAtom 'gmin'";

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

        if (!Has(reader, end, 4))
        {
            Warn(dataLines, "Cannot read version/flags: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }

        byte version = reader.ReadByte();
        byte f1 = reader.ReadByte();
        byte f2 = reader.ReadByte();
        byte f3 = reader.ReadByte();

        dataLines.Add(("version", version.ToString()));
        dataLines.Add(("flags", $"0x{(uint)((f1 << 16) | (f2 << 8) | f3):X6}"));

        if (!Has(reader, end, 2))
        {
            Warn(dataLines, "Cannot read graphics_mode: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }

        dataLines.Add(("graphics_mode", $"0x{reader.ReadUInt16BE():X4}"));

        if (!Has(reader, end, 6))
        {
            Warn(dataLines, "Cannot read opcolor: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }

        dataLines.Add(("opcolor_red", $"0x{reader.ReadUInt16BE():X4}"));
        dataLines.Add(("opcolor_green", $"0x{reader.ReadUInt16BE():X4}"));
        dataLines.Add(("opcolor_blue", $"0x{reader.ReadUInt16BE():X4}"));

        if (!Has(reader, end, 2))
        {
            Warn(dataLines, "Cannot read balance: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }

        dataLines.Add(("balance", FormatFixed88(reader.ReadUInt16BE())));

        if (!Has(reader, end, 2))
        {
            Warn(dataLines, "Cannot read reserved: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }

        dataLines.Add(("reserved", $"0x{reader.ReadUInt16BE():X4}"));

        long unread = end - Pos(reader);
        if (unread > 0)
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");

        return Build(parser, node, title, dataLines);
    }

    // 8.8 fixed-point. The decimal form is shown because the stored bit pattern
    // is not directly readable as a balance value.
    private static string FormatFixed88(ushort raw)
    {
        double value = unchecked((short)raw) / 256.0;

        string text = Math.Abs(value - Math.Round(value)) < 1e-9
            ? Math.Round(value).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        return $"{text} (0x{raw:X4})";
    }

    private static void Seek(MpegReader reader, long pos) => reader.Seek(pos, SeekOrigin.Begin);
    private static long Pos(MpegReader reader) => (long)reader.Position;
    private static bool Has(MpegReader reader, long end, long n) => end - Pos(reader) >= n;
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