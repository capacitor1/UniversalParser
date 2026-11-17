namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Decodes the payload of one ID3v2 frame. Known text, URL, comment, lyrics, picture, object,
/// counter, timing, chapter, and table-of-contents frames are expanded; anything else is kept as
/// a bounded payload label. Compressed and encrypted frames never reach this class.
/// </summary>
internal static class Id3FrameContent
{
    public static void Parse(string frameId, byte version, ReadOnlySpan<byte> content, Id3Sink sink)
    {
        if (content.IsEmpty)
        {
            sink.Payload("frame_data", content);
            return;
        }

        var cursor = new Id3Cursor(content);
        switch (frameId)
        {
            case "TT2":
            case "TIT2":
                ParseTextFrame(cursor, sink, "title");
                return;
            case "TP1":
            case "TPE1":
                ParseTextFrame(cursor, sink, "lead_performer");
                return;
            case "TP2":
            case "TPE2":
                ParseTextFrame(cursor, sink, "band_or_orchestra");
                return;
            case "TCM":
            case "TCOM":
                ParseTextFrame(cursor, sink, "composer");
                return;
            case "TAL":
            case "TALB":
                ParseTextFrame(cursor, sink, "album");
                return;
            case "TRK":
            case "TRCK":
                ParseTextFrame(cursor, sink, "track_number");
                return;
            case "TPA":
            case "TPOS":
                ParseTextFrame(cursor, sink, "part_of_set");
                return;
            case "TYE":
            case "TDRC":
                ParseTextFrame(cursor, sink, "recording_time");
                return;
            case "TCO":
            case "TCON":
                ParseTextFrame(cursor, sink, "content_type");
                return;
            case "TBP":
            case "TBPM":
                ParseTextFrame(cursor, sink, "bpm");
                return;
            case "TCR":
            case "TCOP":
                ParseTextFrame(cursor, sink, "copyright_message");
                return;
            case "TPB":
            case "TPUB":
                ParseTextFrame(cursor, sink, "publisher");
                return;
            case "TXX":
            case "TXXX":
                ParseUserTextFrame(cursor, sink);
                return;
            case "WAF":
            case "WOAF":
                ParseUrlFrame(cursor, sink, "file_url");
                return;
            case "WAR":
            case "WOAR":
                ParseUrlFrame(cursor, sink, "artist_url");
                return;
            case "WAS":
            case "WOAS":
                ParseUrlFrame(cursor, sink, "source_url");
                return;
            case "WCM":
            case "WCOM":
                ParseUrlFrame(cursor, sink, "commercial_information_url");
                return;
            case "WXX":
            case "WXXX":
                ParseUserUrlFrame(cursor, sink);
                return;
            case "COM":
            case "COMM":
                ParseCommentFrame(cursor, sink);
                return;
            case "ULT":
            case "USLT":
                ParseLyricsFrame(cursor, sink);
                return;
            case "PIC":
            case "APIC":
                ParsePictureFrame(cursor, sink);
                return;
            case "GEO":
            case "GEOB":
                ParseGeobFrame(cursor, sink);
                return;
            case "BUF":
            case "RBUF":
                ParseRecommendedBufferSize(cursor, sink);
                return;
            case "CNT":
            case "PCNT":
                ParseCounter(cursor, sink);
                return;
            case "POP":
            case "POPM":
                ParsePopularimeter(cursor, sink);
                return;
            case "ETC":
            case "ETCO":
                ParseEventTimingCodes(cursor, sink);
                return;
            case "MLL":
            case "MLLT":
                ParseMpegLocationLookupTable(cursor, sink);
                return;
            case "CRM":
            case "ENCR":
                ParseEncryptionMethodRegistration(cursor, sink);
                return;
            case "AENC":
                ParseAudioEncryption(cursor, sink);
                return;
            case "GRID":
                ParseGroupIdentification(cursor, sink);
                return;
            case "PRIV":
                ParsePrivateFrame(cursor, sink);
                return;
            case "SIGN":
                ParseSignatureFrame(cursor, sink);
                return;
            case "COMR":
                ParseCommercialFrame(cursor, sink);
                return;
            case "LINK":
                ParseLinkedInformation(cursor, sink);
                return;
            case "CHAP":
                ParseChapterFrame(cursor, version, sink);
                return;
            case "CTOC":
                ParseTableOfContentsFrame(cursor, version, sink);
                return;
            case "RVA":
            case "RVAD":
            case "RVA2":
                ParseRelativeVolumeFrame(cursor, sink, frameId);
                return;
            case "EQU":
            case "EQUA":
            case "EQU2":
                ParseEqualisationFrame(cursor, sink, frameId);
                return;
            case "USER":
            case "OWNE":
                sink.Payload("frame_data", content);
                return;
            default:
                sink.Payload("frame_data", content);
                return;
        }
    }

