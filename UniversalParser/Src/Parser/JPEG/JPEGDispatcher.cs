using System;
using System.Collections.Generic;
using System.Text;
using UniversalParser.Src.Parser.JPEG.Chunks;

namespace UniversalParser.Src.Parser.JPEG
{
    internal class JPEGDispatcher
    {
        public static ParseResult Dispatch(JPEGParser parser, Node node)
        {
            return node.NodeName switch
            {
                //general
                "FFD0" or "FFD1" or "FFD2" or "FFD3" or "FFD4" or "FFD5" or "FFD6" or "FFD7" or "FFD8" or "FFD9" => FFD0_FFD9.Parse(parser, node),
                "SCAN" => SCAN.Parse(parser, node),

                //
                "FFE0" or "FFE2" or "FFE3" or "FFE4" or "FFE5" or "FFE6" or "FFE7" or "FFE8" or "FFE9" or "FFEA" or "FFEB" or "FFEC" or "FFED" => FFE0_FFED.Parse(parser, node),
                "FFEE" => FFEE.Parse(parser, node),
                "FFE1" => FFE1.Parse(parser, node),
                "FFDB" => FFDB.Parse(parser, node),
                "FFC0" => FFC0.Parse(parser, node),
                "FFDD" => FFDD.Parse(parser, node),
                "FFC4" => FFC4.Parse(parser, node),
                "FFDA" => FFDA.Parse(parser, node),
                "FFFE" => FFFE.Parse(parser, node),
                "FFC1" => FFC1.Parse(parser, node),
                "FFC2" => FFC2.Parse(parser, node),
                "FFC3" => FFC3.Parse(parser, node),
                _ => Default.Parse(parser, node)
            };
        }
    }
}
