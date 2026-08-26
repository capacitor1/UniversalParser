using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    /// <summary>
    /// 通用 FBX 属性节点解析器。
    ///
    /// 该解析器只负责当前节点自身，不解析子节点。
    /// 复杂节点应由专用解析器单独实现。
    /// </summary>
    internal static class FBXPropertyChunk
    {
        public static ParseResult Parse(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            bool parsed =
                FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out List<FBXPropertyValue> properties,
                    out long unparsedLength);

            if (!parsed)
            {
                dataLines.Add(
                    (
                        "<Error>",
                        "The FBX property list could not be decoded."
                    ));
            }

            for (int i = 0; i < properties.Count; i++)
            {
                FBXPropertyValue property =
                    properties[i];

                string propertyName =
                    properties.Count == 1
                        ? "Value"
                        : $"Value{i}";

                AddProperty(
                    property,
                    propertyName,
                    dataLines);
            }

            if (unparsedLength > 0)
            {
                dataLines.Add(
                    (
                        "<PayloadLength>",
                        FBXUtil.FormatBytes(unparsedLength)
                    ));
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The FBX node is truncated or its end offset is invalid."
                    ));
            }

            return new ParseResult
            {
                Title = FBXUtil.MakeTitle(
                    "Property",
                    header.Name),

                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,

                RawData = parser.CreateRawStream(
                    header.NodeStart,
                    (long)node.Length),
            };
        }

        private static void AddProperty(
            FBXPropertyValue property,
            string propertyName,
            List<(string K, string V)> dataLines)
        {
            if (property.IsArray)
            {
                dataLines.Add(
                    (
                        propertyName,
                        $"{property.TypeCode}[]"
                    ));

                dataLines.Add(
                    (
                        $"<{propertyName}>",
                        FormatArray(
                            property.DataLength,
                            property.ArrayEncoding)
                    ));

                return;
            }

            if (property.IsRaw)
            {
                dataLines.Add(
                    (
                        propertyName,
                        "Raw"
                    ));

                dataLines.Add(
                    (
                        $"<{propertyName}>",
                        $"{property.DataLength:N0} bytes"
                    ));

                return;
            }

            string rawValue =
                FormatRawValue(
                    property.Value);

            dataLines.Add(
                (
                    propertyName,
                    rawValue
                ));

            string? readableValue =
                FormatReadableValue(
                    property);

            if (!string.IsNullOrEmpty(readableValue))
            {
                dataLines.Add(
                    (
                        $"<{propertyName}>",
                        readableValue
                    ));
            }
        }

        private static string FormatRawValue(
            object? value)
        {
            return value switch
            {
                null => string.Empty,

                bool boolean =>
                    boolean ? "1" : "0",

                string text =>
                    text,

                char character =>
                    character.ToString(),

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
                    value.ToString() ?? string.Empty,
            };
        }

        private static string? FormatReadableValue(
            FBXPropertyValue property)
        {
            return property.TypeCode switch
            {
                'C' when property.Value is bool boolean =>
                    boolean ? "True" : "False",

                'Y' when property.Value is short int16 =>
                    int16.ToString(
                        CultureInfo.InvariantCulture),

                'I' when property.Value is int int32 =>
                    int32.ToString(
                        CultureInfo.InvariantCulture),

                'L' when property.Value is long int64 =>
                    int64.ToString(
                        CultureInfo.InvariantCulture),

                'F' when property.Value is float single =>
                    single.ToString(
                        "G9",
                        CultureInfo.InvariantCulture),

                'D' when property.Value is double number =>
                    number.ToString(
                        "G17",
                        CultureInfo.InvariantCulture),

                _ => null,
            };
        }

        private static string FormatArray(
            ulong elementCount,
            uint encoding)
        {
            string encodingName =
                encoding switch
                {
                    0 => "Uncompressed",
                    1 => "Zlib",
                    _ => $"Unknown encoding ({encoding})",
                };

            return $"{elementCount:N0} elements, {encodingName}";
        }
    }
}