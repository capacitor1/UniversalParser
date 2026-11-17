using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>Shared WAVEFORMAT / WAVEFORMATEX / WAVEFORMATEXTENSIBLE field emitter.</summary>
internal static class RiffWaveFormat
{
    private const ushort WaveFormatPcm = 0x0001;
    private const ushort WaveFormatIeeeFloat = 0x0003;
    private const ushort WaveFormatExtensible = 0xFFFE;

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
        [0x0161] = "Windows Media Audio 2",
        [0x0162] = "Windows Media Audio 9 Professional",
        [0x0163] = "Windows Media Audio 9 Lossless",
        [0x2000] = "Dolby AC-3",
        [0x2001] = "DTS",
        [0xFFFE] = "Extensible",
        [0xFFFF] = "Development / Experimental",
    };

    private static readonly (uint Mask, string Name)[] SpeakerFlags =
    [
        (0x00000001, "FRONT_LEFT"), (0x00000002, "FRONT_RIGHT"),
        (0x00000004, "FRONT_CENTER"), (0x00000008, "LOW_FREQUENCY"),
        (0x00000010, "BACK_LEFT"), (0x00000020, "BACK_RIGHT"),
        (0x00000040, "FRONT_LEFT_OF_CENTER"), (0x00000080, "FRONT_RIGHT_OF_CENTER"),
        (0x00000100, "BACK_CENTER"), (0x00000200, "SIDE_LEFT"),
        (0x00000400, "SIDE_RIGHT"), (0x00000800, "TOP_CENTER"),
        (0x00001000, "TOP_FRONT_LEFT"), (0x00002000, "TOP_FRONT_CENTER"),
        (0x00004000, "TOP_FRONT_RIGHT"), (0x00008000, "TOP_BACK_LEFT"),
        (0x00010000, "TOP_BACK_CENTER"), (0x00020000, "TOP_BACK_RIGHT"),
    ];

    private static readonly byte[] KsSubTypeSuffix =
    [
        0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71,
    ];

    public static long Populate(RiffChunkContext context)
    {
        var data = context.ReadPayload(64);
        if (data.Length < 14)
        {
            context.Fields.Error(
                DiagnosticCodes.TruncatedPayload,
                $"WAVEFORMAT requires at least 14 bytes, {data.Length} available.",
                context.Layout.PayloadRange);
            return 0;
        }

        var endian = context.Endian;
        var formatTag = RiffRead.UInt16(data, 0, endian);
        var channels = RiffRead.UInt16(data, 2, endian);
        var samplesPerSec = RiffRead.UInt32(data, 4, endian);
        var avgBytesPerSec = RiffRead.UInt32(data, 8, endian);
        var blockAlign = RiffRead.UInt16(data, 12, endian);

        context.Fields.RawHex("wFormatTag", formatTag, 4, context.Range(0, 2));
        context.Fields.Derived("<wFormatTag>", DescribeFormatTag(formatTag), context.Range(0, 2));
        context.Fields.Raw("nChannels", channels, context.Range(2, 2));
        context.Fields.Raw("nSamplesPerSec", samplesPerSec, context.Range(4, 4));
        context.Fields.Raw("nAvgBytesPerSec", avgBytesPerSec, context.Range(8, 4));
        context.Fields.Raw("nBlockAlign", blockAlign, context.Range(12, 2));

        var parsed = 14L;
        ushort bitsPerSample = 0;
        if (data.Length >= 16)
        {
            bitsPerSample = RiffRead.UInt16(data, 14, endian);
            context.Fields.Raw("wBitsPerSample", bitsPerSample, context.Range(14, 2));
            parsed = 16;
        }
        else
        {
            context.Fields.Note("14-byte WAVEFORMAT: wBitsPerSample is absent.", context.Range(14, 0));
        }

        if (formatTag is WaveFormatPcm or WaveFormatIeeeFloat && channels > 0 && bitsPerSample > 0)
        {
            var expectedAlign = channels * (bitsPerSample / 8);
            if (expectedAlign > 0 && blockAlign != expectedAlign)
            {
                context.Fields.Warning(
                    DiagnosticCodes.InvalidLength,
                    $"nBlockAlign should be {expectedAlign}, found {blockAlign}.",
                    context.Range(12, 2));
            }

            var expectedRate = (long)samplesPerSec * (blockAlign > 0 ? blockAlign : expectedAlign);
            if (expectedRate > 0 && avgBytesPerSec != expectedRate)
            {
                context.Fields.Warning(
                    DiagnosticCodes.InvalidLength,
                    $"nAvgBytesPerSec should be {expectedRate}, found {avgBytesPerSec}.",
                    context.Range(8, 4));
            }
        }

        if (data.Length < 18)
        {
            context.AddTruncationWarning();
            return parsed;
        }

        var cbSize = RiffRead.UInt16(data, 16, endian);
        context.Fields.Raw("cbSize", cbSize, context.Range(16, 2));
        parsed = 18;

        if (formatTag == WaveFormatExtensible)
        {
            if (cbSize >= 22 && data.Length >= 40)
            {
                var validBits = RiffRead.UInt16(data, 18, endian);
                var channelMask = RiffRead.UInt32(data, 20, endian);
                var subFormat = data.AsSpan(24, 16);

                context.Fields.Raw("wValidBitsPerSample", validBits, context.Range(18, 2));
                context.Fields.RawHex("dwChannelMask", channelMask, 8, context.Range(20, 4));
                context.Fields.Derived("<dwChannelMask>", DescribeChannelMask(channelMask, channels), context.Range(20, 4));
                context.Fields.Raw("SubFormat", new Guid(subFormat).ToString("B").ToUpperInvariant(), context.Range(24, 16));
                context.Fields.Derived("<SubFormat>", DescribeSubFormat(subFormat, endian), context.Range(24, 16));
                parsed = 40;

                if (bitsPerSample > 0 && validBits > bitsPerSample)
                {
                    context.Fields.Warning(
                        DiagnosticCodes.InvalidLength,
                        $"wValidBitsPerSample ({validBits}) exceeds wBitsPerSample ({bitsPerSample}).",
                        context.Range(18, 2));
                }
            }
            else
            {
                context.Fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    "Tagged as WAVE_FORMAT_EXTENSIBLE but the extension is incomplete (cbSize < 22 or data missing).",
                    context.Layout.PayloadRange);
            }
        }
        else if (cbSize > 0 && data.Length > 18)
        {
            var extension = data.AsSpan(18, Math.Min(cbSize, data.Length - 18));
            context.Fields.Raw("ExtensionData", RiffFormatting.FormatHex(extension, 64), context.Range(18, extension.Length));
            parsed = 18 + extension.Length;
        }

        if (context.PayloadLength > parsed)
        {
            context.Fields.Derived("<UnparsedPayloadLength>", context.PayloadLength - parsed, context.Range(parsed, context.PayloadLength - parsed));
        }

        context.AddTruncationWarning();
        return parsed;
    }

    private static string DescribeFormatTag(ushort tag) =>
        FormatNames.TryGetValue(tag, out var name) ? name : "Unknown format tag";

    private static string DescribeChannelMask(uint mask, ushort channels)
    {
        if (mask == 0)
        {
            return "Not specified; speaker positions are undefined";
        }

        var names = SpeakerFlags.Where(flag => (mask & flag.Mask) != 0).Select(flag => flag.Name).ToArray();
        var text = names.Length == 0 ? "No defined speaker bits" : string.Join(" | ", names);
        return names.Length == channels ? text : $"{text} [{names.Length} position(s) for {channels} channel(s)]";
    }

    private static string DescribeSubFormat(ReadOnlySpan<byte> raw, Endian endian)
    {
        if (!raw[4..16].SequenceEqual(KsSubTypeSuffix))
        {
            return "Non-standard subformat GUID";
        }

        var tag = RiffRead.UInt16(raw, 0, endian);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"KSDATAFORMAT_SUBTYPE (0x{tag:X4}: {DescribeFormatTag(tag)})");
    }
}
