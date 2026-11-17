using System.Text;

namespace UniversalParser.Core.Formats.Psd;

/// <summary>
/// Constants from the Adobe Photoshop File Formats Specification (2019). PSD version 1 and PSB
/// version 2 share every section; PSB only widens a few length fields.
/// </summary>
internal static class PsdConstants
{
    public const int HeaderLength = 26;
    public const int ReservedLength = 6;
    public const int SignatureLength = 4;
    public const ushort VersionPsd = 1;
    public const ushort VersionPsb = 2;
    public const int CompressionFieldLength = 2;

    public static ReadOnlySpan<byte> Signature => "8BPS"u8;
    public static ReadOnlySpan<byte> ResourceSignature => "8BIM"u8;
    public static ReadOnlySpan<byte> AdditionalSignature => "8BIM"u8;
    public static ReadOnlySpan<byte> AdditionalLargeSignature => "8B64"u8;

    public static int LengthFieldSize(bool psb) => psb ? 8 : 4;

    /// <summary>
    /// PSB stores these additional-layer-information lengths as 8 bytes. Every other key, and
    /// every key in PSD, uses 4 bytes.
    /// </summary>
    public static bool UsesLongAdditionalLength(ReadOnlySpan<byte> key) =>
        key.SequenceEqual("LMsk"u8)
        || key.SequenceEqual("Lr16"u8)
        || key.SequenceEqual("Lr32"u8)
        || key.SequenceEqual("Layr"u8)
        || key.SequenceEqual("Mt16"u8)
        || key.SequenceEqual("Mt32"u8)
        || key.SequenceEqual("Mtrn"u8)
        || key.SequenceEqual("Alph"u8)
        || key.SequenceEqual("FMsk"u8)
        || key.SequenceEqual("lnk2"u8)
        || key.SequenceEqual("FEid"u8)
        || key.SequenceEqual("FXid"u8)
        || key.SequenceEqual("PxSD"u8);

