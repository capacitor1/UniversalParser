using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.EBML.Elements
{
    /// <summary>
    /// Matroska / WebM SeekHead 解析器。
    ///
    /// SeekHead
    /// └── Seek
    ///     ├── SeekID
    ///     └── SeekPosition
    ///
    /// 当前解析内容：
    /// - SeekHead Master Element 基本状态；
    /// - Seek Master Element 基本状态；
    /// - SeekID 中保存的 EBML Element ID；
    /// - SeekPosition 相对于 Segment payload 的偏移；
    /// - SeekPosition 对应的绝对文件偏移；
    ///
    /// TODO:
    /// - 根据 SeekPosition 定位并解析目标 Element；
    /// - 校验 SeekID 与目标 Element 的实际 ID 是否一致；
    /// - 处理多个 Segment；
    /// - 支持 SeekHead 中的 Void / CRC-32；
    /// - 支持文件中非规范排序的 SeekHead。
    /// </summary>
    internal static class SeekHead
    {
        private const ulong SeekHeadId = 0x114D9B74;
        private const ulong SeekId = 0x4DBB;
        private const ulong SeekIDId = 0x53AB;
        private const ulong SeekPositionId = 0x53AC;

        // ============================================================
        // SeekHead
        // ============================================================

        public static ParseResult ParseSeekHead(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (header.ElementId != SeekHeadId)
            {
                dataLines.Add((
                    "<Warning>",
                    "The Element ID does not match SeekHead."));
            }

            if (!header.IsMaster)
            {
                dataLines.Add((
                    "<Warning>",
                    "SeekHead is not recognized as a Master Element."));
            }

            if (header.IsUnknownSize)
            {
                dataLines.Add((
                    "<Note>",
                    "SeekHead uses an unknown Data Size."));
            }

            if (header.IsTruncated)
            {
                dataLines.Add((
                    "<Warning>",
                    "SeekHead is truncated."));
            }

            int seekCount = 0;

            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetElementHeader(
                        child,
                        out EBMLElementHeader childHeader))
                {
                    continue;
                }

                if (childHeader.ElementId == SeekId)
                    seekCount++;
            }

            if (seekCount == 0)
            {
                dataLines.Add((
                    "<Warning>",
                    "SeekHead does not contain any Seek Element."));
            }

            return Build(
                parser,
                node,
                header,
                "SeekHead",
                dataLines);
        }

        // ============================================================
        // Seek
        // ============================================================

        public static ParseResult ParseSeek(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (header.ElementId != SeekId)
            {
                dataLines.Add((
                    "<Warning>",
                    "The Element ID does not match Seek."));
            }

            if (!header.IsMaster)
            {
                dataLines.Add((
                    "<Warning>",
                    "Seek is not recognized as a Master Element."));
            }

            if (header.IsUnknownSize)
            {
                dataLines.Add((
                    "<Note>",
                    "Seek uses an unknown Data Size."));
            }

            if (header.IsTruncated)
            {
                dataLines.Add((
                    "<Warning>",
                    "Seek is truncated."));
            }

            bool hasSeekId = false;
            bool hasSeekPosition = false;

            foreach (Node child in node.SubNodes)
            {
                if (!parser.TryGetElementHeader(
                        child,
                        out EBMLElementHeader childHeader))
                {
                    continue;
                }

                switch (childHeader.ElementId)
                {
                    case SeekIDId:
                        hasSeekId = true;
                        break;

                    case SeekPositionId:
                        hasSeekPosition = true;
                        break;
                }
            }

            if (!hasSeekId)
            {
                dataLines.Add((
                    "<Warning>",
                    "Seek does not contain SeekID."));
            }

            if (!hasSeekPosition)
            {
                dataLines.Add((
                    "<Warning>",
                    "Seek does not contain SeekPosition."));
            }

            return Build(
                parser,
                node,
                header,
                "Seek",
                dataLines);
        }

        // ============================================================
        // SeekID
        // ============================================================

        public static ParseResult ParseSeekID(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (header.ElementId != SeekIDId)
            {
                dataLines.Add((
                    "<Warning>",
                    "The Element ID does not match SeekID."));
            }

            // SeekID 保存的是目标 Element ID 的二进制编码。
            // 合法 Element ID 长度只能是 1 到 4 字节。
            if (header.PayloadLength is < 1 or > EBMLUtil.MaxElementIdLength)
            {
                dataLines.Add((
                    "<Error>",
                    "SeekID must contain between 1 and 4 bytes."));

                if (header.PayloadLength > 0)
                {
                    dataLines.Add((
                        "<PayloadLength>",
                        EBMLUtil.FormatBytes(header.PayloadLength)));
                }

                return Build(
                    parser,
                    node,
                    header,
                    "SeekID",
                    dataLines);
            }

            int byteCount = checked((int)header.PayloadLength);
            Span<byte> buffer = stackalloc byte[EBMLUtil.MaxElementIdLength];

            int read = parser.ReadAt(
                header.PayloadStart,
                buffer[..byteCount]);

            if (read != byteCount)
            {
                dataLines.Add((
                    "<Error>",
                    "Unable to read the complete SeekID value."));

                long unreadLength = Math.Max(0, header.PayloadLength - read);

                if (unreadLength > 0)
                {
                    dataLines.Add((
                        "<PayloadLength>",
                        EBMLUtil.FormatBytes(unreadLength)));
                }

                return Build(
                    parser,
                    node,
                    header,
                    "SeekID",
                    dataLines);
            }

            ReadOnlySpan<byte> rawId = buffer[..byteCount];

            // 原生项目：二进制数据的十六进制表示。
            dataLines.Add((
                "SeekID",
                Convert.ToHexString(rawId)));

            if (!EBMLUtil.TryDecodeElementId(
                    rawId,
                    out ulong targetId,
                    out int encodedLength))
            {
                dataLines.Add((
                    "<SeekID>",
                    "Invalid EBML Element ID."));

                return Build(
                    parser,
                    node,
                    header,
                    "SeekID",
                    dataLines);
            }

            if (encodedLength != byteCount)
            {
                dataLines.Add((
                    "<SeekID>",
                    "Invalid EBML Element ID length."));
            }
            else
            {
                string formattedId =
                    EBMLUtil.FormatElementId(
                        targetId,
                        encodedLength);

                string targetName =
                    EBMLSchema.GetName(targetId);

                // 同名带尖括号项目：将二进制 Element ID 可读化。
                dataLines.Add((
                    "<SeekID>",
                    $"{targetName} '{formattedId}'"));
            }

            if (header.IsTruncated)
            {
                dataLines.Add((
                    "<Warning>",
                    "SeekID is truncated."));
            }

            return Build(
                parser,
                node,
                header,
                "SeekID",
                dataLines);
        }

        // ============================================================
        // SeekPosition
        // ============================================================

        public static ParseResult ParseSeekPosition(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var dataLines = new List<(string K, string V)>();

            if (header.ElementId != SeekPositionId)
            {
                dataLines.Add((
                    "<Warning>",
                    "The Element ID does not match SeekPosition."));
            }

            if (header.PayloadLength is < 1 or > 8)
            {
                dataLines.Add((
                    "<Error>",
                    "SeekPosition must contain between 1 and 8 bytes."));

                if (header.PayloadLength > 0)
                {
                    dataLines.Add((
                        "<PayloadLength>",
                        EBMLUtil.FormatBytes(header.PayloadLength)));
                }

                return Build(
                    parser,
                    node,
                    header,
                    "SeekPosition",
                    dataLines);
            }

            int byteCount = checked((int)header.PayloadLength);
            Span<byte> buffer = stackalloc byte[8];

            int read = parser.ReadAt(
                header.PayloadStart,
                buffer[..byteCount]);

            if (read != byteCount)
            {
                dataLines.Add((
                    "<Error>",
                    "Unable to read the complete SeekPosition value."));

                long unreadLength = Math.Max(0, header.PayloadLength - read);

                if (unreadLength > 0)
                {
                    dataLines.Add((
                        "<PayloadLength>",
                        EBMLUtil.FormatBytes(unreadLength)));
                }

                return Build(
                    parser,
                    node,
                    header,
                    "SeekPosition",
                    dataLines);
            }

            ulong relativePosition =
                EBMLUtil.ReadUnsignedInteger(buffer[..byteCount]);

            // 官方字段名称和原始值。
            dataLines.Add((
                "SeekPosition",
                relativePosition.ToString()));

            if (parser.SegmentPayloadStart is long segmentPayloadStart)
            {
                try
                {
                    checked
                    {
                        long absolutePosition =
                            segmentPayloadStart +
                            checked((long)relativePosition);

                        dataLines.Add((
                            "<AbsolutePosition>",
                            $"{absolutePosition} (0x{absolutePosition:X})"));
                    }
                }
                catch (OverflowException)
                {
                    dataLines.Add((
                        "<Warning>",
                        "SeekPosition cannot be converted to a file absolute offset."));
                }
            }
            else
            {
                dataLines.Add((
                    "<Warning>",
                    "Segment payload start is unavailable; absolute offset was not calculated."));
            }

            if (header.IsTruncated)
            {
                dataLines.Add((
                    "<Warning>",
                    "SeekPosition is truncated."));
            }

            return Build(
                parser,
                node,
                header,
                "SeekPosition",
                dataLines);
        }

        // ============================================================
        // Result builder
        // ============================================================

        private static ParseResult Build(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header,
            string readableName,
            List<(string K, string V)> dataLines)
        {
            return new ParseResult
            {
                Title = EBMLUtil.MakeTitle(
                    readableName,
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