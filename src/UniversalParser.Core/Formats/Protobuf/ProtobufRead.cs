using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Protobuf;

internal enum ProtobufReadFailure
{
    None,
    TruncatedHeader,
    InvalidVarint,
    InvalidFieldNumber,
    InvalidWireType,
}

/// <summary>
/// One decoded protobuf key/value framing. The header never copies the payload; it only records
/// source ranges so selection stays lazy. <see cref="ValueStart"/> is the payload start for
/// length-delimited fields, the value start for scalar fields, and the group body start for
/// start-group fields.
/// </summary>
internal readonly record struct ProtobufFieldHeader(
    long Start,
    int KeyLength,
    ulong Key,
    uint FieldNumber,
    int WireType,
    long ValueStart,
    long ValueEnd,
    long LengthStart,
    int LengthLength,
    ulong VarintValue,
    ulong DeclaredLength,
    bool Truncated)
{
    public SourceRange KeyRange => new(Start, KeyLength);
    public SourceRange ValueRange => SourceRange.FromBounds(ValueStart, ValueEnd);
    public SourceRange LengthRange => new(LengthStart, LengthLength);
    public long End => WireType == ProtobufConstants.EndGroupWireType ? ValueStart : ValueEnd;
}

/// <summary>
/// Bounded protobuf key/varint/length reading. A key is (field_number &lt;&lt; 3) | wire_type;
/// field number zero and wire types 6/7 are invalid, while an unterminated 10-byte varint is a
/// structural failure rather than an endless read.
/// </summary>
internal static class ProtobufRead
{
    public static bool TryReadVarint(
        IRandomAccessSource source,
        long offset,
        long limit,
        out ulong value,
        out int length,
        out ProtobufReadFailure failure)
    {
        value = 0;
        length = 0;
        failure = ProtobufReadFailure.None;

        if (offset < 0 || limit < offset || limit > source.Length)
        {
            failure = ProtobufReadFailure.TruncatedHeader;
            return false;
        }

        var available = (int)Math.Min(ProtobufConstants.MaxVarintBytes, limit - offset);
        if (available <= 0)
        {
            failure = ProtobufReadFailure.TruncatedHeader;
            return false;
        }

        Span<byte> buffer = stackalloc byte[ProtobufConstants.MaxVarintBytes];
        var read = SourceRead.ReadAtMost(source, offset, buffer[..available]);
        if (read <= 0)
        {
            failure = ProtobufReadFailure.TruncatedHeader;
            return false;
        }

        for (var index = 0; index < read; index++)
        {
            var item = buffer[index];
            if (index == 9 && item > 1)
            {
                failure = ProtobufReadFailure.InvalidVarint;
                return false;
            }

            value |= (ulong)(item & 0x7F) << (index * 7);
            length = index + 1;
            if ((item & 0x80) == 0)
            {
                return true;
            }
        }

        failure = ProtobufReadFailure.TruncatedHeader;
        return false;
    }

