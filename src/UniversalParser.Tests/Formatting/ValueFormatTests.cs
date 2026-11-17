using UniversalParser.Core.Formatting;

namespace UniversalParser.Tests.Formatting;

public sealed class ValueFormatTests
{
    [Fact]
    public void DefaultPrecisionIsThreeDecimals()
    {
        var original = ValueFormat.PercentageDecimals;
        try
        {
            ValueFormat.PercentageDecimals = ValueFormat.DefaultPercentageDecimals;
            Assert.Equal("12.346 %", ValueFormat.Percentage(0.123456m));
            Assert.Equal("100.000 %", ValueFormat.Percentage(1m));
        }
        finally
        {
            ValueFormat.PercentageDecimals = original;
        }
    }

    [Theory]
    [InlineData(1, "12.3 %")]
    [InlineData(2, "12.35 %")]
    [InlineData(3, "12.346 %")]
    [InlineData(6, "12.345600 %")]
    public void ConfiguredPrecisionControlsFractionDigits(int decimals, string expected)
    {
        var original = ValueFormat.PercentageDecimals;
        try
        {
            ValueFormat.PercentageDecimals = decimals;
            Assert.Equal(expected, ValueFormat.Percentage(0.123456m));
        }
        finally
        {
            ValueFormat.PercentageDecimals = original;
        }
    }

    [Fact]
    public void PrecisionIsClampedToTheSupportedRange()
    {
        var original = ValueFormat.PercentageDecimals;
        try
        {
            ValueFormat.PercentageDecimals = 0;
            Assert.Equal(ValueFormat.MinPercentageDecimals, ValueFormat.PercentageDecimals);

            ValueFormat.PercentageDecimals = 99;
            Assert.Equal(ValueFormat.MaxPercentageDecimals, ValueFormat.PercentageDecimals);
        }
        finally
        {
            ValueFormat.PercentageDecimals = original;
        }
    }

    [Fact]
    public void ShareOfZeroTotalIsZero()
    {
        Assert.Equal("0.000 %", ValueFormat.Percentage(0, 0));
    }
}
