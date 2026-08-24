using UniversalParser.Src.Parser;
using UniversalParser.Src.Parser.MPEG;

// SingleItemTypeReferenceBox -- children of 'iref' (ISO/IEC 14496-12).
// Plain box, not a full box; the 4CC is the reference type itself.
//
//   size              4 bytes
//   type              4 bytes
//   from_item_ID      2 or 4 bytes
//   reference_count   2 or 4 bytes
//   to_item_ID        2 or 4 bytes, repeated reference_count times
//
// The field width is 2 bytes when the parent 'iref' has version 0 and 4 bytes when it
// has version 1. The parent version is skipped as a container prefix and is therefore
// not available here, so the width is inferred from the box size: only one of the two
// layouts can account for the payload exactly.
namespace UniversalParser.Src.Parser.MPEG.Boxes;
internal static class SingleItemTypeReference
{
    public static ParseResult Parse(MPEGParser parser, Node node, string fourCC)
    {
        var fs = parser.FileStream;
        var reader = new MpegReader(fs);

        long start = (long)node.Position;
        long end = Math.Min(start + (long)node.Length, fs.Length);

        var dataLines = new List<(string K, string V)>();
        string title = $"SingleItemTypeReferenceBox '{fourCC}'";

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

        long payload = start + headerSize;
        long payloadLength = end - payload;

        // ---- Infer the field width from the payload length ----
        bool fits16 = false;
        bool fits32 = false;

        if (payloadLength >= 4)
        {
            Seek(reader, payload);
            reader.ReadUInt16BE();               // from_item_ID under the 16-bit layout
            uint count16 = reader.ReadUInt16BE();
            fits16 = payloadLength == 4L + 2L * count16;
        }

        if (payloadLength >= 8)
        {
            Seek(reader, payload);
            reader.ReadUInt32BE();               // from_item_ID under the 32-bit layout
            uint count32 = reader.ReadUInt32BE();
            fits32 = payloadLength == 8L + 4L * count32;
        }

        int idBytes;

        if (fits16)
        {
            idBytes = 2;
            if (fits32)
                Warn(dataLines, "Both the 16-bit and the 32-bit layout account for the box size; parsed as 16-bit.");
        }
        else if (fits32)
        {
            idBytes = 4;
        }
        else if (payloadLength >= 4)
        {
            idBytes = 2;
            Warn(dataLines, $"Payload of {payloadLength} byte(s) matches neither the 16-bit nor the " +
                            "32-bit layout; parsed as 16-bit.");
        }
        else
        {
            Warn(dataLines, $"Cannot read from_item_ID/reference_count: {payloadLength} byte(s) remaining, 4 required.");
            return Build(parser, node, title, dataLines);
        }

        // ---- Read the fields ----
        Seek(reader, payload);

        uint fromItemId;
        uint referenceCount;

        if (idBytes == 2)
        {
            fromItemId = reader.ReadUInt16BE();
            referenceCount = reader.ReadUInt16BE();
        }
        else
        {
            fromItemId = reader.ReadUInt32BE();
            referenceCount = reader.ReadUInt32BE();
        }

        dataLines.Add(("id_size", idBytes == 2 ? "16-bit" : "32-bit"));
        dataLines.Add(("from_item_ID", fromItemId.ToString()));
        dataLines.Add(("reference_count", referenceCount.ToString()));

        for (uint i = 0; i < referenceCount; i++)
        {
            if (end - Pos(reader) < idBytes)
            {
                Warn(dataLines, $"Read {i} of {referenceCount} to_item_ID value(s); the box ends early.");
                break;
            }

            uint toItemId = idBytes == 2 ? reader.ReadUInt16BE() : reader.ReadUInt32BE();
            dataLines.Add(($"to_item_ID[{i}]", toItemId.ToString()));
        }

        long unread = end - Pos(reader);
        if (unread > 0)
            Warn(dataLines, $"{unread} trailing byte(s) were not interpreted.");

        return Build(parser, node, title, dataLines);
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