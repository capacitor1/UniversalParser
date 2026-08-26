using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UniversalParser.Src.Parser.FBX
{
    /// <summary>
    /// 一个 FBX 二进制属性。
    /// </summary>
    internal readonly struct FBXPropertyValue
    {
        /// <summary>FBX 属性类型码，例如 I、L、S。</summary>
        public required char TypeCode { get; init; }

        /// <summary>已经解码的属性值。数组和 Raw 属性不在这里展开。</summary>
        public object? Value { get; init; }

        /// <summary>属性在文件中的总长度，包括类型码和长度字段。</summary>
        public int EncodedLength { get; init; }

        /// <summary>数组或 Raw 属性的元素/字节数量。</summary>
        public ulong DataLength { get; init; }

        /// <summary>数组编码方式：0 为未压缩，1 为 zlib。</summary>
        public uint ArrayEncoding { get; init; }

        public bool IsArray =>
            TypeCode is 'f' or 'd' or 'l' or 'i' or 'b' or 'c';

        public bool IsRaw =>
            TypeCode == 'R';

        public string ToInvariantString()
        {
            return Value switch
            {
                null => string.Empty,
                bool boolean => boolean ? "1" : "0",
                float single => single.ToString("R", CultureInfo.InvariantCulture),
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => Value.ToString() ?? string.Empty,
            };
        }
    }

    /// <summary>
    /// FBX 二进制属性列表读取器。
    ///
    /// 标量类型：
    /// Y = Int16
    /// C = Boolean
    /// I = Int32
    /// F = Single
    /// D = Double
    /// L = Int64
    /// S = UTF-8 string
    /// R = Raw binary
    ///
    /// 数组类型：
    /// f = Single[]
    /// d = Double[]
    /// l = Int64[]
    /// i = Int32[]
    /// b = Boolean[]
    /// c = Byte[]
    /// </summary>
    internal static class FBXPropertyReader
    {
        /// <summary>
        /// HeaderExtension 中的属性通常很小。该限制用于避免损坏文件制造巨型分配。
        /// </summary>
        private const int MaxPropertyListBytes = 16 * 1024 * 1024;

        public static bool TryReadProperties(
            FBXParser parser,
            in FBXNodeHeader header,
            out List<FBXPropertyValue> properties,
            out long unparsedLength)
        {
            ArgumentNullException.ThrowIfNull(parser);

            properties = [];
            unparsedLength = header.ActualPropertyLength;

            if (header.ActualPropertyLength == 0)
            {
                unparsedLength = 0;
                return header.PropertyCount == 0;
            }

            if (header.ActualPropertyLength < 0 ||
                header.ActualPropertyLength > MaxPropertyListBytes ||
                header.ActualPropertyLength > int.MaxValue)
            {
                return false;
            }

            int propertyBytes = (int)header.ActualPropertyLength;
            byte[] buffer = new byte[propertyBytes];

            int read = parser.ReadAt(
                header.PropertyOffset,
                buffer);

            if (read != propertyBytes)
            {
                unparsedLength = Math.Max(0, propertyBytes - read);
                return false;
            }

            int offset = 0;
            ulong propertyIndex = 0;

            while (propertyIndex < header.PropertyCount)
            {
                int propertyStart = offset;

                if (!TryReadProperty(
                        buffer,
                        ref offset,
                        out FBXPropertyValue property))
                {
                    unparsedLength = Math.Max(0, propertyBytes - propertyStart);
                    return false;
                }

                properties.Add(property);
                propertyIndex++;
            }

            unparsedLength = Math.Max(0, propertyBytes - offset);
            return unparsedLength == 0;
        }

        private static bool TryReadProperty(
            ReadOnlySpan<byte> data,
            ref int offset,
            out FBXPropertyValue property)
        {
            property = default;

            int propertyStart = offset;

            if (!TryReadByte(data, ref offset, out byte typeByte))
                return false;

            char typeCode = (char)typeByte;

            switch (typeCode)
            {
                case 'Y':
                {
                    if (!TryReadInt16(data, ref offset, out short value))
                        return false;

                    property = new FBXPropertyValue
                    {
                        TypeCode = typeCode,
                        Value = value,
                        EncodedLength = offset - propertyStart,
                    };

                    return true;
                }

                case 'C':
                {
                    if (!TryReadByte(data, ref offset, out byte value))
                        return false;

                    property = new FBXPropertyValue
                    {
                        TypeCode = typeCode,
                        Value = value != 0,
                        EncodedLength = offset - propertyStart,
                    };

                    return true;
                }

                case 'I':
                {
                    if (!TryReadInt32(data, ref offset, out int value))
                        return false;

                    property = new FBXPropertyValue
                    {
                        TypeCode = typeCode,
                        Value = value,
                        EncodedLength = offset - propertyStart,
                    };

                    return true;
                }

                case 'F':
                {
                    if (!TryReadInt32(data, ref offset, out int bits))
                        return false;

                    property = new FBXPropertyValue
                    {
                        TypeCode = typeCode,
                        Value = BitConverter.Int32BitsToSingle(bits),
                        EncodedLength = offset - propertyStart,
                    };

                    return true;
                }

                case 'D':
                {
                    if (!TryReadInt64(data, ref offset, out long bits))
                        return false;

                    property = new FBXPropertyValue
                    {
                        TypeCode = typeCode,
                        Value = BitConverter.Int64BitsToDouble(bits),
                        EncodedLength = offset - propertyStart,
                    };

                    return true;
                }

                case 'L':
                {
                    if (!TryReadInt64(data, ref offset, out long value))
                        return false;

                    property = new FBXPropertyValue
                    {
                        TypeCode = typeCode,
                        Value = value,
                        EncodedLength = offset - propertyStart,
                    };

                    return true;
                }

                case 'S':
                {
                    if (!TryReadUInt32(data, ref offset, out uint byteLength))
                        return false;

                    if (!TryReadBlock(
                            data,
                            ref offset,
                            byteLength,
                            out ReadOnlySpan<byte> textData))
                    {
                        return false;
                    }

                    string value;

                    try
                    {
                        value = Encoding.UTF8.GetString(textData);
                    }
                    catch
                    {
                        value = Encoding.Latin1.GetString(textData);
                    }

                    property = new FBXPropertyValue
                    {
                        TypeCode = typeCode,
                        Value = value,
                        DataLength = byteLength,
                        EncodedLength = offset - propertyStart,
                    };

                    return true;
                }

                case 'R':
                {
                    if (!TryReadUInt32(data, ref offset, out uint byteLength))
                        return false;

                    if (!TrySkip(data, ref offset, byteLength))
                        return false;

                    property = new FBXPropertyValue
                    {
                        TypeCode = typeCode,
                        Value = null,
                        DataLength = byteLength,
                        EncodedLength = offset - propertyStart,
                    };

                    return true;
                }

                case 'f':
                case 'd':
                case 'l':
                case 'i':
                case 'b':
                case 'c':
                {
                    if (!TryReadUInt32(data, ref offset, out uint arrayLength) ||
                        !TryReadUInt32(data, ref offset, out uint encoding) ||
                        !TryReadUInt32(data, ref offset, out uint encodedLength))
                    {
                        return false;
                    }

                    if (!TrySkip(data, ref offset, encodedLength))
                        return false;

                    property = new FBXPropertyValue
                    {
                        TypeCode = typeCode,
                        Value = null,
                        DataLength = arrayLength,
                        ArrayEncoding = encoding,
                        EncodedLength = offset - propertyStart,
                    };

                    return true;
                }

                default:
                    return false;
            }
        }

        private static bool TryReadByte(
            ReadOnlySpan<byte> data,
            ref int offset,
            out byte value)
        {
            value = 0;

            if ((uint)offset >= (uint)data.Length)
                return false;

            value = data[offset];
            offset++;
            return true;
        }

        private static bool TryReadInt16(
            ReadOnlySpan<byte> data,
            ref int offset,
            out short value)
        {
            value = 0;

            if (offset < 0 || offset > data.Length - 2)
                return false;

            value = BinaryPrimitives.ReadInt16LittleEndian(
                data.Slice(offset, 2));

            offset += 2;
            return true;
        }

        private static bool TryReadInt32(
            ReadOnlySpan<byte> data,
            ref int offset,
            out int value)
        {
            value = 0;

            if (offset < 0 || offset > data.Length - 4)
                return false;

            value = BinaryPrimitives.ReadInt32LittleEndian(
                data.Slice(offset, 4));

            offset += 4;
            return true;
        }

        private static bool TryReadUInt32(
            ReadOnlySpan<byte> data,
            ref int offset,
            out uint value)
        {
            value = 0;

            if (offset < 0 || offset > data.Length - 4)
                return false;

            value = BinaryPrimitives.ReadUInt32LittleEndian(
                data.Slice(offset, 4));

            offset += 4;
            return true;
        }

        private static bool TryReadInt64(
            ReadOnlySpan<byte> data,
            ref int offset,
            out long value)
        {
            value = 0;

            if (offset < 0 || offset > data.Length - 8)
                return false;

            value = BinaryPrimitives.ReadInt64LittleEndian(
                data.Slice(offset, 8));

            offset += 8;
            return true;
        }

        private static bool TryReadBlock(
            ReadOnlySpan<byte> data,
            ref int offset,
            uint length,
            out ReadOnlySpan<byte> block)
        {
            block = default;

            if (length > int.MaxValue)
                return false;

            int count = (int)length;

            if (offset < 0 ||
                count < 0 ||
                offset > data.Length - count)
            {
                return false;
            }

            block = data.Slice(offset, count);
            offset += count;
            return true;
        }

        private static bool TrySkip(
            ReadOnlySpan<byte> data,
            ref int offset,
            uint length)
        {
            if (length > int.MaxValue)
                return false;

            int count = (int)length;

            if (offset < 0 ||
                count < 0 ||
                offset > data.Length - count)
            {
                return false;
            }

            offset += count;
            return true;
        }

        /// <summary>
        /// 数组类型码。
        /// </summary>
        public static bool IsArrayType(char typeCode) =>
            typeCode is 'f' or 'd' or 'l' or 'i' or 'b' or 'c';

        /// <summary>
        /// 轻量探测节点的第一个属性，不读取数组 / 原始数据本体。
        /// 用于区分数据载荷节点（数组 / Raw）与元数据节点（标量）。
        ///
        /// 只读取：
        /// 标量：1 字节类型码；
        /// S / R：1 + 4 字节长度；
        /// 数组：1 + 12 字节（数组长度、编码方式、编码后长度）。
        /// </summary>
        public static bool TryProbeFirstProperty(
            FBXParser parser,
            in FBXNodeHeader header,
            out char typeCode,
            out ulong dataLength,
            out uint arrayEncoding)
        {
            typeCode = '\0';
            dataLength = 0;
            arrayEncoding = 0;

            ArgumentNullException.ThrowIfNull(parser);

            if (header.PropertyCount == 0 ||
                header.ActualPropertyLength <= 0)
            {
                return false;
            }

            Span<byte> buffer = stackalloc byte[13];

            int read = parser.ReadAt(
                header.PropertyOffset,
                buffer);

            if (read < 1)
                return false;

            typeCode = (char)buffer[0];

            if (typeCode is 'S' or 'R' or 'f' or 'd' or 'l' or 'i' or 'b' or 'c')
            {
                if (read < 5)
                    return false;

                dataLength = BinaryPrimitives.ReadUInt32LittleEndian(
                    buffer.Slice(1, 4));

                if (IsArrayType(typeCode))
                {
                    if (read < 13)
                        return false;

                    arrayEncoding = BinaryPrimitives.ReadUInt32LittleEndian(
                        buffer.Slice(5, 4));
                }
            }

            return true;
        }
    }
}