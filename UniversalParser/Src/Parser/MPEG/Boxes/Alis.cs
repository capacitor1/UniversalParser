using System.Globalization;
using System.Text;
using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// DataEntryAliasBox 'alis' -- QuickTime alias data reference, found under dinf/dref.
//
// Outer shell:
//   FullBox('alis', version = 0, flags)
//   flags bit 0 (0x000001) = self-contained, same meaning as in 'url '.
//   The payload, when present, is a Macintosh Alias Manager record (all big endian).
//
// Alias record common header:
//   offset 0  size 4  application specific four-character code
//   offset 4  size 2  record_size (counts the whole alias record; >= 150 for version 2)
//   offset 6  size 2  version (2 or 3; the fixed part differs between them)
//
// The fixed part is followed by optional tag-length-value entries:
//   tag(2, signed) + length(2) + value[length], padded to an even length.
//   tag -1 terminates the list. Tag 18 (POSIX path) is normally the readable one.
internal static class Alis
{
    private const uint FlagSelfContained = 0x000001;

    private const int RecordHeaderLen = 8;    // creator code + record_size + version
    private const int V2FixedEnd = 150;       // end of the version 2 fixed part
    private const int V3FixedEnd = 58;        // end of the version 3 fixed part
    private const int TagHeaderLen = 4;       // tag + length
    private const short TagEndOfList = -1;

    private const int MaxTextLen = 4096;      // sanity cap for one string field
    private const int MaxHexBytes = 32;       // bytes shown for opaque tag values
    private const int MaxCnidCount = 256;     // sanity cap for a CNID path
    private const int MaxTagCount = 256;      // sanity cap for a corrupt tag list

    private static readonly DateTime MacEpoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static ParseResult Parse(MPEGParser parser, Node node)
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
            return Build(parser, node, dataLines);
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

        // ---- FullBox ----
        if (end - Pos(reader) < 4)
        {
            Warn(dataLines, "Cannot read version/flags: not enough bytes.");
            return Build(parser, node, dataLines);
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

        if (remaining <= 0)
        {
            dataLines.Add(("alias_record", "<absent>"));
            if (!selfContained)
                Warn(dataLines, "self_contained is not set, but no alias record follows the flags field.");
            return Build(parser, node, dataLines);
        }

        // An alias record is present. Some writers emit one even when self_contained is set,
        // so it is parsed either way rather than being skipped.
        if (selfContained)
            Warn(dataLines, "self_contained is set, yet an alias record is present. Reporting it anyway.");

        ParseAliasRecord(reader, end, dataLines);

        long tail = end - Pos(reader);
        if (tail > 0)
            Warn(dataLines, $"{tail} trailing byte(s) were not interpreted.");

        return Build(parser, node, dataLines);
    }

    // ==================== Alias record ====================

    private static void ParseAliasRecord(MpegReader reader, long limit, List<(string K, string V)> dataLines)
    {
        long recordStart = Pos(reader);

        if (limit - recordStart < RecordHeaderLen)
        {
            Warn(dataLines, $"Alias record is only {limit - recordStart} byte(s) long, " +
                            "too short for its header.");
            return;
        }

        string creator = reader.ReadFourCC();
        int recordSize = reader.ReadUInt16BE();
        int recordVersion = reader.ReadUInt16BE();

        dataLines.Add(("alias.creator_code", creator == "\0\0\0\0" ? "<none>" : Display(creator)));
        dataLines.Add(("alias.record_size", recordSize.ToString()));
        dataLines.Add(("alias.version", recordVersion.ToString()));

        long recordEnd = recordStart + recordSize;
        if (recordSize < RecordHeaderLen || recordEnd > limit)
        {
            Warn(dataLines, $"record_size {recordSize} is unusable " +
                            $"({limit - recordStart} byte(s) available). Clamped to the box end.");
            recordEnd = limit;
        }

        int fixedEnd;
        if (recordVersion == 2)
        {
            fixedEnd = V2FixedEnd;
            if (recordSize < V2FixedEnd)
                Warn(dataLines, $"Version 2 alias records must be at least {V2FixedEnd} bytes, got {recordSize}.");
        }
        else if (recordVersion == 3)
        {
            fixedEnd = V3FixedEnd;
        }
        else
        {
            Warn(dataLines, $"Unsupported alias record version {recordVersion}. " +
                            "The fixed part cannot be located, so the tag list is not read.");
            return;
        }

        long fixedPartEnd = recordStart + fixedEnd;
        if (fixedPartEnd > recordEnd)
        {
            Warn(dataLines, $"The version {recordVersion} fixed part needs {fixedEnd} bytes, " +
                            $"but the record holds only {recordEnd - recordStart}.");
            return;
        }

        if (recordVersion == 2)
            ParseV2Fixed(reader, dataLines);
        else
            ParseV3Fixed(reader, dataLines);

        Seek(reader, fixedPartEnd);   // align to the end of the fixed part

        ParseTags(reader, recordEnd, dataLines);

        Seek(reader, recordEnd);      // align to the declared record end
    }

