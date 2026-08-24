using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using UniversalParser.Src.Parser.MPEG.Boxes;

namespace UniversalParser.Src.Parser.MPEG
{
    public static class MPEGBoxDispatcher
    {
        public static ParseResult Dispatch(MPEGParser parser, Node node)
        {
            return node.NodeName switch
            {
                //general
                "ftyp" => Ftyp.Parse(parser, node),
                "uuid" => Uuid.Parse(parser, node),
                "mdat" => Mdat.Parse(parser, node),
                "free" => Free.Parse(parser, node),
                "skip" => Skip.Parse(parser, node),

                //video media metadatas
                "mvhd" => Mvhd.Parse(parser, node),
                "tkhd" => Tkhd.Parse(parser, node),
                "mdhd" => Mdhd.Parse(parser, node),
                "hdlr" => Hdlr.Parse(parser, node),
                "elst" => Elst.Parse(parser, node),
                "vmhd" => Vmhd.Parse(parser, node),
                "url " => DataEntryBox.Parse(parser, node,false),
                "urn " => DataEntryBox.Parse(parser, node,true),
                "wide" => Wide.Parse(parser, node),
                "alis" => Alis.Parse(parser, node),

                //video main data
                "stsd" => Stsd.Parse(parser, node),
                "stts" => Stts.Parse(parser, node),
                "stsc" => Stsc.Parse(parser, node),
                "stsz" => Stsz.Parse(parser, node),
                "stco" => Stco.Parse(parser, node),
                "co64" => Co64.Parse(parser, node),
                "stss" => Stss.Parse(parser, node),
                "sdtp" => Sdtp.Parse(parser, node),
                "ctts" => Ctts.Parse(parser, node),
                "sbgp" => Sbgp.Parse(parser, node),
                "sgpd" => Sgpd.Parse(parser, node),
                "cslg" => Cslg.Parse(parser, node),

                //misc
                "smhd" => Smhd.Parse(parser, node),
                "Xtra" => Xtra.Parse(parser, node),
                "iods" => Iods.Parse(parser, node),
                
                // ---- Item references (iref) ----
                "dimg" => SingleItemTypeReference.Parse(parser, node, "dimg"),
                "base" => SingleItemTypeReference.Parse(parser, node, "base"),
                "eroi" => SingleItemTypeReference.Parse(parser, node, "eroi"),
                "evir" => SingleItemTypeReference.Parse(parser, node, "evir"),
                "exbl" => SingleItemTypeReference.Parse(parser, node, "exbl"),
                "mask" => SingleItemTypeReference.Parse(parser, node, "mask"),
                "mint" => SingleItemTypeReference.Parse(parser, node, "mint"),
                "pred" => SingleItemTypeReference.Parse(parser, node, "pred"),
                "prem" => SingleItemTypeReference.Parse(parser, node, "prem"),
                "rpds" => SingleItemTypeReference.Parse(parser, node, "rpds"),
                "auxr" => SingleItemTypeReference.Parse(parser, node, "auxr"),
                "fdel" => SingleItemTypeReference.Parse(parser, node, "fdel"),
                "auxl" => SingleItemTypeReference.Parse(parser, node, "auxl"),
                "cdsc" => SingleItemTypeReference.Parse(parser, node, "cdsc"),
                
                // ---- Track references (tref) ----
                "hint" => TrackReferenceType.Parse(parser, node, "hint"),
                "hind" => TrackReferenceType.Parse(parser, node, "hind"),
                "sync" => TrackReferenceType.Parse(parser, node, "sync"),
                "subt" => TrackReferenceType.Parse(parser, node, "subt"),
                "scal" => TrackReferenceType.Parse(parser, node, "scal"),
                "sbas" => TrackReferenceType.Parse(parser, node, "sbas"),
                "tmcd" => TrackReferenceType.Parse(parser, node, "tmcd"),
                "adda" => TrackReferenceType.Parse(parser, node, "adda"),
                "adrc" => TrackReferenceType.Parse(parser, node, "adrc"),
                "avcp" => TrackReferenceType.Parse(parser, node, "avcp"),
                "swfr" => TrackReferenceType.Parse(parser, node, "swfr"),
                "swto" => TrackReferenceType.Parse(parser, node, "swto"),
                "deps" => TrackReferenceType.Parse(parser, node, "deps"),
                "cdtg" => TrackReferenceType.Parse(parser, node, "cdtg"),
                "shsc" => TrackReferenceType.Parse(parser, node, "shsc"),
                "cdep" => TrackReferenceType.Parse(parser, node, "cdep"),
                
                // ---- Entity groups (grpl) ----
                "altr" => EntityToGroup.Parse(parser, node, "altr"),
                "ster" => EntityToGroup.Parse(parser, node, "ster"),
                "pymd" => EntityToGroup.Parse(parser, node, "pymd"),
                "brst" => EntityToGroup.Parse(parser, node, "brst"),
                "eqiv" => EntityToGroup.Parse(parser, node, "eqiv"),
                "iaug" => EntityToGroup.Parse(parser, node, "iaug"),
                "tsyn" => EntityToGroup.Parse(parser, node, "tsyn"),
                "pano" => EntityToGroup.Parse(parser, node, "pano"),
                "slid" => EntityToGroup.Parse(parser, node, "slid"),
                "albc" => EntityToGroup.Parse(parser, node, "albc"),
                "favc" => EntityToGroup.Parse(parser, node, "favc"),
                "aebr" => EntityToGroup.Parse(parser, node, "aebr"),
                "afbr" => EntityToGroup.Parse(parser, node, "afbr"),
                "dobr" => EntityToGroup.Parse(parser, node, "dobr"),
                "fobr" => EntityToGroup.Parse(parser, node, "fobr"),
                "wbbr" => EntityToGroup.Parse(parser, node, "wbbr"),
                "prsl" => EntityToGroup.Parse(parser, node, "prsl"),
                "stem" => EntityToGroup.Parse(parser, node, "stem"),
                "rgpa" => EntityToGroup.Parse(parser, node, "rgpa"),
                "acgl" => EntityToGroup.Parse(parser, node, "acgl"),
                "amgl" => EntityToGroup.Parse(parser, node, "amgl"),
                "opeg" => EntityToGroup.Parse(parser, node, "opeg"),
                "swtk" => EntityToGroup.Parse(parser, node, "swtk"),
                "vvcb" => EntityToGroup.Parse(parser, node, "vvcb"),
                "oval" => EntityToGroup.Parse(parser, node, "oval"),
                "ovbg" => EntityToGroup.Parse(parser, node, "ovbg"),
                "vipo" => EntityToGroup.Parse(parser, node, "vipo"),
                "eply" => EntityToGroup.Parse(parser, node, "eply"),
                "swpc" => EntityToGroup.Parse(parser, node, "swpc"),
                
                //qt metadata
                "desc"      => MetadataText.Parse(parser, node, "desc", "Description"),
                "ldes"      => MetadataText.Parse(parser, node, "ldes", "LongDescription"),
                "catg"      => MetadataText.Parse(parser, node, "catg", "Category"),
                "purl"      => MetadataText.Parse(parser, node, "purl", "PodcastURL"),
                "purd"      => MetadataText.Parse(parser, node, "purd", "PurchaseDate"),
                "tvsh"      => MetadataText.Parse(parser, node, "tvsh", "TVShow"),
                "tven"      => MetadataText.Parse(parser, node, "tven", "TVEpisodeID"),
                "tvnn"      => MetadataText.Parse(parser, node, "tvnn", "TVNetworkName"),
                "egid"      => MetadataText.Parse(parser, node, "egid", "EpisodeGlobalUniqueID"),
                "aART"      => MetadataText.Parse(parser, node, "aART", "AlbumArtist"),
                "keyw"      => MetadataText.Parse(parser, node, "keyw", "Keyword"),
                "\u00A9nam" => MetadataText.Parse(parser, node, "\u00A9nam", "Title"),
                "\u00A9ART" => MetadataText.Parse(parser, node, "\u00A9ART", "Artist"),
                "\u00A9alb" => MetadataText.Parse(parser, node, "\u00A9alb", "Album"),
                "\u00A9cmt" => MetadataText.Parse(parser, node, "\u00A9cmt", "Comment"),
                "\u00A9day" => MetadataText.Parse(parser, node, "\u00A9day", "ContentCreateDate"),
                "\u00A9gen" => MetadataText.Parse(parser, node, "\u00A9gen", "Genre"),
                "\u00A9wrt" => MetadataText.Parse(parser, node, "\u00A9wrt", "Composer"),
                "\u00A9too" => MetadataText.Parse(parser, node, "\u00A9too", "Encoder"),
                "\u00A9cpy" => MetadataText.Parse(parser, node, "\u00A9cpy", "Copyright"),
                "\u00A9des" => MetadataText.Parse(parser, node, "\u00A9des", "Description"),
                "\u00A9dir" => MetadataText.Parse(parser, node, "\u00A9dir", "Director"),
                "\u00A9prd" => MetadataText.Parse(parser, node, "\u00A9prd", "Producer"),
                "\u00A9prf" => MetadataText.Parse(parser, node, "\u00A9prf", "Performers"),
                "\u00A9inf" => MetadataText.Parse(parser, node, "\u00A9inf", "Information"),
                "\u00A9swr" => MetadataText.Parse(parser, node, "\u00A9swr", "Software"),
                "\u00A9src" => MetadataText.Parse(parser, node, "\u00A9src", "Source"),
                "\u00A9enc" => MetadataText.Parse(parser, node, "\u00A9enc", "EncodedBy"),
                "\u00A9lyr" => MetadataText.Parse(parser, node, "\u00A9lyr", "Lyrics"),
                "\u00A9grp" => MetadataText.Parse(parser, node, "\u00A9grp", "Grouping"),
                "\u00A9aut" => MetadataText.Parse(parser, node, "\u00A9aut", "Author"),
                "\u00A9TIM" => MetadataText.Parse(parser, node, "\u00A9TIM", "CreationTime"),
                "\u00A9TSC" => MetadataText.Parse(parser, node, "\u00A9TSC", "CreationScale"),
                "\u00A9TSZ" => MetadataText.Parse(parser, node, "\u00A9TSZ", "TimeZoneOffset"),
                
                //gmhd -- QuickTime base media information header (container)
                "gmin" => Gmin.Parse(parser, node),                   // base media info
                "text" => TextMediaInformation.Parse(parser, node),   // text media information, matrix only
                "tcmi" => Tcmi.Parse(parser, node),                   // timecode media information
                
                //apple qt
                "stik" => Stik.Parse(parser, node),
                "hdvd" => Hdvd.Parse(parser, node),
                "mean" => Mean.Parse(parser, node),
                "data" => Data.Parse(parser, node),
                "covr" => Covr.Parse(parser, node),
                "clef" => Clef.Parse(parser, node),
                "prof" => Prof.Parse(parser, node),
                "enof" => Enof.Parse(parser, node),
                "csgm" => Csgm.Parse(parser, node),
                "keys" => Keys.Parse(parser, node),

                //fmp4 video
                "mfhd" => Mfhd.Parse(parser, node),
                "tfhd" => Tfhd.Parse(parser, node),
                "tfdt" => Tfdt.Parse(parser, node),
                "trun" => Trun.Parse(parser, node),
                "mehd" => Mehd.Parse(parser, node),
                "trex" => Trex.Parse(parser, node),
                "sidx" => Sidx.Parse(parser, node),

                //heic image
                "pitm" => Pitm.Parse(parser, node),
                "iref" => Iref.Parse(parser, node),
                "iloc" => Iloc.Parse(parser, node),
                "idat" => Idat.Parse(parser, node),
                "infe" => Infe.Parse(parser, node),
                "colr" => Colr.Parse(parser, node),
                "hvcC" => HvcC.Parse(parser, node),
                "ispe" => Ispe.Parse(parser, node),
                "irot" => Irot.Parse(parser, node),
                "pixi" => Pixi.Parse(parser, node),
                "ipma" => Ipma.Parse(parser, node),
                "auxC" => AuxC.Parse(parser, node),
                "imir" => Imir.Parse(parser, node),
                
                //3gpp
                "titl" => Titl.Parse(parser, node),
                "dscp" => Dscp.Parse(parser, node),
                "perf" => Perf.Parse(parser, node),
                "albm" => Albm.Parse(parser, node),
                "auth" => Auth.Parse(parser, node),
                "gnre" => Gnre.Parse(parser, node),
                "cprt" => Cprt.Parse(parser, node),
                "coll" => Coll.Parse(parser, node),
                "thmb" => Thmb.Parse(parser, node),
                "kywd" => Kywd.Parse(parser, node),
                "clfn" => PlainTextBox.Parse(parser, node, "clfn", "ClipFileName"),
                "reel" => PlainTextBox.Parse(parser, node, "reel", "TapeReelName"),
                "scen" => PlainTextBox.Parse(parser, node, "scen", "SceneName"),
                "shot" => PlainTextBox.Parse(parser, node, "shot", "ShotName"),
                "slno" => PlainTextBox.Parse(parser, node, "slno", "CameraSerialNumber"),
                "manu" => PlainTextBox.Parse(parser, node, "manu", "CameraManufacturer"),
                "modl" => PlainTextBox.Parse(parser, node, "modl", "CameraModel"),
                "cmid" => PlainTextBox.Parse(parser, node, "cmid", "CameraIdentifier"),
                "swre" => PlainTextBox.Parse(parser, node, "swre", "Software"),
                "date" => PlainTextBox.Parse(parser, node, "date", "ContentCreateDate"),
                "name" => PlainTextBox.Parse(parser, node, "name", "Name"),

                //avif image
                "clli" => Clli.Parse(parser, node),
                "av1C" => Av1C.Parse(parser, node),
                _ => Default.Parse(parser, node)
            };
        }
    }
}
