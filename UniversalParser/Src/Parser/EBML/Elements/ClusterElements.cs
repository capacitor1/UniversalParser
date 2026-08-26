using System;

namespace UniversalParser.Src.Parser.EBML.Elements
{
    /// <summary>
    /// Matroska / WebM Cluster 及其子 Element 解析器。
    ///
    /// Cluster
    /// ├── Timestamp
    /// ├── SilentTracks
    /// │   └── SilentTrackNumber
    /// ├── Position
    /// ├── PrevSize
    /// ├── SimpleBlock
    /// ├── BlockGroup
    /// │   ├── Block
    /// │   ├── BlockVirtual
    /// │   ├── BlockAdditions
    /// │   │   └── BlockMore
    /// │   │       ├── BlockAddID
    /// │   │       └── BlockAdditional
    /// │   ├── BlockDuration
    /// │   ├── ReferencePriority
    /// │   ├── ReferenceBlock
    /// │   ├── ReferenceVirtual
    /// │   ├── CodecState
    /// │   ├── DiscardPadding
    /// │   └── Slices
    /// ├── EncryptedBlock
    /// └── ...
    ///
    /// 所有方法只读取当前 Element 的自身负载。
    /// Block 和 SimpleBlock 的内部数据暂不解析。
    /// </summary>
    internal static class ClusterElements
    {
        // ============================================================
        // Cluster
        // ============================================================

        public static ParseResult ParseCluster(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Cluster");

        public static ParseResult ParseTimestamp(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "Timestamp");

        public static ParseResult ParseSilentTracks(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "SilentTracks");

        public static ParseResult ParseSilentTrackNumber(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "SilentTrackNumber");

        public static ParseResult ParsePosition(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "Position");

        public static ParseResult ParsePrevSize(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "PrevSize");

        // ============================================================
        // Dense block payloads
        // ============================================================

        /// <summary>
        /// SimpleBlock 的负载包含 Track Number、时间戳、Flags、Lacing
        /// 以及一个或多个实际帧。本阶段全部视为未解析数据。
        /// </summary>
        public static ParseResult ParseSimpleBlock(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "SimpleBlock");

        /// <summary>
        /// Block 的负载包含 Block Header、Lacing 和实际帧数据。
        /// 本阶段不解析。
        /// </summary>
        public static ParseResult ParseBlock(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "Block");

        public static ParseResult ParseBlockVirtual(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "BlockVirtual");

        public static ParseResult ParseEncryptedBlock(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "EncryptedBlock");

        // ============================================================
        // BlockGroup
        // ============================================================

        public static ParseResult ParseBlockGroup(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "BlockGroup");

        public static ParseResult ParseBlockAdditions(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "BlockAdditions");

        public static ParseResult ParseBlockMore(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "BlockMore");

        public static ParseResult ParseBlockAddID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "BlockAddID");

        /// <summary>
        /// BlockAdditional 是 Block 附加数据，可能是高密度或编码器私有数据。
        /// 当前不解析。
        /// </summary>
        public static ParseResult ParseBlockAdditional(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "BlockAdditional");

        public static ParseResult ParseBlockDuration(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "BlockDuration");

        public static ParseResult ParseReferencePriority(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "ReferencePriority");

        public static ParseResult ParseReferenceBlock(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseSignedInteger(
                parser,
                node,
                header,
                "ReferenceBlock");

        public static ParseResult ParseReferenceVirtual(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseSignedInteger(
                parser,
                node,
                header,
                "ReferenceVirtual");

        public static ParseResult ParseCodecState(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "CodecState");

        public static ParseResult ParseDiscardPadding(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseSignedInteger(
                parser,
                node,
                header,
                "DiscardPadding");

        // ============================================================
        // Slices
        // ============================================================

        public static ParseResult ParseSlices(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Slices");

        public static ParseResult ParseTimeSlice(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "TimeSlice");

        public static ParseResult ParseLaceNumber(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "LaceNumber");

        public static ParseResult ParseFrameNumber(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "FrameNumber");

        public static ParseResult ParseBlockAdditionID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "BlockAdditionID");

        public static ParseResult ParseDelay(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "Delay");

        public static ParseResult ParseSliceDuration(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "SliceDuration");
    }
}