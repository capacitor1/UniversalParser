using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'data' 块：音频采样数据本体，内容不解析，交由 GUI 二进制预览呈现。
    /// </summary>
    internal static class WaveDataChunk
    {
        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            List<(string K, string V)>? extra = null;

            if (header.DeclaredSize == uint.MaxValue)
            {
                // RF64 / BW64：ckSize 是 0xFFFFFFFF 占位值，真实长度记录在 'ds64' 的 dataSize 字段。
                // 此时 IsTruncated 必然为 true，但这不是错误，不能报 Warning。
                extra =
                [
                    ("<Note>", "ckSize is 0xFFFFFFFF (RF64/BW64 placeholder); the real length is stored in 'ds64'."),
                ];
            }
            else if (header.IsTruncated)
            {
                extra =
                [
                    ("<Warning>",
                        $"Declared {header.DeclaredPayloadLength:N0} bytes but only {header.PayloadLength:N0} are available; the file is truncated."),
                ];
            }

            return OpaqueChunk.Build(parser, node, header, "WaveData", trailingLines: extra);
        }
    }
}