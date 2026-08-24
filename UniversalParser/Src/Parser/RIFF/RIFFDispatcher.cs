using System;
using System.Collections.Generic;
using System.Diagnostics;
using UniversalParser.Src.Parser.RIFF.Chunks;

namespace UniversalParser.Src.Parser.RIFF
{
    internal delegate ParseResult RIFFChunkHandler(RIFFParser parser, Node node, RIFFChunkHeader header);

    internal delegate bool RIFFChunkMatcher(RIFFParser parser, RIFFChunkHeader header);

    internal static class RIFFDispatcher
    {
        /// <summary>
        /// 精确键。可以是 "ID"（如 "fmt "），也可以是 "ID:TYPE"（如 "LIST:INFO"）——后者优先。
        /// 分发依据是从文件重新读到的真实块头，不是 Node 的显示名。
        /// </summary>
        private static readonly Dictionary<string, RIFFChunkHandler> Handlers = new(StringComparer.Ordinal);

        /// <summary>
        /// 模式回退。用于 FourCC 不固定、无法穷举的块（如 AVI 的 '##dc' —— 文本流的类型码可任意）。
        /// 按注册顺序匹配，优先级低于所有精确键、高于 Default。
        /// </summary>
        private static readonly List<(RIFFChunkMatcher Match, RIFFChunkHandler Handler)> Fallbacks = [];

        static RIFFDispatcher()
        {
            // ---- 容器 ----
            Register(ContainerChunk.Parse, "RIFF", "RIFX", "RF64", "BW64", "LIST");

            // ---- WAVE ----
            Register(WaveFmtChunk.Parse, "fmt ");
            Register(WaveDataChunk.Parse, "data");
            Register(Ds64Chunk.Parse, "ds64");
            Register(WaveFactChunk.Parse, "fact");
            Register(BextChunk.Parse, "bext");
            Register(WaveCueChunk.Parse, "cue ");
            Register(WaveSamplerChunk.Parse, "smpl");
            Register(HashChunk.Parse, "hash");

            // ---- AVI 头部 ----
            Register(AviMainHeaderChunk.Parse, "avih");
            Register(AviStreamHeaderChunk.Parse, "strh");
            Register(AviStreamFormatChunk.Parse, "strf");
            Register(AviVideoPropChunk.Parse, "vprp");
            Register(AviOpenDmlHeaderChunk.Parse, "dmlh");

            // ---- AVI 索引 ----
            Register(AviOldIndexChunk.Parse, "idx1");
            Register(AviIndexChunk.Parse, "indx");

            // ---- 通用填充 ----
            foreach (string id in JunkChunk.KnownIds.Keys)
                Register(JunkChunk.Parse, id);

            // ---- INFO / AVI 文本 ----
            foreach (string tag in InfoTextChunk.KnownTags.Keys)
                Register(InfoTextChunk.Parse, tag);
            // ---- XML / XMP 文本包 ----
            foreach (string id in XmpPacketChunk.KnownIds.Keys)
                Register(XmpPacketChunk.Parse, id);

            // ---- Adobe Tdat 子块（无公开规范）----
            foreach (string id in TdatChunk.RegistrationKeys)
                Register(TdatChunk.Parse, id);
            
            // ---- WebP ----
            Register(WebPVp8xChunk.Parse, "VP8X");
            Register(WebPVp8lChunk.Parse, "VP8L");
            Register(IccProfileChunk.Parse, "ICCP");
            Register(WebPAnimChunk.Parse, "ANIM");
            Register(WebPAnmfChunk.Parse, "ANMF");
            Register(WebPVp8Chunk.Parse, "VP8 ");
            Register(WebPAlphChunk.Parse, "ALPH"); 
            
            // ---- ANI (ACON) ----
            Register(AniHeaderChunk.Parse, "anih");
            Register(AniIconChunk.Parse, "icon");
            Register(AniStepArrayChunk.ParseRate, "rate");
            Register(AniStepArrayChunk.ParseSequence, "seq ");

            // ---- 未见规范的块 ----
            Register(PsaiChunk.Parse, "PSAI");

            // ---- 模式回退 ----
            RegisterFallback(AviStreamDataChunk.Matches, AviStreamDataChunk.Parse);
            RegisterFallback(AviIndexChunk.MatchesStandardIndexFourCC, AviIndexChunk.Parse);
        }

        public static void Register(RIFFChunkHandler handler, params string[] keys)
        {
            ArgumentNullException.ThrowIfNull(handler);
            foreach (string key in keys)
            {
                Debug.Assert(key.Length == 4 || key.Length == 9, $"Invalid dispatch key '{key}'.");
                Handlers[key] = handler;
            }
        }

        public static void RegisterFallback(RIFFChunkMatcher matcher, RIFFChunkHandler handler)
        {
            ArgumentNullException.ThrowIfNull(matcher);
            ArgumentNullException.ThrowIfNull(handler);
            Fallbacks.Add((matcher, handler));
        }

        public static ParseResult Dispatch(RIFFParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            // 第一步单独隔离：拿不到合法块头的（尾部数据 / 损坏区域 / 深度截断占位）走 raw 视图
            RIFFChunkHeader header;
            try
            {
                if (!parser.TryGetChunkHeader(node, out header))
                    return Default.ParseRaw(parser, node);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RIFFDispatcher] Header read failed at 0x{node.Position:X}: {ex}");
                return Default.ParseRaw(parser, node);
            }

            // 第二步：真正的块解析。单个块失败绝不能让整个 UI 崩掉
            try
            {
                if (header.TypeCode is not null && Handlers.TryGetValue(header.DispatchKey, out var typedHandler))
                    return typedHandler(parser, node, header);

                if (Handlers.TryGetValue(header.Id, out var handler))
                    return handler(parser, node, header);

                foreach ((RIFFChunkMatcher match, RIFFChunkHandler fallback) in Fallbacks)
                {
                    if (match(parser, header))
                        return fallback(parser, node, header);
                }

                return Default.Parse(parser, node, header);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RIFFDispatcher] Failed to parse '{header.Id}' at 0x{header.ChunkStart:X}: {ex}");
                return new ParseResult
                {
                    Title = RIFFUtil.MakeTitle("ParseError", header.Id),
                    Position = node.Position,
                    Length = node.Length,
                    DataLines =
                    [
                        ("<Error>", ex.GetType().Name),
                        ("<Message>", ex.Message),
                        ("<PayloadLength>", RIFFUtil.FormatBytes(header.PayloadLength)),
                    ],
                    RawData = parser.CreateRawStream(header.ChunkStart, (long)node.Length),
                };
            }
        }
    }
}