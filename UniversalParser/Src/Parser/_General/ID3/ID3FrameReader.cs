using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ID3
{
    /// <summary>帧头循环。CHAP / CTOC 的内嵌子帧复用同一实现。</summary>
    internal static class ID3FrameReader
    {
        /// <summary>逐帧解析，返回帧区实际消耗的字节数（即填充区起点）。</summary>
        public static int ReadFrames(ReadOnlySpan<byte> body, byte version, string arrayName, List<(string K, string V)> lines)
        {
            int idLength = version <= 2 ? 3 : 4;
            int headerSize = version <= 2 ? 6 : 10;
            int offset = 0;
            int index = 0;

            while (offset + headerSize <= body.Length)
            {
                if (body[offset] == 0x00) break;                             // 进入填充区

                string frameId = ID3String.Decode(body.Slice(offset, idLength), ID3String.EncodingIso88591);
                if (!IsValidFrameId(frameId))
                {
                    new ID3Sink(lines).Verbatim("parse_note",
                        "Invalid frame identifier encountered, frame scan stopped at offset " + ID3Format.Number(offset) + ".");
                    break;
                }

                int declaredSize = ReadFrameSize(body, offset, version, idLength, headerSize);
                ushort frameFlags = version <= 2
                    ? (ushort)0
                    : (ushort)((body[offset + 8] << 8) | body[offset + 9]);

                int payloadOffset = offset + headerSize;
                int payloadSize = Math.Min(Math.Max(declaredSize, 0), body.Length - payloadOffset);

                ID3Sink sink = new ID3Sink(lines, arrayName + "[" + ID3Format.Number(index) + "]");
                sink.Text("frame_id", frameId);
                sink.Number("size", declaredSize);
                if (version >= 3) sink.Verbatim("flags", ID3Flags.FrameFlags(frameFlags, version));
                if (payloadSize < declaredSize)
                    sink.Verbatim("parse_note", "Frame size exceeds the remaining tag data, frame is truncated.");

                var c = new ID3Cursor(body.Slice(payloadOffset, payloadSize));
                bool opaque = false;                                         // 压缩 / 加密后无法按字段解析

                if (version == 3)
                {
                    if ((frameFlags & ID3Flags.FrameV3Compression) != 0)
                    {
                        sink.Number("decompressed_size", c.ReadUInt(4));
                        opaque = true;
                    }
                    if ((frameFlags & ID3Flags.FrameV3Encryption) != 0)
                    {
                        sink.Verbatim("encryption_method", ID3Format.Hex(c.ReadByte()));
                        opaque = true;
                    }
                    if ((frameFlags & ID3Flags.FrameV3GroupingIdentity) != 0)
                        sink.Verbatim("group_identifier", ID3Format.Hex(c.ReadByte()));
                }
                else if (version >= 4)
                {
                    // 附加字段顺序与标志位顺序一致：分组标识 → 加密方法 → 数据长度指示。
                    if ((frameFlags & ID3Flags.FrameV4GroupingIdentity) != 0)
                        sink.Verbatim("group_identifier", ID3Format.Hex(c.ReadByte()));
                    if ((frameFlags & ID3Flags.FrameV4Encryption) != 0)
                    {
                        sink.Verbatim("encryption_method", ID3Format.Hex(c.ReadByte()));
                        opaque = true;
                    }
                    if ((frameFlags & ID3Flags.FrameV4DataLengthIndicator) != 0)
                        sink.Number("data_length_indicator", c.ReadSyncSafe32());
                    if ((frameFlags & ID3Flags.FrameV4Compression) != 0)
                        opaque = true;
                }

                ReadOnlySpan<byte> raw = c.ReadRest();
                byte[]? restored = version >= 4 && (frameFlags & ID3Flags.FrameV4Unsynchronisation) != 0
                    ? ID3Unsynchronisation.Restore(raw)
                    : null;
                ReadOnlySpan<byte> content = restored is null ? raw : restored;

                if (opaque) sink.Payload("frame_data", content);
                else ID3FrameContent.Parse(frameId, version, content, sink);

                offset = payloadOffset + payloadSize;
                index++;
            }

            return offset;
        }

        private static bool IsValidFrameId(string id)
        {
            if (id.Length == 0) return false;
            foreach (char ch in id)
                if (!((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))) return false;
            return true;
        }

        /// <summary>v2.4 帧长本应为 synchsafe；对非规范编码器写出的纯大端值做边界校验回退。</summary>
        private static int ReadFrameSize(ReadOnlySpan<byte> body, int offset, byte version, int idLength, int headerSize)
        {
            ReadOnlySpan<byte> raw = body.Slice(offset + idLength, version <= 2 ? 3 : 4);
            if (version <= 3) return ID3Number.ToLength(ID3Number.UIntBE(raw));

            int plain = ID3Number.ToLength(ID3Number.UIntBE(raw));
            if (!ID3Number.IsSyncSafe(raw)) return plain;                    // 不可能是 synchsafe

            int syncSafe = ID3Number.SyncSafe32(raw);
            if (plain != syncSafe
                && !IsFrameBoundary(body, offset + headerSize + syncSafe, version)
                && IsFrameBoundary(body, offset + headerSize + plain, version))
                return plain;
            return syncSafe;
        }

        private static bool IsFrameBoundary(ReadOnlySpan<byte> body, int position, byte version)
        {
            if (position == body.Length) return true;
            if (position < 0 || position > body.Length) return false;
            if (body[position] == 0x00) return true;                         // 填充区
            int idLength = version <= 2 ? 3 : 4;
            if (position + idLength > body.Length) return false;
            return IsValidFrameId(ID3String.Decode(body.Slice(position, idLength), ID3String.EncodingIso88591));
        }
    }
}