    private static void ParseTextFrame(Id3Cursor cursor, Id3Sink sink, string valueKey)
    {
        var encoding = cursor.ReadByte();
        sink.Verbatim("text_encoding", Id3String.EncodingName(encoding));

        var values = Id3String.DecodeTextValues(cursor.ReadRest(), encoding);
        if (values.Length == 0)
        {
            sink.Text(valueKey, string.Empty);
            return;
        }

        if (values.Length == 1)
        {
            sink.Text(valueKey, values[0]);
            return;
        }

        for (var index = 0; index < values.Length; index++)
        {
            sink.Text("array[" + index + "]." + valueKey, values[index]);
        }
    }

    private static void ParseUserTextFrame(Id3Cursor cursor, Id3Sink sink)
    {
        var encoding = cursor.ReadByte();
        sink.Verbatim("text_encoding", Id3String.EncodingName(encoding));

        var description = cursor.ReadTerminatedText(encoding);
        var values = Id3String.DecodeTextValues(cursor.ReadRest(), encoding);
        sink.Text("description", description);
        for (var index = 0; index < values.Length; index++)
        {
            sink.Text("array[" + index + "].value", values[index]);
        }
    }

    private static void ParseUrlFrame(Id3Cursor cursor, Id3Sink sink, string valueKey) =>
        sink.Text(valueKey, cursor.ReadLatin1String());

    private static void ParseUserUrlFrame(Id3Cursor cursor, Id3Sink sink)
    {
        var encoding = cursor.ReadByte();
        sink.Verbatim("text_encoding", Id3String.EncodingName(encoding));
        sink.Text("description", cursor.ReadTerminatedText(encoding));
        sink.Text("url", Id3String.Decode(cursor.ReadRest(), Id3String.EncodingIso88591));
    }

    private static void ParseCommentFrame(Id3Cursor cursor, Id3Sink sink)
    {
        var encoding = cursor.ReadByte();
        sink.Verbatim("text_encoding", Id3String.EncodingName(encoding));
        sink.Text("language", Id3String.Decode(cursor.ReadBytes(3), Id3String.EncodingIso88591));
        sink.Text("short_content_description", cursor.ReadTerminatedText(encoding));
        sink.Text("text", Id3String.Decode(cursor.ReadRest(), encoding));
    }

    private static void ParseLyricsFrame(Id3Cursor cursor, Id3Sink sink)
    {
        var encoding = cursor.ReadByte();
        sink.Verbatim("text_encoding", Id3String.EncodingName(encoding));
        sink.Text("language", Id3String.Decode(cursor.ReadBytes(3), Id3String.EncodingIso88591));
        sink.Text("content_descriptor", cursor.ReadTerminatedText(encoding));
        sink.Text("lyrics", Id3String.Decode(cursor.ReadRest(), encoding));
    }

    private static void ParsePictureFrame(Id3Cursor cursor, Id3Sink sink)
    {
        var encoding = cursor.ReadByte();
        sink.Verbatim("text_encoding", Id3String.EncodingName(encoding));

        if (cursor.Remaining < 4)
        {
            sink.Payload("picture_data", cursor.ReadRest());
            return;
        }

        string mimeType;
        if (cursor.Remaining >= 4 && cursor.PeekByte(0) != 0)
        {
            var zero = cursor.IndexOfZero();
            mimeType = zero >= 0 && zero <= 64
                ? cursor.ReadLatin1String()
                : Id3String.Decode(cursor.ReadBytes(3), Id3String.EncodingIso88591);
        }
        else
        {
            mimeType = cursor.ReadLatin1String();
        }

        sink.Text("mime_type", mimeType);
        if (!cursor.End)
        {
            sink.Verbatim("picture_type", Id3Tables.PictureType(cursor.ReadByte()));
        }

        sink.Text("description", cursor.ReadTerminatedText(encoding));
        var image = cursor.ReadRest();
        sink.Payload("picture_data", image, Id3Format.PayloadTypeFromMime(mimeType));
    }

    private static void ParseGeobFrame(Id3Cursor cursor, Id3Sink sink)
    {
        var encoding = cursor.ReadByte();
        sink.Verbatim("text_encoding", Id3String.EncodingName(encoding));
        sink.Text("mime_type", cursor.ReadLatin1String());
        sink.Text("filename", cursor.ReadTerminatedText(encoding));
        sink.Text("description", cursor.ReadTerminatedText(encoding));
        sink.Payload("object_data", cursor.ReadRest());
    }

