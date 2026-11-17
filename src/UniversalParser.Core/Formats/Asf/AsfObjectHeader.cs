namespace UniversalParser.Core.Formats.Asf;

/// <summary>
/// One ASF object header resolved against a bounded container. <see cref="End"/> is the clamped
/// range end; <see cref="RawSize"/> is the unmodified Object Size value from the file.
/// </summary>
internal readonly record struct AsfObjectHeader(
    Guid Id,
    ulong RawSize,
    long Offset,
    long End,
    bool Truncated,
    bool SizeZero)
{
    public long PayloadOffset => Offset + AsfConstants.ObjectHeaderLength;
}
