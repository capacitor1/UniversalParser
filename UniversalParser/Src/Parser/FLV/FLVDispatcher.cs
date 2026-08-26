using System;
using System.Collections.Generic;
using System.Diagnostics;
using UniversalParser.Src.Parser.FLV.Chunks;

namespace UniversalParser.Src.Parser.FLV
{
    internal delegate ParseResult FLVNodeHandler(FLVParser parser, Node node);

    internal static class FLVDispatcher
    {
        private static readonly Dictionary<string, FLVNodeHandler> Handlers =
            new(StringComparer.Ordinal)
            {
                ["FLV"] = FLVHeaderChunk.Parse,

                ["AudioTag"] = FLVTagChunk.Parse,
                ["VideoTag"] = FLVTagChunk.Parse,
                ["ScriptDataTag"] = FLVTagChunk.Parse,
                ["UnknownTag"] = FLVTagChunk.Parse,

                ["AudioTagData"] = FLVAudioTagDataChunk.Parse,
                ["VideoTagData"] = FLVVideoTagDataChunk.Parse,
                ["ScriptDataTagData"] = FLVScriptDataTagDataChunk.Parse,
                ["UnknownTagData"] = FLVDefaultChunk.Parse
            };

        public static void Register(
            string nodeName,
            FLVNodeHandler handler)
        {
            ArgumentException.ThrowIfNullOrEmpty(nodeName);
            ArgumentNullException.ThrowIfNull(handler);

            Handlers[nodeName] = handler;
        }

        public static ParseResult Dispatch(
            FLVParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            try
            {
                if (Handlers.TryGetValue(node.NodeName, out FLVNodeHandler? handler))
                    return handler(parser, node);

                return FLVDefaultChunk.Parse(parser, node);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[FLVDispatcher] Failed to parse '{node.NodeName}' " +
                    $"at 0x{node.Position:X}: {ex}");

                return new ParseResult
                {
                    Title = FLVUtil.MakeTitle("ParseError", node.NodeName),
                    Position = node.Position,
                    Length = node.Length,
                    DataLines =
                    [
                        ("<Error>", ex.GetType().Name),
                        ("<Message>", ex.Message),
                        ("<PayloadLength>", FLVUtil.FormatBytes((long)node.Length))
                    ],
                    RawData = parser.CreateRawStream(
                        (long)node.Position,
                        (long)node.Length)
                };
            }
        }
    }
}