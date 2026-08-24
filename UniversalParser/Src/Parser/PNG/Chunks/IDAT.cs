using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.PNG;

/// <summary>
/// IDAT - Image Data Chunk (compressed image stream)
/// </summary>
internal static class IDAT
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

        if (type != "IDAT")
            throw new InvalidDataException($"Expected IDAT but got '{type}'");

        var crcResult = PNGCRCValidator.Validate(fs, pos, length);

        var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
                ("CompressedSize", length.ToString())
            };

        return new ParseResult
        {
            Title = "ImageData 'IDAT'",
            Position = node.Position,
            Length = node.Length,
            DataLines = dataLines,
            RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
        };
    }
}