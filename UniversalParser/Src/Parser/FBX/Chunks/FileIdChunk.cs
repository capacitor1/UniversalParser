using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.FBX.Chunks
{
    internal static class FileIdChunk
    {
        private const int FileIdLength = 16;

        public static ParseResult Parse(
            FBXParser parser,
            Node node,
            FBXNodeHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            byte[] buffer = new byte[FileIdLength];

            int read = parser.ReadAt(
                header.PropertyOffset,
                buffer);

            if (read < FileIdLength)
            {
                if (read > 0)
                {
                    string partial = Convert.ToHexString(
                        buffer,
                        0,
                        read);

                    dataLines.Add(("FileId", partial));
                }

                dataLines.Add(
                    (
                        "<Error>",
                        $"FileId requires {FileIdLength} bytes, but only {read} bytes are available."
                    ));

                long unparsedLength =
                    Math.Max(0, header.ActualPropertyLength - read);

                if (unparsedLength > 0)
                {
                    dataLines.Add(
                        (
                            "<PayloadLength>",
                            FBXUtil.FormatBytes(unparsedLength)
                        ));
                }
            }
            else
            {
                string fileId =
                    Convert.ToHexString(buffer);

                dataLines.Add(("FileId", fileId));
                dataLines.Add(
                    (
                        "<FileId>",
                        "16-byte binary file identifier"
                    ));

                long unparsedLength =
                    Math.Max(0, header.ActualPropertyLength - FileIdLength);

                if (unparsedLength > 0)
                {
                    dataLines.Add(
                        (
                            "<PayloadLength>",
                            FBXUtil.FormatBytes(unparsedLength)
                        ));
                }
            }

            if (header.IsTruncated)
            {
                dataLines.Add(
                    (
                        "<Warning>",
                        "The FileId node is truncated."
                    ));
            }

            return new ParseResult
            {
                Title = FBXUtil.MakeTitle(
                    "FileId",
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