using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Parses one complete ICC profile block. ICC.1:2022 defines a fixed 128-byte header followed by a
/// tag table; tag payloads are addressed by 32-bit offsets inside the profile. The parser never
/// crosses the supplied block boundary and never applies colour transforms: it only exposes the
/// header, tag table and the common self-describing tag types.
/// </summary>
internal static class IccProfileParser
{
    private const int HeaderLength = 128;
    private const int TagCountOffset = 128;
    private const int TagTableOffset = 132;
    private const int TagEntryLength = 12;
    private const int TagTypeHeaderLength = 8;
    private const int MaxClutChannels = 16;

    public static void Parse(
        byte[] profile,
        FieldList fields,
        ParseLimits limits,
        CancellationToken cancellationToken,
        long? profileOffset = null,
        SourceRange? fallbackRange = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(limits);

        new ProfileParser(profile, fields, limits, cancellationToken, profileOffset, fallbackRange).Parse();
    }

    private sealed class ProfileParser
    {
        private readonly byte[] _data;
        private readonly FieldList _fields;
        private readonly ParseLimits _limits;
        private readonly CancellationToken _cancellationToken;
        private readonly long? _profileOffset;
        private readonly SourceRange? _fallbackRange;

        public ProfileParser(
            byte[] data,
            FieldList fields,
            ParseLimits limits,
            CancellationToken cancellationToken,
            long? profileOffset,
            SourceRange? fallbackRange)
        {
            _data = data;
            _fields = fields;
            _limits = limits;
            _cancellationToken = cancellationToken;
            _profileOffset = profileOffset;
            _fallbackRange = fallbackRange;
        }

        public void Parse()
        {
            if (_data.Length < HeaderLength)
            {
                Warn(
                    DiagnosticCodes.TruncatedHeader,
                    $"The ICC profile needs a {HeaderLength}-byte header but only {_data.Length} byte(s) are available.",
                    0,
                    _data.Length);
                return;
            }

            var limit = ParseHeader();
            if (limit >= TagTableOffset)
            {
                ParseTagTable(limit);
            }
            else
            {
                _fields.Note("The profile is too short to contain a tag table.", Range(HeaderLength, 0));
            }
        }

        private int ParseHeader()
        {
            var profileSize = U32(0);
            Raw("Profile size", profileSize, 0, 4);
            var limit = _data.Length;
            if (profileSize < HeaderLength)
            {
                Warn(
                    DiagnosticCodes.InvalidLength,
                    $"Profile size {profileSize} is smaller than the {HeaderLength}-byte ICC header.",
                    0,
                    4);
            }
            else if (profileSize > _data.Length)
            {
                Warn(
                    DiagnosticCodes.TruncatedPayload,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The profile declares {profileSize} byte(s) but only {_data.Length} are available."),
                    0,
                    4);
            }
            else if (profileSize < _data.Length)
            {
                limit = (int)profileSize;
                _fields.Note(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{_data.Length - profileSize} byte(s) follow the declared profile size."),
                    Range((int)profileSize, _data.Length - (int)profileSize));
            }

            Raw("Preferred CMM type", IccFormatting.Signature(_data.AsSpan(4, 4)), 4, 4);
            Raw("Profile version", IccFormatting.Version(_data.AsSpan(8, 4)), 8, 4);
            Raw("Profile/device class", IccFormatting.ProfileClass(_data.AsSpan(12, 4)), 12, 4);
            Raw("Data colour space", IccFormatting.ColorSpace(_data.AsSpan(16, 4)), 16, 4);
            Raw("Profile connection space (PCS)", IccFormatting.ColorSpace(_data.AsSpan(20, 4)), 20, 4);

            var year = U16(24);
            var month = U16(26);
            var day = U16(28);
            var hour = U16(30);
            var minute = U16(32);
            var second = U16(34);
            var dateTime = IccFormatting.DateTime(year, month, day, hour, minute, second);
            Raw("Date and time", dateTime, 24, 12);
            if (dateTime == "invalid")
            {
                Warn(DiagnosticCodes.SchemaMismatch, "The profile date and time fields are not a valid timestamp.", 24, 12);
            }

            var signature = IccFormatting.Signature(_data.AsSpan(36, 4));
            Raw("Profile file signature", signature, 36, 4);
            if (signature != "acsp")
            {
                Warn(DiagnosticCodes.InvalidSignature, "The profile file signature must be 'acsp'.", 36, 4);
            }

            Raw("Primary platform", IccFormatting.Platform(_data.AsSpan(40, 4)), 40, 4);

            var flags = U32(44);
            Raw("Profile flags", FormatProfileFlags(flags), 44, 4);
            if ((flags & ~3u) != 0)
            {
                Warn(DiagnosticCodes.InvalidEncoding, "Profile flags reserved bits are not zero.", 44, 4);
            }

            Raw("Device manufacturer", IccFormatting.Signature(_data.AsSpan(48, 4)), 48, 4);
            Raw("Device model", IccFormatting.Signature(_data.AsSpan(52, 4)), 52, 4);

            var attributes = U64(56);
            Raw("Device attributes", FormatDeviceAttributes(attributes), 56, 8);
            if ((attributes & ~0xFu) != 0)
            {
                Warn(DiagnosticCodes.InvalidEncoding, "Device attributes reserved bits are not zero.", 56, 8);
            }

            var intent = U32(64);
            Raw("Rendering intent", IccFormatting.RenderingIntent(intent), 64, 4);
            if (intent > 3)
            {
                Warn(DiagnosticCodes.InvalidEncoding, $"Rendering intent {intent} is not defined by ICC.", 64, 4);
            }

            Raw(
                "PCS illuminant",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"X={IccFormatting.Fixed(I32(68))}, Y={IccFormatting.Fixed(I32(72))}, Z={IccFormatting.Fixed(I32(76))}"),
                68,
                12);
            Raw("Profile creator", IccFormatting.Signature(_data.AsSpan(80, 4)), 80, 4);

