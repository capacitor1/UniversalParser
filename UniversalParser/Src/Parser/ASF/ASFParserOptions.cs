namespace UniversalParser.Src.Parser.ASF
{
    internal sealed class ASFParserOptions
    {
        /// <summary>递归深度上限，防止恶意/损坏文件造成 StackOverflow。</summary>
        public int MaxDepth { get; set; } = 1024;

        /// <summary>单个容器最多展开的子节点数，防止恶意构造的海量对象把内存吃光。</summary>
        public int MaxChildrenPerContainer { get; set; } = 100_000_000;

        /// <summary>对象头读取缓冲区大小（海量 24 字节小读的关键优化）。</summary>
        public int ReadBufferSize { get; set; } = 64 * 1024;
    }
}