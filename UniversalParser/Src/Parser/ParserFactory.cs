using System;
using System.IO;
using UniversalParser.Src.Parser;

namespace UniversalParser.Src.Parser
{
    // Factory that selects an appropriate IParser implementation
    // Uses IsValid() static method on each parser class for detection
    public static class ParserFactory
    {
        public static IParser CreateParser(FileStream fileStream)
        {
            if (fileStream == null || !fileStream.CanRead)
                throw new ArgumentException("FileStream must be readable.", nameof(fileStream));

            // Try each parser's IsValid method
            // Order matters: try more specific formats first

            if (RIFF.RIFFParser.IsValid(fileStream))
            {
                return new RIFF.RIFFParser(fileStream);
            }
            else if (PNG.PNGParser.IsValid(fileStream))
            {
                return new PNG.PNGParser(fileStream);
            }
            else if (JPEG.JPEGParser.IsValid(fileStream))
            {
                return new JPEG.JPEGParser(fileStream);
            }
            else if (EBML.EBMLParser.IsValid(fileStream))
            {
                return new EBML.EBMLParser(fileStream);
            }
            else if (FLV.FLVParser.IsValid(fileStream))
            {
                return new FLV.FLVParser(fileStream);
            }
            else if (MPEG.MPEGParser.IsValid(fileStream))
            {
                return new MPEG.MPEGParser(fileStream);
            }
            else if (ASF.ASFParser.IsValid(fileStream))
            {
                return new ASF.ASFParser(fileStream);
            }
            else if (FBX.FBXParser.IsValid(fileStream))
            {
                return new FBX.FBXParser(fileStream);
            }
            else if (RawData.RawDataParser.IsValid(fileStream))
            {
                return new RawData.RawDataParser(fileStream);
            }
            else
            {
                throw new NotSupportedException("failed to read.");
            }
        }
    }
}
