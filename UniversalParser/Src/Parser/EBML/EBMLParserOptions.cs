using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.EBML
{
    internal sealed class EBMLParserOptions
    {
        /// <summary>最大 Master Element 嵌套深度。</summary>
        public int MaxDepth { get; set; } = 1024;

        /// <summary>单个 Master Element 最多展开的直接子节点数量。</summary>
        public int MaxChildrenPerContainer { get; set; } = 100_000_000;

        /// <summary>顺序定位读取缓冲区大小。</summary>
        public int ReadBufferSize { get; set; } = 64 * 1024;

        /// <summary>
        /// 额外的 Master Element ID。
        /// EBML 本身不在二进制中存储 Element 类型，因此未知 schema 的 Master Element
        /// 必须通过外部 schema 或此集合注册。
        /// </summary>
        public HashSet<ulong> ExtraMasterElementIds { get; } = [];

        /// <summary>
        /// 强制按叶子 Element 处理的 ID。
        /// 优先级高于内置 Master 表和 ExtraMasterElementIds。
        /// </summary>
        public HashSet<ulong> ForcedLeafElementIds { get; } = [];
    }
}