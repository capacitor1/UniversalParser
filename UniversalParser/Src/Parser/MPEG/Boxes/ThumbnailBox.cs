using System.Text;
using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;
namespace UniversalParser.Src.Parser.MPEG.Boxes;
// Thumbnail box 'thmb' inside 'udta' (3GPP TS 26.244, table 8.12c).
//
//   size     4 bytes
//   type     4 bytes   'thmb'
//   version  1 byte
//   flags    3 bytes
//   format   4 bytes   four-character code of the coding format
//   data     bytes to the end of the box, image data in the indicated format
//
// The same 4CC is also an item reference type inside 'iref' and a track reference
// type inside 'tref', so the containing box decides which parser applies.

internal static class Thmb
{
    public static ParseResult Parse(MPEGParser parser, Node node)
    {
        string? parentType = parser.GetParentType(node);

        if (parentType == "udta" || parentType == "meta")
            return ThumbnailBox.Parse(parser, node);

        if (parentType == "iref" || parentType == "tref")
            return ReferenceBox.Parse(parser, node, "thmb");

        // Parent unknown: the thumbnail box starts with a zero version and zero flags,
        // while a reference box starts with an identifier that is not zero.
        return StartsWithZeroFullBoxHeader(parser, node)
            ? ThumbnailBox.Parse(parser, node)
            : ReferenceBox.Parse(parser, node, "thmb");
    }

    private static bool StartsWithZeroFullBoxHeader(MPEGParser parser, Node node)
    {
        var fs = parser.FileStream;
        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        if (end - start < 12) return false;

        long origin = fs.Position;
        try
        {
            var buffer = new byte[4];
            fs.Position = start + 8;
            if (fs.Read(buffer, 0, 4) < 4) return false;

            return buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0 && buffer[3] == 0;
        }
        finally
        {
            fs.Position = origin;
        }
    }
}

internal static class ThumbnailBox
{
    private const int HexLimit = 64;

    public static ParseResult Parse(MPEGParser parser, Node node)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        const string title = "Thumbnail 'thmb'";

        fs.Position = start;
        Seek(reader, start);

        // ---- Box header: size/type were already validated by MPEGParser,
        //      so only the correct header length has to be skipped here.
        if (end - start < 8)
        {
            Warn(dataLines, $"Box is only {end - start} byte(s) long, too short for a box header.");
            return Build(parser, node, title, dataLines);
        }

        int headerSize = 8;
        uint size32 = reader.ReadUInt32BE();
        reader.ReadFourCC();

        if (size32 == 1 && end - start >= 16)
        {
            reader.ReadUInt64BE();
            headerSize = 16;
        }

        Seek(reader, start + headerSize);

        // ---- version / flags ----
        if (end - Pos(reader) < 4)
        {
            Warn(dataLines, "Cannot read version/flags: not enough bytes.");
            return Build(parser, node, title, dataLines);
        }

        byte version = reader.ReadByte();
        byte f1 = reader.ReadByte();
        byte f2 = reader.ReadByte();
        byte f3 = reader.ReadByte();

        uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

        dataLines.Add(("version", version.ToString()));
        dataLines.Add(("flags", $"0x{flags:X6}"));

        // ---- format ----
        if (end - Pos(reader) < 4)
        {
            Warn(dataLines, $"Cannot read format: {end - Pos(reader)} byte(s) remaining, 4 required.");
            return Build(parser, node, title, dataLines);
        }

        byte c0 = reader.ReadByte();
        byte c1 = reader.ReadByte();
        byte c2 = reader.ReadByte();
        byte c3 = reader.ReadByte();

        // Shown as both a four-character code and raw bytes, because a writer that omits
        // this field puts the start of the image data here instead.
        string fourCC = new string(new[] { Printable(c0), Printable(c1), Printable(c2), Printable(c3) });
        dataLines.Add(("format", $"{fourCC} (0x{c0:X2}{c1:X2}{c2:X2}{c3:X2})"));

        // ---- image data ----
        long dataLength = end - Pos(reader);
        dataLines.Add(("data_size", dataLength.ToString()));

        if (dataLength > 0)
            dataLines.Add(("data", ReadHex(reader, end, dataLength)));

        return Build(parser, node, title, dataLines);
    }

    private static char Printable(byte b) => (b >= 0x20 && b <= 0x7E) ? (char)b : '.';

    private static string ReadHex(MpegReader reader, long limit, long length)
    {
        long take = Math.Min(Math.Min(length, limit - Pos(reader)), HexLimit);
        if (take <= 0) return string.Empty;

        var sb = new StringBuilder((int)take * 3);
        for (long i = 0; i < take; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(reader.ReadByte().ToString("X2"));
        }

        if (length > take)
            sb.Append($" ... ({length} bytes total)");

        return sb.ToString();
    }

    private static void Seek(MpegReader reader, long pos) => reader.Seek(pos, SeekOrigin.Begin);
    private static long Pos(MpegReader reader) => (long)reader.Position;
    private static void Warn(List<(string K, string V)> lines, string msg) => lines.Add(("warning", msg));

    private static ParseResult Build(MPEGParser parser, Node node, string title,
                                     List<(string K, string V)> dataLines)
        => new ParseResult
        {
            Title = title,
            Position = node.Position,
            Length = node.Length,
            DataLines = dataLines,
            RawData = new OffsetStream(parser.FileStream, (long)node.Position, (long)node.Length)
        };
}