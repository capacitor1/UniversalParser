namespace UniversalParser.Src.Parser.RIFF
{
    /// <summary>
    /// 一个 RIFF 块的头部信息。所有偏移都是**文件绝对偏移**。
    /// 由 <see cref="RIFFParser.TryGetChunkHeader"/> 在点开节点时重新读取，
    /// 因此不依赖 Node 携带任何额外字段。
    /// </summary>
    internal readonly struct RIFFChunkHeader
    {
        /// <summary>块 ID（4CC，原始值，未 sanitize）。</summary>
        public required string Id { get; init; }

        /// <summary>容器的类型码（LIST 的 listType / RIFF 的 formType）；非容器为 null。</summary>
        public string? TypeCode { get; init; }

        /// <summary>头部 ckSize 字段的原始值（不含 8 字节头、不含对齐填充；容器则**包含**4 字节类型码）。</summary>
        public uint DeclaredSize { get; init; }

        /// <summary>块起始（即 8 字节头所在位置）。</summary>
        public long ChunkStart { get; init; }

        /// <summary>真实负载起点：非容器 = ChunkStart+8，容器 = ChunkStart+12。</summary>
        public long PayloadStart { get; init; }

        /// <summary>按 ckSize 声明的负载长度（容器已扣掉 4 字节类型码）。</summary>
        public long DeclaredPayloadLength { get; init; }

        /// <summary>实际可读到的负载长度（已按文件/父块边界裁剪）。</summary>
        public long PayloadLength { get; init; }

        public bool IsContainer => TypeCode is not null;
        public bool IsTruncated => DeclaredPayloadLength > PayloadLength;
        public int HeaderSize => TypeCode is null ? RIFFUtil.ChunkHeaderSize : RIFFUtil.TypedContainerHeaderSize;
        public long PayloadEnd => PayloadStart + PayloadLength;

        /// <summary>分发用的键：容器为 "LIST:INFO"，普通块为 "fmt "。</summary>
        public string DispatchKey => TypeCode is null ? Id : $"{Id}:{TypeCode}";
    }
}