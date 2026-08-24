using System.Text;
using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// Timecode media information atom 'tcmi' -- found under 'gmhd' / 'tmcd'.
//
//   size              4 bytes
//   type              4 bytes   'tcmi'
//   version           1 byte
//   flags             3 bytes
//   text font         2 bytes
//   text face         2 bytes
//   text size         2 bytes
//   reserved          2 bytes
//   text color        6 bytes   three 16-bit values: red, green, blue
//   background color  6 bytes   three 16-bit values: red, green, blue
//   font name         Pascal string (one length byte followed by that many bytes)

internal static class Tcmi
{
    public static ParseResult Parse(MPEGParser parser, Node node)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        const string title = "TimecodeMediaInformationAtom 'tcmi'";

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
            Warn(dataLines, "Cannot read text_font: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }
        dataLines.Add(("text_font", reader.ReadUInt16BE().ToString()));

        if (!Has(reader, end, 2))
        {
            Warn(dataLines, "Cannot read text_face: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }
        dataLines.Add(("text_face", $"0x{reader.ReadUInt16BE():X4}"));

        if (!Has(reader, end, 2))
        {
            Warn(dataLines, "Cannot read text_size: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }
        dataLines.Add(("text_size", reader.ReadUInt16BE().ToString()));

        if (!Has(reader, end, 2))
        {
            Warn(dataLines, "Cannot read reserved: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }
        dataLines.Add(("reserved", $"0x{reader.ReadUInt16BE():X4}"));

        if (!Has(reader, end, 6))
        {
            Warn(dataLines, "Cannot read text_color: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }
        dataLines.Add(("text_color_red", $"0x{reader.ReadUInt16BE():X4}"));
        dataLines.Add(("text_color_green", $"0x{reader.ReadUInt16BE():X4}"));
        dataLines.Add(("text_color_blue", $"0x{reader.ReadUInt16BE():X4}"));

        if (!Has(reader, end, 6))
        {
            Warn(dataLines, "Cannot read background_color: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }
        dataLines.Add(("background_color_red", $"0x{reader.ReadUInt16BE():X4}"));
        dataLines.Add(("background_color_green", $"0x{reader.ReadUInt16BE():X4}"));
        dataLines.Add(("background_color_blue", $"0x{reader.ReadUInt16BE():X4}"));

        // ---- font name: Pascal string, may be absent when the atom ends here ----
        if (Has(reader, end, 1))
        {
            byte nameLength = reader.ReadByte();
            long available = end - Pos(reader);

            if (nameLength > available)
            {
                Warn(dataLines, $"font_name declares {nameLength} byte(s) but only {available} remain; " +
                                "reading the available bytes only.");
                nameLength = (byte)available;
            }

            var sb = new StringBuilder(nameLength);
            for (int i = 0; i < nameLength; i++)
            {
                byte b = reader.ReadByte();
                sb.Append(b >= 0x20 && b != 0x7F ? (char)b : '.');
            }

            dataLines.Add(("font_name", sb.ToString()));
        }

        long unread = end - Pos(reader);
        if (unread > 0)
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");

        return Build(parser, node, title, dataLines);
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