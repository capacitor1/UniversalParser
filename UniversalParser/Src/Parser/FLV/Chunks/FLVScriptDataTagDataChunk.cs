using System;
using System.Collections.Generic;
using System.IO;

namespace UniversalParser.Src.Parser.FLV.Chunks
{
    internal static class FLVScriptDataTagDataChunk
    {
        public static ParseResult Parse(
            FLVParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            long totalLength = (long)node.Length;
            long parseLength = Math.Min(
                totalLength,
                parser.Options.MaxScriptDataParseBytes);

            var dataLines = new List<(string K, string V)>();

            if (totalLength <= 0)
            {
                dataLines.Add((
                    "<Error>",
                    "ScriptDataTagData is empty."));

                return Build(parser, node, dataLines);
            }

            long consumed = 0;

            using Stream stream = parser.CreateRawStream(
                (long)node.Position,
                parseLength);

            var reader = new FLVReader(stream);

            try
            {
                var amf = new AMF0Parser(
                    reader,
                    parser.Options.MaxAmfDepth);

                /*
                 * FLV ScriptDataBody:
                 *
                 * Name  SCRIPTDATAVALUE
                 * Value SCRIPTDATAVALUE
                 *
                 * Composite values are flattened directly into DataLines.
                 */
                amf.ReadValue("Name", dataLines);
                amf.ReadValue("Value", dataLines);
            }
            catch (Exception ex)
            {
                /*
                 * Successfully parsed entries remain visible. Only the unread
                 * suffix is counted as payload.
                 */
                dataLines.Add(("<Error>", ex.GetType().Name));
                dataLines.Add(("<Message>", ex.Message));
            }
            finally
            {
                if (reader.Position >= 0)
                    consumed = Math.Clamp(reader.Position, 0, parseLength);
            }

            long unparsed = Math.Max(0, totalLength - consumed);

            if (unparsed > 0)
            {
                dataLines.Add((
                    "<PayloadLength>",
                    FLVUtil.FormatBytes(unparsed)));
            }

            return Build(parser, node, dataLines);
        }

        private static ParseResult Build(
            FLVParser parser,
            Node node,
            List<(string K, string V)> dataLines) =>
            new()
            {
                Title = FLVUtil.MakeTitle(
                    "ScriptDataTagData",
                    node.NodeName),

                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,

                RawData = parser.CreateRawStream(
                    (long)node.Position,
                    (long)node.Length)
            };
    }
}