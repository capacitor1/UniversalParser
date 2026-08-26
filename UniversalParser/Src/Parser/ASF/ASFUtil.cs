using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.ASF
{
    /// <summary>ASF 相关的公共常量与工具方法（MS-ASF 规范）。</summary>
    internal static class ASFUtil
    {
        public const int GuidSize = 16;
        public const int ObjectHeaderSize = 24; // Object GUID(16) + Object Size(8)

        // =========================
        // 顶层 / 索引对象
        // =========================
        public static readonly Guid HeaderObject = new("75B22630-668E-11CF-A6D9-00AA0062CE6C");
        public static readonly Guid DataObject = new("75B22636-668E-11CF-A6D9-00AA0062CE6C");
        public static readonly Guid SimpleIndexObject = new("33000890-E5B1-11CF-89F4-00A0C90349CB");
        public static readonly Guid IndexObject = new("D6E229D3-35DA-11D1-9034-00A0C90349BE");
        public static readonly Guid MediaObjectIndexObject = new("FEB103F8-12AD-4C64-840F-2A1D2F7AD48C");
        public static readonly Guid TimecodeIndexObject = new("3CB73FD7-0C4A-4801-953D-EDF7B6228F0C");

        // =========================
        // Header Object 子对象
        // =========================
        public static readonly Guid FilePropertiesObject = new("8CABDCA1-A947-11CF-8EE4-00C00C205365");
        public static readonly Guid StreamPropertiesObject = new("B7DC0791-A9B7-11CF-8EE6-00C00C205365");
        public static readonly Guid HeaderExtensionObject = new("5FBF03B5-A92E-11CF-8EE3-00C00C205365");
        public static readonly Guid CodecListObject = new("86D15240-311D-11D0-A3A4-00A0C90348F6");
        public static readonly Guid ScriptCommandObject = new("1EFB1A30-0B62-11D0-A39B-00A0C90348F6");
        public static readonly Guid MarkerObject = new("F487CD01-A951-11CF-8EE5-00C00C205365");
        public static readonly Guid BitrateMutualExclusionObject = new("D6E229DC-35DA-11D1-9034-00A0C90349BE");
        public static readonly Guid ErrorCorrectionObject = new("75B22635-668E-11CF-A6D9-00AA0062CE6C");
        public static readonly Guid ContentDescriptionObject = new("75B22633-668E-11CF-A6D9-00AA0062CE6C");
        public static readonly Guid ExtendedContentDescriptionObject = new("D2D0A440-E307-11D2-97F0-00A0C95EA850");
        public static readonly Guid StreamingMediaPropertiesObject = new("7BF875CE-468D-11D1-8D82-006097C9A2B2");
        public static readonly Guid PaddingObject = new("1806D474-CADF-4509-A4BA-9AABCB96AAE8");
        public static readonly Guid ContentEncryptionObject = new("2211B3FB-BD23-11D2-B4B7-00A0C955FC6E");
        public static readonly Guid ExtendedContentEncryptionObject = new("298AE614-2622-4C17-B935-DAE07EE9289C");
        public static readonly Guid DigitalSignatureObject = new("2211B3FC-BD23-11D2-B4B7-00A0C955FC6E");
        public static readonly Guid AdvancedMutualExclusionObject = new("A08649CF-4775-4670-8A16-6E35357566CD");
        public static readonly Guid GroupMutualExclusionObject = new("D1465A40-5A79-4338-B71B-E36B8FD6C249");
        public static readonly Guid CompatibilityObject = new("75B22632-668E-11CF-A6D9-00AA0062CE6C");
        public static readonly Guid LanguageListObject = new("7C4346A9-EFE0-4BFC-B229-393EDE415C85");

        // =========================
        // Header Extension 子对象
        // =========================
        public static readonly Guid MetadataObject = new("C5F8CBEA-5BAF-4877-8467-AA8C44FA4CCA");
        public static readonly Guid MetadataLibraryObject = new("44231C94-9498-49D1-A141-1D134E457054");
        public static readonly Guid IndexParametersObject = new("D6E229DF-35DA-11D1-9034-00A0C90349BE");
        public static readonly Guid MediaObjectIndexParametersObject = new("6B203BAD-3F11-48E4-ACA8-D7613DE2CFA7");
        public static readonly Guid TimecodeIndexParametersObject = new("F55E496D-9797-4B5D-8C8B-604DFE9BFB24");

        // =========================
        // Stream Type（Stream Properties 解析用，TODO）
        // =========================
        public static readonly Guid AudioMedia = new("F8699E40-5B4D-11CF-A8FD-00805F5C442B");
        public static readonly Guid VideoMedia = new("BC19EFC0-5B4D-11CF-A8FD-00805F5C442B");
        public static readonly Guid CommandMedia = new("59DACFC0-59E6-11D0-A3AC-00A0C90348F6");
        public static readonly Guid JFIFMedia = new("B61BE100-5B4E-11CF-A8FD-00805F5C442B");
        public static readonly Guid DegradableJPEGMedia = new("35907DE0-E415-11CF-A917-00805F5C442B");
        public static readonly Guid FileTransferMedia = new("91BD222C-F21C-497A-8B6D-44AA6224C428");
        public static readonly Guid BinaryMedia = new("3AFB65E2-47EF-40F2-AC2C-70A90D71D343");
        public static readonly Guid WebStreamMedia = new("776257D4-C627-41CB-8F81-7AC7FF1C40CC");
        public static readonly Guid NoErrorCorrection = new("20FB5700-5B55-11CF-A8FD-00805F5C442B");
        public static readonly Guid AudioSpread = new("BFC3CD50-618F-11CF-8BB2-00AA00B4E220");

        // =========================
        // 对象可读名
        // =========================
        private static readonly Dictionary<Guid, string> ObjectNames = new()
        {
            [HeaderObject] = "Header",
            [DataObject] = "Data",
            [SimpleIndexObject] = "SimpleIndex",
            [IndexObject] = "Index",
            [MediaObjectIndexObject] = "MediaObjectIndex",
            [TimecodeIndexObject] = "TimecodeIndex",
            [FilePropertiesObject] = "FileProperties",
            [StreamPropertiesObject] = "StreamProperties",
            [HeaderExtensionObject] = "HeaderExtension",
            [CodecListObject] = "CodecList",
            [ScriptCommandObject] = "ScriptCommand",
            [MarkerObject] = "Marker",
            [BitrateMutualExclusionObject] = "BitrateMutualExclusion",
            [ErrorCorrectionObject] = "ErrorCorrection",
            [ContentDescriptionObject] = "ContentDescription",
            [ExtendedContentDescriptionObject] = "ExtendedContentDescription",
            [StreamingMediaPropertiesObject] = "StreamingMediaProperties",
            [PaddingObject] = "Padding",
            [ContentEncryptionObject] = "ContentEncryption",
            [ExtendedContentEncryptionObject] = "ExtendedContentEncryption",
            [DigitalSignatureObject] = "DigitalSignature",
            [AdvancedMutualExclusionObject] = "AdvancedMutualExclusion",
            [GroupMutualExclusionObject] = "GroupMutualExclusion",
            [CompatibilityObject] = "Compatibility",
            [LanguageListObject] = "LanguageList",
            [MetadataObject] = "Metadata",
            [MetadataLibraryObject] = "MetadataLibrary",
            [IndexParametersObject] = "IndexParameters",
            [MediaObjectIndexParametersObject] = "MediaObjectIndexParameters",
            [TimecodeIndexParametersObject] = "TimecodeIndexParameters",
        };

        /// <summary>ASF 中只有 Header Object 与 Header Extension Object 直接嵌套子对象。</summary>
        public static bool IsContainer(Guid guid) =>
            guid == HeaderObject || guid == HeaderExtensionObject;

        /// <summary>
        /// 容器对象自身的结构字段长度（位于 24 字节对象头之后）：
        /// Header = NumberOfHeaderObjects(4) + Reserved1(1) + Reserved2(1) = 6；
        /// HeaderExtension = Reserved1(2) + Reserved2(2) + ExtensionDataSize(4) = 8。
        /// </summary>
        public static int ContainerStructureSize(Guid guid) =>
            guid == HeaderObject ? 6 :
            guid == HeaderExtensionObject ? 8 : 0;

        public static string GuidDisplay(Guid guid) => guid.ToString("D").ToUpperInvariant();

        public static string GuidShort(Guid guid) => guid.ToString("N")[..8].ToUpperInvariant();

        public static string? TryGetObjectName(Guid guid) =>
            ObjectNames.TryGetValue(guid, out string? name) ? name : null;

        /// <summary>统一生成 ParseResult.Title：无空格英文可读名 + 空格 + 单引号包裹的标识。</summary>
        public static string MakeTitle(string readableName, string identifier) =>
            $"{readableName} '{identifier}'";

        /// <summary>把不可打印字符替换掉，避免污染 UI 的节点名 / 标题。</summary>
        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                buffer[i] = c is >= ' ' and <= '~' || c > 0x9F ? c : '.';
            }
            return new string(buffer);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return bytes.ToString();
            if (bytes < 1024) return $"{bytes} B";

            string[] units = ["KiB", "MiB", "GiB", "TiB", "PiB"];
            double value = bytes / 1024.0;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return $"{value:0.##} {units[unit]} ({bytes:N0} B)";
        }

        /// <summary>
        /// ASF 文本一律为 UTF-16LE（MS-ASF），NUL 结尾。此处按偶数边界扫描终止符后解码。
        /// </summary>
        public static string DecodeWide(ReadOnlySpan<byte> data)
        {
            int end = 0;
            while (end + 1 < data.Length)
            {
                if (data[end] == 0 && data[end + 1] == 0) break;
                end += 2;
            }
            data = data[..end];
            if (data.IsEmpty) return string.Empty;
            return Encoding.Unicode.GetString(data);
        }
    }
}