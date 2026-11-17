using System.Buffers.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// FLAC STREAMINFO metadata block body (RFC 9639 section 8.2), shared by native FLAC and the
/// Ogg FLAC mapping. Field names are the specification's uppercase FLAC identifiers.
/// </summary>
internal static class FlacStreamInfo
{
    public const int Length = 34;

    public static bool TryParse(FieldList fields, BlockReader reader, string prefix)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(reader);

        if (!reader.TryUInt16BE(out var minimumBlockSize)
            || !reader.TryUInt16BE(out var maximumBlockSize)
            || !reader.TryUInt24BE(out var minimumFrameSize)
            || !reader.TryUInt24BE(out var maximumFrameSize))
        {
            Truncated(fields, reader);
            return false;
        }

        fields.Raw(prefix + "MINIMUM_BLOCK_SIZE", minimumBlockSize, reader.LastRange);
        fields.Raw(prefix + "MAXIMUM_BLOCK_SIZE", maximumBlockSize, reader.LastRange);
        fields.Raw(prefix + "MINIMUM_FRAME_SIZE", minimumFrameSize, reader.LastRange);
        fields.Raw(prefix + "MAXIMUM_FRAME_SIZE", maximumFrameSize, reader.LastRange);

        Span<byte> packedBytes = stackalloc byte[8];
        if (!reader.TryReadBytes(packedBytes))
        {
            Truncated(fields, reader);
            return false;
        }

        var packed = BinaryPrimitives.ReadUInt64BigEndian(packedBytes);
        var sampleRate = (uint)(packed >> 44);
        var channels = (uint)((packed >> 41) & 0x07) + 1;
        var bitsPerSample = (uint)((packed >> 36) & 0x1F) + 1;
        var totalSamples = packed & 0x0FFFFFFFFFUL;
        fields.Raw(prefix + "SAMPLE_RATE", sampleRate, reader.LastRange);
        fields.Raw(prefix + "CHANNELS", channels, reader.LastRange);
        fields.Raw(prefix + "BITS_PER_SAMPLE", bitsPerSample, reader.LastRange);
        fields.Raw(prefix + "TOTAL_SAMPLES", totalSamples, reader.LastRange);

        Span<byte> md5 = stackalloc byte[16];
        if (!reader.TryReadBytes(md5))
        {
            Truncated(fields, reader);
            return false;
        }

        fields.Raw(prefix + "MD5_SIGNATURE", Convert.ToHexStringLower(md5), reader.LastRange);

        if (minimumBlockSize < 16 || maximumBlockSize < 16 || minimumBlockSize > maximumBlockSize)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The STREAMINFO block sizes must be in the 16-65535 range and the minimum must not exceed the maximum.",
                reader.LastRange);
        }

        if (bitsPerSample < 4)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                "BITS_PER_SAMPLE is below the FLAC minimum of 4.",
                reader.LastRange);
        }

        return true;
    }

    private static void Truncated(FieldList fields, BlockReader reader) =>
        fields.Warning(
            DiagnosticCodes.TruncatedPayload,
            "The STREAMINFO metadata block is truncated.",
            reader.CurrentRange);
}
