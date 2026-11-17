using System.Collections.Frozen;

namespace UniversalParser.Core.Formats.Ebml;

/// <summary>EBML element data type. Master elements own a child sequence; the rest are leaves.</summary>
internal enum ElementType
{
    Master,
    UnsignedInteger,
    SignedInteger,
    Float,
    String,
    Utf8String,
    Date,
    Binary,
}

internal readonly record struct ElementDefinition(string Name, ElementType Type);

/// <summary>
/// Element IDs and types used by the Matroska/WebM EBML schema (RFC 9559), plus legacy
/// Matroska elements that still occur in older files. Global elements (Void, CRC-32) are
/// valid in every master element and never terminate an unknown-sized parent.
/// </summary>
internal static class EbmlSchema
{
    public const ulong EbmlHeaderId = 0x1A45DFA3;
    public const ulong SegmentId = 0x18538067;
    public const ulong ClusterId = 0x1F43B675;
    public const ulong VoidId = 0xEC;
    public const ulong Crc32Id = 0xBF;

    private static readonly FrozenDictionary<ulong, ElementDefinition> Definitions = new Dictionary<ulong, ElementDefinition>
    {
        [0x80] = new("ChapterDisplay", ElementType.Master),
        [0x83] = new("TrackType", ElementType.UnsignedInteger),
        [0x85] = new("ChapString", ElementType.Utf8String),
        [0x86] = new("CodecID", ElementType.String),
        [0x88] = new("FlagDefault", ElementType.UnsignedInteger),
        [0x89] = new("ChapterTrackUID", ElementType.UnsignedInteger),
        [0x8F] = new("ChapterTrack", ElementType.Master),
        [0x91] = new("ChapterTimeStart", ElementType.UnsignedInteger),
        [0x92] = new("ChapterTimeEnd", ElementType.UnsignedInteger),
        [0x96] = new("CueRefTime", ElementType.UnsignedInteger),
        [0x98] = new("ChapterFlagHidden", ElementType.UnsignedInteger),
        [0x9A] = new("FlagInterlaced", ElementType.UnsignedInteger),
        [0x9B] = new("BlockDuration", ElementType.UnsignedInteger),
        [0x9C] = new("FlagLacing", ElementType.UnsignedInteger),
        [0x9D] = new("FieldOrder", ElementType.UnsignedInteger),
        [0x9F] = new("Channels", ElementType.UnsignedInteger),
        [0xA0] = new("BlockGroup", ElementType.Master),
        [0xA1] = new("Block", ElementType.Binary),
        [0xA2] = new("BlockVirtual", ElementType.Binary),
        [0xA3] = new("SimpleBlock", ElementType.Binary),
        [0xA4] = new("CodecState", ElementType.Binary),
        [0xA5] = new("BlockAdditional", ElementType.Binary),
        [0xA6] = new("BlockMore", ElementType.Master),
        [0xA7] = new("Position", ElementType.UnsignedInteger),
        [0xAB] = new("PrevSize", ElementType.UnsignedInteger),
        [0xAE] = new("TrackEntry", ElementType.Master),
        [0xAF] = new("EncryptedBlock", ElementType.Binary),
        [0xB0] = new("PixelWidth", ElementType.UnsignedInteger),
        [0xB2] = new("CueDuration", ElementType.UnsignedInteger),
        [0xB3] = new("CueTime", ElementType.UnsignedInteger),
        [0xB5] = new("SamplingFrequency", ElementType.Float),
        [0xB6] = new("ChapterAtom", ElementType.Master),
        [0xB7] = new("CueTrackPositions", ElementType.Master),
        [0xB9] = new("FlagEnabled", ElementType.UnsignedInteger),
        [0xBA] = new("PixelHeight", ElementType.UnsignedInteger),
        [0xBB] = new("CuePoint", ElementType.Master),
        [0xBF] = new("CRC-32", ElementType.Binary),
        [0xD7] = new("TrackNumber", ElementType.UnsignedInteger),
        [0xDB] = new("CueReference", ElementType.Master),
        [0xE0] = new("Video", ElementType.Master),
        [0xE1] = new("Audio", ElementType.Master),
        [0xE2] = new("TrackOperation", ElementType.Master),
        [0xE3] = new("TrackCombinePlanes", ElementType.Master),
        [0xE4] = new("TrackPlane", ElementType.Master),
        [0xE5] = new("TrackPlaneUID", ElementType.UnsignedInteger),
        [0xE6] = new("TrackPlaneType", ElementType.UnsignedInteger),
        [0xE7] = new("Timestamp", ElementType.UnsignedInteger),
        [0xE9] = new("TrackJoinBlocks", ElementType.Master),
        [0xEA] = new("CueCodecState", ElementType.UnsignedInteger),
        [0xEC] = new("Void", ElementType.Binary),
        [0xED] = new("TrackJoinUID", ElementType.UnsignedInteger),
        [0xEE] = new("BlockAddID", ElementType.UnsignedInteger),
        [0xF0] = new("CueRelativePosition", ElementType.UnsignedInteger),
        [0xF1] = new("CueClusterPosition", ElementType.UnsignedInteger),
        [0xF7] = new("CueTrack", ElementType.UnsignedInteger),
        [0xFA] = new("ReferencePriority", ElementType.UnsignedInteger),
        [0xFB] = new("ReferenceBlock", ElementType.SignedInteger),
        [0xFD] = new("ReferenceVirtual", ElementType.SignedInteger),
        [0x41A4] = new("BlockAddIDName", ElementType.String),
        [0x41E4] = new("BlockAdditionMapping", ElementType.Master),
        [0x41E7] = new("BlockAddIDType", ElementType.UnsignedInteger),
        [0x41ED] = new("BlockAddIDExtraData", ElementType.Binary),
        [0x41F0] = new("BlockAddIDValue", ElementType.UnsignedInteger),
        [0x4254] = new("ContentCompAlgo", ElementType.UnsignedInteger),
        [0x4255] = new("ContentCompSettings", ElementType.Binary),
        [0x4282] = new("DocType", ElementType.String),
        [0x4285] = new("DocTypeReadVersion", ElementType.UnsignedInteger),
        [0x4286] = new("EBMLVersion", ElementType.UnsignedInteger),
        [0x4287] = new("DocTypeVersion", ElementType.UnsignedInteger),
        [0x42F2] = new("EBMLMaxIDLength", ElementType.UnsignedInteger),
        [0x42F3] = new("EBMLMaxSizeLength", ElementType.UnsignedInteger),
        [0x42F7] = new("EBMLReadVersion", ElementType.UnsignedInteger),
        [0x437C] = new("ChapLanguage", ElementType.String),
        [0x437D] = new("ChapLanguageBCP47", ElementType.String),
        [0x437E] = new("ChapCountry", ElementType.String),
        [0x4444] = new("SegmentFamily", ElementType.Binary),
        [0x4461] = new("DateUTC", ElementType.Date),
        [0x447A] = new("TagLanguage", ElementType.String),
        [0x447B] = new("TagLanguageBCP47", ElementType.String),
        [0x4484] = new("TagDefault", ElementType.UnsignedInteger),
        [0x4485] = new("TagBinary", ElementType.Binary),
        [0x4487] = new("TagString", ElementType.Utf8String),
        [0x4489] = new("Duration", ElementType.Float),
        [0x44B4] = new("TagDefaultBogus", ElementType.UnsignedInteger),
        [0x450D] = new("ChapProcessPrivate", ElementType.Binary),
        [0x4598] = new("ChapterFlagEnabled", ElementType.UnsignedInteger),
        [0x45A3] = new("TagName", ElementType.Utf8String),
        [0x45B9] = new("EditionEntry", ElementType.Master),
        [0x45BC] = new("EditionUID", ElementType.UnsignedInteger),
        [0x45BD] = new("EditionFlagHidden", ElementType.UnsignedInteger),
        [0x45DB] = new("EditionFlagDefault", ElementType.UnsignedInteger),
        [0x45DD] = new("EditionFlagOrdered", ElementType.UnsignedInteger),
        [0x465C] = new("FileData", ElementType.Binary),
        [0x4660] = new("FileMediaType", ElementType.String),
        [0x4661] = new("FileUsedStartTime", ElementType.UnsignedInteger),
        [0x4662] = new("FileUsedEndTime", ElementType.UnsignedInteger),
        [0x466E] = new("FileName", ElementType.Utf8String),
        [0x4675] = new("FileReferral", ElementType.Binary),
        [0x467E] = new("FileDescription", ElementType.Utf8String),
        [0x46AE] = new("FileUID", ElementType.UnsignedInteger),
        [0x47E1] = new("ContentEncAlgo", ElementType.UnsignedInteger),
        [0x47E2] = new("ContentEncKeyID", ElementType.Binary),
        [0x47E7] = new("ContentEncAESSettings", ElementType.Master),
        [0x47E8] = new("AESSettingsCipherMode", ElementType.UnsignedInteger),
        [0x4D80] = new("MuxingApp", ElementType.Utf8String),
        [0x4DBB] = new("Seek", ElementType.Master),
        [0x5031] = new("ContentEncodingOrder", ElementType.UnsignedInteger),
        [0x5032] = new("ContentEncodingScope", ElementType.UnsignedInteger),
        [0x5033] = new("ContentEncodingType", ElementType.UnsignedInteger),
        [0x5034] = new("ContentCompression", ElementType.Master),
        [0x5035] = new("ContentEncryption", ElementType.Master),
        [0x536E] = new("Name", ElementType.Utf8String),
        [0x5378] = new("CueBlockNumber", ElementType.UnsignedInteger),
        [0x53AB] = new("SeekID", ElementType.Binary),
        [0x53AC] = new("SeekPosition", ElementType.UnsignedInteger),
        [0x53B8] = new("StereoMode", ElementType.UnsignedInteger),
        [0x53B9] = new("OldStereoMode", ElementType.UnsignedInteger),
        [0x53C0] = new("AlphaMode", ElementType.UnsignedInteger),
        [0x54AA] = new("PixelCropBottom", ElementType.UnsignedInteger),
        [0x54B0] = new("DisplayWidth", ElementType.UnsignedInteger),
        [0x54B2] = new("DisplayUnit", ElementType.UnsignedInteger),
        [0x54B3] = new("AspectRatioType", ElementType.UnsignedInteger),
        [0x54BA] = new("DisplayHeight", ElementType.UnsignedInteger),
        [0x54BB] = new("PixelCropTop", ElementType.UnsignedInteger),
        [0x54CC] = new("PixelCropLeft", ElementType.UnsignedInteger),
        [0x54DD] = new("PixelCropRight", ElementType.UnsignedInteger),
        [0x55AA] = new("FlagForced", ElementType.UnsignedInteger),
        [0x55AB] = new("FlagHearingImpaired", ElementType.UnsignedInteger),
        [0x55AC] = new("FlagVisualImpaired", ElementType.UnsignedInteger),
        [0x55AD] = new("FlagTextDescriptions", ElementType.UnsignedInteger),
        [0x55AE] = new("FlagOriginal", ElementType.UnsignedInteger),
        [0x55AF] = new("FlagCommentary", ElementType.UnsignedInteger),
        [0x55B0] = new("Colour", ElementType.Master),
        [0x55B1] = new("MatrixCoefficients", ElementType.UnsignedInteger),
        [0x55B2] = new("BitsPerChannel", ElementType.UnsignedInteger),
        [0x55B3] = new("ChromaSubsamplingHorz", ElementType.UnsignedInteger),
        [0x55B4] = new("ChromaSubsamplingVert", ElementType.UnsignedInteger),
        [0x55B5] = new("CbSubsamplingHorz", ElementType.UnsignedInteger),
        [0x55B6] = new("CbSubsamplingVert", ElementType.UnsignedInteger),
        [0x55B7] = new("ChromaSitingHorz", ElementType.UnsignedInteger),
        [0x55B8] = new("ChromaSitingVert", ElementType.UnsignedInteger),
        [0x55B9] = new("Range", ElementType.UnsignedInteger),
        [0x55BA] = new("TransferCharacteristics", ElementType.UnsignedInteger),
        [0x55BB] = new("Primaries", ElementType.UnsignedInteger),
        [0x55BC] = new("MaxCLL", ElementType.UnsignedInteger),
        [0x55BD] = new("MaxFALL", ElementType.UnsignedInteger),
        [0x55D0] = new("MasteringMetadata", ElementType.Master),
        [0x55D1] = new("PrimaryRChromaticityX", ElementType.Float),
        [0x55D2] = new("PrimaryRChromaticityY", ElementType.Float),
        [0x55D3] = new("PrimaryGChromaticityX", ElementType.Float),
        [0x55D4] = new("PrimaryGChromaticityY", ElementType.Float),
        [0x55D5] = new("PrimaryBChromaticityX", ElementType.Float),
        [0x55D6] = new("PrimaryBChromaticityY", ElementType.Float),
        [0x55D7] = new("WhitePointChromaticityX", ElementType.Float),
        [0x55D8] = new("WhitePointChromaticityY", ElementType.Float),
        [0x55D9] = new("LuminanceMax", ElementType.Float),
        [0x55DA] = new("LuminanceMin", ElementType.Float),
        [0x55EE] = new("MaxBlockAdditionID", ElementType.UnsignedInteger),
        [0x5654] = new("ChapterStringUID", ElementType.Utf8String),
        [0x56AA] = new("CodecDelay", ElementType.UnsignedInteger),
        [0x56BB] = new("SeekPreRoll", ElementType.UnsignedInteger),
        [0x5741] = new("WritingApp", ElementType.Utf8String),
        [0x5854] = new("SilentTracks", ElementType.Master),
        [0x58D7] = new("SilentTrackNumber", ElementType.UnsignedInteger),
        [0x61A7] = new("AttachedFile", ElementType.Master),
        [0x6240] = new("ContentEncoding", ElementType.Master),
        [0x6264] = new("BitDepth", ElementType.UnsignedInteger),
        [0x63A2] = new("CodecPrivate", ElementType.Binary),
        [0x63C0] = new("Targets", ElementType.Master),
        [0x63C3] = new("ChapterPhysicalEquiv", ElementType.UnsignedInteger),
        [0x63C4] = new("TagChapterUID", ElementType.UnsignedInteger),
        [0x63C5] = new("TagTrackUID", ElementType.UnsignedInteger),
        [0x63C6] = new("TagAttachmentUID", ElementType.UnsignedInteger),
        [0x63C9] = new("TagEditionUID", ElementType.UnsignedInteger),
        [0x63CA] = new("TargetType", ElementType.String),
        [0x6624] = new("TrackTranslate", ElementType.Master),
        [0x66A5] = new("TrackTranslateTrackID", ElementType.Binary),
        [0x66BF] = new("TrackTranslateCodec", ElementType.UnsignedInteger),
        [0x66FC] = new("TrackTranslateEditionUID", ElementType.UnsignedInteger),
        [0x67C8] = new("SimpleTag", ElementType.Master),
        [0x68CA] = new("TargetTypeValue", ElementType.UnsignedInteger),
        [0x6911] = new("ChapProcessCommand", ElementType.Master),
        [0x6922] = new("ChapProcessTime", ElementType.UnsignedInteger),
        [0x6924] = new("ChapterTranslate", ElementType.Master),
        [0x6933] = new("ChapProcessData", ElementType.Binary),
        [0x6944] = new("ChapProcess", ElementType.Master),
        [0x6955] = new("ChapProcessCodecID", ElementType.UnsignedInteger),
        [0x69A5] = new("ChapterTranslateID", ElementType.Binary),
        [0x69BF] = new("ChapterTranslateCodec", ElementType.UnsignedInteger),
        [0x69FC] = new("ChapterTranslateEditionUID", ElementType.UnsignedInteger),
        [0x6D80] = new("ContentEncodings", ElementType.Master),
        [0x6DE7] = new("MinCache", ElementType.UnsignedInteger),
        [0x6DF8] = new("MaxCache", ElementType.UnsignedInteger),
        [0x6E67] = new("ChapterSegmentUUID", ElementType.Binary),
        [0x6EBC] = new("ChapterSegmentEditionUID", ElementType.UnsignedInteger),
        [0x7373] = new("Tag", ElementType.Master),
        [0x7384] = new("SegmentFilename", ElementType.Utf8String),
        [0x73A4] = new("SegmentUUID", ElementType.Binary),
        [0x73C4] = new("ChapterUID", ElementType.UnsignedInteger),
        [0x73C5] = new("TrackUID", ElementType.UnsignedInteger),
        [0x7446] = new("AttachmentLink", ElementType.UnsignedInteger),
        [0x75A1] = new("BlockAdditions", ElementType.Master),
        [0x75A2] = new("DiscardPadding", ElementType.SignedInteger),
        [0x7670] = new("Projection", ElementType.Master),
        [0x7671] = new("ProjectionType", ElementType.UnsignedInteger),
        [0x7672] = new("ProjectionPrivate", ElementType.Binary),
        [0x7673] = new("ProjectionPoseYaw", ElementType.Float),
        [0x7674] = new("ProjectionPosePitch", ElementType.Float),
        [0x7675] = new("ProjectionPoseRoll", ElementType.Float),
        [0x78B5] = new("OutputSamplingFrequency", ElementType.Float),
        [0x7BA9] = new("Title", ElementType.Utf8String),
        [0x7D7B] = new("ChannelPositions", ElementType.Binary),
        [0x22B59C] = new("Language", ElementType.String),
        [0x22B59D] = new("LanguageBCP47", ElementType.String),
        [0x23314F] = new("TrackTimestampScale", ElementType.Float),
        [0x234E7A] = new("DefaultDecodedFieldDuration", ElementType.UnsignedInteger),
        [0x23E383] = new("DefaultDuration", ElementType.UnsignedInteger),
        [0x258688] = new("CodecName", ElementType.Utf8String),
        [0x2AD7B1] = new("TimestampScale", ElementType.UnsignedInteger),
        [0x2EB524] = new("UncompressedFourCC", ElementType.Binary),
        [0x3C83AB] = new("PrevFilename", ElementType.Utf8String),
        [0x3CB923] = new("PrevUUID", ElementType.Binary),
        [0x3E83BB] = new("NextFilename", ElementType.Utf8String),
        [0x3EB923] = new("NextUUID", ElementType.Binary),
        [0x1043A770] = new("Chapters", ElementType.Master),
        [0x114D9B74] = new("SeekHead", ElementType.Master),
        [0x1254C367] = new("Tags", ElementType.Master),
        [0x1549A966] = new("Info", ElementType.Master),
        [0x1654AE6B] = new("Tracks", ElementType.Master),
        [0x18538067] = new("Segment", ElementType.Master),
        [0x1941A469] = new("Attachments", ElementType.Master),
        [0x1A45DFA3] = new("EBML", ElementType.Master),
        [0x1C53BB6B] = new("Cues", ElementType.Master),
        [0x1F43B675] = new("Cluster", ElementType.Master),
    }.ToFrozenDictionary();

