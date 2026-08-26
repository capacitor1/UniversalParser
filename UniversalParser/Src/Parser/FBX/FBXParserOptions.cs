namespace UniversalParser.Src.Parser.FBX
{
    internal sealed class FBXParserOptions
    {
        /// <summary>
        /// 递归深度上限。
        /// </summary>
        public int MaxDepth { get; set; } = 64;

        /// <summary>
        /// 每个父节点最多展开的子节点数量。
        /// </summary>
        public int MaxChildrenPerNode { get; set; } = 100_000_000;

        /// <summary>
        /// 单个节点允许的最大属性数量。
        /// </summary>
        public ulong MaxPropertyCount { get; set; } = 100_000_000;

        /// <summary>
        /// 单个节点允许的最大名称长度。
        /// </summary>
        public int MaxNodeNameLength { get; set; } = 40960;

        /// <summary>
        /// 是否展开子节点。
        /// </summary>
        public bool ParseChildren { get; set; } = true;

        /// <summary>
        /// FBX 解析读取缓存大小。
        ///
        /// FBX 通常包含大量小节点，建议至少 1 MiB。
        /// </summary>
        public int ReadBufferSize { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// 进度回调最小间隔。
        /// 进度回调通常会触发 GUI 更新，不应按节点触发。
        /// </summary>
        public int ProgressIntervalMilliseconds { get; set; } = 500;
    }
}