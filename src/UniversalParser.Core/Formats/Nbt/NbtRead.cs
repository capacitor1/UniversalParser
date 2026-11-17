using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Nbt;

internal enum NbtReadFailure
{
    None,
    TruncatedHeader,
    InvalidTagType,
    EndTag,
}

internal readonly record struct NbtTagHeader(
    long Start,
    byte Type,
    ushort NameLength,
    long NameStart,
    string Name,
    bool NameInvalid,
    long PayloadStart);

/// <summary>
/// Big-endian NBT header/primitive reads. TAG_End is reported separately because it terminates a
/// compound and never carries a name or payload of its own.
/// </summary>
internal static class NbtRead
{
    public static bool TryReadByte(
        IRandomAccessSource source,
        long offset,
        long limit,
        out byte value)
    {
        value = 0;
        if (offset < 0 || limit < offset || limit > source.Length)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[1];
        if (SourceRead.ReadAtMost(source, offset, buffer) != 1)
        {
            return false;
        }

        value = buffer[0];
        return true;
    }

    public static bool TryReadUInt16BE(
        IRandomAccessSource source,
        long offset,
        long limit,
        out ushort value)
    {
        value = 0;
        if (offset < 0 || limit - offset < 2 || limit > source.Length)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[2];
        if (SourceRead.ReadAtMost(source, offset, buffer) != 2)
        {
            return false;
        }

        value = (ushort)(buffer[0] << 8 | buffer[1]);
        return true;
    }

    public static bool TryReadInt32BE(
        IRandomAccessSource source,
        long offset,
        long limit,
        out int value)
    {
        value = 0;
        if (offset < 0 || limit - offset < 4 || limit > source.Length)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[4];
        if (SourceRead.ReadAtMost(source, offset, buffer) != 4)
        {
            return false;
        }

        value = buffer[0] << 24 | buffer[1] << 16 | buffer[2] << 8 | buffer[3];
        return true;
    }

    public static bool TryReadTagHeader(
        IRandomAccessSource source,
        long offset,
        long limit,
        out NbtTagHeader header,
        out NbtReadFailure failure)
    {
        header = default;
        if (!TryReadByte(source, offset, limit, out var type))
        {
            failure = NbtReadFailure.TruncatedHeader;
            return false;
        }

        if (type == NbtConstants.EndType)
        {
            failure = NbtReadFailure.EndTag;
            return false;
        }

        if (!NbtConstants.IsValidTagType(type))
        {
            failure = NbtReadFailure.InvalidTagType;
            return false;
        }

        if (!TryReadUInt16BE(source, offset + 1, limit, out var nameLength))
        {
            failure = NbtReadFailure.TruncatedHeader;
            return false;
        }

        var nameStart = offset + 3;
        if (nameLength > limit - nameStart)
        {
            failure = NbtReadFailure.TruncatedHeader;
            return false;
        }

        var nameBytes = new byte[nameLength];
        if (nameLength > 0 && SourceRead.ReadAtMost(source, nameStart, nameBytes) != nameLength)
        {
            failure = NbtReadFailure.TruncatedHeader;
            return false;
        }

        var name = TextDecoding.Utf8(nameBytes, complete: true, out var nameInvalid);
        header = new NbtTagHeader(
            offset,
            type,
            nameLength,
            nameStart,
            TextEscaping.Escape(name),
            nameInvalid,
            nameStart + nameLength);
        failure = NbtReadFailure.None;
        return true;
    }

    public static string GetFailureCode(NbtReadFailure failure) => failure switch
    {
        NbtReadFailure.TruncatedHeader => DiagnosticCodes.TruncatedHeader,
        NbtReadFailure.InvalidTagType => DiagnosticCodes.InvalidTagType,
        NbtReadFailure.EndTag => DiagnosticCodes.InvalidTagType,
        _ => DiagnosticCodes.SchemaMismatch,
    };

    public static string GetFailureMessage(NbtReadFailure failure) => failure switch
    {
        NbtReadFailure.TruncatedHeader => "The NBT tag header is truncated.",
        NbtReadFailure.InvalidTagType => "The NBT tag type is outside the range 1..12.",
        NbtReadFailure.EndTag => "TAG_End cannot be used as a named tag at this position.",
        _ => "The NBT tag could not be decoded.",
    };
}
