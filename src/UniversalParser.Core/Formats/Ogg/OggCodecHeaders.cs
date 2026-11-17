using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Ogg;

/// <summary>
/// Codec header packets carried inside Ogg pages. Field names follow each media mapping:
/// Vorbis I specification section 4.2/5, RFC 7845 section 5, Theora specification section 6,
/// the Speex <c>SpeexHeader</c> structure, the FLAC-to-Ogg mapping, and the Ogg Skeleton draft.
/// </summary>
internal static class OggCodecHeaders
{
    private static ReadOnlySpan<byte> FlacNativeSignature => "fLaC"u8;

    public static void Parse(
        FieldList fields,
        BlockReader reader,
        OggHeaderKind kind,
        string prefix,
        int maxStringBytes)
    {
        switch (kind)
        {
            case OggHeaderKind.VorbisIdentification:
                ParseVorbisIdentification(fields, reader, prefix);
                break;
            case OggHeaderKind.VorbisComment:
                reader.Skip(7);
                VorbisComment.Parse(fields, reader, prefix, VorbisCommentNaming.LowerCase, hasFramingBit: true, maxStringBytes);
                break;
            case OggHeaderKind.VorbisSetup:
                reader.Skip(7);
                fields.Note("The Vorbis setup header codebooks are not interpreted.", reader.CurrentRange);
                reader.SkipRemaining();
                break;
            case OggHeaderKind.OpusHead:
                ParseOpusHead(fields, reader, prefix);
                break;
            case OggHeaderKind.OpusTags:
                ParseOpusTags(fields, reader, prefix, maxStringBytes);
                break;
            case OggHeaderKind.TheoraIdentification:
                ParseTheoraIdentification(fields, reader, prefix);
                break;
            case OggHeaderKind.TheoraComment:
                reader.Skip(7);
                VorbisComment.Parse(fields, reader, prefix, VorbisCommentNaming.LowerCase, hasFramingBit: false, maxStringBytes);
                break;
            case OggHeaderKind.TheoraSetup:
                reader.Skip(7);
                fields.Note("The Theora setup header quantisation and Huffman tables are not interpreted.", reader.CurrentRange);
                reader.SkipRemaining();
                break;
            case OggHeaderKind.SpeexHeader:
                ParseSpeexHeader(fields, reader, prefix);
                break;
            case OggHeaderKind.FlacMapping:
                ParseFlacMapping(fields, reader, prefix);
                break;
            case OggHeaderKind.SkeletonFishead:
                ParseFishead(fields, reader, prefix);
                break;
            case OggHeaderKind.SkeletonFisbone:
                ParseFisbone(fields, reader, prefix, maxStringBytes);
                break;
            default:
                fields.Note("This packet header is not interpreted.", reader.CurrentRange);
                reader.SkipRemaining();
                break;
        }
    }

    private static void ParseVorbisIdentification(FieldList fields, BlockReader reader, string prefix)
    {
        reader.Skip(7);
        if (!reader.TryUInt32LE(out var version))
        {
            Truncated(fields, reader, "Vorbis identification header");
            return;
        }

        fields.Raw(prefix + "vorbis_version", version, reader.LastRange);
        if (version != 0)
        {
            fields.Warning(DiagnosticCodes.UnsupportedVersion, "The Vorbis version is not 0.", reader.LastRange);
        }

        if (!reader.TryByte(out var channels))
        {
            Truncated(fields, reader, "Vorbis identification header");
            return;
        }

        fields.Raw(prefix + "audio_channels", channels.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        if (channels == 0)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, "audio_channels must be greater than zero.", reader.LastRange);
        }

        if (!reader.TryUInt32LE(out var sampleRate))
        {
            Truncated(fields, reader, "Vorbis identification header");
            return;
        }

