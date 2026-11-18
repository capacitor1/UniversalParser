using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

#pragma warning disable IDE0060 // Uniform box-parser signature keeps the dispatcher simple.

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>Movie, track, media and QuickTime media-information boxes.</summary>
internal static class MovieBoxes
{
    public static string Mvhd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"MovieHeader '{header.Type}'");
        if (!data.TryFullBox(out var version, out _))
        {
            return title;
        }

        ulong creation;
        ulong modification;
        uint timescale;
        ulong duration;

        switch (version)
        {
            case 0:
                data.TryUInt32("creation_time", out var creation32);
                data.TryUInt32("modification_time", out var modification32);
                creation = creation32;
                modification = modification32;
                data.TryUInt32("timescale", out timescale);
                data.TryUInt32("duration", out var duration32);
                duration = duration32;
                break;
            case 1:
                data.TryUInt64("creation_time", out creation);
                data.TryUInt64("modification_time", out modification);
                data.TryUInt32("timescale", out timescale);
                data.TryUInt64("duration", out duration);
                break;
            default:
                fields.Warning(DiagnosticCodes.UnsupportedVersion, $"Unsupported mvhd version {version}.");
                data.SkipRemaining();
                return title;
        }

        fields.Derived("<creation_time>", IsoBmffFormatting.FormatTime(creation));
        fields.Derived("<modification_time>", IsoBmffFormatting.FormatTime(modification));
        if (timescale > 0)
        {
            fields.Derived(
                "<duration_seconds>",
                (duration / (double)timescale).ToString("F3", CultureInfo.InvariantCulture));
        }

        if (data.TryUInt32("rate", out var rate))
        {
            fields.Derived("<rate>", IsoBmffFieldHelpers.Scaled(rate, 65536.0, "F6"));
        }

        if (data.TryUInt16("volume", out var volume))
        {
            fields.Derived("<volume>", IsoBmffFieldHelpers.Scaled(volume, 256.0, "F3"));
        }

        data.TryUInt16("reserved[0]", out _);
        data.TryUInt32("reserved[1]", out _);
        data.TryUInt32("reserved[2]", out _);

        ReadMatrix(data, fields, "matrix");
        ReadUInt32Array(data, fields, "pre_defined", 6);
        data.TryUInt32("next_track_ID", out _);
        data.RequireEmpty("movie header");
        return title;
    }

    public static string Tkhd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"TrackHeader '{header.Type}'");
        if (!data.TryFullBox(out var version, out _))
        {
            return title;
        }

        if (version > 1)
        {
            fields.Warning(DiagnosticCodes.UnsupportedVersion, $"Unsupported tkhd version {version}.");
            data.SkipRemaining();
            return title;
        }

        if (version == 1)
        {
            data.TryUInt64("creation_time", out var creation);
            data.TryUInt64("modification_time", out var modification);
            fields.Derived("<creation_time>", IsoBmffFormatting.FormatTime(creation));
            fields.Derived("<modification_time>", IsoBmffFormatting.FormatTime(modification));
            data.TryUInt32("track_ID", out _);
            data.TryUInt32("reserved", out _);
            data.TryUInt64("duration", out _);
        }
        else
        {
            data.TryUInt32("creation_time", out var creation);
            data.TryUInt32("modification_time", out var modification);
            fields.Derived("<creation_time>", IsoBmffFormatting.FormatTime(creation));
            fields.Derived("<modification_time>", IsoBmffFormatting.FormatTime(modification));
            data.TryUInt32("track_ID", out _);
            data.TryUInt32("reserved", out _);
            data.TryUInt32("duration", out _);
        }

        data.TryUInt32("reserved1", out _);
        data.TryUInt32("reserved2", out _);

        if (data.TryReadUInt16Raw(out var layer))
        {
            fields.Raw("layer", unchecked((short)layer));
        }

        if (data.TryReadUInt16Raw(out var group))
        {
            fields.Raw("alternate_group", unchecked((short)group));
        }

        if (data.TryUInt16("volume", out var volume))
        {
            fields.Derived("<volume>", IsoBmffFieldHelpers.Scaled(volume, 256.0, "F3"));
        }

        data.TryUInt16("reserved3", out _);
        ReadMatrix(data, fields, "matrix");

        if (data.TryUInt32("width", out var width))
        {
            fields.Derived("<width>", IsoBmffFieldHelpers.Scaled(width, 65536.0, "F3"));
        }

        if (data.TryUInt32("height", out var height))
        {
            fields.Derived("<height>", IsoBmffFieldHelpers.Scaled(height, 65536.0, "F3"));
        }

        data.RequireEmpty("track header");
        return title;
    }

    public static string Mdhd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"MediaHeader '{header.Type}'");
        if (!data.TryFullBox(out var version, out _))
        {
            return title;
        }

        switch (version)
        {
            case 0:
                data.TryUInt32("creation_time", out var creation32);
                data.TryUInt32("modification_time", out var modification32);
                fields.Derived("<creation_time>", IsoBmffFormatting.FormatTime(creation32));
                fields.Derived("<modification_time>", IsoBmffFormatting.FormatTime(modification32));
                data.TryUInt32("timescale", out _);
                data.TryUInt32("duration", out _);
                break;
            case 1:
                data.TryUInt64("creation_time", out var creation64);
                data.TryUInt64("modification_time", out var modification64);
                fields.Derived("<creation_time>", IsoBmffFormatting.FormatTime(creation64));
                fields.Derived("<modification_time>", IsoBmffFormatting.FormatTime(modification64));
                data.TryUInt32("timescale", out _);
                data.TryUInt64("duration", out _);
                break;
            default:
                fields.Warning(DiagnosticCodes.UnsupportedVersion, $"Unsupported mdhd version {version}.");
                data.SkipRemaining();
                return title;
        }

        if (data.TryUInt16("language", out var language))
        {
            fields.Derived("<language>", IsoBmffFieldHelpers.Language(language));
        }

        data.TryUInt16("pre_defined", out _);
        data.RequireEmpty("media header");
        return title;
    }

    public static string Hdlr(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"Handler '{header.Type}'");
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        data.TryUInt32("pre_defined", out _);
        if (data.TryFourCc("handler_type", out var handlerType))
        {
            fields.Derived("<handler_type>", handlerType);
        }

        ReadUInt32Array(data, fields, "reserved", 3);
        var name = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out var nameLength, out _);
        if (nameLength > 0)
        {
            var text = IsoBmffFormatting.ReadNullTerminatedText(name, Encoding.UTF8);
            fields.Derived("<name>", IsoBmffFormatting.Display(text));
        }

        return title;
    }

    public static string Elst(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"EditList '{header.Type}'");
        if (!data.TryFullBox(out var version, out _))
        {
            return title;
        }

        if (version > 1)
        {
            fields.Warning(DiagnosticCodes.UnsupportedVersion, $"Unsupported elst version {version}.");
            data.SkipRemaining();
            return title;
        }

        if (!data.TryUInt32("entry_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            ulong segmentDuration;
            long mediaTime;
            if (version == 1)
            {
                if (!data.TryReadUInt64Raw(out segmentDuration)
                    || !data.TryReadUInt64Raw(out var mediaRaw)
                    || !data.TryReadUInt16Raw(out var rateInteger)
                    || !data.TryReadUInt16Raw(out var rateFraction))
                {
                    fields.Warning(DiagnosticCodes.TruncatedPayload, $"Edit list entry {index} is truncated.");
                    break;
                }

                mediaTime = unchecked((long)mediaRaw);
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"(segment_duration,media_time,media_rate)[{index}]"),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{segmentDuration},{mediaTime},{unchecked((short)rateInteger)}.{rateFraction}"));
            }
            else
            {
                if (!data.TryReadUInt32Raw(out var segmentRaw)
                    || !data.TryReadUInt32Raw(out var mediaRaw)
                    || !data.TryReadUInt16Raw(out var rateInteger)
                    || !data.TryReadUInt16Raw(out var rateFraction))
                {
                    fields.Warning(DiagnosticCodes.TruncatedPayload, $"Edit list entry {index} is truncated.");
                    break;
                }

                mediaTime = unchecked((int)mediaRaw);
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"(segment_duration,media_time,media_rate)[{index}]"),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{segmentRaw},{mediaTime},{unchecked((short)rateInteger)}.{rateFraction}"));
            }
        }

        data.RequireEmpty("edit list");
        return title;
    }

    public static string Vmhd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"VideoMediaHeader '{header.Type}'");
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        data.TryUInt16("graphics_mode", out _);
        var red = 0;
        var green = 0;
        var blue = 0;
        if (data.TryUInt16("opcolor[0]", out var r))
        {
            red = r;
        }

        if (data.TryUInt16("opcolor[1]", out var g))
        {
            green = g;
        }

        if (data.TryUInt16("opcolor[2]", out var b))
        {
            blue = b;
        }

        fields.Derived("<opcolor>", string.Create(CultureInfo.InvariantCulture, $"R = {red}, G = {green}, B = {blue}"));
        data.RequireEmpty("video media header");
        return title;
    }

    public static string Smhd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"SoundMediaHeader '{header.Type}'");
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        if (data.TryReadUInt16Raw(out var balance))
        {
            fields.Raw("balance", unchecked((short)balance));
        }

        data.TryUInt16("reserved", out _);
        data.RequireEmpty("sound media header");
        return title;
    }

    public static string DataEntry(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields,
        bool isUrn)
    {
        var title = isUrn
            ? string.Create(CultureInfo.InvariantCulture, $"DataEntryUrnBox '{header.Type}'")
            : string.Create(CultureInfo.InvariantCulture, $"DataEntryUrlBox '{header.Type}'");

        if (!data.TryFullBox(out _, out var flags))
        {
            return title;
        }

        var selfContained = (flags & 0x000001) != 0;
        fields.Raw(
            "self_contained",
            selfContained
                ? "1 (media data is in this file)"
                : "0 (references an external file)");

        if (selfContained)
        {
            fields.Derived("<location>", "<absent: self_contained>");
            if (isUrn)
            {
                fields.Derived("<name>", "<absent: self_contained>");
            }

            data.RequireEmpty("data entry");
            return title;
        }

        var payload = data.ReadRemainingPrefix(context.Limits.MaxStringBytes, out _, out _);
        if (isUrn)
        {
            var nameEnd = Array.IndexOf(payload, (byte)0);
            if (nameEnd < 0)
            {
                fields.Derived("<name>", IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(payload)));
            }
            else
            {
                fields.Derived("<name>", IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(payload.AsSpan(0, nameEnd))));
                fields.Derived(
                    "location",
                    IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(payload.AsSpan(nameEnd + 1))));
            }
        }
        else
        {
            fields.Derived("location", IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(payload)));
        }

        return title;
    }

    public static string Alis(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "DataEntryAliasBox 'alis'";
        if (!data.TryFullBox(out _, out var flags))
        {
            return title;
        }

        var selfContained = (flags & 0x000001) != 0;
        fields.Raw(
            "self_contained",
            selfContained
                ? "1 (media data is in this file)"
                : "0 (references an external file)");

        if (!data.TryFourCc("alias.creator_code", out _)
            || !data.TryUInt16("alias.record_size", out _)
            || !data.TryUInt16("alias.version", out var aliasVersion))
        {
            return title;
        }

        if (aliasVersion is >= 2 and <= 3)
        {
            data.TryUInt16("alias.kind", out _);
            ReadPascalString(data, fields, "alias.volume_name");
            data.TryUInt32("alias.volume_date", out _);
            data.TryUInt16("alias.filesystem_type", out _);
            data.TryUInt16("alias.disk_type", out _);
            data.TryUInt32("alias.parent_cnid", out _);
            ReadPascalString(data, fields, "alias.target_name");
            data.TryUInt32("alias.target_cnid", out _);
            data.TryUInt32("alias.target_date", out _);
            data.TryUInt32("alias.target_creator", out _);
            data.TryUInt32("alias.target_type", out _);
            data.TryUInt16("alias.levels_to_root", out _);
            data.TryUInt16("alias.levels_to_target", out _);
            data.TryUInt32("alias.volume_attributes", out _);
            data.TryUInt16("alias.volume_fs_id", out _);
        }

        var index = 0;
        while (data.Remaining >= 2)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryReadUInt16Raw(out var tag))
            {
                break;
            }

            var prefix = string.Create(CultureInfo.InvariantCulture, $"alias.tag[{index}]");
            if (tag == 0xFFFF)
            {
                fields.Raw($"{prefix}.tag", "0xFFFF (end of alias record)");
                break;
            }

            if (!data.TryReadUInt16Raw(out var length))
            {
                break;
            }

            if (data.Remaining < length)
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"Alias tag {index} declares {length} byte(s) but the box ends first.");
                data.SkipRemaining();
                break;
            }

            var tagData = new byte[length];
            if (length > 0 && !data.TryReadBytesRaw(tagData))
            {
                break;
            }

            fields.Raw($"{prefix}.tag", string.Create(CultureInfo.InvariantCulture, $"0x{tag:X4} ({AliasTagName(tag)})"));
            fields.Raw($"{prefix}.length", length);
            AppendAliasTagValue(fields, prefix, tag, tagData);
            index++;

            // Alias tag values are padded to an even length.
            if ((length & 1) != 0 && data.Remaining > 0)
            {
                _ = data.TryReadUInt8Raw(out _);
            }
        }

        return title;
    }

    private static string AliasTagName(ushort tag) => tag switch
    {
        0 => "carbon_folder_name",
        1 => "cnid_path",
        2 => "carbon_path",
        3 => "appleshare_zone",
        4 => "appleshare_server_name",
        5 => "appleshare_user_name",
        6 => "driver_name",
        9 => "network_mount_info",
        10 => "dial_up_info",
        14 => "unicode_target_name",
        15 => "unicode_volume_name",
        16 => "high_resolution_volume_creation_date",
        17 => "high_resolution_creation_date",
        18 => "posix_path",
        19 => "posix_path_to_volume_mountpoint",
        20 => "recursive_alias",
        21 => "user_home_length_prefix",
        _ => "unknown",
    };

    private static void AppendAliasTagValue(FieldList fields, string prefix, ushort tag, byte[] data)
    {
        switch (tag)
        {
            case 0 or 2 or 3 or 4 or 5 or 6 or 18 or 19:
                fields.Raw($"{prefix}.value", IsoBmffFormatting.Display(Encoding.Latin1.GetString(data)));
                break;
            case 1:
                var cnidCount = data.Length / 4;
                fields.Raw($"{prefix}.cnid_count", cnidCount);
                for (var index = 0; index < cnidCount; index++)
                {
                    fields.Raw(
                        $"{prefix}.cnid[{index}]",
                        System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(index * 4, 4)));
                }

                break;
            case 14 or 15:
                fields.Raw($"{prefix}.value", IsoBmffFormatting.Display(Encoding.BigEndianUnicode.GetString(data)));
                break;
            case 16 or 17 when data.Length >= 8:
                fields.Raw(
                    $"{prefix}.value",
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(data));
                break;
            case 21 when data.Length >= 2:
                fields.Raw(
                    $"{prefix}.value",
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data));
                break;
            case 20:
                fields.Raw($"{prefix}.value", $"nested alias record, {data.Length} byte(s)");
                break;
            default:
                fields.Raw($"{prefix}.value", IsoBmffFormatting.Hex(data, 32));
                break;
        }
    }

    public static string Iods(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        if (data.TryFullBox(out _, out _))
        {
            fields.Derived("<descriptor_length>", data.Remaining);
        }

        return "InitialObjectDescriptorBox 'iods'";
    }

    public static string Gmin(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "BaseMediaInfoAtom 'gmin'";
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        data.TryUInt16("graphics_mode", out _);
        data.TryUInt16("opcolor_red", out _);
        data.TryUInt16("opcolor_green", out _);
        data.TryUInt16("opcolor_blue", out _);

        if (data.TryUInt16("balance", out var balance))
        {
            fields.Raw("balance", IsoBmffFieldHelpers.Fixed88(balance), data.LastRange);
        }

        data.TryUInt16("reserved", out _);
        data.RequireEmpty("base media information");
        return title;
    }

    public static string TextMediaInformation(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "TextMediaInformationAtom 'text'";
        string[] names = ["a", "b", "u", "c", "d", "v", "tx", "ty", "w"];
        double[] scales = [65536.0, 65536.0, 1073741824.0, 65536.0, 65536.0, 1073741824.0, 65536.0, 65536.0, 1073741824.0];

        for (var index = 0; index < names.Length; index++)
        {
            if (!data.TryReadUInt32Raw(out var raw))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, "The text media information matrix is truncated.");
                break;
            }

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"matrix[{index}] {names[index]}"),
                IsoBmffFieldHelpers.Scaled(raw, scales[index], "0.######")
                    + string.Create(CultureInfo.InvariantCulture, $" (0x{raw:X8})"));
        }

        data.RequireEmpty("text media information");
        return title;
    }

    public static string Tcmi(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "TimecodeMediaInformationAtom 'tcmi'";
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        data.TryUInt16("text_font", out _);
        data.TryUInt16("text_face", out _);
        data.TryUInt16("text_size", out _);
        data.TryUInt16("reserved", out _);
        data.TryUInt16("text_color_red", out _);
        data.TryUInt16("text_color_green", out _);
        data.TryUInt16("text_color_blue", out _);
        data.TryUInt16("background_color_red", out _);
        data.TryUInt16("background_color_green", out _);
        data.TryUInt16("background_color_blue", out _);

        ReadPascalString(data, fields, "font_name");
        data.RequireEmpty("timecode media information");
        return title;
    }

    public static string Aperture(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields,
        string title)
    {
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        if (data.TryUInt32("width", out var width))
        {
            fields.Raw("width", IsoBmffFieldHelpers.Fixed1616(width), data.LastRange);
        }

        if (data.TryUInt32("height", out var height))
        {
            fields.Raw("height", IsoBmffFieldHelpers.Fixed1616(height), data.LastRange);
        }

        data.RequireEmpty("aperture dimensions");
        return title;
    }

    public static string Csgm(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        fields.Derived("<payload_length>", data.Remaining);
        fields.Note("Contains unsolved data; please see the raw dump below.");
        return "(QuickTime)CompositionShiftGroupMeta 'csgm'";
    }

    private static void ReadMatrix(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        for (var index = 0; index < 9; index++)
        {
            if (!data.TryReadUInt32Raw(out var raw))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"The {prefix} is truncated.");
                return;
            }

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}[{index}]"),
                IsoBmffFieldHelpers.Fixed1616(raw));
        }
    }

    private static void ReadUInt32Array(IsoBmffBoxReader data, FieldList fields, string prefix, int count)
    {
        for (var index = 0; index < count; index++)
        {
            if (!data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}[{index}]"), out _))
            {
                return;
            }
        }
    }

    private static void ReadPascalString(IsoBmffBoxReader data, FieldList fields, string key)
    {
        if (!data.TryReadUInt8Raw(out var length))
        {
            return;
        }

        if (length == 0)
        {
            fields.Raw(key, "<empty>", data.LastRange);
            return;
        }

        var bytes = new byte[length];
        if (!data.TryReadBytesRaw(bytes))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, $"Pascal string '{key}' is truncated.");
            return;
        }

        fields.Raw(key, IsoBmffFormatting.Display(IsoBmffFormatting.DecodeText(bytes)), data.LastRange);
    }
}
