namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Frame-header loop shared by the tag body and by CHAP/CTOC sub-frames. Returns the number of
/// bytes consumed, so the caller can report the padding and any unparsed tail.
/// </summary>
internal static class Id3FrameReader
{
    public static int ReadFrames(
        ReadOnlySpan<byte> body,
        byte version,
        string arrayName,
        List<(string Key, string Value)> lines)
    {
        var idLength = version <= 2 ? 3 : 4;
        var headerSize = version <= 2 ? 6 : 10;
        var offset = 0;
        var index = 0;

        while (offset + headerSize <= body.Length)
        {
            if (body[offset] == 0x00)
            {
                break;
            }

            var frameId = Id3String.Decode(body.Slice(offset, idLength), Id3String.EncodingIso88591);
            if (!IsValidFrameId(frameId))
            {
                new Id3Sink(lines).Verbatim(
                    "parse_note",
                    "Invalid frame identifier encountered, frame scan stopped at offset " + Id3Format.Number(offset) + ".");
                break;
            }

            var declaredSize = ReadFrameSize(body, offset, version, idLength, headerSize);
            var frameFlags = version <= 2
                ? (ushort)0
                : (ushort)((body[offset + 8] << 8) | body[offset + 9]);

            var payloadOffset = offset + headerSize;
            var payloadSize = Math.Min(Math.Max(declaredSize, 0), body.Length - payloadOffset);

            var sink = new Id3Sink(lines, arrayName + "[" + Id3Format.Number(index) + "]");
            sink.Text("frame_id", frameId);
            sink.Number("size", declaredSize);
            if (version >= 3)
            {
                sink.Verbatim("flags", Id3Flags.FrameFlags(frameFlags, version));
            }

            if (payloadSize < declaredSize)
            {
                sink.Verbatim("parse_note", "Frame size exceeds the remaining tag data, frame is truncated.");
            }

            var cursor = new Id3Cursor(body.Slice(payloadOffset, payloadSize));
            var opaque = false;

            if (version == 3)
            {
                if ((frameFlags & Id3Flags.FrameV3Compression) != 0)
                {
                    sink.Number("decompressed_size", cursor.ReadUInt(4));
                    opaque = true;
                }

                if ((frameFlags & Id3Flags.FrameV3Encryption) != 0)
                {
                    sink.Verbatim("encryption_method", Id3Format.Hex(cursor.ReadByte()));
                    opaque = true;
                }

                if ((frameFlags & Id3Flags.FrameV3GroupingIdentity) != 0)
                {
                    sink.Verbatim("group_identifier", Id3Format.Hex(cursor.ReadByte()));
                }
            }
            else if (version >= 4)
            {
                if ((frameFlags & Id3Flags.FrameV4GroupingIdentity) != 0)
                {
                    sink.Verbatim("group_identifier", Id3Format.Hex(cursor.ReadByte()));
                }

                if ((frameFlags & Id3Flags.FrameV4Encryption) != 0)
                {
                    sink.Verbatim("encryption_method", Id3Format.Hex(cursor.ReadByte()));
                    opaque = true;
                }

                if ((frameFlags & Id3Flags.FrameV4DataLengthIndicator) != 0)
                {
                    sink.Number("data_length_indicator", cursor.ReadSyncSafe32());
                }

                if ((frameFlags & Id3Flags.FrameV4Compression) != 0)
                {
                    opaque = true;
                }
            }

            var raw = cursor.ReadRest();
            var restored = version >= 4 && (frameFlags & Id3Flags.FrameV4Unsynchronisation) != 0
                ? Id3Unsynchronisation.Restore(raw)
                : null;
            var content = restored is null ? raw : restored;

            if (opaque)
            {
                sink.Payload("frame_data", content);
            }
            else
            {
                Id3FrameContent.Parse(frameId, version, content, sink);
            }

            offset = payloadOffset + payloadSize;
            index++;
        }

        return offset;
    }

    private static bool IsValidFrameId(string id)
    {
        if (id.Length == 0)
        {
            return false;
        }

        foreach (var character in id)
        {
            if (!((character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// v2.4 frame sizes should be synchsafe. Some encoders write a plain big-endian value instead;
    /// accept it only when the synchsafe reading does not land on a frame boundary.
    /// </summary>
    private static int ReadFrameSize(
        ReadOnlySpan<byte> body,
        int offset,
        byte version,
        int idLength,
        int headerSize)
    {
        var raw = body.Slice(offset + idLength, version <= 2 ? 3 : 4);
        if (version <= 3)
        {
            return Id3Number.ToLength(Id3Number.UIntBe(raw));
        }

        var plain = Id3Number.ToLength(Id3Number.UIntBe(raw));
        if (!Id3Number.IsSyncSafe(raw))
        {
            return plain;
        }

        var syncSafe = Id3Number.SyncSafe32(raw);
        if (plain != syncSafe
            && !IsFrameBoundary(body, offset + headerSize + syncSafe, version)
            && IsFrameBoundary(body, offset + headerSize + plain, version))
        {
            return plain;
        }

        return syncSafe;
    }

    private static bool IsFrameBoundary(ReadOnlySpan<byte> body, int position, byte version)
    {
        if (position == body.Length)
        {
            return true;
        }

        if (position < 0 || position > body.Length)
        {
            return false;
        }

        if (body[position] == 0x00)
        {
            return true;
        }

        var idLength = version <= 2 ? 3 : 4;
        if (position + idLength > body.Length)
        {
            return false;
        }

        return IsValidFrameId(Id3String.Decode(body.Slice(position, idLength), Id3String.EncodingIso88591));
    }
}
