using UniversalParser.Core.Binary;

namespace UniversalParser.Tests.Binary;

public sealed class BufferedReadSourceTests
{
    [Fact]
    public void ServesReadsAcrossWindowBoundariesAndBackward()
    {
        var data = Enumerable.Range(0, 100).Select(static value => (byte)value).ToArray();
        using var source = new MemorySource(data);
        using var buffered = new BufferedReadSource(source, windowSize: 16);

        var first = new byte[8];
        Assert.Equal(8, buffered.Read(0, first));
        Assert.Equal(data[..8], first);

        var crossing = new byte[12];
        Assert.Equal(12, buffered.Read(12, crossing));
        Assert.Equal(data[12..24], crossing);

        var far = new byte[5];
        Assert.Equal(5, buffered.Read(70, far));
        Assert.Equal(data[70..75], far);

        var backward = new byte[4];
        Assert.Equal(4, buffered.Read(3, backward));
        Assert.Equal(data[3..7], backward);
    }

    [Fact]
    public void ReadsLargerThanTheWindowBypassIt()
    {
        var data = Enumerable.Range(0, 200).Select(static value => (byte)value).ToArray();
        using var source = new MemorySource(data);
        using var buffered = new BufferedReadSource(source, windowSize: 16);

        var destination = new byte[100];
        Assert.Equal(100, buffered.Read(25, destination));
        Assert.Equal(data[25..125], destination);
    }

    [Fact]
    public void ReadExactlyCrossesWindowsAndRejectsShortSources()
    {
        var data = Enumerable.Range(0, 40).Select(static value => (byte)value).ToArray();
        using var source = new MemorySource(data);
        using var buffered = new BufferedReadSource(source, windowSize: 8);

        var destination = new byte[20];
        buffered.ReadExactly(10, destination);
        Assert.Equal(data[10..30], destination);
        Assert.Throws<SourceReadException>(() =>
        {
            Span<byte> tooLong = stackalloc byte[11];
            buffered.ReadExactly(30, tooLong);
        });
    }

    [Fact]
    public void DisposeDropsTheWindowAndRejectsFurtherReads()
    {
        using var source = new MemorySource(new byte[] { 1, 2, 3, 4 });
        var buffered = new BufferedReadSource(source, windowSize: 2);
        var value = new byte[1];
        Assert.Equal(1, buffered.Read(0, value));

        buffered.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffered.Read(0, value));
        Assert.Throws<ObjectDisposedException>(() => buffered.ReadExactly(0, value));
    }

    [Fact]
    public void WindowSizeCannotExceedTheHardCap()
    {
        using var source = new MemorySource(new byte[] { 1, 2, 3, 4 });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BufferedReadSource(source, BufferedReadSource.WindowSize + 1));
    }
}
