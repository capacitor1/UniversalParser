namespace UniversalParser.Core.Binary;

/// <summary>
/// In-memory source for tests and small fixtures. The caller retains ownership of the memory.
/// </summary>
public sealed class MemorySource : IRandomAccessSource
{
    private ReadOnlyMemory<byte> _data;
    private bool _disposed;

    public MemorySource(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    public long Length => _data.Length;

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (buffer.IsEmpty || offset >= _data.Length)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, _data.Length - offset);
        _data.Span.Slice((int)offset, count).CopyTo(buffer);
        return count;
    }

    public void ReadExactly(long offset, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (destination.IsEmpty)
        {
            return;
        }

        if (offset > _data.Length || destination.Length > _data.Length - offset)
        {
            throw new SourceReadException(offset, destination.Length, Math.Max(0, _data.Length - offset));
        }

        _data.Span.Slice((int)offset, destination.Length).CopyTo(destination);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _data = default;
    }
}
