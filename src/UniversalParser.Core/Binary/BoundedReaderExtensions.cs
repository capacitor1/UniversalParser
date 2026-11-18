namespace UniversalParser.Core.Binary;

/// <summary>
/// Minimal bounded-reader surface shared by format-specific cursors. Implementations only expose
/// raw reads; the extension methods below add the common "fail instead of throwing" contract and
/// LastRange bookkeeping so every format reports truncation the same way.
/// </summary>
internal interface IBoundedReader
{
    long Position { get; }

    long Remaining { get; }

    SourceRange LastRange { get; set; }

    byte ReadByteCore();

    ushort ReadUInt16Core(Endian endian);

    uint ReadUInt32Core(Endian endian);

    ulong ReadUInt64Core(Endian endian);

    void ReadExactlyCore(Span<byte> destination);

    void SkipCore(long count);
}

internal static class BoundedReaderExtensions
{
    public static bool TryByte(this IBoundedReader reader, out byte value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.Remaining < 1)
        {
            value = 0;
            return false;
        }

        var start = reader.Position;
        value = reader.ReadByteCore();
        reader.LastRange = new SourceRange(start, 1);
        return true;
    }

    public static bool TryUInt16(this IBoundedReader reader, Endian endian, out ushort value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.Remaining < 2)
        {
            value = 0;
            return false;
        }

        var start = reader.Position;
        value = reader.ReadUInt16Core(endian);
        reader.LastRange = new SourceRange(start, 2);
        return true;
    }

    public static bool TryUInt16BE(this IBoundedReader reader, out ushort value) =>
        reader.TryUInt16(Endian.Big, out value);

    public static bool TryUInt16LE(this IBoundedReader reader, out ushort value) =>
        reader.TryUInt16(Endian.Little, out value);

    public static bool TryUInt32(this IBoundedReader reader, Endian endian, out uint value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.Remaining < 4)
        {
            value = 0;
            return false;
        }

        var start = reader.Position;
        value = reader.ReadUInt32Core(endian);
        reader.LastRange = new SourceRange(start, 4);
        return true;
    }

    public static bool TryUInt32BE(this IBoundedReader reader, out uint value) =>
        reader.TryUInt32(Endian.Big, out value);

    public static bool TryUInt32LE(this IBoundedReader reader, out uint value) =>
        reader.TryUInt32(Endian.Little, out value);

    public static bool TryUInt64(this IBoundedReader reader, Endian endian, out ulong value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.Remaining < 8)
        {
            value = 0;
            return false;
        }

        var start = reader.Position;
        value = reader.ReadUInt64Core(endian);
        reader.LastRange = new SourceRange(start, 8);
        return true;
    }

    public static bool TryUInt64BE(this IBoundedReader reader, out ulong value) =>
        reader.TryUInt64(Endian.Big, out value);

    public static bool TryUInt64LE(this IBoundedReader reader, out ulong value) =>
        reader.TryUInt64(Endian.Little, out value);

    public static bool TryReadBytes(this IBoundedReader reader, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.Remaining < destination.Length)
        {
            return false;
        }

        var start = reader.Position;
        reader.ReadExactlyCore(destination);
        reader.LastRange = new SourceRange(start, destination.Length);
        return true;
    }

    public static byte[] ReadRemainingPrefix(
        this IBoundedReader reader,
        int maxBytes,
        out long totalLength,
        out SourceRange range)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var start = reader.Position;
        totalLength = reader.Remaining;
        range = new SourceRange(start, totalLength);
        var count = (int)Math.Min(totalLength, maxBytes);
        if (count == 0)
        {
            reader.SkipCore(totalLength);
            return [];
        }

        var bytes = new byte[count];
        reader.ReadExactlyCore(bytes);
        reader.SkipCore(totalLength - count);
        reader.LastRange = range;
        return bytes;
    }
}
