using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

internal static class Infe
{
    public static ParseResult Parse(MPEGParser parser, Node node)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end   = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();

        fs.Position = start;
        Seek(reader, start);

        // ---- box header（size 已由 MPEGParser 校验过，这里只需跳过）----
        int headerSize = 8;
        if (end - start >= 8)
        {
            uint size32 = reader.ReadUInt32BE();
            reader.ReadFourCC();
            if (size32 == 1) { reader.ReadUInt64BE(); headerSize = 16; }
        }
        Seek(reader, start + headerSize);

        if (end - Pos(reader) < 4)
        {
            Warn(dataLines, "cannot read version/flags");
            return Build(parser, node, dataLines);
        }

        byte version = reader.ReadByte();
        byte f1 = reader.ReadByte();
        byte f2 = reader.ReadByte();
        byte f3 = reader.ReadByte();
        uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

        dataLines.Add(("version", version.ToString()));
        dataLines.Add(("flags", $"0x{flags:X6}"));

        if (version == 0 || version == 1)
        {
            if (end - Pos(reader) < 4) { Warn(dataLines, "cannot read item_id/protection_index"); return Build(parser, node, dataLines); }

            dataLines.Add(("item_id", reader.ReadUInt16BE().ToString()));
            dataLines.Add(("protection_index", reader.ReadUInt16BE().ToString()));

            if (TryReadString(reader, end, "item_name", dataLines, out string itemName))
                dataLines.Add(("name", itemName));
            if (TryReadString(reader, end, "content_type", dataLines, out string contentType))
                dataLines.Add(("content_type", contentType));

            // content_encoding 在 spec 里是 optional，只有还有剩余字节才读
            if (Pos(reader) < end && TryReadString(reader, end, "content_encoding", dataLines, out string enc))
                dataLines.Add(("encoding", enc));

            // extension_type + ItemInfoExtension 同样 optional，且仅 version==1
            if (version == 1 && end - Pos(reader) >= 4)
            {
                string extType = reader.ReadFourCC();
                dataLines.Add(("extension_type", extType));
                ParseExtension(reader, extType, end, dataLines);
            }
        }
        else
        {
            int idBytes = version == 2 ? 2 : 4;
            if (version > 3)
                Warn(dataLines, $"unknown version {version}，try parse as version 3 layout");

            if (end - Pos(reader) < idBytes + 2 + 4)
            {
                Warn(dataLines, "cannot read item_id/protection_index/item_type");
                return Build(parser, node, dataLines);
            }

            uint itemId = version == 2 ? reader.ReadUInt16BE() : reader.ReadUInt32BE();
            ushort protectionIndex = reader.ReadUInt16BE();
            string itemType = reader.ReadFourCC();

            dataLines.Add(("item_id", itemId.ToString()));
            dataLines.Add(("protection_index", protectionIndex.ToString()));
            dataLines.Add(("item_type", itemType));

            if (TryReadString(reader, end, "item_name", dataLines, out string itemName))
                dataLines.Add(("name", itemName));

            if (itemType == "mime")
            {
                if (TryReadString(reader, end, "content_type", dataLines, out string contentType))
                    dataLines.Add(("content_type", contentType));
                if (Pos(reader) < end && TryReadString(reader, end, "content_encoding", dataLines, out string enc))
                    dataLines.Add(("encoding", enc));
            }
            else if (itemType == "uri ")
            {
                if (TryReadString(reader, end, "item_uri_type", dataLines, out string uri))
                    dataLines.Add(("uri", uri));
            }
        }

        long unread = end - Pos(reader);
        if (unread > 0) Warn(dataLines, $"unparsed tail size {unread} ");

        return Build(parser, node, dataLines);
    }

    private static void ParseExtension(MpegReader reader, string extensionType, long limit,
                                       List<(string K, string V)> dataLines)
    {
        if (extensionType != "fdel")
        {
            long rest = limit - Pos(reader);
            if (rest > 0) Warn(dataLines, $"unsupported extension_type '{extensionType}'，skip {rest} bytes");
            return;
        }

        if (!TryReadString(reader, limit, "fdel.content_location", dataLines, out string loc)) return;
        dataLines.Add(("fdel.location", loc));

        if (!TryReadString(reader, limit, "fdel.content_MD5", dataLines, out string md5)) return;
        dataLines.Add(("fdel.md5", md5));

        if (limit - Pos(reader) < 16) { Warn(dataLines, "fdel cannot read content_length/transfer_length"); return; }
        dataLines.Add(("fdel.content_length", reader.ReadUInt64BE().ToString()));
        dataLines.Add(("fdel.transfer_length", reader.ReadUInt64BE().ToString()));

        if (limit - Pos(reader) < 1) { Warn(dataLines, "fdel cannot read entry_count"); return; }
        byte entryCount = reader.ReadByte();
        dataLines.Add(("fdel.entry_count", entryCount.ToString()));

        for (int i = 0; i < entryCount; i++)
        {
            if (limit - Pos(reader) < 4)
            {
                Warn(dataLines, $"fdel group_id read {i} but expected {entryCount}");
                break;
            }
            dataLines.Add(($"fdel.group[{i}]", reader.ReadUInt32BE().ToString()));
        }
    }

    // ReadNullTerminatedString 无边界保护 → 读前查剩余、读后查越界
    private static bool TryReadString(MpegReader reader, long limit, string field,
                                      List<(string K, string V)> dataLines, out string value)
    {
        value = string.Empty;
        if (limit - Pos(reader) <= 0) { Warn(dataLines, $"missing {field}"); return false; }

        value = reader.ReadNullTerminatedString() ?? string.Empty;

        long over = Pos(reader) - limit;
        if (over > 0)
        {
            Warn(dataLines, $"{field} missing end，overread {over} bytes");
            Seek(reader, limit);
            value = string.Empty;
            return false;
        }
        return true;
    }

    private static void Seek(MpegReader reader, long pos) => reader.Seek(pos, SeekOrigin.Begin);
    private static long Pos(MpegReader reader) => (long)reader.Position;
    private static void Warn(List<(string K, string V)> lines, string msg) => lines.Add(("warning", msg));

    private static ParseResult Build(MPEGParser parser, Node node, List<(string K, string V)> dataLines)
        => new ParseResult
        {
            Title = "ItemInfoEntry 'infe'",
            Position = node.Position,
            Length = node.Length,
            DataLines = dataLines,
            RawData = new OffsetStream(parser.FileStream, (long)node.Position, (long)node.Length)
        };
}