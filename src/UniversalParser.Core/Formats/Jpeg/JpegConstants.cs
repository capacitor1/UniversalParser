using System.Globalization;

namespace UniversalParser.Core.Formats.Jpeg;

/// <summary>
/// JPEG marker constants and display names. Marker codes are the second byte of a two-byte
/// <c>FF xx</c> marker; fill bytes may precede the marker in the stream.
/// </summary>
internal static class JpegConstants
{
    public const byte MarkerPrefix = 0xFF;
    public const byte Tem = 0x01;
    public const byte Soi = 0xD8;
    public const byte Eoi = 0xD9;
    public const byte Sos = 0xDA;
    public const byte Dqt = 0xDB;
    public const byte Dht = 0xC4;
    public const byte Dri = 0xDD;
    public const byte Com = 0xFE;

    private static readonly Dictionary<byte, string> Names = new()
    {
        [0x01] = "TEM",
        [0xC0] = "SOF0",
        [0xC1] = "SOF1",
        [0xC2] = "SOF2",
        [0xC3] = "SOF3",
        [0xC4] = "DHT",
        [0xC5] = "SOF5",
        [0xC6] = "SOF6",
        [0xC7] = "SOF7",
        [0xC8] = "JPG",
        [0xC9] = "SOF9",
        [0xCA] = "SOF10",
        [0xCB] = "SOF11",
        [0xCC] = "DAC",
        [0xCD] = "SOF13",
        [0xCE] = "SOF14",
        [0xCF] = "SOF15",
        [0xD0] = "RST0",
        [0xD1] = "RST1",
        [0xD2] = "RST2",
        [0xD3] = "RST3",
        [0xD4] = "RST4",
        [0xD5] = "RST5",
        [0xD6] = "RST6",
        [0xD7] = "RST7",
        [0xD8] = "SOI",
        [0xD9] = "EOI",
        [0xDA] = "SOS",
        [0xDB] = "DQT",
        [0xDC] = "DNL",
        [0xDD] = "DRI",
        [0xDE] = "DHP",
        [0xDF] = "EXP",
        [0xE0] = "APP0",
        [0xE1] = "APP1",
        [0xE2] = "APP2",
        [0xE3] = "APP3",
        [0xE4] = "APP4",
        [0xE5] = "APP5",
        [0xE6] = "APP6",
        [0xE7] = "APP7",
        [0xE8] = "APP8",
        [0xE9] = "APP9",
        [0xEA] = "APP10",
        [0xEB] = "APP11",
        [0xEC] = "APP12",
        [0xED] = "APP13",
        [0xEE] = "APP14",
        [0xEF] = "APP15",
        [0xFE] = "COM",
    };

    private static readonly Dictionary<byte, string> ReadableNames = new()
    {
        [0x01] = "Temporary",
        [0xC0] = "StartOfFrameBaselineDct",
        [0xC1] = "StartOfFrameExtendedSequentialDct",
        [0xC2] = "StartOfFrameProgressiveDct",
        [0xC3] = "StartOfFrameLosslessSequential",
        [0xC4] = "DefineHuffmanTables",
        [0xC5] = "StartOfFrameDifferentialSequentialDct",
        [0xC6] = "StartOfFrameDifferentialProgressiveDct",
        [0xC7] = "StartOfFrameDifferentialLossless",
        [0xC8] = "JpegExtensions",
        [0xC9] = "StartOfFrameExtendedSequentialArithmetic",
        [0xCA] = "StartOfFrameProgressiveArithmetic",
        [0xCB] = "StartOfFrameLosslessArithmetic",
        [0xCC] = "DefineArithmeticCodingConditioning",
        [0xCD] = "StartOfFrameDifferentialSequentialArithmetic",
        [0xCE] = "StartOfFrameDifferentialProgressiveArithmetic",
        [0xCF] = "StartOfFrameDifferentialLosslessArithmetic",
        [0xD0] = "Restart0",
        [0xD1] = "Restart1",
        [0xD2] = "Restart2",
        [0xD3] = "Restart3",
        [0xD4] = "Restart4",
        [0xD5] = "Restart5",
        [0xD6] = "Restart6",
        [0xD7] = "Restart7",
        [0xD8] = "StartOfImage",
        [0xD9] = "EndOfImage",
        [0xDA] = "StartOfScan",
        [0xDB] = "DefineQuantizationTables",
        [0xDC] = "DefineNumberOfLines",
        [0xDD] = "DefineRestartInterval",
        [0xDE] = "DefineHierarchicalProgression",
        [0xDF] = "ExpandReferenceComponents",
        [0xE0] = "Application0",
        [0xE1] = "Application1",
        [0xE2] = "Application2",
        [0xE3] = "Application3",
        [0xE4] = "Application4",
        [0xE5] = "Application5",
        [0xE6] = "Application6",
        [0xE7] = "Application7",
        [0xE8] = "Application8",
        [0xE9] = "Application9",
        [0xEA] = "Application10",
        [0xEB] = "Application11",
        [0xEC] = "Application12",
        [0xED] = "Application13",
        [0xEE] = "Application14",
        [0xEF] = "Application15",
        [0xFE] = "Comment",
    };

    public static bool IsStandalone(byte code) =>
        code is Tem or Soi or Eoi or (>= 0xD0 and <= 0xD7);

    public static bool IsFrameHeader(byte code) =>
        code is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7
            or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    public static bool IsKnown(byte code) => Names.ContainsKey(code);

    public static bool IsApplication(byte code) => code is >= 0xE0 and <= 0xEF;

    public static string GetName(byte code) =>
        Names.TryGetValue(code, out var name) ? name : $"0xFF{code:X2}";

    public static string GetReadableName(byte code) =>
        ReadableNames.TryGetValue(code, out var name) ? name : "UnknownMarker";

    public static string GetTitle(byte code) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{GetReadableName(code)} '\\xFF\\x{code:X2}'");

    public static string FormatMarker(byte code) => $"FF{code:X2}";
}
