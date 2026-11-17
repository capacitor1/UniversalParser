using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Raw;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;
using UniversalParser.Core.Validation;

namespace UniversalParser.Tests.Parsing;

public sealed class ParserEngineTests
{
    [Fact]
    public void ParseConvertsParserExceptionToDiagnostic()
    {
        var parser = new TestFormatParser(
            "boom",
            ProbeResult.Strong(),
            parse: _ => throw new InvalidOperationException("parse boom"));
        var engine = new ParserEngine(new ParserRegistry(new IFormatParser[] { parser, new RawParser() }));
        using var source = new MemorySource(new byte[] { 1, 2, 3 });

        var document = engine.Parse(source, "boom.bin");

        var diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(DiagnosticCodes.ParseFailed, diagnostic.Code);
        Assert.Equal(NodeKind.Synthetic, document.Root.Kind);
        Assert.Equal(new SourceRange(0, 3), document.Root.Range);
        Assert.Empty(NodeTreeValidator.Validate(document.Root, document.SourceLength, requirePayloadCoverage: true));
    }

    [Fact]
    public void ParseNodeConvertsExceptionToErrorField()
    {
        var parser = new TestFormatParser(
            "node",
            ProbeResult.Strong(),
            parseNode: (_, _) => throw new InvalidOperationException("node boom"));
        var engine = new ParserEngine(new ParserRegistry(new IFormatParser[] { parser, new RawParser() }));
        using var source = new MemorySource(new byte[] { 1, 2, 3 });
        var document = engine.Parse(source);

        var result = engine.ParseNode(source, document, document.Root);

        Assert.Equal(DiagnosticCodes.ParseFailed, Assert.Single(result.Diagnostics).Code);
        Assert.Equal(FieldKind.Error, Assert.Single(result.Fields).Kind);
        Assert.Equal(document.Root.Range, result.RawRange);
    }

    [Fact]
    public void ParseNodeReturnsParserFields()
    {
        var parser = new TestFormatParser(
            "fields",
            ProbeResult.Strong(),
            parseNode: (_, node) => new NodeParseResult(
                "Fields",
                node.Range,
                node.Range,
                [Field.Raw("CodecID", "H264")]));
        var engine = new ParserEngine(new ParserRegistry(new IFormatParser[] { parser, new RawParser() }));
        using var source = new MemorySource(new byte[] { 1 });
        var document = engine.Parse(source);

        var result = engine.ParseNode(source, document, document.Root);

        Assert.Equal("H264", Assert.Single(result.Fields).Value);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ParseReportsDetectionParsingAndCompletionProgress()
    {
        var parser = new TestFormatParser(
            "progress",
            ProbeResult.Strong(),
            parse: context =>
            {
                var range = new SourceRange(0, context.Source.Length);
                context.ReportProgress("progress", 0, context.Source.Length);
                context.ReportProgress("progress", context.Source.Length, context.Source.Length);
                return new FormatNode("Progress", NodeKind.Container, range, range);
            });
        var engine = new ParserEngine(new ParserRegistry(new IFormatParser[] { parser, new RawParser() }));
        using var source = new MemorySource(new byte[] { 1, 2, 3 });
        var progress = new RecordingProgress();

        _ = engine.Parse(source, "progress.bin", progress: progress);

        Assert.Contains(progress.Items, static item => item.Stage == "Detecting format");
        Assert.Contains(progress.Items, static item => item.Stage == "Parsing");
        Assert.Contains(progress.Items, static item => item.Stage == "progress" && item.Processed == 3);
        Assert.Equal(1d, progress.Items[^1].Fraction);
    }

    private sealed class RecordingProgress : IProgress<ParseProgress>
    {
        public List<ParseProgress> Items { get; } = [];

        public void Report(ParseProgress value) => Items.Add(value);
    }
}
