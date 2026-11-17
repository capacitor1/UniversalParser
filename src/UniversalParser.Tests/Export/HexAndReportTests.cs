using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Export;
using UniversalParser.Core.Formats.Raw;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Tests.Export;

public sealed class HexAndReportTests
{
    [Fact]
    public void LineCountStays64Bit()
    {
        Assert.Equal(0, HexDumpFormatter.GetLineCount(0));
        Assert.Equal(1, HexDumpFormatter.GetLineCount(1));
        Assert.Equal(1, HexDumpFormatter.GetLineCount(16));
        Assert.Equal(2, HexDumpFormatter.GetLineCount(17));

        var huge = (long)int.MaxValue * 40;
        Assert.True(HexDumpFormatter.GetLineCount(huge) > int.MaxValue);
        Assert.Equal((long.MaxValue / 16) + 1, HexDumpFormatter.GetLineCount(long.MaxValue));
    }

    [Theory]
    [InlineData(false, 10, 62)]
    [InlineData(true, 18, 70)]
    public void HeaderAndByteColumnsMatchTheRenderedLine(bool wide, int hexColumn, int asciiColumn)
    {
        using var source = new MemorySource("A&0123456789BCDEF"u8.ToArray());
        var line = HexDumpFormatter.RenderLine(source, wide, 0, source.Length, 0);
        var header = HexDumpFormatter.FormatHeader(wide);

        Assert.Equal(hexColumn, HexDumpFormatter.GetHexColumn(wide));
        Assert.Equal(asciiColumn, HexDumpFormatter.GetAsciiColumn(wide));
        Assert.Equal("+0", header.Substring(hexColumn, 2));
        Assert.Equal("+F", header.Substring(hexColumn + 45, 2));
        Assert.Equal("41", line.Substring(hexColumn, 2));
        Assert.Equal("45", line.Substring(hexColumn + 45, 2));
        Assert.Equal("A&0123456789BCDE", line[asciiColumn..]);
        Assert.Equal("0123456789ABCDEF", header[asciiColumn..]);
        Assert.Equal(line.Length, header.Length);
    }

