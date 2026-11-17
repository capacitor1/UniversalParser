using System.Globalization;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

#pragma warning disable IDE0060 // Uniform box-parser signature keeps the dispatcher simple.

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>Sample table boxes. Each table row is emitted with its own index so keys stay unique.</summary>
internal static class SampleTableBoxes
{
    public static string Stsd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields) =>
        SampleEntryBoxes.Stsd(context, node, header, data, fields);

    public static string Stts(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"TimeToSample '{header.Type}'");
        if (!data.TryFullBox(out _, out _) || !data.TryUInt32("entry_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryReadUInt32Raw(out var sampleCount) || !data.TryReadUInt32Raw(out var sampleDelta))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"stts entry {index} is truncated.");
                break;
            }

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"(sample_count,sample_delta)[{index}]"),
                string.Create(CultureInfo.InvariantCulture, $"{sampleCount},{sampleDelta}"));
        }

        data.RequireEmpty("time-to-sample table");
        return title;
    }

    public static string Stsc(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"SampleToChunk '{header.Type}'");
        if (!data.TryFullBox(out _, out _) || !data.TryUInt32("entry_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryReadUInt32Raw(out var firstChunk)
                || !data.TryReadUInt32Raw(out var samplesPerChunk)
                || !data.TryReadUInt32Raw(out var descriptionIndex))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"stsc entry {index} is truncated.");
                break;
            }

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"(first_chunk,samples_per_chunk,sample_desc_index)[{index}]"),
                string.Create(CultureInfo.InvariantCulture, $"{firstChunk},{samplesPerChunk},{descriptionIndex}"));
        }

        data.RequireEmpty("sample-to-chunk table");
        return title;
    }

    public static string Stsz(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"SampleSize '{header.Type}'");
        if (!data.TryFullBox(out _, out _)
            || !data.TryUInt32("sample_size", out var sampleSize)
            || !data.TryUInt32("sample_count", out var declared))
        {
            return title;
        }

        if (sampleSize != 0)
        {
            fields.Note("All samples have the same size; the per-sample table is absent.");
            data.RequireEmpty("sample size table");
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"sample[{index}]"), out _))
            {
                break;
            }
        }

        data.RequireEmpty("sample size table");
        return title;
    }

    public static string Stco(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields) =>
        ChunkOffsets(context, header, data, fields, wide: false);

    public static string Co64(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields) =>
        ChunkOffsets(context, header, data, fields, wide: true);

    private static string ChunkOffsets(
        ParseContext context,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields,
        bool wide)
    {
        var title = wide ? "ChunkOffset64 'co64'" : "ChunkOffset 'stco'";
        if (!data.TryFullBox(out _, out _) || !data.TryUInt32("entry_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (wide)
            {
                if (!data.TryUInt64(string.Create(CultureInfo.InvariantCulture, $"chunk[{index}]"), out _))
                {
                    break;
                }
            }
            else if (!data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"chunk[{index}]"), out _))
            {
                break;
            }
        }

        data.RequireEmpty("chunk offset table");
        return title;
    }

    public static string Stss(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"SyncSample '{header.Type}'");
        if (!data.TryFullBox(out _, out _) || !data.TryUInt32("sync_sample_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"sync_sample[{index}]"), out _))
            {
                break;
            }
        }

        data.RequireEmpty("sync sample table");
        return title;
    }

    public static string Sdtp(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "SampleDependencyBox 'sdtp'";
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, (ulong)data.Remaining, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryReadUInt8Raw(out var value))
            {
                break;
            }

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"(is_leading,depends_on,is_depended_on,redundancy)[{index}]"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(value >> 6) & 0x3},{(value >> 4) & 0x3},{(value >> 2) & 0x3},{value & 0x3}"));
        }

        data.SkipRemaining();
        return title;
    }

    public static string Ctts(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "CompositionTimeToSample 'ctts'";
        if (!data.TryFullBox(out var version, out _) || !data.TryUInt32("entry_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryReadUInt32Raw(out var sampleCount) || !data.TryReadUInt32Raw(out var offsetRaw))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"ctts entry {index} is truncated.");
                break;
            }

            var offset = version == 0
                ? offsetRaw.ToString(CultureInfo.InvariantCulture)
                : unchecked((int)offsetRaw).ToString(CultureInfo.InvariantCulture);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"(sample_count,offset)[{index}]"),
                string.Create(CultureInfo.InvariantCulture, $"{sampleCount},{offset}"));
        }

        data.RequireEmpty("composition time table");
        return title;
    }

    public static string Sbgp(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"SampleToGroupBox '{header.Type}'");
        if (!data.TryFullBox(out var version, out _) || !data.TryFourCc("grouping_type", out _))
        {
            return title;
        }

        if (version == 1)
        {
            data.TryUInt32("grouping_type_parameter", out _);
        }

        if (!data.TryUInt32("entry_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryReadUInt32Raw(out var sampleCount) || !data.TryReadUInt32Raw(out var descriptionIndex))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"sbgp entry {index} is truncated.");
                break;
            }

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"(sample_count,group_description_index)[{index}]"),
                string.Create(CultureInfo.InvariantCulture, $"{sampleCount},{descriptionIndex}"));
        }

        data.RequireEmpty("sample-to-group table");
        return title;
    }

    public static string Sgpd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"SampleGroupDescriptionBox '{header.Type}'");
        if (!data.TryFullBox(out var version, out _) || !data.TryFourCc("grouping_type", out _))
        {
            return title;
        }

        uint defaultLength = 0;
        if (version == 1)
        {
            data.TryUInt32("default_length", out defaultLength);
        }

        if (version >= 2)
        {
            data.TryUInt32("default_sample_description_index", out _);
        }

        if (!data.TryUInt32("entry_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            var length = defaultLength;
            if (version == 1 && defaultLength == 0)
            {
                if (!data.TryReadUInt32Raw(out length))
                {
                    fields.Warning(DiagnosticCodes.TruncatedPayload, $"sgpd entry {index} is truncated.");
                    break;
                }

                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"description_length[{index}]"), length);
            }

            if (length > data.Remaining)
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"sgpd entry {index} declares {length} byte(s) but only {data.Remaining} remain."));
                data.SkipRemaining();
                break;
            }

            var payload = data.ReadBlock(length, 32, out _);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"entry[{index}]"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"payload_length={length} payload={IsoBmffFormatting.Hex(payload)}"));
        }

        data.SkipRemaining();
        return title;
    }

    public static string Cslg(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "CompositionToDecodeBox 'cslg'";
        if (!data.TryFullBox(out var version, out _))
        {
            return title;
        }

        string[] names =
        [
            "composition_to_dts_shift",
            "least_decode_to_display_delta",
            "greatest_decode_to_display_delta",
            "composition_start_time",
            "composition_end_time",
        ];

        foreach (var name in names)
        {
            if (version == 0)
            {
                if (!data.TryInt32(name, out _))
                {
                    break;
                }
            }
            else if (!data.TryInt64(name, out _))
            {
                break;
            }
        }

        data.RequireEmpty("composition-to-decode table");
        return title;
    }
}
