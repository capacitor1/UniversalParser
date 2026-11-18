using UniversalParser.Core.Export;
using UniversalParser.Core.Formatting;

namespace UniversalParser.Tests.Formatting;

public sealed class TextEscapingTests
{
    [Fact]
    public void PlainMetadataKeepsTheOriginalString()
    {
        const string text = "中文😀 © metadata \\\"";
        Assert.Same(text, TextEscaping.Escape(text));
        Assert.Same(text, ReportText.Escape(text));
    }

    [Fact]
    public void MetadataAndReportControlPoliciesArePreserved()
    {
        const string text = "中\0\r\n\t\u0001\u007F\u0085\"";
        Assert.Equal("中\\0\\r\\n\\t\\x01\\x7F\u0085\"", TextEscaping.Escape(text));
        Assert.Equal("中\\x00\\r\\n\\t\\x01\\x7F\u0085\"", TextEscaping.Escape(text, escapeNull: false));
        Assert.Equal("中\\x00\\r\\n\\t\\x01\\x7F\\x85\"", ReportText.Escape(text));
        Assert.Equal(
            "中\\x00\\r\\n\\t\\x01\\x7F\\x85\\\"",
            TextEscaping.Escape(text, escapeNull: false, escapeAllControls: true, escapeQuote: true));
    }

    [Theory]
    [InlineData("aaaaB", "aaaa…")]
    [InlineData("abc\nB", "abc\\n…")]
    [InlineData("abc\n", "abc\\n")]
    [InlineData("abcd", "abcd")]
    [InlineData("", "")]
    public void BoundedPreviewTruncatesBetweenInputCharacters(string input, string expected)
    {
        Assert.Equal(expected, TextEscaping.Escape(input, maxOutputLength: 4));
    }
}