            var profileId = _data.AsSpan(84, 16);
            Raw("Profile ID", profileId.IndexOfAnyExcept((byte)0) < 0 ? "all zero" : Convert.ToHexString(profileId), 84, 16);

            var reserved = _data.AsSpan(100, 28);
            Raw(
                "Reserved",
                reserved.IndexOfAnyExcept((byte)0) < 0 ? "28 bytes, all zero" : Convert.ToHexString(reserved),
                100,
                28);

            return limit;
        }

        private void ParseTagTable(int limit)
        {
            if (_data.Length < TagTableOffset)
            {
                _fields.Note("The profile is too short to contain a tag count.", Range(_data.Length, 0));
                return;
            }

            var declaredCount = U32(TagCountOffset);
            Raw("Tag count", declaredCount, TagCountOffset, 4);
            if (declaredCount == 0)
            {
                _fields.Note("The profile tag table is empty.", Range(TagCountOffset, 4));
                return;
            }

            var required = TagTableOffset + (long)declaredCount * TagEntryLength;
            var count = declaredCount;
            if (required > limit)
            {
                count = (uint)Math.Max(0, (limit - TagTableOffset) / TagEntryLength);
                Warn(
                    DiagnosticCodes.InvalidLength,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The tag table declares {declaredCount} entries but only {count} fit in the profile."),
                    TagCountOffset,
                    4);
            }

            var tableEnd = TagTableOffset + (long)count * TagEntryLength;
            var shown = Math.Min((long)count, _limits.MaxDataLines);
            for (var index = 0; index < shown; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                ParseTag(index, TagTableOffset + (int)index * TagEntryLength, tableEnd, limit);
            }

            if (count > shown)
            {
                Warn(
                    DiagnosticCodes.LimitRecords,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The profile has {count} tag entries; only the first {shown} are shown."),
                    TagCountOffset,
                    4);
            }
        }

        private void ParseTag(int index, int entryOffset, long tableEnd, int limit)
        {
            var prefix = string.Create(CultureInfo.InvariantCulture, $"Tag[{index}].");
            var signature = IccFormatting.Signature(_data.AsSpan(entryOffset, 4));
            Raw(prefix + "Tag signature", FormatTagSignature(signature), entryOffset, 4);

            var dataOffset = U32(entryOffset + 4);
            var dataSize = U32(entryOffset + 8);
            Raw(prefix + "Offset to data", dataOffset, entryOffset + 4, 4);
            Raw(prefix + "Size", dataSize, entryOffset + 8, 4);

            if (dataOffset < tableEnd)
            {
                Warn(
                    DiagnosticCodes.InvalidLength,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Tag data offset {dataOffset} overlaps the profile header or tag table."),
                    entryOffset + 4,
                    4);
                return;
            }

            if (dataOffset > limit || dataSize > limit - dataOffset)
            {
                Warn(
                    DiagnosticCodes.InvalidLength,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Tag data range {dataOffset}+{dataSize} exceeds the {limit}-byte profile."),
                    entryOffset + 4,
                    8);
                return;
            }

            if (dataSize < TagTypeHeaderLength)
            {
                Warn(
                    DiagnosticCodes.InvalidLength,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Tag data is {dataSize} byte(s); every ICC tag type header needs {TagTypeHeaderLength}."),
                    entryOffset + 8,
                    4);
                return;
            }

            var start = (int)dataOffset;
            var size = (int)dataSize;
            var type = Encoding.Latin1.GetString(_data.AsSpan(start, 4));
            Raw(prefix + "Type", FormatTagType(type), start, 4);

            for (var reserved = start + 4; reserved < start + TagTypeHeaderLength; reserved++)
            {
                if (_data[reserved] != 0)
                {
                    Warn(
                        DiagnosticCodes.InvalidEncoding,
                        "The tag type reserved bytes are not zero.",
                        start + 4,
                        4);
                    break;
                }
            }

            ParseTagData(prefix, start, size, type);
        }

