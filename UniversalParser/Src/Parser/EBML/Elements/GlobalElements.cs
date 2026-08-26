namespace UniversalParser.Src.Parser.EBML.Elements
{
    /// <summary>EBML Global Elements。</summary>
    internal static class GlobalElements
    {
        /// <summary>
        /// Void 的负载是填充数据，不需要解析。
        /// </summary>
        public static ParseResult ParseVoid(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            return EBMLValueElement.ParseUnparsed(
                parser,
                node,
                header,
                "Void");
        }

        /// <summary>
        /// CRC-32 只呈现当前 Element 内保存的 4 字节校验值。
        /// 不读取或校验父 Master Element 的其他数据。
        /// </summary>
        public static ParseResult ParseCRC32(
            EBMLParser parser,
            Node node,
            EBMLElementHeader header)
        {
            return EBMLValueElement.ParseBinary(
                parser,
                node,
                header,
                "CRC-32",
                requiredLength: 4);
        }
    }
}