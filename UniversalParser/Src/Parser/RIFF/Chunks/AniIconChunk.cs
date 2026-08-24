using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// ANI 帧数据 'icon'。负载通常是一个完整的 ICO/CUR 文件，即 ICONDIR + ICONDIRENTRY 数组 + 图像数据。
    /// 图像数据（DIB 或 PNG）按设计不解析。
    /// 布局仅依据本块自身的 ICONDIR 标记判定，不读取兄弟块 'anih' 的 AF_ICON 标志。
    /// </summary>
    internal static class AniIconChunk
    {
        private const int DirectoryHeaderSize = 6;
        private const int DirectoryEntrySize = 16;

        private const ushort ResourceTypeIcon = 1;
        private const ushort ResourceTypeCursor = 2;

        private const string EntryColumns =
            "bWidth,bHeight,bColorCount,bReserved,wPlanes,wBitCount,dwBytesInRes,dwImageOffset";

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            bool be = parser.IsBigEndian;

            if (!TryReadDirectoryHeader(parser, header, be, out ushort idReserved, out ushort idType, out ushort idCount))
            {
                dataLines.Add(("<Note>",
                    "The payload carries no valid ICONDIR header. When AF_ICON is clear in the 'flags' field of the "
                    + "'anih' chunk the frames hold raw DIB data instead, and this parser never correlates chunks, "
                    + "so the payload is left undecoded."));

                if (header.IsTruncated)
                    dataLines.Add(("<Warning>", "The 'icon' chunk is truncated."));

                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "Frame", dataLines);
            }

            dataLines.Add(("idReserved", idReserved.ToString()));
            dataLines.Add(("idType", idType.ToString()));
            dataLines.Add(("<idType>", idType switch
            {
                ResourceTypeIcon => "Icon resource (ICO)",
                ResourceTypeCursor => "Cursor resource (CUR)",
                _ => "Unknown resource type",
            }));
            dataLines.Add(("idCount", idCount.ToString()));

            long directorySize = DirectoryHeaderSize + (long)idCount * DirectoryEntrySize;
            byte[] entries = new byte[idCount * DirectoryEntrySize];
            int read = parser.ReadAt(header.PayloadStart + DirectoryHeaderSize, entries);

            if (read < entries.Length)
            {
                dataLines.Add(("<Warning>",
                    $"ICONDIR declares {idCount} entries ({entries.Length} bytes) but only {read} bytes are readable."));
                read -= read % DirectoryEntrySize;
            }

            int usable = read / DirectoryEntrySize;

            if (idType == ResourceTypeCursor)
            {
                dataLines.Add(("<Note>",
                    "For cursor resources wPlanes and wBitCount hold the hotspot X and Y coordinates."));
            }

            // 数组必须放在最后，因此聚合校验先行
            AppendValidation(dataLines, entries.AsSpan(0, usable * DirectoryEntrySize), be, header.PayloadLength);

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'icon' chunk is truncated."));

            ChunkUtil.AddUnparsedLength(dataLines, header, directorySize);

            if (usable > 0)
                AppendEntries(dataLines, entries.AsSpan(0, usable * DirectoryEntrySize), be, usable);

            string readableName = idType switch
            {
                ResourceTypeIcon => "IconFrame",
                ResourceTypeCursor => "CursorFrame",
                _ => "Frame",
            };

            return ChunkUtil.Build(parser, node, header, readableName, dataLines);
        }

        private static bool TryReadDirectoryHeader(
            RIFFParser parser,
            in RIFFChunkHeader header,
            bool bigEndian,
            out ushort idReserved,
            out ushort idType,
            out ushort idCount)
        {
            idReserved = 0;
            idType = 0;
            idCount = 0;

            if (header.PayloadLength < DirectoryHeaderSize + DirectoryEntrySize) return false;

            Span<byte> probe = stackalloc byte[DirectoryHeaderSize];
            if (parser.ReadAt(header.PayloadStart, probe) < DirectoryHeaderSize) return false;

            idReserved = RIFFUtil.ReadUInt16(probe[..2], bigEndian);
            idType = RIFFUtil.ReadUInt16(probe.Slice(2, 2), bigEndian);
            idCount = RIFFUtil.ReadUInt16(probe.Slice(4, 2), bigEndian);

            // idReserved 必须为 0；BITMAPINFOHEADER 的同位置是 biSize（40/108/124），因此可靠区分。
            if (idReserved != 0) return false;
            if (idType is not (ResourceTypeIcon or ResourceTypeCursor)) return false;
            if (idCount == 0) return false;

            return header.PayloadLength >= DirectoryHeaderSize + (long)idCount * DirectoryEntrySize;
        }

        private static void AppendValidation(
            List<(string K, string V)> dataLines,
            ReadOnlySpan<byte> entries,
            bool bigEndian,
            long payloadLength)
        {
            int reservedNonZero = 0;
            int outOfRange = 0;

            for (int offset = 0; offset + DirectoryEntrySize <= entries.Length; offset += DirectoryEntrySize)
            {
                ReadOnlySpan<byte> entry = entries.Slice(offset, DirectoryEntrySize);

                if (entry[3] != 0) reservedNonZero++;

                uint bytesInRes = RIFFUtil.ReadUInt32(entry.Slice(8, 4), bigEndian);
                uint imageOffset = RIFFUtil.ReadUInt32(entry.Slice(12, 4), bigEndian);

                if ((long)imageOffset + bytesInRes > payloadLength) outOfRange++;
            }

            if (reservedNonZero > 0)
                dataLines.Add(("<Warning>", $"bReserved is non-zero in {reservedNonZero} entry/entries; it must be 0."));

            if (outOfRange > 0)
            {
                dataLines.Add(("<Warning>",
                    $"{outOfRange} entry/entries describe image data that extends past the end of this chunk."));
            }
        }

        private static void AppendEntries(
            List<(string K, string V)> dataLines, ReadOnlySpan<byte> entries, bool bigEndian, int count)
        {
            dataLines.Add(($"idEntries[{count}]", EntryColumns));

            for (int offset = 0; offset + DirectoryEntrySize <= entries.Length; offset += DirectoryEntrySize)
            {
                ReadOnlySpan<byte> entry = entries.Slice(offset, DirectoryEntrySize);

                string width = FormatDimension(entry[0]);
                string height = FormatDimension(entry[1]);
                string colours = entry[2] == 0 ? "0 (256 or more)" : entry[2].ToString();

                ushort planes = RIFFUtil.ReadUInt16(entry.Slice(4, 2), bigEndian);
                ushort bitCount = RIFFUtil.ReadUInt16(entry.Slice(6, 2), bigEndian);
                uint bytesInRes = RIFFUtil.ReadUInt32(entry.Slice(8, 4), bigEndian);
                uint imageOffset = RIFFUtil.ReadUInt32(entry.Slice(12, 4), bigEndian);

                dataLines.Add((string.Empty,
                    $"{width},{height},{colours},{entry[3]},{planes},{bitCount},{bytesInRes},{imageOffset}"));
            }
        }

        /// <summary>bWidth / bHeight 为 0 时表示 256 像素。</summary>
        private static string FormatDimension(byte value) => value == 0 ? "0 (256)" : value.ToString();
    }
}