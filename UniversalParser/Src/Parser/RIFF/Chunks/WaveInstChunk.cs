using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'inst' 块（Instrument，采样器音高／力度映射）。规范定长 7 字节，
    /// 字段全为单字节故不受 RIFF 字节序影响；其后若有数据不解析。
    /// </summary>
    internal static class WaveInstChunk
    {
        private const int DefinedSize = 7;
        private const int MaxMidiValue = 127;
        private const int FineTuneLimit = 50;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(20);
            int read = ChunkUtil.ReadPayload(parser, header, DefinedSize, out byte[] payload);

            if (read < DefinedSize)
            {
                dataLines.Add(("<Error>", $"'inst' requires {DefinedSize} bytes, {read} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "WaveInstrument", dataLines);
            }

            byte unshiftedNote = payload[0];
            sbyte fineTune = unchecked((sbyte)payload[1]);
            sbyte gain = unchecked((sbyte)payload[2]);
            byte lowNote = payload[3];
            byte highNote = payload[4];
            byte lowVelocity = payload[5];
            byte highVelocity = payload[6];

            dataLines.Add(("bUnshiftedNote", unshiftedNote.ToString()));
            dataLines.Add(("<bUnshiftedNote>", ChunkUtil.FormatMidiNote(unshiftedNote)));
            dataLines.Add(("chFineTune", fineTune.ToString()));
            dataLines.Add(("<chFineTune>", FormatCents(fineTune)));
            dataLines.Add(("chGain", gain.ToString()));
            dataLines.Add(("<chGain>", $"{gain} dB"));
            dataLines.Add(("bLowNote", lowNote.ToString()));
            dataLines.Add(("<bLowNote>", ChunkUtil.FormatMidiNote(lowNote)));
            dataLines.Add(("bHighNote", highNote.ToString()));
            dataLines.Add(("<bHighNote>", ChunkUtil.FormatMidiNote(highNote)));
            dataLines.Add(("bLowVelocity", lowVelocity.ToString()));
            dataLines.Add(("bHighVelocity", highVelocity.ToString()));

            // 写入器占位或字段被清空时全零，取证上与"确实映射到 MIDI 音 0"不可混淆。
            if (ChunkUtil.IsAllZero(payload))
                dataLines.Add(("<Note>", "The payload is entirely zero; no usable mapping is expressed."));

            AddMidiRangeWarning(dataLines, "bUnshiftedNote", unshiftedNote);
            AddMidiRangeWarning(dataLines, "bLowNote", lowNote);
            AddMidiRangeWarning(dataLines, "bHighNote", highNote);
            AddMidiRangeWarning(dataLines, "bLowVelocity", lowVelocity);
            AddMidiRangeWarning(dataLines, "bHighVelocity", highVelocity);

            if (Math.Abs(fineTune) > FineTuneLimit)
            {
                dataLines.Add(("<Warning>",
                    $"chFineTune is {fineTune}; the defined range is -{FineTuneLimit} to +{FineTuneLimit} cents."));
            }

            if (lowNote > highNote)
                dataLines.Add(("<Warning>", "bLowNote exceeds bHighNote; the note range is inverted."));

            if (lowVelocity > highVelocity)
                dataLines.Add(("<Warning>", "bLowVelocity exceeds bHighVelocity; the velocity range is inverted."));

            if (lowVelocity == 0 || highVelocity == 0)
            {
                dataLines.Add(("<Note>",
                    "The defined usable velocity range starts at 1; velocity 0 means note-off in MIDI."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'inst' chunk is truncated."));

            if (header.PayloadLength > DefinedSize)
            {
                dataLines.Add(("<Note>",
                    $"'inst' is defined as exactly {DefinedSize} bytes; trailing bytes are undefined "
                    + "and not decoded by design."));
            }

            ChunkUtil.AddUnparsedLength(dataLines, header, DefinedSize);
            return ChunkUtil.Build(parser, node, header, "WaveInstrument", dataLines);
        }

        /// <summary>音分单位：±1 时用单数，免得输出 "1 cents"。</summary>
        private static string FormatCents(sbyte cents) =>
            Math.Abs(cents) == 1 ? $"{cents} cent" : $"{cents} cents";

        /// <summary>MIDI 取值上限统一校验；越界意味着写入器出错或该字节被挪作他用。</summary>
        private static void AddMidiRangeWarning(
            List<(string K, string V)> dataLines, string field, byte value)
        {
            if (value > MaxMidiValue)
                dataLines.Add(("<Warning>", $"{field} is {value}; the valid MIDI range is 0-{MaxMidiValue}."));
        }
    }
}