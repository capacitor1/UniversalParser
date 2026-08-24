using System.Text;
using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// QuickTime / iTunes metadata item atoms such as '\xA9aut', 'keyw', 'desc'.
//
// Two layouts occur for the same 4CC depending on where the atom sits:
//
// 1. One or more nested 'data' boxes carry the value:
//
//      size 4 | type 4 | [ nested 'data' box ] repeated
//
//    The 'data' box, called the value atom in the QuickTime specification, is:
//
//      size 4 | type 4 ('data') | reserved 1 | well_known_type 3
//             | country 2 | language 2 | value
//
// 2. International text. Every user data type starting with the character at ASCII 169
//    is a list of text strings with language codes, stored in the small atom format:
//    16-bit size and 16-bit language instead of 32-bit.
//
//      size 4 | type 4 | [ uint16 text_size | uint16 language | text ] repeated
//
// If neither layout applies the payload is reported as a hex dump.
//
// IMPORTANT: MpegReader follows the position of the FileStream it wraps, so a single
// reader instance is used throughout and every probe seeks back to where it started.
// Creating a second reader on the same stream silently moves the first one.

internal static class MetadataText
{
    private const int HexLimit = 64;
    private const int MinDataBoxSize = 16;   // header 8 + type indicator 4 + locale indicator 4

    public static ParseResult Parse(MPEGParser parser, Node node, string fourCC, string title)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        string boxTitle = $"{title} '{EscapeFourCC(fourCC)}'";

        fs.Position = start;
        Seek(reader, start);

        // ---- Box header: size/type were already validated by MPEGParser,
        //      so only the correct header length has to be skipped here.
        if (end - start < 8)
        {
            Warn(dataLines, $"Box is only {end - start} byte(s) long, too short for a box header.");
            return Build(parser, node, boxTitle, dataLines);
        }

        int headerSize = 8;
        uint size32 = reader.ReadUInt32BE();
        reader.ReadFourCC();

        if (size32 == 1 && end - start >= 16)
        {
            reader.ReadUInt64BE();
            headerSize = 16;
        }

        long payload = start + headerSize;

        if (payload >= end)
        {
            dataLines.Add(("payload_size", "0"));
            return Build(parser, node, boxTitle, dataLines);
        }

        // ---- Layout 1: nested 'data' boxes ----
        if (StartsWithDataBox(reader, payload, end))
        {
            Seek(reader, payload);

            int index = 0;
            while (StartsWithDataBox(reader, Pos(reader), end))
            {
                long boxStart = Pos(reader);
                long boxEnd = ParseDataBox(reader, end, index, dataLines);

                if (boxEnd <= boxStart) break;     // no progress, stop instead of looping forever

                Seek(reader, boxEnd);
                index++;
            }

            dataLines.Add(("data_box_count", index.ToString()));
        }
        // ---- Layout 2: international text list ----
        else if (LooksLikeTextList(reader, payload, end))
        {
            Seek(reader, payload);
            ParseTextList(reader, end, dataLines);
        }
        // ---- Unrecognised payload ----
        else
        {
            Seek(reader, payload);

            long length = end - payload;
            dataLines.Add(("payload_size", length.ToString()));
            dataLines.Add(("payload", ReadHex(reader, end, length)));
        }

        long unread = end - Pos(reader);
        if (unread > 0)
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");

