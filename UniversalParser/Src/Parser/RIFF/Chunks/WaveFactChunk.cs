using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'fact' 块。规范只定义了首个 DWORD（dwSampleLength），
    /// 并允许其后追加格式相关的附加数据；附加数据不解析。
    /// </summary>
    internal static class WaveFactChunk
    {
        private const int DefinedSize = 4;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(4);
            int read = ChunkUtil.ReadPayload(parser, header, DefinedSize, out byte[] payload);

            if (read < DefinedSize)
            {
                dataLines.Add(("<Error>", $"'fact' requires at least {DefinedSize} bytes, {read} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "WaveFact", dataLines);
            }

            uint sampleLength = RIFFUtil.ReadUInt32(payload, 0, parser.IsBigEndian);
            dataLines.Add(("dwSampleLength", sampleLength.ToString()));

            if (sampleLength == uint.MaxValue && parser.RootId is "RF64" or "BW64")
            {
                dataLines.Add(("<Note>",
                    "0xFFFFFFFF is the RF64/BW64 placeholder; the real sample count is carried by 'ds64'."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'fact' chunk is truncated."));

            if (header.PayloadLength > DefinedSize)
            {
                dataLines.Add(("<Note>",
                    "Trailing bytes hold format-specific additional data; not decoded by design."));
            }

            ChunkUtil.AddUnparsedLength(dataLines, header, DefinedSize);
            return ChunkUtil.Build(parser, node, header, "WaveFact", dataLines);
        }
    }
}