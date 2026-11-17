using System.Globalization;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

#pragma warning disable IDE0060 // Uniform box-parser signature keeps the dispatcher simple.

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>Item references (children of 'iref') and track references (children of 'tref').</summary>
internal static class ReferenceBoxes
{
    public static string ItemReference(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields,
        string fourCc)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"SingleItemTypeReferenceBox '{fourCc}'");
        var length = data.Remaining;
        var fits16 = length >= 4 && (length - 4) % 2 == 0;
        var fits32 = length >= 8 && (length - 8) % 4 == 0;
        var wide = fits32 && !fits16;

        if (fits16 && fits32)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                "Both 16-bit and 32-bit reference layouts fit the payload; the 16-bit layout is used.");
        }
        else if (!fits16 && !fits32)
        {
            fields.Warning(
                DiagnosticCodes.SchemaMismatch,
                "The reference payload matches neither the 16-bit nor the 32-bit layout; the 16-bit layout is used.");
        }

        fields.Derived("id_size", wide ? "32-bit" : "16-bit");

        ulong fromItem;
        uint referenceCount;
        if (wide)
        {
            if (!data.TryReadUInt32Raw(out var from32) || !data.TryReadUInt32Raw(out referenceCount))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, "The item reference header is truncated.");
                return title;
            }

            fromItem = from32;
        }
        else
        {
            if (!data.TryReadUInt16Raw(out var from16) || !data.TryReadUInt16Raw(out var count16))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, "The item reference header is truncated.");
                return title;
            }

            fromItem = from16;
            referenceCount = count16;
        }

        fields.Raw("from_item_ID", fromItem);
        fields.Raw("reference_count", referenceCount);

        var rows = IsoBmffFieldHelpers.RowLimit(context, referenceCount, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (wide)
            {
                if (!data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"to_item_ID[{index}]"), out _))
                {
                    break;
                }
            }
            else if (!data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"to_item_ID[{index}]"), out _))
            {
                break;
            }
        }

        data.RequireEmpty("item reference");
        return title;
    }

    public static string TrackReference(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields,
        string fourCc)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"TrackReferenceTypeBox '{fourCc}'");
        var declared = data.Remaining / 4;
        var count = IsoBmffFieldHelpers.RowLimit(context, (ulong)declared, header.Type, fields);
        for (var index = 0; index < count; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"track_ID[{index}]"), out _))
            {
                break;
            }
        }

        fields.Derived("track_ID_count", declared);
        if (declared > count)
        {
            data.SkipRemaining();
        }
        else
        {
            data.RequireEmpty("track reference");
        }

        return title;
    }
}
