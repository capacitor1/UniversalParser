using System;
using System.Collections.Generic;
using System.Diagnostics;
using UniversalParser.Src.Parser.ASF.Chunks;

namespace UniversalParser.Src.Parser.ASF
{
    internal delegate ParseResult ASFObjectHandler(ASFParser parser, Node node, ASFObjectHeader header);

    internal static class ASFDispatcher
    {
        /// <summary>键为 GUID 大写 "D" 格式（见 <see cref="ASFUtil.GuidDisplay"/>）。</summary>
        private static readonly Dictionary<string, ASFObjectHandler> Handlers = new(StringComparer.Ordinal);

        static ASFDispatcher()
        {
            // 容器（框架必需）
            Register(ContainerChunk.Parse, ASFUtil.HeaderObject, ASFUtil.HeaderExtensionObject);
            
            Register(CodecListChunk.Parse, ASFUtil.CodecListObject);
            Register(FilePropertiesChunk.Parse, ASFUtil.FilePropertiesObject);
            Register(ContentDescriptionChunk.Parse, ASFUtil.ContentDescriptionObject);
            Register(StreamingMediaPropertiesChunk.Parse, ASFUtil.StreamingMediaPropertiesObject);
            Register(ExtendedContentDescriptionChunk.Parse, ASFUtil.ExtendedContentDescriptionObject);
            Register(StreamPropertiesChunk.Parse, ASFUtil.StreamPropertiesObject);
            Register(DataChunk.Parse, ASFUtil.DataObject);
        }

        private static void Register(ASFObjectHandler handler, params Guid[] guids)
        {
            ArgumentNullException.ThrowIfNull(handler);
            foreach (Guid guid in guids) Handlers[ASFUtil.GuidDisplay(guid)] = handler;
        }

        public static ParseResult Dispatch(ASFParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            // 第一步单独隔离：拿不到合法对象头的（尾部数据 / 损坏区域 / 深度截断占位）走 raw 视图
            ASFObjectHeader header;
            try
            {
                if (!parser.TryGetObjectHeader(node, out header))
                    return Default.ParseRaw(parser, node);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ASFDispatcher] Header read failed at 0x{node.Position:X}: {ex}");
                return Default.ParseRaw(parser, node);
            }

            // 第二步：真正的对象解析。单个对象失败绝不能让整个 UI 崩掉
            try
            {
                if (Handlers.TryGetValue(header.DispatchKey, out var handler))
                    return handler(parser, node, header);

                return Default.Parse(parser, node, header);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ASFDispatcher] Failed to parse '{header.Name ?? "UnknownObject"}' at 0x{header.ObjectStart:X}: {ex}");
                return new ParseResult
                {
                    Title = ASFUtil.MakeTitle("ParseError", ASFUtil.GuidDisplay(header.Guid)),
                    Position = node.Position,
                    Length = node.Length,
                    DataLines =
                    [
                        ("<Error>", ex.GetType().Name),
                        ("<Message>", ex.Message),
                        ("<PayloadLength>", ASFUtil.FormatBytes(header.PayloadLength)),
                    ],
                    RawData = parser.CreateRawStream(header.ObjectStart, (long)node.Length),
                };
            }
        }
    }
}