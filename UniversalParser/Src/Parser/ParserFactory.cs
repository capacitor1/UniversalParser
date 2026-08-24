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
            else if (MPEG.MPEGParser.IsValid(fileStream))
            {
                return new MPEG.MPEGParser(fileStream);
            }

            // TODO: Add other formats here when implemented
            // if (RIFF.RIFFParser.IsValid(fileStream)) { ... }
            // if (Matroska.MatroskaParser.IsValid(fileStream)) { ... }

            throw new NotSupportedException("Unknown or unsupported file format.");
        }
    }
}
