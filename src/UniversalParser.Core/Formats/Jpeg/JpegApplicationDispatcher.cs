using System.Buffers;
using System.Collections.Frozen;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Embedded;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Jpeg;

/// <summary>
/// APP marker numbers describe the envelope, not its contents. Register a handler here when an
/// embedded format is supported; a recognised signature without a handler keeps its body unparsed.
/// </summary>
internal static class JpegApplicationDispatcher
{
    private static readonly FrozenDictionary<string, ApplicationFormat> Formats = new ApplicationFormat[]
    {
        new("Exif", "Exif metadata", ParseExif),
        new("ICC_PROFILE", "ICC profile", ParseIccProfile),
        new("Photoshop 3.0", "Photoshop image resources (8BIM)", ParsePhotoshop),
        new("Adobe", "Adobe application data", NullTerminated: false),
        new("http://ns.adobe.com/xap/1.0/", "Adobe XMP document", (context, fields, data) => XmpText.Parse(context, fields, data)),
        new("JFIF", "JPEG File Interchange Format"),
        new("JFXX", "JFIF extension"),
    }.ToFrozenDictionary(static format => format.Identifier, StringComparer.Ordinal);

    private static readonly int LongestSignature = Formats.Values.Max(static format => format.Signature.Length);

    public static bool HasParser(ParseContext context, SourceRange payload)
    {
        context.ThrowIfCancellationRequested();
        // Only inspect registered signatures while building the tree. The full Identifier and
        // embedded metadata are read lazily when the user selects the node.
        Span<byte> prefix = stackalloc byte[LongestSignature];
        var count = checked((int)Math.Min(payload.Length, Math.Min(LongestSignature, context.Limits.MaxStringBytes)));
        var read = SourceRead.ReadAtMost(context.Source, payload.Offset, prefix[..count]);
        foreach (var format in Formats.Values)
        {
            if (format.Handler is not null && prefix[..read].StartsWith(format.Signature))
            {
                return true;
            }
        }

        return false;
    }

    public static void Parse(ParseContext context, FieldList fields, SourceRange payload)
    {
        var identifier = ReadIdentifier(context, payload);
        if (identifier.Range.Length > 0)
        {
            var terminated = identifier.Range.Length > identifier.Value.Length;
            var value = JpegFormatting.EscapeText(identifier.Value) + (terminated ? "\\0" : string.Empty);
            fields.Raw("Identifier", value, identifier.Range);
        }

        if (!identifier.Complete)
        {
            if (identifier.LimitReached)
            {
                fields.Warning(
                    DiagnosticCodes.LimitString,
                    $"The APP identifier exceeds the {context.Limits.MaxStringBytes}-byte string limit.",
                    identifier.Range);
            }
            else
            {
                // Some APPn segments (for example APP12 picture info) do not carry a
                // null-terminated namespace at all, so an unterminated prefix is informational.
                fields.Note(
                    "The APP payload has no NUL-terminated identifier; the segment is kept unparsed.",
                    payload);
            }

            KeepUnparsed(fields, payload);
            return;
        }

        var body = SourceRange.FromBounds(identifier.Range.End, payload.End);
        if (Formats.TryGetValue(identifier.Value, out var format))
        {
            fields.Derived("<ApplicationFormat>", format.Description, identifier.Range);
            if (format.Handler is { } handler)
            {
                handler(context, fields, new BigEndianReader(context.Source, body.Offset, body.Length));
                return;
            }
        }

        KeepUnparsed(fields, body);
    }

    private static IdentifierRead ReadIdentifier(ParseContext context, SourceRange payload)
    {
        var maximum = Math.Min(payload.Length, context.Limits.MaxStringBytes);
        var bytes = new ArrayBufferWriter<byte>();
        Span<byte> buffer = stackalloc byte[256];
        var consumed = 0L;
        while (consumed < maximum)
        {
            context.ThrowIfCancellationRequested();
            var count = checked((int)Math.Min(buffer.Length, maximum - consumed));
            var read = SourceRead.ReadAtMost(context.Source, payload.Offset + consumed, buffer[..count]);
            if (read == 0)
            {
                break;
            }

            if (consumed == 0)
            {
                foreach (var format in Formats.Values)
                {
                    // Adobe TN 5116: "Adobe" is five bytes, followed immediately by a uint16
                    // version. Its usual leading zero is not a string terminator.
                    if (!format.NullTerminated && buffer[..read].StartsWith(format.Signature))
                    {
                        return new IdentifierRead(
                            format.Identifier, new SourceRange(payload.Offset, format.Signature.Length), true, false);
                    }
                }
            }

            var terminator = buffer[..read].IndexOf((byte)0);
            var textLength = terminator >= 0 ? terminator : read;
            bytes.Write(buffer[..textLength]);
            consumed += textLength;
            if (terminator >= 0)
            {
                return new IdentifierRead(
                    Encoding.Latin1.GetString(bytes.WrittenSpan),
                    new SourceRange(payload.Offset, consumed + 1),
                    true,
                    false);
            }
        }

        return new IdentifierRead(
            Encoding.Latin1.GetString(bytes.WrittenSpan),
            new SourceRange(payload.Offset, consumed),
            false,
            consumed == maximum && maximum < payload.Length);
    }

    private static void ParseExif(ParseContext context, FieldList fields, BigEndianReader data)
    {
        // The common string reader consumed "Exif\0"; Exif adds one more zero before TIFF.
        var remaining = data.CurrentRange;
        if (!data.TryByte(out var padding))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The Exif header is missing its padding byte.", remaining);
            KeepUnparsed(fields, remaining);
            return;
        }

        if (padding != 0)
        {
            fields.Warning(DiagnosticCodes.InvalidSignature, "The Exif identifier must be followed by a second zero byte.", data.LastRange);
            KeepUnparsed(fields, remaining);
            return;
        }

        fields.Derived("<ExifPayloadLength>", data.Remaining, data.CurrentRange);
        ExifParser.Parse(
            context.Source,
            data.Position,
            data.Remaining,
            context.Limits,
            fields,
            string.Empty,
            context.CancellationToken);
    }

    private static void ParsePhotoshop(ParseContext context, FieldList fields, BigEndianReader data) =>
        PhotoshopImageResources.ParseList(context, fields, data);

    private static void ParseIccProfile(ParseContext context, FieldList fields, BigEndianReader data) =>
        JpegIccProfile.Parse(context, fields, data);

    private static void KeepUnparsed(FieldList fields, SourceRange range) =>
        fields.Derived("<UnparsedLength>", range.Length, range);

    private sealed record ApplicationFormat(
        string Identifier,
        string Description,
        Action<ParseContext, FieldList, BigEndianReader>? Handler = null,
        bool NullTerminated = true)
    {
        public byte[] Signature { get; } = Encoding.ASCII.GetBytes(Identifier + (NullTerminated ? "\0" : string.Empty));
    }

    private readonly record struct IdentifierRead(string Value, SourceRange Range, bool Complete, bool LimitReached);
}
