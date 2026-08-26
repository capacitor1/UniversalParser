namespace UniversalParser.Src.Parser.EBML
{
    /// <summary>
    /// 一个 EBML Element 的头部及实际文件范围。
    /// 所有偏移均为文件绝对偏移。
    /// </summary>
    internal readonly struct EBMLElementHeader
    {
        /// <summary>Element ID 的数值，保留 VINT 长度标记位。</summary>
        public required ulong ElementId { get; init; }

        /// <summary>Element ID 的编码字节数，范围为 1-4。</summary>
        public required int ElementIdLength { get; init; }

        /// <summary>Data Size VINT 的编码字节数，范围为 1-8。</summary>
        public required int DataSizeLength { get; init; }

        /// <summary>
        /// Data Size 的原始解码值。
        /// 当 IsUnknownSize 为 true 时，该值是对应长度的全 1 保留值。
        /// </summary>
        public required ulong DeclaredDataSize { get; init; }

        /// <summary>Data Size 是否使用未知大小编码。</summary>
        public required bool IsUnknownSize { get; init; }

        /// <summary>Element 是否是 Master Element。</summary>
        public required bool IsMaster { get; init; }

        /// <summary>Element 起始偏移，即 Element ID 所在位置。</summary>
        public required long ElementStart { get; init; }

        /// <summary>负载起始偏移。</summary>
        public required long PayloadStart { get; init; }

        /// <summary>
        /// 实际可用负载长度。
        /// 已经根据父 Element 边界、Node 边界及文件长度裁剪。
        /// </summary>
        public required long PayloadLength { get; init; }

        /// <summary>Element ID 和 Data Size 合计长度。</summary>
        public int HeaderLength => ElementIdLength + DataSizeLength;

        public long PayloadEnd => PayloadStart + PayloadLength;

        /// <summary>包含头部但不包含任何外部数据的实际 Element 长度。</summary>
        public long ElementLength => HeaderLength + PayloadLength;

        /// <summary>
        /// 已知大小 Element 的声明负载是否超过实际可用范围。
        /// 未知大小 Element 不视为被截断。
        /// </summary>
        public bool IsTruncated =>
            !IsUnknownSize && DeclaredDataSize > (ulong)PayloadLength;

        public string FormattedId =>
            EBMLUtil.FormatElementId(ElementId, ElementIdLength);
    }
}