using System.Text;
using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// 3GPP asset meta-data boxes inside 'udta' (3GPP TS 26.244, clause 8).
//
//   size     4 bytes
//   type     4 bytes   'titl' / 'dscp' / 'perf' / 'albm' / 'cprt' / 'auth' / 'gnre' / 'coll'
//   version  1 byte
//   flags    3 bytes
//   pad      1 bit
//   language 15 bits   three 5-bit values, each = ASCII code - 0x60
//   text     string    null-terminated, UTF-8 or UTF-16 (UTF-16 starts with BOM 0xFEFF)
//   track    1 byte    'albm' only, optional
namespace UniversalParser.Src.Parser.MPEG.Boxes;
internal static class Titl
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ThreeGPPAsset.Parse(parser, node, "Title 'titl'", "title", false);
}

internal static class Dscp
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ThreeGPPAsset.Parse(parser, node, "Description 'dscp'", "description", false);
}

internal static class Perf
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ThreeGPPAsset.Parse(parser, node, "Performer 'perf'", "performer", false);
}

internal static class Auth
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ThreeGPPAsset.Parse(parser, node, "Author 'auth'", "author", false);
}

internal static class Gnre
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ThreeGPPAsset.Parse(parser, node, "Genre 'gnre'", "genre", false);
}

internal static class Cprt
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ThreeGPPAsset.Parse(parser, node, "Copyright 'cprt'", "copyright", false);
}

internal static class Coll
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ThreeGPPAsset.Parse(parser, node, "CollectionName 'coll'", "name", false);
}

internal static class Albm
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => ThreeGPPAsset.Parse(parser, node, "Album 'albm'", "album_title", true);
}

internal static class ThreeGPPAsset
{
    public static ParseResult Parse(MPEGParser parser, Node node, string title,
                                    string textField, bool hasTrackNumber)
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

        // ---- pad + language ----
        if (end - Pos(reader) < 2)
        {
            Warn(dataLines, $"Cannot read language: {end - Pos(reader)} byte(s) remaining, 2 required.");
            return Build(parser, node, title, dataLines);
        }

        ushort packedLanguage = reader.ReadUInt16BE();
        dataLines.Add(("language", FormatLanguage(packedLanguage)));

        // ---- text ----
        if (ReadText(reader, end, textField, dataLines, out string text, out string encodingName))
        {
            dataLines.Add(("text_encoding", encodingName));
            dataLines.Add((textField, Display(text)));
        }

        // ---- optional track number ('albm' only) ----
        if (hasTrackNumber)
        {
            if (end - Pos(reader) >= 1)
                dataLines.Add(("track_number", reader.ReadByte().ToString()));
            else
                dataLines.Add(("track_number", "<absent>"));
        }

        long unread = end - Pos(reader);
        if (unread > 0)
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");

        return Build(parser, node, title, dataLines);
    }

    // Packed as pad(1) + three 5-bit values, each value = ASCII code - 0x60.
    // The decoded form is shown because the raw 16-bit value is not readable as a language code.
    private static string FormatLanguage(ushort packed)
    {
        int c0 = (packed >> 10) & 0x1F;
        int c1 = (packed >> 5) & 0x1F;
        int c2 = packed & 0x1F;

        bool decodable = c0 >= 1 && c0 <= 26
                      && c1 >= 1 && c1 <= 26
                      && c2 >= 1 && c2 <= 26;

        if (!decodable)
            return $"0x{packed:X4}";

        string code = new string(new[] { (char)(0x60 + c0), (char)(0x60 + c1), (char)(0x60 + c2) });
        return $"{code} (0x{packed:X4})";
    }

    // The text is either UTF-8 or UTF-16; UTF-16 is signalled by a leading BOM.
    // ReadNullTerminatedString cannot be used here: a UTF-16 string would be cut at
    // the first 0x00 byte of its first ASCII character.
    private static bool ReadText(MpegReader reader, long end, string field,
                                 List<(string K, string V)> dataLines,
                                 out string text, out string encodingName)
    {
        text = string.Empty;
        encodingName = string.Empty;

        long fieldStart = Pos(reader);
        long available = end - fieldStart;

        if (available <= 0)
        {
            Warn(dataLines, $"Missing '{field}': already at the end of the box.");
            return false;
        }

        int b0 = reader.ReadByte();
        int b1 = available >= 2 ? reader.ReadByte() : -1;

        bool utf16Be = b0 == 0xFE && b1 == 0xFF;
        bool utf16Le = b0 == 0xFF && b1 == 0xFE;

        var bytes = new List<byte>();
        bool terminated = false;

        if (utf16Be || utf16Le)
        {
            encodingName = utf16Be ? "UTF-16BE (BOM 0xFEFF)" : "UTF-16LE (BOM 0xFFFE)";

            while (end - Pos(reader) >= 2)
            {
                byte hi = reader.ReadByte();
                byte lo = reader.ReadByte();

                if (hi == 0x00 && lo == 0x00)
                {
                    terminated = true;
                    break;
                }

                bytes.Add(hi);
                bytes.Add(lo);
            }

            text = (utf16Be ? Encoding.BigEndianUnicode : Encoding.Unicode)
                   .GetString(bytes.ToArray());
        }
        else
        {
            encodingName = "UTF-8";
            Seek(reader, fieldStart);

            while (Pos(reader) < end)
            {
                byte b = reader.ReadByte();
                if (b == 0x00)
                {
                    terminated = true;
                    break;
                }
                bytes.Add(b);
            }

            text = Encoding.UTF8.GetString(bytes.ToArray());
        }

        if (!terminated)
        {
            Warn(dataLines, $"'{field}' has no NUL terminator; decoded the remaining " +
                            $"{bytes.Count} byte(s) up to the end of the box.");
        }

        return true;
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