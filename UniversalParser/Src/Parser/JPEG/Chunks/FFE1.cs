using System;
using System.Collections.Generic;
using UniversalParser.Src.Parser.EXIF;

namespace UniversalParser.Src.Parser.JPEG.Chunks
{
    internal class FFE1
    {
        public static ParseResult Parse(JPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            // JPEG APP1 length includes the 2-byte length field itself
            ulong payloadLength = node.Length > 2 ? node.Length - 2 : 0;

            dataLines.Add(("<PayloadLength>", payloadLength.ToString()));

            // 读取 APP1 原始数据
            byte[] raw = ReadNodeBytes(parser, node);

            if (raw == null || raw.Length < 8)
            {
                dataLines.Add(("<EXIF>", "Invalid APP1 segment"));
                return BuildResult(parser, node, dataLines);
            }

            // APP1 EXIF 标识: "Exif\0\0"
            bool isExif = raw[0] == 0x45 && raw[1] == 0x78 &&
                          raw[2] == 0x69 && raw[3] == 0x66 &&
                          raw[4] == 0x00 && raw[5] == 0x00;

            if (!isExif)
            {
                dataLines.Add(("<Format>", "Unknown APP1 (not EXIF)"));
                return BuildResult(parser, node, dataLines);
            }

            dataLines.Add(("<Format>", "EXIF"));

            // TIFF 数据从 EXIF header 后开始（6 bytes: "Exif\0\0"）
            int tiffStart = 6;
            if (raw.Length <= tiffStart)
            {
                dataLines.Add(("<EXIF>", "Missing TIFF data"));
                return BuildResult(parser, node, dataLines);
            }

            byte[] tiffData = new byte[raw.Length - tiffStart];
            Buffer.BlockCopy(raw, tiffStart, tiffData, 0, tiffData.Length);

            try
            {
                // 调用你已有的 TIFF EXIF 解析器
                var exifResult = ExifParser.Parse(tiffData);
                foreach (var i in exifResult)
                {
                    dataLines.Add((string.Empty, $"{i.Key}  =  {i.Value}"));
                }

                return new ParseResult
                {
                    Title = $"ApplicationMarker(APP1) 'FFE1'",
                    Position = node.Position,
                    Length = node.Length,
                    DataLines = dataLines,
                    RawData = new OffsetStream(
                        parser.FileStream,
                        (long)node.Position,
                        (long)node.Length
                    )
                };
            }
            catch (Exception ex)
            {
                dataLines.Add(("<EXIFParseError>", ex.Message));
                return BuildResult(parser, node, dataLines);
            }
        }

        /// <summary>
        /// 从文件流读取当前 Node 的原始字节
        /// </summary>
        private static byte[] ReadNodeBytes(JPEGParser parser, Node node)
        {
            long length = (long)node.Length;
            long position = (long)node.Position;

            var buffer = new byte[length];

            lock (parser.FileStream)
            {
                parser.FileStream.Seek(position + 4, System.IO.SeekOrigin.Begin);
                parser.FileStream.ReadExactly(buffer);
            }

            return buffer;
        }

        /// <summary>
        /// 构建基础 ParseResult（避免重复代码）
        /// </summary>
        private static ParseResult BuildResult(JPEGParser parser, Node node, List<(string K, string V)> dataLines)
        {
            return new ParseResult
            {
                Title = $"ApplicationMarker(APP1) 'FFE1'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(
                    parser.FileStream,
                    (long)node.Position,
                    (long)node.Length
                )
            };
        }
    }
}