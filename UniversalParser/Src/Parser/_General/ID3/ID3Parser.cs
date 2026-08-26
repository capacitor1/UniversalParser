using System;
using System.Collections.Generic;
using System.IO;

namespace UniversalParser.Src.Parser.ID3
{
    /// <summary>ID3v2 标签解析入口。宿主容器（RIFF 'id3 ' / AIFF 'ID3 ' / MP3 裸标签）共用。</summary>
    internal static class ID3Parser
    {
        private const int TagHeaderSize = 10;
        private const int TagFooterSize = 10;
        private const string FrameArrayName = "frame";

        /// <summary>
        /// 解析一个完整的 ID3v2 标签（tag[0..9] 为 10 字节标签头），
        /// 把条目按呈现顺序追加到宿主 chunk 的 dataLines。
        /// </summary>
        /// <param name="tag">原始完整ID3标签字节数据。</param>
        /// <param name="dataLines">宿主 chunk 正在构建的条目列表。</param>
        public static void Parse(ReadOnlySpan<byte> tag, List<(string K, string V)> dataLines)
            => Parse(tag, dataLines, string.Empty);

        /// <summary>同上，但允许宿主为所有条目名加统一前缀（例如 "id3"）。</summary>
        public static void Parse(ReadOnlySpan<byte> tag, List<(string K, string V)> dataLines, string keyPrefix)
        {
            ArgumentNullException.ThrowIfNull(dataLines);

            if (tag.Length < TagHeaderSize)
                throw new InvalidDataException("ID3v2 tag is smaller than its 10-byte header.");
            if (tag[0] != (byte)'I' || tag[1] != (byte)'D' || tag[2] != (byte)'3')
                throw new InvalidDataException("ID3v2 tag does not start with the \"ID3\" identifier.");

            var sink = new ID3Sink(dataLines, keyPrefix);

            byte major = tag[3];
            byte revision = tag[4];
            byte flags = tag[5];
            int declaredSize = ID3Number.SyncSafe32(tag.Slice(6, 4));

            sink.Text("file_identifier", "ID3");
            sink.Number("version_major", major);
            sink.Number("version_revision", revision);
            sink.Verbatim("flags", ID3Flags.TagFlags(flags, major));
            sink.Number("size", declaredSize);

            // 未知主版本按最接近的 v2.4 布局尽力解析。
            byte version = major > 4 ? (byte)4 : major;
            if (major > 4)
                sink.Verbatim("parse_note", "Unknown ID3v2 major version, decoded with the ID3v2.4 layout.");

            bool hasFooter = version >= 4 && (flags & ID3Flags.TagFooterPresent) != 0;
            int usable = Math.Max(0, tag.Length - TagHeaderSize - (hasFooter ? TagFooterSize : 0));
            int bodySize = Math.Min(declaredSize, usable);
            if (bodySize < declaredSize)
                sink.Verbatim("parse_note", "Declared tag size exceeds the supplied buffer, only available bytes are decoded.");

            ReadOnlySpan<byte> stored = tag.Slice(TagHeaderSize, bodySize);

            // v2.2 的整标签压缩方案未定义，规范要求解码器整体忽略。
            if (version <= 2 && (flags & ID3Flags.TagCompressionV22) != 0)
            {
                sink.Payload("compressed_data", stored);
                return;
            }

            // v2.2 / v2.3：去同步化作用于整个标签体；v2.4：作用于单帧（见 ID3FrameReader）。
            byte[]? restored = version <= 3 && (flags & ID3Flags.TagUnsynchronisation) != 0
                ? ID3Unsynchronisation.Restore(stored)
                : null;
            ReadOnlySpan<byte> body = restored is null ? stored : restored;

            int offset = 0;
            if (version >= 3 && (flags & ID3Flags.TagExtendedHeader) != 0)
                offset += ParseExtendedHeader(body[offset..], version, sink);
            ID3Sink frameSink = sink.Key(FrameArrayName);

            offset += ID3FrameReader.ReadFrames(body[offset..], version, frameSink.Path, dataLines);

            int padding = 0;
            while (offset + padding < body.Length && body[offset + padding] == 0x00) padding++;
            if (padding > 0) sink.Number("padding_size", padding);
            offset += padding;

            if (offset < body.Length) sink.Payload("unparsed_data", body[offset..]);

            if (hasFooter) ParseFooter(tag, declaredSize, sink);
        }

