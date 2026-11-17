using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Tests.Model;

public sealed class FieldTests
{
    [Fact]
    public void RawKeysRejectAngleBrackets()
    {
        Assert.Throws<ArgumentException>(() => Field.Raw("<FileSize>", "42"));
        Assert.Equal("<FileSize>", Field.Derived("<FileSize>", "42").Key);
        Assert.Equal("<Note>", Field.Note("test").Key);
    }

    [Fact]
    public void NodeResultRejectsRawRangeOutsideNode()
    {
        var range = new SourceRange(10, 5);
        Assert.Throws<ArgumentException>(() => new NodeParseResult("Test", range, new SourceRange(9, 1)));
    }

    [Fact]
    public void ParseLimitsRejectNonPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParseLimits { MaxStringBytes = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParseLimits { MaxDataLines = 0 }.Validate());
    }
}