    private static void ParseV2Fixed(MpegReader reader, List<(string K, string V)> dataLines)
    {
        int kind = reader.ReadUInt16BE();
        string volumeName = ReadPascalString(reader, 28);
        uint volumeDate = reader.ReadUInt32BE();
        string fsType = reader.ReadFourCCHalf();
        int diskType = reader.ReadUInt16BE();
        uint parentCnid = reader.ReadUInt32BE();
        string targetName = ReadPascalString(reader, 64);
        uint targetCnid = reader.ReadUInt32BE();
        uint targetDate = reader.ReadUInt32BE();
        string creatorCode = reader.ReadFourCC();
        string typeCode = reader.ReadFourCC();
        short levelsToRoot = (short)reader.ReadUInt16BE();
        short levelsToTarget = (short)reader.ReadUInt16BE();
        uint volumeAttributes = reader.ReadUInt32BE();
        int volumeFsId = reader.ReadUInt16BE();

        dataLines.Add(("alias.kind", DescribeKind(kind)));
        dataLines.Add(("alias.volume_name", Display(volumeName)));
        dataLines.Add(("alias.volume_date", FormatMacDate(volumeDate)));
        dataLines.Add(("alias.filesystem_type", Display(fsType)));
        dataLines.Add(("alias.disk_type", DescribeDiskType(diskType)));
        dataLines.Add(("alias.parent_cnid", parentCnid.ToString()));
        dataLines.Add(("alias.target_name", Display(targetName)));
        dataLines.Add(("alias.target_cnid", targetCnid.ToString()));
        dataLines.Add(("alias.target_date", FormatMacDate(targetDate)));
        dataLines.Add(("alias.target_creator", creatorCode == "\0\0\0\0" ? "<none>" : Display(creatorCode)));
        dataLines.Add(("alias.target_type", typeCode == "\0\0\0\0" ? "<none>" : Display(typeCode)));
        dataLines.Add(("alias.levels_to_root", levelsToRoot.ToString()));
        dataLines.Add(("alias.levels_to_target", levelsToTarget.ToString()));
        dataLines.Add(("alias.volume_attributes", $"0x{volumeAttributes:X8}"));
        dataLines.Add(("alias.volume_fs_id", $"0x{volumeFsId:X4}"));
        // The remaining 10 reserved bytes are skipped by the caller.
    }

    private static void ParseV3Fixed(MpegReader reader, List<(string K, string V)> dataLines)
    {
        int kind = reader.ReadUInt16BE();
        ulong volumeDate = reader.ReadUInt64BE();
        string fsType = reader.ReadFourCC();
        int diskType = reader.ReadUInt16BE();
        uint parentCnid = reader.ReadUInt32BE();
        uint targetCnid = reader.ReadUInt32BE();
        ulong targetDate = reader.ReadUInt64BE();
        uint volumeAttributes = reader.ReadUInt32BE();

        dataLines.Add(("alias.kind", DescribeKind(kind)));
        dataLines.Add(("alias.volume_date", FormatMacDateHiRes(volumeDate)));
        dataLines.Add(("alias.filesystem_type", Display(fsType)));
        dataLines.Add(("alias.disk_type", DescribeDiskType(diskType)));
        dataLines.Add(("alias.parent_cnid", parentCnid.ToString()));
        dataLines.Add(("alias.target_cnid", targetCnid.ToString()));
        dataLines.Add(("alias.target_date", FormatMacDateHiRes(targetDate)));
        dataLines.Add(("alias.volume_attributes", $"0x{volumeAttributes:X8}"));
        // The remaining 14 reserved bytes are skipped by the caller.
        // Version 3 carries no fixed name fields; the target name lives in tag 14 or tag 18.
    }

