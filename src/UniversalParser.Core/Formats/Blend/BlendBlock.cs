using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Blend;

internal enum BlendBlockReadError
{
    None,
    Truncated,
    InvalidLength,
}

/// <summary>
/// One blend block header. Version 0 uses BHead4 (20 bytes) or SmallBHead8 (24 bytes);
/// version 1 uses LargeBHead8 (32 bytes: code, SDNAnr, old, 64-bit len, 64-bit nr).
/// </summary>
internal readonly record struct BlendBlockHeader(
    long Start,
    long DataStart,
    long End,
    long DeclaredEnd,
    int HeaderSize,
    string Code,
    int SdnaIndex,
    ulong OldAddress,
    long Length,
    long Count)
{
    public bool Truncated => End < DeclaredEnd;
    public long DataLength => Math.Max(0, End - DataStart);
    public bool IsLegacyEnd => HeaderSize == 8;
}

internal static class BlendBlockReader
{
    public static ReadOnlySpan<byte> EndCode => "ENDB"u8;

    public static bool TryRead(
        IRandomAccessSource source,
        long position,
        long limit,
        in BlendHeader header,
        out BlendBlockHeader block,
        out BlendBlockReadError error)
    {
        ArgumentNullException.ThrowIfNull(source);
        block = default;
        error = BlendBlockReadError.Truncated;

        var headerSize = GetHeaderSize(header);
        var available = Math.Min(limit, source.Length) - position;
        if (position < 0 || available < headerSize)
        {
            if (available >= 8)
            {
                Span<byte> legacy = stackalloc byte[8];
                if (SourceRead.ReadAtMost(source, position, legacy) == legacy.Length
                    && legacy[..4].SequenceEqual(EndCode))
                {
                    block = new BlendBlockHeader(
                        position,
                        position + 8,
                        position + 8,
                        position + 8,
                        8,
                        "ENDB",
                        0,
                        0,
                        0,
                        0);
                    error = BlendBlockReadError.None;
                    return true;
                }
            }

            return false;
        }

        Span<byte> bytes = stackalloc byte[32];
        if (SourceRead.ReadAtMost(source, position, bytes[..headerSize]) != headerSize)
        {
            return false;
        }

        var code = DecodeCode(bytes[..4]);
        var little = header.Endian == BlendEndian.Little;
        int sdnaIndex;
        ulong oldAddress;
        long length;
        long count;

        if (header.FileFormatVersion == 1)
        {
            sdnaIndex = ReadInt32(bytes[4..8], little);
            oldAddress = ReadUInt64(bytes[8..16], little);
            length = ReadInt64(bytes[16..24], little);
            count = ReadInt64(bytes[24..32], little);
        }
        else if (header.PointerSize == 8)
        {
            length = ReadInt32(bytes[4..8], little);
            oldAddress = ReadUInt64(bytes[8..16], little);
            sdnaIndex = ReadInt32(bytes[16..20], little);
            count = ReadInt32(bytes[20..24], little);
        }
        else
        {
            length = ReadInt32(bytes[4..8], little);
            oldAddress = ReadUInt32(bytes[8..12], little);
            sdnaIndex = ReadInt32(bytes[12..16], little);
            count = ReadInt32(bytes[16..20], little);
        }

        if (length < 0 || count < 0)
        {
            error = BlendBlockReadError.InvalidLength;
            return false;
        }

        var dataStart = position + headerSize;
        if (length > long.MaxValue - dataStart)
        {
            error = BlendBlockReadError.InvalidLength;
            return false;
        }

        var declaredEnd = dataStart + length;
        var end = Math.Min(declaredEnd, Math.Min(limit, source.Length));
        block = new BlendBlockHeader(
            position,
            dataStart,
            end,
            declaredEnd,
            headerSize,
            code,
            sdnaIndex,
            oldAddress,
            length,
            count);
        error = BlendBlockReadError.None;
        return true;
    }

    public static int GetHeaderSize(in BlendHeader header) =>
        header.FileFormatVersion == 1 ? 32 : header.PointerSize == 8 ? 24 : 20;

    private static string DecodeCode(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(4);
        foreach (var item in bytes)
        {
            if (item == 0)
            {
                break;
            }

            if (item is >= 0x20 and <= 0x7E)
            {
                builder.Append((char)item);
            }
            else
            {
                builder.Append("\\x").Append(item.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.Length == 0 ? "<empty>" : builder.ToString();
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, bool little) =>
        little
            ? BinaryPrimitives.ReadInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadInt32BigEndian(bytes);

    private static long ReadInt64(ReadOnlySpan<byte> bytes, bool little) =>
        little
            ? BinaryPrimitives.ReadInt64LittleEndian(bytes)
            : BinaryPrimitives.ReadInt64BigEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool little) =>
        little
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, bool little) =>
        little
            ? BinaryPrimitives.ReadUInt64LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt64BigEndian(bytes);
}