        return Build(parser, node, boxTitle, dataLines);
    }

    // Entry point for a node that is itself a 'data' box, which happens when the
    // containing item atom was already expanded into child nodes.
    public static ParseResult ParseDataNode(MPEGParser parser, Node node)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        const string title = "Value Atom 'data'";

        fs.Position = start;
        Seek(reader, start);

        if (end - start < 8)
        {
            Warn(dataLines, $"Box is only {end - start} byte(s) long, too short for a box header.");
            return Build(parser, node, title, dataLines);
        }

        ParseDataBox(reader, end, -1, dataLines);

        long unread = end - Pos(reader);
        if (unread > 0)
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");

        return Build(parser, node, title, dataLines);
    }

    // ==================== Probes: position is always restored ====================

    private static bool StartsWithDataBox(MpegReader reader, long position, long end)
    {
        if (end - position < MinDataBoxSize) return false;

        Seek(reader, position);
        uint size = reader.ReadUInt32BE();
        string type = reader.ReadFourCC();
        Seek(reader, position);

        return type == "data" && size >= MinDataBoxSize && position + size <= end;
    }

    private static bool LooksLikeTextList(MpegReader reader, long payload, long end)
    {
        long available = end - payload;
        if (available < 4) return false;

        Seek(reader, payload);
        ushort textSize = reader.ReadUInt16BE();
        reader.ReadUInt16BE();
        Seek(reader, payload);

        // Writers disagree on whether text_size counts the 4-byte entry header.
        return textSize <= available - 4 || textSize <= available;
    }

    // ==================== Layout 1: 'data' box ====================

    /// <returns>The absolute end offset of the parsed 'data' box.</returns>
    private static long ParseDataBox(MpegReader reader, long limit, int index,
                                     List<(string K, string V)> dataLines)
    {
        long boxStart = Pos(reader);
        string prefix = index < 0 ? string.Empty : $"data[{index}].";

        uint size = reader.ReadUInt32BE();
        reader.ReadFourCC();

        long boxEnd = size == 0 ? limit : Math.Min(boxStart + size, limit);

        if (boxEnd - Pos(reader) < 8)
        {
            Warn(dataLines, $"Cannot read the type and locale indicators: " +
                            $"{boxEnd - Pos(reader)} byte(s) remaining, 8 required.");
            return boxEnd;
        }

        byte reserved = reader.ReadByte();
        byte t1 = reader.ReadByte();
        byte t2 = reader.ReadByte();
        byte t3 = reader.ReadByte();

        uint wellKnownType = (uint)((t1 << 16) | (t2 << 8) | t3);

        ushort country = reader.ReadUInt16BE();
        ushort language = reader.ReadUInt16BE();

        dataLines.Add(($"{prefix}reserved", $"0x{reserved:X2}"));
        dataLines.Add(($"{prefix}well_known_type", wellKnownType.ToString()));
        dataLines.Add(($"{prefix}country_indicator", country.ToString()));
        dataLines.Add(($"{prefix}language_indicator", FormatLanguage(language)));

        long valueLength = boxEnd - Pos(reader);
        dataLines.Add(($"{prefix}value_size", valueLength.ToString()));

        if (valueLength <= 0) return boxEnd;

        // The well-known type states how the bytes encode the value, so it has to be
        // applied for the value to be readable at all.
        if (wellKnownType == 1 || wellKnownType == 4)
        {
            dataLines.Add(($"{prefix}value", Display(ReadString(reader, boxEnd, Encoding.UTF8))));
        }
        else if (wellKnownType == 2 || wellKnownType == 5)
        {
            dataLines.Add(($"{prefix}value", Display(ReadString(reader, boxEnd, Encoding.BigEndianUnicode))));
        }
        else if (IsSignedIntegerType(wellKnownType))
        {
            dataLines.Add(($"{prefix}value", ReadInteger(reader, boxEnd, true)));
        }
        else if (IsUnsignedIntegerType(wellKnownType))
        {
            dataLines.Add(($"{prefix}value", ReadInteger(reader, boxEnd, false)));
        }
        else
        {
            dataLines.Add(($"{prefix}value", ReadHex(reader, boxEnd, valueLength)));
        }

        return boxEnd;
    }

    private static bool IsSignedIntegerType(uint t)
        => t == 21 || t == 65 || t == 66 || t == 67 || t == 74;

    private static bool IsUnsignedIntegerType(uint t)
        => t == 22 || t == 75 || t == 76 || t == 77 || t == 78;

    private static string ReadInteger(MpegReader reader, long limit, bool signed)
    {
        long available = limit - Pos(reader);
        if (available <= 0 || available > 8)
            return ReadHex(reader, limit, available);

        ulong value = 0;
        for (long i = 0; i < available; i++)
            value = (value << 8) | reader.ReadByte();

        if (!signed) return value.ToString();

        int bits = (int)(available * 8);
        if (bits < 64 && (value & (1UL << (bits - 1))) != 0)
            return ((long)(value | (ulong.MaxValue << bits))).ToString();

        return ((long)value).ToString();
    }

    // ==================== Layout 2: international text list ====================

    private static void ParseTextList(MpegReader reader, long limit, List<(string K, string V)> dataLines)
    {
        int index = 0;

        while (limit - Pos(reader) >= 4)
        {
            ushort textSize = reader.ReadUInt16BE();
            ushort language = reader.ReadUInt16BE();

            long available = limit - Pos(reader);
            long length = textSize;

            // Some writers store the text length alone, others include the 4-byte
            // entry header. Pick whichever fits the remaining bytes.
            if (length > available && textSize >= 4 && textSize - 4 <= available)
                length = textSize - 4;

            if (length > available)
            {
                Warn(dataLines, $"entry[{index}] declares {textSize} byte(s) of text but only " +
                                $"{available} byte(s) remain; the rest was decoded.");
                length = available;
            }

            var bytes = new byte[length];
            for (long i = 0; i < length; i++)
                bytes[i] = reader.ReadByte();

            dataLines.Add(($"entry[{index}].text_size", textSize.ToString()));
            dataLines.Add(($"entry[{index}].language", FormatLanguage(language)));
            dataLines.Add(($"entry[{index}].text", Display(DecodeText(bytes))));

            index++;

            if (length == 0) break;   // no progress, stop instead of looping forever
        }

        dataLines.Add(("entry_count", index.ToString()));
    }

    // ==================== Shared helpers ====================

    // A leading byte order mark selects UTF-16; otherwise the text is UTF-8.
    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        return Encoding.UTF8.GetString(bytes);
    }

    private static string ReadString(MpegReader reader, long limit, Encoding encoding)
    {
        long length = limit - Pos(reader);
        if (length <= 0) return string.Empty;

        var bytes = new byte[length];
        for (long i = 0; i < length; i++)
            bytes[i] = reader.ReadByte();

        return encoding.GetString(bytes);
    }

    private static string ReadHex(MpegReader reader, long limit, long length)
    {
        long take = Math.Min(Math.Min(length, limit - Pos(reader)), HexLimit);
        if (take <= 0) return string.Empty;

        var sb = new StringBuilder((int)take * 3);
        for (long i = 0; i < take; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(reader.ReadByte().ToString("X2"));
        }

        if (length > take)
            sb.Append($" ... ({length} bytes total)");

        return sb.ToString();
    }

    // Values of 0x400 and above are packed ISO-639-2/T codes: three 5-bit values, each
    // equal to the ASCII code minus 0x60. Lower values are Macintosh region codes.
    private static string FormatLanguage(ushort packed)
    {
        if (packed < 0x400) return packed.ToString();

        int c0 = (packed >> 10) & 0x1F;
        int c1 = (packed >> 5) & 0x1F;
        int c2 = packed & 0x1F;

        bool decodable = c0 >= 1 && c0 <= 26
                      && c1 >= 1 && c1 <= 26
                      && c2 >= 1 && c2 <= 26;

        if (!decodable) return $"0x{packed:X4}";

        string code = new string(new[] { (char)(0x60 + c0), (char)(0x60 + c1), (char)(0x60 + c2) });
        return $"{code} (0x{packed:X4})";
    }

    private static string EscapeFourCC(string s)
    {
        /*
        if (string.IsNullOrEmpty(s)) return string.Empty;

        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c >= 0x20 && c <= 0x7E) sb.Append(c);
            else sb.Append("\\x").Append(((int)c & 0xFF).ToString("X2"));
        }
        return sb.ToString();
        */
        return s;
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

internal static class DataBox
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => MetadataText.ParseDataNode(parser, node);
}