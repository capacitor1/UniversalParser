using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    internal static class CreatorChunk
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

            if (!parsed || properties.Count != 1)
            {
                dataLines.Add(
                    (
                        "<Error>",
                        "The Creator property could not be decoded."
                    ));
            }
            else if (properties[0].Value is string value)
            {
                dataLines.Add(("Creator", value));
            }
            else
            {
                dataLines.Add(
                    (
                        "<Error>",
                        "Creator does not contain a string property."
                    ));
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
                        "The Creator node is truncated."
                    ));
            }

            return new ParseResult
            {
                Title = FBXUtil.MakeTitle(
                    "Creator",
                    header.Name),

                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,

                RawData = parser.CreateRawStream(
                    header.NodeStart,
                    (long)node.Length),
            };
        }
    }
}