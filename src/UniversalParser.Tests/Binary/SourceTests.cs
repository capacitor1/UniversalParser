using UniversalParser.Core.Binary;

namespace UniversalParser.Tests.Binary;

public sealed class SourceTests
{
    [Fact]
    public void MemorySourceReadsAndReportsShortReads()
    {
        using var source = new MemorySource(new byte[] { 1, 2, 3 });
        var buffer = new byte[4];

        Assert.Equal(3, source.Read(0, buffer));
        Assert.Equal(1, buffer[0]);
        Assert.Equal(3, buffer[2]);
        Assert.Equal(0, source.Read(3, buffer));
        Assert.Throws<SourceReadException>(() => source.ReadExactly(2, buffer));
    }

    [Fact]
    public void MemorySourceRejectsReadsAfterDispose()
    {
        var source = new MemorySource(new byte[] { 1 });
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.Read(0, new byte[1]));
    }

    [Fact]
    public void FileSourceReadsFromDiskAndRejectsShortReads()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(path, [0x10, 0x20, 0x30, 0x40]);

        try
        {
            using var source = FileSource.Open(path);
            Assert.Equal(4, source.Length);

            var buffer = new byte[4];
            source.ReadExactly(0, buffer);
            Assert.Equal(new byte[] { 0x10, 0x20, 0x30, 0x40 }, buffer);
            Assert.Throws<SourceReadException>(() => source.ReadExactly(3, buffer));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FileSourceRejectsReadsAfterDispose()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(path, [1]);

        try
        {
            var source = FileSource.Open(path);
            source.Dispose();

            Assert.Throws<ObjectDisposedException>(() => source.Read(0, new byte[1]));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
