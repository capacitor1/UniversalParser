using System.Globalization;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

#pragma warning disable IDE0060 // Uniform box-parser signature keeps the dispatcher simple.

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>
/// Container boxes have no payload fields of their own beyond an optional FullBox or entry-count
/// prefix. The prefix is reported here so the tree and the detail pane agree on what was skipped.
/// </summary>
internal static class ContainerBoxes
{
    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        ["moov"] = "Movie 'moov'",
        ["trak"] = "Track 'trak'",
        ["edts"] = "Edit 'edts'",
        ["mdia"] = "Media 'mdia'",
        ["minf"] = "MediaInfo 'minf'",
        ["dinf"] = "DataInfo 'dinf'",
        ["stbl"] = "SampleTable 'stbl'",
        ["mvex"] = "MovieExtends 'mvex'",
        ["moof"] = "MovieFragment 'moof'",
        ["traf"] = "TrackFragment 'traf'",
        ["mfra"] = "MovieFragmentRandomAccess 'mfra'",
        ["meta"] = "Metadata 'meta'",
        ["udta"] = "UserData 'udta'",
        ["ilst"] = "ItemList 'ilst'",
        ["ipro"] = "ItemProtection 'ipro'",
        ["sinf"] = "ProtectionSchemeInfo 'sinf'",
        ["schi"] = "SchemeInfo 'schi'",
        ["rinf"] = "RestrictedSchemeInfo 'rinf'",
        ["iinf"] = "ItemInfo 'iinf'",
        ["fiin"] = "FileInfo 'fiin'",
        ["paen"] = "ProtectionAssociationEntry 'paen'",
        ["meco"] = "MovieExtensContainer 'meco'",
        ["iprp"] = "ItemProperties 'iprp'",
        ["ipco"] = "ItemPropertyContainer 'ipco'",
        ["----"] = "(QuickTime)FreeForm '----'",
        ["tapt"] = "(QuickTime)TrackApertureDimensions 'tapt'",
        ["dref"] = "DataReference 'dref'",
        ["iref"] = "ItemReference 'iref'",
        ["grpl"] = "GroupsList 'grpl'",
        ["tref"] = "TrackReference 'tref'",
        ["gmhd"] = "BaseMediaInformationHeader 'gmhd'",
        ["strk"] = "SubTrack 'strk'",
        ["strd"] = "SubTrackDefinition 'strd'",
        ["cinf"] = "ContainerInfo 'cinf'",
        ["trgr"] = "TrackGroups 'trgr'",
        ["hnti"] = "HintInfo 'hnti'",
        ["hinf"] = "HintStatistics 'hinf'",
        ["trep"] = "TrackExtensionProperties 'trep'",
        ["©nam"] = "(QuickTime)Title '©nam'",
        ["©ART"] = "(QuickTime)Artist '©ART'",
        ["©alb"] = "(QuickTime)Album '©alb'",
        ["©cmt"] = "(QuickTime)Comment '©cmt'",
        ["©day"] = "(QuickTime)ContentCreateDate '©day'",
        ["©gen"] = "(QuickTime)Genre '©gen'",
        ["©wrt"] = "(QuickTime)Composer '©wrt'",
        ["©too"] = "(QuickTime)Encoder '©too'",
        ["©cpy"] = "(QuickTime)Copyright '©cpy'",
        ["©des"] = "(QuickTime)Description '©des'",
        ["©dir"] = "(QuickTime)Director '©dir'",
        ["©prd"] = "(QuickTime)Producer '©prd'",
        ["©prf"] = "(QuickTime)Performers '©prf'",
        ["©inf"] = "(QuickTime)Information '©inf'",
        ["©swr"] = "(QuickTime)Software '©swr'",
        ["©src"] = "(QuickTime)Source '©src'",
        ["©enc"] = "(QuickTime)EncodedBy '©enc'",
        ["©lyr"] = "(QuickTime)Lyrics '©lyr'",
        ["©grp"] = "(QuickTime)Grouping '©grp'",
        ["©aut"] = "(QuickTime)Author '©aut'",
        ["©TIM"] = "(QuickTime)CreationTime '©TIM'",
        ["©TSC"] = "(QuickTime)CreationScale '©TSC'",
        ["©TSZ"] = "(QuickTime)TimeZoneOffset '©TSZ'",
    };

    public static string Parse(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = Titles.TryGetValue(header.Type, out var known)
            ? known
            : header.Type.StartsWith("key[", StringComparison.Ordinal)
                ? string.Create(CultureInfo.InvariantCulture, $"(QuickTime)ItemIndex '{header.Type}'")
                : string.Create(CultureInfo.InvariantCulture, $"UnknownContainer '{header.Type}'");

        switch (header.Type)
        {
            case "meta":
                ParseMeta(node, header, data, fields);
                break;
            case "iinf":
                ParseIinf(data);
                break;
            case "iref":
                data.TryFullBox(out _, out _);
                break;
            case "dref":
                ParseFullBoxWithCount(data, "entry_count", 32);
                break;
            case "ipro":
            case "fiin":
                ParseFullBoxWithCount(data, header.Type == "ipro" ? "protection_count" : "entry_count", 16);
                break;
            case "trep":
                data.TryFullBox(out _, out _);
                data.TryUInt32("track_ID", out _);
                break;
            default:
                fields.Note("Please select a subnode of this box.", node.Range);
                break;
        }

        return title;
    }

    private static void ParseMeta(
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var prefix = node.PayloadRange is { } payload ? payload.Offset - header.PayloadOffset : 0;
        if (prefix <= 0)
        {
            // Apple writes 'meta' without a FullBox header, so there is nothing to report.
            fields.Note("QuickTime-style 'meta' without a version/flags prefix.", node.Range);
            return;
        }

        data.TryFullBox(out _, out _);
        if (prefix > 4)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{prefix - 4} unexpected byte(s) precede the first child box."));
        }
    }

    private static void ParseIinf(IsoBmffBoxReader data)
    {
        if (!data.TryFullBox(out var version, out _))
        {
            return;
        }

        if (version == 0)
        {
            data.TryUInt16("entry_count", out _);
        }
        else
        {
            data.TryUInt32("entry_count", out _);
        }
    }

    private static void ParseFullBoxWithCount(IsoBmffBoxReader data, string countKey, int width)
    {
        if (!data.TryFullBox(out _, out _))
        {
            return;
        }

        if (width == 16)
        {
            data.TryUInt16(countKey, out _);
        }
        else
        {
            data.TryUInt32(countKey, out _);
        }
    }
}