    private static readonly FrozenSet<ulong> TopLevelIds = new ulong[]
    {
        0x114D9B74, 0x1549A966, ClusterId, 0x1654AE6B, 0x1C53BB6B, 0x1941A469, 0x1043A770, 0x1254C367,
    }.ToFrozenSet();

    public static bool TryGet(ulong id, out ElementDefinition definition) =>
        Definitions.TryGetValue(id, out definition);

    private static bool IsGlobal(ulong id) => id is VoidId or Crc32Id;

    private static bool IsTopLevel(ulong id) => TopLevelIds.Contains(id);

    /// <summary>
    /// Block elements carry interleaved frame headers, lacing and codec bytes. The payload is
    /// high-entropy data, so it is never interpreted as EBML structure.
    /// </summary>
    public static bool IsHighEntropyBlock(ulong id) => id is 0xA1 or 0xA2 or 0xA3;

    public static string GetName(ulong id) =>
        Definitions.TryGetValue(id, out var definition) ? definition.Name : "UnknownElement";

    /// <summary>
    /// RFC 8794 section 6.2: a non-global element ends an unknown-sized element when it is a
    /// valid parent, a sibling sharing the same direct parent, or a root element.
    /// </summary>
    public static bool EndsUnknownSizedElement(ulong? parentId, ulong candidateId)
    {
        if (IsGlobal(candidateId))
        {
            return false;
        }

        if (candidateId is EbmlHeaderId or SegmentId)
        {
            return true;
        }

        if (parentId is null)
        {
            return false;
        }

        if (parentId == SegmentId)
        {
            return IsTopLevel(candidateId);
        }

        return candidateId == parentId || IsTopLevel(candidateId);
    }
}
