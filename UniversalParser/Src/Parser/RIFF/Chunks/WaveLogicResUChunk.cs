using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace UniversalParser.Src.Parser.RIFF.Chunks
{
    /// <summary>
    /// WAVE 的 'ResU' 块：Apple Logic Pro 私有块，无公开规范。
    /// 负载为 zlib 压缩的 JSON。能完整解压且确为合法 JSON 时按缩进逐行呈现，
    /// 否则整块记为未解析。
    /// </summary>
    internal static class WaveLogicResUChunk
    {
        private const int MaxCompressedBytes = 64 * 1024 * 1024;
        private const int MaxInflatedBytes = 512 * 1024 * 1024;
        private const int MaxLines = 2147483600;
        private const int MaxLineLength = 2147483600;

        private static readonly JsonSerializerOptions IndentOptions = new()
        {
            WriteIndented = true,
            // 放宽转义，避免把非 ASCII 文本写成 \uXXXX 而丧失可读性。
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static ParseResult Parse(RIFFParser parser, Node node, RIFFChunkHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>(64);

            int read = ChunkUtil.ReadPayload(parser, header, MaxCompressedBytes, out byte[] payload);
            bool complete = read > 0 && read == header.PayloadLength;

            if (complete
                && TryInflate(payload, read, out byte[] inflated)
                && TryFormatJson(inflated, out List<string> lines))
            {
                dataLines.Add(("<Note>",
                    "Apple Logic Pro private chunk with no published specification. The payload is a "
                    + "zlib-compressed JSON documen."));

                for (int i = 0; i < lines.Count; i++)
                    dataLines.Add(("", lines[i]));

                // JSON 完整还原，负载无剩余，故不记 <PayloadLength>。
                return ChunkUtil.Build(parser, node, header, "WaveLogicResU", dataLines);
            }

            dataLines.Add(("<Note>",
                "Apple Logic Pro private chunk with no published specification. The payload is expected "
                + "to be a zlib-compressed JSON document but could not be recovered as one here."));

            if (header.IsTruncated)
                dataLines.Add(("<Warning>", "The 'ResU' chunk is truncated."));

            ChunkUtil.AddUnparsedLength(dataLines, header, 0);
            return ChunkUtil.Build(parser, node, header, "WaveLogicResU", dataLines);
        }

        /// <summary>zlib 解压。带解压上限，免得畸形块把内存吃干。</summary>
        private static bool TryInflate(byte[] source, int length, out byte[] result)
        {
            result = [];
            try
            {
                using var input = new MemoryStream(source, 0, length, writable: false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();

                byte[] buffer = new byte[81920];
                long total = 0;
                int n;

                while ((n = zlib.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += n;
                    if (total > MaxInflatedBytes) return false;
                    output.Write(buffer, 0, n);
                }

                result = output.ToArray();
                return result.Length > 0;
            }
            catch (InvalidDataException)
            {
                return false; // 非 zlib 流或校验失败
            }
        }

        /// <summary>校验并重排 JSON。任一环节不成立即视为不可呈现。</summary>
        private static bool TryFormatJson(byte[] utf8Json, out List<string> lines)
        {
            lines = [];

            ReadOnlyMemory<byte> body = utf8Json;
            if (body.Length >= 3 && body.Span[0] == 0xEF && body.Span[1] == 0xBB && body.Span[2] == 0xBF)
                body = body[3..]; // 跳过 BOM，Utf8JsonReader 不会自行处理

            string text;
            try
            {
                using var document = JsonDocument.Parse(body, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                text = JsonSerializer.Serialize(document.RootElement, IndentOptions);
            }
            catch (JsonException)
            {
                return false;
            }

            string[] raw = text.Split('\n');
            if (raw.Length > MaxLines) return false;

            lines = new List<string>(raw.Length);
            foreach (string item in raw)
            {
                string line = item.TrimEnd('\r');
                if (line.Length > MaxLineLength)
                    line = string.Concat(line.AsSpan(0, MaxLineLength), "...");

                lines.Add(line);
            }
            return true;
        }
    }
}