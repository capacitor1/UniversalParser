using System.Globalization;
using System.Text;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

#pragma warning disable IDE0060 // Uniform box-parser signature keeps the dispatcher simple.

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>QuickTime / iTunes metadata atoms, plain text atoms and the 3GPP thumbnail box.</summary>
internal static class QuickTimeBoxes
{
    public static string Keys(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "MetadataItemKeysAtom 'keys'";
        if (!data.TryFullBox(out _, out _) || !data.TryUInt32("entry_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryReadUInt32Raw(out var keySize))
            {
                break;
            }

            if (keySize < 8 || keySize - 8 > data.Remaining)
            {
                fields.Warning(
                    DiagnosticCodes.InvalidLength,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Key entry {index} declares key_size {keySize} but only {data.Remaining} byte(s) remain."));
                data.SkipRemaining();
                break;
            }

            if (!data.TryReadFourCcRaw(out var keyNamespace))
            {
                break;
            }

            var value = data.ReadBlock(keySize - 8, context.Limits.MaxStringBytes, out _);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"key[{index + 1}]"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"namespace={keyNamespace} name={IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(value))}"));
        }

        data.RequireEmpty("metadata key table");
        return title;
    }

    public static string Mean(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"Mean '{header.Type}'");
        data.TryUInt32("locale", out _);
        var payload = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out _, out _);
        fields.Raw("domain", IsoBmffFormatting.Display(IsoBmffFormatting.ReadNullTerminatedText(payload, Encoding.UTF8)));
        return title;
    }

    public static string Name(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"Name '{header.Type}'");
        var payload = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out _, out _);
        fields.Raw("name", IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(payload)));
        return title;
    }

    public static string Data(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"(QuickTime)Data '{header.Type}'");
        ParseDataPayload(context, data, fields, string.Empty);
        return title;
    }

    public static string Kywd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "KeywordsBox 'kywd'";
        var payload = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out var payloadLength, out _);
        if (payloadLength == 0)
        {
            return title;
        }

        // A leading run of four zero bytes is a FullBox header; a UTF-8 string never starts with NUL.
        var offset = payload.Length >= 4 && payload[0] == 0 && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            ? 4
            : 0;

        if (offset == 0)
        {
            fields.Raw("text", IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(payload)));
            return title;
        }

        var reader = new IsoBmffBoxReader(context.Source, data.Position - payloadLength + offset, payloadLength - offset, fields);
        reader.TryFullBox(out _, out _);
        if (!reader.TryUInt16("language_and_pad", out var language))
        {
            return title;
        }

        fields.Derived("<language>", IsoBmffFieldHelpers.Language((ushort)(language & 0x7FFF)));
        if (!reader.TryByte("keyword_count", out var keywordCount))
        {
            return title;
        }

        for (var index = 0; index < keywordCount; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!reader.TryByte("keyword_size", out var size))
            {
                break;
            }

            var text = reader.ReadBlock(size, context.Limits.MaxStringBytes, out _);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"keyword[{index}]"),
                IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(text)));
        }

        return title;
    }

    public static string Thmb(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var parent = IsoBmffNodeParser.GetParentType(node);
        if (parent == "iref")
        {
            return ReferenceBoxes.ItemReference(context, node, header, data, fields, "thmb");
        }

        if (parent == "tref")
        {
            return ReferenceBoxes.TrackReference(context, node, header, data, fields, "thmb");
        }

        if (parent is not ("udta" or "meta") && data.Remaining >= 4)
        {
            // No usable parent context: a thumbnail starts with a zero version/flags pair.
            Span<byte> probe = stackalloc byte[4];
            var start = data.Position;
            if (data.TryReadBytesRaw(probe))
            {
                var looksLikeThumbnail = probe[0] == 0 && probe[1] == 0 && probe[2] == 0 && probe[3] == 0;
                data = new IsoBmffBoxReader(context.Source, start, header.PayloadLength, fields);
                if (!looksLikeThumbnail)
                {
                    return ReferenceBoxes.ItemReference(context, node, header, data, fields, "thmb");
                }
            }
        }

        return Thumbnail(context, header, data, fields);
    }

    private static string Thumbnail(
        ParseContext context,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "Thumbnail 'thmb'";
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        if (data.TryFourCc("format", out var format))
        {
            fields.Raw("format", string.Create(CultureInfo.InvariantCulture, $"{format}"));
        }

        var image = data.ReadRemainingPrefix(64, out var dataSize, out _);
        fields.Derived("<data_size>", dataSize);
        if (dataSize > 0)
        {
            fields.Raw("data", IsoBmffFormatting.Hex(image));
        }

        return title;
    }

    public static string PlainText(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields,
        string fourCc,
        string title)
    {
        var fullTitle = string.Create(CultureInfo.InvariantCulture, $"{title} '{fourCc}'");
        var payload = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out var payloadLength, out _);
        if (payloadLength == 0)
        {
            return fullTitle;
        }

        var offset = payload.Length >= 4 && payload[0] == 0 && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            ? 4
            : 0;

        if (offset == 4)
        {
            fields.Raw("version", payload[0]);
            fields.Raw("flags", string.Create(CultureInfo.InvariantCulture, $"0x{payload[1]:X2}{payload[2]:X2}{payload[3]:X2}"));
        }

        var textBytes = payload.AsSpan(offset);
        fields.Derived("<text_size>", textBytes.Length);
        fields.Raw("text", IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(textBytes)));
        return fullTitle;
    }

    public static string MetadataText(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields,
        string fourCc,
        string title)
    {
        var fullTitle = string.Create(CultureInfo.InvariantCulture, $"{title} '{fourCc}'");

        var position = data.Position;
        var end = header.End;
        var dataBoxCount = 0;
        while (position + 8 <= end)
        {
            context.ThrowIfCancellationRequested();
            if (!IsoBmffBoxHeader.TryRead(
                    context.Source,
                    position,
                    end,
                    allowNumericType: false,
                    out var child,
                    out _))
            {
                break;
            }

            if (child.Type == "data")
            {
                var childData = new IsoBmffBoxReader(context.Source, child.PayloadOffset, child.PayloadLength, fields);
                ParseDataPayload(
                    context,
                    childData,
                    fields,
                    string.Create(CultureInfo.InvariantCulture, $"data[{dataBoxCount}]."));
                dataBoxCount++;
            }

            position = child.End;
            if (child.Truncated || child.End <= child.Start)
            {
                break;
            }
        }

        if (dataBoxCount > 0)
        {
            fields.Derived("data_box_count", dataBoxCount);
            data.SkipRemaining();
            return fullTitle;
        }

        // International text: repeated uint16 text_size, uint16 language, text.
        if (data.Remaining >= 4)
        {
            var textStart = data.Position;
            var payload = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out _, out _);
            var reader = new IsoBmffBoxReader(context.Source, textStart, payload.Length, fields);
            var index = 0;
            while (reader.Remaining >= 4)
            {
                if (!reader.TryReadUInt16Raw(out var textSize) || !reader.TryReadUInt16Raw(out var language))
                {
                    break;
                }

                var prefix = string.Create(CultureInfo.InvariantCulture, $"entry[{index}]");
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.text_size"), textSize);
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.language"), language);
                var text = reader.ReadBlock(textSize, context.Limits.MaxStringBytes, out _);
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}.text"),
                    IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(text)));
                index++;
            }

            if (index > 0)
            {
                fields.Derived("entry_count", index);
                return fullTitle;
            }
        }

        var fallback = data.ReadRemainingPrefix(64, out var fallbackLength, out _);
        fields.Derived("payload_size", fallbackLength);
        if (fallbackLength > 0)
        {
            fields.Raw("payload", IsoBmffFormatting.Hex(fallback));
        }

        return fullTitle;
    }

    public static string Cover(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"(QuickTime)CoverImage '{header.Type}'");
        ParseFirstDataChild(context, header, fields, "data");
        return title;
    }

    public static string StoreItemKind(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "(QuickTime)StoreItemKind 'stik'";
        if (!TryReadFirstDataChild(context, header, out var value))
        {
            return title;
        }

        fields.Raw("data.type_indicator", value.TypeIndicator);
        fields.Raw("data.locale", value.Locale);
        fields.Raw("$value", value.FirstByte);
        var meaning = value.FirstByte switch
        {
            0 => "Movie",
            1 => "Normal",
            2 => "Audiobook",
            5 => "Music Video",
            _ => "Unknown",
        };
        fields.Raw("$meaning", meaning);
        return title;
    }

    public static string Hdvd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "(QuickTime)Hdvd 'hdvd'";
        if (!TryReadFirstDataChild(context, header, out var value))
        {
            return title;
        }

        fields.Raw("data.type_indicator", value.TypeIndicator);
        fields.Raw("data.locale", value.Locale);
        fields.Raw("$value", value.FirstByte);
        return title;
    }

    private static void ParseDataPayload(
        ParseContext context,
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix)
    {
        if (!data.TryReadUInt32Raw(out var typeIndicator) || !data.TryReadUInt32Raw(out var locale))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "The data atom header is truncated.");
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}type_indicator"), typeIndicator);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}locale"), locale);

        var payloadLength = data.Remaining;
        fields.Derived(string.Create(CultureInfo.InvariantCulture, $"{prefix}<payload_length>"), payloadLength);

        string value;
        switch (typeIndicator)
        {
            case 1:
                {
                    var payload = data.ReadBlock(payloadLength, Math.Min(context.Limits.MaxStringBytes, int.MaxValue), out _);
                    value = IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(payload));
                    break;
                }

            case 2:
                {
                    var payload = data.ReadBlock(payloadLength, Math.Min(context.Limits.MaxStringBytes, int.MaxValue), out _);
                    value = IsoBmffFormatting.Display(Encoding.BigEndianUnicode.GetString(payload).TrimEnd('\0'));
                    break;
                }

            case 13:
                data.SkipRemaining();
                value = string.Create(CultureInfo.InvariantCulture, $"<jpeg:{payloadLength}>");
                break;

            case 14:
                data.SkipRemaining();
                value = string.Create(CultureInfo.InvariantCulture, $"<png:{payloadLength}>");
                break;

            case 21 when payloadLength is 1 or 2 or 4 or 8:
                {
                    var payload = data.ReadBlock(payloadLength, 8, out _);
                    value = ReadBigEndianInteger(payload).ToString(CultureInfo.InvariantCulture);
                    break;
                }

            default:
                data.SkipRemaining();
                value = string.Create(CultureInfo.InvariantCulture, $"<raw:{payloadLength}>");
                break;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}$value"), value);
    }

    private static void ParseFirstDataChild(
        ParseContext context,
        IsoBmffBoxHeader header,
        FieldList fields,
        string prefix)
    {
        if (!TryReadFirstDataChild(context, header, out var value))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.type_indicator"), value.TypeIndicator);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.locale"), value.Locale);
        fields.Derived(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}.<payload_length>"),
            value.PayloadLength);
        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}.$value"),
            string.Create(CultureInfo.InvariantCulture, $"<img:{value.PayloadLength}bytes>"));
    }

    private static bool TryReadFirstDataChild(
        ParseContext context,
        IsoBmffBoxHeader header,
        out DataChild value)
    {
        value = default;
        var position = header.PayloadOffset;
        while (position + 8 <= header.End)
        {
            if (!IsoBmffBoxHeader.TryRead(
                    context.Source,
                    position,
                    header.End,
                    allowNumericType: false,
                    out var child,
                    out _))
            {
                return false;
            }

            if (child.Type == "data" && child.PayloadLength >= 8)
            {
                var childData = new IsoBmffBoxReader(context.Source, child.PayloadOffset, child.PayloadLength, new FieldList(int.MaxValue));
                if (!childData.TryReadUInt32Raw(out var typeIndicator) || !childData.TryReadUInt32Raw(out var locale))
                {
                    return false;
                }

                var payloadLength = childData.Remaining;
                var first = childData.ReadBlock(payloadLength, 1, out _);
                value = new DataChild(typeIndicator, locale, payloadLength, first.Length > 0 ? first[0] : (byte)0);
                return true;
            }

            position = child.End;
            if (child.Truncated || child.End <= child.Start)
            {
                return false;
            }
        }

        return false;
    }

    private static ulong ReadBigEndianInteger(ReadOnlySpan<byte> bytes)
    {
        ulong value = 0;
        foreach (var item in bytes)
        {
            value = value << 8 | item;
        }

        return value;
    }

    private readonly record struct DataChild(uint TypeIndicator, uint Locale, long PayloadLength, byte FirstByte);
}