    private static void ParseRecommendedBufferSize(Id3Cursor cursor, Id3Sink sink)
    {
        if (cursor.Remaining < 3)
        {
            sink.Payload("frame_data", cursor.ReadRest());
            return;
        }

        sink.Number("buffer_size", cursor.ReadUInt(3));
        sink.Number("embedded_info_flag", cursor.ReadByte());
        sink.Number("offset_to_next_tag", cursor.ReadUInt(4));
    }

    private static void ParseCounter(Id3Cursor cursor, Id3Sink sink) =>
        sink.Number("counter", cursor.ReadUIntVariable());

    private static void ParsePopularimeter(Id3Cursor cursor, Id3Sink sink)
    {
        sink.Text("email", cursor.ReadLatin1String());
        if (!cursor.End)
        {
            sink.Number("rating", cursor.ReadByte());
        }

        if (!cursor.End)
        {
            sink.Number("counter", cursor.ReadUIntVariable());
        }
    }

    private static void ParseEventTimingCodes(Id3Cursor cursor, Id3Sink sink)
    {
        if (cursor.End)
        {
            return;
        }

        sink.Verbatim("timestamp_format", Id3Tables.TimestampFormat(cursor.ReadByte()));
        var index = 0;
        while (cursor.Remaining >= 5)
        {
            sink.Verbatim("array[" + index + "].event_type", Id3Tables.EventType(cursor.ReadByte()));
            sink.Number("array[" + index + "].timestamp", cursor.ReadUInt(4));
            index++;
        }

        if (!cursor.End)
        {
            sink.Payload("unparsed_data", cursor.ReadRest());
        }
    }

    private static void ParseMpegLocationLookupTable(Id3Cursor cursor, Id3Sink sink)
    {
        if (cursor.Remaining < 10)
        {
            sink.Payload("frame_data", cursor.ReadRest());
            return;
        }

        sink.Text("reference_point", cursor.ReadLatin1String(4));
        sink.Number("bits_for_byte", cursor.ReadByte());
        sink.Number("bits_for_millisecond", cursor.ReadByte());
        sink.Number("milliseconds_between_reference", cursor.ReadUInt(4));
        sink.Number("bytes_between_reference", cursor.ReadUInt(3));
        sink.Number("milliseconds_between_reference_2", cursor.ReadUInt(3));

        if (!cursor.End)
        {
            sink.Payload("deviation_data", cursor.ReadRest());
        }
    }

    private static void ParseEncryptionMethodRegistration(Id3Cursor cursor, Id3Sink sink)
    {
        sink.Text("owner_identifier", cursor.ReadLatin1String());
        if (!cursor.End)
        {
            sink.Verbatim("encryption_method_symbol", Id3Format.Hex(cursor.ReadByte()));
        }

        if (!cursor.End)
        {
            sink.Payload("encryption_data", cursor.ReadRest());
        }
    }

    private static void ParseAudioEncryption(Id3Cursor cursor, Id3Sink sink)
    {
        sink.Text("owner_identifier", cursor.ReadLatin1String());
        if (cursor.Remaining >= 2)
        {
            sink.Number("preview_start", cursor.ReadUInt(2));
        }

        if (cursor.Remaining >= 2)
        {
            sink.Number("preview_length", cursor.ReadUInt(2));
        }

        if (!cursor.End)
        {
            sink.Payload("encryption_info", cursor.ReadRest());
        }
    }

    private static void ParseGroupIdentification(Id3Cursor cursor, Id3Sink sink)
    {
        sink.Text("owner_identifier", cursor.ReadLatin1String());
        if (!cursor.End)
        {
            sink.Verbatim("group_symbol", Id3Format.Hex(cursor.ReadByte()));
        }

        if (!cursor.End)
        {
            sink.Payload("group_data", cursor.ReadRest());
        }
    }

    private static void ParsePrivateFrame(Id3Cursor cursor, Id3Sink sink)
    {
        sink.Text("owner_identifier", cursor.ReadLatin1String());
        sink.Payload("private_data", cursor.ReadRest());
    }

    private static void ParseSignatureFrame(Id3Cursor cursor, Id3Sink sink)
    {
        if (!cursor.End)
        {
            sink.Verbatim("group_symbol", Id3Format.Hex(cursor.ReadByte()));
        }

        sink.Payload("signature_data", cursor.ReadRest());
    }

