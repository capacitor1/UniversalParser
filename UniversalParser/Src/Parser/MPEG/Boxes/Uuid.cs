using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Uuid
    {
        // 解析 UUID Box
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var fs = parser.FileStream;

            if (fs.Length < (long)(node.Position + 24)) // 8(header) + 16(uuid)
                throw new InvalidDataException("UUID box is truncated.");

            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            // 读取 box header
            uint size = reader.ReadUInt32BE();
            string type = reader.ReadFourCC(); // "uuid"

            // 读取 16-byte UUID
            byte[] uuidBytes = reader.ReadBytes(16);

            string uuid = new Guid(uuidBytes).ToString();

            // payload
            long payloadStart = fs.Position;
            long payloadEnd = (long)(node.Position + node.Length);

            long payloadLength = payloadEnd - payloadStart;

            // DataLines
            var dataLines = new List<(string K, string V)>
        {
            ("uuid", uuid),
            ("<payload_length>", payloadLength.ToString())
        };

            return new ParseResult
            {
                
                Title = $"UUID '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}