    // ==================== Tag-length-value list ====================

    private static void ParseTags(MpegReader reader, long recordEnd, List<(string K, string V)> dataLines)
    {
        int seen = 0;

        while (Pos(reader) + TagHeaderLen <= recordEnd)
        {
            if (++seen > MaxTagCount)
            {
                Warn(dataLines, $"More than {MaxTagCount} tags encountered; stopping. Data may be corrupt.");
                return;
            }

            short tag = (short)reader.ReadUInt16BE();
            int length = reader.ReadUInt16BE();

            if (tag == TagEndOfList)
                return;

            long valueStart = Pos(reader);
            long valueEnd = valueStart + length;

            if (valueEnd > recordEnd)
            {
                Warn(dataLines, $"tag {tag}: declared length {length} runs past the end of the alias record. " +
                                "Clamped.");
                valueEnd = recordEnd;
            }

            RenderTag(reader, tag, valueStart, valueEnd, dataLines);

            // A value of odd length is followed by one pad byte.
            long next = valueStart + length + (length & 1);
            Seek(reader, Math.Min(next, recordEnd));
        }
    }

    private static void RenderTag(MpegReader reader, short tag, long valueStart, long valueEnd,
                                  List<(string K, string V)> dataLines)
    {
        long count = valueEnd - valueStart;
        string key = $"alias.tag[{tag}] {TagName(tag)}";

        switch (tag)
        {
            // Carbon era strings. MacRoman by definition; decoded as Latin-1 here, which
            // matches for every ASCII character and needs no extra encoding provider.
            case 0:
            case 2:
            case 3:
            case 4:
            case 5:
            case 6:
                dataLines.Add((key, Display(ReadText(reader, count, Encoding.Latin1, tag, dataLines))));
                break;

            // POSIX paths are UTF-8 on macOS.
            case 18:
            case 19:
                dataLines.Add((key, Display(ReadText(reader, count, Encoding.UTF8, tag, dataLines))));
                break;

            // UTF-16 big endian strings.
            case 14:
            case 15:
                dataLines.Add((key, Display(ReadText(reader, count, Encoding.BigEndianUnicode, tag, dataLines))));
                break;

            // CNID path: one 32-bit CNID per directory level.
            case 1:
            {
                long entries = count / 4;
                if (entries > MaxCnidCount)
                {
                    Warn(dataLines, $"tag {tag}: {entries} CNIDs declared, only the first {MaxCnidCount} are shown.");
                    entries = MaxCnidCount;
                }

                var sb = new StringBuilder();
                for (long i = 0; i < entries; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(reader.ReadUInt32BE());
                }
                dataLines.Add((key, entries == 0 ? "<empty>" : sb.ToString()));
                break;
            }

            // High resolution dates: 65536ths of a second since the Mac epoch.
            case 16:
            case 17:
                if (count >= 8)
                    dataLines.Add((key, FormatMacDateHiRes(reader.ReadUInt64BE())));
                else
                    Warn(dataLines, $"tag {tag}: expected 8 bytes for a high resolution date, got {count}.");
                break;

            // Number of directory levels up to the user home folder.
            case 21:
                if (count >= 2)
                    dataLines.Add((key, ((short)reader.ReadUInt16BE()).ToString()));
                else
                    Warn(dataLines, $"tag {tag}: expected 2 bytes, got {count}.");
                break;

            // A nested alias record. Reported but not expanded, to keep this box a single leaf.
            case 20:
                dataLines.Add((key, $"<nested alias record, {count} byte(s), not expanded>"));
                break;

            // Opaque or unregistered payloads.
            default:
                dataLines.Add((key, $"{count} byte(s): {ReadHex(reader, count, dataLines)}"));
                break;
        }
    }

    private static string TagName(short tag) => tag switch
    {
        0 => "carbon_folder_name",
        1 => "cnid_path",
        2 => "carbon_path",
        3 => "appleshare_zone",
        4 => "appleshare_server",
        5 => "appleshare_user",
        6 => "driver_name",
        9 => "network_mount_info",
        10 => "dialup_info",
        14 => "target_name_unicode",
        15 => "volume_name_unicode",
        16 => "volume_date_hires",
        17 => "target_date_hires",
        18 => "posix_path",
        19 => "posix_path_to_mountpoint",
        20 => "nested_alias_of_disk_image",
        21 => "user_home_levels",
        _ => "unknown",
    };

