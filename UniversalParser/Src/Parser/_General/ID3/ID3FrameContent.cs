using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.ID3
{
    internal static partial class ID3FrameContent
    {
        private static readonly string[] VolumeFieldNames =
        {
            "relative_volume_change_right",
            "relative_volume_change_left",
            "peak_volume_right",
            "peak_volume_left",
            "relative_volume_change_right_back",
            "relative_volume_change_left_back",
            "peak_volume_right_back",
            "peak_volume_left_back",
            "relative_volume_change_center",
            "peak_volume_center",
            "relative_volume_change_bass",
            "peak_volume_bass"
        };

        public static void Parse(
            string frameId,
            byte version,
            ReadOnlySpan<byte> content,
            ID3Sink sink)
        {
            if (content.IsEmpty)
            {
                sink.Payload("frame_data", content);
                return;
            }

            var c = new ID3Cursor(content);

            switch (frameId)
            {
                case "TT2":
                case "TIT2":
                    ParseTextFrame(c, sink, "encoding", "title");
                    return;

                case "TP1":
                case "TPE1":
                    ParseTextFrame(c, sink, "encoding", "lead_performer");
                    return;

                case "TP2":
                case "TPE2":
                    ParseTextFrame(c, sink, "encoding", "band_or_orchestra");
                    return;

                case "TCM":
                case "TCOM":
                    ParseTextFrame(c, sink, "encoding", "composer");
                    return;

                case "TAL":
                case "TALB":
                    ParseTextFrame(c, sink, "encoding", "album");
                    return;

                case "TRK":
                case "TRCK":
                    ParseTextFrame(c, sink, "encoding", "track_number");
                    return;

                case "TPA":
                case "TPOS":
                    ParseTextFrame(c, sink, "encoding", "part_of_set");
                    return;

                case "TYE":
                case "TDRC":
                    ParseTextFrame(c, sink, "encoding", "recording_time");
                    return;

                case "TCO":
                case "TCON":
                    ParseTextFrame(c, sink, "encoding", "content_type");
                    return;

                case "TBP":
                case "TBPM":
                    ParseTextFrame(c, sink, "encoding", "bpm");
                    return;

                case "TCR":
                case "TCOP":
                    ParseTextFrame(c, sink, "encoding", "copyright_message");
                    return;

                case "TPB":
                case "TPUB":
                    ParseTextFrame(c, sink, "encoding", "publisher");
                    return;

                case "TXX":
                case "TXXX":
                    ParseUserTextFrame(c, sink);
                    return;

                case "WAF":
                case "WOAF":
                    ParseUrlFrame(c, sink, "file_url");
                    return;

                case "WAR":
                case "WOAR":
                    ParseUrlFrame(c, sink, "artist_url");
                    return;

                case "WAS":
                case "WOAS":
                    ParseUrlFrame(c, sink, "source_url");
                    return;

                case "WCM":
                case "WCOM":
                    ParseUrlFrame(c, sink, "commercial_information_url");
                    return;

                case "WXX":
                case "WXXX":
                    ParseUserUrlFrame(c, sink);
                    return;

                case "COM":
                case "COMM":
                    ParseCommentFrame(c, sink);
                    return;

                case "ULT":
                case "USLT":
                    ParseLyricsFrame(c, sink);
                    return;

                case "PIC":
                case "APIC":
                    ParsePictureFrame(c, sink);
                    return;

                case "GEO":
                case "GEOB":
                    ParseGeobFrame(c, sink);
                    return;

                case "BUF":
                case "RBUF":
                    ParseRecommendedBufferSize(c, sink);
                    return;

                case "CNT":
                case "PCNT":
                    ParseCounter(c, sink);
                    return;

                case "POP":
                case "POPM":
                    ParsePopularimeter(c, sink);
                    return;

                case "ETC":
                case "ETCO":
                    ParseEventTimingCodes(c, sink);
                    return;

                case "MLL":
                case "MLLT":
                    ParseMpegLocationLookupTable(c, sink);
                    return;

                case "CRM":
                case "ENCR":
                    ParseEncryptionMethodRegistration(c, sink);
                    return;

                case "AENC":
                    ParseAudioEncryption(c, sink);
                    return;

                case "GRID":
                    ParseGroupIdentification(c, sink);
                    return;

                case "PRIV":
                    ParsePrivateFrame(c, sink);
                    return;

                case "SIGN":
                    ParseSignatureFrame(c, sink);
                    return;

                case "COMR":
                    ParseCommercialFrame(c, sink);
                    return;

                case "LINK":
                    ParseLinkedInformation(c, sink);
                    return;

                case "CHAP":
                    ParseChapterFrame(c, version, sink);
                    return;

                case "CTOC":
                    ParseTableOfContentsFrame(c, version, sink);
                    return;

                case "RVA":
                case "RVAD":
                case "RVA2":
                    ParseRelativeVolumeFrame(c, sink, frameId);
                    return;

                case "EQU":
                case "EQUA":
                case "EQU2":
                    ParseEqualisationFrame(c, sink, frameId);
                    return;

                case "USER":
                case "OWNE":
                    sink.Payload("frame_data", content);
                    return;

                default:
                    ParseUnknownFrame(content, sink);
                    return;
            }
        }

        private static void ParseTextFrame(
            ID3Cursor c,
            ID3Sink sink,
            string encodingKey,
            string valueKey)
        {
            byte encoding = c.ReadByte();

            sink.Verbatim("text_encoding", ID3String.EncodingName(encoding));

            ReadOnlySpan<byte> bytes = c.ReadRest();
            string[] values = ID3String.DecodeTextValues(bytes, encoding);

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

            for (int i = 0; i < values.Length; i++)
                sink.Text("array[" + i + "]." + valueKey, values[i]);
        }

        private static void ParseUserTextFrame(ID3Cursor c, ID3Sink sink)
        {
            byte encoding = c.ReadByte();
            sink.Verbatim("text_encoding", ID3String.EncodingName(encoding));

            string description = c.ReadTerminatedText(encoding);
            string[] values = ID3String.DecodeTextValues(c.ReadRest(), encoding);

            sink.Text("description", description);

            for (int i = 0; i < values.Length; i++)
                sink.Text("array[" + i + "].value", values[i]);
        }

        private static void ParseUrlFrame(
            ID3Cursor c,
            ID3Sink sink,
            string valueKey)
        {
            sink.Text(valueKey, c.ReadLatin1String());
        }

        private static void ParseUserUrlFrame(ID3Cursor c, ID3Sink sink)
        {
            byte encoding = c.ReadByte();
            sink.Verbatim("text_encoding", ID3String.EncodingName(encoding));

            sink.Text("description", c.ReadTerminatedText(encoding));
            sink.Text("url", ID3String.Decode(c.ReadRest(), ID3String.EncodingIso88591));
        }

        private static void ParseCommentFrame(ID3Cursor c, ID3Sink sink)
        {
            byte encoding = c.ReadByte();

            sink.Verbatim("text_encoding", ID3String.EncodingName(encoding));
            sink.Text("language", ID3String.Decode(c.ReadBytes(3), ID3String.EncodingIso88591));
            sink.Text("short_content_description", c.ReadTerminatedText(encoding));
            sink.Text("text", ID3String.Decode(c.ReadRest(), encoding));
        }

        private static void ParseLyricsFrame(ID3Cursor c, ID3Sink sink)
        {
            byte encoding = c.ReadByte();

            sink.Verbatim("text_encoding", ID3String.EncodingName(encoding));
            sink.Text("language", ID3String.Decode(c.ReadBytes(3), ID3String.EncodingIso88591));
            sink.Text("content_descriptor", c.ReadTerminatedText(encoding));
            sink.Text("lyrics", ID3String.Decode(c.ReadRest(), encoding));
        }

        private static void ParsePictureFrame(ID3Cursor c, ID3Sink sink)
        {
            byte encoding = c.ReadByte();

            sink.Verbatim("text_encoding", ID3String.EncodingName(encoding));

            if (c.Remaining < 4)
            {
                sink.Payload("picture_data", c.ReadRest());
                return;
            }

            string mimeType;

            // ID3v2.2 使用 3 字节图片格式，如 JPG、PNG。
            if (c.Remaining >= 4 && c.PeekByte(0) != 0)
            {
                int zero = c.IndexOfZero();
                if (zero >= 0 && zero <= 64)
                    mimeType = c.ReadLatin1String();
                else
                    mimeType = ID3String.Decode(c.ReadBytes(3), ID3String.EncodingIso88591);
            }
            else
            {
                mimeType = c.ReadLatin1String();
            }

            sink.Text("mime_type", mimeType);

            if (!c.End)
                sink.Verbatim("picture_type", ID3Tables.PictureType(c.ReadByte()));

            sink.Text("description", c.ReadTerminatedText(encoding));

            ReadOnlySpan<byte> image = c.ReadRest();
            sink.Payload("picture_data", image, ID3Format.PayloadTypeFromMime(mimeType));
        }

        private static void ParseGeobFrame(ID3Cursor c, ID3Sink sink)
        {
            byte encoding = c.ReadByte();

            sink.Verbatim("text_encoding", ID3String.EncodingName(encoding));
            sink.Text("mime_type", c.ReadLatin1String());
            sink.Text("filename", c.ReadTerminatedText(encoding));
            sink.Text("description", c.ReadTerminatedText(encoding));
            sink.Payload("object_data", c.ReadRest());
        }

        private static void ParseRecommendedBufferSize(ID3Cursor c, ID3Sink sink)
        {
            if (c.Remaining < 3)
            {
                sink.Payload("frame_data", c.ReadRest());
                return;
            }

            sink.Number("buffer_size", c.ReadUInt(3));
            sink.Number("embedded_info_flag", c.ReadByte());
            sink.Number("offset_to_next_tag", c.ReadUInt(4));
        }

        private static void ParseCounter(ID3Cursor c, ID3Sink sink)
        {
            sink.Number("counter", c.ReadUIntVariable());
        }

        private static void ParsePopularimeter(ID3Cursor c, ID3Sink sink)
        {
            sink.Text("email", c.ReadLatin1String());

            if (!c.End)
                sink.Number("rating", c.ReadByte());

            if (!c.End)
                sink.Number("counter", c.ReadUIntVariable());
        }

        private static void ParseEventTimingCodes(ID3Cursor c, ID3Sink sink)
        {
            if (c.End)
                return;

            sink.Verbatim("timestamp_format", ID3Tables.TimestampFormat(c.ReadByte()));

            int index = 0;
            while (c.Remaining >= 5)
            {
                sink.Verbatim(
                    "array[" + index + "].event_type",
                    ID3Tables.EventType(c.ReadByte()));

                sink.Number(
                    "array[" + index + "].timestamp",
                    c.ReadUInt(4));

                index++;
            }

            if (!c.End)
                sink.Payload("unparsed_data", c.ReadRest());
        }

        private static void ParseMpegLocationLookupTable(
            ID3Cursor c,
            ID3Sink sink)
        {
            if (c.Remaining < 10)
            {
                sink.Payload("frame_data", c.ReadRest());
                return;
            }

            sink.Text("reference_point", c.ReadLatin1String(4));
            sink.Number("bits_for_byte раз", c.ReadByte());
            sink.Number("bits_for_millisecond", c.ReadByte());
            sink.Number("milliseconds_between_reference", c.ReadUInt(4));
            sink.Number("bytes_between_reference", c.ReadUInt(3));
            sink.Number("milliseconds_between_reference_2", c.ReadUInt(3));

            if (!c.End)
                sink.Payload("deviation_data", c.ReadRest());
        }

        private static void ParseEncryptionMethodRegistration(
            ID3Cursor c,
            ID3Sink sink)
        {
            sink.Text("owner_identifier", c.ReadLatin1String());

            if (!c.End)
                sink.Verbatim("encryption_method_symbol", ID3Format.Hex(c.ReadByte()));

            if (!c.End)
                sink.Payload("encryption_data", c.ReadRest());
        }

        private static void ParseAudioEncryption(ID3Cursor c, ID3Sink sink)
        {
            sink.Text("owner_identifier", c.ReadLatin1String());

            if (c.Remaining >= 2)
                sink.Number("preview_start", c.ReadUInt(2));

            if (c.Remaining >= 2)
                sink.Number("preview_length", c.ReadUInt(2));

            if (!c.End)
                sink.Payload("encryption_info", c.ReadRest());
        }

        private static void ParseGroupIdentification(ID3Cursor c, ID3Sink sink)
        {
            sink.Text("owner_identifier", c.ReadLatin1String());

            if (!c.End)
                sink.Verbatim("group_symbol", ID3Format.Hex(c.ReadByte()));

            if (!c.End)
                sink.Payload("group_data", c.ReadRest());
        }

        private static void ParsePrivateFrame(ID3Cursor c, ID3Sink sink)
        {
            sink.Text("owner_identifier", c.ReadLatin1String());
            sink.Payload("private_data", c.ReadRest());
        }

        private static void ParseSignatureFrame(ID3Cursor c, ID3Sink sink)
        {
            if (!c.End)
                sink.Verbatim("group_symbol", ID3Format.Hex(c.ReadByte()));

            sink.Payload("signature_data", c.ReadRest());
        }

        private static void ParseCommercialFrame(ID3Cursor c, ID3Sink sink)
        {
            byte encoding = c.ReadByte();

            sink.Verbatim("text_encoding", ID3String.EncodingName(encoding));

            if (c.Remaining < 3)
            {
                sink.Payload("frame_data", c.ReadRest());
                return;
            }

            sink.Text("language", ID3String.Decode(c.ReadBytes(3), ID3String.EncodingIso88591));
            sink.Text("price_string", c.ReadTerminatedText(encoding));
            sink.Text("valid_until", c.ReadBytesAsText(8, ID3String.EncodingIso88591));
            sink.Text("contact_url", c.ReadTerminatedText(encoding));
            sink.Text("seller_name", c.ReadTerminatedText(encoding));
            sink.Text("description", c.ReadTerminatedText(encoding));

            if (!c.End)
                sink.Verbatim("received_as", ID3Tables.ReceivedAs(c.ReadByte()));

            if (!c.End)
                sink.Payload("logo", c.ReadRest(), "image");
        }

        private static void ParseLinkedInformation(ID3Cursor c, ID3Sink sink)
        {
            sink.Text("frame_identifier", c.ReadLatin1String(4));
            sink.Text("url", c.ReadLatin1String());

            if (!c.End)
                sink.Payload("additional_data", c.ReadRest());
        }

        private static void ParseChapterFrame(
            ID3Cursor c,
            byte version,
            ID3Sink sink)
        {
            sink.Text("element_id", c.ReadLatin1String());

            if (c.Remaining < 16)
            {
                sink.Payload("chapter_data", c.ReadRest());
                return;
            }

            sink.Number("start_time", c.ReadUInt(4));
            sink.Number("end_time", c.ReadUInt(4));
            sink.Number("start_offset", c.ReadUInt(4));
            sink.Number("end_offset", c.ReadUInt(4));

            ReadOnlySpan<byte> subFrames = c.ReadRest();

            if (!subFrames.IsEmpty)
            {
                int count = ID3FrameReader.ReadFrames(
                    subFrames,
                    version,
                    sink.Scope("sub_frame").Path,
                    sink.Lines);

                if (count < subFrames.Length)
                    sink.Payload("unparsed_sub_frame_data", subFrames[count..]);
            }
        }

        private static void ParseTableOfContentsFrame(
            ID3Cursor c,
            byte version,
            ID3Sink sink)
        {
            sink.Text("element_id", c.ReadLatin1String());

            if (c.End)
                return;

            byte flags = c.ReadByte();
            sink.Verbatim("flags", ID3Flags.TableOfContentsFlags(flags));

            if (c.End)
                return;

            int childCount = c.ReadByte();
            sink.Number("child_count", childCount);

            for (int i = 0; i < childCount && !c.End; i++)
                sink.Text("array[" + i + "].child_element_id", c.ReadLatin1String());

            ReadOnlySpan<byte> subFrames = c.ReadRest();

            if (!subFrames.IsEmpty)
                ID3FrameReader.ReadFrames(
                    subFrames,
                    version,
                    sink.Scope("sub_frame").Path,
                    sink.Lines);
        }

        private static void ParseRelativeVolumeFrame(
            ID3Cursor c,
            ID3Sink sink,
            string frameId)
        {
            if (frameId == "RVA2")
            {
                sink.Text("identification", c.ReadLatin1String());
            }
            else
            {
                if (!c.End)
                    sink.Verbatim("increment_decrement", ID3Format.Hex(c.ReadByte()));

                if (!c.End)
                    sink.Number("bits_used_for_volume_description", c.ReadByte());
            }

            int index = 0;

            while (!c.End)
            {
                int channel = c.ReadByte();
                sink.Verbatim(
                    "array[" + index + "].channel",
                    ID3Tables.ChannelType(channel));

                if (c.Remaining >= 2)
                    sink.Number("array[" + index + "].volume_adjustment", c.ReadInt16());

                if (c.Remaining >= 2)
                    sink.Number("array[" + index + "].peak_volume", c.ReadUInt(2));

                index++;
            }
        }

        private static void ParseEqualisationFrame(
            ID3Cursor c,
            ID3Sink sink,
            string frameId)
        {
            if (frameId == "EQU2")
            {
                if (!c.End)
                    sink.Number("interpolation_method", c.ReadByte());

                sink.Text("identification", c.ReadLatin1String());
            }
            else if (!c.End)
            {
                sink.Verbatim("increment_decrement", ID3Format.Hex(c.ReadByte()));
            }

            int index = 0;

            while (c.Remaining >= 4)
            {
                sink.Number("array[" + index + "].frequency", c.ReadUInt(2));
                sink.Number("array[" + index + "].adjustment", c.ReadInt16());
                index++;
            }

            if (!c.End)
                sink.Payload("unparsed_data", c.ReadRest());
        }

        private static void ParseUnknownFrame(
            ReadOnlySpan<byte> content,
            ID3Sink sink)
        {
            sink.Payload("frame_data", content);
        }
    }
}