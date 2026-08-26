using System;
using System.Buffers.Binary;
using System.Text;

namespace UniversalParser.Src.Parser.FBX
{
    internal static class FBXUtil
    {
        /// <summary>
        /// Binary FBX 文件头。
        ///
        /// ASCII:
        /// Kaydara FBX Binary  \0\x1a\0
        /// </summary>
        public static readonly byte[] BinarySignature =
        [
            0x4B, 0x61, 0x79, 0x64, 0x61, 0x72, 0x61,
            0x20,
            0x46, 0x42, 0x58,
            0x20,
            0x42, 0x69, 0x6E, 0x61, 0x72, 0x79,
            0x20, 0x20,
            0x00,
            0x1A,
            0x00
        ];

        public const int SignatureLength = 23;
        public const int VersionOffset = 23;
        public const int HeaderLength = 27;

        /// <summary>
        /// FBX 7.5 使用 64 位节点偏移字段。
        /// </summary>
        public const uint ExtendedNodeVersion = 7500;

        /// <summary>
        /// 旧版节点固定头长度：
        ///
        /// EndOffset          uint32  4
        /// NumProperties      uint32  4
        /// PropertyListLen    uint32  4
        /// NameLen            byte    1
        /// </summary>
        public const int LegacyNodeHeaderLength = 13;

        /// <summary>
        /// FBX 7.5+ 节点固定头长度：
        ///
        /// EndOffset          uint64  8
        /// NumProperties      uint64  8
        /// PropertyListLen    uint64  8
        /// NameLen            byte    1
        /// </summary>
        public const int ExtendedNodeHeaderLength = 25;

        public static bool IsExtendedVersion(uint version)
        {
            return version >= ExtendedNodeVersion;
        }

        public static int GetFixedNodeHeaderLength(uint version)
        {
            return IsExtendedVersion(version)
                ? ExtendedNodeHeaderLength
                : LegacyNodeHeaderLength;
        }

        public static bool IsBinarySignature(ReadOnlySpan<byte> data)
        {
            return data.Length >= SignatureLength &&
                   data[..SignatureLength].SequenceEqual(BinarySignature);
        }

        public static uint ReadUInt32LE(ReadOnlySpan<byte> data)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(data);
        }

        public static ulong ReadUInt64LE(ReadOnlySpan<byte> data)
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(data);
        }

        public static string DecodeNodeName(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
                return string.Empty;

            return Encoding.UTF8.GetString(data);
        }

        public static string Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            Span<char> result = value.Length <= 256
                ? stackalloc char[value.Length]
                : new char[value.Length];

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                result[i] = c switch
                {
                    '\r' or '\n' or '\t' => ' ',
                    < ' ' => '.',
                    _ => c
                };
            }

            return new string(result);
        }

        /// <summary>
        /// FBX 节点没有 RIFF 风格的 4CC。
        /// 这里仍然保持与现有框架一致的标题格式：
        ///
        /// Unknown 'Model'
        /// </summary>
        public static string MakeTitle(string readableName, string originalName)
        {
            return $"{readableName} '{Sanitize(originalName)}'";
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0)
                return bytes.ToString();

            if (bytes < 1024)
                return $"{bytes} B";

            string[] units =
            [
                "KiB",
                "MiB",
                "GiB",
                "TiB",
                "PiB"
            ];

            double value = bytes / 1024.0;
            int unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.##} {units[unit]} ({bytes:N0} B)";
        }

        public static long ClampToLong(ulong value)
        {
            return value > long.MaxValue
                ? long.MaxValue
                : (long)value;
        }

        public static bool IsZeroRecord(
            ulong endOffset,
            ulong propertyCount,
            ulong propertyListLength,
            byte nameLength)
        {
            return endOffset == 0 &&
                   propertyCount == 0 &&
                   propertyListLength == 0 &&
                   nameLength == 0;
        }
    }
}