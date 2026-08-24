using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.PNG;

/// <summary>
/// IHDR - Image Header Chunk (must appear first)
/// </summary>
internal static class IHDR
{
    public static ParseResult Parse(PNGParser parser, Node node)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(node);

        var fs = parser.FileStream;
        var reader = new PngReader(fs);

        long pos = (long)node.Position;
        reader.Seek(pos);

        uint length = reader.ReadUInt32BE();
        string type = reader.ReadFourCC();

        if (type != "IHDR")
            throw new InvalidDataException($"Expected IHDR but got '{type}'");

        // IHDR fixed 13 bytes
        uint width = reader.ReadUInt32BE();
        uint height = reader.ReadUInt32BE();

        byte bitDepth = reader.ReadByte();
        byte colorType = reader.ReadByte();
        byte compression = reader.ReadByte();
        byte filter = reader.ReadByte();
        byte interlace = reader.ReadByte();

        var crcResult = PNGCRCValidator.Validate(fs, pos, length);

        var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("Width", width.ToString()),
                ("Height", height.ToString()),
                ("BitDepth", bitDepth.ToString()),
                ("ColorType", $"{colorType} ({GetColorType(colorType)})"),
                ("Compression", $"{compression} ({GetCompType(compression)})"),
                ("Filter", filter.ToString()),
                ("Interlace", interlace.ToString())
            };

        return new ParseResult
        {
            Title = "ImageHeader 'IHDR'",
            Position = node.Position,
            Length = node.Length,
            DataLines = dataLines,
            RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
        };
    }

    private static string GetColorType(byte type) => type switch
    {
        0 => "Grayscale",
        2 => "Truecolor",
        3 => "Indexed-color",
        4 => "Grayscale + Alpha",
        6 => "Truecolor + Alpha",
        _ => "Unknown"
    };
    private static string GetCompType(byte type) => type switch
    {
        0 => "Deflate/Inflate",
        _ => "Unknown"
    };
}