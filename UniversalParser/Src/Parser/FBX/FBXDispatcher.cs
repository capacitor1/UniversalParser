using System;
using System.Collections.Generic;
using System.Diagnostics;
using UniversalParser.Src.Parser.FBX.Chunks;

namespace UniversalParser.Src.Parser.FBX
{
    internal delegate ParseResult FBXChunkHandler(
        FBXParser parser,
        Node node,
        FBXNodeHeader header);

    internal static class FBXDispatcher
    {
        private static readonly Dictionary<string, FBXChunkHandler> Handlers =
            new(StringComparer.Ordinal);

        static FBXDispatcher()
        {
            Register(FBXHeaderExtensionChunk.Parse, "FBXHeaderExtension");
            Register(Properties70Chunk.Parse, "Properties70");
            Register(Properties70Chunk.ParseProperty, "P");
            Register(GlobalSettingsChunk.Parse, "GlobalSettings");
            Register(DocumentsChunk.Parse, "Documents");
            Register(DocumentsChunk.ParseDocument, "Document");
            Register(DefinitionsChunk.Parse, "Definitions");
            Register(DefinitionsChunk.ParseObjectType, "ObjectType");
            Register(DefinitionsChunk.ParsePropertyTemplate, "PropertyTemplate");
            Register(ObjectsChunk.Parse, "Objects");
            RegisterA(ObjectsChunk.ParseObject, "Geometry", "Model", "Material", "Texture", "Video", "Pose", "Deformer", "SubDeformer", "Cluster", "BlendShape", "BlendShapeChannel", "AnimationStack", "AnimationLayer", "AnimationCurve", "AnimationCurveNode");
            RegisterA(ObjectsChunk.ParseObject, "NodeAttribute", "Light", "Camera", "GenericNode", "Audio");
            Register(FBXNodeChunk.Parse, "TypeFlags");
            Register(FBXNodeChunk.Parse, "Count");
            Register(FBXNodeChunk.Parse, "RootNode");
        }

        public static void Register(
            FBXChunkHandler handler,
            string nodeName)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentNullException.ThrowIfNull(nodeName);
            if (!string.IsNullOrEmpty(nodeName))
                Handlers[nodeName] = handler;
        }
        public static void RegisterA(
            FBXChunkHandler handler,
            params string[] nodeNames)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentNullException.ThrowIfNull(nodeNames);

            foreach (string nodeName in nodeNames)
            {
                if (string.IsNullOrEmpty(nodeName))
                    continue;

                Handlers[nodeName] = handler;
            }
        }

        public static ParseResult Dispatch(
            FBXParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            FBXNodeHeader header;

            try
            {
                if (!parser.TryGetNodeHeader(
                        node,
                        out header))
                {
                    return Default.ParseRaw(
                        parser,
                        node);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[FBXDispatcher] Unable to read node header at " +
                    $"0x{node.Position:X}: {ex}");

                return Default.ParseRaw(
                    parser,
                    node);
            }

            try
            {
                if (Handlers.TryGetValue(
                        header.Name,
                        out FBXChunkHandler? handler))
                {
                    return handler(
                        parser,
                        node,
                        header);
                }

                return FBXNodeChunk.Parse(
                    parser,
                    node,
                    header);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[FBXDispatcher] Failed to parse node " +
                    $"'{header.Name}' at 0x{header.NodeStart:X}: {ex}");

                return new ParseResult
                {
                    Title = FBXUtil.MakeTitle(
                        "ParseError",
                        header.Name),

                    Position = node.Position,
                    Length = node.Length,

                    DataLines =
                    [
                        ("<Error>", ex.GetType().Name),
                        ("<Message>", ex.Message),
                        (
                            "<PayloadLength>",
                            FBXUtil.FormatBytes(
                                header.ActualPropertyLength)
                        ),
                    ],

                    RawData = parser.CreateRawStream(
                        header.NodeStart,
                        (long)node.Length),
                };
            }
        }
    }
}