using System.Text;
using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// AuxiliaryTypeProperty 'auxC' -- item property inside 'iprp'/'ipco' (ISO/IEC 23008-12).
//
//   size         4 bytes
//   type         4 bytes   'auxC'
//   version      1 byte
//   flags        3 bytes
//   aux_type     string    null-terminated UTF-8
//   aux_subtype  bytes     the remainder of the box

internal static class AuxC
{
    private const int SubtypeHexLimit = 64;

    public static ParseResult Parse(MPEGParser parser, Node node)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        const string title = "AuxiliaryTypeProperty 'auxC'";

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

        // ---- aux_type ----
        if (TryReadString(reader, end, "aux_type", dataLines, out string auxType))
            dataLines.Add(("aux_type", Display(auxType)));

        // ---- aux_subtype: the rest of the box ----
        long subtypeLength = end - Pos(reader);
        dataLines.Add(("aux_subtype_size", subtypeLength.ToString()));

        if (subtypeLength > 0)
        {
            int take = (int)Math.Min(subtypeLength, SubtypeHexLimit);
            var bytes = new byte[take];

            for (int i = 0; i < take; i++)
                bytes[i] = reader.ReadByte();

            string hex = ToHex(bytes);
            if (subtypeLength > take)
                hex += $" ... ({subtypeLength} bytes total)";

            dataLines.Add(("aux_subtype", hex));

            Seek(reader, end);
        }

        return Build(parser, node, title, dataLines);
    }

    // ReadNullTerminatedString has no bounds checking, so:
    // check the remaining byte count before reading, and detect overrun afterwards.
    private static bool TryReadString(MpegReader reader, long limit, string field,
                                      List<(string K, string V)> dataLines, out string value)
    {
        value = string.Empty;

        if (limit - Pos(reader) <= 0)
        {
            Warn(dataLines, $"Missing '{field}': already at the end of the box.");
            return false;
        }

        value = reader.ReadNullTerminatedString() ?? string.Empty;

        long over = Pos(reader) - limit;
        if (over > 0)
        {
            Warn(dataLines, $"'{field}' has no NUL terminator; overran the box by {over} byte(s). " +
                            "Position was rolled back.");
            Seek(reader, limit);
            value = string.Empty;
            return false;
        }

        return true;
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

    private static string Display(string s)
    {
        if (string.IsNullOrEmpty(s)) return "<empty>";

        char[] chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] < 0x20 || chars[i] == 0x7F) chars[i] = '.';

        return new string(chars);
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