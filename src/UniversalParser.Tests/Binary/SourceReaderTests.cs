using System.Text;
using UniversalParser.Core.Binary;

namespace UniversalParser.Tests.Binary;

public sealed class SourceReaderTests
{
    [Fact]
    public void ReadsIntegersWithSelectedEndianness()
    {
        using var source = new MemorySource(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });
        var reader = new SourceReader(source, 0, 8);

        Assert.Equal(0x0201, reader.ReadUInt16());
        Assert.Equal(0x06050403u, reader.ReadUInt32());
        Assert.Equal(0x0708, reader.ReadUInt16(Endian.Big));
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ReadsNullTerminatedStringWithinLimit()
    {
        using var source = new MemorySource(new byte[] { (byte)'a', (byte)'b', 0, (byte)'c' });
        var reader = new SourceReader(source, 0, 4);

        Assert.Equal("ab", reader.ReadNullTerminatedString(Encoding.ASCII, 4));
        Assert.Equal(3, reader.Position);
        Assert.Equal((byte)'c', reader.ReadByte());
    }

    [Fact]
    public void RejectsUnterminatedString()
    {
        using var source = new MemorySource(new byte[] { (byte)'a', (byte)'b', (byte)'c' });
        var reader = new SourceReader(source, 0, 3);

        Assert.Throws<SourceReadException>(() => reader.ReadNullTerminatedString(Encoding.ASCII, 3));
    }

    [Fact]
    public void ReadsBoundedSliceAndSkips()
    {
        using var source = new MemorySource(new byte[] { 0, 1, 2, 3, 4, 5 });
        var reader = new SourceReader(source, 1, 4);
        reader.Skip(1);

        var slice = reader.Slice(new SourceRange(3, 2));
        Assert.Equal(new SourceRange(3, 2), slice.CurrentRange);
        Assert.Equal(3, slice.ReadByte());
        Assert.Equal(4, slice.ReadByte());
    }

    [Fact]
    public void RejectsReadsBeyondBoundedRegion()
    {
        using var source = new MemorySource(new byte[] { 1, 2 });
        var reader = new SourceReader(source, 0, 2);

        Assert.Throws<SourceReadException>(() => reader.ReadUInt32());
        Assert.Throws<SourceReadException>(() => reader.Skip(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Slice(new SourceRange(1, 2)));
    }
}
