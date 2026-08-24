using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Ftyp
    {
        // 解析 FTYP Box
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var fs = parser.FileStream;

            // 确保位置合法
            if (fs.Length < (long)(node.Position + 8))
                throw new InvalidDataException("Box is truncated.");

            // 创建大端读取器
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            // 读取 box header
            uint size = reader.ReadUInt32BE();
            string type = reader.ReadFourCC(); // 应该是 "ftyp"

            // 读取 FTYP payload
            string majorBrand = reader.ReadFourCC();
            uint minorVersion = reader.ReadUInt32BE();

            var compatibleBrands = new List<string>();

            long payloadEnd = (long)(node.Position + node.Length);
            while (fs.Position + 4 <= payloadEnd)
            {
                compatibleBrands.Add(reader.ReadFourCC());
            }

            // 准备 DataLines
            var dataLines = new List<(string K, string V)>
            {
                ("major_brand", majorBrand),
                ("minor_version", minorVersion.ToString())
            };

            // 添加 compatible_brands
            dataLines.Add(($"compatible_brands[{compatibleBrands.Count}]", compatibleBrands[0]));
            for (int i = 1; i < compatibleBrands.Count; i++)
            {
                dataLines.Add((string.Empty, compatibleBrands[i]));
            }

            return new ParseResult
            {
                
                Title = $"FileType '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}