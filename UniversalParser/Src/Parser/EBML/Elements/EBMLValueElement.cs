using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UniversalParser.Src.Parser.EBML.Elements
{
    /// <summary>
    /// EBML 基础类型 Element 的通用解析辅助类。
    ///
    /// 此类只读取当前 Element 的自身负载，不读取父节点、兄弟节点或其他 Element。
    /// 如果负载无法按指定类型完整解析，则将未解析部分作为 PayloadLength 呈现。
    /// </summary>
    internal static class EBMLValueElement
    {
        private const int DefaultMaximumStringLength = 1024 * 1024;
        private const int DefaultMaximumBinaryLength = 1024 * 1024;

        internal delegate string? UnsignedValueFormatter(ulong value);
        internal delegate string? SignedValueFormatter(long value);
        internal delegate string? FloatValueFormatter(double value);
        internal delegate string? StringValueFormatter(string value);
        internal delegate string? BinaryValueFormatter(ReadOnlySpan<byte> value);

        // ============================================================
        // Master
        // ============================================================

        /// <summary>
        /// Master Element 的数据已经表现为子节点，因此 DataLines 保持为空。
        /// </summary>
        public static ParseResult ParseMaster(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string? elementName = null)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            return Build(
                parser,
                node,
                header,
                elementName ?? EBMLSchema.GetName(header.ElementId),
                []);
        }

        // ============================================================
        // Unsigned Integer
        // ============================================================

        public static ParseResult ParseUnsignedInteger(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string? elementName = null,
            UnsignedValueFormatter? formatter = null)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string name = elementName ?? EBMLSchema.GetName(header.ElementId);
            var dataLines = new List<(string K, string V)>();

            if (header.PayloadLength is < 1 or > 8)
            {
                AddPayloadLength(dataLines, header.PayloadLength);
                return Build(parser, node, header, name, dataLines);
            }

            int byteCount = (int)header.PayloadLength;
            Span<byte> buffer = stackalloc byte[8];

            int read = parser.ReadAt(
                header.PayloadStart,
                buffer[..byteCount]);

            if (read != byteCount)
            {
                AddPayloadLength(dataLines, Math.Max(0, header.PayloadLength - read));
                return Build(parser, node, header, name, dataLines);
            }

            ulong value = EBMLUtil.ReadUnsignedInteger(buffer[..byteCount]);

            dataLines.Add((
                name,
                value.ToString(CultureInfo.InvariantCulture)));

            string? readable = formatter?.Invoke(value);
            if (!string.IsNullOrEmpty(readable))
                dataLines.Add(($"<{name}>", readable));

            return Build(parser, node, header, name, dataLines);
        }

        // ============================================================
        // Signed Integer
        // ============================================================

        public static ParseResult ParseSignedInteger(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string? elementName = null,
            SignedValueFormatter? formatter = null)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string name = elementName ?? EBMLSchema.GetName(header.ElementId);
            var dataLines = new List<(string K, string V)>();

            if (header.PayloadLength is < 1 or > 8)
            {
                AddPayloadLength(dataLines, header.PayloadLength);
                return Build(parser, node, header, name, dataLines);
            }

            int byteCount = (int)header.PayloadLength;
            Span<byte> buffer = stackalloc byte[8];

            int read = parser.ReadAt(
                header.PayloadStart,
                buffer[..byteCount]);

            if (read != byteCount)
            {
                AddPayloadLength(dataLines, Math.Max(0, header.PayloadLength - read));
                return Build(parser, node, header, name, dataLines);
            }

            long value = EBMLUtil.ReadSignedInteger(buffer[..byteCount]);

            dataLines.Add((
                name,
                value.ToString(CultureInfo.InvariantCulture)));

            string? readable = formatter?.Invoke(value);
            if (!string.IsNullOrEmpty(readable))
                dataLines.Add(($"<{name}>", readable));

            return Build(parser, node, header, name, dataLines);
        }

        // ============================================================
        // Float
        // ============================================================

        public static ParseResult ParseFloat(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string? elementName = null,
            FloatValueFormatter? formatter = null)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string name = elementName ?? EBMLSchema.GetName(header.ElementId);
            var dataLines = new List<(string K, string V)>();

            double value;

            switch (header.PayloadLength)
            {
                case 0:
                    value = 0.0;
                    break;

                case 4:
                {
                    Span<byte> buffer = stackalloc byte[4];

                    int read = parser.ReadAt(
                        header.PayloadStart,
                        buffer);

                    if (read != buffer.Length)
                    {
                        AddPayloadLength(dataLines, Math.Max(0, header.PayloadLength - read));
                        return Build(parser, node, header, name, dataLines);
                    }

                    value = EBMLUtil.ReadFloat32(buffer);
                    break;
                }

                case 8:
                {
                    Span<byte> buffer = stackalloc byte[8];

                    int read = parser.ReadAt(
                        header.PayloadStart,
                        buffer);

                    if (read != buffer.Length)
                    {
                        AddPayloadLength(dataLines, Math.Max(0, header.PayloadLength - read));
                        return Build(parser, node, header, name, dataLines);
                    }

                    value = EBMLUtil.ReadFloat64(buffer);
                    break;
                }

                default:
                    AddPayloadLength(dataLines, header.PayloadLength);
                    return Build(parser, node, header, name, dataLines);
            }

            dataLines.Add((
                name,
                value.ToString("R", CultureInfo.InvariantCulture)));

            string? readable = formatter?.Invoke(value);
            if (!string.IsNullOrEmpty(readable))
                dataLines.Add(($"<{name}>", readable));

            return Build(parser, node, header, name, dataLines);
        }

        // ============================================================
        // ASCII String
        // ============================================================

        public static ParseResult ParseAsciiString(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string? elementName = null,
            StringValueFormatter? formatter = null,
            int maximumLength = DefaultMaximumStringLength)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string name = elementName ?? EBMLSchema.GetName(header.ElementId);
            var dataLines = new List<(string K, string V)>();

            if (!TryReadPayload(
                    parser,
                    header,
                    maximumLength,
                    out byte[] payload,
                    out long unparsedLength))
            {
                AddPayloadLength(dataLines, unparsedLength);
                return Build(parser, node, header, name, dataLines);
            }

            foreach (byte value in payload)
            {
                if (value > 0x7F)
                {
                    AddPayloadLength(dataLines, header.PayloadLength);
                    return Build(parser, node, header, name, dataLines);
                }
            }

            string value1 = Encoding.ASCII.GetString(payload);

            dataLines.Add((name, value1));

            string? readable = formatter?.Invoke(value1);
            if (!string.IsNullOrEmpty(readable))
                dataLines.Add(($"<{name}>", readable));

            return Build(parser, node, header, name, dataLines);
        }

        // ============================================================
        // UTF-8 String
        // ============================================================

        public static ParseResult ParseUtf8String(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string? elementName = null,
            StringValueFormatter? formatter = null,
            int maximumLength = DefaultMaximumStringLength)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string name = elementName ?? EBMLSchema.GetName(header.ElementId);
            var dataLines = new List<(string K, string V)>();

            if (!TryReadPayload(
                    parser,
                    header,
                    maximumLength,
                    out byte[] payload,
                    out long unparsedLength))
            {
                AddPayloadLength(dataLines, unparsedLength);
                return Build(parser, node, header, name, dataLines);
            }

            string value;

            try
            {
                value = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true).GetString(payload);
            }
            catch (DecoderFallbackException)
            {
                AddPayloadLength(dataLines, header.PayloadLength);
                return Build(parser, node, header, name, dataLines);
            }

            dataLines.Add((name, value));

            string? readable = formatter?.Invoke(value);
            if (!string.IsNullOrEmpty(readable))
                dataLines.Add(($"<{name}>", readable));

            return Build(parser, node, header, name, dataLines);
        }

        // ============================================================
        // Date
        // ============================================================

        /// <summary>
        /// EBML Date 的原始值是相对于 2001-01-01T00:00:00 UTC 的有符号纳秒数。
        /// </summary>
        public static ParseResult ParseDate(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string? elementName = null)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string name = elementName ?? EBMLSchema.GetName(header.ElementId);
            var dataLines = new List<(string K, string V)>();

            if (header.PayloadLength != 8)
            {
                AddPayloadLength(dataLines, header.PayloadLength);
                return Build(parser, node, header, name, dataLines);
            }

            Span<byte> buffer = stackalloc byte[8];

            int read = parser.ReadAt(
                header.PayloadStart,
                buffer);

            if (read != buffer.Length)
            {
                AddPayloadLength(dataLines, Math.Max(0, header.PayloadLength - read));
                return Build(parser, node, header, name, dataLines);
            }

            long nanoseconds = EBMLUtil.ReadSignedInteger(buffer);

            dataLines.Add((
                name,
                nanoseconds.ToString(CultureInfo.InvariantCulture)));

            try
            {
                // DateTime 的最小分辨率为 100 ns。
                long ticks = nanoseconds / 100L;

                var epoch = new DateTimeOffset(
                    2001, 1, 1,
                    0, 0, 0,
                    TimeSpan.Zero);

                DateTimeOffset value = epoch.AddTicks(ticks);

                dataLines.Add((
                    $"<{name}>",
                    value.ToString("O", CultureInfo.InvariantCulture)));
            }
            catch (ArgumentOutOfRangeException)
            {
                // 原始值仍已完整读取，不将其重新视为未解析数据。
            }

            return Build(parser, node, header, name, dataLines);
        }

        // ============================================================
        // Binary
        // ============================================================

        /// <summary>
        /// 解析需要作为数值呈现的小型 Binary Element。
        /// 视频、音频、附件等高密度数据不应调用此方法，应直接走未解析负载。
        /// </summary>
        public static ParseResult ParseBinary(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string? elementName = null,
            BinaryValueFormatter? formatter = null,
            int maximumLength = DefaultMaximumBinaryLength,
            int? requiredLength = null)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string name = elementName ?? EBMLSchema.GetName(header.ElementId);
            var dataLines = new List<(string K, string V)>();

            if (requiredLength is int exactLength &&
                header.PayloadLength != exactLength)
            {
                AddPayloadLength(dataLines, header.PayloadLength);
                return Build(parser, node, header, name, dataLines);
            }

            if (!TryReadPayload(
                    parser,
                    header,
                    maximumLength,
                    out byte[] payload,
                    out long unparsedLength))
            {
                AddPayloadLength(dataLines, unparsedLength);
                return Build(parser, node, header, name, dataLines);
            }

            dataLines.Add((
                name,
                Convert.ToHexString(payload)));

            string? readable = formatter?.Invoke(payload);
            if (!string.IsNullOrEmpty(readable))
                dataLines.Add(($"<{name}>", readable));

            return Build(parser, node, header, name, dataLines);
        }

        // ============================================================
        // Unparsed payload
        // ============================================================

        /// <summary>
        /// 用于 Void、高密度二进制载荷以及明确不需要解析的 Element。
        /// </summary>
        public static ParseResult ParseUnparsed(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string? elementName = null)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            string name = elementName ?? EBMLSchema.GetName(header.ElementId);

            var dataLines = new List<(string K, string V)>();

            AddPayloadLength(dataLines, header.PayloadLength);

            return Build(parser, node, header, name, dataLines);
        }

        // ============================================================
        // Internal helpers
        // ============================================================

        private static bool TryReadPayload(
            EBMLParser parser,
            EBMLElementHeader header,
            int maximumLength,
            out byte[] payload,
            out long unparsedLength)
        {
            payload = [];
            unparsedLength = header.PayloadLength;

            if (header.PayloadLength < 0 ||
                header.PayloadLength > maximumLength ||
                header.PayloadLength > int.MaxValue)
            {
                return false;
            }

            int byteCount = (int)header.PayloadLength;

            if (byteCount == 0)
            {
                payload = [];
                unparsedLength = 0;
                return true;
            }

            payload = new byte[byteCount];

            int read = parser.ReadAt(
                header.PayloadStart,
                payload);

            if (read != byteCount)
            {
                payload = [];
                unparsedLength = Math.Max(0, header.PayloadLength - read);
                return false;
            }

            unparsedLength = 0;
            return true;
        }

        private static void AddPayloadLength(
            List<(string K, string V)> dataLines,
            long payloadLength)
        {
            if (payloadLength <= 0)
                return;

            dataLines.Add((
                "<PayloadLength>",
                EBMLUtil.FormatBytes(payloadLength)));
        }

        private static ParseResult Build(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string elementName,
            List<(string K, string V)> dataLines)
        {
            return new ParseResult
            {
                Title = EBMLUtil.MakeTitle(
                    elementName,
                    header.ElementId,
                    header.ElementIdLength),

                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,

                RawData = parser.CreateRawStream(
                    header.ElementStart,
                    header.ElementLength),
            };
        }
    }
}