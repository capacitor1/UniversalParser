using System;
using System.Buffers.Binary;

namespace UniversalParser.Src.Parser.EBML
{
    internal static class EBMLUtil
    {
        public const int MaxElementIdLength = 4;
        public const int MaxDataSizeLength = 8;
        public const int MaxElementHeaderLength = MaxElementIdLength + MaxDataSizeLength;

        public const ulong EbmlHeaderId = 0x1A45DFA3;
        public const ulong SegmentId = 0x18538067;

        /// <summary>
        /// 根据首字节取得 EBML VINT 长度。
        /// 首个置位位决定长度，例如：
        /// 1xxxxxxx → 1 byte
        /// 01xxxxxx → 2 bytes
        /// 001xxxxx → 3 bytes
        /// </summary>
        public static int GetVIntLength(byte firstByte, int maximumLength)
        {
            if (firstByte == 0)
                return 0;

            int length = 1;
            byte mask = 0x80;

            while ((firstByte & mask) == 0)
            {
                length++;
                mask >>= 1;

                if (length > maximumLength || mask == 0)
                    return 0;
            }

            return length;
        }

        /// <summary>
        /// 解码 Element ID。
        /// Element ID 的长度标记位属于 ID 本身，不应清除。
        /// </summary>
        public static bool TryDecodeElementId(
            ReadOnlySpan<byte> data,
            out ulong elementId,
            out int encodedLength)
        {
            elementId = 0;
            encodedLength = 0;

            if (data.IsEmpty)
                return false;

            int length = GetVIntLength(data[0], MaxElementIdLength);
            if (length == 0 || data.Length < length)
                return false;

            ulong value = 0;
            for (int i = 0; i < length; i++)
                value = (value << 8) | data[i];

            elementId = value;
            encodedLength = length;
            return true;
        }

        /// <summary>
        /// 解码 EBML Data Size。
        /// Data Size 的长度标记位不属于数值，因此需要清除。
        /// 若所有有效位均为 1，则表示未知大小。
        /// </summary>
        public static bool TryDecodeDataSize(
            ReadOnlySpan<byte> data,
            out ulong value,
            out int encodedLength,
            out bool isUnknown)
        {
            value = 0;
            encodedLength = 0;
            isUnknown = false;

            if (data.IsEmpty)
                return false;

            int length = GetVIntLength(data[0], MaxDataSizeLength);
            if (length == 0 || data.Length < length)
                return false;

            byte markerMask = (byte)(0x80 >> (length - 1));
            ulong decoded = (ulong)(data[0] & (markerMask - 1));

            for (int i = 1; i < length; i++)
                decoded = (decoded << 8) | data[i];

            int valueBitCount = length * 7;
            ulong unknownValue = (1UL << valueBitCount) - 1UL;

            value = decoded;
            encodedLength = length;
            isUnknown = decoded == unknownValue;
            return true;
        }

        /// <summary>
        /// 解码 EBML lacing 使用的有符号 VINT。
        /// 当前树解析框架不使用，供未来 Block/SimpleBlock 解析器调用。
        /// </summary>
        public static bool TryDecodeSignedVInt(
            ReadOnlySpan<byte> data,
            out long value,
            out int encodedLength)
        {
            value = 0;
            encodedLength = 0;

            if (!TryDecodeDataSize(data, out ulong unsignedValue, out int length, out bool unknown))
                return false;

            if (unknown)
                return false;

            int valueBits = length * 7;
            long bias = (1L << (valueBits - 1)) - 1L;

            value = checked((long)unsignedValue - bias);
            encodedLength = length;
            return true;
        }

        public static ulong ReadUnsignedInteger(ReadOnlySpan<byte> data)
        {
            if (data.Length is < 1 or > 8)
                throw new ArgumentOutOfRangeException(
                    nameof(data),
                    "An EBML unsigned integer must contain between 1 and 8 bytes.");

            ulong value = 0;
            foreach (byte current in data)
                value = (value << 8) | current;

            return value;
        }

        public static long ReadSignedInteger(ReadOnlySpan<byte> data)
        {
            if (data.Length is < 1 or > 8)
                throw new ArgumentOutOfRangeException(
                    nameof(data),
                    "An EBML signed integer must contain between 1 and 8 bytes.");

            ulong raw = ReadUnsignedInteger(data);
            int bits = data.Length * 8;

            if (bits == 64)
                return unchecked((long)raw);

            ulong signBit = 1UL << (bits - 1);
            if ((raw & signBit) == 0)
                return (long)raw;

            ulong signExtension = ulong.MaxValue << bits;
            return unchecked((long)(raw | signExtension));
        }

        public static float ReadFloat32(ReadOnlySpan<byte> data)
        {
            if (data.Length != 4)
                throw new ArgumentException("An EBML float32 must contain exactly 4 bytes.", nameof(data));

            int bits = BinaryPrimitives.ReadInt32BigEndian(data);
            return BitConverter.Int32BitsToSingle(bits);
        }

        public static double ReadFloat64(ReadOnlySpan<byte> data)
        {
            if (data.Length != 8)
                throw new ArgumentException("An EBML float64 must contain exactly 8 bytes.", nameof(data));

            long bits = BinaryPrimitives.ReadInt64BigEndian(data);
            return BitConverter.Int64BitsToDouble(bits);
        }

        public static string FormatElementId(ulong elementId, int encodedLength)
        {
            int digits = Math.Clamp(encodedLength, 1, MaxElementIdLength) * 2;
            return $"0x{elementId.ToString($"X{digits}")}";
        }

        public static string MakeTitle(
            string readableName,
            ulong elementId,
            int encodedLength) =>
            $"{readableName} '{FormatElementId(elementId, encodedLength)}'";

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0)
                return bytes.ToString();

            if (bytes < 1024)
                return $"{bytes} B";

            string[] units = ["KiB", "MiB", "GiB", "TiB", "PiB"];
            double value = bytes / 1024.0;
            int unit = 0;

            while (value >= 1024.0 && unit < units.Length - 1)
            {
                value /= 1024.0;
                unit++;
            }

            return $"{value:0.##} {units[unit]} ({bytes:N0} B)";
        }
    }
}