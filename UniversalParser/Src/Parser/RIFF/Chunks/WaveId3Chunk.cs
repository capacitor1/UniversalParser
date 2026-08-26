using System;
using System.Collections.Generic;
using UniversalParser.Src.Parser.ID3;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'id3 '（亦见 'ID3 '）块：负载是一个完整的 ID3v2 标签。
    /// 此处只解析 10 字节标签头；扩展头、帧与填充属 ID3v2 层内容，不在 RIFF 层解码。
    /// </summary>
    internal static class WaveId3Chunk
    {
        private const int HeaderSize = 10;
        private const int FooterSize = 10;

        private const byte FlagUnsynchronisation = 0x80;
        private const byte FlagCompression = 0x40;    // 仅 ID3v2.2
        private const byte FlagExtendedHeader = 0x40; // ID3v2.3 起
        private const byte FlagFooterPresent = 0x10;  // 仅 ID3v2.4

        private static readonly (uint Mask, string Name)[] Flags22 =
        [
            (FlagUnsynchronisation, "UNSYNCHRONISATION"),
            (FlagCompression, "COMPRESSION"),
        ];

        private static readonly (uint Mask, string Name)[] Flags23 =
        [
            (FlagUnsynchronisation, "UNSYNCHRONISATION"),
            (FlagExtendedHeader, "EXTENDED_HEADER"),
            (0x20u, "EXPERIMENTAL"),
        ];

        private static readonly (uint Mask, string Name)[] Flags24 =
        [
            (FlagUnsynchronisation, "UNSYNCHRONISATION"),
            (FlagExtendedHeader, "EXTENDED_HEADER"),
            (0x20u, "EXPERIMENTAL"),
            (FlagFooterPresent, "FOOTER_PRESENT"),
        ];

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(14);
            int read = ChunkUtil.ReadPayload(parser, header, HeaderSize, out byte[] payload);

            if (read < HeaderSize)
            {
                dataLines.Add(("<Error>",
                    $"An ID3v2 tag header requires {HeaderSize} bytes, {read} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "WaveId3", dataLines);
            }

            if (payload[0] != (byte)'I' || payload[1] != (byte)'D' || payload[2] != (byte)'3')
            {
                dataLines.Add(("<Error>",
                    "The payload does not start with the 'ID3' identifier; found "
                    + $"{ChunkUtil.Hex(payload.AsSpan(0, 3))}."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "WaveId3", dataLines);
            }

            byte major = payload[3];
            byte revision = payload[4];
            byte flags = payload[5];
            ReadOnlySpan<byte> sizeField = payload.AsSpan(6, 4);

            bool syncSafe = ((sizeField[0] | sizeField[1] | sizeField[2] | sizeField[3]) & 0x80) == 0;
            uint bodyLength = DecodeSyncSafe(sizeField);
            bool hasFooter = major >= 4 && (flags & FlagFooterPresent) != 0;
            long tagLength = HeaderSize + bodyLength + (hasFooter ? FooterSize : 0);

            dataLines.Add(("Identifier", "ID3"));
            dataLines.Add(("MajorVersion", major.ToString()));
            dataLines.Add(("RevisionNumber", revision.ToString()));
            dataLines.Add(("<Version>", $"ID3v2.{major}.{revision}"));
            dataLines.Add(("Flags", $"0x{flags:X2}"));
            dataLines.Add(("<Flags>", ChunkUtil.DescribeFlags(flags, FlagsFor(major))));
            dataLines.Add(("Size", bodyLength.ToString()));
            dataLines.Add(("<TagLength>", RIFFUtil.FormatBytes(tagLength)));

            //dataLines.Add(("<Note>",
            //    "Size is a 32-bit synchsafe big-endian value covering the tag body only; "
            //    + "ID3v2 byte order is fixed by its own specification, not by the RIFF form."));

            if (major is < 2 or > 4)
            {
                dataLines.Add(("<Warning>",
                    $"ID3v2 major version {major} is unknown; the tag body layout cannot be assumed."));
            }

            if (!syncSafe)
            {
                dataLines.Add(("<Warning>",
                    "The size field is not a valid synchsafe integer; bit 7 is set in at least one byte."));
                dataLines.Add(("<Note>",
                    "Read as a plain big-endian integer the field would be "
                    + $"{RIFFUtil.ReadUInt32(sizeField, true)}."));
            }

            if ((flags & FlagUnsynchronisation) != 0)
            {
                dataLines.Add(("<Note>",
                    "Unsynchronisation is applied; the tag body must be de-unsynchronised before frame decoding."));
            }

            if (major == 2 && (flags & FlagCompression) != 0)
            {
                dataLines.Add(("<Note>",
                    "ID3v2.2 leaves its compression scheme undefined; compliant readers skip such a tag."));
            }

            if (major >= 3 && (flags & FlagExtendedHeader) != 0)
                dataLines.Add(("<Note>", "An extended header precedes the frames."));

            if (hasFooter)
                dataLines.Add(("<Note>", "A 10-byte footer trails the tag body and is counted in <TagLength>."));

            if (tagLength > header.PayloadLength)
                dataLines.Add(("<Warning>", "The declared tag length exceeds the chunk payload."));
            else if (tagLength < header.PayloadLength)
            {
                dataLines.Add(("<Note>",
                    "The chunk payload is longer than the declared tag; the trailing bytes lie outside the tag."));
            }

            // 两种拼写在野外都存在，大小写本身可作写入器指纹。
            if (header.Id is "ID3 ")
                dataLines.Add(("<Note>", "Uppercase 'ID3 ' spelling; 'id3 ' is the other identifier in use."));

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The ID3 chunk is truncated."));

            //dataLines.Add(("<Note>",
            //    "Extended header, frames and padding are ID3v2 content."));
            
            int wantTagBytes = (int)Math.Min(tagLength, header.PayloadLength);
            int tagRead = ChunkUtil.ReadPayload(parser, header, wantTagBytes, out byte[] tagData);

            ID3Parser.Parse(tagData, dataLines);
            _ = tagData; // 占位期间避免“已赋值但未使用”提示，接入 ID3Parser 后删除

            if (tagRead < wantTagBytes)
                dataLines.Add(("<Warning>", $"Only {tagRead} of {wantTagBytes} tag bytes could be read."));
            //
            return ChunkUtil.Build(parser, node, header, "WaveId3", dataLines);
        }

        private static (uint Mask, string Name)[] FlagsFor(byte majorVersion) => majorVersion switch
        {
            2 => Flags22,
            3 => Flags23,
            _ => Flags24, // 2.4 与未知版本按最完整的定义展开，多余位由 DescribeFlags 记为 reserved
        };

        /// <summary>32 位 synchsafe 整数：每字节仅低 7 位有效，定长大端。</summary>
        private static uint DecodeSyncSafe(ReadOnlySpan<byte> span) =>
            ((uint)(span[0] & 0x7F) << 21) | ((uint)(span[1] & 0x7F) << 14)
            | ((uint)(span[2] & 0x7F) << 7) | (uint)(span[3] & 0x7F);
    }
}