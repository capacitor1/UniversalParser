using System.Text;
using UniversalParser.Core.Formats.Riff;
using UniversalParser.Core.Formatting;

namespace UniversalParser.Tests.Formatting;

public sealed class TextEncodingTests
{
    [Fact]
    public void RiffFallbackUsesWindows1252InsteadOfLatin1()
    {
        Assert.Equal("€‘’“”–—", RiffFormatting.DecodeText([0x80, 0x91, 0x92, 0x93, 0x94, 0x96, 0x97]));
        Assert.Equal("café", RiffFormatting.DecodeText([0x63, 0x61, 0x66, 0xE9]));
        Assert.Equal("€\\0", RiffFormatting.DecodeText([0x80, 0, 0x91]));
    }

    [Fact]
    public void RiffStillPrefersUtf8AndPreservesFourCcBytes()
    {
        Assert.Equal("中文😀", RiffFormatting.DecodeText("中文😀"u8));
        Assert.Equal("plain\\0", RiffFormatting.DecodeText("plain\0ignored"u8));
        Assert.Equal(string.Empty, RiffFormatting.DecodeText([]));
        Assert.Equal("\u0080", RiffFormatting.DecodeFourCc([0x80]));
    }

    [Fact]
    public void SharedEncodingsKeepTheirFallbackAndBomPolicies()
    {
        Assert.Empty(TextEncodings.Utf8.GetPreamble());
        Assert.Empty(TextEncodings.StrictUtf8.GetPreamble());
        Assert.Equal("�", TextEncodings.Utf8.GetString([0xFF]));
        Assert.Throws<DecoderFallbackException>(() => TextEncodings.StrictUtf8.GetString(new byte[] { 0xFF }));
        Assert.Throws<DecoderFallbackException>(() => TextEncodings.StrictUtf16Le.GetString(new byte[] { 0x00 }));
        Assert.Throws<DecoderFallbackException>(() => TextEncodings.StrictUtf16Be.GetString(new byte[] { 0x00 }));
        Assert.Equal(1252, TextEncodings.Windows1252.CodePage);
    }
}
