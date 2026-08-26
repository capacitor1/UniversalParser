using System;

namespace UniversalParser.Src.Parser.ASF
{
    /// <summary>
    /// 一个 ASF 对象的头部信息。所有偏移都是**文件绝对偏移**。
    /// 由 <see cref="ASFParser.TryGetObjectHeader"/> 在点开节点时重新读取，
    /// 因此不依赖 Node 携带任何额外字段。
    /// </summary>
    internal readonly struct ASFObjectHeader
    {
        /// <summary>对象 GUID（ASF 混合端序布局，等于 .NET Guid 内存布局）。</summary>
        public required Guid Guid { get; init; }

        /// <summary>可读对象名；未识别的对象为 null。</summary>
        public string? Name { get; init; }

        /// <summary>对象起始（即 24 字节对象头所在位置）。</summary>
        public long ObjectStart { get; init; }

        /// <summary>负载起点 = ObjectStart + 24。</summary>
        public long PayloadStart { get; init; }

        /// <summary>头部 Object Size 字段的原始值（含 24 字节头；超出 long 范围时已钳制）。</summary>
        public long DeclaredSize { get; init; }

        /// <summary>实际可读到的负载长度（已按文件/父对象边界裁剪）。</summary>
        public long PayloadLength { get; init; }

        public int HeaderSize => ASFUtil.ObjectHeaderSize;

        /// <summary>按 Object Size 声明的负载长度。</summary>
        public long DeclaredPayloadLength => Math.Max(0, DeclaredSize - HeaderSize);

        public bool IsTruncated => DeclaredPayloadLength > PayloadLength;
        public bool IsContainer => ASFUtil.IsContainer(Guid);
        public long PayloadEnd => PayloadStart + PayloadLength;

        /// <summary>分发用的键：GUID 大写 "D" 格式。</summary>
        public string DispatchKey => ASFUtil.GuidDisplay(Guid);
    }
}