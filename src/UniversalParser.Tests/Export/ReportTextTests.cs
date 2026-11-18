using UniversalParser.Core.Export;

namespace UniversalParser.Tests.Export;

public sealed class ReportTextTests
{
    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("COM1", "_COM1")]
    [InlineData("COM9.log", "_COM9.log")]
    [InlineData("com¹", "_com¹")]
    [InlineData("COM².txt", "_COM².txt")]
    [InlineData("CoM³.bin", "_CoM³.bin")]
    [InlineData("LPT1", "_LPT1")]
    [InlineData("LPT9.txt", "_LPT9.txt")]
    [InlineData("LPT¹.txt", "_LPT¹.txt")]
    [InlineData("lpt²", "_lpt²")]
    [InlineData("LpT³.bin", "_LpT³.bin")]
    [InlineData("COM0", "COM0")]
    [InlineData("LPT0.txt", "LPT0.txt")]
    [InlineData("COM10", "COM10")]
    [InlineData("COM⁴", "COM⁴")]
    [InlineData("LPT¹data.txt", "LPT¹data.txt")]
    public void DeviceNamesAreSanitizedOnEveryPlatform(string input, string expected)
    {
        Assert.Equal(expected, ReportText.SanitizeFileName(input));
    }

    [Fact]
    public void InvalidCharactersAndTrailingDotsRemainSanitized()
    {
        Assert.Equal("a_________b_", ReportText.SanitizeFileName(" a<>:\"/\\|?*b\0. "));
        Assert.Equal("node", ReportText.SanitizeFileName("..."));
        Assert.Equal("fallback", ReportText.SanitizeFileName(null, "fallback"));
    }

    [Fact]
    public void LengthLimitDoesNotSplitASurrogatePair()
    {
        var prefix = new string('a', 95);
        Assert.Equal(prefix, ReportText.SanitizeFileName(prefix + "😀tail"));
    }
}