    public static bool TryReadFieldHeader(
        IRandomAccessSource source,
        long offset,
        long limit,
        out ProtobufFieldHeader header,
        out ProtobufReadFailure failure)
    {
        header = default;
        if (!TryReadVarint(source, offset, limit, out var key, out var keyLength, out failure))
        {
            return false;
        }

        var fieldNumber = key >> 3;
        if (fieldNumber == 0 || fieldNumber > ProtobufConstants.MaxFieldNumber)
        {
            failure = ProtobufReadFailure.InvalidFieldNumber;
            return false;
        }

        var wireType = (int)(key & 0x7);
        if (wireType is 6 or 7)
        {
            failure = ProtobufReadFailure.InvalidWireType;
            return false;
        }

        var valueStart = offset + keyLength;
        switch (wireType)
        {
            case ProtobufConstants.VarintWireType:
                {
                    if (!TryReadVarint(source, valueStart, limit, out var value, out var valueLength, out failure))
                    {
                        return false;
                    }

                    header = new ProtobufFieldHeader(
                        offset,
                        keyLength,
                        key,
                        (uint)fieldNumber,
                        wireType,
                        valueStart,
                        valueStart + valueLength,
                        valueStart,
                        valueLength,
                        value,
                        (ulong)valueLength,
                        Truncated: false);
                    return true;
                }

            case ProtobufConstants.Fixed64WireType:
                {
                    if (limit - valueStart < 8)
                    {
                        failure = ProtobufReadFailure.TruncatedHeader;
                        return false;
                    }

                    header = CreateFixedHeader(offset, keyLength, key, fieldNumber, wireType, valueStart, 8);
                    return true;
                }

            case ProtobufConstants.Fixed32WireType:
                {
                    if (limit - valueStart < 4)
                    {
                        failure = ProtobufReadFailure.TruncatedHeader;
                        return false;
                    }

                    header = CreateFixedHeader(offset, keyLength, key, fieldNumber, wireType, valueStart, 4);
                    return true;
                }

            case ProtobufConstants.LengthDelimitedWireType:
                {
                    if (!TryReadVarint(source, valueStart, limit, out var declaredLength, out var lengthLength, out failure))
                    {
                        return false;
                    }

                    var payloadStart = valueStart + lengthLength;
                    var remaining = limit - payloadStart;
                    var truncated = declaredLength > (ulong)remaining;
                    var payloadEnd = truncated ? limit : payloadStart + (long)declaredLength;
                    header = new ProtobufFieldHeader(
                        offset,
                        keyLength,
                        key,
                        (uint)fieldNumber,
                        wireType,
                        payloadStart,
                        payloadEnd,
                        valueStart,
                        lengthLength,
                        VarintValue: 0,
                        declaredLength,
                        truncated);
                    return true;
                }

            case ProtobufConstants.StartGroupWireType:
                {
                    header = new ProtobufFieldHeader(
                        offset,
                        keyLength,
                        key,
                        (uint)fieldNumber,
                        wireType,
                        valueStart,
                        limit,
                        valueStart,
                        LengthLength: 0,
                        VarintValue: 0,
                        DeclaredLength: 0,
                        Truncated: false);
                    return true;
                }

            case ProtobufConstants.EndGroupWireType:
                {
                    header = new ProtobufFieldHeader(
                        offset,
                        keyLength,
                        key,
                        (uint)fieldNumber,
                        wireType,
                        valueStart,
                        valueStart,
                        valueStart,
                        LengthLength: 0,
                        VarintValue: 0,
                        DeclaredLength: 0,
                        Truncated: false);
                    return true;
                }

            default:
                failure = ProtobufReadFailure.InvalidWireType;
                return false;
        }
    }

    private static ProtobufFieldHeader CreateFixedHeader(
        long start,
        int keyLength,
        ulong key,
        ulong fieldNumber,
        int wireType,
        long valueStart,
        int valueLength) =>
        new(
            start,
            keyLength,
            key,
            (uint)fieldNumber,
            wireType,
            valueStart,
            valueStart + valueLength,
            valueStart,
            valueLength,
            VarintValue: 0,
            DeclaredLength: 0,
            Truncated: false);

    public static string GetFailureMessage(ProtobufReadFailure failure) => failure switch
    {
        ProtobufReadFailure.TruncatedHeader =>
            "The protobuf key or value is truncated before the field boundary.",
        ProtobufReadFailure.InvalidVarint =>
            "The protobuf varint exceeds ten bytes or overflows 64 bits.",
        ProtobufReadFailure.InvalidFieldNumber =>
            "The protobuf field number is zero or exceeds the 29-bit maximum.",
        ProtobufReadFailure.InvalidWireType =>
            "The protobuf wire type is reserved (6 or 7).",
        _ => "The protobuf field could not be decoded.",
    };
}
