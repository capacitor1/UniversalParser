using UniversalParser.Src.Parser;

using UniversalParser.Src.Parser.PNG;

/// <summary>
/// IEND - Image End Chunk (marks end of PNG stream)
/// </summary>
internal static class IEND
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

        if (type != "IEND")
            throw new InvalidDataException($"Expected IEND but got '{type}'");

        if (length != 0)
            throw new InvalidDataException("IEND chunk must have zero length");

        var crcResult = PNGCRCValidator.Validate(fs, pos, length);

        var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult),
            };

        return new ParseResult
        {
            Title = "ImageEnd 'IEND'",
            Position = node.Position,
            Length = node.Length,
            DataLines = dataLines,
            RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
        };
    }
}