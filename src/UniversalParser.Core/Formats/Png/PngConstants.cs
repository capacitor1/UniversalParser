using System.Collections.Frozen;
namespace UniversalParser.Core.Formats.Png;

/// <summary>
/// PNG container constants. Chunk types are compared with ordinal, case-sensitive matching because
/// the PNG naming conventions encode critical/ancillary and public/private flags in letter case.
/// </summary>
internal static class PngConstants
{
    public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public const int SignatureLength = 8;
    public const int ChunkHeaderLength = 8;
    public const int ChunkOverhead = 12;

    /// <summary>PNG four-byte integers are limited to 2^31 - 1 by the specification.</summary>
    public const uint MaxChunkLength = 0x7FFF_FFFF;

    private static readonly FrozenDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["IHDR"] = "ImageHeader",
        ["PLTE"] = "Palette",
        ["IDAT"] = "ImageData",
        ["IEND"] = "ImageTrailer",
        ["tRNS"] = "Transparency",
        ["cHRM"] = "PrimaryChromaticities",
        ["gAMA"] = "ImageGamma",
        ["iCCP"] = "EmbeddedIccProfile",
        ["sBIT"] = "SignificantBits",
        ["sRGB"] = "StandardRgb",
        ["cICP"] = "CodingIndependentCodePoints",
        ["mDCV"] = "MasteringDisplayColorVolume",
        ["cLLI"] = "ContentLightLevelInformation",
        ["bKGD"] = "BackgroundColor",
        ["hIST"] = "ImageHistogram",
        ["pHYs"] = "PhysicalPixelDimensions",
        ["sPLT"] = "SuggestedPalette",
        ["eXIf"] = "ExifProfile",
        ["tIME"] = "LastModificationTime",
        ["tEXt"] = "TextualData",
        ["zTXt"] = "CompressedTextualData",
        ["iTXt"] = "InternationalTextualData",
        ["acTL"] = "AnimationControl",
        ["fcTL"] = "FrameControl",
        ["fdAT"] = "FrameData",
        ["oFFs"] = "ImageOffset",
    }.ToFrozenDictionary();

    public static bool IsKnown(string chunkType) => Names.ContainsKey(chunkType);

    public static string GetName(string chunkType) =>
        Names.TryGetValue(chunkType, out var name) ? name : "UnknownChunk";

    public static string GetTitle(string chunkType) =>
        $"{GetName(chunkType)} '{PngFormatting.FormatChunkType(chunkType)}'";
}
