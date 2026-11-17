using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

#pragma warning disable IDE0060 // Uniform box-parser signature keeps the dispatcher simple.

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>General boxes: file/segment type, UUID, media data and filler boxes.</summary>
internal static class GeneralBoxes
{
    public static string Ftyp(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields) =>
        ParseFileType(context, header, data, fields, "FileType");

    public static string Styp(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields) =>
        ParseFileType(context, header, data, fields, "SegmentType");

    private static string ParseFileType(
        ParseContext context,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields,
        string title)
    {
        if (!data.TryFourCc("major_brand", out _))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{title} '{header.Type}'");
        }

        data.TryUInt32("minor_version", out _);

        var declared = data.Remaining / 4;
        if (declared <= 0)
        {
            fields.Note("No compatible_brands are present.");
            return string.Create(CultureInfo.InvariantCulture, $"{title} '{header.Type}'");
        }

        var count = IsoBmffFieldHelpers.RowLimit(context, (ulong)declared, header.Type, fields);
        for (var index = 0; index < count; index++)
        {
            if (!data.TryFourCc(string.Create(CultureInfo.InvariantCulture, $"compatible_brand[{index}]"), out _))
            {
                break;
            }
        }

        if (declared > count)
        {
            data.SkipRemaining();
        }
        else if (data.Remaining > 0)
        {
            data.Warn(
                DiagnosticCodes.SchemaMismatch,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{data.Remaining} trailing byte(s) are too short for another compatible brand."));
            data.SkipRemaining();
        }

        return string.Create(CultureInfo.InvariantCulture, $"{title} '{header.Type}'");
    }

    public static string Uuid(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        if (header.UserType is null)
        {
            fields.Warning(DiagnosticCodes.SchemaMismatch, "The uuid box has no 16-byte user type.");
        }
        else
        {
            fields.Raw("uuid", header.UserType, new SourceRange(header.Start + 8, 16));
        }

        fields.Derived("<payload_length>", data.Remaining);
        return "UUID 'uuid'";
    }

    public static string Mdat(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        fields.Derived("<payload_length>", data.Remaining);
        return string.Create(CultureInfo.InvariantCulture, $"MediaData '{header.Type}'");
    }

    public static string Free(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        fields.Derived("<payload_length>", data.Remaining);
        return string.Create(CultureInfo.InvariantCulture, $"FreeSpace '{header.Type}'");
    }

    public static string Skip(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        fields.Derived("<payload_length>", data.Remaining);
        return string.Create(CultureInfo.InvariantCulture, $"SkipBox '{header.Type}'");
    }

    public static string Wide(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        fields.Derived("<payload_length>", data.Remaining);
        return string.Create(CultureInfo.InvariantCulture, $"Wide '{header.Type}'");
    }

    public static string Xtra(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var payload = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out var totalLength, out _);
        fields.Derived("<payload_length>", totalLength);

        var text = TryDetectText(payload);
        if (!string.IsNullOrEmpty(text))
        {
            fields.Derived("<as_text>", text);
        }

        return "(Extension)Xtra 'Xtra'";
    }

    public static string Unknown(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        fields.Derived("<payload_length>", data.Remaining);
        fields.Note("Please see raw dump below.", node.Range);
        return string.Create(CultureInfo.InvariantCulture, $"UnknownBox '{header.Type}'");
    }

    private static string? TryDetectText(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return null;
        }

        var printable = 0;
        foreach (var value in payload)
        {
            if (value is >= 0x20 and <= 0x7E or 0x09 or 0x0A or 0x0D or 0)
            {
                printable++;
            }
        }

        if (printable * 10 < payload.Length * 9)
        {
            return null;
        }

        var text = IsoBmffFormatting.DecodeText(payload).Trim('\0');
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length > 200 ? text[..200] + "..." : text;
    }
}
