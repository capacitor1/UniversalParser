using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalParser.Src.Parser.FBX
{
    /// <summary>
    /// FBX 单值节点取值辅助类。
    ///
    /// FBX 中存在大量形如：
    ///
    /// Version: 100
    /// Count: 42
    /// ObjectType: "Model"
    ///
    /// 这类“一个节点只承载一个标量”的结构。
    /// 该类统一提供取值逻辑，避免各 Chunk 解析器重复实现。
    ///
    /// 所有方法都要求属性区被完整消费。
    /// 若节点属性数量不符或存在尾部未解析字节，一律返回 false，
    /// 由调用方决定是否提示告警。
    /// </summary>
    internal static class FBXNodeValueReader
    {
        // ============================================================
        // 基础：读取属性列表
        // ============================================================

        /// <summary>
        /// 读取节点的完整属性列表。
        /// 仅当属性区被完整解析且无剩余字节时返回 true。
        /// </summary>
        public static bool TryReadAll(
            FBXParser parser,
            in FBXNodeHeader header,
            out List<FBXPropertyValue> properties)
        {
            ArgumentNullException.ThrowIfNull(parser);

            bool complete =
                FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out properties,
                    out long remaining);

            return complete && remaining == 0;
        }

        /// <summary>
        /// 读取节点属性列表，同时返回尾部未解析字节数。
        /// </summary>
        public static bool TryReadAll(
            FBXParser parser,
            in FBXNodeHeader header,
            out List<FBXPropertyValue> properties,
            out long unparsedLength)
        {
            ArgumentNullException.ThrowIfNull(parser);

            return FBXPropertyReader.TryReadProperties(
                parser,
                header,
                out properties,
                out unparsedLength);
        }

        /// <summary>
        /// 读取恰好包含指定数量属性的节点。
        /// </summary>
        public static bool TryReadExactly(
            FBXParser parser,
            in FBXNodeHeader header,
            int expectedCount,
            out List<FBXPropertyValue> properties)
        {
            properties = [];

            if (expectedCount < 0)
                return false;

            if (!TryReadAll(
                    parser,
                    header,
                    out List<FBXPropertyValue> all))
            {
                return false;
            }

            if (all.Count != expectedCount)
                return false;

            properties = all;
            return true;
        }

        /// <summary>
        /// 读取只包含一个属性的节点。
        /// </summary>
        public static bool TryReadSingle(
            FBXParser parser,
            in FBXNodeHeader header,
            out FBXPropertyValue property)
        {
            property = default;

            if (!TryReadExactly(
                    parser,
                    header,
                    1,
                    out List<FBXPropertyValue> properties))
            {
                return false;
            }

            property = properties[0];
            return true;
        }

        // ============================================================
        // 单值：整数
        // ============================================================

        public static bool TryGetSingleInt32(
            FBXParser parser,
            in FBXNodeHeader header,
            out int value)
        {
            value = 0;

            if (!TryReadSingle(
                    parser,
                    header,
                    out FBXPropertyValue property))
            {
                return false;
            }

            return TryConvertInt32(
                property,
                out value);
        }

        public static bool TryGetSingleInt64(
            FBXParser parser,
            in FBXNodeHeader header,
            out long value)
        {
            value = 0;

            if (!TryReadSingle(
                    parser,
                    header,
                    out FBXPropertyValue property))
            {
                return false;
            }

            return TryConvertInt64(
                property,
                out value);
        }

        // ============================================================
        // 单值：浮点
        // ============================================================

        public static bool TryGetSingleDouble(
            FBXParser parser,
            in FBXNodeHeader header,
            out double value)
        {
            value = 0.0;

            if (!TryReadSingle(
                    parser,
                    header,
                    out FBXPropertyValue property))
            {
                return false;
            }

            return TryConvertDouble(
                property,
                out value);
        }

        public static bool TryGetSingleSingle(
            FBXParser parser,
            in FBXNodeHeader header,
            out float value)
        {
            value = 0.0f;

            if (!TryGetSingleDouble(
                    parser,
                    header,
                    out double number))
            {
                return false;
            }

            value = (float)number;
            return true;
        }

        // ============================================================
        // 单值：布尔
        // ============================================================

        public static bool TryGetSingleBoolean(
            FBXParser parser,
            in FBXNodeHeader header,
            out bool value)
        {
            value = false;

            if (!TryReadSingle(
                    parser,
                    header,
                    out FBXPropertyValue property))
            {
                return false;
            }

            if (property.Value is bool boolean)
            {
                value = boolean;
                return true;
            }

            if (TryConvertInt64(
                    property,
                    out long number))
            {
                value = number != 0;
                return true;
            }

            return false;
        }

        // ============================================================
        // 单值：字符串
        // ============================================================

        public static bool TryGetSingleString(
            FBXParser parser,
            in FBXNodeHeader header,
            out string value)
        {
            value = string.Empty;

            if (!TryReadSingle(
                    parser,
                    header,
                    out FBXPropertyValue property))
            {
                return false;
            }

            if (property.Value is not string text)
                return false;

            value = text;
            return true;
        }

        // ============================================================
        // 多值：按索引取值
        // ============================================================

        public static bool TryGetStringAt(
            List<FBXPropertyValue> properties,
            int index,
            out string value)
        {
            value = string.Empty;

            if (properties is null ||
                index < 0 ||
                index >= properties.Count)
            {
                return false;
            }

            if (properties[index].Value is not string text)
                return false;

            value = text;
            return true;
        }

        public static bool TryGetInt32At(
            List<FBXPropertyValue> properties,
            int index,
            out int value)
        {
            value = 0;

            if (properties is null ||
                index < 0 ||
                index >= properties.Count)
            {
                return false;
            }

            return TryConvertInt32(
                properties[index],
                out value);
        }

        public static bool TryGetInt64At(
            List<FBXPropertyValue> properties,
            int index,
            out long value)
        {
            value = 0;

            if (properties is null ||
                index < 0 ||
                index >= properties.Count)
            {
                return false;
            }

            return TryConvertInt64(
                properties[index],
                out value);
        }

        public static bool TryGetDoubleAt(
            List<FBXPropertyValue> properties,
            int index,
            out double value)
        {
            value = 0.0;

            if (properties is null ||
                index < 0 ||
                index >= properties.Count)
            {
                return false;
            }

            return TryConvertDouble(
                properties[index],
                out value);
        }

        // ============================================================
        // 子节点查找
        // ============================================================

        /// <summary>
        /// 在直接子节点中查找指定名称的第一个节点。
        /// </summary>
        public static bool TryFindChild(
            FBXParser parser,
            Node parent,
            string nodeName,
            out Node child,
            out FBXNodeHeader childHeader)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(parent);

            child = null!;
            childHeader = default;

            if (string.IsNullOrEmpty(nodeName))
                return false;

            foreach (Node candidate in parent.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        candidate,
                        out FBXNodeHeader header))
                {
                    continue;
                }

                if (!string.Equals(
                        header.Name,
                        nodeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                child = candidate;
                childHeader = header;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 从指定名称的子节点中读取单个 Int32。
        /// </summary>
        public static bool TryGetChildInt32(
            FBXParser parser,
            Node parent,
            string nodeName,
            out int value)
        {
            value = 0;

            if (!TryFindChild(
                    parser,
                    parent,
                    nodeName,
                    out _,
                    out FBXNodeHeader header))
            {
                return false;
            }

            return TryGetSingleInt32(
                parser,
                header,
                out value);
        }

        /// <summary>
        /// 从指定名称的子节点中读取单个 Int64。
        /// </summary>
        public static bool TryGetChildInt64(
            FBXParser parser,
            Node parent,
            string nodeName,
            out long value)
        {
            value = 0;

            if (!TryFindChild(
                    parser,
                    parent,
                    nodeName,
                    out _,
                    out FBXNodeHeader header))
            {
                return false;
            }

            return TryGetSingleInt64(
                parser,
                header,
                out value);
        }

        /// <summary>
        /// 从指定名称的子节点中读取单个字符串。
        /// </summary>
        public static bool TryGetChildString(
            FBXParser parser,
            Node parent,
            string nodeName,
            out string value)
        {
            value = string.Empty;

            if (!TryFindChild(
                    parser,
                    parent,
                    nodeName,
                    out _,
                    out FBXNodeHeader header))
            {
                return false;
            }

            return TryGetSingleString(
                parser,
                header,
                out value);
        }

        /// <summary>
        /// 统计直接子节点中指定名称的节点数量。
        /// </summary>
        public static int CountChildren(
            FBXParser parser,
            Node parent,
            string nodeName)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(parent);

            if (string.IsNullOrEmpty(nodeName))
                return 0;

            int count = 0;

            foreach (Node candidate in parent.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        candidate,
                        out FBXNodeHeader header))
                {
                    continue;
                }

                if (string.Equals(
                        header.Name,
                        nodeName,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        // ============================================================
        // 未解析长度
        // ============================================================

        /// <summary>
        /// 计算节点自身属性区中未被解析的字节数。
        /// 属性数量为零时直接返回零。
        /// </summary>
        public static long GetUnparsedPropertyLength(
            FBXParser parser,
            in FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);

            if (header.PropertyCount == 0)
                return 0;

            FBXPropertyReader.TryReadProperties(
                parser,
                header,
                out _,
                out long unparsedLength);

            return unparsedLength;
        }

        // ============================================================
        // 格式化
        // ============================================================

        public static string FormatInt32(int value)
        {
            return value.ToString(
                CultureInfo.InvariantCulture);
        }

        public static string FormatInt64(long value)
        {
            return value.ToString(
                CultureInfo.InvariantCulture);
        }

        public static string FormatDouble(double value)
        {
            return value.ToString(
                "R",
                CultureInfo.InvariantCulture);
        }

        public static string FormatBoolean(bool value)
        {
            return value ? "1" : "0";
        }

        public static string DescribeBoolean(bool value)
        {
            return value ? "True" : "False";
        }

        // ============================================================
        // 类型转换
        // ============================================================

        public static bool TryConvertInt32(
            FBXPropertyValue property,
            out int value)
        {
            value = 0;

            switch (property.Value)
            {
                case int int32:
                    value = int32;
                    return true;

                case short int16:
                    value = int16;
                    return true;

                case bool boolean:
                    value = boolean ? 1 : 0;
                    return true;

                case long int64
                    when int64 >= int.MinValue &&
                         int64 <= int.MaxValue:
                    value = (int)int64;
                    return true;

                default:
                    return false;
            }
        }

        public static bool TryConvertInt64(
            FBXPropertyValue property,
            out long value)
        {
            value = 0;

            switch (property.Value)
            {
                case long int64:
                    value = int64;
                    return true;

                case int int32:
                    value = int32;
                    return true;

                case short int16:
                    value = int16;
                    return true;

                case bool boolean:
                    value = boolean ? 1L : 0L;
                    return true;

                default:
                    return false;
            }
        }

        public static bool TryConvertDouble(
            FBXPropertyValue property,
            out double value)
        {
            value = 0.0;

            switch (property.Value)
            {
                case double number:
                    value = number;
                    return true;

                case float single:
                    value = single;
                    return true;

                case long int64:
                    value = int64;
                    return true;

                case int int32:
                    value = int32;
                    return true;

                case short int16:
                    value = int16;
                    return true;

                default:
                    return false;
            }
        }
        /// <summary>
        /// 格式化单个标量属性为原始字符串表示。
        /// </summary>
        public static string FormatScalar(
            FBXPropertyValue property)
        {
            return FBXProperty70Reader.FormatScalar(property);
        }
    }
}