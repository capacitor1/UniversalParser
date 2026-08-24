using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using UniversalParser.Src.Parser.PNG.Chunks;

namespace UniversalParser.Src.Parser.PNG
{
    public static class PNGDispatcher
    {
        public static ParseResult Dispatch(PNGParser parser, Node node)
        {
            return node.NodeName switch
            {
                //general
                "IHDR" => IHDR.Parse(parser, node),
                "IDAT" => IDAT.Parse(parser, node),
                "IEND" => IEND.Parse(parser, node),
                "pHYs" => PHYs.Parse(parser, node),
                "tEXt" => TEXt.Parse(parser, node),
                "iTXt" => ITXt.Parse(parser, node),

                //color
                "sRGB" => SRGB.Parse(parser, node),
                "gAMA" => GAMA.Parse(parser, node),
                "iCCP" => ICCP.Parse(parser, node),
                "PLTE" => PLTE.Parse(parser, node),
                "tRNS" => TRNS.Parse(parser, node),
                "cICP" => CICP.Parse(parser, node),
                "mDCv" => MDCV.Parse(parser, node),
                "cLLi" => CLLI.Parse(parser, node),

                //misc
                "cHRM" => CHRM.Parse(parser, node),
                "bKGD" => BKGD.Parse(parser, node),
                "tIME" => TIME.Parse(parser, node),
                "zTXt" => ZTXt.Parse(parser, node),
                "sBIT" => SBIT.Parse(parser, node),
                "oFFs" => OFFs.Parse(parser, node),
                "eXIf" => EXIf.Parse(parser, node),
                "hIST" => HIST.Parse(parser, node),
                "sPLT" => SPLT.Parse(parser, node),

                //apng
                "acTL" => AcTL.Parse(parser, node),
                "fcTL" => FcTL.Parse(parser, node),
                "fdAT" => FdAT.Parse(parser, node),
                _ => Default.Parse(parser, node)

            };
        }
    }
}