        private void ParseTagData(string prefix, int start, int size, string type)
        {
            var data = new IccCursor(_data, start + TagTypeHeaderLength, size - TagTypeHeaderLength);
            switch (type)
            {
                case "XYZ ":
                    ParseXyz(data, prefix);
                    break;
                case "curv":
                    ParseCurve(data, prefix);
                    break;
                case "para":
                    ParseParametricCurve(data, prefix);
                    break;
                case "text":
                    ParseText(data, prefix);
                    break;
                case "desc":
                    ParseTextDescription(data, prefix);
                    break;
                case "mluc":
                    ParseMultiLocalizedUnicode(start, size, prefix);
                    break;
                case "sig ":
                    ParseSignature(data, prefix);
                    break;
                case "sf32":
                    ParseFixedArray(data, prefix);
                    break;
                case "ui32":
                    ParseUInt32Array(data, prefix);
                    break;
                case "ui16":
                    ParseUInt16Array(data, prefix);
                    break;
                case "ui08":
                    ParseByteArray(data, prefix);
                    break;
                case "dtim":
                    ParseDateTime(data, prefix);
                    break;
                case "meas":
                    ParseMeasurement(data, prefix);
                    break;
                case "view":
                    ParseViewingConditions(data, prefix);
                    break;
                case "chrm":
                    ParseChromaticity(data, prefix);
                    break;
                case "mft1":
                    ParseLut8(start, size, prefix);
                    break;
                case "mft2":
                    ParseLut16(start, size, prefix);
                    break;
                case "mAB ":
                case "mBA ":
                    ParseLutAtoB(start, size, prefix);
                    break;
                default:
                    _fields.Note(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Tag type '{RawIdentifier.FormatBytes(Encoding.Latin1.GetBytes(type))}' is not interpreted in this version."),
                        Range(start, size));
                    data.SkipRemaining();
                    break;
            }
        }

        private void ParseXyz(IccCursor data, string prefix)
        {
            if (data.Remaining % 12 != 0)
            {
                Warn(DiagnosticCodes.InvalidLength, "XYZType data is not a whole number of XYZ values.", data.Position, data.Remaining);
            }

            var count = data.Remaining / 12;
            Derived($"<{prefix}ValueCount>", count, data.Position, data.Remaining);
            var shown = Math.Min(count, _limits.MaxDataLines);
            for (var index = 0; index < shown; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var key = string.Create(CultureInfo.InvariantCulture, $"Value[{index}]");
                if (!TryInt32Fixed(data, prefix + key + ".X", out _)
                    || !TryInt32Fixed(data, prefix + key + ".Y", out _)
                    || !TryInt32Fixed(data, prefix + key + ".Z", out _))
                {
                    return;
                }
            }

            if (count > shown)
            {
                WarnLimitRecords(prefix, count, shown, data);
            }
        }

        private void ParseCurve(IccCursor data, string prefix)
        {
            if (!TryUInt32(data, prefix + "Count", out var count))
            {
                return;
            }

            if (count == 0)
            {
                _fields.Note("Count 0 defines an identity curve.", Range(data.LastOffset, data.LastLength));
                return;
            }

            if (count == 1)
            {
                if (!TryUInt16(data, prefix + "Gamma", out var gamma))
                {
                    return;
                }

                Derived($"<{prefix}GammaValue>", IccFormatting.U8Fixed8(gamma), data.LastOffset, data.LastLength);
                return;
            }

            var available = (uint)(data.Remaining / 2);
            if (count > available)
            {
                Warn(
                    DiagnosticCodes.TruncatedPayload,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Curve count {count} exceeds the {available} available UInt16 value(s)."),
                    data.Position,
                    data.Remaining);
            }

            var shown = Math.Min(Math.Min(count, available), (uint)_limits.MaxDataLines);
            for (var index = 0; index < shown; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!TryUInt16(data, string.Create(CultureInfo.InvariantCulture, $"{prefix}Value[{index}]"), out _))
                {
                    return;
                }
            }

