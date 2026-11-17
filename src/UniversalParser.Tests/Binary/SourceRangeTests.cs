using UniversalParser.Core.Binary;

namespace UniversalParser.Tests.Binary;

public sealed class SourceRangeTests
{
    [Fact]
    public void ContainsAndOverlapsUseHalfOpenIntervals()
    {
        var range = new SourceRange(10, 5);

        Assert.True(range.Contains(10));
        Assert.True(range.Contains(14));
        Assert.False(range.Contains(15));
        Assert.True(range.Contains(new SourceRange(10, 5)));
        Assert.False(range.Contains(new SourceRange(9, 2)));
        Assert.True(range.Overlaps(new SourceRange(14, 2)));
        Assert.False(range.Overlaps(new SourceRange(15, 1)));
        Assert.False(SourceRange.Empty.Overlaps(range));
    }

    [Fact]
    public void SliceRejectsOutOfBoundsRequests()
    {
        var range = new SourceRange(10, 5);

        Assert.Equal(new SourceRange(12, 2), range.Slice(2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.Slice(4, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => range.Slice(-1, 1));
    }

    [Fact]
    public void FromBoundsComputesLength()
    {
        Assert.Equal(new SourceRange(7, 5), SourceRange.FromBounds(7, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceRange.FromBounds(12, 7));
    }
}