        /// <summary>扩展头；返回其占用的字节数。</summary>
        private static int ParseExtendedHeader(ReadOnlySpan<byte> body, byte version, ID3Sink parent)
        {
            ID3Sink sink = parent.Scope("extended_header");
            var c = new ID3Cursor(body);

            if (version == 3)
            {
                int size = ID3Number.ToLength(c.ReadUInt(4));               // 不含自身 4 字节
                sink.Number("size", size);
                ushort extFlags = (ushort)c.ReadUInt(2);
                sink.Verbatim("flags", ID3Flags.ExtendedFlagsV3(extFlags));
                sink.Number("padding_size", ID3Number.ToLength(c.ReadUInt(4)));
                if ((extFlags & ID3Flags.ExtendedV3CrcDataPresent) != 0)
                    sink.Verbatim("crc_data", ID3Format.Hex(c.ReadUInt(4), 8));
                return Clamp(4 + size, c.Position, body.Length);
            }

            int totalSize = c.ReadSyncSafe32();                              // 含自身
            sink.Number("size", totalSize);
            int flagBytes = c.ReadByte();
            sink.Number("number_of_flag_bytes", flagBytes);
            byte v4Flags = flagBytes > 0 ? c.ReadByte() : (byte)0;
            if (flagBytes > 1) c.ReadBytes(flagBytes - 1);
            sink.Verbatim("flags", ID3Flags.ExtendedFlagsV4(v4Flags));

            // 每个已置位的标志后面都跟一个“标志数据长度”字节，按标志从左到右的顺序排列。
            if ((v4Flags & ID3Flags.ExtendedV4TagIsAnUpdate) != 0)
                c.ReadBytes(c.ReadByte());                                   // 长度恒为 0，无数据
            if ((v4Flags & ID3Flags.ExtendedV4CrcDataPresent) != 0)
            {
                ReadOnlySpan<byte> crc = c.ReadBytes(c.ReadByte());
                sink.Verbatim("total_frame_crc", ID3Format.Hex(ID3Number.SyncSafe(crc.ToArray()), 8));
            }
            if ((v4Flags & ID3Flags.ExtendedV4TagRestrictions) != 0)
            {
                ReadOnlySpan<byte> data = c.ReadBytes(c.ReadByte());
                if (!data.IsEmpty) sink.Verbatim("restrictions", ID3Flags.Restrictions(data[0]));
            }
            return Clamp(totalSize, c.Position, body.Length);
        }

        private static void ParseFooter(ReadOnlySpan<byte> tag, int declaredSize, ID3Sink parent)
        {
            int offset = TagHeaderSize + declaredSize;
            if (offset + TagFooterSize > tag.Length) offset = tag.Length - TagFooterSize;
            if (offset < TagHeaderSize) return;

            ReadOnlySpan<byte> footer = tag.Slice(offset, TagFooterSize);
            ID3Sink sink = parent.Scope("footer");

            string identifier = ID3String.Decode(footer[..3], ID3String.EncodingIso88591);
            sink.Text("file_identifier", identifier == "3DI" ? identifier : identifier + " (expected 3DI)");
            sink.Number("version_major", footer[3]);
            sink.Number("version_revision", footer[4]);
            sink.Verbatim("flags", ID3Flags.TagFlags(footer[5], footer[3]));
            sink.Number("size", ID3Number.SyncSafe32(footer.Slice(6, 4)));
        }

        private static int Clamp(int declared, int consumed, int limit)
            => declared < consumed || declared > limit ? Math.Min(consumed, limit) : declared;
    }
}