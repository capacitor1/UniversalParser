using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// ID3v2 tag parser shared by container chunks such as RIFF <c>id3 </c>. The tag body is read
/// once, bounded by <see cref="ParseLimits.MaxBlobBytes"/>; frame decoding itself never throws and
/// reports truncation through the field list.
/// </summary>
internal static class Id3Parser
{
    private const int TagHeaderSize = 10;
    private const int TagFooterSize = 10;
    private const string FrameArrayName = "frame";

    public static void Parse(
        IRandomAccessSource source,
        long offset,
        long length,
        ParseLimits limits,
        FieldList fields,
        string prefix,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(limits);

        if (length < TagHeaderSize)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"ID3v2 tag is {length} byte(s); at least {TagHeaderSize} are required.",
                new SourceRange(offset, length));
            return;
        }

        Span<byte> header = stackalloc byte[TagHeaderSize];
        var headerRead = SourceRead.ReadAtMost(source, offset, header);
        if (headerRead < TagHeaderSize)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                "ID3v2 tag header is truncated.",
                new SourceRange(offset, headerRead));
            return;
        }

        if (header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3')
        {
            fields.Warning(
                DiagnosticCodes.InvalidSignature,
                "The embedded tag does not start with the \"ID3\" identifier.",
                new SourceRange(offset, TagHeaderSize));
            return;
        }

        var major = header[3];
        var flags = header[5];
        var declaredSize = Id3Number.SyncSafe32(header[6..]);
        var hasFooter = major >= 4 && (flags & Id3Flags.TagFooterPresent) != 0;
        var total = TagHeaderSize + (long)declaredSize + (hasFooter ? TagFooterSize : 0);
        var available = Math.Min(length, total);
        var cap = Math.Min(available, Math.Min(limits.MaxStringBytes, int.MaxValue));
        var bytes = new byte[(int)cap];
        cancellationToken.ThrowIfCancellationRequested();
        var read = SourceRead.ReadAtMost(source, offset, bytes);
        if (read < bytes.Length)
        {
            Array.Resize(ref bytes, read);
        }

        var lines = new List<(string Key, string Value)>();
        Parse(bytes, lines, prefix);
        foreach (var (key, value) in lines)
        {
            fields.Raw(key, value);
        }

        if (available > cap)
        {
            fields.Note(
                $"ID3v2 tag is {available:N0} byte(s); only the first {cap:N0} byte(s) were decoded because of the configured blob limit.",
                new SourceRange(offset, length));
        }
    }

    /// <summary>Decodes one complete ID3v2 tag into ordered key/value pairs.</summary>
    public static void Parse(
        ReadOnlySpan<byte> tag,
        List<(string Key, string Value)> lines,
        string keyPrefix = "")
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (tag.Length < TagHeaderSize
            || tag[0] != (byte)'I'
            || tag[1] != (byte)'D'
            || tag[2] != (byte)'3')
        {
            return;
        }

        var sink = new Id3Sink(lines, keyPrefix);
        var major = tag[3];
        var revision = tag[4];
        var flags = tag[5];
        var declaredSize = Id3Number.SyncSafe32(tag.Slice(6, 4));

        sink.Text("file_identifier", "ID3");
        sink.Number("version_major", major);
        sink.Number("version_revision", revision);
        sink.Verbatim("flags", Id3Flags.TagFlags(flags, major));
        sink.Number("size", declaredSize);

        // Unknown major versions are decoded with the closest newer layout.
        var version = major > 4 ? (byte)4 : major;
        if (major > 4)
        {
            sink.Verbatim("parse_note", "Unknown ID3v2 major version, decoded with the ID3v2.4 layout.");
        }

        var hasFooter = version >= 4 && (flags & Id3Flags.TagFooterPresent) != 0;
        var usable = Math.Max(0, tag.Length - TagHeaderSize - (hasFooter ? TagFooterSize : 0));
        var bodySize = Math.Min(declaredSize, usable);
        if (bodySize < declaredSize)
        {
            sink.Verbatim("parse_note", "Declared tag size exceeds the supplied buffer, only available bytes are decoded.");
        }

        var stored = tag.Slice(TagHeaderSize, bodySize);

        // ID3v2.2 has no defined whole-tag compression scheme; the spec says to ignore the tag.
        if (version <= 2 && (flags & Id3Flags.TagCompressionV22) != 0)
        {
            sink.Payload("compressed_data", stored);
            return;
        }

        // v2.2/v2.3 unsynchronisation applies to the whole body; v2.4 applies per frame.
        var restored = version <= 3 && (flags & Id3Flags.TagUnsynchronisation) != 0
            ? Id3Unsynchronisation.Restore(stored)
            : null;
        ReadOnlySpan<byte> body = restored ?? stored;

        var offset = 0;
        if (version >= 3 && (flags & Id3Flags.TagExtendedHeader) != 0)
        {
            offset += ParseExtendedHeader(body[offset..], version, sink);
        }

        var frameSink = sink.Key(FrameArrayName);
        offset += Id3FrameReader.ReadFrames(body[offset..], version, frameSink.Path, lines);

        var padding = 0;
        while (offset + padding < body.Length && body[offset + padding] == 0x00)
        {
            padding++;
        }

        if (padding > 0)
        {
            sink.Number("padding_size", padding);
        }

        offset += padding;
        if (offset < body.Length)
        {
            sink.Payload("unparsed_data", body[offset..]);
        }

        if (hasFooter)
        {
            ParseFooter(tag, declaredSize, sink);
        }
    }

    private static int ParseExtendedHeader(ReadOnlySpan<byte> body, byte version, Id3Sink parent)
    {
        var sink = parent.Scope("extended_header");
        var cursor = new Id3Cursor(body);

        if (version == 3)
        {
            var size = Id3Number.ToLength(cursor.ReadUInt(4));
            sink.Number("size", size);
            var extendedFlags = (ushort)cursor.ReadUInt(2);
            sink.Verbatim("flags", Id3Flags.ExtendedFlagsV3(extendedFlags));
            sink.Number("padding_size", Id3Number.ToLength(cursor.ReadUInt(4)));
            if ((extendedFlags & Id3Flags.ExtendedV3CrcDataPresent) != 0)
            {
                sink.Verbatim("crc_data", Id3Format.Hex(cursor.ReadUInt(4), 8));
            }

            return Clamp(4 + size, cursor.Position, body.Length);
        }

        var totalSize = cursor.ReadSyncSafe32();
        sink.Number("size", totalSize);
        var flagBytes = cursor.ReadByte();
        sink.Number("number_of_flag_bytes", flagBytes);
        var flags = flagBytes > 0 ? cursor.ReadByte() : (byte)0;
        if (flagBytes > 1)
        {
            cursor.ReadBytes(flagBytes - 1);
        }

        sink.Verbatim("flags", Id3Flags.ExtendedFlagsV4(flags));

        // Each set flag is followed by a length byte and its data, in flag order.
        if ((flags & Id3Flags.ExtendedV4TagIsAnUpdate) != 0)
        {
            cursor.ReadBytes(cursor.ReadByte());
        }

        if ((flags & Id3Flags.ExtendedV4CrcDataPresent) != 0)
        {
            var crc = cursor.ReadBytes(cursor.ReadByte());
            sink.Verbatim("total_frame_crc", Id3Format.Hex(Id3Number.SyncSafe32(crc), 8));
        }

        if ((flags & Id3Flags.ExtendedV4TagRestrictions) != 0)
        {
            var data = cursor.ReadBytes(cursor.ReadByte());
            if (!data.IsEmpty)
            {
                sink.Verbatim("restrictions", Id3Flags.Restrictions(data[0]));
            }
        }

        return Clamp(totalSize, cursor.Position, body.Length);
    }

    private static void ParseFooter(ReadOnlySpan<byte> tag, int declaredSize, Id3Sink parent)
    {
        var offset = TagHeaderSize + declaredSize;
        if (offset + TagFooterSize > tag.Length)
        {
            offset = tag.Length - TagFooterSize;
        }

        if (offset < TagHeaderSize)
        {
            return;
        }

        var footer = tag.Slice(offset, TagFooterSize);
        var sink = parent.Scope("footer");
        var identifier = Id3String.Decode(footer[..3], Id3String.EncodingIso88591);
        sink.Text("file_identifier", identifier == "3DI" ? identifier : identifier + " (expected 3DI)");
        sink.Number("version_major", footer[3]);
        sink.Number("version_revision", footer[4]);
        sink.Verbatim("flags", Id3Flags.TagFlags(footer[5], footer[3]));
        sink.Number("size", Id3Number.SyncSafe32(footer.Slice(6, 4)));
    }

    private static int Clamp(int declared, int consumed, int limit) =>
        declared < consumed || declared > limit ? Math.Min(consumed, limit) : declared;
}
