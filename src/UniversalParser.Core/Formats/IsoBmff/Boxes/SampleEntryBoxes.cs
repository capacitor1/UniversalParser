using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

#pragma warning disable IDE0060 // Uniform box-parser signature keeps the dispatcher simple.

namespace UniversalParser.Core.Formats.IsoBmff.Boxes;

/// <summary>
/// 'stsd' stays a tree leaf; its sample entries and codec configuration boxes are reported as
/// dotted fields such as entry[0].avcC.configuration_version.
/// </summary>
internal static class SampleEntryBoxes
{
    private const int CompressorNameLength = 32;

    private enum EntryKind
    {
        Unknown,
        Video,
        Audio,
        Text,
        Metadata,
        Hint,
    }

    private sealed class EntryContext
    {
        public string EntryType = string.Empty;
        public EntryKind Kind;
        public int ChannelCount;
        public int SampleSize;
        public double SampleRate;
        public int Width;
        public int Height;
    }

    private static readonly HashSet<string> VideoTypes = new(StringComparer.Ordinal)
    {
        "avc1", "avc2", "avc3", "avc4", "hvc1", "hev1", "hvt1", "dvh1", "dvhe", "dva1", "dvav",
        "av01", "vp08", "vp09", "mp4v", "s263", "encv", "mjp2", "jpeg", "raw ", "apch", "apcn",
        "apcs", "apco", "ap4h",
    };

    private static readonly HashSet<string> AudioTypes = new(StringComparer.Ordinal)
    {
        "mp4a", "enca", "ac-3", "ec-3", "ac-4", "dtsc", "dtse", "dtsh", "dtsl", "Opus", "fLaC",
        "alac", "samr", "sawb", "sowt", "twos", "lpcm", "ipcm", "fpcm", ".mp3", "mp3 ", "MAC3",
        "ulaw", "alaw",
    };

    private static readonly HashSet<string> TextTypes = new(StringComparer.Ordinal)
    {
        "tx3g", "text", "wvtt", "stpp", "stxt", "c608", "c708", "subt", "sbtl", "enct",
    };

    private static readonly HashSet<string> MetadataTypes = new(StringComparer.Ordinal)
    {
        "mebx", "metx", "mett", "urim", "tmcd", "tc64", "gpmd", "camm", "rtmd", "fdsc", "djmd",
        "dbgi", "ctmd", "psmd", "ssmd", "marl", "mari",
    };

    public static string Stsd(
        ParseContext context,
        FormatNode node,
        IsoBmffBoxHeader header,
        IsoBmffBoxReader data,
        FieldList fields)
    {
        var title = string.Create(CultureInfo.InvariantCulture, $"SampleDescription '{header.Type}'");
        if (!data.TryFullBox(out _, out _) || !data.TryUInt32("entry_count", out var declared))
        {
            return title;
        }

        var rows = IsoBmffFieldHelpers.RowLimit(context, declared, header.Type, fields);
        var position = data.Position;
        var end = header.End;

        for (var index = 0; index < rows; index++)
        {
            context.ThrowIfCancellationRequested();
            if (position + 8 > end)
            {
                break;
            }

            if (!IsoBmffBoxHeader.TryRead(context.Source, position, end, allowNumericType: false, out var entry, out _))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"Sample entry {index} has an unreadable header.");
                break;
            }

