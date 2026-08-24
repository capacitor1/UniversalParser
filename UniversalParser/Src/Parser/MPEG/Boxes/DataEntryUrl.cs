using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// DataEntryUrlBox / DataEntryUrnBox -- children of 'dref'.
// NOTE: the 4CCs carry a trailing space. Dispatcher keys must be "url " / "urn ",
// not "url" / "urn".
internal static class Url
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => DataEntryBox.Parse(parser, node, isUrn: false);
}

internal static class Urn
{
    public static ParseResult Parse(MPEGParser parser, Node node)
        => DataEntryBox.Parse(parser, node, isUrn: true);
}

internal static class DataEntryBox
{
    // entry_flags bit 0: media data is in the same file as the Movie Box.
    // When set, no string field is present at all.
    private const uint FlagSelfContained = 0x000001;

    public static ParseResult Parse(MPEGParser parser, Node node, bool isUrn)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        string title = isUrn ? "DataEntryUrnBox 'urn '" : "DataEntryUrlBox 'url '";

        fs.Position = start;
        Seek(reader, start);

        // ---- Box header: size/type were already validated by MPEGParser,
        //      so only the correct header length has to be skipped here.
        int headerSize = 8;
        if (end - start >= 8)
        {
            uint size32 = reader.ReadUInt32BE();
            reader.ReadFourCC();
            if (size32 == 1 && end - start >= 16)
            {
                reader.ReadUInt64BE();
                headerSize = 16;
            }
        }
        else
        {
            Warn(dataLines, $"Box is only {end - start} byte(s) long, too short for a box header.");
            return Build(parser, node, title, dataLines);
        }

        Seek(reader, start + headerSize);

        // ---- FullBox ----
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
        bool selfContained = (flags & FlagSelfContained) != 0;

        dataLines.Add(("version", version.ToString()));
        dataLines.Add(("flags", $"0x{flags:X6}"));
        dataLines.Add(("self_contained",
            selfContained ? "1 (media data is in this file)" : "0 (references an external file)"));

        if (version != 0)
            Warn(dataLines, $"Spec requires version = 0, got {version}; parsing as version 0 anyway.");

        uint unknownFlags = flags & ~FlagSelfContained;
        if (unknownFlags != 0)
            Warn(dataLines, $"Undefined flag bits set: 0x{unknownFlags:X6}");

        long remaining = end - Pos(reader);

        // *** Key point: when self-contained, the box ends here and carries no string. ***
        if (selfContained)
        {
            dataLines.Add((isUrn ? "name" : "location", "<absent: self_contained>"));

            if (remaining > 0)
            {
                Warn(dataLines, $"self_contained is set but {remaining} trailing byte(s) remain. " +
                                "Some writers emit the string regardless; see the raw data below.");
            }

            return Build(parser, node, title, dataLines);
        }

        if (remaining <= 0)
        {
            string field = isUrn ? "name" : "location";
            Warn(dataLines, $"self_contained is not set, but '{field}' is missing " +
                            "(box ends right after the flags field).");
            return Build(parser, node, title, dataLines);
        }

        // ---- String fields ----
        if (isUrn)
        {
            // 'urn ': name is mandatory, location is optional.
            if (TryReadString(reader, end, "name", dataLines, out string name))
                dataLines.Add(("name", Display(name)));

            if (Pos(reader) < end &&
                TryReadString(reader, end, "location", dataLines, out string location))
                dataLines.Add(("location", Display(location)));
        }
        else
        {
            if (TryReadString(reader, end, "location", dataLines, out string location))
                dataLines.Add(("location", Display(location)));
        }

        long unread = end - Pos(reader);
        if (unread > 0)
        {
            // Common causes: zero padding after the string, or the writer stored an oversized box size.
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");
        }

        return Build(parser, node, title, dataLines);
    }

    // ==================== Helpers (kept consistent with Infe.cs) ====================

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

    private static string Display(string s)
        => string.IsNullOrEmpty(s) ? "<empty>" : Printable(s);

    // Only control characters are masked. Non-ASCII characters are kept, because
    // 'location' is a UTF-8 string by spec and may legitimately contain them.
    private static string Printable(string s)
    {
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