    // ==================== Field readers ====================

    // Pascal-style string in a fixed width field: one length byte followed by the characters.
    private static string ReadPascalString(MpegReader reader, int fieldWidth)
    {
        int declared = reader.ReadByte();
        int capacity = fieldWidth - 1;
        int take = Math.Min(declared, capacity);

        var bytes = new byte[capacity];
        for (int i = 0; i < capacity; i++)
            bytes[i] = reader.ReadByte();

        // MacRoman by definition; Latin-1 is used so that no extra encoding provider is required.
        return Encoding.Latin1.GetString(bytes, 0, take);
    }

    private static string ReadText(MpegReader reader, long count, Encoding encoding,
                                   short tag, List<(string K, string V)> dataLines)
    {
        if (count <= 0) return string.Empty;

        int take = (int)Math.Min(count, MaxTextLen);
        if (take < count)
            Warn(dataLines, $"tag {tag}: value is {count} byte(s) long, only the first {take} are decoded.");

        var bytes = new byte[take];
        for (int i = 0; i < take; i++)
            bytes[i] = reader.ReadByte();

        // Some writers NUL-pad these values even though they are length delimited.
        int len = take;
        while (len > 0 && bytes[len - 1] == 0) len--;

        return encoding.GetString(bytes, 0, len);
    }

    private static string ReadHex(MpegReader reader, long count, List<(string K, string V)> dataLines)
    {
        if (count <= 0) return "<empty>";

        int take = (int)Math.Min(count, MaxHexBytes);
        var sb = new StringBuilder(take * 3);

        for (int i = 0; i < take; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(reader.ReadByte().ToString("X2", CultureInfo.InvariantCulture));
        }

        if (take < count) sb.Append(" ...");
        return sb.ToString();
    }

    // A version 2 alias stores the filesystem type as two bytes, not four.
    private static string ReadFourCCHalf(this MpegReader reader)
    {
        byte a = reader.ReadByte();
        byte b = reader.ReadByte();
        return new string(new[] { (char)a, (char)b });
    }

    // ==================== Formatting ====================

    private static string DescribeKind(int kind) => kind switch
    {
        0 => "0 (file)",
        1 => "1 (folder)",
        _ => $"{kind} (unknown)",
    };

    private static string DescribeDiskType(int diskType) => diskType switch
    {
        0 => "0 (fixed disk)",
        1 => "1 (network disk)",
        2 => "2 (400 KB floppy)",
        3 => "3 (800 KB floppy)",
        4 => "4 (1.44 MB floppy)",
        5 => "5 (ejectable media)",
        _ => $"{diskType} (unknown)",
    };

    // Seconds since 1904-01-01 00:00:00 UTC.
    private static string FormatMacDate(uint seconds)
    {
        if (seconds == 0) return "0 (not set)";
        try
        {
            DateTime t = MacEpoch.AddSeconds(seconds);
            return $"{seconds} ({t.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)})";
        }
        catch (ArgumentOutOfRangeException)
        {
            return $"{seconds} (out of range)";
        }
    }

    // 65536ths of a second since 1904-01-01 00:00:00 UTC.
    private static string FormatMacDateHiRes(ulong ticks)
    {
        if (ticks == 0) return "0 (not set)";
        try
        {
            double seconds = ticks / 65536.0;
            DateTime t = MacEpoch.AddSeconds(seconds);
            return $"{ticks} ({t.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)})";
        }
        catch (ArgumentOutOfRangeException)
        {
            return $"{ticks} (out of range)";
        }
    }

    private static string Display(string s)
        => string.IsNullOrEmpty(s) ? "<empty>" : Printable(s);

    // Only control characters are masked. Non-ASCII characters are kept, because
    // volume names and POSIX paths legitimately contain them.
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

    private static ParseResult Build(MPEGParser parser, Node node, List<(string K, string V)> dataLines)
        => new ParseResult
        {
            Title = "DataEntryAliasBox 'alis'",
            Position = node.Position,
            Length = node.Length,
            DataLines = dataLines,
            RawData = new OffsetStream(parser.FileStream, (long)node.Position, (long)node.Length)
        };
}