            if (count > shown)
            {
                WarnLimitRecords(prefix, count, shown, data);
            }
        }

        private void ParseParametricCurve(IccCursor data, string prefix)
        {
            if (!TryUInt16(data, prefix + "Function type", out var functionType))
            {
                return;
            }

            if (!TryUInt16(data, prefix + "Reserved", out var reserved))
            {
                return;
            }

            if (reserved != 0)
            {
                Warn(DiagnosticCodes.InvalidEncoding, "parametricCurveType reserved field is not zero.", data.LastOffset, 2);
            }

            var parameterCount = functionType switch
            {
                0 => 1,
                1 => 3,
                2 => 4,
                3 => 5,
                4 => 7,
                _ => -1,
            };
            if (parameterCount < 0)
            {
                Warn(
                    DiagnosticCodes.InvalidEncoding,
                    $"parametricCurveType function type {functionType} is not defined.",
                    data.LastOffset,
                    2);
                return;
            }

            Derived($"<{prefix}ParameterCount>", parameterCount, data.Position, data.Remaining);
            for (var index = 0; index < parameterCount; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!TryInt32Fixed(data, string.Create(CultureInfo.InvariantCulture, $"{prefix}Parameter[{index}]"), out _))
                {
                    return;
                }
            }
        }

        private void ParseText(IccCursor data, string prefix)
        {
            var bytes = data.ReadRemainingPrefix(_limits.MaxStringBytes, out var totalLength);
            var textLength = Array.IndexOf(bytes, (byte)0);
            if (textLength < 0)
            {
                textLength = bytes.Length;
            }

            Raw(prefix + "ASCII string", IccFormatting.Latin1(bytes.AsSpan(0, textLength)), data.LastOffset, totalLength);
            Derived($"<{prefix}TextByteLength>", totalLength, data.LastOffset, totalLength);
            if (totalLength > bytes.Length)
            {
                Warn(
                    DiagnosticCodes.LimitString,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The text is {totalLength} byte(s); only the first {bytes.Length} are shown."),
                    data.LastOffset,
                    totalLength);
            }
        }

        private void ParseTextDescription(IccCursor data, string prefix)
        {
            if (!TryUInt32(data, prefix + "ASCII count", out var asciiCount))
            {
                return;
            }

            var remaining = (uint)data.Remaining;
            var asciiTake = (int)Math.Min(asciiCount, Math.Min(remaining, (uint)_limits.MaxStringBytes));
            if (asciiTake > 0)
            {
                var ascii = data.ReadBytes(asciiTake);
                Raw(prefix + "ASCII string", IccFormatting.Latin1(TrimTrailingNull(ascii)), data.LastOffset, data.LastLength);
            }

            if (asciiCount > asciiTake)
            {
                if (asciiCount > remaining)
                {
                    Warn(
                        DiagnosticCodes.TruncatedPayload,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"ASCII string declares {asciiCount} byte(s) but only {remaining} remain."),
                        data.Position,
                        data.Remaining);
                    return;
                }

                Warn(
                    DiagnosticCodes.LimitString,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"ASCII string is {asciiCount} byte(s); only the first {asciiTake} are shown."),
                    data.LastOffset,
                    data.LastLength);
                data.Skip((int)asciiCount - asciiTake);
            }

            if (!TryUInt32(data, prefix + "Unicode language code", out _))
            {
                return;
            }

            if (!TryUInt32(data, prefix + "Unicode count", out var unicodeCount))
            {
                return;
            }

            var unicodeBytes = (ulong)unicodeCount * 2;
            remaining = (uint)data.Remaining;
            if (unicodeBytes > remaining)
            {
                Warn(
                    DiagnosticCodes.TruncatedPayload,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Unicode string declares {unicodeCount} UTF-16 unit(s) but only {data.Remaining} byte(s) remain."),
                    data.Position,
                    data.Remaining);
                return;
            }

            var unicodeTake = (int)Math.Min(unicodeBytes, (uint)_limits.MaxStringBytes);
            if (unicodeTake > 0)
            {
                var unicode = data.ReadBytes(unicodeTake);
                Raw(
                    prefix + "Unicode string",
                    IccFormatting.Utf16Be(TrimTrailingNull16(unicode)),
                    data.LastOffset,
                    data.LastLength);
            }

            if (unicodeBytes > (ulong)unicodeTake)
            {
                Warn(
                    DiagnosticCodes.LimitString,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Unicode string is {unicodeBytes} byte(s); only the first {unicodeTake} are shown."),
                    data.LastOffset,
                    data.LastLength);
                data.Skip((int)unicodeBytes - unicodeTake);
            }

            if (!TryUInt16(data, prefix + "ScriptCode code", out _)
                || !TryByte(data, prefix + "ScriptCode count", out var scriptCount))
            {
                return;
            }

            var scriptTake = Math.Min(Math.Min((int)scriptCount, 67), data.Remaining);
            if (scriptTake > 0)
            {
                var script = data.ReadBytes(scriptTake);
                Raw(prefix + "ScriptCode string", IccFormatting.Latin1(script), data.LastOffset, data.LastLength);
            }

            if (data.Remaining > 0)
            {
                data.SkipRemaining();
            }
        }

        private void ParseMultiLocalizedUnicode(int start, int size, string prefix)
        {
            var data = new IccCursor(_data, start + TagTypeHeaderLength, size - TagTypeHeaderLength);
            if (!TryUInt32(data, prefix + "Record count", out var recordCount)
                || !TryUInt32(data, prefix + "Record size", out var recordSize))
            {
                return;
            }

            if (recordSize < 12)
            {
                Warn(
                    DiagnosticCodes.InvalidLength,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"multiLocalizedUnicodeType record size {recordSize} is smaller than 12."),
                    data.LastOffset,
                    4);
                return;
            }

            var available = recordSize == 0 ? 0 : (uint)(data.Remaining / recordSize);
            if (recordCount > available)
            {
                Warn(
                    DiagnosticCodes.TruncatedPayload,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Record count {recordCount} exceeds the {available} record(s) that fit in the tag."),
                    data.Position,
                    data.Remaining);
            }

            var shown = Math.Min(Math.Min(recordCount, available), (uint)_limits.MaxDataLines);
            Span<byte> language = stackalloc byte[2];
            Span<byte> country = stackalloc byte[2];
            for (var index = 0; index < shown; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var recordPrefix = string.Create(CultureInfo.InvariantCulture, $"{prefix}Record[{index}].");
                var recordStart = data.Position;
                var record = data.Slice(recordStart, (int)Math.Min(recordSize, (uint)data.Remaining));
                if (!record.TryReadBytes(language))
                {
                    Warn(DiagnosticCodes.TruncatedPayload, $"Record[{index}] is truncated.", recordStart, record.Remaining);
                    break;
                }

                var languageOffset = record.LastOffset;
                if (!record.TryReadBytes(country))
                {
                    Warn(DiagnosticCodes.TruncatedPayload, $"Record[{index}] is truncated.", recordStart, record.Remaining);
                    break;
                }

                var countryOffset = record.LastOffset;
                if (!record.TryUInt32BE(out var stringLength))
                {
                    Warn(DiagnosticCodes.TruncatedPayload, $"Record[{index}] is truncated.", recordStart, record.Remaining);
                    break;
                }

                var lengthOffset = record.LastOffset;
                if (!record.TryUInt32BE(out var stringOffset))
                {
                    Warn(DiagnosticCodes.TruncatedPayload, $"Record[{index}] is truncated.", recordStart, record.Remaining);
                    break;
                }

                var offsetOffset = record.LastOffset;
                _fields.Raw(recordPrefix + "Language code", IccFormatting.Latin1(language), Range(languageOffset, 2));
                _fields.Raw(recordPrefix + "Country code", IccFormatting.Latin1(country), Range(countryOffset, 2));
                _fields.Raw(recordPrefix + "Length", stringLength, Range(lengthOffset, 4));
                _fields.Raw(recordPrefix + "Offset", stringOffset, Range(offsetOffset, 4));

                if (stringOffset < TagTypeHeaderLength
                    || stringOffset > (uint)size
                    || stringLength > (uint)size - stringOffset)
                {
                    Warn(
                        DiagnosticCodes.InvalidLength,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Record[{index}] string range {stringOffset}+{stringLength} exceeds the {size}-byte tag."),
                        recordStart,
                        Math.Min((int)recordSize, data.Remaining));
                }
                else
                {
                    var absolute = start + (int)stringOffset;
                    var take = (int)Math.Min(stringLength, (uint)Math.Min(_limits.MaxStringBytes, size - (int)stringOffset));
                    if (take > 0)
                    {
                        if ((take & 1) != 0)
                        {
                            Warn(
                                DiagnosticCodes.InvalidLength,
                                $"Record[{index}] UTF-16 string length is not an even number of bytes.",
                                absolute,
                                take);
                            take--;
                        }

                        var text = _data.AsSpan(absolute, take);
                        _fields.Raw(recordPrefix + "String", IccFormatting.Utf16Be(text), Range(absolute, take));
                    }

                    if (stringLength > (uint)take)
                    {
                        Warn(
                            DiagnosticCodes.LimitString,
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"Record[{index}] string is {stringLength} byte(s); only the first {take} are shown."),
                            absolute,
                            take);
                    }
                }

                var consumed = data.Position - recordStart;
                if (consumed < recordSize)
                {
                    data.Skip((int)Math.Min(recordSize - consumed, (uint)data.Remaining));
                }
            }

            if (recordCount > shown)
            {
                WarnLimitRecords(prefix, recordCount, shown, data);
            }
        }

        private void ParseSignature(IccCursor data, string prefix)
        {
            Span<byte> signature = stackalloc byte[4];
            if (!data.TryReadBytes(signature))
            {
                Truncated(data, prefix + "Signature");
                return;
            }

            Raw(prefix + "Signature", IccFormatting.Signature(signature), data.LastOffset, 4);
        }

        private void ParseFixedArray(IccCursor data, string prefix)
        {
            var count = data.Remaining / 4;
            if (data.Remaining % 4 != 0)
            {
                Warn(DiagnosticCodes.InvalidLength, "s15Fixed16ArrayType data is not a whole number of values.", data.Position, data.Remaining);
            }

            Derived($"<{prefix}ValueCount>", count, data.Position, data.Remaining);
            var shown = Math.Min(count, _limits.MaxDataLines);
            for (var index = 0; index < shown; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!TryInt32Fixed(data, string.Create(CultureInfo.InvariantCulture, $"{prefix}Value[{index}]"), out _))
                {
                    return;
                }
            }

            if (count > shown)
            {
                WarnLimitRecords(prefix, count, shown, data);
            }
        }

        private void ParseUInt32Array(IccCursor data, string prefix) =>
            ParseScalarArray(data, prefix, 4, static cursor =>
            {
                if (!cursor.TryUInt32BE(out var value))
                {
                    return null;
                }

                return value.ToString(CultureInfo.InvariantCulture);
            });

        private void ParseUInt16Array(IccCursor data, string prefix) =>
            ParseScalarArray(data, prefix, 2, static cursor =>
            {
                if (!cursor.TryUInt16BE(out var value))
                {
                    return null;
                }

                return value.ToString(CultureInfo.InvariantCulture);
            });

        private void ParseByteArray(IccCursor data, string prefix) =>
            ParseScalarArray(data, prefix, 1, static cursor =>
            {
                if (!cursor.TryByte(out var value))
                {
                    return null;
                }

                return value.ToString(CultureInfo.InvariantCulture);
            });

        private void ParseScalarArray(
            IccCursor data,
            string prefix,
            int elementSize,
            Func<IccCursor, string?> read)
        {
            var count = data.Remaining / elementSize;
            if (data.Remaining % elementSize != 0)
            {
                Warn(
                    DiagnosticCodes.InvalidLength,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Tag data is not a whole number of {elementSize}-byte values."),
                    data.Position,
                    data.Remaining);
            }

            Derived($"<{prefix}ValueCount>", count, data.Position, data.Remaining);
            var shown = Math.Min(count, _limits.MaxDataLines);
            for (var index = 0; index < shown; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var value = read(data);
                if (value is null)
                {
                    Truncated(data, string.Create(CultureInfo.InvariantCulture, $"{prefix}Value[{index}]"));
                    return;
                }

                _fields.Raw(
                    string.Create(CultureInfo.InvariantCulture, $"{prefix}Value[{index}]"),
                    value,
                    Range(data.LastOffset, elementSize));
            }

            if (count > shown)
            {
                WarnLimitRecords(prefix, count, shown, data);
            }
        }

        private void ParseDateTime(IccCursor data, string prefix)
        {
            if (!TryUInt16(data, prefix + "Year", out var year)
                || !TryUInt16(data, prefix + "Month", out var month)
                || !TryUInt16(data, prefix + "Day", out var day)
                || !TryUInt16(data, prefix + "Hours", out var hours)
                || !TryUInt16(data, prefix + "Minutes", out var minutes)
                || !TryUInt16(data, prefix + "Seconds", out var seconds))
            {
                return;
            }

            var value = IccFormatting.DateTime(year, month, day, hours, minutes, seconds);
            Derived($"<{prefix}DateTime>", value, data.LastOffset, 12);
            if (value == "invalid")
            {
                Warn(DiagnosticCodes.SchemaMismatch, "dateTimeType contains an invalid date or time.", data.LastOffset, 12);
            }
        }

        private void ParseMeasurement(IccCursor data, string prefix)
        {
            if (!TryUInt32(data, prefix + "Observer", out var observer))
            {
                return;
            }

            Derived($"<{prefix}ObserverMeaning>", IccFormatting.Observer(observer), data.LastOffset, 4);

            if (!TryInt32Fixed(data, prefix + "Backing X", out _)
                || !TryInt32Fixed(data, prefix + "Backing Y", out _)
                || !TryInt32Fixed(data, prefix + "Backing Z", out _)
                || !TryUInt32(data, prefix + "Geometry", out var geometry))
            {
                return;
            }

            Derived($"<{prefix}GeometryMeaning>", IccFormatting.Geometry(geometry), data.LastOffset, 4);

            if (!TryUInt32(data, prefix + "Flare", out _)
                || !TryUInt32(data, prefix + "Illuminant", out var illuminant))
            {
                return;
            }

            Derived($"<{prefix}IlluminantMeaning>", IccFormatting.Illuminant(illuminant), data.LastOffset, 4);
        }

        private void ParseViewingConditions(IccCursor data, string prefix)
        {
            if (!TryInt32Fixed(data, prefix + "Illuminant X", out _)
                || !TryInt32Fixed(data, prefix + "Illuminant Y", out _)
                || !TryInt32Fixed(data, prefix + "Illuminant Z", out _)
                || !TryInt32Fixed(data, prefix + "Surround X", out _)
                || !TryInt32Fixed(data, prefix + "Surround Y", out _)
                || !TryInt32Fixed(data, prefix + "Surround Z", out _)
                || !TryUInt32(data, prefix + "Illuminant type", out var illuminant))
            {
                return;
            }

            Derived($"<{prefix}IlluminantMeaning>", IccFormatting.Illuminant(illuminant), data.LastOffset, 4);
        }

        private void ParseChromaticity(IccCursor data, string prefix)
        {
            if (!TryUInt16(data, prefix + "Number of device channels", out var channels)
                || !TryUInt16(data, prefix + "Colorant type", out var colorantType))
            {
                return;
            }

            Derived($"<{prefix}ColorantTypeMeaning>", IccFormatting.ColorantType(colorantType), data.LastOffset, 2);

            var available = (uint)(data.Remaining / 8);
            if (channels > available)
            {
                Warn(
                    DiagnosticCodes.TruncatedPayload,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Chromaticity declares {channels} channel(s) but only {available} fit in the tag."),
                    data.Position,
                    data.Remaining);
            }

            var shown = Math.Min(Math.Min(channels, available), (uint)_limits.MaxDataLines);
            for (var index = 0; index < shown; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var key = string.Create(CultureInfo.InvariantCulture, $"{prefix}Channel[{index}].");
                if (!TryInt32Fixed(data, key + "x", out _)
                    || !TryInt32Fixed(data, key + "y", out _))
                {
                    return;
                }
            }

            if (channels > shown)
            {
                WarnLimitRecords(prefix, channels, shown, data);
            }
        }

        private void ParseLut8(int start, int size, string prefix)
        {
            var data = new IccCursor(_data, start + TagTypeHeaderLength, size - TagTypeHeaderLength);
            if (!TryByte(data, prefix + "Input channels", out _)
                || !TryByte(data, prefix + "Output channels", out _)
                || !TryByte(data, prefix + "CLUT grid points", out _)
                || !TryByte(data, prefix + "Padding", out var padding))
            {
                return;
            }

            if (padding != 0)
            {
                Warn(DiagnosticCodes.InvalidEncoding, "lut8Type padding byte is not zero.", data.LastOffset, 1);
            }

            ParseMatrix(data, prefix);
            Derived($"<{prefix}InputTableEntries>", 256, data.Position, data.Remaining);
            Derived($"<{prefix}OutputTableEntries>", 256, data.Position, data.Remaining);
            _fields.Note("The lut8Type input, CLUT and output tables are not expanded.", Range(start, size));
        }

        private void ParseLut16(int start, int size, string prefix)
        {
            var data = new IccCursor(_data, start + TagTypeHeaderLength, size - TagTypeHeaderLength);
            if (!TryByte(data, prefix + "Input channels", out _)
                || !TryByte(data, prefix + "Output channels", out _)
                || !TryByte(data, prefix + "CLUT grid points", out _)
                || !TryByte(data, prefix + "Padding", out var padding))
            {
                return;
            }

            if (padding != 0)
            {
                Warn(DiagnosticCodes.InvalidEncoding, "lut16Type padding byte is not zero.", data.LastOffset, 1);
            }

            ParseMatrix(data, prefix);
            if (!TryUInt16(data, prefix + "Input table entries", out _)
                || !TryUInt16(data, prefix + "Output table entries", out _))
            {
                return;
            }

            _fields.Note("The lut16Type input, CLUT and output tables are not expanded.", Range(start, size));
        }

        private void ParseLutAtoB(int start, int size, string prefix)
        {
            var data = new IccCursor(_data, start + TagTypeHeaderLength, size - TagTypeHeaderLength);
            if (!TryByte(data, prefix + "Input channels", out var inputChannels)
                || !TryByte(data, prefix + "Output channels", out _)
                || !TryUInt16(data, prefix + "Padding", out var padding))
            {
                return;
            }

            if (padding != 0)
            {
                Warn(DiagnosticCodes.InvalidEncoding, "lutAtoBType/lutBtoAType padding field is not zero.", data.LastOffset, 2);
            }

            string[] names =
            [
                "Offset to B curves",
                "Offset to matrix",
                "Offset to M curves",
                "Offset to CLUT",
                "Offset to A curves",
            ];
            var offsets = new uint[names.Length];
            for (var index = 0; index < names.Length; index++)
            {
                if (!TryUInt32(data, prefix + names[index], out offsets[index]))
                {
                    return;
                }
            }

            if (offsets[1] != 0)
            {
                var matrixStart = start + (long)offsets[1];
                if (matrixStart + 36 > start + size)
                {
                    Warn(
                        DiagnosticCodes.InvalidLength,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Matrix offset {offsets[1]} does not leave room for a 3x3 s15Fixed16 matrix."),
                        start,
                        size);
                }
                else
                {
                    var matrix = new IccCursor(_data, (int)matrixStart, 36);
                    ParseMatrix(matrix, prefix);
                }
            }

            if (offsets[3] != 0)
            {
                var clutStart = start + (long)offsets[3];
                if (clutStart + inputChannels > start + size)
                {
                    Warn(
                        DiagnosticCodes.InvalidLength,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"CLUT offset {offsets[3]} does not fit in the tag."),
                        start,
                        size);
                }
                else
                {
                    var clut = new IccCursor(_data, (int)clutStart, start + size - (int)clutStart);
                    for (var index = 0; index < inputChannels; index++)
                    {
                        if (!TryByte(clut, string.Create(CultureInfo.InvariantCulture, $"{prefix}CLUT grid points[{index}]"), out _))
                        {
                            return;
                        }
                    }

                    if (clut.TryByte(out var precision))
                    {
                        Raw(prefix + "CLUT precision", precision, clut.LastOffset, 1);
                        if (precision is not (1 or 2))
                        {
                            Warn(
                                DiagnosticCodes.InvalidEncoding,
                                $"CLUT precision {precision} must be 1 or 2.",
                                clut.LastOffset,
                                1);
                        }
                    }
                }
            }

            _fields.Note(
                "Referenced B/M/A curve data and CLUT samples are not expanded in this version.",
                Range(start, size));
        }

        private void ParseMatrix(IccCursor data, string prefix)
        {
            for (var index = 0; index < 9; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!TryInt32Fixed(data, string.Create(CultureInfo.InvariantCulture, $"{prefix}Matrix[{index}]"), out _))
                {
                    return;
                }
            }
        }

        private bool TryByte(IccCursor data, string key, out byte value)
        {
            if (data.TryByte(out value))
            {
                Raw(key, value, data.LastOffset, 1);
                return true;
            }

            return Truncated(data, key);
        }

        private bool TryUInt16(IccCursor data, string key, out ushort value)
        {
            if (data.TryUInt16BE(out value))
            {
                Raw(key, value, data.LastOffset, 2);
                return true;
            }

            return Truncated(data, key);
        }

        private bool TryUInt32(IccCursor data, string key, out uint value)
        {
            if (data.TryUInt32BE(out value))
            {
                Raw(key, value, data.LastOffset, 4);
                return true;
            }

            return Truncated(data, key);
        }

        private bool TryInt32Fixed(IccCursor data, string key, out int value)
        {
            if (data.TryInt32BE(out value))
            {
                Raw(key, IccFormatting.Fixed(value), data.LastOffset, 4);
                return true;
            }

            return Truncated(data, key);
        }

        private bool Truncated(IccCursor data, string key)
        {
            Warn(
                DiagnosticCodes.TruncatedPayload,
                $"Field '{key}' is truncated.",
                data.Position,
                data.Remaining);
            return false;
        }

        private void WarnLimitRecords(string prefix, long declared, long shown, IccCursor data) =>
            _fields.Warning(
                DiagnosticCodes.LimitRecords,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{prefix} declares {declared} value(s); only the first {shown} are shown."),
                Range(data.Position, data.Remaining));

        private void Raw(string key, string value, int offset, int length) =>
            _fields.Raw(key, value, Range(offset, length));

        private void Raw(string key, uint value, int offset, int length) =>
            _fields.Raw(key, value, Range(offset, length));

        private void Raw(string key, ushort value, int offset, int length) =>
            _fields.Raw(key, value, Range(offset, length));

        private void Raw(string key, byte value, int offset, int length) =>
            _fields.Raw(key, value.ToString(CultureInfo.InvariantCulture), Range(offset, length));

        private void Derived(string key, string value, int offset, int length) =>
            _fields.Derived(key, value, Range(offset, length));

        private void Derived(string key, long value, int offset, int length) =>
            _fields.Derived(key, value, Range(offset, length));

        private void Warn(string code, string message, int offset, int length) =>
            _fields.Warning(code, message, Range(offset, length));

        private SourceRange? Range(int offset, int length) =>
            _profileOffset is { } origin ? new SourceRange(origin + offset, length) : _fallbackRange;

        private uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset, 4));

        private ushort U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset, 2));

        private int I32(int offset) => BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(offset, 4));

        private ulong U64(int offset) => BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(offset, 8));

        private static string FormatProfileFlags(uint value)
        {
            var names = new List<string>(2);
            if ((value & 1) != 0)
            {
                names.Add("embedded");
            }

            if ((value & 2) != 0)
            {
                names.Add("cannot be used independently");
            }

            var text = names.Count == 0 ? "none" : string.Join(", ", names);
            return string.Create(CultureInfo.InvariantCulture, $"0x{value:X8} ({text})");
        }

        private static string FormatDeviceAttributes(ulong value)
        {
            var names = new List<string>(4)
            {
                (value & 1) != 0 ? "transparency" : "reflective",
                (value & 2) != 0 ? "matte" : "glossy",
                (value & 4) != 0 ? "negative" : "positive",
                (value & 8) != 0 ? "black-and-white" : "colour",
            };
            return string.Create(CultureInfo.InvariantCulture, $"0x{value:X16} ({string.Join(", ", names)})");
        }

        private static string FormatTagSignature(string signature)
        {
            var name = IccFormatting.TagName(signature);
            return name is null ? signature : $"{signature} ({name})";
        }

        private static string FormatTagType(string type)
        {
            var name = IccFormatting.TypeName(type);
            var escaped = RawIdentifier.FormatBytes(Encoding.Latin1.GetBytes(type));
            return name is null ? escaped : $"{escaped} ({name})";
        }

        private static ReadOnlySpan<byte> TrimTrailingNull(byte[] bytes)
        {
            var length = bytes.Length;
            while (length > 0 && bytes[length - 1] == 0)
            {
                length--;
            }

            return bytes.AsSpan(0, length);
        }

        private static ReadOnlySpan<byte> TrimTrailingNull16(byte[] bytes)
        {
            var length = bytes.Length & ~1;
            while (length >= 2 && bytes[length - 1] == 0 && bytes[length - 2] == 0)
            {
                length -= 2;
            }

            return bytes.AsSpan(0, length);
        }
    }
}
