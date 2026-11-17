using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Tests.Parsing;

public sealed class ParseContextTests
{
    [Fact]
    public void PreCancelledTokenStopsSourceReads()
    {
        using var source = new MemorySource(new byte[] { 1, 2, 3 });
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var context = new ParseContext(source, ParseLimits.Default, cancellationToken: cancel.Token);

        Assert.Throws<OperationCanceledException>(() => context.Reader(0, 1).ReadByte());
    }

    [Fact]
    public void ReaderIsBoundedByRequestedRange()
    {
        using var source = new MemorySource(new byte[] { 1, 2, 3, 4 });
        var context = new ParseContext(source, ParseLimits.Default);

        var reader = context.Reader(new SourceRange(1, 2));

        Assert.Equal(2, reader.Remaining);
        Assert.Equal(2, reader.ReadByte());
        Assert.Equal(3, reader.ReadByte());
        Assert.Throws<SourceReadException>(() => reader.ReadByte());
    }
}
