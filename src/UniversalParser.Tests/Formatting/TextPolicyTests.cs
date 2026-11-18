using System.Text;
using UniversalParser.Core.Formatting;

namespace UniversalParser.Tests.Formatting;

public sealed class TextPolicyTests
{
    [Fact]
    public void NullTerminatorIsPreservedButFollowingBytesAreNot()
    {
        Assert.Equal("ThisIsText\\0", TextDecoding.DisplayNullTerminated("ThisIsText\0OtherData"u8, Encoding.Latin1));
        Assert.Equal("ThisIsText", TextDecoding.Display("ThisIsText"u8, Encoding.Latin1));
        Assert.Equal("ThisIsText", TextDecoding.DisplayNullTerminated("ThisIsText"u8, Encoding.Latin1));
        Assert.Equal("\\0", TextDecoding.DisplayNullTerminated([0, (byte)'x'], Encoding.Latin1));
    }

    [Fact]
    public void Utf16NullTerminatorUsesItsDeclaredWidth()
    {
        var bytes = new byte[] { (byte)'H', 0, (byte)'i', 0, 0, 0, (byte)'x', 0 };
        Assert.Equal("Hi\\0", TextDecoding.DisplayNullTerminated(bytes, Encoding.Unicode, terminatorSize: 2));
    }

    [Fact]
    public void MultiLineTextKeepsEscapedLineBreaks()
    {
        Assert.Equal("Multi\\nLine\\r\\n", TextDecoding.Display("Multi\nLine\r\n"u8, Encoding.UTF8));
        Assert.Equal("Multi\\nLine", TextDecoding.DisplayPredicted("Multi\nLine"u8));
    }

    [Fact]
    public void DeclaredEncodingIsUsedInsteadOfLocaleGuessing()
    {
        var utf16 = Encoding.Unicode.GetBytes("中文");
        Assert.Equal("中文", TextDecoding.Display(utf16, Encoding.Unicode));
        Assert.Equal("中文", TextDecoding.Display("中文"u8, Encoding.UTF8));
    }

    [Fact]
    public void InvalidDeclaredEncodingIsShownAsHex()
    {
        Assert.Equal("\\xFF\\xFE", TextDecoding.Display([0xFF, 0xFE], Encoding.UTF8));
        Assert.Equal("\\x00\\xD8", TextDecoding.Display([0x00, 0xD8], Encoding.Unicode));
        Assert.Equal("\\x80", TextDecoding.Display([0x80], Encoding.ASCII));
    }

    [Fact]
    public void EncodingPredictionPrefersUtf8ThenWindows1252ThenHex()
    {
        Assert.Equal("中文", TextDecoding.DisplayPredicted("中文"u8));
        Assert.Equal("€", TextDecoding.DisplayPredicted([0x80]));
        Assert.Equal("€\\0", TextDecoding.DisplayPredictedNullTerminated([0x80, 0, 0x81]));
        Assert.Equal("\\x81", TextDecoding.DisplayPredicted([0x81]));
        Assert.Equal("\\x00\\x01", TextDecoding.DisplayPredicted([0x00, 0x01]));
    }

    [Fact]
    public void EscapedControlCharactersRemainReadable()
    {
        Assert.Equal("A\\0B\\nC\\tD\\x01", TextEscaping.Escape("A\0B\nC\tD\u0001"));
    }
}
