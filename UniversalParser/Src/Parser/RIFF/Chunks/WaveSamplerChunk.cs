using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'smpl' 块（Sampler chunk）：36 字节固定部分 + SampleLoop 数组（每项 24 字节）
    /// + cbSamplerData 字节的厂商私有数据（不解析）。
    /// </summary>
    internal static class WaveSamplerChunk
    {
        private const int FixedSize = 36;
        private const int LoopSize = 24;
        private const long MaxEntries = 134_217_728 - 16;

        /// <summary>dwMIDIPitchFraction：0x80000000 表示半个半音（50 音分）。</summary>
        private const double PitchFractionScale = 4294967296.0;

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(24);
            bool be = parser.IsBigEndian;
            long payloadLength = Math.Max(0, header.PayloadLength);

            int read = ChunkUtil.ReadPayload(parser, header, FixedSize, out byte[] payload);
            if (read < FixedSize)
            {
                dataLines.Add(("<Error>", $"'smpl' requires a {FixedSize}-byte fixed part, {read} available."));
                ChunkUtil.AddUnparsedLength(dataLines, header, 0);
                return ChunkUtil.Build(parser, node, header, "Sampler", dataLines);
            }

            var span = new ReadOnlySpan<byte>(payload, 0, read);

            uint manufacturer = RIFFUtil.ReadUInt32(span[..4], be);
            uint product = RIFFUtil.ReadUInt32(span.Slice(4, 4), be);
            uint samplePeriod = RIFFUtil.ReadUInt32(span.Slice(8, 4), be);
            uint unityNote = RIFFUtil.ReadUInt32(span.Slice(12, 4), be);
            uint pitchFraction = RIFFUtil.ReadUInt32(span.Slice(16, 4), be);
            uint smpteFormat = RIFFUtil.ReadUInt32(span.Slice(20, 4), be);
            uint smpteOffset = RIFFUtil.ReadUInt32(span.Slice(24, 4), be);
            uint loopCount = RIFFUtil.ReadUInt32(span.Slice(28, 4), be);
            uint samplerDataSize = RIFFUtil.ReadUInt32(span.Slice(32, 4), be);

            dataLines.Add(("dwManufacturer", $"0x{manufacturer:X8}"));
            if (manufacturer == 0)
                dataLines.Add(("<dwManufacturer>", "0: no specific manufacturer"));

            dataLines.Add(("dwProduct", $"0x{product:X8}"));
            dataLines.Add(("dwSamplePeriod", samplePeriod.ToString()));
            dataLines.Add(("dwMIDIUnityNote", unityNote.ToString()));

            dataLines.Add(("dwMIDIPitchFraction", $"0x{pitchFraction:X8}"));
            dataLines.Add(("<dwMIDIPitchFraction>",
                $"{pitchFraction / PitchFractionScale * 100:0.###} cents above the unity note"));

            dataLines.Add(("dwSMPTEFormat", smpteFormat.ToString()));
            dataLines.Add(("<dwSMPTEFormat>", DescribeSmpteFormat(smpteFormat)));

            dataLines.Add(("dwSMPTEOffset", $"0x{smpteOffset:X8}"));
            dataLines.Add(("<dwSMPTEOffset>",
                $"{smpteOffset >> 24:00}:{(smpteOffset >> 16) & 0xFF:00}:"
                + $"{(smpteOffset >> 8) & 0xFF:00}:{smpteOffset & 0xFF:00} (hh:mm:ss:ff)"));

            dataLines.Add(("cSampleLoops", loopCount.ToString()));
            dataLines.Add(("cbSamplerData", samplerDataSize.ToString()));

            if (unityNote > 127)
                dataLines.Add(("<Warning>", "dwMIDIUnityNote is outside the valid MIDI note range 0-127."));

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'smpl' chunk is truncated."));

            // ---- SampleLoop 数组 ----
            long afterFixed = payloadLength - FixedSize;
            long loopBytes = Math.Min(afterFixed, (long)loopCount * LoopSize);
            long presentLoops = loopBytes / LoopSize;
            long loopRemainder = loopBytes % LoopSize;

            if (loopRemainder != 0)
            {
                dataLines.Add(("<Warning>",
                    $"The loop array is not a multiple of {LoopSize} bytes; "
                    + $"the trailing {loopRemainder} byte(s) do not form a complete loop."));
            }

            if (presentLoops != loopCount)
            {
                dataLines.Add(("<Note>",
                    $"cSampleLoops is {loopCount} while {presentLoops} complete loops fit in the payload; "
                    + "all present loops are listed."));
            }

            if (presentLoops > MaxEntries)
            {
                dataLines.Add(("<Error>",
                    $"The array holds {presentLoops:N0} loops, which exceeds the {MaxEntries:N0} rows that can be "
                    + "materialised in a single result. The array is left undecoded rather than truncated."));
                ChunkUtil.AddUnparsedLength(dataLines, header, FixedSize);
                return ChunkUtil.Build(parser, node, header, "Sampler", dataLines);
            }

            if (presentLoops > 0)
                AppendLoops(parser, header, dataLines, presentLoops, be);

            // ---- 厂商私有数据 + 任何未覆盖的尾部一律计入未解析长度 ----
            if (samplerDataSize > 0)
            {
                dataLines.Add(("<Note>",
                    "Trailing bytes hold manufacturer-specific sampler data; not decoded by design."));
            }

            ChunkUtil.AddUnparsedLength(dataLines, header, FixedSize + presentLoops * LoopSize);
            return ChunkUtil.Build(parser, node, header, "Sampler", dataLines);
        }

        private static void AppendLoops(
            RIFFParser parser,
            in RIFFChunkHeader header,
            List<(string K, string V)> dataLines,
            long loopCount,
            bool be)
        {
            dataLines.Add(($"SampleLoops[{loopCount}]",
                "dwIdentifier,dwType,dwStart,dwEnd,dwFraction,dwPlayCount"));

            var builder = new StringBuilder(96);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(LoopSize * 1024);
            try
            {
                int blockSize = buffer.Length - buffer.Length % LoopSize;
                long position = header.PayloadStart + FixedSize;
                long remaining = loopCount * LoopSize;

                while (remaining > 0)
                {
                    int want = (int)Math.Min(blockSize, remaining);
                    int read = parser.ReadAt(position, buffer.AsSpan(0, want));
                    read -= read % LoopSize;

                    if (read <= 0)
                    {
                        dataLines.Add(("<Warning>", "Unable to read the remaining sample loops."));
                        break;
                    }

                    var block = new ReadOnlySpan<byte>(buffer, 0, read);
                    for (int offset = 0; offset + LoopSize <= read; offset += LoopSize)
                    {
                        ReadOnlySpan<byte> loop = block.Slice(offset, LoopSize);
                        uint type = RIFFUtil.ReadUInt32(loop.Slice(4, 4), be);

                        builder.Clear();
                        builder.Append(RIFFUtil.ReadUInt32(loop[..4], be))
                               .Append(',').Append(type)
                               .Append(" (").Append(DescribeLoopType(type)).Append(')')
                               .Append(',').Append(RIFFUtil.ReadUInt32(loop.Slice(8, 4), be))
                               .Append(',').Append(RIFFUtil.ReadUInt32(loop.Slice(12, 4), be))
                               .Append(',').Append(RIFFUtil.ReadUInt32(loop.Slice(16, 4), be))
                               .Append(',').Append(RIFFUtil.ReadUInt32(loop.Slice(20, 4), be));

                        dataLines.Add((string.Empty, builder.ToString()));
                    }

                    position += read;
                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static string DescribeSmpteFormat(uint value) => value switch
        {
            0 => "0: no SMPTE offset",
            24 => "24 fps",
            25 => "25 fps",
            29 => "30 fps drop frame",
            30 => "30 fps",
            _ => "Unknown SMPTE format",
        };

        private static string DescribeLoopType(uint value) => value switch
        {
            0 => "forward",
            1 => "alternating",
            2 => "backward",
            < 32 => "reserved",
            _ => "manufacturer specific",
        };
    }
}