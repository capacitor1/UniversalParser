namespace UniversalParser.Core.Formats.Ogg;

internal enum OggCodecKind
{
    Unknown,
    Vorbis,
    Opus,
    Theora,
    Speex,
    Flac,
    Skeleton,
}

internal enum OggHeaderKind
{
    Unknown,
    VorbisIdentification,
    VorbisComment,
    VorbisSetup,
    OpusHead,
    OpusTags,
    TheoraIdentification,
    TheoraComment,
    TheoraSetup,
    SpeexHeader,
    FlacMapping,
    SkeletonFishead,
    SkeletonFisbone,
}

/// <summary>
/// Codec identification from the first bytes of a packet. The media mappings define these
/// magic values; they are never guessed from the file extension.
/// </summary>
internal static class OggCodec
{
    private static ReadOnlySpan<byte> OpusHead => "OpusHead"u8;
    private static ReadOnlySpan<byte> OpusTags => "OpusTags"u8;
    private static ReadOnlySpan<byte> VorbisSignature => "vorbis"u8;
    private static ReadOnlySpan<byte> TheoraSignature => "theora"u8;
    private static ReadOnlySpan<byte> SpeexSignature => "Speex   "u8;
    private static ReadOnlySpan<byte> FlacSignature => "FLAC"u8;
    private static ReadOnlySpan<byte> FisheadSignature => "fishead\0"u8;
    private static ReadOnlySpan<byte> FisboneSignature => "fisbone\0"u8;

    public static OggHeaderKind Identify(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length >= 7 && prefix[1..7].SequenceEqual(VorbisSignature))
        {
            return prefix[0] switch
            {
                0x01 => OggHeaderKind.VorbisIdentification,
                0x03 => OggHeaderKind.VorbisComment,
                0x05 => OggHeaderKind.VorbisSetup,
                _ => OggHeaderKind.Unknown,
            };
        }

        if (prefix.Length >= 7 && prefix[1..7].SequenceEqual(TheoraSignature))
        {
            return prefix[0] switch
            {
                0x80 => OggHeaderKind.TheoraIdentification,
                0x81 => OggHeaderKind.TheoraComment,
                0x82 => OggHeaderKind.TheoraSetup,
                _ => OggHeaderKind.Unknown,
            };
        }

        if (prefix.StartsWith(OpusHead))
        {
            return OggHeaderKind.OpusHead;
        }

        if (prefix.StartsWith(OpusTags))
        {
            return OggHeaderKind.OpusTags;
        }

        if (prefix.StartsWith(SpeexSignature))
        {
            return OggHeaderKind.SpeexHeader;
        }

        if (prefix.Length >= 5 && prefix[0] == 0x7F && prefix[1..5].SequenceEqual(FlacSignature))
        {
            return OggHeaderKind.FlacMapping;
        }

        if (prefix.StartsWith(FisheadSignature))
        {
            return OggHeaderKind.SkeletonFishead;
        }

        if (prefix.StartsWith(FisboneSignature))
        {
            return OggHeaderKind.SkeletonFisbone;
        }

        return OggHeaderKind.Unknown;
    }

    public static OggCodecKind GetCodec(OggHeaderKind kind) => kind switch
    {
        OggHeaderKind.VorbisIdentification or OggHeaderKind.VorbisComment or OggHeaderKind.VorbisSetup =>
            OggCodecKind.Vorbis,
        OggHeaderKind.OpusHead or OggHeaderKind.OpusTags => OggCodecKind.Opus,
        OggHeaderKind.TheoraIdentification or OggHeaderKind.TheoraComment or OggHeaderKind.TheoraSetup =>
            OggCodecKind.Theora,
        OggHeaderKind.SpeexHeader => OggCodecKind.Speex,
        OggHeaderKind.FlacMapping => OggCodecKind.Flac,
        OggHeaderKind.SkeletonFishead or OggHeaderKind.SkeletonFisbone => OggCodecKind.Skeleton,
        _ => OggCodecKind.Unknown,
    };

    public static string GetDisplayName(OggCodecKind codec) => codec switch
    {
        OggCodecKind.Vorbis => "Vorbis",
        OggCodecKind.Opus => "Opus",
        OggCodecKind.Theora => "Theora",
        OggCodecKind.Speex => "Speex",
        OggCodecKind.Flac => "FLAC",
        OggCodecKind.Skeleton => "Ogg Skeleton",
        _ => "Unknown",
    };

    public static string GetHeaderName(OggHeaderKind kind) => kind switch
    {
        OggHeaderKind.VorbisIdentification => "Vorbis identification header",
        OggHeaderKind.VorbisComment => "Vorbis comment header",
        OggHeaderKind.VorbisSetup => "Vorbis setup header",
        OggHeaderKind.OpusHead => "OpusHead",
        OggHeaderKind.OpusTags => "OpusTags",
        OggHeaderKind.TheoraIdentification => "Theora identification header",
        OggHeaderKind.TheoraComment => "Theora comment header",
        OggHeaderKind.TheoraSetup => "Theora setup header",
        OggHeaderKind.SpeexHeader => "Speex header",
        OggHeaderKind.FlacMapping => "Ogg FLAC mapping header",
        OggHeaderKind.SkeletonFishead => "Skeleton fishead",
        OggHeaderKind.SkeletonFisbone => "Skeleton fisbone",
        _ => "Unknown",
    };
}
