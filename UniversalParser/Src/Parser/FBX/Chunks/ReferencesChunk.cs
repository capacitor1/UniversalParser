using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    internal static class ReferencesChunk
    {
        public static ParseResult Parse(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            long unparsedLength =
                header.ActualPropertyLength;

            if (header.PropertyCount > 0)
            {
                FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out _,
                    out unparsedLength);
            }
            else
            {
                unparsedLength = 0;
            }

            int referenceCount = 0;

            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetNodeHeader(
                        child,
                        out FBXNodeHeader childHeader))
                {
                    continue;
                }

                if (!string.Equals(
                        childHeader.Name,
                        "C",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                referenceCount++;

                AddReferenceSummary(
                    parser,
                    childHeader,
                    referenceCount,
                    dataLines);
            }

            dataLines.Add(
                (
                    "<ReferenceCount>",
                    referenceCount.ToString(
                        CultureInfo.InvariantCulture)
                ));

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
                        "The References node is truncated."
                    ));
            }

            return new ParseResult
            {
                Title = FBXUtil.MakeTitle(
                    "References",
                    header.Name),

                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,

                RawData = parser.CreateRawStream(
                    header.NodeStart,
                    (long)node.Length),
            };
        }

        private static void AddReferenceSummary(
            FBXParser parser,
            in FBXNodeHeader header,
            int index,
            List<(string K, string V)> dataLines)
        {
            if (!FBXPropertyReader.TryReadProperties(
                    parser,
                    header,
                    out List<FBXPropertyValue> properties,
                    out _))
            {
                dataLines.Add(
                    (
                        $"<Reference{index}>",
                        "The reference record could not be decoded."
                    ));

                return;
            }

            string summary =
                FormatReferenceProperties(properties);

            dataLines.Add(
                (
                    $"<Reference{index}>",
                    summary
                ));
        }

        private static string FormatReferenceProperties(
            List<FBXPropertyValue> properties)
        {
            if (properties.Count == 0)
                return "(empty)";

            var values = new List<string>(
                properties.Count);

            foreach (FBXPropertyValue property in properties)
            {
                if (property.IsArray)
                {
                    values.Add(
                        $"{property.TypeCode}[" +
                        $"{property.DataLength.ToString(
                            CultureInfo.InvariantCulture)}]");
                }
                else if (property.IsRaw)
                {
                    values.Add(
                        $"R[{property.DataLength.ToString(
                            CultureInfo.InvariantCulture)} bytes]");
                }
                else
                {
                    values.Add(
                        property.ToInvariantString());
                }
            }

            return string.Join(
                ", ",
                values);
        }
    }
}