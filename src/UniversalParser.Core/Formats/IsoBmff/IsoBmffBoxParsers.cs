using UniversalParser.Core.Formats.IsoBmff.Boxes;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.IsoBmff;

/// <summary>Routes a box to its field parser and returns the display title.</summary>
internal static class IsoBmffBoxParsers
{
    public static string Dispatch(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var type = header.Type;

        if (node.Kind == NodeKind.Container)
        {
            return type switch
            {
                "covr" => QuickTimeBoxes.Cover(context, node, header, data, fields),
                "stik" => QuickTimeBoxes.StoreItemKind(context, node, header, data, fields),
                "hdvd" => QuickTimeBoxes.Hdvd(context, node, header, data, fields),
                _ => ContainerBoxes.Parse(context, node, header, data, fields),
            };
        }

        return type switch
        {
            // General
            "ftyp" => GeneralBoxes.Ftyp(context, node, header, data, fields),
            "styp" => GeneralBoxes.Styp(context, node, header, data, fields),
            "uuid" => GeneralBoxes.Uuid(context, node, header, data, fields),
            "mdat" => GeneralBoxes.Mdat(context, node, header, data, fields),
            "free" => GeneralBoxes.Free(context, node, header, data, fields),
            "skip" => GeneralBoxes.Skip(context, node, header, data, fields),
            "wide" => GeneralBoxes.Wide(context, node, header, data, fields),
            "Xtra" => GeneralBoxes.Xtra(context, node, header, data, fields),

            // Movie / track / media
            "mvhd" => MovieBoxes.Mvhd(context, node, header, data, fields),
            "tkhd" => MovieBoxes.Tkhd(context, node, header, data, fields),
            "mdhd" => MovieBoxes.Mdhd(context, node, header, data, fields),
            "hdlr" => MovieBoxes.Hdlr(context, node, header, data, fields),
            "elst" => MovieBoxes.Elst(context, node, header, data, fields),
            "vmhd" => MovieBoxes.Vmhd(context, node, header, data, fields),
            "smhd" => MovieBoxes.Smhd(context, node, header, data, fields),
            "url " => MovieBoxes.DataEntry(context, node, header, data, fields, isUrn: false),
            "urn " => MovieBoxes.DataEntry(context, node, header, data, fields, isUrn: true),
            "alis" => MovieBoxes.Alis(context, node, header, data, fields),
            "iods" => MovieBoxes.Iods(context, node, header, data, fields),
            "gmin" => MovieBoxes.Gmin(context, node, header, data, fields),
            "text" => MovieBoxes.TextMediaInformation(context, node, header, data, fields),
            "tcmi" => MovieBoxes.Tcmi(context, node, header, data, fields),
            "clef" => MovieBoxes.Aperture(context, node, header, data, fields, "(QuickTime)TrackCleanApertureDimensions 'clef'"),
            "prof" => MovieBoxes.Aperture(context, node, header, data, fields, "(QuickTime)TrackProductionApertureDimensions 'prof'"),
            "enof" => MovieBoxes.Aperture(context, node, header, data, fields, "(QuickTime)TrackEncodedPixelsDimensions 'enof'"),
            "csgm" => MovieBoxes.Csgm(context, node, header, data, fields),

            // Sample tables
            "stsd" => SampleTableBoxes.Stsd(context, node, header, data, fields),
            "stts" => SampleTableBoxes.Stts(context, node, header, data, fields),
            "stsc" => SampleTableBoxes.Stsc(context, node, header, data, fields),
            "stsz" => SampleTableBoxes.Stsz(context, node, header, data, fields),
            "stco" => SampleTableBoxes.Stco(context, node, header, data, fields),
            "co64" => SampleTableBoxes.Co64(context, node, header, data, fields),
            "stss" => SampleTableBoxes.Stss(context, node, header, data, fields),
            "sdtp" => SampleTableBoxes.Sdtp(context, node, header, data, fields),
            "ctts" => SampleTableBoxes.Ctts(context, node, header, data, fields),
            "sbgp" => SampleTableBoxes.Sbgp(context, node, header, data, fields),
            "sgpd" => SampleTableBoxes.Sgpd(context, node, header, data, fields),
            "cslg" => SampleTableBoxes.Cslg(context, node, header, data, fields),

            // Fragments
            "mehd" => FragmentBoxes.Mehd(context, node, header, data, fields),
            "mfhd" => FragmentBoxes.Mfhd(context, node, header, data, fields),
            "trex" => FragmentBoxes.Trex(context, node, header, data, fields),
            "tfdt" => FragmentBoxes.Tfdt(context, node, header, data, fields),
            "tfhd" => FragmentBoxes.Tfhd(context, node, header, data, fields),
            "trun" => FragmentBoxes.Trun(context, node, header, data, fields),
            "sidx" => FragmentBoxes.Sidx(context, node, header, data, fields),

            // HEIF / AVIF item metadata
            "pitm" => MetaBoxes.Pitm(context, node, header, data, fields),
            "iloc" => MetaBoxes.Iloc(context, node, header, data, fields),
            "idat" => MetaBoxes.Idat(context, node, header, data, fields),
            "infe" => MetaBoxes.Infe(context, node, header, data, fields),
            "ispe" => MetaBoxes.Ispe(context, node, header, data, fields),
            "irot" => MetaBoxes.Irot(context, node, header, data, fields),
            "imir" => MetaBoxes.Imir(context, node, header, data, fields),
            "pixi" => MetaBoxes.Pixi(context, node, header, data, fields),
            "colr" => MetaBoxes.Colr(context, node, header, data, fields),
            "auxC" => MetaBoxes.AuxC(context, node, header, data, fields),
            "av1C" => MetaBoxes.Av1C(context, node, header, data, fields),
            "hvcC" => MetaBoxes.HvcC(context, node, header, data, fields),
            "clli" => MetaBoxes.Clli(context, node, header, data, fields),
            "ipma" => MetaBoxes.Ipma(context, node, header, data, fields),

            // QuickTime metadata and text atoms
            "keys" => QuickTimeBoxes.Keys(context, node, header, data, fields),
            "mean" => QuickTimeBoxes.Mean(context, node, header, data, fields),
            "name" => QuickTimeBoxes.Name(context, node, header, data, fields),
            "data" => QuickTimeBoxes.Data(context, node, header, data, fields),
            "kywd" => QuickTimeBoxes.Kywd(context, node, header, data, fields),
            "covr" => QuickTimeBoxes.Cover(context, node, header, data, fields),
            "stik" => QuickTimeBoxes.StoreItemKind(context, node, header, data, fields),
            "hdvd" => QuickTimeBoxes.Hdvd(context, node, header, data, fields),
            "thmb" => QuickTimeBoxes.Thmb(context, node, header, data, fields),
            "clfn" => QuickTimeBoxes.PlainText(context, node, header, data, fields, "clfn", "ClipFileName"),
            "reel" => QuickTimeBoxes.PlainText(context, node, header, data, fields, "reel", "TapeReelName"),
            "scen" => QuickTimeBoxes.PlainText(context, node, header, data, fields, "scen", "SceneName"),
            "shot" => QuickTimeBoxes.PlainText(context, node, header, data, fields, "shot", "ShotName"),
            "slno" => QuickTimeBoxes.PlainText(context, node, header, data, fields, "slno", "CameraSerialNumber"),
            "manu" => QuickTimeBoxes.PlainText(context, node, header, data, fields, "manu", "CameraManufacturer"),
            "modl" => QuickTimeBoxes.PlainText(context, node, header, data, fields, "modl", "CameraModel"),
            "cmid" => QuickTimeBoxes.PlainText(context, node, header, data, fields, "cmid", "CameraIdentifier"),
            "swre" => QuickTimeBoxes.PlainText(context, node, header, data, fields, "swre", "Software"),
            "date" => QuickTimeBoxes.PlainText(context, node, header, data, fields, "date", "ContentCreateDate"),
            "desc" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "desc", "Description"),
            "ldes" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "ldes", "LongDescription"),
            "catg" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "catg", "Category"),
            "purl" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "purl", "PodcastURL"),
            "purd" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "purd", "PurchaseDate"),
            "tvsh" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "tvsh", "TVShow"),
            "tven" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "tven", "TVEpisodeID"),
            "tvnn" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "tvnn", "TVNetworkName"),
            "egid" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "egid", "EpisodeGlobalUniqueID"),
            "aART" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "aART", "AlbumArtist"),
            "keyw" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "keyw", "Keyword"),
            "©nam" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©nam", "Title"),
            "©ART" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©ART", "Artist"),
            "©alb" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©alb", "Album"),
            "©cmt" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©cmt", "Comment"),
            "©day" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©day", "ContentCreateDate"),
            "©gen" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©gen", "Genre"),
            "©wrt" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©wrt", "Composer"),
            "©too" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©too", "Encoder"),
            "©cpy" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©cpy", "Copyright"),
            "©des" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©des", "Description"),
            "©dir" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©dir", "Director"),
            "©prd" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©prd", "Producer"),
            "©prf" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©prf", "Performers"),
            "©inf" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©inf", "Information"),
            "©swr" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©swr", "Software"),
            "©src" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©src", "Source"),
            "©enc" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©enc", "EncodedBy"),
            "©lyr" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©lyr", "Lyrics"),
            "©grp" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©grp", "Grouping"),
            "©aut" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©aut", "Author"),
            "©TIM" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©TIM", "CreationTime"),
            "©TSC" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©TSC", "CreationScale"),
            "©TSZ" => QuickTimeBoxes.MetadataText(context, node, header, data, fields, "©TSZ", "TimeZoneOffset"),

            // 3GPP asset boxes
            "titl" => ThreeGppBoxes.Asset(context, node, header, data, fields, "Title 'titl'", "title", false),
            "dscp" => ThreeGppBoxes.Asset(context, node, header, data, fields, "Description 'dscp'", "description", false),
            "perf" => ThreeGppBoxes.Asset(context, node, header, data, fields, "Performer 'perf'", "performer", false),
            "auth" => ThreeGppBoxes.Asset(context, node, header, data, fields, "Author 'auth'", "author", false),
            "gnre" => ThreeGppBoxes.Asset(context, node, header, data, fields, "Genre 'gnre'", "genre", false),
            "cprt" => ThreeGppBoxes.Asset(context, node, header, data, fields, "Copyright 'cprt'", "copyright", false),
            "coll" => ThreeGppBoxes.Asset(context, node, header, data, fields, "CollectionName 'coll'", "name", false),
            "albm" => ThreeGppBoxes.Asset(context, node, header, data, fields, "Album 'albm'", "album_title", true),

            // References and groups
            "dimg" or "base" or "eroi" or "evir" or "exbl" or "mask" or "mint" or "pred" or "prem"
                or "rpds" or "auxr" or "fdel" or "auxl" or "cdsc"
                => ReferenceBoxes.ItemReference(context, node, header, data, fields, type),
            "hint" or "hind" or "sync" or "subt" or "scal" or "sbas" or "tmcd" or "adda" or "adrc"
                or "avcp" or "swfr" or "swto" or "deps" or "cdtg" or "shsc" or "cdep"
                => ReferenceBoxes.TrackReference(context, node, header, data, fields, type),
            "altr" or "ster" or "pymd" or "brst" or "eqiv" or "iaug" or "tsyn" or "pano" or "slid"
                or "albc" or "favc" or "aebr" or "afbr" or "dobr" or "fobr" or "wbbr" or "prsl"
                or "stem" or "rgpa" or "acgl" or "amgl" or "opeg" or "swtk" or "vvcb" or "oval"
                or "ovbg" or "vipo" or "eply" or "swpc"
                => MetaBoxes.EntityToGroup(context, node, header, data, fields, type),

            _ => GeneralBoxes.Unknown(context, node, header, data, fields),
        };
    }
}
