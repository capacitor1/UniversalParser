using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.FBX
{
    /// <summary>
    /// Properties70 中 P 记录的值语义类别。
    /// </summary>
    internal enum FBXProperty70ValueKind
    {
        /// <summary>没有值段，例如 Compound 与 object。</summary>
        None,

        Boolean,
        Integer,
        Number,
        Time,
        Text,
        Blob,
        Vector2,
        Vector3,
        Vector4,
        Color3,
        Color4,
        Distance,

        /// <summary>类型名未登记，值段按原始属性逐个显示。</summary>
        Unknown,
    }

    /// <summary>
    /// 一条 Properties70 属性记录。
    ///
    /// FBX 二进制格式没有为 P 记录的各个槽位定义正式字段名，
    /// 这里采用与 FBX SDK 及 ASCII FBX 输出一致的命名：
    ///
    /// PropName / PropType / Label / Flags / Value
    /// </summary>
    internal readonly struct FBXProperty70
    {
        /// <summary>属性名，例如 UpAxis、Lcl Translation、DiffuseColor。</summary>
        public required string PropName { get; init; }

        /// <summary>属性数据类型名，例如 int、double、ColorRGB、KString。</summary>
        public required string PropType { get; init; }

        /// <summary>属性标签，通常为空字符串。</summary>
        public required string Label { get; init; }

        /// <summary>属性标志串，例如 A、A+、AU、LH。</summary>
        public required string Flags { get; init; }

        /// <summary>值段。</summary>
        public required List<FBXPropertyValue> Values { get; init; }

        /// <summary>根据 PropType 推断的值语义。</summary>
        public FBXProperty70ValueKind Kind { get; init; }

        /// <summary>类型表登记的期望值数量，未登记时为 -1。</summary>
        public int ExpectedValueCount { get; init; }

        /// <summary>值段中 Raw 属性所携带的未解析字节总数。</summary>
        public long RawByteLength { get; init; }

        /// <summary>属性区尾部未被消费的字节数。</summary>
        public long UnparsedByteLength { get; init; }

        public bool HasValueCountMismatch =>
            ExpectedValueCount >= 0 &&
            ExpectedValueCount != Values.Count;
    }
}