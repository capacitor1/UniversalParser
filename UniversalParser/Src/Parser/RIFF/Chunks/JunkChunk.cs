using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// 填充 / 占位块：内容无意义，仅用于对齐或预留空间，不解析。
    /// </summary>
    internal static class JunkChunk
    {
        /// <summary>4CC → 无空格英文可读名（同时作为 Dispatcher 的注册键）。</summary>
        public static readonly Dictionary<string, string> KnownIds = new(StringComparer.Ordinal)
        {
            ["JUNK"] = "Padding",       // RIFF 标准填充块
            ["junk"] = "Padding",       // 小写
            ["JUNQ"] = "Padding",       // 部分写入器使用的等价变体
            ["PAD "] = "Padding",       // BWF / 广播类文件常见
            ["FLLR"] = "Filler",        // 扇区对齐填充（Sound Forge 等）
        };

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            List<(string K, string V)>? extra = header.IsTruncated
                ? [("<Warning>", "The padding chunk is truncated.")]
                : null;

            string readableName = KnownIds.TryGetValue(header.Id, out string? name) ? name : "Padding";
            return OpaqueChunk.Build(parser, node, header, readableName, trailingLines: extra);
        }
    }
}