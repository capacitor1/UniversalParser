namespace UniversalParser.Core.Binary;

/// <summary>
/// Half-open source interval: [Offset, Offset + Length).
/// </summary>
public readonly record struct SourceRange
{
    public SourceRange(long offset, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (offset > long.MaxValue - length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Offset + length overflows Int64.");
        }

        Offset = offset;
        Length = length;
    }

    public long Offset { get; }
    public long Length { get; }
    public long End => Offset + Length;
    public bool IsEmpty => Length == 0;

    public static SourceRange Empty => new(0, 0);

    public static SourceRange FromBounds(long start, long end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "End must not be smaller than start.");
        }

        return new SourceRange(start, end - start);
    }

    public bool Contains(long position) => position >= Offset && position < End;

    public bool Contains(SourceRange other) => other.Offset >= Offset && other.End <= End;

    public bool Overlaps(SourceRange other) => !IsEmpty && !other.IsEmpty && Offset < other.End && other.Offset < End;

    public SourceRange Slice(long offset, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (offset > Length || length > Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Slice exceeds the source range.");
        }

        return new SourceRange(Offset + offset, length);
    }

    public override string ToString() => $"0x{Offset:X}+0x{Length:X}";
}
