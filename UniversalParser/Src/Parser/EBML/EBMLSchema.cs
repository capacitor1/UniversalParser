using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.EBML
{
    /// <summary>
    /// 最小 EBML / Matroska / WebM schema。
    ///
    /// 当前职责只有：
    /// 1. 判断一个 Element 是否为 Master Element；
    /// 2. 提供用于节点树和 Default 结果的英文名称。
    ///
    /// TODO:
    /// - 根据 DocType 动态加载不同 schema；
    /// - 从 Matroska XML schema 自动生成完整定义；
    /// - 加入 Element 类型、层级、版本和 WebM 限制信息。
    /// </summary>
    internal static class EBMLSchema
    {
        private readonly record struct ElementDefinition(string Name, bool IsMaster);

        private static readonly Dictionary<ulong, ElementDefinition> Definitions = new()
        {
            // --------------------------------------------------------
            // EBML
            // --------------------------------------------------------
            [0x1A45DFA3] = new("EBML", true),
            [0x4286] = new("EBMLVersion", false),
            [0x42F7] = new("EBMLReadVersion", false),
            [0x42F2] = new("EBMLMaxIDLength", false),
            [0x42F3] = new("EBMLMaxSizeLength", false),
            [0x4282] = new("DocType", false),
            [0x4287] = new("DocTypeVersion", false),
            [0x4285] = new("DocTypeReadVersion", false),

            [0xEC] = new("Void", false),
            [0xBF] = new("CRC32", false),

            // --------------------------------------------------------
            // Matroska / WebM: Segment
            // --------------------------------------------------------
            [0x18538067] = new("Segment", true),

            // Seek
            [0x114D9B74] = new("SeekHead", true),
            [0x4DBB] = new("Seek", true),
            [0x53AB] = new("SeekID", false),
            [0x53AC] = new("SeekPosition", false),

            // Segment Information
            [0x1549A966] = new("Info", true),
            [0x73A4] = new("SegmentUUID", false),
            [0x7384] = new("SegmentFilename", false),
            [0x3CB923] = new("PrevUUID", false),
            [0x3C83AB] = new("PrevFilename", false),
            [0x3EB923] = new("NextUUID", false),
            [0x3E83BB] = new("NextFilename", false),
            [0x4444] = new("SegmentFamily", false),
            [0x6924] = new("ChapterTranslate", true),
            [0x69A5] = new("ChapterTranslateID", false),
            [0x69BF] = new("ChapterTranslateCodec", false),
            [0x69FC] = new("ChapterTranslateEditionUID", false),
            [0x2AD7B1] = new("TimestampScale", false),
            [0x4489] = new("Duration", false),
            [0x4461] = new("DateUTC", false),
            [0x7BA9] = new("Title", false),
            [0x4D80] = new("MuxingApp", false),
            [0x5741] = new("WritingApp", false),

            // Cluster
            [0x1F43B675] = new("Cluster", true),
            [0xE7] = new("Timestamp", false),
            [0x5854] = new("SilentTracks", true),
            [0x58D7] = new("SilentTrackNumber", false),
            [0xA7] = new("Position", false),
            [0xAB] = new("PrevSize", false),
            [0xA3] = new("SimpleBlock", false),
            [0xA0] = new("BlockGroup", true),
            [0xA1] = new("Block", false),
            [0xA2] = new("BlockVirtual", false),
            [0x75A1] = new("BlockAdditions", true),
            [0xA6] = new("BlockMore", true),
            [0xEE] = new("BlockAddID", false),
            [0xA5] = new("BlockAdditional", false),
            [0x9B] = new("BlockDuration", false),
            [0xFA] = new("ReferencePriority", false),
            [0xFB] = new("ReferenceBlock", false),
            [0xFD] = new("ReferenceVirtual", false),
            [0xA4] = new("CodecState", false),
            [0x75A2] = new("DiscardPadding", false),

            // Tracks
            [0x1654AE6B] = new("Tracks", true),
            [0xAE] = new("TrackEntry", true),
            [0xD7] = new("TrackNumber", false),
            [0x73C5] = new("TrackUID", false),
            [0x83] = new("TrackType", false),
            [0xB9] = new("FlagEnabled", false),
            [0x88] = new("FlagDefault", false),
            [0x55AA] = new("FlagForced", false),
            [0x9C] = new("FlagLacing", false),
            [0x6DE7] = new("MinCache", false),
            [0x6DF8] = new("MaxCache", false),
            [0x23E383] = new("DefaultDuration", false),
            [0x234E7A] = new("DefaultDecodedFieldDuration", false),
            [0x23314F] = new("TrackTimestampScale", false),
            [0x55EE] = new("MaxBlockAdditionID", false),
            [0x536E] = new("Name", false),
            [0x22B59C] = new("Language", false),
            [0x22B59D] = new("LanguageBCP47", false),
            [0x86] = new("CodecID", false),
            [0x63A2] = new("CodecPrivate", false),
            [0x258688] = new("CodecName", false),
            [0x56AA] = new("CodecDelay", false),
            [0x56BB] = new("SeekPreRoll", false),

            [0x6624] = new("TrackTranslate", true),
            [0x66FC] = new("TrackTranslateEditionUID", false),
            [0x66BF] = new("TrackTranslateCodec", false),
            [0x66A5] = new("TrackTranslateTrackID", false),

            // Video
            [0xE0] = new("Video", true),
            [0x9A] = new("FlagInterlaced", false),
            [0x9D] = new("FieldOrder", false),
            [0x53B8] = new("StereoMode", false),
            [0x53C0] = new("AlphaMode", false),
            [0xB0] = new("PixelWidth", false),
            [0xBA] = new("PixelHeight", false),
            [0x54AA] = new("PixelCropBottom", false),
            [0x54BB] = new("PixelCropTop", false),
            [0x54CC] = new("PixelCropLeft", false),
            [0x54DD] = new("PixelCropRight", false),
            [0x54B0] = new("DisplayWidth", false),
            [0x54BA] = new("DisplayHeight", false),
            [0x54B2] = new("DisplayUnit", false),
            [0x54B3] = new("AspectRatioType", false),
            [0x2EB524] = new("UncompressedFourCC", false),

            // Colour
            [0x55B0] = new("Colour", true),
            [0x55B1] = new("MatrixCoefficients", false),
            [0x55B2] = new("BitsPerChannel", false),
            [0x55B3] = new("ChromaSubsamplingHorz", false),
            [0x55B4] = new("ChromaSubsamplingVert", false),
            [0x55B5] = new("CbSubsamplingHorz", false),
            [0x55B6] = new("CbSubsamplingVert", false),
            [0x55B7] = new("ChromaSitingHorz", false),
            [0x55B8] = new("ChromaSitingVert", false),
            [0x55B9] = new("Range", false),
            [0x55BA] = new("TransferCharacteristics", false),
            [0x55BB] = new("Primaries", false),
            [0x55BC] = new("MaxCLL", false),
            [0x55BD] = new("MaxFALL", false),

            // Mastering metadata
            [0x55D0] = new("MasteringMetadata", true),
            [0x55D1] = new("PrimaryRChromaticityX", false),
            [0x55D2] = new("PrimaryRChromaticityY", false),
            [0x55D3] = new("PrimaryGChromaticityX", false),
            [0x55D4] = new("PrimaryGChromaticityY", false),
            [0x55D5] = new("PrimaryBChromaticityX", false),
            [0x55D6] = new("PrimaryBChromaticityY", false),
            [0x55D7] = new("WhitePointChromaticityX", false),
            [0x55D8] = new("WhitePointChromaticityY", false),
            [0x55D9] = new("LuminanceMax", false),
            [0x55DA] = new("LuminanceMin", false),

            // Projection
            [0x7670] = new("Projection", true),
            [0x7671] = new("ProjectionType", false),
            [0x7672] = new("ProjectionPrivate", false),
            [0x7673] = new("ProjectionPoseYaw", false),
            [0x7674] = new("ProjectionPosePitch", false),
            [0x7675] = new("ProjectionPoseRoll", false),

            // Audio
            [0xE1] = new("Audio", true),
            [0xB5] = new("SamplingFrequency", false),
            [0x78B5] = new("OutputSamplingFrequency", false),
            [0x9F] = new("Channels", false),
            [0x7D7B] = new("ChannelPositions", false),
            [0x6264] = new("BitDepth", false),

            // Track operation
            [0xE2] = new("TrackOperation", true),
            [0xE3] = new("TrackCombinePlanes", true),
            [0xE4] = new("TrackPlane", true),
            [0xE5] = new("TrackPlaneUID", false),
            [0xE6] = new("TrackPlaneType", false),
            [0xE9] = new("TrackJoinBlocks", true),
            [0xED] = new("TrackJoinUID", false),

            // Content encoding
            [0x6D80] = new("ContentEncodings", true),
            [0x6240] = new("ContentEncoding", true),
            [0x5031] = new("ContentEncodingOrder", false),
            [0x5032] = new("ContentEncodingScope", false),
            [0x5033] = new("ContentEncodingType", false),
            [0x5034] = new("ContentCompression", true),
            [0x4254] = new("ContentCompAlgo", false),
            [0x4255] = new("ContentCompSettings", false),
            [0x5035] = new("ContentEncryption", true),
            [0x47E1] = new("ContentEncAlgo", false),
            [0x47E2] = new("ContentEncKeyID", false),
            [0x47E7] = new("ContentEncAESSettings", true),
            [0x47E8] = new("AESSettingsCipherMode", false),

            // Cues
            [0x1C53BB6B] = new("Cues", true),
            [0xBB] = new("CuePoint", true),
            [0xB3] = new("CueTime", false),
            [0xB7] = new("CueTrackPositions", true),
            [0xF7] = new("CueTrack", false),
            [0xF1] = new("CueClusterPosition", false),
            [0xF0] = new("CueRelativePosition", false),
            [0xB2] = new("CueDuration", false),
            [0x5378] = new("CueBlockNumber", false),
            [0xEA] = new("CueCodecState", false),
            [0xDB] = new("CueReference", true),
            [0x96] = new("CueRefTime", false),

            // Attachments
            [0x1941A469] = new("Attachments", true),
            [0x61A7] = new("AttachedFile", true),
            [0x467E] = new("FileDescription", false),
            [0x466E] = new("FileName", false),
            [0x4660] = new("FileMediaType", false),
            [0x465C] = new("FileData", false),
            [0x46AE] = new("FileUID", false),
            [0x4675] = new("FileReferral", false),
            [0x4661] = new("FileUsedStartTime", false),
            [0x4662] = new("FileUsedEndTime", false),

            // Chapters
            [0x1043A770] = new("Chapters", true),
            [0x45B9] = new("EditionEntry", true),
            [0x45BC] = new("EditionUID", false),
            [0x45BD] = new("EditionFlagHidden", false),
            [0x45DB] = new("EditionFlagDefault", false),
            [0x45DD] = new("EditionFlagOrdered", false),
            [0xB6] = new("ChapterAtom", true),
            [0x73C4] = new("ChapterUID", false),
            [0x5654] = new("ChapterStringUID", false),
            [0x91] = new("ChapterTimeStart", false),
            [0x92] = new("ChapterTimeEnd", false),
            [0x98] = new("ChapterFlagHidden", false),
            [0x4598] = new("ChapterFlagEnabled", false),
            [0x6E67] = new("ChapterSegmentUUID", false),
            [0x6EBC] = new("ChapterSegmentEditionUID", false),
            [0x63C3] = new("ChapterPhysicalEquiv", false),
            [0x8F] = new("ChapterTrack", true),
            [0x89] = new("ChapterTrackUID", false),
            [0x80] = new("ChapterDisplay", true),
            [0x85] = new("ChapString", false),
            [0x437C] = new("ChapLanguage", false),
            [0x437D] = new("ChapLanguageBCP47", false),
            [0x437E] = new("ChapCountry", false),
            [0x6944] = new("ChapProcess", true),
            [0x6955] = new("ChapProcessCodecID", false),
            [0x450D] = new("ChapProcessPrivate", false),
            [0x6911] = new("ChapProcessCommand", true),
            [0x6922] = new("ChapProcessTime", false),
            [0x6933] = new("ChapProcessData", false),

            // Tags
            [0x1254C367] = new("Tags", true),
            [0x7373] = new("Tag", true),
            [0x63C0] = new("Targets", true),
            [0x68CA] = new("TargetTypeValue", false),
            [0x63CA] = new("TargetType", false),
            [0x63C5] = new("TagTrackUID", false),
            [0x63C9] = new("TagEditionUID", false),
            [0x63C4] = new("TagChapterUID", false),
            [0x63C6] = new("TagAttachmentUID", false),
            [0x67C8] = new("SimpleTag", true),
            [0x45A3] = new("TagName", false),
            [0x447A] = new("TagLanguage", false),
            [0x447B] = new("TagLanguageBCP47", false),
            [0x4484] = new("TagDefault", false),
            [0x44B4] = new("TagDefaultBogus", false),
            [0x4487] = new("TagString", false),
            [0x4485] = new("TagBinary", false),
        };

        public static bool IsMaster(
            ulong elementId,
            EBMLParserOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.ForcedLeafElementIds.Contains(elementId))
                return false;

            if (options.ExtraMasterElementIds.Contains(elementId))
                return true;

            return Definitions.TryGetValue(elementId, out ElementDefinition definition)
                   && definition.IsMaster;
        }

        public static string GetName(ulong elementId)
        {
            return Definitions.TryGetValue(elementId, out ElementDefinition definition)
                ? definition.Name
                : "Unknown";
        }

        public static bool IsKnown(ulong elementId) =>
            Definitions.ContainsKey(elementId);
    }
}