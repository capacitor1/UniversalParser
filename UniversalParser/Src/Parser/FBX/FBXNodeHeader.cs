namespace UniversalParser.Src.Parser.FBX
{
    /// <summary>
    /// FBX 二进制节点头信息。
    ///
    /// 所有偏移都是文件绝对偏移。
    /// </summary>
    internal readonly struct FBXNodeHeader
    {
        /// <summary>节点起始位置。</summary>
        public long NodeStart { get; init; }

        /// <summary>节点结束位置，来自 FBX 的 EndOffset 字段。</summary>
        public long EndOffset { get; init; }

        /// <summary>属性数量。</summary>
        public ulong PropertyCount { get; init; }

        /// <summary>属性区总长度。</summary>
        public ulong PropertyListLength { get; init; }

        /// <summary>节点名称长度。</summary>
        public byte NameLength { get; init; }

        /// <summary>节点名称。</summary>
        public string Name { get; init; }

        /// <summary>固定节点头长度，不包含节点名称。</summary>
        public int FixedHeaderLength { get; init; }

        /// <summary>节点名称起始位置。</summary>
        public long NameOffset =>
            NodeStart + FixedHeaderLength;

        /// <summary>属性区起始位置。</summary>
        public long PropertyOffset =>
            NameOffset + NameLength;

        /// <summary>属性区实际长度。</summary>
        public long ActualPropertyLength { get; init; }

        /// <summary>子节点列表起始位置。</summary>
        public long ChildrenOffset { get; init; }

        /// <summary>节点长度。</summary>
        public long NodeLength =>
            EndOffset > NodeStart
                ? EndOffset - NodeStart
                : 0;

        /// <summary>属性区是否被截断。</summary>
        public bool IsPropertyListTruncated =>
            ActualPropertyLength < FBXUtil.ClampToLong(PropertyListLength);

        /// <summary>节点是否被截断。</summary>
        public bool IsTruncated { get; init; }
    }
}