using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVEFORMAT / PCMWAVEFORMAT / WAVEFORMATEX / WAVEFORMATEXTENSIBLE 的字段发射器。
    /// 由 WAVE 的 'fmt ' 与 AVI 音频流的 'strf' 共用。
    /// </summary>
    internal static class WaveFormatEx
    {
        public const int MaxStructSize = 40;

        private const ushort WaveFormatPcm = 0x0001;
        private const ushort WaveFormatIeeeFloat = 0x0003;
        private const ushort WaveFormatExtensible = 0xFFFE;

        /// <summary>KSDATAFORMAT_SUBTYPE_xxx = {tag,0x0000,0x0010,{0x80,0x00,0x00,0xAA,0x00,0x38,0x9B,0x71}}</summary>
        private static readonly byte[] KsSubTypeSuffix =
            [0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

        private static readonly Dictionary<ushort, string> FormatNames = new()
        {
            [0x0001] = "PCM",
            [0x0002] = "Microsoft ADPCM",
            [0x0003] = "IEEE float",
            [0x0006] = "ITU G.711 A-law",
            [0x0007] = "ITU G.711 mu-law",
            [0x0011] = "IMA ADPCM",
            [0x0016] = "ITU G.723 ADPCM",
            [0x0031] = "GSM 6.10",
            [0x0040] = "ITU G.721 ADPCM",
            [0x0050] = "MPEG Layer-1/2",
            [0x0055] = "MPEG Layer-3",
            [0x0092] = "Dolby AC-3 over S/PDIF",
            [0x00FF] = "Raw AAC",
            [0x161] = "Windows Media Audio 2",
            [0x162] = "Windows Media Audio 9 Professional",
            [0x163] = "Windows Media Audio 9 Lossless",
            [0x2000] = "Dolby AC-3",
            [0x2001] = "DTS",
            [0xFFFE] = "Extensible",
            [0xFFFF] = "Development / Experimental",
        };

        private static readonly (uint Mask, string Name)[] SpeakerFlags =
        [
            (0x00000001, "FRONT_LEFT"),        (0x00000002, "FRONT_RIGHT"),
            (0x00000004, "FRONT_CENTER"),      (0x00000008, "LOW_FREQUENCY"),
            (0x00000010, "BACK_LEFT"),         (0x00000020, "BACK_RIGHT"),
            (0x00000040, "FRONT_LEFT_OF_CENTER"), (0x00000080, "FRONT_RIGHT_OF_CENTER"),
            (0x00000100, "BACK_CENTER"),       (0x00000200, "SIDE_LEFT"),
            (0x00000400, "SIDE_RIGHT"),        (0x00000800, "TOP_CENTER"),
            (0x00001000, "TOP_FRONT_LEFT"),    (0x00002000, "TOP_FRONT_CENTER"),
            (0x00004000, "TOP_FRONT_RIGHT"),   (0x00008000, "TOP_BACK_LEFT"),
            (0x00010000, "TOP_BACK_CENTER"),   (0x00020000, "TOP_BACK_RIGHT"),
        ];

        /// <summary>
        /// 把结构字段追加到 dataLines，返回已解析的字节数（0 表示数据不足，已写入 &lt;Error&gt;）。
        /// 不发射 &lt;PayloadLength&gt;，由调用方按剩余量决定。
        /// </summary>
        public static long Populate(
            RIFFParser parser,
            long payloadStart,
            long payloadLength,
            bool bigEndian,
            List<(string K, string V)> dataLines)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(dataLines);

            Span<byte> buffer = stackalloc byte[MaxStructSize];
            int want = (int)Math.Min(MaxStructSize, Math.Max(0, payloadLength));
            int read = want > 0 ? parser.ReadAt(payloadStart, buffer[..want]) : 0;

            if (read < 14)
            {
                dataLines.Add(("<Error>", $"WAVEFORMAT requires at least 14 bytes, {read} available."));
                return 0;
            }

            ushort formatTag = RIFFUtil.ReadUInt16(buffer.Slice(0, 2), bigEndian);
            ushort channels = RIFFUtil.ReadUInt16(buffer.Slice(2, 2), bigEndian);
            uint samplesPerSec = RIFFUtil.ReadUInt32(buffer.Slice(4, 4), bigEndian);
            uint avgBytesPerSec = RIFFUtil.ReadUInt32(buffer.Slice(8, 4), bigEndian);
            ushort blockAlign = RIFFUtil.ReadUInt16(buffer.Slice(12, 2), bigEndian);
            long parsedBytes = 14;

            dataLines.Add(("wFormatTag", $"0x{formatTag:X4}"));
            dataLines.Add(("<wFormatTag>", DescribeFormatTag(formatTag)));
            dataLines.Add(("nChannels", channels.ToString()));
            dataLines.Add(("nSamplesPerSec", samplesPerSec.ToString()));
            dataLines.Add(("nAvgBytesPerSec", avgBytesPerSec.ToString()));
            dataLines.Add(("nBlockAlign", blockAlign.ToString()));

            ushort bitsPerSample = 0;
            if (read >= 16)
            {
                bitsPerSample = RIFFUtil.ReadUInt16(buffer.Slice(14, 2), bigEndian);
                dataLines.Add(("wBitsPerSample", bitsPerSample.ToString()));
                parsedBytes = 16;
            }
            else
            {
                dataLines.Add(("<Note>", "14-byte WAVEFORMAT: wBitsPerSample is absent."));
            }

            if (formatTag is WaveFormatPcm or WaveFormatIeeeFloat && channels > 0 && bitsPerSample > 0)
            {
                int expectedAlign = channels * (bitsPerSample / 8);
                if (expectedAlign > 0 && blockAlign != expectedAlign)
                    dataLines.Add(("<Warning>", $"nBlockAlign should be {expectedAlign}, found {blockAlign}."));

                long expectedRate = (long)samplesPerSec * (blockAlign > 0 ? blockAlign : expectedAlign);
                if (expectedRate > 0 && avgBytesPerSec != expectedRate)
                    dataLines.Add(("<Warning>", $"nAvgBytesPerSec should be {expectedRate}, found {avgBytesPerSec}."));
            }

            if (read >= 18)
            {
                ushort cbSize = RIFFUtil.ReadUInt16(buffer.Slice(16, 2), bigEndian);
                dataLines.Add(("cbSize", cbSize.ToString()));
                parsedBytes = 18;

                if (formatTag == WaveFormatExtensible)
                {
                    if (cbSize >= 22 && read >= 40)
                    {
                        ushort validBits = RIFFUtil.ReadUInt16(buffer.Slice(18, 2), bigEndian);
                        uint channelMask = RIFFUtil.ReadUInt32(buffer.Slice(20, 4), bigEndian);
                        ReadOnlySpan<byte> subFormatRaw = buffer.Slice(24, 16);

                        dataLines.Add(("wValidBitsPerSample", validBits.ToString()));
                        dataLines.Add(("dwChannelMask", $"0x{channelMask:X8}"));
                        dataLines.Add(("<dwChannelMask>", DescribeChannelMask(channelMask, channels)));
                        dataLines.Add(("SubFormat", new Guid(subFormatRaw).ToString("B").ToUpperInvariant()));
                        dataLines.Add(("<SubFormat>", DescribeSubFormat(subFormatRaw, bigEndian)));
                        parsedBytes = 40;

                        if (bitsPerSample > 0 && validBits > bitsPerSample)
                        {
                            dataLines.Add(("<Warning>",
                                $"wValidBitsPerSample ({validBits}) exceeds wBitsPerSample ({bitsPerSample})."));
                        }
                    }
                    else
                    {
                        dataLines.Add(("<Warning>",
                            "Tagged as WAVE_FORMAT_EXTENSIBLE but the extension is incomplete "
                            + "(cbSize < 22 or data missing)."));
                    }
                }
            }
            else if (formatTag != WaveFormatPcm)
            {
                //dataLines.Add(("<Note>", "Non-PCM formats are expected to carry cbSize (structure length >= 18)."));
            }

            return parsedBytes;
        }

        private static string DescribeFormatTag(ushort tag) =>
            FormatNames.TryGetValue(tag, out string? name) ? name : "Unknown format tag";

        private static string DescribeChannelMask(uint mask, ushort channels)
        {
            if (mask == 0) return "Not specified; speaker positions are undefined";

            string text = AviUtil.DescribeFlags(mask, SpeakerFlags);

            int count = 0;
            foreach ((uint flag, _) in SpeakerFlags)
                if ((mask & flag) != 0) count++;

            return count == channels ? text : $"{text} [{count} position(s) for {channels} channel(s)]";
        }

        private static string DescribeSubFormat(ReadOnlySpan<byte> raw, bool bigEndian)
        {
            if (!raw[4..16].SequenceEqual(KsSubTypeSuffix))
                return "Non-standard subformat GUID";

            ushort tag = RIFFUtil.ReadUInt16(raw[..2], bigEndian);
            return $"KSDATAFORMAT_SUBTYPE (0x{tag:X4}: {DescribeFormatTag(tag)})";
        }
    }
}