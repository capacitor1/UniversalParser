using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WebP 块共用辅助。
    /// WebP 规范无条件把 uint16/uint24/uint32 定义为小端，与 RIFX 的大端变体无关，
    /// 因此这里的读取一律显式使用小端，不参考 RIFFParser.IsBigEndian。
    /// </summary>
    internal static class WebPUtil
    {
        public static uint ReadUInt24LE(ReadOnlySpan<byte> span) =>
            (uint)(span[0] | (span[1] << 8) | (span[2] << 16));

        public static uint ReadUInt32LE(ReadOnlySpan<byte> span) =>
            BinaryPrimitives.ReadUInt32LittleEndian(span);

        /// <summary>把剩余未解析字节记为 &lt;PayloadLength&gt;（仅在确有剩余时）。</summary>
        public static void AddUnparsedLength(
            List<(string K, string V)> dataLines, in RIFFChunkHeader header, long parsedBytes)
        {
            ArgumentNullException.ThrowIfNull(dataLines);

            long unparsed = header.PayloadLength - parsedBytes;
            if (unparsed > 0)
                dataLines.Add(("<PayloadLength>", RIFFUtil.FormatBytes(unparsed)));
        }

        public static ParseResult Build(
            RIFFParser parser,
            Node node,
            in RIFFChunkHeader header,
            string readableName,
            List<(string K, string V)> dataLines) =>
            new()
            {
                Title = RIFFUtil.MakeTitle(readableName, header.Id),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(header.ChunkStart, (long)node.Length),
            };
    }
}