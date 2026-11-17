using UniversalParser.Core.Model;

namespace UniversalParser.Tests.Model;

public sealed class ParseLimitsConfigurationTests
{
    [Fact]
    public void MissingFileReturnsBuiltInDefaults()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ParseLimitsConfiguration.DefaultFileName);

        var limits = ParseLimitsConfiguration.Load(missing);

        Assert.Equal(ParseLimits.Default, limits);
    }

    [Fact]
    public void PartialJsonOverridesOnlySpecifiedValues()
    {
        var limits = ParseLimitsConfiguration.Deserialize(
            """
            {
              // larger field buffers for a local forensic run
              "maxStringBytes": 8192,
              "maxDataLines": 128
            }
            """);

        Assert.Equal(8192, limits.MaxStringBytes);
        Assert.Equal(128, limits.MaxDataLines);
        Assert.Equal(ParseLimits.Default.MaxBlobBytes, limits.MaxBlobBytes);
    }

    [Fact]
    public void RemovedExportBudgetPropertyIsIgnored()
    {
        var limits = ParseLimitsConfiguration.Deserialize("""{"maxExportBytes": 1024}""");

        Assert.Equal(ParseLimits.Default, limits);
    }

    [Fact]
    public void RemovedProbeBytesPropertyIsIgnored()
    {
        var limits = ParseLimitsConfiguration.Deserialize("""{"maxProbeBytes": 65536}""");

        Assert.Equal(ParseLimits.Default, limits);
    }

    [Fact]
    public void InvalidValueIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ParseLimitsConfiguration.Deserialize("""{"maxStringBytes": 0}"""));
    }

    [Fact]
    public void TryLoadReportsMalformedFileWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"universalparser-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"maxStringBytes": 0}""");

            var loaded = ParseLimitsConfiguration.TryLoad(path, out var limits, out var error);

            Assert.False(loaded);
            Assert.NotNull(error);
            Assert.Equal(ParseLimits.Default, limits);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
