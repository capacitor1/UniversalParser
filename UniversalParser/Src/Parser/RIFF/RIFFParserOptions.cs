using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF
{
    internal sealed class RIFFParserOptions
    {
        /// <summary>递归深度上限，防止恶意/损坏文件造成 StackOverflow（B1）。</summary>
        public int MaxDepth { get; set; } = 32;

        /// <summary>单个容器最多展开的子节点数，防止 AVI movi 的百万级块把内存吃光（B3）。</summary>
        public int MaxChildrenPerContainer { get; set; } = 100_000;

        /// <summary>块头读取缓冲区大小（B4：小块密集读取的关键优化）。</summary>
        public int ReadBufferSize { get; set; } = 64 * 1024;

        /// <summary>额外的“带类型码容器”（负载前 4 字节是类型码），按需自行注册。</summary>
        public HashSet<string> ExtraTypedContainerIds { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// “无类型码容器”：负载直接是子块序列（没有 4CC 类型码）。
        /// 默认为空——原代码里的 movi/strl/INFO/PSAI 属于 LIST 类型码，放这里是错的（A4）。
        /// 如果确实遇到某些非标准写法的裸容器块，在这里登记。
        /// </summary>
        public HashSet<string> PlainContainerIds { get; } = new(StringComparer.Ordinal);
        /// <summary>
        /// “带固定长度头部的容器”：块头之后有 N 字节自有头部，其后才是子块序列。
        /// 例：WebP 的 'ANMF' 头部为 16 字节，之后是可选 'ALPH' 与一个位流子块。
        /// </summary>
        public Dictionary<string, int> HeaderedContainerIds { get; } = new(StringComparer.Ordinal)
        {
            ["ANMF"] = 16,
        };
    }
}