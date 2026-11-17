namespace UniversalParser.Core.Binary;

/// <summary>
/// Forward-biased read-ahead window over a random-access source. Tree parsers scan mostly
/// sequentially, so one large read per window removes millions of tiny file reads.
/// </summary>
/// <remarks>
/// The window is a fixed 16 MiB. It is deliberately not configurable: the product allows a
/// bounded memory spike in exchange for throughput, with a hard ceiling far below 16 GiB.
/// Disposing the wrapper drops the window so it can be collected as soon as parsing ends.
/// </remarks>
internal sealed class BufferedReadSource : IRandomAccessSource
{
    public const int WindowSize = 16 * 1024 * 1024;

    /// <summary>Small files parse quickly already; skipping the window avoids a 16 MiB spike.</summary>
    public const long MinimumLength = 1 << 20;

    private readonly IRandomAccessSource _source;
    private readonly long _length;
    private byte[]? _buffer;
    private long _bufferOffset;
    private int _bufferLength;
    private bool _disposed;

    public BufferedReadSource(IRandomAccessSource source)
        : this(source, WindowSize)
    {
    }

    internal BufferedReadSource(IRandomAccessSource source, int windowSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(windowSize, WindowSize);
        _source = source;
        _length = source.Length;
        var size = (int)Math.Min(windowSize, Math.Max(1, _length));
        _buffer = GC.AllocateUninitializedArray<byte>(size);
    }

    public long Length => _length;

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var window = _buffer;
        if (window is null || buffer.IsEmpty || offset >= _length)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, _length - offset);
        if (TryCopyFromWindow(window, offset, buffer[..count]))
        {
            return count;
        }

        // Very large requests bypass the window instead of growing it past the hard cap.
        if (count > window.Length)
        {
            return _source.Read(offset, buffer[..count]);
        }

        var fill = (int)Math.Min(window.Length, _length - offset);
        _bufferLength = SourceRead.ReadAtMost(_source, offset, window.AsSpan(0, fill));
        _bufferOffset = offset;
        if (_bufferLength == 0)
        {
            return 0;
        }

        var copied = Math.Min(count, _bufferLength);
        window.AsSpan(0, copied).CopyTo(buffer);
        return copied;
    }

    public void ReadExactly(long offset, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (destination.IsEmpty)
        {
            return;
        }

        var total = 0;
        while (total < destination.Length)
        {
            var read = Read(offset + total, destination[total..]);
            if (read <= 0)
            {
                var current = offset + total;
                throw new SourceReadException(
                    current,
                    destination.Length - total,
                    Math.Max(0, _length - current));
            }

            total += read;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _buffer = null;
        _bufferLength = 0;
    }

    private bool TryCopyFromWindow(ReadOnlySpan<byte> window, long offset, Span<byte> destination)
    {
        if (offset < _bufferOffset)
        {
            return false;
        }

        var relative = offset - _bufferOffset;
        if (relative > _bufferLength - destination.Length)
        {
            return false;
        }

        window.Slice((int)relative, destination.Length).CopyTo(destination);
        return true;
    }
}
