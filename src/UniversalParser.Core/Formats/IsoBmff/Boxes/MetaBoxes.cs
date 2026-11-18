using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

#pragma warning disable IDE0060 // Uniform box-parser signature keeps the dispatcher simple.

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>HEIF/AVIF item metadata and item-property boxes.</summary>
internal static class MetaBoxes
{
    public static string Pitm(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"PrimaryItemBox '{header.Type}'");
        if (!data.TryFullBox(out var version, out _))
        {
            return title;
        }

        if (version == 0)
        {
            data.TryUInt16("primary_item_ID", out _);
        }
        else
        {
            data.TryUInt32("primary_item_ID", out _);
        }

        data.RequireEmpty("primary item");
        return title;
    }

    public static string Iloc(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "ItemLocationBox 'iloc'";
        if (!data.TryFullBox(out var version, out _) || !data.TryReadUInt8Raw(out var first))
        {
            return title;
        }

        var offsetSize = (first & 0xF0) >> 4;
        var lengthSize = first & 0x0F;
        fields.Raw("offset_size", offsetSize);
        fields.Raw("length_size", lengthSize);

        if (!data.TryReadUInt8Raw(out var second))
        {
            return title;
        }

        var baseOffsetSize = (second & 0xF0) >> 4;
        var indexSize = version is 1 or 2 ? second & 0x0F : 0;
        fields.Raw("base_offset_size", baseOffsetSize);
        fields.Raw("index_size", indexSize);

        uint itemCount;
        if (version < 2)
        {
            if (!data.TryUInt16("item_count", out var count16))
            {
                return title;
            }

            itemCount = count16;
        }
        else if (!data.TryUInt32("item_count", out itemCount))
        {
            return title;
        }

        var items = IsoBmffFieldHelpers.RowLimit(context, itemCount, header.Type, fields);
        for (var itemIndex = 0; itemIndex < items; itemIndex++)
        {
            context.ThrowIfCancellationRequested();
            var prefix = string.Create(CultureInfo.InvariantCulture, $"item[{itemIndex}]");

            ulong itemId;
            if (version < 2)
            {
                if (!data.TryReadUInt16Raw(out var id16))
                {
                    fields.Warning(DiagnosticCodes.TruncatedPayload, $"iloc item {itemIndex} is truncated.");
                    break;
                }

                itemId = id16;
            }
            else if (!data.TryReadUInt32Raw(out var id32))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"iloc item {itemIndex} is truncated.");
                break;
            }
            else
            {
                itemId = id32;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.item_ID"), itemId);

            if (version is 1 or 2)
            {
                if (!data.TryReadUInt16Raw(out var construction))
                {
                    break;
                }

                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}.construction_method"),
                    construction & 0x0F);
            }

            if (!data.TryReadUInt16Raw(out var dataReferenceIndex))
            {
                break;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.data_reference_index"), dataReferenceIndex);
            data.ReadVariableUInt(baseOffsetSize, string.Create(CultureInfo.InvariantCulture, $"{prefix}.base_offset"));

            if (!data.TryReadUInt16Raw(out var extentCount))
            {
                break;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.extent_count"), extentCount);

            for (var extentIndex = 0; extentIndex < extentCount; extentIndex++)
            {
                context.ThrowIfCancellationRequested();
                var extentPrefix = string.Create(CultureInfo.InvariantCulture, $"{prefix}.extent[{extentIndex}]");
                if (version is 1 or 2 && indexSize > 0)
                {
                    data.ReadVariableUInt(indexSize, string.Create(CultureInfo.InvariantCulture, $"{extentPrefix}.extent_index"));
                }

                data.ReadVariableUInt(offsetSize, string.Create(CultureInfo.InvariantCulture, $"{extentPrefix}.extent_offset"));
                data.ReadVariableUInt(lengthSize, string.Create(CultureInfo.InvariantCulture, $"{extentPrefix}.extent_length"));
            }
        }

        data.RequireEmpty("item location");
        return title;
    }

    public static string Idat(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        fields.Derived("<payload_length>", data.Remaining);
        return string.Create(CultureInfo.InvariantCulture, $"ItemDataBox '{header.Type}'");
    }

    public static string Infe(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "ItemInfoEntry 'infe'";
        if (!data.TryFullBox(out var version, out _))
        {
            return title;
        }

        if (version is 0 or 1)
        {
            data.TryUInt16("item_id", out _);
            data.TryUInt16("protection_index", out _);
            var name = IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes);
            fields.Raw("item_name", IsoBmffFormatting.Display(name));
            var contentType = IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes);
            fields.Raw("content_type", IsoBmffFormatting.Display(contentType));
            var encoding = IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes);
            fields.Raw("content_encoding", IsoBmffFormatting.Display(encoding));

            if (version == 1 && data.Remaining >= 4)
            {
                if (data.TryFourCc("extension_type", out var extensionType))
                {
                    if (extensionType == "fdel")
                    {
                        ParseFileDeliveryExtension(context, data, fields);
                    }
                    else
                    {
                        fields.Note($"Item information extension '{extensionType}' is not decoded.", data.CurrentRange);
                    }
                }
            }

            data.SkipRemaining();
            return title;
        }

        if (version is 2 or 3)
        {
            if (version == 2)
            {
                data.TryUInt16("item_id", out _);
            }
            else
            {
                data.TryUInt32("item_id", out _);
            }

            data.TryUInt16("protection_index", out _);
            if (!data.TryFourCc("item_type", out var itemType))
            {
                return title;
            }

            var name = IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes);
            fields.Raw("item_name", IsoBmffFormatting.Display(name));

            switch (itemType)
            {
                case "mime":
                    fields.Raw(
                        "content_type",
                        IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                    fields.Raw(
                        "content_encoding",
                        IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                    break;
                case "uri ":
                    fields.Raw(
                        "item_uri",
                        IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                    break;
            }

            data.SkipRemaining();
            return title;
        }

        fields.Warning(DiagnosticCodes.UnsupportedVersion, $"Unsupported infe version {version}.");
        data.SkipRemaining();
        return title;
    }

    private static void ParseFileDeliveryExtension(
        ParseContext context,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var location = IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes);
        fields.Raw("content_location", IsoBmffFormatting.Display(location));
        var md5 = IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes);
        fields.Raw("content_MD5", IsoBmffFormatting.Display(md5));

        data.TryUInt64("content_length", out _);
        data.TryUInt64("transfer_length", out _);
        if (!data.TryByte("group_ID_count", out var count))
        {
            return;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, count, "fdel", fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"group_ID[{index}]"), out _))
            {
                break;
            }
        }
    }

    public static string Ispe(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"ImageSpatialExtents '{header.Type}'");
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        data.TryUInt32("image_width", out _);
        data.TryUInt32("image_height", out _);
        data.RequireEmpty("image spatial extents");
        return title;
    }

    public static string Irot(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"ImageRotation '{header.Type}'");
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        if (data.TryReadUInt8Raw(out var value))
        {
            fields.Raw("rotation", value & 0x03);
            fields.Raw("reserved", string.Create(CultureInfo.InvariantCulture, $"0x{value >> 2:X2}"));
        }

        data.RequireEmpty("image rotation");
        return title;
    }

    public static string Imir(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "ImageMirror 'imir'";
        if (data.TryReadUInt8Raw(out var value))
        {
            fields.Raw("reserved", string.Create(CultureInfo.InvariantCulture, $"0x{value >> 1:X2}"));
            fields.Raw("axis", value & 0x01);
        }

        data.RequireEmpty("image mirror");
        return title;
    }

    public static string Pixi(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"PixelInformation '{header.Type}'");
        if (!data.TryFullBox(out _, out _) || !data.TryByte("num_channels", out var channels))
        {
            return title;
        }

        for (var index = 0; index < channels; index++)
        {
            if (!data.TryByte(string.Create(CultureInfo.InvariantCulture, $"bits_per_channel[{index}]"), out _))
            {
                break;
            }
        }

        data.RequireEmpty("pixel information");
        return title;
    }

    public static string Colr(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"ColourInformation '{header.Type}'");
        if (!data.TryFourCc("colour_type", out var colourType))
        {
            return title;
        }

        switch (colourType)
        {
            case "nclx":
            case "nclc":
                data.TryUInt16("colour_primaries", out _);
                data.TryUInt16("transfer_characteristics", out _);
                data.TryUInt16("matrix_coefficients", out _);
                if (colourType == "nclx" && data.TryByte("full_range_flag", out var fullRange))
                {
                    fields.Raw("full_range_flag", (fullRange >> 7) & 0x01);
                    fields.Raw("reserved", string.Create(CultureInfo.InvariantCulture, $"0x{fullRange & 0x7F:X2}"));
                }

                break;
            default:
                fields.Derived("<payload_length>", data.Remaining);
                break;
        }

        data.RequireEmpty("colour information");
        return title;
    }

    public static string AuxC(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        const string title = "AuxiliaryTypeProperty 'auxC'";
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        var auxType = IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes);
        fields.Raw("aux_type", IsoBmffFormatting.Display(auxType));

        var subtype = data.ReadRemainingPrefix(64, out var subtypeLength, out _);
        fields.Derived("<aux_subtype_size>", subtypeLength);
        if (subtypeLength > 0)
        {
            fields.Raw("aux_subtype", IsoBmffFormatting.Hex(subtype));
        }

        return title;
    }

    public static string Av1C(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"AV1ConfigurationBox '{header.Type}'");
        if (!data.TryReadUInt8Raw(out var markerAndVersion))
        {
            return title;
        }

        fields.Raw("marker", (markerAndVersion >> 7) & 0x01);
        fields.Raw("version", markerAndVersion & 0x7F);

        if (!data.TryByte("seq_profile_and_level", out var profileAndLevel))
        {
            return title;
        }

        fields.Raw("seq_profile", (profileAndLevel >> 5) & 0x07);
        fields.Raw("seq_level_idx_0", profileAndLevel & 0x1F);

        if (!data.TryByte("tier_and_flags", out var tierFlags))
        {
            return title;
        }

        fields.Raw("seq_tier_0", (tierFlags >> 7) & 0x01);
        fields.Raw("high_bitdepth", (tierFlags >> 6) & 0x01);
        fields.Raw("twelve_bit", (tierFlags >> 5) & 0x01);
        fields.Raw("monochrome", (tierFlags >> 4) & 0x01);
        fields.Raw("chroma_subsampling_x", (tierFlags >> 3) & 0x01);
        fields.Raw("chroma_subsampling_y", (tierFlags >> 2) & 0x01);
        fields.Raw("chroma_sample_position", tierFlags & 0x03);

        if (data.TryByte("delay_and_reserved", out var delay))
        {
            fields.Raw("reserved", string.Create(CultureInfo.InvariantCulture, $"0x{delay >> 5:X2}"));
            fields.Raw("initial_presentation_delay_present", (delay >> 4) & 0x01);
            if (((delay >> 4) & 0x01) != 0)
            {
                fields.Raw("initial_presentation_delay_minus_one", delay & 0x0F);
            }
            else
            {
                fields.Raw("reserved2", delay & 0x0F);
            }
        }

        data.RequireEmpty("AV1 configuration");
        return title;
    }

    public static string HvcC(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"HEVCConfigurationBox '{header.Type}'");
        if (!data.TryByte("configurationVersion", out _))
        {
            return title;
        }

        if (data.TryByte("generalProfileSpaceTierFlag", out var profileSpaceTier))
        {
            fields.Raw("generalProfileSpaceTierFlag", string.Create(CultureInfo.InvariantCulture, $"0x{profileSpaceTier:X2}"));
        }

        data.TryByte("generalProfileIdc", out _);
        if (data.TryReadUInt32Raw(out var compatibility))
        {
            fields.Raw("generalProfileCompatibilityFlags", string.Create(CultureInfo.InvariantCulture, $"0x{compatibility:X8}"));
        }

        if (data.TryUInt64("generalConstraintIndicator", out var constraint))
        {
            fields.Raw("generalConstraintIndicator", string.Create(CultureInfo.InvariantCulture, $"0x{constraint:X16}"));
        }

        data.TryByte("levelIdc", out _);
        data.TryUInt16("minSpatialSegmentation", out _);
        data.TryByte("parallelismType", out _);
        data.TryByte("chromaFormat", out _);
        data.TryByte("bitDepthLumaMinus8", out _);
        data.TryByte("bitDepthChromaMinus8", out _);
        data.TryUInt16("avgFrameRate", out _);

        if (!data.TryByte("frame_rate_and_length", out var packed))
        {
            return title;
        }

        fields.Raw("constantFrameRate", (packed >> 6) & 0x03);
        fields.Raw("numTemporalLayers", (packed >> 3) & 0x07);
        fields.Raw("temporalIdNested", (packed >> 2) & 0x01);
        fields.Raw("lengthSizeMinusOne", packed & 0x03);

        if (!data.TryByte("numOfArrays", out var arrayCount))
        {
            return title;
        }

        for (var arrayIndex = 0; arrayIndex < arrayCount; arrayIndex++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryByte("array_header", out var arrayHeader))
            {
                break;
            }

            var nalType = arrayHeader & 0x3F;
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"nal_array[{arrayIndex}]"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"nal_type={nalType} complete={(arrayHeader >> 7) & 0x01}"));

            if (!data.TryUInt16("nal_count", out var nalCount))
            {
                break;
            }

            for (var nalIndex = 0; nalIndex < nalCount; nalIndex++)
            {
                context.ThrowIfCancellationRequested();
                if (!data.TryReadUInt16Raw(out var nalLength))
                {
                    fields.Warning(DiagnosticCodes.TruncatedPayload, "hvcC NAL array is truncated.");
                    data.SkipRemaining();
                    return title;
                }

                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"nal_array[{arrayIndex}].nal[{nalIndex}]"),
                    string.Create(CultureInfo.InvariantCulture, $"size={nalLength}"));
                if (!data.TrySkip(nalLength, "NAL unit"))
                {
                    data.SkipRemaining();
                    return title;
                }
            }
        }

        data.RequireEmpty("HEVC configuration");
        return title;
    }

    public static string Clli(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"ContentLightLevelInfo '{header.Type}'");
        data.TryUInt16("max_content_light_level", out _);
        data.TryUInt16("max_pic_average_light_level", out _);
        data.RequireEmpty("content light level information");
        return title;
    }

    public static string Ipma(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"ItemPropertyAssociation '{header.Type}'");
        if (!data.TryFullBox(out var version, out var flags))
        {
            return title;
        }

        var largeItemId = version == 1;
        var wideIndex = (flags & 0x000001) != 0;
        fields.Derived("<large_item_id>", largeItemId ? "1" : "0");
        fields.Derived("<property_index_16bit>", wideIndex ? "1" : "0");

        if (!data.TryUInt32("entry_count", out var declared))
        {
            return title;
        }

        var entries = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < entries; index++)
        {
            context.ThrowIfCancellationRequested();
            var prefix = string.Create(CultureInfo.InvariantCulture, $"item[{index}]");

            if (largeItemId)
            {
                if (!data.TryReadUInt32Raw(out var itemId))
                {
                    break;
                }

                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.item_ID"), itemId);
            }
            else
            {
                if (!data.TryReadUInt16Raw(out var itemId))
                {
                    break;
                }

                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.item_ID"), itemId);
            }

            if (!data.TryReadUInt8Raw(out var associationCount))
            {
                break;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.association_count"), associationCount);

            for (var association = 0; association < associationCount; association++)
            {
                context.ThrowIfCancellationRequested();
                var associationPrefix = string.Create(CultureInfo.InvariantCulture, $"{prefix}.assoc[{association}]");
                if (wideIndex)
                {
                    if (!data.TryReadUInt16Raw(out var value))
                    {
                        break;
                    }

                    fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{associationPrefix}.essential"), (value >> 15) & 0x01);
                    fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{associationPrefix}.property_index"), value & 0x7FFF);
                }
                else
                {
                    if (!data.TryReadUInt8Raw(out var value))
                    {
                        break;
                    }

                    fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{associationPrefix}.essential"), (value >> 7) & 0x01);
                    fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{associationPrefix}.property_index"), value & 0x7F);
                }
            }
        }

        data.RequireEmpty("item property association");
        return title;
    }

    public static string EntityToGroup(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields,
        string fourCc)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"EntityToGroupBox '{fourCc}'");
        if (!data.TryFullBox(out _, out _))
        {
            return title;
        }

        data.TryUInt32("group_id", out _);
        if (!data.TryUInt32("num_entities_in_group", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"entity_id[{index}]"), out _))
            {
                break;
            }
        }

        var trailing = data.ReadRemainingPrefix(64, out var trailingLength, out _);
        fields.Derived("<trailing_data_size>", trailingLength);
        if (trailingLength > 0)
        {
            fields.Raw("trailing_data", IsoBmffFormatting.Hex(trailing));
        }

        return title;
    }
}
