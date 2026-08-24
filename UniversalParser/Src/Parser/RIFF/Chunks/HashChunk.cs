using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// 'hash' 块：整个负载就是摘要二进制本身，没有任何头部或结构。
    /// 该 FourCC 未见于任何已发布规范，因此不存在官方字段名，全部项目均为派生项；
    /// 算法只能由长度推断，且长度本身不足以唯一确定算法。
    /// </summary>
    internal static class HashChunk
    {
        /// <summary>
        /// 十六进制输出上限。已知最长的常见摘要为 64 字节，此处留足余量；
        /// 超限（只可能来自损坏数据）时余下部分计入未解析长度，而不是静默丢弃。
        /// </summary>
        private const int MaxDigestBytes = 1024;

        /// <summary>字节长度 → 候选算法，按常见程度排序。</summary>
        private static readonly Dictionary<int, string> DigestLengths = new()
        {
            [4] = "CRC-32",
            [8] = "CRC-64 / xxHash64",
            [16] = "MD5 / MD4 / RIPEMD-128 / BLAKE2s-128",
            [20] = "SHA-1 / RIPEMD-160",
            [24] = "Tiger-192",
            [28] = "SHA-224 / SHA3-224",
            [32] = "SHA-256 / SHA3-256 / BLAKE2s-256 / BLAKE3",
            [48] = "SHA-384 / SHA3-384",
            [64] = "SHA-512 / SHA3-512 / BLAKE2b-512 / Whirlpool",
        };

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(6);
            long payloadLength = Math.Max(0, header.PayloadLength);

            if (payloadLength == 0)
            {
                dataLines.Add(("<Error>", "The chunk carries no digest bytes."));
                return ChunkUtil.Build(parser, node, header, "Hash", dataLines);
            }

            int read = ChunkUtil.ReadPayload(parser, header, MaxDigestBytes, out byte[] payload);

            if (read <= 0)
            {
                dataLines.Add(("<Error>", "The digest bytes could not be read."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "Hash", dataLines);
            }

            var digest = new ReadOnlySpan<byte>(payload, 0, read);

            dataLines.Add(("<Digest>", ChunkUtil.Hex(digest)));
            dataLines.Add(("<DigestLength>", $"{payloadLength} B ({payloadLength * 8} bits)"));
            dataLines.Add(("<Algorithm>", InferAlgorithm(payloadLength)));
            dataLines.Add(("<Note>",
                "No published RIFF or WAVE specification defines a 'hash' chunk. The payload is presented as raw "
                + "digest bytes; neither the algorithm nor the byte range it covers is recorded in the file."));

            if (ChunkUtil.IsAllZero(digest))
                dataLines.Add(("<Warning>", "The digest is entirely zero, which usually means it was never written."));

            if (payloadLength > MaxDigestBytes)
            {
                dataLines.Add(("<Warning>",
                    $"The payload is {payloadLength} bytes, far beyond any known digest size; "
                    + $"only the first {MaxDigestBytes} bytes are shown."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'hash' chunk is truncated."));

            ChunkUtil.AddUnparsedLength(dataLines, header, read);
            return ChunkUtil.Build(parser, node, header, "Hash", dataLines);
        }

        private static string InferAlgorithm(long length)
        {
            if (length is > 0 and <= int.MaxValue
                && DigestLengths.TryGetValue((int)length, out string? candidates))
            {
                return $"{candidates} — inferred from the {length}-byte length, which is not conclusive";
            }

            return $"{length} bytes does not match a common digest size";
        }
    }
}