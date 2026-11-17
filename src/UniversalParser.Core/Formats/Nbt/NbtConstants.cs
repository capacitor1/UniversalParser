using System.Globalization;

namespace UniversalParser.Core.Formats.Nbt;

internal static class NbtConstants
{
    public const string ParserId = "nbt";
    public const string DisplayName = "Named Binary Tag";
    public const string RootStableId = "nbt-root";

    public const byte EndType = 0;
    public const byte ByteType = 1;
    public const byte ShortType = 2;
    public const byte IntType = 3;
    public const byte LongType = 4;
    public const byte FloatType = 5;
    public const byte DoubleType = 6;
    public const byte ByteArrayType = 7;
    public const byte StringType = 8;
    public const byte ListType = 9;
    public const byte CompoundType = 10;
    public const byte IntArrayType = 11;
    public const byte LongArrayType = 12;

    public const int MaxTagType = 12;
    public const int MaxScanDepth = 128;
    public const int ByteArrayPreviewBytes = 32;
    public const int NumericArrayPreviewCount = 8;

    public static bool IsValidTagType(byte type) => type is >= ByteType and <= LongArrayType;

    public static string GetTypeName(byte type) => type switch
    {
        ByteType => "Byte",
        ShortType => "Short",
        IntType => "Int",
        LongType => "Long",
        FloatType => "Float",
        DoubleType => "Double",
        ByteArrayType => "ByteArray",
        StringType => "String",
        ListType => "List",
        CompoundType => "Compound",
        IntArrayType => "IntArray",
        LongArrayType => "LongArray",
        EndType => "End",
        _ => "Unknown",
    };

    public static string GetSpecName(byte type) => type switch
    {
        ByteType => "TAG_Byte",
        ShortType => "TAG_Short",
        IntType => "TAG_Int",
        LongType => "TAG_Long",
        FloatType => "TAG_Float",
        DoubleType => "TAG_Double",
        ByteArrayType => "TAG_Byte_Array",
        StringType => "TAG_String",
        ListType => "TAG_List",
        CompoundType => "TAG_Compound",
        IntArrayType => "TAG_Int_Array",
        LongArrayType => "TAG_Long_Array",
        EndType => "TAG_End",
        _ => "TAG_Unknown",
    };

    public static string FormatTagType(byte type) =>
        string.Create(CultureInfo.InvariantCulture, $"{type} ({GetSpecName(type)})");

    public static string FormatNamedTitle(byte type, string name) =>
        string.Create(CultureInfo.InvariantCulture, $"{GetTypeName(type)} '{name}'");

    public static string FormatElementTitle(byte type, long index) =>
        string.Create(CultureInfo.InvariantCulture, $"{GetTypeName(type)} '[{index}]'");

    public static string FormatNamedStableId(long offset) =>
        string.Create(CultureInfo.InvariantCulture, $"nbt:tag:{offset}");

    public static string FormatElementStableId(long offset, byte type, long index) =>
        string.Create(CultureInfo.InvariantCulture, $"nbt:element:{offset}:{type}:{index}");

    public static string FormatUnparsedStableId(long offset) =>
        string.Create(CultureInfo.InvariantCulture, $"nbt:unparsed:{offset}");
}
