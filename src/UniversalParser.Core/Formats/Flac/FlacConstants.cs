namespace UniversalParser.Core.Formats.Flac;

/// <summary>
/// FLAC metadata block types and value interpretations (RFC 9639 section 8). The structure
/// names are the specification's uppercase identifiers.
/// </summary>
internal static class FlacConstants
{
    public const int SignatureLength = 4;
    public const int BlockHeaderLength = 4;
    public const int MetadataBlockMaxLength = 0xFFFFFF;
    public const int StreamInfoLength = 34;
    public const int CuesheetHeaderLength = 396;
    public const int CuesheetTrackHeaderLength = 36;
    public const int CuesheetIndexLength = 12;
    public const int SeekPointLength = 18;

    public const byte StreamInfoType = 0;
    public const byte PaddingType = 1;
    public const byte ApplicationType = 2;
    public const byte SeekTableType = 3;
    public const byte VorbisCommentType = 4;
    public const byte CuesheetType = 5;
    public const byte PictureType = 6;
    public const byte ForbiddenType = 127;

    public static ReadOnlySpan<byte> Signature => "fLaC"u8;

    public static string GetBlockName(byte type) => type switch
    {
        StreamInfoType => "STREAMINFO",
        PaddingType => "PADDING",
        ApplicationType => "APPLICATION",
        SeekTableType => "SEEKTABLE",
        VorbisCommentType => "VORBIS_COMMENT",
        CuesheetType => "CUESHEET",
        PictureType => "PICTURE",
        _ => "RESERVED",
    };

    public static string FormatPictureType(uint type) => type switch
    {
        0 => "0 (Other)",
        1 => "1 (PNG file icon of 32x32 pixels)",
        2 => "2 (General file icon)",
        3 => "3 (Front cover)",
        4 => "4 (Back cover)",
        5 => "5 (Liner notes page)",
        6 => "6 (Media label)",
        7 => "7 (Lead artist, lead performer, or soloist)",
        8 => "8 (Artist or performer)",
        9 => "9 (Conductor)",
        10 => "10 (Band or orchestra)",
        11 => "11 (Composer)",
        12 => "12 (Lyricist or text writer)",
        13 => "13 (Recording location)",
        14 => "14 (During recording)",
        15 => "15 (During performance)",
        16 => "16 (Movie or video screen capture)",
        17 => "17 (A bright colored fish)",
        18 => "18 (Illustration)",
        19 => "19 (Band or artist logotype)",
        20 => "20 (Publisher or studio logotype)",
        _ => $"{type} (reserved)",
    };
}
