namespace UniversalParser.Core.Formats.IsoBmff;

/// <summary>
/// Four-character codes handled by the ISOBMFF field parsers. Keeping the list in one place lets
/// the tree builder distinguish a known leaf from a genuinely unparsed box without duplicating the
/// dispatcher switch. Container display titles live in <c>Boxes/ContainerBoxes.Titles</c>.
/// </summary>
internal static class IsoBmffBoxCatalog
{
    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        // General
        "ftyp", "styp", "uuid", "mdat", "free", "skip", "wide", "Xtra",

        // Movie, track and media
        "mvhd", "tkhd", "mdhd", "hdlr", "elst", "vmhd", "smhd", "url ", "urn ", "alis",
        "iods", "mehd", "mfhd", "tfhd", "tfdt", "trun", "trex", "sidx", "data",

        // Sample tables
        "stsd", "stts", "stsc", "stsz", "stco", "co64", "stss", "sdtp", "ctts", "sbgp",
        "sgpd", "cslg",

        // Item references
        "dimg", "base", "eroi", "evir", "exbl", "mask", "mint", "pred", "prem", "rpds",
        "auxr", "fdel", "auxl", "cdsc", "thmb",

        // Track references
        "hint", "hind", "sync", "subt", "scal", "sbas", "tmcd", "adda", "adrc", "avcp",
        "swfr", "swto", "deps", "cdtg", "shsc", "cdep",

        // Entity groups
        "altr", "ster", "pymd", "brst", "eqiv", "iaug", "tsyn", "pano", "slid", "albc",
        "favc", "aebr", "afbr", "dobr", "fobr", "wbbr", "prsl", "stem", "rgpa", "acgl",
        "amgl", "opeg", "swtk", "vvcb", "oval", "ovbg", "vipo", "eply", "swpc",

        // QuickTime metadata item atoms
        "desc", "ldes", "catg", "purl", "purd", "tvsh", "tven", "tvnn", "egid", "aART",
        "keyw", "©nam", "©ART", "©alb", "©cmt", "©day", "©gen", "©wrt", "©too", "©cpy",
        "©des", "©dir", "©prd", "©prf", "©inf", "©swr", "©src", "©enc", "©lyr", "©grp",
        "©aut", "©TIM", "©TSC", "©TSZ",

        // QuickTime media and item atoms
        "gmin", "text", "tcmi", "stik", "hdvd", "mean", "covr", "clef", "prof", "enof",
        "csgm", "keys",

        // HEIF / AVIF
        "pitm", "iloc", "idat", "infe", "colr", "hvcC", "ispe", "irot", "pixi", "ipma",
        "auxC", "imir", "clli", "av1C",

        // 3GPP
        "titl", "dscp", "perf", "albm", "auth", "gnre", "cprt", "coll", "kywd", "clfn",
        "reel", "scen", "shot", "slno", "manu", "modl", "cmid", "swre", "date", "name",
    };

    public static bool IsKnown(string type, string? parentType)
    {
        if (KnownTypes.Contains(type))
        {
            return true;
        }

        return parentType == "ilst";
    }
}
