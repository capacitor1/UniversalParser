using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'acid' 块（Sonic Foundry / ACID 循环信息）。该结构从未公开，
    /// 字段名与字段顺序沿用业界通行的逆向布局；定长 24 字节，其后若有数据不解析。
    /// </summary>
    internal static class WaveAcidChunk
    {
        private const int DefinedSize = 24;

        private const uint TypeOneShot = 0x00000001;
        private const uint TypeRootNoteSet = 0x00000002;

        // 0x10 的语义至今无人给出确证，ACID 系写入器一律置位，故沿用 ACIDIZER 之名。
        private static readonly (uint Mask, string Name)[] FileTypeFlags =
        [
            (TypeOneShot, "ONE_SHOT"),
            (TypeRootNoteSet, "ROOT_NOTE_SET"),
            (0x00000004u, "STRETCH"),
            (0x00000008u, "DISK_BASED"),
            (0x00000010u, "ACIDIZER"),
        ];

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(18);
            int read = ChunkUtil.ReadPayload(parser, header, DefinedSize, out byte[] payload);

            if (read < DefinedSize)
            {
                dataLines.Add(("<Error>", $"'acid' requires {DefinedSize} bytes, {read} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "WaveAcid", dataLines);
            }

            bool bigEndian = parser.IsBigEndian;
            uint fileType = RIFFUtil.ReadUInt32(payload.AsSpan(0, 4), bigEndian);
            ushort rootNote = RIFFUtil.ReadUInt16(payload.AsSpan(4, 2), bigEndian);
            ushort unknown1 = RIFFUtil.ReadUInt16(payload.AsSpan(6, 2), bigEndian);
            float unknown2 = ChunkUtil.ReadSingle(payload.AsSpan(8, 4), bigEndian);
            uint numberOfBeats = RIFFUtil.ReadUInt32(payload.AsSpan(12, 4), bigEndian);
            ushort meterDenominator = RIFFUtil.ReadUInt16(payload.AsSpan(16, 2), bigEndian);
            ushort meterNumerator = RIFFUtil.ReadUInt16(payload.AsSpan(18, 2), bigEndian);
            float tempo = ChunkUtil.ReadSingle(payload.AsSpan(20, 4), bigEndian);

            dataLines.Add(("dwFileType", $"0x{fileType:X8}"));
            dataLines.Add(("<dwFileType>", ChunkUtil.DescribeFlags(fileType, FileTypeFlags)));
            dataLines.Add(("wRootNote", rootNote.ToString()));
            dataLines.Add(("<wRootNote>", ChunkUtil.FormatMidiNote(rootNote)));
            dataLines.Add(("wUnknown1", $"0x{unknown1:X4}"));
            dataLines.Add(("fUnknown2", ChunkUtil.FormatSingle(unknown2)));
            dataLines.Add(("dwNumberOfBeats", numberOfBeats.ToString()));
            dataLines.Add(("wMeterDenominator", meterDenominator.ToString()));
            dataLines.Add(("wMeterNumerator", meterNumerator.ToString()));
            dataLines.Add(("<Meter>", $"{meterNumerator}/{meterDenominator}"));
            dataLines.Add(("fTempo", ChunkUtil.FormatSingle(tempo)));

            if ((fileType & TypeOneShot) != 0)
            {
                dataLines.Add(("<Note>",
                    "One-shot: dwNumberOfBeats and fTempo take no part in time stretching."));
            }

            if ((fileType & TypeRootNoteSet) == 0)
            {
                dataLines.Add(("<Note>", "ROOT_NOTE_SET is clear; wRootNote carries no pitch information."));
            }
            else
            {
                dataLines.Add(("<Note>",
                    "Writers disagree on the root-note octave (0x30..0x3B versus 0x3C..0x47); "
                    + "only the pitch class is dependable."));
            }

            // 已知写入器恒为 0x8000 / 0，偏离即为可疑写入痕迹。
            if (unknown1 != 0x8000 || unknown2 != 0f)
            {
                dataLines.Add(("<Note>",
                    "wUnknown1 / fUnknown2 are undocumented; known writers emit 0x8000 and 0."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'acid' chunk is truncated."));

            if (header.PayloadLength > DefinedSize)
            {
                dataLines.Add(("<Note>",
                    "Trailing bytes beyond the 24-byte structure are undefined; not decoded by design."));
            }

            ChunkUtil.AddUnparsedLength(dataLines, header, DefinedSize);
            return ChunkUtil.Build(parser, node, header, "WaveAcid", dataLines);
        }
    }
}