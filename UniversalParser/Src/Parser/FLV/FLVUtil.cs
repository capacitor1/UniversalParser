using System;
using System.Buffers.Binary;
using System.Globalization;

namespace UniversalParser.Src.Parser.FLV
{
    internal static class FLVUtil
    {
        public const int MinimumHeaderSize = 9;
        public const int PreviousTagSizeFieldSize = 4;
        public const int TagHeaderSize = 11;

        public static uint ReadUInt24BE(ReadOnlySpan<byte> data)
        {
            if (data.Length < 3)
                throw new ArgumentException("At least three bytes are required.", nameof(data));

            return ((uint)data[0] << 16)
                 | ((uint)data[1] << 8)
                 | data[2];
        }

        public static int ReadInt24BE(ReadOnlySpan<byte> data)
        {
            uint value = ReadUInt24BE(data);

            // 24-bit 二进制补码符号扩展
            if ((value & 0x0080_0000) != 0)
                value |= 0xFF00_0000;

            return unchecked((int)value);
        }

        public static uint ReadUInt32BE(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
                throw new ArgumentException("At least four bytes are required.", nameof(data));

            return BinaryPrimitives.ReadUInt32BigEndian(data);
        }

        public static ushort ReadUInt16BE(ReadOnlySpan<byte> data)
        {
            if (data.Length < 2)
                throw new ArgumentException("At least two bytes are required.", nameof(data));

            return BinaryPrimitives.ReadUInt16BigEndian(data);
        }

        public static double ReadDoubleBE(ReadOnlySpan<byte> data)
        {
            if (data.Length < 8)
                throw new ArgumentException("At least eight bytes are required.", nameof(data));

            long bits = BinaryPrimitives.ReadInt64BigEndian(data);
            return BitConverter.Int64BitsToDouble(bits);
        }

        public static string MakeTitle(string readableName, string nodeName) =>
            $"{readableName} '{Sanitize(nodeName)}'";

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
                result[i] = c is >= ' ' and <= '~' || c > 0x9F ? c : '.';
            }

            return new string(result);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0)
                return bytes.ToString(CultureInfo.InvariantCulture);

            if (bytes < 1024)
                return $"{bytes} B";

            string[] units = ["KiB", "MiB", "GiB", "TiB", "PiB"];
            double value = bytes / 1024.0;
            int unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.##} {units[unit]} ({bytes:N0} B)";
        }

        public static string FormatTimestamp(uint milliseconds)
        {
            TimeSpan time = TimeSpan.FromMilliseconds(milliseconds);

            if (time.TotalHours >= 1)
                return $"{time:hh\\:mm\\:ss\\.fff} ({milliseconds} ms)";

            return $"{time:mm\\:ss\\.fff} ({milliseconds} ms)";
        }

        public static string DescribeTagType(byte tagType) =>
            tagType switch
            {
                8 => "Audio",
                9 => "Video",
                18 => "Script data",
                _ => "Unknown"
            };

        public static string GetTagNodeName(byte tagType) =>
            tagType switch
            {
                8 => "AudioTag",
                9 => "VideoTag",
                18 => "ScriptDataTag",
                _ => "UnknownTag"
            };

        public static string GetTagDataNodeName(byte tagType) =>
            tagType switch
            {
                8 => "AudioTagData",
                9 => "VideoTagData",
                18 => "ScriptDataTagData",
                _ => "UnknownTagData"
            };
    }
}