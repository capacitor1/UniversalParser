using System.Globalization;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

#pragma warning disable IDE0060 // Uniform box-parser signature keeps the dispatcher simple.

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>Fragmented-MP4 boxes: movie extends, movie/track fragments and segment index.</summary>
internal static class FragmentBoxes
{
    public static string Mehd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "MovieExtendsHeader 'mehd'";
        if (!data.TryFullBox(out var version, out _))
        {
            return title;
        }

        if (version == 1)
        {
            data.TryUInt64("fragment_duration", out _);
        }
        else
        {
            data.TryUInt32("fragment_duration", out _);
        }

        data.RequireEmpty("movie extends header");
        return title;
    }

    public static string Mfhd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"MovieFragmentHeader '{header.Type}'");
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        data.TryUInt32("sequence_number", out _);
        data.RequireEmpty("movie fragment header");
        return title;
    }

    public static string Trex(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "TrackExtends 'trex'";
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        data.TryUInt32("track_ID", out _);
        data.TryUInt32("default_sample_description_index", out _);
        data.TryUInt32("default_sample_duration", out _);
        data.TryUInt32("default_sample_size", out _);
        if (data.TryReadUInt32Raw(out var flags))
        {
            fields.Raw("default_sample_flags", string.Create(CultureInfo.InvariantCulture, $"0x{flags:X8}"));
        }

        data.RequireEmpty("track extends");
        return title;
    }

    public static string Tfdt(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"TrackFragmentDecodeTime '{header.Type}'");
        if (!data.TryFullBox(out var version, out _))
        {
            return title;
        }

        if (version == 1)
        {
            data.TryUInt64("baseMediaDecodeTime", out _);
        }
        else
        {
            data.TryUInt32("baseMediaDecodeTime", out _);
        }

        data.RequireEmpty("track fragment decode time");
        return title;
    }

    public static string Tfhd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"TrackFragmentHeader '{header.Type}'");
        if (!data.TryFullBox(out _, out var flags) || !data.TryUInt32("track_ID", out _))
        {
            return title;
        }

        if ((flags & 0x000001) != 0)
        {
            data.TryUInt64("base_data_offset", out _);
        }

        if ((flags & 0x000002) != 0)
        {
            data.TryUInt32("sample_description_index", out _);
        }

        if ((flags & 0x000008) != 0)
        {
            data.TryUInt32("default_sample_duration", out _);
        }

        if ((flags & 0x000010) != 0)
        {
            data.TryUInt32("default_sample_size", out _);
        }

        if ((flags & 0x000020) != 0 && data.TryReadUInt32Raw(out var sampleFlags))
        {
            fields.Raw("default_sample_flags", string.Create(CultureInfo.InvariantCulture, $"0x{sampleFlags:X8}"));
        }

        data.RequireEmpty("track fragment header");
        return title;
    }

    public static string Trun(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"TrackRun '{header.Type}'");
        if (!data.TryFullBox(out var version, out var flags) || !data.TryUInt32("sample_count", out var declared))
        {
            return title;
        }

        if ((flags & 0x000001) != 0 && data.TryInt32("data_offset", out _))
        {
            // Reported by TryInt32.
        }

        if ((flags & 0x000004) != 0 && data.TryReadUInt32Raw(out var firstFlags))
        {
            fields.Raw("first_sample_flags", string.Create(CultureInfo.InvariantCulture, $"0x{firstFlags:X8}"));
        }

        var hasDuration = (flags & 0x000100) != 0;
        var hasSize = (flags & 0x000200) != 0;
        var hasFlags = (flags & 0x000400) != 0;
        var hasComposition = (flags & 0x000800) != 0;

        if (!hasDuration && !hasSize && !hasFlags && !hasComposition)
        {
            fields.Note("No per-sample fields are present in this track run.");
            data.RequireEmpty("track run");
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();

            uint duration = 0;
            uint size = 0;
            uint sampleFlags = 0;
            int composition = 0;

            if (hasDuration && !data.TryReadUInt32Raw(out duration))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"trun sample {index} is truncated.");
                break;
            }

            if (hasSize && !data.TryReadUInt32Raw(out size))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"trun sample {index} is truncated.");
                break;
            }

            if (hasFlags && !data.TryReadUInt32Raw(out sampleFlags))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"trun sample {index} is truncated.");
                break;
            }

            if (hasComposition)
            {
                if (!data.TryReadUInt32Raw(out var compositionRaw))
                {
                    fields.Warning(DiagnosticCodes.TruncatedPayload, $"trun sample {index} is truncated.");
                    break;
                }

                composition = version == 0 ? (int)compositionRaw : unchecked((int)compositionRaw);
            }

            var builder = new System.Text.StringBuilder();
            builder.Append(string.Create(CultureInfo.InvariantCulture, $"duration={duration},size={size}"));
            builder.Append(string.Create(CultureInfo.InvariantCulture, $",flags=0x{sampleFlags:X8}"));
            builder.Append(string.Create(CultureInfo.InvariantCulture, $",composition_time_offset={composition}"));

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"sample[{index}]"),
                builder.ToString());
        }

        data.RequireEmpty("track run");
        return title;
    }

    public static string Sidx(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "SegmentIndexBox 'sidx'";
        if (!data.TryFullBox(out var version, out _))
        {
            return title;
        }

        data.TryUInt32("reference_ID", out _);
        data.TryUInt32("timescale", out _);

        if (version == 0)
        {
            data.TryUInt32("earliest_presentation_time", out _);
            data.TryUInt32("first_offset", out _);
        }
        else
        {
            data.TryUInt64("earliest_presentation_time", out _);
            data.TryUInt64("first_offset", out _);
        }

        data.TryUInt16("reserved", out _);
        if (!data.TryUInt16("reference_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryReadUInt32Raw(out var first)
                || !data.TryReadUInt32Raw(out var subsegmentDuration)
                || !data.TryReadUInt32Raw(out var sap))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"sidx reference {index} is truncated.");
                break;
            }

            var prefix = string.Create(CultureInfo.InvariantCulture, $"reference[{index}]");
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}.reference_type"),
                (first >> 31) & 0x1);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}.referenced_size"),
                first & 0x7FFFFFFF);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}.subsegment_duration"),
                subsegmentDuration);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}.starts_with_SAP"),
                (sap >> 31) & 0x1);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}.SAP_type"),
                (sap >> 28) & 0x7);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}.SAP_delta_time"),
                sap & 0x0FFFFFFF);
        }

        data.RequireEmpty("segment index");
        return title;
    }
}
