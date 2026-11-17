using Microsoft.Win32.SafeHandles;

namespace UniversalParser.Core.Binary;

/// <summary>
/// Random-access file source backed by a handle. Reads do not share a mutable file position.
/// </summary>
public sealed class FileSource : IRandomAccessSource
{
    private readonly SafeFileHandle _handle;
    private readonly long _length;
    private bool _disposed;

    private FileSource(SafeFileHandle handle, long length, string filePath)
    {
        _handle = handle;
        _length = length;
        FilePath = filePath;
        CreationTimeUtc = File.GetCreationTimeUtc(handle);
        Attributes = File.GetAttributes(handle);
    }

    public long Length => _length;
    public string FilePath { get; }
    public DateTime CreationTimeUtc { get; }
    public FileAttributes Attributes { get; }

    public static FileSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var handle = File.OpenHandle(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        try
        {
            return new FileSource(handle, RandomAccess.GetLength(handle), fullPath);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (buffer.IsEmpty || offset >= _length)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, _length - offset);
        return RandomAccess.Read(_handle, buffer[..count], offset);
    }

    public void ReadExactly(long offset, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (destination.IsEmpty)
        {
            return;
        }

        if (offset > _length || destination.Length > _length - offset)
        {
            throw new SourceReadException(offset, destination.Length, Math.Max(0, _length - offset));
        }

        var remaining = destination;
        var current = offset;
        while (!remaining.IsEmpty)
        {
            var read = RandomAccess.Read(_handle, remaining, current);
            if (read == 0)
            {
                throw new SourceReadException(current, remaining.Length, 0);
            }

            remaining = remaining[read..];
            current += read;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }
}
