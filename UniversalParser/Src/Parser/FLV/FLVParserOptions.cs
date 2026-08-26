namespace UniversalParser.Src.Parser.FLV
{
    internal sealed class FLVParserOptions
    {
        /// <summary>
        /// 最多创建的 Tag 数量，防止恶意文件制造过量 Node。
        /// </summary>
        public int MaxTagCount { get; set; } = 10_000_000;

        /// <summary>
        /// 定位读取缓冲区大小。
        /// </summary>
        public int ReadBufferSize { get; set; } = 64 * 1024;

        /// <summary>
        /// ScriptDataTagData 最多加载到内存并交给 AMF0 解析器的字节数。
        /// 剩余部分通过 &lt;PayloadLength&gt; 表示。
        /// </summary>
        public int MaxScriptDataParseBytes { get; set; } = 128 * 1024 * 1024;

        /// <summary>AMF0 最大递归深度。</summary>
        public int MaxAmfDepth { get; set; } = 2048;
    }
}