using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Tests.Parsing;

public sealed class ParserRegistryTests
{
    [Fact]
    public void DetectPrefersStrongMatchOverPriority()
    {
        var weak = new TestFormatParser("weak", ProbeResult.Weak(), priority: 100);
        var strong = new TestFormatParser("strong", ProbeResult.Strong(), priority: 1);
        var fallback = new TestFormatParser("fallback", ProbeResult.NoMatch, fallback: true);
        var registry = new ParserRegistry(new IFormatParser[] { weak, strong, fallback });
        using var source = new MemorySource(new byte[] { 1 });

        var result = registry.Detect(source, ParseLimits.Default);

        Assert.Equal("strong", result.Parser.Id);
        Assert.Equal(ProbeConfidence.Strong, result.Probe.Confidence);
    }

    [Fact]
    public void DetectUsesWeakMatchBeforeFallback()
    {
        var weak = new TestFormatParser("weak", ProbeResult.Weak("header"));
        var fallback = new TestFormatParser("fallback", ProbeResult.NoMatch, fallback: true);
        var registry = new ParserRegistry(new IFormatParser[] { weak, fallback });
        using var source = new MemorySource(new byte[] { 1 });

        var result = registry.Detect(source, ParseLimits.Default);

        Assert.Equal("weak", result.Parser.Id);
        Assert.Equal("header", result.Probe.Reason);
    }

    [Fact]
    public void DetectFallsBackWhenNoParserMatches()
    {
        var parser = new TestFormatParser("known", ProbeResult.NoMatch);
        var fallback = new TestFormatParser("fallback", ProbeResult.NoMatch, fallback: true);
        var registry = new ParserRegistry(new IFormatParser[] { parser, fallback });
        using var source = new MemorySource(new byte[] { 1 });

        var result = registry.Detect(source, ParseLimits.Default);

        Assert.Equal("fallback", result.Parser.Id);
        Assert.False(result.Probe.IsMatch);
    }

    [Fact]
    public void DetectCapturesProbeFailures()
    {
        var registry = new ParserRegistry(new IFormatParser[] { new ThrowingProbeParser(), new TestFormatParser("fallback", ProbeResult.NoMatch, fallback: true) });
        using var source = new MemorySource(new byte[] { 1 });

        var result = registry.Detect(source, ParseLimits.Default);

        Assert.Equal("fallback", result.Parser.Id);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.ProbeFailed);
    }

    [Fact]
    public void ConstructorRejectsDuplicateIdsAndMissingFallback()
    {
        var first = new TestFormatParser("same", ProbeResult.NoMatch, fallback: true);
        var second = new TestFormatParser("same", ProbeResult.NoMatch);

        Assert.Throws<ArgumentException>(() => new ParserRegistry(new IFormatParser[] { first, second }));
        Assert.Throws<ArgumentException>(() => new ParserRegistry(new IFormatParser[] { second }));
    }

    private sealed class ThrowingProbeParser : IFormatParser
    {
        public string Id => "throwing";
        public string DisplayName => "Throwing";
        public int ProbePriority => 0;
        public bool IsFallback => false;

        public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
        {
            _ = source;
            _ = limits;
            _ = cancellationToken;
            throw new InvalidOperationException("probe boom");
        }

        public FormatNode Parse(ParseContext context)
        {
            _ = context;
            throw new NotSupportedException();
        }
    }
}
