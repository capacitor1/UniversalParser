namespace UniversalParser.Src.Parser.FLV
{
    /// <summary>
    /// FLV Tag Header 信息。所有偏移均为文件绝对偏移。
    /// </summary>
    internal readonly struct FLVTagHeader
    {
        public long TagStart { get; init; }

        /// <summary>Tag Header 第一字节的 Reserved 字段。</summary>
        public byte Reserved { get; init; }

        /// <summary>Tag Header 第一字节的 Filter 字段。</summary>
        public bool Filter { get; init; }

        /// <summary>5-bit TagType。</summary>
        public byte TagType { get; init; }

        /// <summary>Tag Header 中声明的 DataSize。</summary>
        public uint DataSize { get; init; }

        /// <summary>Tag Header 中原始的低 24-bit Timestamp。</summary>
        public uint Timestamp { get; init; }

        /// <summary>Tag Header 中原始的高 8-bit TimestampExtended。</summary>
        public byte TimestampExtended { get; init; }

        /// <summary>完整 32-bit 时间戳，单位毫秒。</summary>
        public uint CompleteTimestamp =>
            ((uint)TimestampExtended << 24) | Timestamp;

        public uint StreamID { get; init; }

        public long DataStart => TagStart + FLVUtil.TagHeaderSize;

        /// <summary>实际可用的 Tag Data 长度。</summary>
        public long ActualDataSize { get; init; }

        public bool IsTruncated => ActualDataSize < DataSize;

        /// <summary>紧随 Tag Data 的 PreviousTagSize 是否存在。</summary>
        public bool HasPreviousTagSize { get; init; }

        /// <summary>
        /// 该字段在文件中位于当前 Tag 之后，但其值描述当前 Tag 的 Header + Data 长度。
        /// </summary>
        public uint PreviousTagSize { get; init; }

        public long PreviousTagSizeOffset => DataStart + DataSize;

        public long ExpectedPreviousTagSize =>
            FLVUtil.TagHeaderSize + (long)DataSize;

        public string TagNodeName => FLVUtil.GetTagNodeName(TagType);

        public string TagDataNodeName => FLVUtil.GetTagDataNodeName(TagType);
    }
}