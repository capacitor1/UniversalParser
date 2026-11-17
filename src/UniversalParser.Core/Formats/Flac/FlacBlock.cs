using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Flac;

internal enum FlacBlockReadError
{
    None,
    TruncatedHeader,
}

/// <summary>
/// One FLAC metadata block header (RFC 9639 section 8.1): 1-bit last flag, 7-bit type and a
/// 24-bit big-endian length. The range is clamped so a truncated final block stays visible.
/// </summary>
internal readonly record struct FlacBlockHeader(
    long Start,
    long DataStart,
    long End,
    long DeclaredEnd,
    bool LastBlock,
    byte Type,
    uint Length)
{
    public bool Truncated => End < DeclaredEnd;
    public long DataLength => Math.Max(0, End - DataStart);
}

internal static class FlacBlock
{
    public static bool TryReadHeader(
        IRandomAccessSource source,
        long position,
        long limit,
        out FlacBlockHeader header,
        out FlacBlockReadError error)
    {
        ArgumentNullException.ThrowIfNull(source);
        header = default;
        error = FlacBlockReadError.TruncatedHeader;

        var available = Math.Min(limit, source.Length) - position;
        if (position < 0 || available < FlacConstants.BlockHeaderLength)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[FlacConstants.BlockHeaderLength];
        if (SourceRead.ReadAtMost(source, position, bytes) != bytes.Length)
        {
            return false;
        }

        var length = (uint)(bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        var dataStart = position + FlacConstants.BlockHeaderLength;
        var declaredEnd = dataStart + length;
        var end = Math.Min(declaredEnd, Math.Min(limit, source.Length));
        header = new FlacBlockHeader(
            position,
            dataStart,
            end,
            declaredEnd,
            (bytes[0] & 0x80) != 0,
            (byte)(bytes[0] & 0x7F),
            length);
        error = FlacBlockReadError.None;
        return true;
    }
}
