using System;
using System.Globalization;

namespace UniversalParser.Src.Parser.EBML.Elements
{
    /// <summary>
    /// Matroska / WebM Tracks 及其子 Element 解析器。
    ///
    /// 所有方法只解析当前 Element 自身负载。
    /// 不读取父节点、兄弟节点或其他目标节点。
    /// </summary>
    internal static class TrackElements
    {
        // ============================================================
        // Tracks / TrackEntry
        // ============================================================

        public static ParseResult ParseTracks(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Tracks");

        public static ParseResult ParseTrackEntry(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "TrackEntry");

        public static ParseResult ParseTrackNumber(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TrackNumber");

        public static ParseResult ParseTrackUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TrackUID");

        public static ParseResult ParseTrackType(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TrackType",
                FormatTrackType);

        public static ParseResult ParseFlagEnabled(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "FlagEnabled",
                FormatBoolean);

        public static ParseResult ParseFlagDefault(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "FlagDefault",
                FormatBoolean);

        public static ParseResult ParseFlagForced(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "FlagForced",
                FormatBoolean);

        public static ParseResult ParseFlagLacing(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "FlagLacing",
                FormatBoolean);

        public static ParseResult ParseMinCache(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "MinCache");

        public static ParseResult ParseMaxCache(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "MaxCache");

        public static ParseResult ParseDefaultDuration(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "DefaultDuration");

        public static ParseResult ParseDefaultDecodedFieldDuration(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "DefaultDecodedFieldDuration");

        public static ParseResult ParseTrackTimestampScale(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "TrackTimestampScale");

        public static ParseResult ParseMaxBlockAdditionID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "MaxBlockAdditionID");

        public static ParseResult ParseTrackOverlay(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TrackOverlay");

        public static ParseResult ParseAttachmentLink(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "AttachmentLink");

        public static ParseResult ParseName(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "Name");

        public static ParseResult ParseLanguage(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseAsciiString(
                parser,
                node,
                header,
                "Language");

        public static ParseResult ParseLanguageBCP47(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "LanguageBCP47");

        public static ParseResult ParseCodecID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseAsciiString(
                parser,
                node,
                header,
                "CodecID");

        /// <summary>
        /// CodecPrivate 是编码器私有数据。
        /// 当前只保留其未解析负载长度，不解析具体编码格式。
        /// </summary>
        public static ParseResult ParseCodecPrivate(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "CodecPrivate");

        public static ParseResult ParseCodecName(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "CodecName");

        public static ParseResult ParseCodecSettings(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "CodecSettings");

        public static ParseResult ParseCodecInfo(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "CodecInfo");

        public static ParseResult ParseCodecDownloadURL(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUtf8String(
                parser,
                node,
                header,
                "CodecDownloadURL");

        public static ParseResult ParseCodecDecodeAll(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CodecDecodeAll",
                FormatBoolean);

        // ============================================================
        // Video
        // ============================================================

        public static ParseResult ParseVideo(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Video");

        public static ParseResult ParseFlagInterlaced(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "FlagInterlaced",
                FormatBoolean);

        public static ParseResult ParseFieldOrder(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseAsciiString(
                parser,
                node,
                header,
                "FieldOrder");

        public static ParseResult ParseStereoMode(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "StereoMode",
                FormatStereoMode);

        public static ParseResult ParseOldStereoMode(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "OldStereoMode");

        public static ParseResult ParseAlphaMode(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "AlphaMode",
                FormatBoolean);

        public static ParseResult ParsePixelWidth(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "PixelWidth");

        public static ParseResult ParsePixelHeight(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "PixelHeight");

        public static ParseResult ParsePixelCropBottom(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "PixelCropBottom");

        public static ParseResult ParsePixelCropTop(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "PixelCropTop");

        public static ParseResult ParsePixelCropLeft(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "PixelCropLeft");

        public static ParseResult ParsePixelCropRight(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "PixelCropRight");

        public static ParseResult ParseDisplayWidth(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "DisplayWidth");

        public static ParseResult ParseDisplayHeight(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "DisplayHeight");

        public static ParseResult ParseDisplayUnit(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "DisplayUnit",
                FormatDisplayUnit);

        public static ParseResult ParseAspectRatioType(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "AspectRatioType",
                FormatAspectRatioType);

        public static ParseResult ParseUncompressedFourCC(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseBinary(
                parser,
                node,
                header,
                "UncompressedFourCC",
                FormatFourCC,
                requiredLength: 4);

        public static ParseResult ParseGamma(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "Gamma");

        // ============================================================
        // Colour
        // ============================================================

        public static ParseResult ParseColour(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Colour");

        public static ParseResult ParseMatrixCoefficients(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "MatrixCoefficients");

        public static ParseResult ParseBitsPerChannel(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "BitsPerChannel");

        public static ParseResult ParseChromaSubsamplingHorz(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ChromaSubsamplingHorz");

        public static ParseResult ParseChromaSubsamplingVert(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ChromaSubsamplingVert");

        public static ParseResult ParseCbSubsamplingHorz(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CbSubsamplingHorz");

        public static ParseResult ParseCbSubsamplingVert(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CbSubsamplingVert");

        public static ParseResult ParseChromaSitingHorz(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ChromaSitingHorz");

        public static ParseResult ParseChromaSitingVert(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ChromaSitingVert");

        public static ParseResult ParseRange(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "Range");

        public static ParseResult ParseTransferCharacteristics(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TransferCharacteristics");

        public static ParseResult ParsePrimaries(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "Primaries");

        public static ParseResult ParseMaxCLL(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "MaxCLL");

        public static ParseResult ParseMaxFALL(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "MaxFALL");

        // ============================================================
        // MasteringMetadata
        // ============================================================

        public static ParseResult ParseMasteringMetadata(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "MasteringMetadata");

        public static ParseResult ParsePrimaryRChromaticityX(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "PrimaryRChromaticityX");

        public static ParseResult ParsePrimaryRChromaticityY(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "PrimaryRChromaticityY");

        public static ParseResult ParsePrimaryGChromaticityX(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "PrimaryGChromaticityX");

        public static ParseResult ParsePrimaryGChromaticityY(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "PrimaryGChromaticityY");

        public static ParseResult ParsePrimaryBChromaticityX(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "PrimaryBChromaticityX");

        public static ParseResult ParsePrimaryBChromaticityY(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "PrimaryBChromaticityY");

        public static ParseResult ParseWhitePointChromaticityX(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "WhitePointChromaticityX");

        public static ParseResult ParseWhitePointChromaticityY(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "WhitePointChromaticityY");

        public static ParseResult ParseLuminanceMax(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "LuminanceMax");

        public static ParseResult ParseLuminanceMin(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "LuminanceMin");

        // ============================================================
        // Projection
        // ============================================================

        public static ParseResult ParseProjection(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Projection");

        public static ParseResult ParseProjectionType(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ProjectionType",
                FormatProjectionType);

        public static ParseResult ParseProjectionPrivate(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "ProjectionPrivate");

        public static ParseResult ParseProjectionPoseYaw(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "ProjectionPoseYaw");

        public static ParseResult ParseProjectionPosePitch(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "ProjectionPosePitch");

        public static ParseResult ParseProjectionPoseRoll(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "ProjectionPoseRoll");

        // ============================================================
        // Audio
        // ============================================================

        public static ParseResult ParseAudio(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Audio");

        public static ParseResult ParseSamplingFrequency(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "SamplingFrequency");

        public static ParseResult ParseOutputSamplingFrequency(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseFloat(
                parser,
                node,
                header,
                "OutputSamplingFrequency");

        public static ParseResult ParseChannels(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "Channels");

        public static ParseResult ParseChannelPositions(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "ChannelPositions");

        public static ParseResult ParseBitDepth(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "BitDepth");

        // ============================================================
        // TrackTranslate
        // ============================================================

        public static ParseResult ParseTrackTranslate(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "TrackTranslate");

        public static ParseResult ParseTrackTranslateEditionUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TrackTranslateEditionUID");

        public static ParseResult ParseTrackTranslateCodec(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TrackTranslateCodec");

        public static ParseResult ParseTrackTranslateTrackID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "TrackTranslateTrackID");

        // ============================================================
        // TrackOperation
        // ============================================================

        public static ParseResult ParseTrackOperation(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "TrackOperation");

        public static ParseResult ParseTrackCombinePlanes(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "TrackCombinePlanes");

        public static ParseResult ParseTrackPlane(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "TrackPlane");

        public static ParseResult ParseTrackPlaneUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TrackPlaneUID");

        public static ParseResult ParseTrackPlaneType(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TrackPlaneType");

        public static ParseResult ParseTrackJoinBlocks(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "TrackJoinBlocks");

        public static ParseResult ParseTrackJoinUID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "TrackJoinUID");

        // ============================================================
        // ContentEncodings
        // ============================================================

        public static ParseResult ParseContentEncodings(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "ContentEncodings");

        public static ParseResult ParseContentEncoding(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "ContentEncoding");

        public static ParseResult ParseContentEncodingOrder(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ContentEncodingOrder");

        public static ParseResult ParseContentEncodingScope(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ContentEncodingScope");

        public static ParseResult ParseContentEncodingType(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ContentEncodingType");

        public static ParseResult ParseContentCompression(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "ContentCompression");

        public static ParseResult ParseContentCompAlgo(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ContentCompAlgo");

        public static ParseResult ParseContentCompSettings(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "ContentCompSettings");

        public static ParseResult ParseContentEncryption(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "ContentEncryption");

        public static ParseResult ParseContentEncAlgo(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ContentEncAlgo");

        public static ParseResult ParseContentEncKeyID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "ContentEncKeyID");

        public static ParseResult ParseContentEncAESSettings(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "ContentEncAESSettings");

        public static ParseResult ParseAESSettingsCipherMode(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "AESSettingsCipherMode");
        
        // ============================================================
        // Formatters
        // ============================================================

        private static string? FormatBoolean(ulong value) =>
            value switch
            {
                0 => "false",
                1 => "true",
                _ => null,
            };

        private static string? FormatTrackType(ulong value) =>
            value switch
            {
                0x01 => "video",
                0x02 => "audio",
                0x03 => "complex",
                0x10 => "logo",
                0x11 => "subtitle",
                0x12 => "buttons",
                0x20 => "control",
                _ => null,
            };

        private static string? FormatStereoMode(ulong value) =>
            value switch
            {
                0 => "mono",
                1 => "side by side (left eye first)",
                2 => "top-bottom (right eye first)",
                3 => "top-bottom (left eye first)",
                4 => "checkerboard (right eye first)",
                5 => "checkerboard (left eye first)",
                6 => "row interleaved (right eye first)",
                7 => "row interleaved (left eye first)",
                8 => "column interleaved (right eye first)",
                9 => "column interleaved (left eye first)",
                10 => "anaglyph (cyan/red)",
                11 => "side by side (right eye first)",
                12 => "anaglyph (green/magenta)",
                13 => "both eyes laced in one Block",
                14 => "right eye only",
                15 => "left eye only",
                _ => null,
            };

        private static string? FormatDisplayUnit(ulong value) =>
            value switch
            {
                0 => "pixels",
                1 => "centimeters",
                2 => "inches",
                3 => "display aspect ratio",
                4 => "unspecified",
                _ => null,
            };

        private static string? FormatAspectRatioType(ulong value) =>
            value switch
            {
                0 => "free resizing",
                1 => "keep aspect ratio",
                2 => "fixed pixel aspect ratio",
                _ => null,
            };

        private static string? FormatProjectionType(ulong value) =>
            value switch
            {
                0 => "rectangular",
                1 => "equirectangular",
                2 => "cubemap",
                3 => "mesh",
                _ => null,
            };

        private static string? FormatFourCC(ReadOnlySpan<byte> value)
        {
            if (value.Length != 4)
                return null;

            Span<char> chars = stackalloc char[4];

            for (int i = 0; i < value.Length; i++)
            {
                byte current = value[i];
                chars[i] = current is >= 0x20 and <= 0x7E
                    ? (char)current
                    : '.';
            }

            return new string(chars);
        }
    }
}