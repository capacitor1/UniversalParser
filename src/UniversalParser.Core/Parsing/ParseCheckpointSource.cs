using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Parsing;

/// <summary>
/// Non-owning source view that checks cancellation before every I/O call, including reads made
/// by embedded parsers that do not otherwise receive ParseContext.
/// </summary>
internal sealed class ParseCheckpointSource : IRandomAccessSource
{
    private readonly IRandomAccessSource _source;
    private readonly Action _checkpoint;

    public ParseCheckpointSource(IRandomAccessSource source, Action checkpoint)
    {
        _source = source;
        _checkpoint = checkpoint;
    }

    public long Length => _source.Length;

    public int Read(long offset, Span<byte> buffer)
    {
        _checkpoint();
        return _source.Read(offset, buffer);
    }

    public void ReadExactly(long offset, Span<byte> destination)
    {
        var count = SourceRead.ReadAtMost(this, offset, destination);
        if (count != destination.Length) throw new SourceReadException(offset, destination.Length, count);
    }

    public void Dispose() { } // ParseSession, not the parser, owns the underlying file handle.
}
