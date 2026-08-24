using System;
using System.IO;

public sealed class OffsetStream : Stream
{
    private readonly Stream _baseStream;
    private readonly long _start;
    private readonly long _length;

    public OffsetStream(Stream baseStream, long start, long length)
    {
        _baseStream = baseStream;
        _start = start;
        _length = length;

        _baseStream.Position = start;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _baseStream.Position - _start;
        set => _baseStream.Position = _start + value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long remaining = _length - Position;

        if (remaining <= 0)
            return 0;

        if (count > remaining)
            count = (int)remaining;

        return _baseStream.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPos = origin switch
        {
            SeekOrigin.Begin => _start + offset,
            SeekOrigin.Current => _baseStream.Position + offset,
            SeekOrigin.End => _start + _length + offset,
            _ => throw new ArgumentOutOfRangeException()
        };

        _baseStream.Position = newPos;
        return Position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();
}