    private static void ParseCommercialFrame(Id3Cursor cursor, Id3Sink sink)
    {
        var encoding = cursor.ReadByte();
        sink.Verbatim("text_encoding", Id3String.EncodingName(encoding));

        if (cursor.Remaining < 3)
        {
            sink.Payload("frame_data", cursor.ReadRest());
            return;
        }

        sink.Text("language", Id3String.Decode(cursor.ReadBytes(3), Id3String.EncodingIso88591));
        sink.Text("price_string", cursor.ReadTerminatedText(encoding));
        sink.Text("valid_until", cursor.ReadBytesAsText(8, Id3String.EncodingIso88591));
        sink.Text("contact_url", cursor.ReadTerminatedText(encoding));
        sink.Text("seller_name", cursor.ReadTerminatedText(encoding));
        sink.Text("description", cursor.ReadTerminatedText(encoding));

        if (!cursor.End)
        {
            sink.Verbatim("received_as", Id3Tables.ReceivedAs(cursor.ReadByte()));
        }

        if (!cursor.End)
        {
            sink.Payload("logo", cursor.ReadRest(), "image");
        }
    }

    private static void ParseLinkedInformation(Id3Cursor cursor, Id3Sink sink)
    {
        sink.Text("frame_identifier", cursor.ReadLatin1String(4));
        sink.Text("url", cursor.ReadLatin1String());
        if (!cursor.End)
        {
            sink.Payload("additional_data", cursor.ReadRest());
        }
    }

    private static void ParseChapterFrame(Id3Cursor cursor, byte version, Id3Sink sink)
    {
        sink.Text("element_id", cursor.ReadLatin1String());
        if (cursor.Remaining < 16)
        {
            sink.Payload("chapter_data", cursor.ReadRest());
            return;
        }

        sink.Number("start_time", cursor.ReadUInt(4));
        sink.Number("end_time", cursor.ReadUInt(4));
        sink.Number("start_offset", cursor.ReadUInt(4));
        sink.Number("end_offset", cursor.ReadUInt(4));

        var subFrames = cursor.ReadRest();
        if (!subFrames.IsEmpty)
        {
            var count = Id3FrameReader.ReadFrames(
                subFrames,
                version,
                sink.Scope("sub_frame").Path,
                sink.Lines);
            if (count < subFrames.Length)
            {
                sink.Payload("unparsed_sub_frame_data", subFrames[count..]);
            }
        }
    }

    private static void ParseTableOfContentsFrame(Id3Cursor cursor, byte version, Id3Sink sink)
    {
        sink.Text("element_id", cursor.ReadLatin1String());
        if (cursor.End)
        {
            return;
        }

        var flags = cursor.ReadByte();
        sink.Verbatim("flags", Id3Flags.TableOfContentsFlags(flags));
        if (cursor.End)
        {
            return;
        }

        var childCount = cursor.ReadByte();
        sink.Number("child_count", childCount);
        for (var index = 0; index < childCount && !cursor.End; index++)
        {
            sink.Text("array[" + index + "].child_element_id", cursor.ReadLatin1String());
        }

        var subFrames = cursor.ReadRest();
        if (!subFrames.IsEmpty)
        {
            Id3FrameReader.ReadFrames(subFrames, version, sink.Scope("sub_frame").Path, sink.Lines);
        }
    }

    private static void ParseRelativeVolumeFrame(Id3Cursor cursor, Id3Sink sink, string frameId)
    {
        if (frameId == "RVA2")
        {
            sink.Text("identification", cursor.ReadLatin1String());
        }
        else
        {
            if (!cursor.End)
            {
                sink.Verbatim("increment_decrement", Id3Format.Hex(cursor.ReadByte()));
            }

            if (!cursor.End)
            {
                sink.Number("bits_used_for_volume_description", cursor.ReadByte());
            }
        }

        var index = 0;
        while (!cursor.End)
        {
            var channel = cursor.ReadByte();
            sink.Verbatim("array[" + index + "].channel", Id3Tables.ChannelType(channel));
            if (cursor.Remaining >= 2)
            {
                sink.Number("array[" + index + "].volume_adjustment", cursor.ReadInt16());
            }

            if (cursor.Remaining >= 2)
            {
                sink.Number("array[" + index + "].peak_volume", cursor.ReadUInt(2));
            }

            index++;
        }
    }

    private static void ParseEqualisationFrame(Id3Cursor cursor, Id3Sink sink, string frameId)
    {
        if (frameId == "EQU2")
        {
            if (!cursor.End)
            {
                sink.Number("interpolation_method", cursor.ReadByte());
            }

            sink.Text("identification", cursor.ReadLatin1String());
        }
        else if (!cursor.End)
        {
            sink.Verbatim("increment_decrement", Id3Format.Hex(cursor.ReadByte()));
        }

        var index = 0;
        while (cursor.Remaining >= 4)
        {
            sink.Number("array[" + index + "].frequency", cursor.ReadUInt(2));
            sink.Number("array[" + index + "].adjustment", cursor.ReadInt16());
            index++;
        }

        if (!cursor.End)
        {
            sink.Payload("unparsed_data", cursor.ReadRest());
        }
    }
}
