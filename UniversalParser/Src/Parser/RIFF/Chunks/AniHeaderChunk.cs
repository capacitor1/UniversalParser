using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// ANI 动画光标头部 'anih'（ANIHEADER，负载 36 字节）。
    /// </summary>
    internal static class AniHeaderChunk
    {
        private const int StructSize = 36;
        private const uint AfSequence = 0x00000002;

        private static readonly (uint Mask, string Name)[] HeaderFlags =
        [
            (0x00000001, "AF_ICON"),
            (0x00000002, "AF_SEQUENCE"),
        ];

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();
            int read = ChunkUtil.ReadPayload(parser, header, StructSize, out byte[] payload);

            if (read < StructSize)
            {
                dataLines.Add(("<Error>", $"ANIHEADER requires {StructSize} bytes, {read} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "AniHeader", dataLines);
            }

            bool be = parser.IsBigEndian;
            var span = new ReadOnlySpan<byte>(payload, 0, read);

            uint cbSizeOf = RIFFUtil.ReadUInt32(span.Slice(0, 4), be);
            uint cFrames = RIFFUtil.ReadUInt32(span.Slice(4, 4), be);
            uint cSteps = RIFFUtil.ReadUInt32(span.Slice(8, 4), be);
            uint cx = RIFFUtil.ReadUInt32(span.Slice(12, 4), be);
            uint cy = RIFFUtil.ReadUInt32(span.Slice(16, 4), be);
            uint cBitCount = RIFFUtil.ReadUInt32(span.Slice(20, 4), be);
            uint cPlanes = RIFFUtil.ReadUInt32(span.Slice(24, 4), be);
            uint jifRate = RIFFUtil.ReadUInt32(span.Slice(28, 4), be);
            uint flags = RIFFUtil.ReadUInt32(span.Slice(32, 4), be);

            dataLines.Add(("cbSizeOf", cbSizeOf.ToString()));
            dataLines.Add(("cFrames", cFrames.ToString()));
            dataLines.Add(("cSteps", cSteps.ToString()));
            dataLines.Add(("cx", cx.ToString()));
            dataLines.Add(("cy", cy.ToString()));
            dataLines.Add(("cBitCount", cBitCount.ToString()));
            dataLines.Add(("cPlanes", cPlanes.ToString()));
            dataLines.Add(("JifRate", jifRate.ToString()));
            dataLines.Add(("flags", $"0x{flags:X8}"));
            dataLines.Add(("<flags>", ChunkUtil.DescribeFlags(flags, HeaderFlags)));

            if (cbSizeOf != StructSize)
                dataLines.Add(("<Warning>", $"cbSizeOf should be {StructSize}, found {cbSizeOf}."));

            if ((cx | cy | cBitCount | cPlanes) != 0)
            {
                dataLines.Add(("<Note>",
                    "cx, cy, cBitCount and cPlanes are reserved and should be zero; "
                    + "the frame dimensions are carried by each frame's own image data."));
            }

            if ((flags & AfSequence) == 0 && cSteps != cFrames)
            {
                dataLines.Add(("<Warning>",
                    $"AF_SEQUENCE is clear, so cSteps should equal cFrames; found {cSteps} and {cFrames}."));
            }

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'anih' chunk is truncated."));

            ChunkUtil.AddUnparsedLength(dataLines, header, StructSize);
            return ChunkUtil.Build(parser, node, header, "AniHeader", dataLines);
        }
    }
}