            var prefix = string.Create(CultureInfo.InvariantCulture, $"entry[{index}].");
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}type"), entry.Type);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}size"), entry.TotalSize);

            var entryData = new IsoBmffBoxReader(context.Source, entry.PayloadOffset, entry.PayloadLength, fields);
            var entryContext = ParseSampleEntry(context, entryData, fields, prefix, entry.Type);
            WalkChildren(context, entryData.Position, entry.End, fields, prefix, entryContext);

            position = entry.End;
            if (entry.Truncated)
            {
                break;
            }
        }

        data.SkipRemaining();
        return title;
    }

    private static EntryContext ParseSampleEntry(
        ParseContext context,
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix,
        string entryType)
    {
        var entry = new EntryContext { EntryType = entryType, Kind = KindOf(entryType) };
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}kind"), entry.Kind.ToString());

        if (!data.TrySkip(6, "reserved"))
        {
            return entry;
        }

        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}data_reference_index"), out _);

        switch (entry.Kind)
        {
            case EntryKind.Video:
                ParseVisual(context, data, fields, prefix, entry);
                break;
            case EntryKind.Audio:
                ParseAudio(data, fields, prefix, entry);
                break;
            case EntryKind.Text:
                ParseText(context, data, fields, prefix, entryType);
                break;
            case EntryKind.Metadata:
                ParseMetadata(context, data, fields, prefix, entryType);
                break;
            case EntryKind.Hint:
                fields.Note("Hint sample entry body is not decoded.", data.CurrentRange);
                break;
            default:
                var raw = data.ReadRemainingPrefix(32, out var length, out _);
                fields.Derived(string.Create(CultureInfo.InvariantCulture, $"{prefix}<payload_length>"), length);
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}raw"), IsoBmffFormatting.Hex(raw));
                break;
        }

        return entry;
    }

    private static void ParseVisual(
        ParseContext context,
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix,
        EntryContext entry)
    {
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}pre_defined"), out _);
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}reserved1"), out _);

        for (var index = 0; index < 3; index++)
        {
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}pre_defined2[{index}]"), out _);
        }

        if (data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}width"), out var width))
        {
            entry.Width = width;
        }

        if (data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}height"), out var height))
        {
            entry.Height = height;
        }

        if (data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}horizresolution"), out var horizontal)
            && data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}vertresolution"), out var vertical))
        {
            fields.Derived(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}<resolution>"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(horizontal / 65536.0):F2} x {(vertical / 65536.0):F2} dpi"));
        }

        data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}reserved2"), out _);
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}frame_count"), out _);

        Span<byte> name = stackalloc byte[CompressorNameLength];
        if (data.TryReadBytesRaw(name))
        {
            var length = Math.Min((int)name[0], CompressorNameLength - 1);
            var text = Encoding.ASCII.GetString(name[1..(1 + length)]).TrimEnd('\0');
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}compressor_name"),
                IsoBmffFormatting.Display(text));
        }

        if (data.TryReadUInt16Raw(out var depth))
        {
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}depth"),
                string.Create(CultureInfo.InvariantCulture, $"{depth}{DepthNote(depth)}"));
        }

        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}pre_defined3"), out _);
    }

    private static string DepthNote(ushort depth) => depth switch
    {
        24 => " (colour, no alpha)",
        32 => " (colour, alpha)",
        40 => " (colour, no alpha, 10-bit)",
        48 => " (colour, alpha, 10-bit)",
        56 => " (colour, alpha, 12-bit)",
        _ => string.Empty,
    };

    private static void ParseAudio(IsoBmffBoxReader data, FieldList fields, string prefix, EntryContext entry)
    {
        if (!data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}sound_version"), out var version))
        {
            return;
        }

        if (version == 0)
        {
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}revision"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}vendor"), out _);
            if (data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}channel_count"), out var channels))
            {
                entry.ChannelCount = channels;
            }

            if (data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}sample_size"), out var sampleSize))
            {
                entry.SampleSize = sampleSize;
            }

            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}pre_defined"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}reserved"), out _);
            if (data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}sample_rate"), out var rate))
            {
                entry.SampleRate = rate / 65536.0;
                fields.Derived(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}<sample_rate>"),
                    entry.SampleRate.ToString("F3", CultureInfo.InvariantCulture) + " Hz");
            }

            return;
        }

        if (version == 1)
        {
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}revision"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}vendor"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}channel_count"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}sample_size"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}pre_defined"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}reserved"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}sample_rate"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}samples_per_packet"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}bytes_per_packet"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}bytes_per_frame"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}bytes_per_sample"), out _);
            return;
        }

        if (version == 2)
        {
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.revision"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.vendor"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.always3"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.always16"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.always_minus2"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.always0"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.always65536"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.struct_size"), out _);
            if (data.TryReadUInt64Raw(out var rateBits))
            {
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.sample_rate"),
                    BitConverter.Int64BitsToDouble(unchecked((long)rateBits)).ToString("F3", CultureInfo.InvariantCulture));
            }

            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.channels"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.always7F000000"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.const_bits_per_channel"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.format_flags"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.bytes_per_packet"), out _);
            data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}v2.frames_per_packet"), out _);
            return;
        }

        fields.Warning(
            DiagnosticCodes.UnsupportedVersion,
            string.Create(CultureInfo.InvariantCulture, $"Unsupported audio sample entry version {version}."));
        data.SkipRemaining();
    }

    private static void ParseText(
        ParseContext context,
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix,
        string entryType)
    {
        switch (entryType)
        {
            case "tx3g":
                ParseTx3g(data, fields, prefix);
                break;
            case "text":
                ParseQuickTimeText(data, fields, prefix);
                break;
            case "stpp":
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}namespace"),
                    IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}schema_location"),
                    IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}auxiliary_mime_types"),
                    IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                break;
            case "wvtt":
            case "stxt":
                fields.Note("The value is carried by child boxes.", data.CurrentRange);
                break;
            default:
                var payload = data.ReadRemainingPrefix(64, out var length, out _);
                fields.Derived(string.Create(CultureInfo.InvariantCulture, $"{prefix}<payload_length>"), length);
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}payload"), IsoBmffFormatting.Hex(payload));
                break;
        }
    }

    private static void ParseTx3g(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}display_flags"), out _);

        if (data.TryReadUInt8Raw(out var horizontal))
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}horizontal_justification"), unchecked((sbyte)horizontal));
        }

        if (data.TryReadUInt8Raw(out var vertical))
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}vertical_justification"), unchecked((sbyte)vertical));
        }

        ReadColor(data, fields, string.Create(CultureInfo.InvariantCulture, $"{prefix}background_color_rgba"));
        ReadRectangle(data, fields, string.Create(CultureInfo.InvariantCulture, $"{prefix}default_text_box"));

        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}default_style.start_char"), out _);
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}default_style.end_char"), out _);
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}default_style.font_ID"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}default_style.face_style_flags"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}default_style.font_size"), out _);
        ReadColor(data, fields, string.Create(CultureInfo.InvariantCulture, $"{prefix}default_style.text_color_rgba"));
    }

    private static void ParseQuickTimeText(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}display_flags"), out _);
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}text_justification"), out _);
        ReadColor16(data, fields, string.Create(CultureInfo.InvariantCulture, $"{prefix}background_color"));
        ReadRectangle(data, fields, string.Create(CultureInfo.InvariantCulture, $"{prefix}default_text_box"));
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}font_number"), out _);
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}font_face"), out _);
        ReadColor16(data, fields, string.Create(CultureInfo.InvariantCulture, $"{prefix}foreground_color"));

        if (data.TryReadUInt8Raw(out var length))
        {
            var bytes = new byte[length];
            if (data.TryReadBytesRaw(bytes))
            {
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}font_name"),
                    IsoBmffFormatting.Display(Encoding.ASCII.GetString(bytes)));
            }
        }
    }

    private static void ParseMetadata(
        ParseContext context,
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix,
        string entryType)
    {
        switch (entryType)
        {
            case "mebx":
            case "urim":
                fields.Note("The value is carried by child boxes.", data.CurrentRange);
                break;
            case "tmcd":
            case "tc64":
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}flags"), out _);
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}time_scale"), out _);
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}frame_duration"), out _);
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}number_of_frames"), out _);
                data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}reserved"), out _);
                break;
            case "metx":
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}content_encoding"),
                    IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}namespace"),
                    IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}schema_location"),
                    IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                break;
            case "mett":
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}content_encoding"),
                    IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}mime_format"),
                    IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                break;
            default:
                fields.Note("Vendor metadata sample entry; the payload is not decoded.", data.CurrentRange);
                data.SkipRemaining();
                break;
        }
    }

    private static void WalkChildren(
        ParseContext context,
        long start,
        long end,
        FieldList fields,
        string prefix,
        EntryContext entry)
    {
        var position = start;
        while (position + 8 <= end)
        {
            context.ThrowIfCancellationRequested();
            if (!IsoBmffBoxHeader.TryRead(context.Source, position, end, allowNumericType: false, out var child, out _))
            {
                break;
            }

            var childPrefix = string.Create(CultureInfo.InvariantCulture, $"{prefix}{child.Type}.");
            var childData = new IsoBmffBoxReader(context.Source, child.PayloadOffset, child.PayloadLength, fields);

            if (child.Type is "sinf" or "schi" or "wave" or "rinf")
            {
                WalkChildren(context, child.PayloadOffset, child.End, fields, childPrefix, entry);
            }
            else
            {
                DispatchChild(context, child, childData, fields, childPrefix, entry);
            }

            position = child.End;
            if (child.Truncated || child.End <= child.Start)
            {
                break;
            }
        }
    }

    private static void DispatchChild(
        ParseContext context,
        IsoBmffBoxHeader child,
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix,
        EntryContext entry)
    {
        switch (child.Type)
        {
            case "avcC":
                ParseAvcC(data, fields, prefix);
                break;
            case "hvcC":
            case "hvcE":
                ParseHvcC(context, data, fields, prefix);
                break;
            case "esds":
                ParseEsds(context, data, fields, prefix);
                break;
            case "av1C":
                ParseAv1C(data, fields, prefix);
                break;
            case "dfLa":
                ParseDfLa(context, data, fields, prefix);
                break;
            case "dOps":
                ParseDOps(data, fields, prefix);
                break;
            case "dac3":
                ParseDac3(data, fields, prefix);
                break;
            case "dec3":
                ParseDec3(data, fields, prefix);
                break;
            case "dvcC":
            case "dvvC":
            case "dvwC":
                ParseDolbyVision(data, fields, prefix);
                break;
            case "vpcC":
                ParseVpcC(data, fields, prefix);
                break;
            case "SmDm":
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}primary_r_x"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}primary_r_y"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}primary_g_x"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}primary_g_y"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}primary_b_x"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}primary_b_y"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}white_point_x"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}white_point_y"), out _);
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}luminance_max"), out _);
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}luminance_min"), out _);
                break;
            case "CoLL":
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}max_cll"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}max_fall"), out _);
                break;
            case "btrt":
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}bufferSizeDB"), out _);
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}maxBitrate"), out _);
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}avgBitrate"), out _);
                break;
            case "pasp":
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}hSpacing"), out _);
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}vSpacing"), out _);
                break;
            case "colr":
                ParseColr(data, fields, prefix);
                break;
            case "clap":
                for (var index = 0; index < 8; index++)
                {
                    data.TryUInt32(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{prefix}{(index % 2 == 0 ? "size" : "denominator")}[{index / 2}]"),
                        out _);
                }

                break;
            case "srat":
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}audio_sample_rate"), out _);
                break;
            case "frma":
                data.TryFourCc(string.Create(CultureInfo.InvariantCulture, $"{prefix}data_format"), out _);
                break;
            case "schm":
                if (data.TryFullBox(out _, out var schmFlags))
                {
                    data.TryFourCc(string.Create(CultureInfo.InvariantCulture, $"{prefix}scheme_type"), out _);
                    data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}scheme_version"), out _);
                    if ((schmFlags & 0x000001) != 0)
                    {
                        fields.Raw(
                            string.Create(CultureInfo.InvariantCulture, $"{prefix}scheme_uri"),
                            IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                    }
                }

                break;
            case "uri ":
            case "uriI":
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}uri"),
                    IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                break;
            case "name":
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}name"),
                    IsoBmffFormatting.Display(IsoBmffFieldHelpers.ReadCString(data, context.Limits.MaxStringBytes)));
                break;
            case "damr":
                data.TryFourCc(string.Create(CultureInfo.InvariantCulture, $"{prefix}vendor"), out _);
                data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}decoder_version"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}mode_set"), out _);
                data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}active_modes"), out _);
                data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}mode_change_period"), out _);
                data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}frames_per_sample"), out _);
                break;
            case "alac":
                ParseAlac(data, fields, prefix);
                break;
            case "mhaC":
                data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}configuration_version"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}profile_level_indication"), out _);
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}reference_channel_layout"), out _);
                if (data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}mpegh3da_config_length"), out var mpeghLength))
                {
                    fields.Raw(
                        string.Create(CultureInfo.InvariantCulture, $"{prefix}mpegh3da_config"),
                        IsoBmffFormatting.Hex(data.ReadBlock(mpeghLength, 64, out _)));
                    fields.Note("MPEG-H 3D audio configuration bytes are reported as a hex prefix only and are not decoded.");
                }

                break;
            case "pcmC":
                if (data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}format_flags"), out var formatFlags))
                {
                    fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}format"), (formatFlags >> 4) & 0x0F);
                    fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}endianness"), (formatFlags >> 3) & 0x01);
                }

                data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}pcm_sample_size"), out _);
                break;
            case "chnl":
                ParseChnl(data, fields, prefix, entry);
                break;
            case "dawp":
                data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}payload_length"), out _);
                break;
            case "d263":
                data.TryFourCc(string.Create(CultureInfo.InvariantCulture, $"{prefix}vendor"), out _);
                data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}decoder_version"), out _);
                data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}level"), out _);
                data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}profile"), out _);
                fields.Note("H.263 picture headers are not decoded; only the d263 configuration fields are reported.");
                break;
            case "enda":
                data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}endianness"), out _);
                break;
            case "keys":
                fields.Note("Metadata key table inside a sample entry is not decoded.", data.CurrentRange);
                data.SkipRemaining();
                break;
            default:
                var raw = data.ReadRemainingPrefix(32, out var length, out _);
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}size"), child.PayloadLength);
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}raw"), IsoBmffFormatting.Hex(raw));
                break;
        }
    }

    private static void ParseAvcC(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        if (!data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}configuration_version"), out _))
        {
            return;
        }

        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}profile"), out var profile);
        if (data.TryReadUInt8Raw(out var compatibility))
        {
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}profile_compatibility"),
                string.Create(CultureInfo.InvariantCulture, $"0x{compatibility:X2}"));
        }

        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}level"), out var level);

        if (!data.TryReadUInt8Raw(out var lengthByte) || !data.TryReadUInt8Raw(out var spsByte))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}length_size_minus_one"), lengthByte & 0x03);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}nal_length_size"), (lengthByte & 0x03) + 1);
        var spsCount = spsByte & 0x1F;
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}num_of_sps"), spsCount);
        ReadNalUnits(data, fields, prefix, "sps", spsCount);

        if (!data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}num_of_pps"), out var ppsCount))
        {
            return;
        }

        ReadNalUnits(data, fields, prefix, "pps", ppsCount);

        if (profile is 100 or 110 or 122 or 144)
        {
            if (data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}chroma_format"), out var chroma))
            {
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}chroma_format"), chroma & 0x03);
            }

            if (data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}bit_depth_luma_minus8"), out var luma))
            {
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}bit_depth_luma_minus8"), luma & 0x07);
            }

            if (data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}bit_depth_chroma_minus8"), out var chromaDepth))
            {
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}bit_depth_chroma_minus8"), chromaDepth & 0x07);
            }

            if (data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}num_of_sps_ext"), out var extCount))
            {
                ReadNalUnits(data, fields, prefix, "sps_ext", extCount);
            }
        }

        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}codec_string"),
            string.Create(CultureInfo.InvariantCulture, $"avc1.{profile:X2}{compatibility:X2}{level:X2}"));
        data.SkipRemaining();
    }

    private static void ReadNalUnits(
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix,
        string name,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            if (!data.TryReadUInt16Raw(out var length))
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"{name} NAL unit {index} is truncated.");
                return;
            }

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}{name}[{index}]"),
                string.Create(CultureInfo.InvariantCulture, $"size={length}"));
            if (length > data.Remaining)
            {
                fields.Warning(DiagnosticCodes.TruncatedPayload, $"{name} NAL unit {index} extends past the box.");
                return;
            }

            var nal = data.ReadBlock(length, 4096, out _);
            if (length <= 4096)
            {
                if (name == "sps")
                {
                    ParseH264Sps(nal, fields, prefix);
                }
                else if (name == "pps")
                {
                    ParseH264Pps(nal, fields, prefix);
                }
            }
        }
    }

    /// <summary>Decodes the geometry fields of an H.264 sequence parameter set (ITU-T H.264 §7.3.2.1.1).</summary>
    private static void ParseH264Sps(ReadOnlySpan<byte> nal, FieldList fields, string prefix)
    {
        var rbsp = RemoveEmulationPrevention(nal);
        if (rbsp.Length < 4)
        {
            return;
        }

        // The first byte is the NAL unit header (nal_ref_idc / nal_unit_type).
        var bits = new BitReader(rbsp, 8);
        if (!bits.TryRead(8, out var profileIdc)
            || !bits.TryRead(8, out var constraintFlags)
            || !bits.TryRead(8, out var levelIdc))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.profile_idc"), profileIdc);
        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.constraint_set_flags"),
            string.Create(CultureInfo.InvariantCulture, $"0x{constraintFlags:X2}"));
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.level_idc"), levelIdc);

        if (!bits.TryReadUE(out var spsId))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.seq_parameter_set_id"), spsId);

        var chromaFormatIdc = 1;
        if (profileIdc is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
        {
            if (!bits.TryReadUE(out var chroma))
            {
                return;
            }

            chromaFormatIdc = (int)chroma;
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.chroma_format_idc"), chroma);
            if (chroma == 3)
            {
                ReadBits(ref bits, fields, prefix, "sps.separate_colour_plane_flag", 1);
            }

            if (!bits.TryReadUE(out var bitDepthLuma) || !bits.TryReadUE(out var bitDepthChroma))
            {
                return;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.bit_depth_luma_minus8"), bitDepthLuma);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.bit_depth_chroma_minus8"), bitDepthChroma);
            ReadBits(ref bits, fields, prefix, "sps.qpprime_y_zero_transform_bypass_flag", 1);
            if (!bits.TryRead(1, out var scalingMatrixPresent))
            {
                return;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.seq_scaling_matrix_present_flag"), scalingMatrixPresent);
            if (scalingMatrixPresent != 0)
            {
                var listCount = chromaFormatIdc != 3 ? 8 : 12;
                for (var index = 0; index < listCount; index++)
                {
                    if (!bits.TryRead(1, out var listPresent))
                    {
                        return;
                    }

                    if (listPresent != 0 && !SkipScalingList(ref bits, index < 6 ? 16 : 64))
                    {
                        return;
                    }
                }
            }
        }

        if (!bits.TryReadUE(out var log2MaxFrameNum) || !bits.TryReadUE(out var picOrderCntType))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.log2_max_frame_num_minus4"), log2MaxFrameNum);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.pic_order_cnt_type"), picOrderCntType);

        if (picOrderCntType == 0)
        {
            if (!bits.TryReadUE(out var log2MaxPicOrderCnt))
            {
                return;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.log2_max_pic_order_cnt_lsb_minus4"), log2MaxPicOrderCnt);
        }
        else if (picOrderCntType == 1)
        {
            ReadBits(ref bits, fields, prefix, "sps.delta_pic_order_always_zero_flag", 1);
            if (!bits.TryReadSE(out var offsetNonRef) || !bits.TryReadSE(out var offsetTopBottom))
            {
                return;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.offset_for_non_ref_pic"), offsetNonRef);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.offset_for_top_to_bottom_field"), offsetTopBottom);
            if (!bits.TryReadUE(out var cycleLength))
            {
                return;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.num_ref_frames_in_pic_order_cnt_cycle"), cycleLength);
            for (var index = 0u; index < cycleLength; index++)
            {
                if (!bits.TryReadSE(out _))
                {
                    return;
                }
            }
        }

        if (!bits.TryReadUE(out var maxNumRefFrames))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.max_num_ref_frames"), maxNumRefFrames);
        ReadBits(ref bits, fields, prefix, "sps.gaps_in_frame_num_value_allowed_flag", 1);

        if (!bits.TryReadUE(out var widthInMbs) || !bits.TryReadUE(out var heightInMapUnits))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.pic_width_in_mbs_minus1"), widthInMbs);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.pic_height_in_map_units_minus1"), heightInMapUnits);

        if (!bits.TryRead(1, out var frameMbsOnly))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.frame_mbs_only_flag"), frameMbsOnly);
        if (frameMbsOnly == 0)
        {
            ReadBits(ref bits, fields, prefix, "sps.mb_adaptive_frame_field_flag", 1);
        }

        ReadBits(ref bits, fields, prefix, "sps.direct_8x8_inference_flag", 1);

        uint cropLeft = 0;
        uint cropRight = 0;
        uint cropTop = 0;
        uint cropBottom = 0;
        if (!bits.TryRead(1, out var croppingFlag))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.frame_cropping_flag"), croppingFlag);
        if (croppingFlag != 0)
        {
            if (!bits.TryReadUE(out cropLeft) || !bits.TryReadUE(out cropRight)
                || !bits.TryReadUE(out cropTop) || !bits.TryReadUE(out cropBottom))
            {
                return;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.frame_crop_left_offset"), cropLeft);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.frame_crop_right_offset"), cropRight);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.frame_crop_top_offset"), cropTop);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.frame_crop_bottom_offset"), cropBottom);
        }

        ReadBits(ref bits, fields, prefix, "sps.vui_parameters_present_flag", 1);

        var (subWidth, subHeight) = chromaFormatIdc switch
        {
            1 => (2u, 2u),
            2 => (2u, 1u),
            _ => (1u, 1u),
        };

        var codedWidth = (widthInMbs + 1) * 16u;
        var codedHeight = (2u - frameMbsOnly) * (heightInMapUnits + 1) * 16u;
        var width = (long)codedWidth - (cropLeft + cropRight) * subWidth;
        var height = (long)codedHeight - (cropTop + cropBottom) * subHeight;
        if (width > 0 && height > 0)
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.width"), width);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}sps.height"), height);
        }
    }

    /// <summary>Decodes the first fields of an H.264 picture parameter set (ITU-T H.264 §7.3.2.2).</summary>
    private static void ParseH264Pps(ReadOnlySpan<byte> nal, FieldList fields, string prefix)
    {
        var rbsp = RemoveEmulationPrevention(nal);
        if (rbsp.Length < 2)
        {
            return;
        }

        var bits = new BitReader(rbsp, 8);
        if (!bits.TryReadUE(out var ppsId) || !bits.TryReadUE(out var spsId))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}pps.pic_parameter_set_id"), ppsId);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}pps.seq_parameter_set_id"), spsId);
        ReadBits(ref bits, fields, prefix, "pps.entropy_coding_mode_flag", 1);
        ReadBits(ref bits, fields, prefix, "pps.bottom_field_pic_order_in_frame_present_flag", 1);
        if (bits.TryReadUE(out var sliceGroups))
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}pps.num_slice_groups_minus1"), sliceGroups);
        }
    }

    private static bool SkipScalingList(ref BitReader bits, int size)
    {
        var lastScale = 8;
        var nextScale = 8;
        for (var index = 0; index < size; index++)
        {
            if (nextScale != 0)
            {
                if (!bits.TryReadSE(out var deltaScale))
                {
                    return false;
                }

                nextScale = (lastScale + deltaScale + 256) % 256;
            }

            lastScale = nextScale == 0 ? lastScale : nextScale;
        }

        return true;
    }

    /// <summary>Removes 0x00 0x00 0x03 emulation-prevention bytes before bit-level parsing.</summary>
    private static byte[] RemoveEmulationPrevention(ReadOnlySpan<byte> nal)
    {
        var output = new byte[nal.Length];
        var length = 0;
        var zeros = 0;
        foreach (var value in nal)
        {
            if (zeros >= 2 && value == 0x03)
            {
                zeros = 0;
                continue;
            }

            output[length++] = value;
            zeros = value == 0x00 ? zeros + 1 : 0;
        }

        return output.AsSpan(0, length).ToArray();
    }

    private static void ParseHvcC(
        ParseContext context,
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix)
    {
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}configuration_version"), out _);

        if (data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}profile_space_and_tier"), out var spaceTier))
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}profile_space"), (spaceTier >> 6) & 0x03);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}tier"), (spaceTier >> 5) & 0x01);
        }

        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}profile"), out _);
        if (data.TryReadUInt32Raw(out var compatibility))
        {
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}profile_compatibility_flags"),
                string.Create(CultureInfo.InvariantCulture, $"0x{compatibility:X8}"));
        }

        Span<byte> constraint = stackalloc byte[6];
        if (data.TryReadBytesRaw(constraint))
        {
            ulong constraintValue = 0;
            foreach (var value in constraint)
            {
                constraintValue = constraintValue << 8 | value;
            }

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}constraint_flags"),
                string.Create(CultureInfo.InvariantCulture, $"0x{constraintValue:X12}"));
        }

        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}level"), out _);
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}min_spatial_segmentation_idc"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}parallelism_type"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}chroma_format"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}bit_depth_luma_minus8"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}bit_depth_chroma_minus8"), out _);
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}avg_frame_rate"), out _);

        if (!data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}frame_rate_and_length"), out var packed))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}constant_frame_rate"), (packed >> 6) & 0x03);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}num_temporal_layers"), (packed >> 3) & 0x07);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}temporal_id_nested"), (packed >> 2) & 0x01);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}nal_length_size"), (packed & 0x03) + 1);

        if (!data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}num_of_arrays"), out var arrayCount))
        {
            return;
        }

        for (var arrayIndex = 0; arrayIndex < arrayCount; arrayIndex++)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}array_header"), out var arrayHeader))
            {
                break;
            }

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}array[{arrayIndex}].nal_unit_type"),
                arrayHeader & 0x3F);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}array[{arrayIndex}].array_completeness"),
                (arrayHeader >> 7) & 0x01);

            if (!data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}array[{arrayIndex}].num_nalus"), out var nalCount))
            {
                break;
            }

            for (var nalIndex = 0; nalIndex < nalCount; nalIndex++)
            {
                if (!data.TryReadUInt16Raw(out var nalLength))
                {
                    data.SkipRemaining();
                    return;
                }

                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}array[{arrayIndex}].nalu[{nalIndex}]"),
                    string.Create(CultureInfo.InvariantCulture, $"size={nalLength}"));
                if (!data.TrySkip(nalLength, "NAL unit"))
                {
                    data.SkipRemaining();
                    return;
                }
            }
        }

        data.SkipRemaining();
        fields.Note("HEVC SPS/PPS/SEI payloads inside hvcC arrays are reported by size only and are not decoded.");
    }

    private static void ParseEsds(
        ParseContext context,
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix)
    {
        if (!data.TryFullBox(out _, out _))
        {
            return;
        }

        ParseDescriptor(context, data, fields, prefix, 0);
        data.SkipRemaining();
    }

    private static void ParseDescriptor(
        ParseContext context,
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix,
        int depth)
    {
        if (depth > 6 || data.Remaining < 2)
        {
            return;
        }

        if (!data.TryReadUInt8Raw(out var tag))
        {
            return;
        }

        var size = 0L;
        for (var index = 0; index < 4; index++)
        {
            if (!data.TryReadUInt8Raw(out var value))
            {
                return;
            }

            size = size << 7 | (uint)(value & 0x7F);
            if ((value & 0x80) == 0)
            {
                break;
            }
        }

        var body = data.Slice(size, fields);
        switch (tag)
        {
            case 0x03:
                if (body.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}es.es_id"), out _)
                    && body.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}es.flags"), out var flags))
                {
                    fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}es.stream_priority"), flags & 0x1F);
                    if ((flags & 0x80) != 0)
                    {
                        body.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}es.depends_on_es_id"), out _);
                    }

                    if ((flags & 0x40) != 0 && body.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}es.url_length"), out var urlLength))
                    {
                        body.ReadBlock(urlLength, context.Limits.MaxStringBytes, out _);
                    }

                    if ((flags & 0x20) != 0)
                    {
                        body.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}es.ocr_es_id"), out _);
                    }

                    ParseNestedDescriptors(context, body, fields, prefix, depth);
                }

                break;
            case 0x04:
                if (body.TryReadUInt8Raw(out var objectType))
                {
                    fields.Raw(
                        string.Create(CultureInfo.InvariantCulture, $"{prefix}dcd.object_type"),
                        string.Create(CultureInfo.InvariantCulture, $"{objectType} (0x{objectType:X2})"));
                    if (body.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}dcd.stream_type_flags"), out var streamFlags))
                    {
                        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}dcd.stream_type"), (streamFlags >> 2) & 0x3F);
                        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}dcd.up_stream"), (streamFlags >> 1) & 0x01);
                    }

                    body.TryUInt24(string.Create(CultureInfo.InvariantCulture, $"{prefix}dcd.buffer_size_db"), out _);
                    body.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}dcd.max_bitrate"), out _);
                    body.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}dcd.avg_bitrate"), out _);
                    ParseNestedDescriptors(context, body, fields, prefix, depth);
                }

                break;
            case 0x05:
                var dsi = body.ReadRemainingPrefix(context.Limits.MaxStringBytes, out var dsiLength, out _);
                fields.Derived(string.Create(CultureInfo.InvariantCulture, $"{prefix}dsi.bytes"), dsiLength);
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}dsi.raw"), IsoBmffFormatting.Hex(dsi, 32));
                ParseAudioSpecificConfig(dsi, fields, string.Create(CultureInfo.InvariantCulture, $"{prefix}dsi."));
                break;
            case 0x06:
                body.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}sl.predefined"), out _);
                break;
            default:
                fields.Derived(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}descriptor[{depth}].<size>"),
                    size);
                break;
        }
    }

    private static void ParseNestedDescriptors(
        ParseContext context,
        IsoBmffBoxReader body,
        FieldList fields,
        string prefix,
        int depth)
    {
        var guard = 0;
        while (body.Remaining >= 2 && guard++ < 16)
        {
            ParseDescriptor(context, body, fields, prefix, depth + 1);
        }
    }

    private static readonly int[] AudioSampleRates =
    [
        96000, 88200, 64000, 48000, 44100, 32000,
        24000, 22050, 16000, 12000, 11025, 8000, 7350,
    ];

    private static void ParseAudioSpecificConfig(ReadOnlySpan<byte> data, FieldList fields, string prefix)
    {
        var bits = new BitReader(data);
        if (!bits.TryRead(5, out var audioObjectType))
        {
            return;
        }

        if (audioObjectType == 31)
        {
            if (!bits.TryRead(6, out var extendedType))
            {
                return;
            }

            audioObjectType = 32 + extendedType;
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.audio_object_type_extended"), extendedType);
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.audio_object_type"), audioObjectType);
        if (!bits.TryRead(4, out var frequencyIndex))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.sampling_frequency_index"), frequencyIndex);
        ReadAscFrequency(ref bits, fields, prefix, "asc.sampling_frequency", frequencyIndex);

        if (!bits.TryRead(4, out var channelConfiguration))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.channel_configuration"), channelConfiguration);

        var extensionType = audioObjectType;
        if (audioObjectType is 5 or 29)
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.sbr_present"), 1);
            if (audioObjectType == 29)
            {
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.ps_present"), 1);
            }

            if (!bits.TryRead(4, out var extensionFrequencyIndex))
            {
                return;
            }

            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.extension_sampling_frequency_index"),
                extensionFrequencyIndex);
            ReadAscFrequency(ref bits, fields, prefix, "asc.extension_sampling_frequency", extensionFrequencyIndex);
            if (!bits.TryRead(5, out extensionType))
            {
                return;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.extension_audio_object_type"), extensionType);
        }

        if (extensionType is 1 or 2 or 3 or 4 or 6 or 7 or 17 or 19 or 20 or 21 or 22 or 23)
        {
            if (!bits.TryRead(1, out var frameLengthFlag))
            {
                return;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.frame_length_flag"), frameLengthFlag);
            if (!bits.TryRead(1, out var dependsOnCoreCoder))
            {
                return;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.depends_on_core_coder"), dependsOnCoreCoder);
            if (dependsOnCoreCoder != 0)
            {
                if (!bits.TryRead(14, out var coreCoderDelay))
                {
                    return;
                }

                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.core_coder_delay"), coreCoderDelay);
            }

            if (!bits.TryRead(1, out var extensionFlag))
            {
                return;
            }

            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.extension_flag"), extensionFlag);
        }

        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}asc.codec_string"),
            string.Create(CultureInfo.InvariantCulture, $"mp4a.{audioObjectType:X2}"));
    }

    private static void ReadAscFrequency(
        ref BitReader bits,
        FieldList fields,
        string prefix,
        string key,
        uint frequencyIndex)
    {
        if (frequencyIndex == 0xF)
        {
            if (bits.TryRead(24, out var explicitFrequency))
            {
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}{key}"), explicitFrequency);
            }
        }
        else if (frequencyIndex < AudioSampleRates.Length)
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}{key}"), AudioSampleRates[frequencyIndex]);
        }
    }

    private static void ParseAv1C(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        if (!data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}marker_and_version"), out var markerVersion))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}marker"), (markerVersion >> 7) & 0x01);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}version"), markerVersion & 0x7F);

        if (!data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}seq_profile_and_level"), out var profileLevel))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}seq_profile"), (profileLevel >> 5) & 0x07);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}seq_level_idx_0"), profileLevel & 0x1F);

        if (!data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}tier_and_flags"), out var tierFlags))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}seq_tier_0"), (tierFlags >> 7) & 0x01);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}high_bitdepth"), (tierFlags >> 6) & 0x01);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}twelve_bit"), (tierFlags >> 5) & 0x01);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}monochrome"), (tierFlags >> 4) & 0x01);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}chroma_subsampling_x"), (tierFlags >> 3) & 0x01);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}chroma_subsampling_y"), (tierFlags >> 2) & 0x01);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}chroma_sample_position"), tierFlags & 0x03);

        if (data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}delay_and_reserved"), out var delay))
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}initial_presentation_delay_present"), (delay >> 4) & 0x01);
            if (((delay >> 4) & 0x01) != 0)
            {
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}initial_presentation_delay_minus_one"), delay & 0x0F);
            }
        }

        fields.Derived(string.Create(CultureInfo.InvariantCulture, $"{prefix}config_obus_size"), data.Remaining);
        data.SkipRemaining();
        fields.Note("AV1 configuration OBUs (sequence header) are reported by size only and are not decoded.");
    }

    private static void ParseDfLa(
        ParseContext context,
        IsoBmffBoxReader data,
        FieldList fields,
        string prefix)
    {
        if (!data.TryFullBox(out _, out _))
        {
            return;
        }

        var blockIndex = 0;
        while (data.Remaining >= 4)
        {
            context.ThrowIfCancellationRequested();
            if (!data.TryReadUInt8Raw(out var header))
            {
                break;
            }

            var last = (header >> 7) & 0x01;
            var blockType = header & 0x7F;
            if (!data.TryReadUInt8Raw(out var b1)
                || !data.TryReadUInt8Raw(out var b2)
                || !data.TryReadUInt8Raw(out var b3))
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"FLAC metadata block {blockIndex} header is truncated.");
                break;
            }

            var length = b1 << 16 | b2 << 8 | b3;
            var blockPrefix = string.Create(CultureInfo.InvariantCulture, $"{prefix}block[{blockIndex}]");
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{blockPrefix}.block_type"),
                string.Create(CultureInfo.InvariantCulture, $"{blockType} ({FlacBlockTypeName(blockType)})"));
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{blockPrefix}.length"), length);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{blockPrefix}.last_metadata_block"), last);

            if (length > data.Remaining)
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"FLAC metadata block {blockIndex} declares {length} byte(s) but the box ends first.");
                data.SkipRemaining();
                return;
            }

            var block = data.Slice(length, fields);
            ParseFlacMetadataBlock(context, blockType, block, fields, blockPrefix);

            blockIndex++;
            if (last == 1)
            {
                break;
            }
        }

        data.SkipRemaining();
    }

    private static void ParseFlacMetadataBlock(
        ParseContext context,
        int blockType,
        IsoBmffBoxReader block,
        FieldList fields,
        string prefix)
    {
        switch (blockType)
        {
            case 0:
                ParseFlacStreamInfo(block, fields, prefix);
                break;
            case 1:
                // PADDING has no content; its length is already reported on the block.
                break;
            case 2:
                ParseFlacApplication(block, fields, prefix);
                break;
            case 3:
                ParseFlacSeekTable(context, block, fields, prefix);
                break;
            case 4:
                ParseFlacVorbisComment(context, block, fields, prefix);
                break;
            case 5:
                ParseFlacCueSheet(context, block, fields, prefix);
                break;
            case 6:
                ParseFlacPicture(context, block, fields, prefix);
                break;
            default:
                fields.Derived(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}.payload"),
                    $"{block.Remaining} byte(s) of a reserved metadata block");
                block.SkipRemaining();
                break;
        }
    }

    private static string FlacBlockTypeName(int blockType) => blockType switch
    {
        0 => "STREAMINFO",
        1 => "PADDING",
        2 => "APPLICATION",
        3 => "SEEKTABLE",
        4 => "VORBIS_COMMENT",
        5 => "CUESHEET",
        6 => "PICTURE",
        _ => "reserved",
    };

    private static void ParseFlacStreamInfo(IsoBmffBoxReader block, FieldList fields, string prefix)
    {
        Span<byte> data = stackalloc byte[34];
        if (!block.TryReadBytesRaw(data))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "FLAC STREAMINFO is shorter than 34 bytes.");
            return;
        }

        var minBlock = BinaryPrimitives.ReadUInt16BigEndian(data);
        var maxBlock = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        var minFrame = data[4] << 16 | data[5] << 8 | data[6];
        var maxFrame = data[7] << 16 | data[8] << 8 | data[9];
        var packed = (ulong)data[10] << 56 | (ulong)data[11] << 48 | (ulong)data[12] << 40 | (ulong)data[13] << 32
            | (ulong)data[14] << 24 | (ulong)data[15] << 16 | (ulong)data[16] << 8 | data[17];

        var sampleRate = (packed >> 44) & 0xFFFFF;
        var channels = (int)((packed >> 41) & 0x07) + 1;
        var bitsPerSample = (int)((packed >> 36) & 0x1F) + 1;
        var totalSamples = packed & 0xFFFFFFFFF;

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.streaminfo.min_block_size"), minBlock);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.streaminfo.max_block_size"), maxBlock);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.streaminfo.min_frame_size"), minFrame);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.streaminfo.max_frame_size"), maxFrame);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.streaminfo.sample_rate"), sampleRate);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.streaminfo.channels"), channels);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.streaminfo.bits_per_sample"), bitsPerSample);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.streaminfo.total_samples"), totalSamples);
        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}.streaminfo.md5_signature"),
            IsoBmffFormatting.Hex(data[18..34]));
        block.SkipRemaining();
    }

    private static void ParseFlacApplication(IsoBmffBoxReader block, FieldList fields, string prefix)
    {
        Span<byte> id = stackalloc byte[4];
        if (!block.TryReadBytesRaw(id))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "FLAC APPLICATION block is shorter than 4 bytes.");
            return;
        }

        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}.application_id"),
            IsoBmffFormatting.Display(Encoding.Latin1.GetString(id)));
        fields.Derived(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}.application_data_length"),
            block.Remaining);
        block.SkipRemaining();
    }

    private static void ParseFlacSeekTable(
        ParseContext context,
        IsoBmffBoxReader block,
        FieldList fields,
        string prefix)
    {
        const int entrySize = 18;
        var declared = (ulong)(block.Remaining / entrySize);
        if (block.Remaining % entrySize != 0)
        {
            fields.Warning(
                DiagnosticCodes.InvalidLength,
                $"FLAC SEEKTABLE length {block.Remaining} is not a multiple of {entrySize} bytes.");
        }

        var count = IsoBmffFieldHelpers.RowLimit(context, declared, "FLAC seek table", fields);
        Span<byte> entry = stackalloc byte[entrySize];
        for (var index = 0; index < count; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!block.TryReadBytesRaw(entry))
            {
                break;
            }

            var entryPrefix = string.Create(CultureInfo.InvariantCulture, $"{prefix}.seekpoint[{index}]");
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{entryPrefix}.sample_number"),
                BinaryPrimitives.ReadUInt64BigEndian(entry));
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{entryPrefix}.stream_offset"),
                BinaryPrimitives.ReadUInt64BigEndian(entry[8..]));
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{entryPrefix}.frame_samples"),
                BinaryPrimitives.ReadUInt16BigEndian(entry[16..]));
        }

        block.SkipRemaining();
    }

    private static void ParseFlacVorbisComment(
        ParseContext context,
        IsoBmffBoxReader block,
        FieldList fields,
        string prefix)
    {
        // Vorbis comments are the only little-endian structure in a FLAC metadata block.
        if (!TryReadUInt32Le(block, out var vendorLength))
        {
            return;
        }

        if (vendorLength > block.Remaining || vendorLength > context.Limits.MaxStringBytes)
        {
            fields.Warning(
                DiagnosticCodes.InvalidLength,
                $"FLAC VORBIS_COMMENT vendor length {vendorLength} exceeds the block.");
            block.SkipRemaining();
            return;
        }

        var vendor = ReadUtf8(block, (int)vendorLength);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.vendor_string"), IsoBmffFormatting.Display(vendor));

        if (!TryReadUInt32Le(block, out var commentCount))
        {
            return;
        }

        var count = IsoBmffFieldHelpers.RowLimit(context, commentCount, "FLAC Vorbis comment", fields);
        for (var index = 0; index < count; index++)
        {
            context.ThrowIfCancellationRequested();
            if (!TryReadUInt32Le(block, out var length))
            {
                break;
            }

            if (length > block.Remaining || length > context.Limits.MaxStringBytes)
            {
                fields.Warning(
                    DiagnosticCodes.InvalidLength,
                    $"FLAC Vorbis comment {index} declares {length} byte(s) beyond the block.");
                break;
            }

            var text = ReadUtf8(block, (int)length);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}.comment[{index}]"),
                IsoBmffFormatting.Display(text));
        }

        block.SkipRemaining();
    }

    private static void ParseFlacCueSheet(
        ParseContext context,
        IsoBmffBoxReader block,
        FieldList fields,
        string prefix)
    {
        const int headerLength = 396;
        var header = new byte[headerLength];
        if (!block.TryReadBytesRaw(header))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "FLAC CUESHEET is shorter than 396 bytes.");
            return;
        }

        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}.media_catalog_number"),
            IsoBmffFormatting.Display(Encoding.ASCII.GetString(header, 0, 128).TrimEnd('\0', ' ')));
        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}.lead_in"),
            BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(128, 8)));

        var flags = header[136];
        var isCd = (flags & 0x80) != 0;
        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}.is_cd"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{(flags >> 7) & 0x01} ({(isCd ? "CD-DA" : "not CD-DA")})"));

        var trackCount = header[395];
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.track_count"), trackCount);

        var tracks = IsoBmffFieldHelpers.RowLimit(context, trackCount, "FLAC cue sheet track", fields);
        Span<byte> track = stackalloc byte[36];
        Span<byte> indexBytes = stackalloc byte[12];
        for (var trackIndex = 0; trackIndex < tracks; trackIndex++)
        {
            context.ThrowIfCancellationRequested();
            if (!block.TryReadBytesRaw(track))
            {
                fields.Warning(
                    DiagnosticCodes.TruncatedPayload,
                    $"FLAC CUESHEET track {trackIndex} is truncated.");
                break;
            }

            var trackPrefix = string.Create(CultureInfo.InvariantCulture, $"{prefix}.track[{trackIndex}]");
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{trackPrefix}.offset"),
                BinaryPrimitives.ReadUInt64BigEndian(track));
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{trackPrefix}.number"), track[8]);
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{trackPrefix}.ISRC"),
                IsoBmffFormatting.Display(Encoding.ASCII.GetString(track.Slice(9, 12)).TrimEnd('\0', ' ')));
            var trackFlags = track[21];
            var nonAudio = (trackFlags & 0x80) != 0;
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{trackPrefix}.track_type"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(trackFlags >> 7) & 0x01} ({(nonAudio ? "non-audio" : "audio")})"));
            fields.Raw(
                string.Create(CultureInfo.InvariantCulture, $"{trackPrefix}.pre_emphasis"),
                (trackFlags >> 6) & 0x01);

            var indexCount = track[35];
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{trackPrefix}.num_indices"), indexCount);

            var indices = IsoBmffFieldHelpers.RowLimit(context, indexCount, "FLAC cue sheet index", fields);
            for (var index = 0; index < indices; index++)
            {
                context.ThrowIfCancellationRequested();
                if (!block.TryReadBytesRaw(indexBytes))
                {
                    fields.Warning(
                        DiagnosticCodes.TruncatedPayload,
                        $"FLAC CUESHEET index {index} of track {trackIndex} is truncated.");
                    block.SkipRemaining();
                    return;
                }

                var indexPrefix = string.Create(CultureInfo.InvariantCulture, $"{trackPrefix}.index[{index}]");
                fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{indexPrefix}.offset"),
                    BinaryPrimitives.ReadUInt64BigEndian(indexBytes));
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{indexPrefix}.number"), indexBytes[8]);
            }
        }

        block.SkipRemaining();
    }

    private static void ParseFlacPicture(
        ParseContext context,
        IsoBmffBoxReader block,
        FieldList fields,
        string prefix)
    {
        if (!block.TryReadUInt32Raw(out var pictureType))
        {
            return;
        }

        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}.picture_type"),
            string.Create(CultureInfo.InvariantCulture, $"{pictureType} ({FlacPictureTypeName(pictureType)})"));

        if (!TryReadLengthPrefixedString(context, block, out var mime))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.mime_type"), IsoBmffFormatting.Display(mime));

        if (!TryReadLengthPrefixedString(context, block, out var description))
        {
            return;
        }

        fields.Raw(
            string.Create(CultureInfo.InvariantCulture, $"{prefix}.description"),
            IsoBmffFormatting.Display(description));

        if (!block.TryReadUInt32Raw(out var width)
            || !block.TryReadUInt32Raw(out var height)
            || !block.TryReadUInt32Raw(out var depth)
            || !block.TryReadUInt32Raw(out var colors)
            || !block.TryReadUInt32Raw(out var dataLength))
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, "FLAC PICTURE header is truncated.");
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.width"), width);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.height"), height);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.color_depth"), depth);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.colors"), colors);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}.data_length"), dataLength);

        if (dataLength > block.Remaining)
        {
            fields.Warning(
                DiagnosticCodes.InvalidLength,
                $"FLAC PICTURE data length {dataLength} exceeds the block.");
            block.SkipRemaining();
            return;
        }

        block.TrySkip(dataLength, "FLAC picture data");
        block.SkipRemaining();
    }

    private static bool TryReadLengthPrefixedString(
        ParseContext context,
        IsoBmffBoxReader block,
        out string value)
    {
        value = string.Empty;
        if (!block.TryReadUInt32Raw(out var length))
        {
            return false;
        }

        if (length > block.Remaining || length > context.Limits.MaxStringBytes)
        {
            block.SkipRemaining();
            return false;
        }

        value = ReadUtf8(block, (int)length);
        return true;
    }

    private static string ReadUtf8(IsoBmffBoxReader block, int length)
    {
        if (length == 0)
        {
            return string.Empty;
        }

        var bytes = new byte[length];
        if (!block.TryReadBytesRaw(bytes))
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static bool TryReadUInt32Le(IsoBmffBoxReader block, out uint value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (!block.TryReadBytesRaw(bytes))
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }

    private static string FlacPictureTypeName(uint pictureType) => pictureType switch
    {
        0 => "Other",
        1 => "32x32 pixels file icon",
        2 => "Other file icon",
        3 => "Cover (front)",
        4 => "Cover (back)",
        5 => "Leaflet page",
        6 => "Media",
        7 => "Lead artist/lead performer/soloist",
        8 => "Artist/performer",
        9 => "Conductor",
        10 => "Band/Orchestra",
        11 => "Composer",
        12 => "Lyricist/text writer",
        13 => "Recording location",
        14 => "During recording",
        15 => "During performance",
        16 => "Movie/video screen capture",
        17 => "A bright coloured fish",
        18 => "Illustration",
        19 => "Band/artist logotype",
        20 => "Publisher/Studio logotype",
        _ => "reserved",
    };

    private static void ParseDOps(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}version"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}output_channel_count"), out _);
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}pre_skip"), out _);
        data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}input_sample_rate"), out _);
        if (data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}output_gain"), out var gain))
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}output_gain"), unchecked((short)gain));
        }

        if (data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}channel_mapping_family"), out var family)
            && family != 0)
        {
            data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}stream_count"), out _);
            data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}coupled_count"), out _);
            for (var index = 0; index < data.Remaining; index++)
            {
                if (!data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}channel_mapping[{index}]"), out _))
                {
                    break;
                }
            }
        }

        data.SkipRemaining();
    }

    private static void ParseDac3(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        Span<byte> bytes = stackalloc byte[3];
        if (!data.TryReadBytesRaw(bytes))
        {
            return;
        }

        var bits = new BitReader(bytes);
        ReadBits(ref bits, fields, prefix, "fscod", 2);
        ReadBits(ref bits, fields, prefix, "bsid", 5);
        ReadBits(ref bits, fields, prefix, "bsmod", 3);
        ReadBits(ref bits, fields, prefix, "acmod", 3);
        ReadBits(ref bits, fields, prefix, "lfeon", 1);
        ReadBits(ref bits, fields, prefix, "bit_rate_code", 5);
        ReadBits(ref bits, fields, prefix, "reserved", 5);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}codec_string"), "ac-3");
        data.SkipRemaining();
    }

    private static void ParseDec3(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        Span<byte> bytes = stackalloc byte[2];
        if (!data.TryReadBytesRaw(bytes))
        {
            return;
        }

        var bits = new BitReader(bytes);
        if (!bits.TryRead(13, out var dataRate) || !bits.TryRead(3, out var independent))
        {
            return;
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}data_rate"), dataRate);
        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}num_ind_sub"), independent + 1);

        for (var index = 0; index <= independent; index++)
        {
            var subPrefix = string.Create(CultureInfo.InvariantCulture, $"{prefix}ind_sub[{index}].");
            ReadBits(ref bits, fields, subPrefix, "fscod", 2);
            ReadBits(ref bits, fields, subPrefix, "bsid", 5);
            ReadBits(ref bits, fields, subPrefix, "reserved", 1);
            ReadBits(ref bits, fields, subPrefix, "asvc", 1);
            ReadBits(ref bits, fields, subPrefix, "bsmod", 3);
            ReadBits(ref bits, fields, subPrefix, "acmod", 3);
            ReadBits(ref bits, fields, subPrefix, "lfeon", 1);
            ReadBits(ref bits, fields, subPrefix, "reserved2", 3);
            if (bits.TryRead(4, out var dependent))
            {
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{subPrefix}num_dep_sub"), dependent);
                if (dependent > 0)
                {
                    ReadBits(ref bits, fields, subPrefix, "chan_loc", 9);
                }
                else
                {
                    ReadBits(ref bits, fields, subPrefix, "reserved3", 1);
                }
            }
        }

        fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}codec_string"), "ec-3");
        data.SkipRemaining();
    }

    private static void ParseDolbyVision(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (!data.TryReadBytesRaw(bytes))
        {
            return;
        }

        var bits = new BitReader(bytes);
        ReadBits(ref bits, fields, prefix, "dv_version_major", 8);
        ReadBits(ref bits, fields, prefix, "dv_version_minor", 8);
        ReadBits(ref bits, fields, prefix, "dv_profile", 7);
        ReadBits(ref bits, fields, prefix, "dv_level", 6);
        ReadBits(ref bits, fields, prefix, "rpu_present_flag", 1);
        ReadBits(ref bits, fields, prefix, "el_present_flag", 1);
        ReadBits(ref bits, fields, prefix, "bl_present_flag", 1);
        ReadBits(ref bits, fields, prefix, "dv_bl_signal_compatibility_id", 4);
        ReadBits(ref bits, fields, prefix, "reserved", 28);
        data.SkipRemaining();
    }

    private static void ParseVpcC(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        if (!data.TryFullBox(out var version, out _))
        {
            return;
        }

        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}profile"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}level"), out _);
        if (data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}bit_depth_and_flags"), out var bitDepth))
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}bit_depth"), (bitDepth >> 4) & 0x0F);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}chroma_subsampling"), (bitDepth >> 1) & 0x07);
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}video_full_range_flag"), bitDepth & 0x01);
        }

        if (version == 0)
        {
            data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}color_space"), out _);
            data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}transfer_function"), out _);
        }
        else
        {
            data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}colour_primaries"), out _);
            data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}transfer_characteristics"), out _);
            data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}matrix_coefficients"), out _);
        }

        fields.Derived(string.Create(CultureInfo.InvariantCulture, $"{prefix}codec_initialization_data_size"), data.Remaining);
        data.SkipRemaining();
    }

    private static void ParseColr(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        if (!data.TryFourCc(string.Create(CultureInfo.InvariantCulture, $"{prefix}colour_type"), out var colourType))
        {
            return;
        }

        if (colourType is "nclx" or "nclc")
        {
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}colour_primaries"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}transfer_characteristics"), out _);
            data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}matrix_coefficients"), out _);
            if (colourType == "nclx" && data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}full_range_flag"), out var fullRange))
            {
                fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}full_range_flag"), (fullRange >> 7) & 0x01);
            }
        }
        else
        {
            fields.Derived(string.Create(CultureInfo.InvariantCulture, $"{prefix}<payload_length>"), data.Remaining);
        }

        data.SkipRemaining();
    }

    private static void ParseAlac(IsoBmffBoxReader data, FieldList fields, string prefix)
    {
        if (!data.TryFullBox(out _, out _))
        {
            return;
        }

        data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}frame_length"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}compatible_version"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}bit_depth"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}pb"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}mb"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}kb"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}num_channels"), out _);
        data.TryUInt16(string.Create(CultureInfo.InvariantCulture, $"{prefix}max_run"), out _);
        data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}max_frame_bytes"), out _);
        data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}avg_bit_rate"), out _);
        data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}sample_rate"), out _);
        data.SkipRemaining();
    }

    private static void ParseChnl(IsoBmffBoxReader data, FieldList fields, string prefix, EntryContext entry)
    {
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}version"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}flags"), out _);
        data.TryByte(string.Create(CultureInfo.InvariantCulture, $"{prefix}stream_structure"), out _);
        if (data.TryUInt32(string.Create(CultureInfo.InvariantCulture, $"{prefix}defined_layout"), out _))
        {
            fields.Derived(
                string.Create(CultureInfo.InvariantCulture, $"{prefix}<audio_channel_count>"),
                entry.ChannelCount);
        }

        var raw = data.ReadRemainingPrefix(32, out var length, out _);
        fields.Derived(string.Create(CultureInfo.InvariantCulture, $"{prefix}<payload_length>"), length);
        if (length > 0)
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}layout"), IsoBmffFormatting.Hex(raw));
        }
    }

    private static void ReadColor(IsoBmffBoxReader data, FieldList fields, string key)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (data.TryReadBytesRaw(bytes))
        {
            fields.Raw(key, IsoBmffFormatting.Hex(bytes));
        }
    }

    private static void ReadColor16(IsoBmffBoxReader data, FieldList fields, string key)
    {
        Span<byte> bytes = stackalloc byte[6];
        if (data.TryReadBytesRaw(bytes))
        {
            fields.Raw(key, IsoBmffFormatting.Hex(bytes));
        }
    }

    private static void ReadRectangle(IsoBmffBoxReader data, FieldList fields, string key)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (!data.TryReadBytesRaw(bytes))
        {
            return;
        }

        var top = BinaryPrimitives.ReadInt16BigEndian(bytes);
        var left = BinaryPrimitives.ReadInt16BigEndian(bytes[2..]);
        var bottom = BinaryPrimitives.ReadInt16BigEndian(bytes[4..]);
        var right = BinaryPrimitives.ReadInt16BigEndian(bytes[6..]);
        fields.Raw(
            key,
            string.Create(CultureInfo.InvariantCulture, $"top={top},left={left},bottom={bottom},right={right}"));
    }

    private static void ReadBits(
        ref BitReader bits,
        FieldList fields,
        string prefix,
        string key,
        int count)
    {
        if (bits.TryRead(count, out var value))
        {
            fields.Raw(string.Create(CultureInfo.InvariantCulture, $"{prefix}{key}"), value);
        }
    }

    private static EntryKind KindOf(string type)
    {
        if (VideoTypes.Contains(type))
        {
            return EntryKind.Video;
        }

        if (AudioTypes.Contains(type))
        {
            return EntryKind.Audio;
        }

        if (TextTypes.Contains(type))
        {
            return EntryKind.Text;
        }

        if (MetadataTypes.Contains(type))
        {
            return EntryKind.Metadata;
        }

        return type is "rtp " or "sm2t" ? EntryKind.Hint : EntryKind.Unknown;
    }

    /// <summary>Big-endian bit cursor used by the compact audio and Dolby Vision configuration boxes.</summary>
    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bit;

        public BitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bit = 0;
        }

        public BitReader(ReadOnlySpan<byte> data, int bitOffset)
        {
            _data = data;
            _bit = bitOffset;
        }

        public bool TryRead(int count, out uint value)
        {
            value = 0;
            if (count < 0 || count > 32 || _bit + count > _data.Length * 8)
            {
                return false;
            }

            for (var index = 0; index < count; index++)
            {
                var position = _bit + index;
                var bit = (_data[position >> 3] >> (7 - (position & 7))) & 1;
                value = value << 1 | (uint)bit;
            }

            _bit += count;
            return true;
        }

        public bool TryReadUE(out uint value)
        {
            value = 0;
            var leadingZeros = 0;
            while (leadingZeros < 32)
            {
                if (!TryRead(1, out var bit))
                {
                    return false;
                }

                if (bit != 0)
                {
                    break;
                }

                leadingZeros++;
            }

            if (leadingZeros >= 32)
            {
                return false;
            }

            if (!TryRead(leadingZeros, out var suffix))
            {
                return false;
            }

            value = (uint)((1UL << leadingZeros) - 1 + suffix);
            return true;
        }

        public bool TryReadSE(out int value)
        {
            value = 0;
            if (!TryReadUE(out var code))
            {
                return false;
            }

            var magnitude = (long)((code + 1) / 2);
            value = (int)((code & 1) != 0 ? magnitude : -magnitude);
            return true;
        }
    }
}
