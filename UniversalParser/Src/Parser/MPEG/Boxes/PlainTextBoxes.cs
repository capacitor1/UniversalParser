using System.Text;
using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// Plain text user data boxes such as 'clfn', and the Keywords box 'kywd'.
//
// Boxes like 'clfn' carry nothing but a text string:
//
//   size 4 | type 4 | text
//
// Some writers prepend a zero version and zero flags, so a leading run of four zero
// bytes is reported as a full box header and skipped. A UTF-8 string never starts
// with 0x00, so this test has no false positives.
//
// The Keywords box 'kywd' is specified by 3GPP TS 26.244 table 8.9 as:
//
//   size 4 | type 4 | version 1 | flags 3 | pad(1) + language(15) 2 | keyword_count 1
//          | [ keyword_size 1 | keyword string ] repeated keyword_count times
//
// In practice many writers store a plain text string instead, so both layouts are
// handled and selected by the same leading-zero test.
namespace UniversalParser.Src.Parser.MPEG.Boxes;
internal static class PlainTextBox
{
    public static ParseResult Parse(MPEGParser parser, Node node, string fourCC, string title)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        string boxTitle = $"{title} '{fourCC}'";

        long payload = SkipBoxHeader(reader, fs, start, end, dataLines);
        if (payload < 0) return Build(parser, node, boxTitle, dataLines);

        ReadFullBoxPrefixIfPresent(reader, end, dataLines);
        ReadTextToEnd(reader, end, "text", dataLines);

        return Build(parser, node, boxTitle, dataLines);
    }

    // ==================== Shared helpers ====================

    /// <returns>The absolute offset of the payload, or -1 when the header is unusable.</returns>
    internal static long SkipBoxHeader(MpegReader reader, FileStream fs, long start, long end,
                                       List<(string K, string V)> dataLines)
    {
        fs.Position = start;
        Seek(reader, start);

        if (end - start < 8)
        {
            Warn(dataLines, $"Box is only {end - start} byte(s) long, too short for a box header.");
            return -1;
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
        Seek(reader, payload);
        return payload;
    }

    /// <returns>True when a zero version/flags pair was present and consumed.</returns>
    internal static bool ReadFullBoxPrefixIfPresent(MpegReader reader, long end,
                                                   List<(string K, string V)> dataLines)
    {
        long position = Pos(reader);
        if (end - position < 4) return false;

        byte b0 = reader.ReadByte();
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        byte b3 = reader.ReadByte();

        if (b0 == 0 && b1 == 0 && b2 == 0 && b3 == 0)
        {
            dataLines.Add(("version", "0"));
            dataLines.Add(("flags", "0x000000"));
            return true;
        }

        Seek(reader, position);
        return false;
    }

    internal static void ReadTextToEnd(MpegReader reader, long end, string field,
                                       List<(string K, string V)> dataLines)
    {
        long length = end - Pos(reader);
        dataLines.Add(($"{field}_size", length.ToString()));

        if (length <= 0) return;

        var bytes = new byte[length];
        for (long i = 0; i < length; i++)
            bytes[i] = reader.ReadByte();

        dataLines.Add((field, Display(DecodeText(bytes))));
    }

    // A leading byte order mark selects UTF-16; otherwise the text is UTF-8.
    // A single trailing NUL is dropped so that terminated and unterminated
    // strings render identically.
    internal static string DecodeText(byte[] bytes)
    {
        int length = bytes.Length;

        if (length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            if (length >= 4 && bytes[length - 1] == 0 && bytes[length - 2] == 0) length -= 2;
            return Encoding.BigEndianUnicode.GetString(bytes, 2, length - 2);
        }

        if (length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            if (length >= 4 && bytes[length - 1] == 0 && bytes[length - 2] == 0) length -= 2;
            return Encoding.Unicode.GetString(bytes, 2, length - 2);
        }

        if (length >= 1 && bytes[length - 1] == 0) length -= 1;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    internal static string Display(string s)
    {
        if (string.IsNullOrEmpty(s)) return "<empty>";

        char[] chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] < 0x20 || chars[i] == 0x7F) chars[i] = '.';

        return new string(chars);
    }

    internal static void Seek(MpegReader reader, long pos) => reader.Seek(pos, SeekOrigin.Begin);
    internal static long Pos(MpegReader reader) => (long)reader.Position;
    internal static void Warn(List<(string K, string V)> lines, string msg) => lines.Add(("warning", msg));

    internal static ParseResult Build(MPEGParser parser, Node node, string title,
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

internal static class Kywd
{
    public static ParseResult Parse(MPEGParser parser, Node node)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        const string title = "KeywordsBox 'kywd'";

        long payload = PlainTextBox.SkipBoxHeader(reader, fs, start, end, dataLines);
        if (payload < 0) return PlainTextBox.Build(parser, node, title, dataLines);

        // Layout 2: no full box header, the whole payload is text.
        if (!PlainTextBox.ReadFullBoxPrefixIfPresent(reader, end, dataLines))
        {
            PlainTextBox.ReadTextToEnd(reader, end, "text", dataLines);
            return PlainTextBox.Build(parser, node, title, dataLines);
        }

        // Layout 1: the structured form defined by 3GPP TS 26.244 table 8.9.
        if (end - Pos(reader) < 3)
        {
            Warn(dataLines, $"Cannot read language/keyword_count: " +
                            $"{end - Pos(reader)} byte(s) remaining, 3 required.");
            return PlainTextBox.Build(parser, node, title, dataLines);
        }

        ushort packedLanguage = reader.ReadUInt16BE();
        byte keywordCount = reader.ReadByte();

        dataLines.Add(("language", FormatLanguage(packedLanguage)));
        dataLines.Add(("keyword_count", keywordCount.ToString()));

        for (int i = 0; i < keywordCount; i++)
        {
            if (end - Pos(reader) < 1)
            {
                Warn(dataLines, $"Read {i} of {keywordCount} keyword(s); the box ends early.");
                break;
            }

            byte keywordSize = reader.ReadByte();
            long available = end - Pos(reader);
            long length = keywordSize;

            if (length > available)
            {
                Warn(dataLines, $"keyword[{i}] declares {keywordSize} byte(s) but only " +
                                $"{available} byte(s) remain; the rest was decoded.");
                length = available;
            }

            var bytes = new byte[length];
            for (long b = 0; b < length; b++)
                bytes[b] = reader.ReadByte();

            dataLines.Add(($"keyword[{i}].size", keywordSize.ToString()));
            dataLines.Add(($"keyword[{i}].text", PlainTextBox.Display(PlainTextBox.DecodeText(bytes))));

            if (length == 0) break;   // no progress, stop instead of looping forever
        }

        long unread = end - Pos(reader);
        if (unread > 0)
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");

        return PlainTextBox.Build(parser, node, title, dataLines);
    }

    // Packed as pad(1) + three 5-bit values, each value = ASCII code - 0x60.
    // The decoded form is shown because the raw 16-bit value is not readable
    // as a language code.
    private static string FormatLanguage(ushort packed)
    {
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

    private static long Pos(MpegReader reader) => (long)reader.Position;
    private static void Warn(List<(string K, string V)> lines, string msg) => lines.Add(("warning", msg));
}