    [Fact]
    public void RendersOneLineWithEightDigitOffset()
    {
        var bytes = new byte[17];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)index;
        }

        using var source = new MemorySource(bytes);
        var line = HexDumpFormatter.RenderLine(source, wideOffset: false, baseOffset: 0, length: bytes.Length, lineIndex: 0);

        Assert.StartsWith("00000000  00 01 02 03", line, StringComparison.Ordinal);
        Assert.Contains("09 0A 0B 0C 0D 0E 0F", line, StringComparison.Ordinal);
        Assert.EndsWith("................", line, StringComparison.Ordinal);
    }

    [Fact]
    public void LastLineNeverReadsPastTheRangeEnd()
    {
        // Node range [4,24): line 1 may only contain the four bytes at 20..23.
        var bytes = new byte[64];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)index;
        }

        using var source = new MemorySource(bytes);

        var line = HexDumpFormatter.RenderLine(source, wideOffset: false, baseOffset: 4, length: 20, lineIndex: 1);

        Assert.StartsWith("00000014  14 15 16 17", line, StringComparison.Ordinal);
        Assert.DoesNotContain("18 19", line, StringComparison.Ordinal);
        Assert.DoesNotContain("1A", line, StringComparison.Ordinal);
        Assert.EndsWith("....", line, StringComparison.Ordinal);
        Assert.Equal(
            string.Empty,
            HexDumpFormatter.RenderLine(source, wideOffset: false, baseOffset: 4, length: 20, lineIndex: 2));
    }

    [Fact]
    public void UsesSixteenDigitOffsetForLargeSources()
    {
        using var source = new MemorySource(new byte[] { 0x41, 0x42 });
        var line = HexDumpFormatter.RenderLine(source, wideOffset: true, baseOffset: 0, length: 2, lineIndex: 0);

        Assert.StartsWith("0000000000000000  41 42", line, StringComparison.Ordinal);
        Assert.EndsWith("AB", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnsEmptyPastEnd()
    {
        using var source = new MemorySource(new byte[] { 1, 2, 3 });
        Assert.Equal(
            string.Empty,
            HexDumpFormatter.RenderLine(source, wideOffset: false, baseOffset: 0, length: 3, lineIndex: 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionColumnsCoverExactlyTheRenderedBytes(bool wide)
    {
        var bytes = new byte[16];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)index;
        }

        using var source = new MemorySource(bytes);
        var line = HexDumpFormatter.RenderLine(source, wide, 0, bytes.Length, 0);

        var all = HexDumpFormatter.GetColumnSpan(wide, 0, 16);
        Assert.Equal("00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F", line[all.HexStart..all.HexEnd]);
        Assert.Equal("................", line[all.AsciiStart..all.AsciiEnd]);

        var middle = HexDumpFormatter.GetColumnSpan(wide, 3, 4);
        Assert.Equal("03 04 05 06", line[middle.HexStart..middle.HexEnd]);
        Assert.Equal("....", line[middle.AsciiStart..middle.AsciiEnd]);
    }

    [Fact]
    public void SelectionColumnsNeverExtendPastAShortLastLine()
    {
        using var source = new MemorySource("A&01"u8.ToArray());
        var line = HexDumpFormatter.RenderLine(source, wideOffset: false, baseOffset: 0, length: 4, lineIndex: 0);
        var span = HexDumpFormatter.GetColumnSpan(false, 0, 4);

        Assert.Equal("41 26 30 31", line[span.HexStart..span.HexEnd]);
        Assert.Equal("A&01", line[span.AsciiStart..span.AsciiEnd]);
        Assert.Throws<ArgumentOutOfRangeException>(() => HexDumpFormatter.GetColumnSpan(false, 12, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => HexDumpFormatter.GetColumnSpan(false, 0, 0));
    }

    [Fact]
    public void EscapesControlCharactersAndSanitizesFileNames()
    {
        Assert.Equal("a\\nb\\tc", ReportText.Escape("a\nb\tc"));
        Assert.Equal("x\\x01y", ReportText.Escape("x\u0001y"));
        Assert.Equal("a_b_c", ReportText.SanitizeFileName("a/b:c"));
        Assert.Equal("node", ReportText.SanitizeFileName("   "));
        Assert.Equal("node", ReportText.SanitizeFileName(null));
    }

    [Fact]
    public void ReportContainsSummaryFieldsDiagnosticsHexAndLimits()
    {
        var bytes = new byte[32];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)index;
        }

        using var source = new MemorySource(bytes);
        var (document, node, result) = BuildSample(source, bytes.Length);
        var request = new ForensicReportRequest
        {
            Source = source,
            Document = document,
            Node = node,
            Result = result,
            NodePath = "root / child",
        };

        var writer = new StringWriter();
        ForensicReportWriter.Write(writer, request);
        var report = writer.ToString();

        Assert.Contains("UniversalParser Forensic Report", report, StringComparison.Ordinal);
        Assert.Contains("File: sample.bin", report, StringComparison.Ordinal);
        Assert.Contains("Detected format: Raw Data (raw)", report, StringComparison.Ordinal);
        Assert.Contains("Node: root / child", report, StringComparison.Ordinal);
        Assert.Contains("Node title: Test Node", report, StringComparison.Ordinal);
        Assert.Contains("[Warning] TEST_WARNING: something odd", report, StringComparison.Ordinal);
        Assert.Contains("major_brand = isom", report, StringComparison.Ordinal);
        Assert.Contains("<derived> = explained    (Derived)", report, StringComparison.Ordinal);
        Assert.Contains("6. Read Limits", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportHonoursCancellation()
    {
        using var source = new MemorySource(new byte[16]);
        var (document, node, result) = BuildSample(source, 16);
        var request = new ForensicReportRequest
        {
            Source = source,
            Document = document,
            Node = node,
            Result = result,
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => ForensicReportWriter.Write(new StringWriter(), request, null, cts.Token));
    }

    private static (ParseDocument Document, FormatNode Node, NodeParseResult Result) BuildSample(
        IRandomAccessSource source,
        long length)
    {
        var node = new FormatNode("child", NodeKind.Leaf, new SourceRange(0, length));
        var root = new FormatNode(
            "root",
            NodeKind.Container,
            new SourceRange(0, source.Length),
            new SourceRange(0, source.Length),
            ImmutableArray.Create(node));

        var fields = ImmutableArray.Create(
            Field.Raw("major_brand", "isom"),
            Field.Derived("<derived>", "explained"),
            Field.Note("a note"),
            Field.Error("bad field"));
        var diagnostics = ImmutableArray.Create(
            new Diagnostic(DiagnosticSeverity.Warning, "TEST_WARNING", "something odd", node.Range));
        var result = new NodeParseResult("Test Node", node.Range, node.Range, fields, diagnostics);

        var parser = new RawParser();
        var document = new ParseDocument(
            parser,
            root,
            ImmutableArray<Diagnostic>.Empty,
            ParseLimits.Default,
            "sample.bin",
            source.Length,
            ProbeResult.NoMatch);

        return (document, node, result);
    }
}
