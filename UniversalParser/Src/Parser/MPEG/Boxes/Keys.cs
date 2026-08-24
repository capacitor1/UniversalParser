using System.Text;
using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// MetadataItemKeysAtom 'keys' -- QuickTime metadata key table.
// Lives in a QuickTime style 'meta' box as a sibling of 'ilst'.
// The 1-based position of an entry in this table is exactly what the 'ilst' child
// atoms carry in their type field (rendered as "key[N]" by MPEGParser).
//
// Layout:
//   FullBox('keys', version = 0, flags = 0)
//   unsigned int(32) entry_count;
//   for (i = 1; i <= entry_count; i++) {
//       unsigned int(32) key_size;       // counts key_size and key_namespace themselves
//       unsigned int(32) key_namespace;  // 'mdta' (reverse DNS names) or 'udta'
//       unsigned int(8)  key_value[key_size - 8];
//   }
//
// key_value is length delimited and has NO NUL terminator, so it must be read by
// byte count. Using a NUL scan here would run straight into the next entry.
internal static class Keys
{
    private const int EntryHeaderLen = 8;      // key_size(4) + key_namespace(4)
    private const int MaxKeyValueLen = 4096;   // sanity cap for corrupt key_size values

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

        dataLines.Add(("version", version.ToString()));
        dataLines.Add(("flags", $"0x{flags:X6}"));

        if (version != 0)
            Warn(dataLines, $"Spec requires version = 0, got {version}; parsing as version 0 anyway.");

        // ---- entry_count ----
        if (end - Pos(reader) < 4)
        {
            Warn(dataLines, "Cannot read entry_count: not enough bytes.");
            return Build(parser, node, dataLines);
        }

        uint entryCount = reader.ReadUInt32BE();
        dataLines.Add(("entry_count", entryCount.ToString()));

        long capacity = (end - Pos(reader)) / EntryHeaderLen;
        if (entryCount > capacity)
        {
            Warn(dataLines, $"entry_count is {entryCount}, but the remaining {end - Pos(reader)} byte(s) " +
                            $"can hold at most {capacity} entry/entries. Data may be corrupt.");
        }

        // ---- key table. Index is 1-based; index 0 is not a valid key. ----
        long parsed = 0;

        for (uint i = 1; i <= entryCount; i++)
        {
            long entryStart = Pos(reader);

            if (end - entryStart < EntryHeaderLen)
            {
                Warn(dataLines, $"key[{i}]: only {end - entryStart} byte(s) left, " +
                                $"too short for an entry header. Parsed {parsed} of {entryCount} entry/entries.");
                break;
            }

            uint keySize = reader.ReadUInt32BE();
            string keyNamespace = reader.ReadFourCC();

            if (keySize < EntryHeaderLen)
            {
                Warn(dataLines, $"key[{i}]: key_size is {keySize}, smaller than the {EntryHeaderLen} byte " +
                                "entry header. Cannot advance, stopping.");
                break;
            }

            long entryEnd = entryStart + keySize;
            if (entryEnd > end)
            {
                Warn(dataLines, $"key[{i}]: key_size {keySize} runs past the end of the box. " +
                                "Clamped to the box end.");
                entryEnd = end;
            }

            long valueLen = entryEnd - Pos(reader);
            string keyValue = ReadKeyValue(reader, valueLen, i, dataLines);

            dataLines.Add(($"key[{i}]", $"namespace= {Display(keyNamespace)}, name= {Display(keyValue)}"));

            if (keyNamespace != "mdta" && keyNamespace != "udta")
                Warn(dataLines, $"key[{i}]: unregistered key_namespace '{Display(keyNamespace)}'.");

            Seek(reader, entryEnd);   // align to the declared entry end
            parsed++;
        }

        long tail = end - Pos(reader);
        if (tail > 0)
            Warn(dataLines, $"{tail} trailing byte(s) were not covered by the {entryCount} declared entry/entries.");

        return Build(parser, node, dataLines);
    }

    // ==================== Helpers ====================

    // key_value is length delimited, so it is read by byte count rather than by NUL scan.
    // Reading one byte at a time keeps this file dependent only on MpegReader members that
    // are already in use elsewhere; swap in a bulk read if MpegReader exposes one.
    private static string ReadKeyValue(MpegReader reader, long count, uint index,
                                       List<(string K, string V)> dataLines)
    {
        if (count <= 0) return string.Empty;

        int take = (int)Math.Min(count, MaxKeyValueLen);
        if (take < count)
        {
            Warn(dataLines, $"key[{index}]: key_value is {count} byte(s) long, " +
                            $"only the first {take} byte(s) are shown.");
        }

        var bytes = new byte[take];
        for (int i = 0; i < take; i++)
            bytes[i] = reader.ReadByte();

        // Some writers pad the key value with NULs even though it is length delimited.
        int len = take;
        while (len > 0 && bytes[len - 1] == 0) len--;

        return Encoding.UTF8.GetString(bytes, 0, len);
    }

    private static string Display(string s)
        => string.IsNullOrEmpty(s) ? "<empty>" : Printable(s);

    // Only control characters are masked. Non-ASCII characters are kept, because
    // key_value is a UTF-8 string and may legitimately contain them.
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
            Title = "MetadataItemKeysAtom 'keys'",
            Position = node.Position,
            Length = node.Length,
            DataLines = dataLines,
            RawData = new OffsetStream(parser.FileStream, (long)node.Position, (long)node.Length)
        };
}