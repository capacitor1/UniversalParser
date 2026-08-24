using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// Photoshop 图像资源块（'8BIM'）序列的结构解析。
    /// 这些块一律为大端，与所在 RIFF 容器的字节序无关；它们不是 RIFF 子块，
    /// 因此承载它们的 chunk 绝不能登记为容器。
    /// 只解析块头（签名 / 标识符 / 名称 / 长度），资源数据本身按设计不解码。
    /// </summary>
    internal static class PhotoshopResourceBlocks
    {
        public const string Columns = "Signature,Identifier,Name,Size";

        /// <summary>签名 + 标识符 + 最短名称字段（长度字节 0 + 填充）+ 长度字段。</summary>
        private const int MinBlockSize = 4 + 2 + 2 + 4;

        /// <summary>名称为 Pascal 串，最长 255 字符，故块头最大 4 + 2 + (1 + 255 + 1) + 4。</summary>
        private const int MaxHeaderWindow = 4 + 2 + 257 + 4;

        /// <summary>结构性上限，防止损坏数据生成无上限的行。</summary>
        private const int MaxBlocks = 100_000;

        /// <summary>已知的资源块签名。'8BIM' 为标准，其余为历史或第三方写入器所用。</summary>
        private static readonly HashSet<string> KnownSignatures = new(StringComparer.Ordinal)
        {
            "8BIM",   // Photoshop 标准
            "MeSa",   // ImageReady
            "PHUT",   // PhotoDeluxe
            "DCSR",   // Kodak DCS
            "AgHg",   // Adobe Lightroom
            "8B64",   // 64 位变体
        };

        internal readonly record struct ScanResult(
            int BlockCount,
            long ResourceDataBytes,
            long TrailingBytes,
            bool Malformed,
            bool NonStandardSignature);

        /// <summary>
        /// 快速判定一段载荷是否为资源块序列：签名已知，且首块声明的长度能容纳在范围之内。
        /// 只读取块头，不读数据。
        /// </summary>
        public static bool Probe(RIFFParser parser, long start, long end)
        {
            ArgumentNullException.ThrowIfNull(parser);

            if (end - start < MinBlockSize) return false;

            Span<byte> head = stackalloc byte[MinBlockSize];
            if (parser.ReadAt(start, head) < MinBlockSize) return false;

            if (!RIFFUtil.IsPrintableFourCC(head[..4])) return false;
            if (!KnownSignatures.Contains(RIFFUtil.DecodeFourCC(head[..4]))) return false;

            // 空名称时长度字段位于偏移 8；名称非空则需要更宽的窗口，此处只对最常见形态做确认
            int nameLength = head[6];
            if (nameLength != 0) return true;

            uint size = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(8, 4));
            return MinBlockSize + (long)size <= end - start;
        }

        /// <summary>扫描 [start, end) 内的资源块，把 CSV 行写入 rows。</summary>
        public static ScanResult Scan(RIFFParser parser, long start, long end, List<string> rows)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(rows);

            long pos = start;
            int count = 0;
            long dataBytes = 0;
            bool malformed = false;
            bool nonStandard = false;

            byte[] window = new byte[MaxHeaderWindow];

            while (pos + MinBlockSize <= end)
            {
                if (count >= MaxBlocks) { malformed = true; break; }

                int want = (int)Math.Min(MaxHeaderWindow, end - pos);
                int read = parser.ReadAt(pos, window.AsSpan(0, want));
                if (read < MinBlockSize) { malformed = true; break; }

                var span = new ReadOnlySpan<byte>(window, 0, read);

                if (!RIFFUtil.IsPrintableFourCC(span[..4])) { malformed = true; break; }
                string signature = RIFFUtil.DecodeFourCC(span[..4]);
                if (!KnownSignatures.Contains(signature)) nonStandard = true;

                ushort id = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(4, 2));

                // 名称为 Pascal 串，含长度字节在内补齐到偶数长度
                int nameLength = span[6];
                if (7 + nameLength > read) { malformed = true; break; }

                string name = nameLength > 0
                    ? Encoding.Latin1.GetString(span.Slice(7, nameLength))
                    : string.Empty;

                int nameFieldSize = 1 + nameLength;
                if ((nameFieldSize & 1) != 0) nameFieldSize++;

                int sizeOffset = 6 + nameFieldSize;
                if (sizeOffset + 4 > read) { malformed = true; break; }

                uint size = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(sizeOffset, 4));

                long dataStart = pos + sizeOffset + 4;
                long paddedSize = (long)size + (size & 1);   // 数据补齐到偶数长度

                rows.Add(FormatRow(signature, id, name, size));
                count++;

                if (dataStart + paddedSize > end)
                {
                    // 声明长度越界：记录该块后停止
                    dataBytes += Math.Max(0, end - dataStart);
                    malformed = true;
                    pos = end;
                    break;
                }

                dataBytes += size;
                pos = dataStart + paddedSize;
            }

            return new ScanResult(count, dataBytes, Math.Max(0, end - pos), malformed, nonStandard);
        }

        private static string FormatRow(string signature, ushort id, string name, uint size) =>
            $"{Csv(signature)},{id} ({DescribeId(id)}),{Csv(name)},{size}";

        private static string Csv(string value)
        {
            string clean = RIFFUtil.Sanitize(value);
            if (clean.IndexOfAny([',', '"']) < 0) return clean;
            return $"\"{clean.Replace("\"", "\"\"")}\"";
        }

        /// <summary>Adobe Photoshop File Formats Specification 中定义的资源标识符。</summary>
        private static string DescribeId(ushort id)
        {
            if (KnownIds.TryGetValue(id, out string? name)) return name;
            if (id is >= 2000 and <= 2997) return "PathInformation";
            if (id is >= 4000 and <= 4999) return "PluginResource";
            return "undocumented";
        }

        /// <summary>名称中不含逗号，以免破坏 CSV 列对齐。</summary>
        private static readonly Dictionary<ushort, string> KnownIds = new()
        {
            [1000] = "ChannelsRowsColumnsDepthMode (obsolete)",
            [1001] = "MacPrintManagerInfo",
            [1002] = "MacPageFormatInfo (obsolete)",
            [1003] = "IndexedColorTable (obsolete)",
            [1005] = "ResolutionInfo",
            [1006] = "AlphaChannelNames",
            [1007] = "DisplayInfo (obsolete)",
            [1008] = "Caption",
            [1009] = "BorderInformation",
            [1010] = "BackgroundColor",
            [1011] = "PrintFlags",
            [1012] = "GrayscaleHalftoningInfo",
            [1013] = "ColorHalftoningInfo",
            [1014] = "DuotoneHalftoningInfo",
            [1015] = "GrayscaleTransferFunction",
            [1016] = "ColorTransferFunction",
            [1017] = "DuotoneTransferFunction",
            [1018] = "DuotoneImageInfo",
            [1019] = "EffectiveBlackWhiteValues",
            [1021] = "EpsOptions",
            [1022] = "QuickMaskInfo",
            [1024] = "LayerStateInfo",
            [1025] = "WorkingPath",
            [1026] = "LayersGroupInfo",
            [1028] = "IptcNaaRecord",
            [1029] = "RawFormatImageMode",
            [1030] = "JpegQuality",
            [1032] = "GridAndGuidesInfo",
            [1033] = "Thumbnail (Photoshop 4.0 format)",
            [1034] = "CopyrightFlag",
            [1035] = "Url",
            [1036] = "Thumbnail",
            [1037] = "GlobalAngle",
            [1038] = "ColorSamplers (obsolete)",
            [1039] = "IccProfile",
            [1040] = "Watermark",
            [1041] = "IccUntaggedProfile",
            [1042] = "EffectsVisible",
            [1043] = "SpotHalftone",
            [1044] = "DocumentSpecificIdsSeedNumber",
            [1045] = "UnicodeAlphaNames",
            [1046] = "IndexedColorTableCount",
            [1047] = "TransparencyIndex",
            [1049] = "GlobalAltitude",
            [1050] = "Slices",
            [1051] = "WorkflowUrl",
            [1052] = "JumpToXpep",
            [1053] = "AlphaIdentifiers",
            [1054] = "UrlList",
            [1057] = "VersionInfo",
            [1058] = "ExifData1",
            [1059] = "ExifData3",
            [1060] = "XmpMetadata",
            [1061] = "CaptionDigest",
            [1062] = "PrintScale",
            [1064] = "PixelAspectRatio",
            [1065] = "LayerComps",
            [1066] = "AlternateDuotoneColors",
            [1067] = "AlternateSpotColors",
            [1069] = "LayerSelectionIds",
            [1070] = "HdrToningInfo",
            [1071] = "PrintInfo",
            [1072] = "LayerGroupsEnabledId",
            [1073] = "ColorSamplers",
            [1074] = "MeasurementScale",
            [1075] = "TimelineInfo",
            [1076] = "SheetDisclosure",
            [1077] = "DisplayInfo",
            [1078] = "OnionSkins",
            [1080] = "CountInformation",
            [1082] = "PrintInformation",
            [1083] = "PrintStyle",
            [1084] = "MacintoshNsPrintInfo",
            [1085] = "WindowsDevmode",
            [1086] = "AutoSaveFilePath",
            [1087] = "AutoSaveFormat",
            [1088] = "PathSelectionState",
            [2999] = "ClippingPathName",
            [3000] = "OriginPathInfo",
            [7000] = "ImageReadyVariables",
            [7001] = "ImageReadyDataSets",
            [7002] = "ImageReadyDefaultSelectedState",
            [7003] = "ImageReady7RolloverExpandedState",
            [7004] = "ImageReadyRolloverExpandedState",
            [7005] = "ImageReadySaveLayerSettings",
            [7006] = "ImageReadyVersion",
            [8000] = "LightroomWorkflow",
            [10000] = "PrintFlagsInformation",
        };
    }
}