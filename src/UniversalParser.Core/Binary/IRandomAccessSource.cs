namespace UniversalParser.Core.Binary;

/// <summary>
/// Read-only random access over a byte source. Implementations never expose a mutable position.
/// </summary>
public interface IRandomAccessSource : IDisposable
{
    long Length { get; }

    /// <summary>
    /// Reads up to <paramref name="buffer"/>.Length bytes at <paramref name="offset"/>.
    /// Returns the number of bytes read, or 0 when the offset is at or beyond the end.
    /// </summary>
    int Read(long offset, Span<byte> buffer);

    /// <summary>
    /// Reads exactly <paramref name="destination"/>.Length bytes or throws <see cref="SourceReadException"/>.
    /// </summary>
    void ReadExactly(long offset, Span<byte> destination);
}
