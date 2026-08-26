using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UniversalParser.Src.Parser.FBX
{
    /// <summary>
    /// Properties70 中 P 记录的读取与格式化。
    /// </summary>
    internal static class FBXProperty70Reader
    {
        /// <summary>P 记录固定头部槽位数量。</summary>
        public const int HeaderSlotCount = 4;

        /// <summary>FBX 内部时间单位，1 秒等于该值个时间刻。</summary>
        public const long TimeOneSecond = 46186158000L;

        private static readonly Dictionary<string, (FBXProperty70ValueKind Kind, int Count)> TypeTable =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Compound"] = (FBXProperty70ValueKind.None, 0),
                ["object"] = (FBXProperty70ValueKind.None, 0),
                ["Reference"] = (FBXProperty70ValueKind.None, 0),

                ["bool"] = (FBXProperty70ValueKind.Boolean, 1),
                ["Visibility Inheritance"] = (FBXProperty70ValueKind.Boolean, 1),

                ["int"] = (FBXProperty70ValueKind.Integer, 1),
                ["Integer"] = (FBXProperty70ValueKind.Integer, 1),
                ["enum"] = (FBXProperty70ValueKind.Integer, 1),
                ["short"] = (FBXProperty70ValueKind.Integer, 1),
                ["ushort"] = (FBXProperty70ValueKind.Integer, 1),
                ["uint"] = (FBXProperty70ValueKind.Integer, 1),
                ["char"] = (FBXProperty70ValueKind.Integer, 1),
                ["uchar"] = (FBXProperty70ValueKind.Integer, 1),
                ["ULongLong"] = (FBXProperty70ValueKind.Integer, 1),

                ["double"] = (FBXProperty70ValueKind.Number, 1),
                ["Number"] = (FBXProperty70ValueKind.Number, 1),
                ["Real"] = (FBXProperty70ValueKind.Number, 1),
                ["float"] = (FBXProperty70ValueKind.Number, 1),
                ["Intensity"] = (FBXProperty70ValueKind.Number, 1),
                ["Visibility"] = (FBXProperty70ValueKind.Number, 1),
                ["FieldOfView"] = (FBXProperty70ValueKind.Number, 1),
                ["FieldOfViewX"] = (FBXProperty70ValueKind.Number, 1),
                ["FieldOfViewY"] = (FBXProperty70ValueKind.Number, 1),
                ["Roll"] = (FBXProperty70ValueKind.Number, 1),
                ["OpticalCenterX"] = (FBXProperty70ValueKind.Number, 1),
                ["OpticalCenterY"] = (FBXProperty70ValueKind.Number, 1),
                ["HotSpot"] = (FBXProperty70ValueKind.Number, 1),
                ["ConeAngle"] = (FBXProperty70ValueKind.Number, 1),
                ["Fog"] = (FBXProperty70ValueKind.Number, 1),

                ["KTime"] = (FBXProperty70ValueKind.Time, 1),
                ["Time"] = (FBXProperty70ValueKind.Time, 1),

                ["KString"] = (FBXProperty70ValueKind.Text, 1),
                ["charptr"] = (FBXProperty70ValueKind.Text, 1),
                ["DateTime"] = (FBXProperty70ValueKind.Text, 1),
                ["Url"] = (FBXProperty70ValueKind.Text, 1),
                ["XRefUrl"] = (FBXProperty70ValueKind.Text, 1),

                ["Blob"] = (FBXProperty70ValueKind.Blob, 1),

                ["Vector2"] = (FBXProperty70ValueKind.Vector2, 2),
                ["Vector2D"] = (FBXProperty70ValueKind.Vector2, 2),

                ["Vector"] = (FBXProperty70ValueKind.Vector3, 3),
                ["Vector3"] = (FBXProperty70ValueKind.Vector3, 3),
                ["Vector3D"] = (FBXProperty70ValueKind.Vector3, 3),
                ["Translation"] = (FBXProperty70ValueKind.Vector3, 3),
                ["Rotation"] = (FBXProperty70ValueKind.Vector3, 3),
                ["Scaling"] = (FBXProperty70ValueKind.Vector3, 3),
                ["Lcl Translation"] = (FBXProperty70ValueKind.Vector3, 3),
                ["Lcl Rotation"] = (FBXProperty70ValueKind.Vector3, 3),
                ["Lcl Scaling"] = (FBXProperty70ValueKind.Vector3, 3),

                ["Vector4"] = (FBXProperty70ValueKind.Vector4, 4),
                ["Vector4D"] = (FBXProperty70ValueKind.Vector4, 4),
                ["Quaternion"] = (FBXProperty70ValueKind.Vector4, 4),

                ["ColorRGB"] = (FBXProperty70ValueKind.Color3, 3),
                ["Color"] = (FBXProperty70ValueKind.Color3, 3),

                ["ColorAndAlpha"] = (FBXProperty70ValueKind.Color4, 4),
                ["ColorRGBA"] = (FBXProperty70ValueKind.Color4, 4),

                ["Distance"] = (FBXProperty70ValueKind.Distance, 2),
            };

        /// <summary>
        /// 读取一条 P 记录。
        /// 头部槽位不足 4 个时视为结构非法。
        /// </summary>
        public static bool TryRead(
            FBXParser parser,
            in FBXNodeHeader header,
            out FBXProperty70 property)
        {
            ArgumentNullException.ThrowIfNull(parser);

            property = default;

            bool complete =
                FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out List<FBXPropertyValue> properties,
                    out long unparsedLength);

            if (properties.Count < HeaderSlotCount)
                return false;

            string propName =
                ToText(properties[0]);

            string propType =
                ToText(properties[1]);

            string label =
                ToText(properties[2]);

            string flags =
                ToText(properties[3]);

            var values = properties.GetRange(
                HeaderSlotCount,
                properties.Count - HeaderSlotCount);

            FBXProperty70ValueKind kind;
            int expectedCount;

            if (TypeTable.TryGetValue(
                    propType,
                    out (FBXProperty70ValueKind Kind, int Count) entry))
            {
                kind = entry.Kind;
                expectedCount = entry.Count;
            }
            else
            {
                kind = FBXProperty70ValueKind.Unknown;
                expectedCount = -1;
            }

            long rawByteLength = 0;

            foreach (FBXPropertyValue value in values)
            {
                if (value.IsRaw)
                {
                    rawByteLength += (long)Math.Min(
                        value.DataLength,
                        long.MaxValue);
                }
            }

            property = new FBXProperty70
            {
                PropName = propName,
                PropType = propType,
                Label = label,
                Flags = flags,
                Values = values,
                Kind = kind,
                ExpectedValueCount = expectedCount,
                RawByteLength = rawByteLength,
                UnparsedByteLength = unparsedLength,
            };

            return complete;
        }

        /// <summary>
        /// 把值段格式化为单行原始表示。
        /// </summary>
        public static string FormatValues(
            in FBXProperty70 property)
        {
            if (property.Values.Count == 0)
                return string.Empty;

            if (property.Values.Count == 1)
                return FormatScalar(property.Values[0]);

            var builder = new StringBuilder();

            for (int i = 0; i < property.Values.Count; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append(
                    FormatScalar(property.Values[i]));
            }

            return builder.ToString();
        }

        /// <summary>
        /// 生成派生的可读表示。返回 null 表示原始值已经足够可读。
        /// </summary>
        public static string? DescribeValues(
            in FBXProperty70 property)
        {
            switch (property.Kind)
            {
                case FBXProperty70ValueKind.None:
                    return property.Values.Count == 0
                        ? $"No value ({property.PropType})"
                        : null;

                case FBXProperty70ValueKind.Boolean:
                    if (property.Values.Count == 1 &&
                        TryGetInt64(
                            property.Values[0],
                            out long boolean))
                    {
                        return boolean != 0
                            ? "True"
                            : "False";
                    }

                    return null;

                case FBXProperty70ValueKind.Time:
                    if (property.Values.Count == 1 &&
                        TryGetInt64(
                            property.Values[0],
                            out long ticks))
                    {
                        return FormatTime(ticks);
                    }

                    return null;

                case FBXProperty70ValueKind.Blob:
                    if (property.Values.Count == 1 &&
                        property.Values[0].IsRaw)
                    {
                        return $"Binary blob, {property.Values[0].DataLength:N0} bytes";
                    }

                    return null;

                case FBXProperty70ValueKind.Color3:
                case FBXProperty70ValueKind.Color4:
                    return DescribeColor(property);

                case FBXProperty70ValueKind.Distance:
                    return DescribeDistance(property);

                case FBXProperty70ValueKind.Vector2:
                case FBXProperty70ValueKind.Vector3:
                case FBXProperty70ValueKind.Vector4:
                    return DescribeVector(property);

                default:
                    if (property.Values.Count == 1 &&
                        property.Values[0].IsArray)
                    {
                        return DescribeArray(
                            property.Values[0]);
                    }

                    return null;
            }
        }

        /// <summary>
        /// 解析属性标志串。
        /// </summary>
        public static string DescribeFlags(string flags)
        {
            if (string.IsNullOrEmpty(flags))
                return "None";

            var parts = new List<string>();
            int index = 0;

            while (index < flags.Length)
            {
                char flag = flags[index];

                switch (flag)
                {
                    case 'A':
                        if (index + 1 < flags.Length &&
                            flags[index + 1] == '+')
                        {
                            parts.Add("Animated");
                            index += 2;
                        }
                        else
                        {
                            parts.Add("Animatable");
                            index++;
                        }

                        break;

                    case 'U':
                        parts.Add("UserDefined");
                        index++;
                        break;

                    case 'H':
                        parts.Add("Hidden");
                        index++;
                        break;

                    case 'N':
                        parts.Add("NotSavable");
                        index++;
                        break;

                    case 'L':
                    case 'M':
                    {
                        index++;

                        int start = index;

                        while (index < flags.Length &&
                               char.IsAsciiDigit(flags[index]))
                        {
                            index++;
                        }

                        string mask = start < index
                            ? flags[start..index]
                            : "0";

                        parts.Add(
                            flag == 'L'
                                ? $"LockedMembers(0x{ParseMask(mask):X})"
                                : $"MutedMembers(0x{ParseMask(mask):X})");

                        break;
                    }

                    default:
                        parts.Add($"Unknown('{flag}')");
                        index++;
                        break;
                }
            }

            return string.Join(", ", parts);
        }

        public static string FormatScalar(
            FBXPropertyValue property)
        {
            if (property.IsArray)
                return $"{property.TypeCode}[]";

            if (property.IsRaw)
                return "Raw";

            return property.Value switch
            {
                null => string.Empty,

                bool boolean =>
                    boolean ? "1" : "0",

                string text =>
                    text,

                float single =>
                    single.ToString(
                        "R",
                        CultureInfo.InvariantCulture),

                double number =>
                    number.ToString(
                        "R",
                        CultureInfo.InvariantCulture),

                IFormattable formattable =>
                    formattable.ToString(
                        null,
                        CultureInfo.InvariantCulture) ?? string.Empty,

                _ =>
                    property.Value.ToString() ?? string.Empty,
            };
        }

        public static string FormatTime(long ticks)
        {
            double seconds =
                (double)ticks / TimeOneSecond;

            return $"{seconds.ToString("0.######", CultureInfo.InvariantCulture)} s";
        }

        private static string DescribeArray(
            FBXPropertyValue property)
        {
            string encoding =
                property.ArrayEncoding switch
                {
                    0 => "Uncompressed",
                    1 => "Zlib",
                    _ => $"Unknown encoding ({property.ArrayEncoding})",
                };

            return $"{property.DataLength:N0} elements, {encoding}";
        }

        private static string? DescribeColor(
            in FBXProperty70 property)
        {
            int expected =
                property.Kind == FBXProperty70ValueKind.Color4
                    ? 4
                    : 3;

            if (property.Values.Count < 3)
                return null;

            Span<double> channels = stackalloc double[4];
            int count = Math.Min(expected, property.Values.Count);

            for (int i = 0; i < count; i++)
            {
                if (!TryGetDouble(
                        property.Values[i],
                        out channels[i]))
                {
                    return null;
                }
            }

            int red = ToByteChannel(channels[0]);
            int green = ToByteChannel(channels[1]);
            int blue = ToByteChannel(channels[2]);

            if (expected == 4 && count == 4)
            {
                int alpha = ToByteChannel(channels[3]);

                return $"#{red:X2}{green:X2}{blue:X2}{alpha:X2} " +
                       $"(R {red}, G {green}, B {blue}, A {alpha})";
            }

            return $"#{red:X2}{green:X2}{blue:X2} " +
                   $"(R {red}, G {green}, B {blue})";
        }

        private static string? DescribeVector(
            in FBXProperty70 property)
        {
            string[] labels = property.Kind switch
            {
                FBXProperty70ValueKind.Vector2 => ["X", "Y"],
                FBXProperty70ValueKind.Vector3 => ["X", "Y", "Z"],
                _ => ["X", "Y", "Z", "W"],
            };

            if (property.Values.Count != labels.Length)
                return null;

            var builder = new StringBuilder();

            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append(labels[i]);
                builder.Append(' ');
                builder.Append(
                    FormatScalar(property.Values[i]));
            }

            return builder.ToString();
        }

        private static string? DescribeDistance(
            in FBXProperty70 property)
        {
            if (property.Values.Count != 2)
                return null;

            string value =
                FormatScalar(property.Values[0]);

            string unit =
                FormatScalar(property.Values[1]);

            return string.IsNullOrEmpty(unit)
                ? value
                : $"{value} {unit}";
        }

        private static int ToByteChannel(double value)
        {
            double scaled =
                Math.Round(value * 255.0);

            return (int)Math.Clamp(
                scaled,
                0.0,
                255.0);
        }

        private static ulong ParseMask(string mask)
        {
            return ulong.TryParse(
                mask,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ulong value)
                ? value
                : 0UL;
        }

        private static string ToText(
            FBXPropertyValue property)
        {
            return property.Value is string text
                ? text
                : FormatScalar(property);
        }

        private static bool TryGetInt64(
            FBXPropertyValue property,
            out long value)
        {
            switch (property.Value)
            {
                case bool boolean:
                    value = boolean ? 1L : 0L;
                    return true;

                case short int16:
                    value = int16;
                    return true;

                case int int32:
                    value = int32;
                    return true;

                case long int64:
                    value = int64;
                    return true;

                default:
                    value = 0;
                    return false;
            }
        }

        private static bool TryGetDouble(
            FBXPropertyValue property,
            out double value)
        {
            switch (property.Value)
            {
                case double number:
                    value = number;
                    return true;

                case float single:
                    value = single;
                    return true;

                case int int32:
                    value = int32;
                    return true;

                case long int64:
                    value = int64;
                    return true;

                case short int16:
                    value = int16;
                    return true;

                default:
                    value = 0;
                    return false;
            }
        }
    }
}