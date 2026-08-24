using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Default
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var fs = parser.FileStream;

            // 确保位置合法
            if (fs.Length < (long)(node.Position + 8))
                throw new InvalidDataException("Box is truncated.");

            // 创建大端读取器
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            // 读取 box header
            uint size = reader.ReadUInt32BE();
            string type = reader.ReadFourCC(); //

            // 准备 DataLines
            var dataLines = new List<(string K, string V)>
            {
                ("<unknown_data>", "Please see raw dump below.")
            };

            return new ParseResult
            {
                
                Title = $"UnknownBox '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
    internal static class Boxes
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            //meta
            if(node.NodeName == "meta")
            {
                return Meta.Parse(parser, node);
            }

            var fs = parser.FileStream;

            // 确保位置合法
            if (fs.Length < (long)(node.Position + 8))
                throw new InvalidDataException("Box is truncated.");

            // 创建大端读取器
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            // 读取 box header
            uint size = reader.ReadUInt32BE();
            string type = reader.ReadFourCC(); //

            // 准备 DataLines
            var dataLines = new List<(string K, string V)>
            {
                ("<container_box>", "Please select a subnode of this box.")
            };

            return new ParseResult
            {
                
                Title = type switch
                {
                    "moov" => "Movie 'moov'",
                    "trak" => "Track 'trak'",
                    "edts" => "Edit 'edts'",
                    "mdia" => "Media 'mdia'",
                    "minf" => "MediaInfo 'minf'",
                    "dinf" => "DataInfo 'dinf'",
                    "stbl" => "SampleTable 'stbl'",
                    "mvex" => "MovieExtends 'mvex'",
                    "moof" => "MovieFragment 'moof'",
                    "traf" => "TrackFragment 'traf'",
                    "mfra" => "MovieFragmentRandomAccess 'mfra'",
                    "meta" => "Metadata 'meta'",
                    "udta" => "UserData 'udta'",
                    "ilst" => "ItemList 'ilst'",
                    "ipro" => "ItemProtection 'ipro'",
                    "sinf" => "ProtectionSchemeInfo 'sinf'",
                    "schi" => "SchemeInfo 'schi'",
                    "iinf" => "ItemInfo 'iinf'",
                    "fiin" => "FileInfo 'fiin'",
                    "paen" => "ProtectionAssociationEntry 'paen'",
                    "meco" => "MovieExtensContainer 'meco'",
                    "mere" => "MetaboxRelation 'mere'",
                    "iprp" => "ItemProperties 'iprp'",
                    "ipco" => "ItemPropertyContainer 'ipco'",
                    "----" => "(QuickTime)FreeForm '----'",
                    "tapt" => "(QuickTime)TrackApertureDimensions 'tapt'",
                    "dref" => "DataReference 'dref'",
                    "iref" => "ItemReference 'iref'",
                    "grpl" => "GroupsList 'grpl'",
                    "tref" => "TrackReference 'tref'",
                    "gmhd" => "BaseMediaInformationHeader 'gmhd'",
                    "covr" => "(QuickTime)Cover 'covr'",
                    "tkrn" => "(QuickTime)TKRN 'tkrn'",//no information for tkrn now
                    
                    _ => GetUnknownStr(type)
                },
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }

        private static string GetUnknownStr(string t)
        {
            if (t.StartsWith(@"\x00"))//first byte of apple qt
            {
                return $"(QuickTime) ItemIndex '(int)0x{t.Replace(@"\x", "")}'";
            }
            else
            {
                return $"UnknownContainer '{t}'";
            }
        }
    }
}