        fields.Raw(prefix + "audio_sample_rate", sampleRate, reader.LastRange);
        if (sampleRate == 0)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, "audio_sample_rate must be greater than zero.", reader.LastRange);
        }

        if (!TrySigned(fields, reader, prefix + "bitrate_maximum", out _)
            || !TrySigned(fields, reader, prefix + "bitrate_nominal", out _)
            || !TrySigned(fields, reader, prefix + "bitrate_minimum", out _))
        {
            Truncated(fields, reader, "Vorbis identification header");
            return;
        }

        if (!reader.TryByte(out var blocksizes))
        {
            Truncated(fields, reader, "Vorbis identification header");
            return;
        }

        var blocksize0 = blocksizes & 0x0F;
        var blocksize1 = blocksizes >> 4;
        fields.Raw(prefix + "blocksize_0", FormatBlockSize((byte)blocksize0), reader.LastRange);
        fields.Raw(prefix + "blocksize_1", FormatBlockSize((byte)blocksize1), reader.LastRange);
        if (blocksize0 is < 6 or > 13 || blocksize1 is < 6 or > 13 || blocksize0 > blocksize1)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The Vorbis block sizes must be between 6 and 13 and blocksize_0 must not exceed blocksize_1.",
                reader.LastRange);
        }

        if (!reader.TryByte(out var framing))
        {
            Truncated(fields, reader, "Vorbis identification header");
            return;
        }

        fields.Raw(prefix + "framing_flag", framing.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        if (framing == 0)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, "The Vorbis identification framing bit is not set.", reader.LastRange);
        }

        if (reader.Remaining > 0)
        {
            fields.Note($"{reader.Remaining} byte(s) after the Vorbis identification header are not interpreted.", reader.CurrentRange);
            reader.SkipRemaining();
        }
    }

    private static void ParseOpusHead(FieldList fields, BlockReader reader, string prefix)
    {
        reader.Skip(8);
        if (!reader.TryByte(out var version))
        {
            Truncated(fields, reader, "OpusHead");
            return;
        }

        fields.Raw(prefix + "Version", version.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        if (version > 15)
        {
            fields.Warning(DiagnosticCodes.UnsupportedVersion, "The OpusHead version is not compatible with RFC 7845.", reader.LastRange);
        }

        if (!reader.TryByte(out var channelCount))
        {
            Truncated(fields, reader, "OpusHead");
            return;
        }

        fields.Raw(prefix + "Output Channel Count", channelCount.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        if (channelCount == 0)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, "Output Channel Count must not be zero.", reader.LastRange);
        }

        if (!reader.TryUInt16LE(out var preSkip))
        {
            Truncated(fields, reader, "OpusHead");
            return;
        }

        fields.Raw(prefix + "Pre-skip", preSkip, reader.LastRange);

        if (!reader.TryUInt32LE(out var inputSampleRate))
        {
            Truncated(fields, reader, "OpusHead");
            return;
        }

        fields.Raw(prefix + "Input Sample Rate", inputSampleRate, reader.LastRange);

        if (!reader.TryInt16LE(out var outputGain))
        {
            Truncated(fields, reader, "OpusHead");
            return;
        }

        fields.Raw(prefix + "Output Gain", outputGain.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Derived(DerivedKey(prefix, "Output Gain dB"), OggFormatting.FormatQ78(outputGain), reader.LastRange);

        if (!reader.TryByte(out var mappingFamily))
        {
            Truncated(fields, reader, "OpusHead");
            return;
        }

        fields.Raw(prefix + "Channel Mapping Family", mappingFamily.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        if (mappingFamily == 0)
        {
            return;
        }

        if (!reader.TryByte(out var streamCount) || !reader.TryByte(out var coupledCount))
        {
            Truncated(fields, reader, "OpusHead channel mapping table");
            return;
        }

        fields.Raw(prefix + "Stream Count", streamCount.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw(prefix + "Coupled Count", coupledCount.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        if (streamCount == 0)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, "Stream Count must not be zero.", reader.LastRange);
        }

        if (reader.Remaining < channelCount)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                "The channel mapping table is truncated.",
                reader.CurrentRange);
            reader.SkipRemaining();
            return;
        }

        for (var index = 0; index < channelCount; index++)
        {
            _ = reader.TryByte(out var mapped);
            fields.Raw(
                $"{prefix}Channel Mapping[{index}]",
                mapped.ToString(CultureInfo.InvariantCulture),
                reader.LastRange);
        }
    }

    private static void ParseOpusTags(FieldList fields, BlockReader reader, string prefix, int maxStringBytes)
    {
        reader.Skip(8);
        if (!reader.TryUInt32LE(out var vendorLength))
        {
            Truncated(fields, reader, "OpusTags");
            return;
        }

        fields.Raw(prefix + "Vendor String Length", vendorLength, reader.LastRange);
        if (vendorLength > reader.Remaining)
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The vendor string extends beyond the packet.", reader.CurrentRange);
            reader.SkipRemaining();
            return;
        }

        var vendor = reader.ReadText(vendorLength, maxStringBytes, out var vendorRange, out var vendorTruncated);
        fields.Raw(prefix + "Vendor String", vendor, vendorRange);
        if (vendorTruncated)
        {
            fields.Warning(DiagnosticCodes.LimitString, "'Vendor String' is shown as a prefix.", vendorRange);
        }

        if (!reader.TryUInt32LE(out var commentCount))
        {
            Truncated(fields, reader, "OpusTags");
            return;
        }

        fields.Raw(prefix + "User Comment List Length", commentCount, reader.LastRange);
        var maximumPossible = (uint)Math.Min(reader.Remaining / 4, uint.MaxValue);
        if (commentCount > maximumPossible)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The user comment count exceeds the remaining packet bytes; the list is clamped.",
                reader.LastRange);
            commentCount = maximumPossible;
        }

        for (var index = 0u; index < commentCount; index++)
        {
            if (!reader.TryUInt32LE(out var length))
            {
                break;
            }

            fields.Raw($"{prefix}User Comment #{index} String Length", length, reader.LastRange);
            if (length > reader.Remaining)
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"User Comment #{index} extends beyond the packet.",
                    reader.CurrentRange);
                reader.SkipRemaining();
                return;
            }

            var comment = reader.ReadText(length, maxStringBytes, out var range, out var truncated);
            fields.Raw($"{prefix}User Comment #{index} String", comment, range);
            if (truncated)
            {
                fields.Warning(DiagnosticCodes.LimitString, $"User Comment #{index} is shown as a prefix.", range);
            }
        }

        if (reader.Remaining > 0)
        {
            fields.Note(
                $"{reader.Remaining} byte(s) of optional binary data follow the user comment list.",
                reader.CurrentRange);
            reader.SkipRemaining();
        }
    }

    private static void ParseTheoraIdentification(FieldList fields, BlockReader reader, string prefix)
    {
        reader.Skip(7);
        if (!reader.TryByte(out var major) || !reader.TryByte(out var minor) || !reader.TryByte(out var revision))
        {
            Truncated(fields, reader, "Theora identification header");
            return;
        }

        fields.Raw(prefix + "VMAJ", major.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw(prefix + "VMIN", minor.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw(prefix + "VREV", revision.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        if (major != 3 || minor != 2)
        {
            fields.Warning(
                DiagnosticCodes.UnsupportedVersion,
                "The Theora version is not 3.2.",
                reader.LastRange);
        }

        if (!reader.TryUInt16BE(out var frameWidth) || !reader.TryUInt16BE(out var frameHeight))
        {
            Truncated(fields, reader, "Theora identification header");
            return;
        }

        fields.Raw(prefix + "FMBW", frameWidth, reader.LastRange);
        fields.Derived(DerivedKey(prefix, "FrameWidth"), frameWidth * 16, reader.LastRange);
        fields.Raw(prefix + "FMBH", frameHeight, reader.LastRange);
        fields.Derived(DerivedKey(prefix, "FrameHeight"), frameHeight * 16, reader.LastRange);

        if (!reader.TryUInt24BE(out var pictureWidth) || !reader.TryUInt24BE(out var pictureHeight))
        {
            Truncated(fields, reader, "Theora identification header");
            return;
        }

        fields.Raw(prefix + "PICW", pictureWidth, reader.LastRange);
        fields.Raw(prefix + "PICH", pictureHeight, reader.LastRange);

        if (!reader.TryByte(out var pictureX) || !reader.TryByte(out var pictureY))
        {
            Truncated(fields, reader, "Theora identification header");
            return;
        }

        fields.Raw(prefix + "PICX", pictureX.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw(prefix + "PICY", pictureY.ToString(CultureInfo.InvariantCulture), reader.LastRange);

        if (!reader.TryUInt32BE(out var frameRateNumerator) || !reader.TryUInt32BE(out var frameRateDenominator))
        {
            Truncated(fields, reader, "Theora identification header");
            return;
        }

        fields.Raw(prefix + "FRN", frameRateNumerator, reader.LastRange);
        fields.Raw(prefix + "FRD", frameRateDenominator, reader.LastRange);
        fields.Derived(
            DerivedKey(prefix, "FramesPerSecond"),
            OggFormatting.FormatRatio(frameRateNumerator, frameRateDenominator),
            reader.LastRange);

        if (!reader.TryUInt24BE(out var aspectNumerator) || !reader.TryUInt24BE(out var aspectDenominator))
        {
            Truncated(fields, reader, "Theora identification header");
            return;
        }

        fields.Raw(prefix + "PARN", aspectNumerator, reader.LastRange);
        fields.Raw(prefix + "PARD", aspectDenominator, reader.LastRange);
        if (aspectNumerator != 0 && aspectDenominator != 0)
        {
            fields.Derived(
                DerivedKey(prefix, "PixelAspectRatio"),
                OggFormatting.FormatRatio(aspectNumerator, aspectDenominator),
                reader.LastRange);
        }

        if (!reader.TryByte(out var colorSpace))
        {
            Truncated(fields, reader, "Theora identification header");
            return;
        }

        fields.Raw(prefix + "CS", OggFormatting.FormatColorSpace(colorSpace), reader.LastRange);

        if (!reader.TryUInt24BE(out var nominalBitrate))
        {
            Truncated(fields, reader, "Theora identification header");
            return;
        }

        fields.Raw(prefix + "NOMBR", nominalBitrate, reader.LastRange);

        if (!reader.TryUInt16BE(out var tail))
        {
            Truncated(fields, reader, "Theora identification header");
            return;
        }

        var quality = (tail >> 10) & 0x3F;
        var keyframeShift = (tail >> 5) & 0x1F;
        var pixelFormat = (tail >> 3) & 0x03;
        var reserved = tail & 0x07;
        fields.Raw(prefix + "QUAL", quality.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw(prefix + "KFGSHIFT", keyframeShift.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw(prefix + "PF", OggFormatting.FormatPixelFormat((byte)pixelFormat), reader.LastRange);
        fields.Raw(prefix + "Res", reserved.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        if (reserved != 0)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, "The reserved Theora identification header bits are not zero.", reader.LastRange);
        }

        if (reader.Remaining > 0)
        {
            fields.Note($"{reader.Remaining} byte(s) after the Theora identification header are not interpreted.", reader.CurrentRange);
            reader.SkipRemaining();
        }
    }

    private static void ParseSpeexHeader(FieldList fields, BlockReader reader, string prefix)
    {
        reader.Skip(8);
        Span<byte> versionBytes = stackalloc byte[20];
        if (!reader.TryReadBytes(versionBytes))
        {
            Truncated(fields, reader, "Speex header");
            return;
        }

        var version = TextEscaping(versionBytes);
        fields.Raw(prefix + "speex_version", version, reader.LastRange);

        if (!ReadInt32(fields, reader, prefix, "speex_version_id")
            || !ReadInt32(fields, reader, prefix, "header_size")
            || !ReadInt32(fields, reader, prefix, "rate")
            || !ReadInt32(fields, reader, prefix, "mode")
            || !ReadInt32(fields, reader, prefix, "mode_bitstream_version")
            || !ReadInt32(fields, reader, prefix, "nb_channels")
            || !ReadInt32(fields, reader, prefix, "bitrate")
            || !ReadInt32(fields, reader, prefix, "frame_size")
            || !ReadInt32(fields, reader, prefix, "vbr")
            || !ReadInt32(fields, reader, prefix, "frames_per_packet")
            || !ReadInt32(fields, reader, prefix, "extra_headers")
            || !ReadInt32(fields, reader, prefix, "reserved1")
            || !ReadInt32(fields, reader, prefix, "reserved2"))
        {
            Truncated(fields, reader, "Speex header");
            return;
        }

        if (reader.Remaining > 0)
        {
            fields.Note($"{reader.Remaining} byte(s) after the Speex header are not interpreted.", reader.CurrentRange);
            reader.SkipRemaining();
        }
    }

    private static void ParseFlacMapping(FieldList fields, BlockReader reader, string prefix)
    {
        reader.Skip(5);
        if (!reader.TryByte(out var major) || !reader.TryByte(out var minor) || !reader.TryUInt16BE(out var headerPackets))
        {
            Truncated(fields, reader, "Ogg FLAC mapping header");
            return;
        }

        fields.Raw(prefix + "Major version", major.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw(prefix + "Minor version", minor.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw(prefix + "Number of header packets", headerPackets, reader.LastRange);

        Span<byte> signature = stackalloc byte[4];
        if (!reader.TryReadBytes(signature))
        {
            Truncated(fields, reader, "Ogg FLAC mapping header");
            return;
        }

        if (!signature.SequenceEqual(FlacNativeSignature))
        {
            fields.Warning(DiagnosticCodes.InvalidSignature, "The native FLAC signature 'fLaC' is missing.", reader.LastRange);
            reader.SkipRemaining();
            return;
        }

        if (!reader.TryByte(out var metadataHeader) || !reader.TryUInt24BE(out var metadataLength))
        {
            Truncated(fields, reader, "FLAC metadata block header");
            return;
        }

        fields.Raw(
            prefix + "LAST_METADATA_BLOCK_FLAG",
            ((metadataHeader & 0x80) != 0 ? 1 : 0).ToString(CultureInfo.InvariantCulture),
            reader.LastRange);
        fields.Raw(
            prefix + "BLOCK_TYPE",
            string.Create(CultureInfo.InvariantCulture, $"{metadataHeader & 0x7F} (STREAMINFO)"),
            reader.LastRange);
        fields.Raw(prefix + "LENGTH", metadataLength, reader.LastRange);
        if ((metadataHeader & 0x7F) != 0)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The first FLAC metadata block is not STREAMINFO.",
                reader.LastRange);
            reader.SkipRemaining();
            return;
        }

        if (!FlacStreamInfo.TryParse(fields, reader, prefix))
        {
            return;
        }

        if (reader.Remaining > 0)
        {
            fields.Note($"{reader.Remaining} byte(s) after STREAMINFO are not interpreted.", reader.CurrentRange);
            reader.SkipRemaining();
        }
    }

    private static void ParseFishead(FieldList fields, BlockReader reader, string prefix)
    {
        reader.Skip(8);
        if (!reader.TryUInt16LE(out var major) || !reader.TryUInt16LE(out var minor))
        {
            Truncated(fields, reader, "Skeleton fishead");
            return;
        }

        fields.Raw(prefix + "Version major", major, reader.LastRange);
        fields.Raw(prefix + "Version minor", minor, reader.LastRange);

        if (!reader.TryUInt64LE(out var presentationNumerator)
            || !reader.TryUInt64LE(out var presentationDenominator))
        {
            Truncated(fields, reader, "Skeleton fishead");
            return;
        }

        fields.Raw(prefix + "Presentationtime numerator", presentationNumerator, reader.LastRange);
        fields.Raw(prefix + "Presentationtime denominator", presentationDenominator, reader.LastRange);

        if (!reader.TryInt64LE(out var baseNumerator) || !reader.TryInt64LE(out var baseDenominator))
        {
            Truncated(fields, reader, "Skeleton fishead");
            return;
        }

        fields.Raw(prefix + "Basetime numerator", baseNumerator.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw(prefix + "Basetime denominator", baseDenominator.ToString(CultureInfo.InvariantCulture), reader.LastRange);

        Span<byte> utc = stackalloc byte[20];
        if (!reader.TryReadBytes(utc))
        {
            Truncated(fields, reader, "Skeleton fishead");
            return;
        }

        fields.Raw(prefix + "UTC", TextEscaping(utc), reader.LastRange);

        if (reader.Remaining >= 16)
        {
            _ = reader.TryUInt64LE(out var segmentLength);
            _ = reader.TryUInt64LE(out var contentOffset);
            fields.Raw(prefix + "Segment length", segmentLength, reader.LastRange);
            fields.Raw(prefix + "Content byte offset", contentOffset, reader.LastRange);
        }
        else if (reader.Remaining > 0)
        {
            fields.Note("The Skeleton fishead version 3 layout ends after the UTC field.", reader.CurrentRange);
            reader.SkipRemaining();
        }
    }

    private static void ParseFisbone(FieldList fields, BlockReader reader, string prefix, int maxStringBytes)
    {
        reader.Skip(8);
        if (!reader.TryUInt32LE(out var messageHeaderOffset)
            || !reader.TryUInt32LE(out var serialNumber)
            || !reader.TryUInt32LE(out var headerPackets))
        {
            Truncated(fields, reader, "Skeleton fisbone");
            return;
        }

        fields.Raw(prefix + "Offset to message header fields", messageHeaderOffset, reader.LastRange);
        fields.Raw(prefix + "Serial number", serialNumber, reader.LastRange);
        fields.Raw(prefix + "Number of header packets", headerPackets, reader.LastRange);

        if (!reader.TryInt64LE(out var granuleNumerator) || !reader.TryInt64LE(out var granuleDenominator))
        {
            Truncated(fields, reader, "Skeleton fisbone");
            return;
        }

        fields.Raw(prefix + "Granulerate numerator", granuleNumerator.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        fields.Raw(prefix + "Granulerate denominator", granuleDenominator.ToString(CultureInfo.InvariantCulture), reader.LastRange);

        if (!reader.TryInt64LE(out var baseGranule))
        {
            Truncated(fields, reader, "Skeleton fisbone");
            return;
        }

        fields.Raw(prefix + "Basegranule", baseGranule.ToString(CultureInfo.InvariantCulture), reader.LastRange);

        if (!reader.TryUInt32LE(out var preroll) || !reader.TryByte(out var granuleShift))
        {
            Truncated(fields, reader, "Skeleton fisbone");
            return;
        }

        fields.Raw(prefix + "Preroll", preroll, reader.LastRange);
        fields.Raw(prefix + "Granuleshift", granuleShift.ToString(CultureInfo.InvariantCulture), reader.LastRange);

        Span<byte> padding = stackalloc byte[3];
        if (reader.TryReadBytes(padding))
        {
            fields.Raw(prefix + "Padding/future use", Convert.ToHexStringLower(padding), reader.LastRange);
        }

        if (messageHeaderOffset < 44)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The fisbone message header offset is smaller than the fixed 44-byte prefix.",
                reader.LastRange);
            reader.SkipRemaining();
            return;
        }

        if (messageHeaderOffset > 44)
        {
            reader.Skip(messageHeaderOffset - 44);
        }

        if (reader.Remaining <= 0)
        {
            return;
        }

        var length = reader.Remaining;
        var count = checked((int)Math.Min(length, maxStringBytes));
        var buffer = new byte[count];
        if (!reader.TryReadBytes(buffer))
        {
            return;
        }

        var range = new SourceRange(reader.Position - count, count);
        var text = Encoding.UTF8.GetString(buffer);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var index = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            fields.Raw($"{prefix}Message header[{index}]", EscapeText(line), range);
            index++;
        }

        if (length > maxStringBytes)
        {
            fields.Warning(DiagnosticCodes.LimitString, "The fisbone message headers are shown as a prefix.", range);
        }

        reader.SkipRemaining();
    }

    private static bool ReadInt32(FieldList fields, BlockReader reader, string prefix, string name)
    {
        if (!reader.TryUInt32LE(out var raw))
        {
            return false;
        }

        fields.Raw(prefix + name, unchecked((int)raw).ToString(CultureInfo.InvariantCulture), reader.LastRange);
        return true;
    }

    private static bool TrySigned(FieldList fields, BlockReader reader, string key, out int value)
    {
        if (!reader.TryUInt32LE(out var raw))
        {
            value = 0;
            return false;
        }

        value = unchecked((int)raw);
        fields.Raw(key, value.ToString(CultureInfo.InvariantCulture), reader.LastRange);
        return true;
    }

    private static string DerivedKey(string prefix, string name) => $"<{prefix}{name}>";

    private static string FormatBlockSize(byte exponent) =>
        exponent <= 30
            ? $"{exponent.ToString(CultureInfo.InvariantCulture)} ({1 << exponent} samples)"
            : exponent.ToString(CultureInfo.InvariantCulture);

    private static string TextEscaping(ReadOnlySpan<byte> bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        return UniversalParser.Core.Formatting.TextEscaping.Escape(text);
    }

    private static string EscapeText(string text) =>
        UniversalParser.Core.Formatting.TextEscaping.Escape(text);

    private static void Truncated(FieldList fields, BlockReader reader, string structure) =>
        fields.Warning(
            DiagnosticCodes.TruncatedPayload,
            $"The {structure} is truncated.",
            reader.CurrentRange);
}
