namespace UniversalParser.Src.Parser.EBML.Elements
{
    /// <summary>
    /// Matroska / WebM Cues 及其子 Element 解析器。
    ///
    /// Cues
    /// └── CuePoint
    ///     ├── CueTime
    ///     └── CueTrackPositions
    ///         ├── CueTrack
    ///         ├── CueClusterPosition
    ///         ├── CueRelativePosition
    ///         ├── CueDuration
    ///         ├── CueBlockNumber
    ///         ├── CueCodecState
    ///         └── CueReference
    ///             ├── CueRefTime
    ///             ├── CueRefCluster
    ///             ├── CueRefNumber
    ///             └── CueRefCodecState
    ///
    /// 只解析当前 Element 自身负载。
    /// 不根据 Segment、Cluster 或其他 Cue Element 计算派生位置。
    /// </summary>
    internal static class CueElements
    {
        // ============================================================
        // Cues
        // ============================================================

        public static ParseResult ParseCues(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "Cues");

        public static ParseResult ParseCuePoint(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "CuePoint");

        public static ParseResult ParseCueTime(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueTime");

        // ============================================================
        // CueTrackPositions
        // ============================================================

        public static ParseResult ParseCueTrackPositions(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "CueTrackPositions");

        public static ParseResult ParseCueTrack(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueTrack");

        public static ParseResult ParseCueClusterPosition(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueClusterPosition");

        public static ParseResult ParseCueRelativePosition(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueRelativePosition");

        public static ParseResult ParseCueDuration(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueDuration");

        public static ParseResult ParseCueBlockNumber(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueBlockNumber");

        public static ParseResult ParseCueCodecState(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueCodecState");

        // ============================================================
        // CueReference
        // ============================================================

        public static ParseResult ParseCueReference(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseMaster(
                parser,
                node,
                header,
                "CueReference");

        public static ParseResult ParseCueRefTime(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueRefTime");

        public static ParseResult ParseCueRefCluster(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueRefCluster");

        public static ParseResult ParseCueRefNumber(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueRefNumber");

        public static ParseResult ParseCueRefCodecState(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header) =>
            EBMLValueElement.ParseUnsignedInteger(
                parser,
                node,
                header,
                "CueRefCodecState");
    }
}