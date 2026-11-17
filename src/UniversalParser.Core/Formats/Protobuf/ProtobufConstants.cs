using System.Globalization;

namespace UniversalParser.Core.Formats.Protobuf;

/// <summary>
/// Wire-level constants for Protocol Buffers. No .proto descriptor is used, so field numbers,
/// wire types and raw values are the only identities available.
/// </summary>
internal static class ProtobufConstants
{
    public const string ParserId = "protobuf";
    public const string DisplayName = "Protocol Buffers";
    public const string RootStableId = "protobuf-root";

    public const int VarintWireType = 0;
    public const int Fixed64WireType = 1;
    public const int LengthDelimitedWireType = 2;
    public const int StartGroupWireType = 3;
    public const int EndGroupWireType = 4;
    public const int Fixed32WireType = 5;

    public const uint MaxFieldNumber = 536_870_911;

    public const int MaxVarintBytes = 10;
    public const int TextSampleBytes = 4096;
    public const int MaxPayloadInspections = 32;
    public const int MaxScanDepth = 64;
    public const int HexPreviewBytes = 32;

    public static string GetWireTypeName(int wireType) => wireType switch
    {
        VarintWireType => "varint",
        Fixed64WireType => "fixed64",
        LengthDelimitedWireType => "length-delimited",
        StartGroupWireType => "start-group",
        EndGroupWireType => "end-group",
        Fixed32WireType => "fixed32",
        _ => "invalid",
    };

    public static string GetFieldTitleName(int wireType) => wireType switch
    {
        VarintWireType => "VarintField",
        Fixed64WireType => "Fixed64Field",
        LengthDelimitedWireType => "LengthDelimitedField",
        StartGroupWireType => "GroupField",
        Fixed32WireType => "Fixed32Field",
        _ => "Field",
    };

    public static string FormatStableId(long offset) =>
        string.Create(CultureInfo.InvariantCulture, $"protobuf:{offset}");

    public static string FormatFieldNodeName(int wireType) => GetWireTypeName(wireType);

    public static string FormatGroupNodeName() => GetWireTypeName(StartGroupWireType);
}