    public static string FormatVersion(ushort version) => version switch
    {
        VersionPsd => "1 (PSD)",
        VersionPsb => "2 (PSB, large document format)",
        _ => version.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public static string FormatColorMode(ushort mode) => mode switch
    {
        0 => "0 (Bitmap)",
        1 => "1 (Grayscale)",
        2 => "2 (Indexed)",
        3 => "3 (RGB)",
        4 => "4 (CMYK)",
        7 => "7 (Multichannel)",
        8 => "8 (Duotone)",
        9 => "9 (Lab)",
        _ => mode.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public static string FormatCompression(ushort compression) => compression switch
    {
        0 => "0 (Raw)",
        1 => "1 (RLE)",
        2 => "2 (ZIP without prediction)",
        3 => "3 (ZIP with prediction)",
        _ => compression.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public static string FormatBlendMode(ReadOnlySpan<byte> key)
    {
        var name = key switch
        {
            _ when key.SequenceEqual("pass"u8) => "Pass Through",
            _ when key.SequenceEqual("norm"u8) => "Normal",
            _ when key.SequenceEqual("diss"u8) => "Dissolve",
            _ when key.SequenceEqual("dark"u8) => "Darken",
            _ when key.SequenceEqual("mul "u8) => "Multiply",
            _ when key.SequenceEqual("idiv"u8) => "Color Burn",
            _ when key.SequenceEqual("lbrn"u8) => "Linear Burn",
            _ when key.SequenceEqual("dkCl"u8) => "Darker Color",
            _ when key.SequenceEqual("lite"u8) => "Lighten",
            _ when key.SequenceEqual("scrn"u8) => "Screen",
            _ when key.SequenceEqual("div "u8) => "Color Dodge",
            _ when key.SequenceEqual("lddg"u8) => "Linear Dodge",
            _ when key.SequenceEqual("lgCl"u8) => "Lighter Color",
            _ when key.SequenceEqual("over"u8) => "Overlay",
            _ when key.SequenceEqual("sLit"u8) => "Soft Light",
            _ when key.SequenceEqual("hLit"u8) => "Hard Light",
            _ when key.SequenceEqual("vLit"u8) => "Vivid Light",
            _ when key.SequenceEqual("lLit"u8) => "Linear Light",
            _ when key.SequenceEqual("pLit"u8) => "Pin Light",
            _ when key.SequenceEqual("hMix"u8) => "Hard Mix",
            _ when key.SequenceEqual("diff"u8) => "Difference",
            _ when key.SequenceEqual("smud"u8) => "Exclusion",
            _ when key.SequenceEqual("fsub"u8) => "Subtract",
            _ when key.SequenceEqual("fdiv"u8) => "Divide",
            _ when key.SequenceEqual("hue "u8) => "Hue",
            _ when key.SequenceEqual("sat "u8) => "Saturation",
            _ when key.SequenceEqual("colr"u8) => "Color",
            _ when key.SequenceEqual("lum "u8) => "Luminosity",
            _ => null,
        };
        var code = Encoding.Latin1.GetString(key);
        return name is null ? code : $"{code} ({name})";
    }

    public static string FormatChannelId(short id) => id switch
    {
        >= 0 and <= 2 => id switch
        {
            0 => "0 (Red)",
            1 => "1 (Green)",
            _ => "2 (Blue)",
        },
        -1 => "-1 (Transparency mask)",
        -2 => "-2 (User supplied layer mask)",
        -3 => "-3 (Real user supplied layer mask)",
        _ => id.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public static string FormatAdditionalInfoKey(ReadOnlySpan<byte> key)
    {
        var name = key switch
        {
            _ when key.SequenceEqual("Layr"u8) => "Layer records",
            _ when key.SequenceEqual("Lr16"u8) => "16-bit layer records",
            _ when key.SequenceEqual("Lr32"u8) => "32-bit layer records",
            _ when key.SequenceEqual("LMsk"u8) => "Layer mask",
            _ when key.SequenceEqual("Mt16"u8) => "16-bit layer mask",
            _ when key.SequenceEqual("Mt32"u8) => "32-bit layer mask",
            _ when key.SequenceEqual("Mtrn"u8) => "Layer mask as real",
            _ when key.SequenceEqual("Alph"u8) => "Alpha channel",
            _ when key.SequenceEqual("FMsk"u8) => "Filter mask",
            _ when key.SequenceEqual("lnk2"u8) => "Linked layer",
            _ when key.SequenceEqual("lnkD"u8) => "Linked layer data",
            _ when key.SequenceEqual("lnk3"u8) => "Linked layer",
            _ when key.SequenceEqual("FEid"u8) => "Filter effects ID",
            _ when key.SequenceEqual("FXid"u8) => "Filter effects",
            _ when key.SequenceEqual("PxSD"u8) => "Pixel source data",
            _ when key.SequenceEqual("lspf"u8) => "Protected setting",
            _ when key.SequenceEqual("luni"u8) => "Unicode layer name",
            _ when key.SequenceEqual("lyid"u8) => "Layer ID",
            _ when key.SequenceEqual("lsct"u8) => "Section divider",
            _ when key.SequenceEqual("lsdk"u8) => "Section divider (new)",
            _ when key.SequenceEqual("clbl"u8) => "Blend clipping elements",
            _ when key.SequenceEqual("infx"u8) => "Blend interior elements",
            _ when key.SequenceEqual("knko"u8) => "Knockout setting",
            _ when key.SequenceEqual("lclr"u8) => "Sheet color",
            _ when key.SequenceEqual("fxrp"u8) => "Reference point",
            _ when key.SequenceEqual("lyvr"u8) => "Layer version",
            _ when key.SequenceEqual("tsly"u8) => "Transparency shapes layer",
            _ when key.SequenceEqual("lmgm"u8) => "Layer mask as global mask",
            _ when key.SequenceEqual("vmgm"u8) => "Vector mask as global mask",
            _ when key.SequenceEqual("vmsk"u8) => "Vector mask",
            _ when key.SequenceEqual("vsms"u8) => "Vector mask (new)",
            _ when key.SequenceEqual("vstk"u8) => "Vector stroke data",
            _ when key.SequenceEqual("vscg"u8) => "Vector stroke content",
            _ when key.SequenceEqual("TySh"u8) => "Type tool object",
            _ when key.SequenceEqual("Txt2"u8) => "Text engine data",
            _ when key.SequenceEqual("lrFX"u8) => "Effects layer",
            _ when key.SequenceEqual("lmfx"u8) => "Effects layer (new)",
            _ when key.SequenceEqual("lnsr"u8) => "Layer name source",
            _ when key.SequenceEqual("shmd"u8) => "Metadata",
            _ when key.SequenceEqual("CgEd"u8) => "Content generator extra data",
            _ when key.SequenceEqual("Patt"u8) => "Patterns",
            _ when key.SequenceEqual("Pat2"u8) => "Patterns (new)",
            _ when key.SequenceEqual("Pat3"u8) => "Patterns (newer)",
            _ when key.SequenceEqual("Anno"u8) => "Annotations",
            _ when key.SequenceEqual("cinf"u8) => "Count information",
            _ when key.SequenceEqual("iOpa"u8) => "Fill opacity",
            _ when key.SequenceEqual("SoCo"u8) => "Solid color adjustment",
            _ when key.SequenceEqual("GdFl"u8) => "Gradient fill adjustment",
            _ when key.SequenceEqual("PtFl"u8) => "Pattern fill adjustment",
            _ when key.SequenceEqual("brit"u8) => "Brightness/Contrast adjustment",
            _ when key.SequenceEqual("levl"u8) => "Levels adjustment",
            _ when key.SequenceEqual("curv"u8) => "Curves adjustment",
            _ when key.SequenceEqual("expA"u8) => "Exposure adjustment",
            _ when key.SequenceEqual("vibA"u8) => "Vibrance adjustment",
            _ when key.SequenceEqual("hue "u8) => "Hue/Saturation adjustment (Photoshop 4)",
            _ when key.SequenceEqual("hue2"u8) => "Hue/Saturation adjustment",
            _ when key.SequenceEqual("blnc"u8) => "Color Balance adjustment",
            _ when key.SequenceEqual("blwh"u8) => "Black and White adjustment",
            _ when key.SequenceEqual("phfl"u8) => "Photo Filter adjustment",
            _ when key.SequenceEqual("mixr"u8) => "Channel Mixer adjustment",
            _ when key.SequenceEqual("clrL"u8) => "Color Lookup adjustment",
            _ when key.SequenceEqual("nvrt"u8) => "Invert adjustment",
            _ when key.SequenceEqual("post"u8) => "Posterize adjustment",
            _ when key.SequenceEqual("thrs"u8) => "Threshold adjustment",
            _ when key.SequenceEqual("grdm"u8) => "Gradient Map adjustment",
            _ when key.SequenceEqual("selc"u8) => "Selective Color adjustment",
            _ => null,
        };
        var code = Encoding.Latin1.GetString(key);
        return name is null ? code : $"{code} ({name})";
    }

    public static string FormatSectionType(uint type) => type switch
    {
        0 => "0 (Any other type of layer)",
        1 => "1 (Open folder)",
        2 => "2 (Closed folder)",
        3 => "3 (Bounding section divider)",
        _ => type.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
