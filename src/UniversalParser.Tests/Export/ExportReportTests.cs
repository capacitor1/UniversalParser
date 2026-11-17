using System.Collections.Immutable;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Export;
using UniversalParser.Core.Formats.Raw;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Tests.Export;

public sealed class ExportReportTests
{
    [Fact]
    public void EveryParsedFieldIsWrittenWithoutATruncationLimit()
    {
        using var source = new MemorySource(new byte[] { 1 });
        const int fieldCount = 25_000;
        var fields = ImmutableArray.CreateBuilder<Field>(fieldCount);
        for (var index = 0; index < fieldCount; index++)
        {
            fields.Add(Field.Raw($"key{index}", $"value{index}"));
        }

        var request = Request(source, new NodeParseResult("Big field list", new SourceRange(0, 1), new SourceRange(0, 1), fields.ToImmutable()));
        using var writer = new StringWriter();
        var result = ForensicReportWriter.Write(writer, request);
        var report = writer.ToString();

        Assert.False(result.HexTruncated);
        Assert.Contains("key0 = value0", report, StringComparison.Ordinal);
        Assert.Contains("key24999 = value24999", report, StringComparison.Ordinal);
        Assert.DoesNotContain("omitted", report, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetByteCount(report), result.BytesWritten);
    }

    [Fact]
    public void HexAtExactly256KiBIsWrittenCompletely()
    {
        using var source = new MemorySource(Pattern((int)ForensicReportWriter.MaxHexBytes));
        var node = new FormatNode("child", NodeKind.Leaf, new SourceRange(0, source.Length));
        var request = Request(source, new NodeParseResult("Hex", node.Range, node.Range), node);
        using var writer = new StringWriter();
        var result = ForensicReportWriter.Write(writer, request);
        var report = writer.ToString();

        Assert.False(result.HexTruncated);
        Assert.DoesNotContain("[Hex truncated]", report, StringComparison.Ordinal);
        Assert.Contains(HexDumpFormatter.RenderLine(source, false, 0, source.Length, 0), report, StringComparison.Ordinal);
        var lastLine = HexDumpFormatter.GetLineCount(source.Length) - 1;
        Assert.Contains(
            HexDumpFormatter.RenderLine(source, false, 0, source.Length, lastLine),
            report,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HexAbove256KiBWritesSeparateHeadAndTailWindowsWithANotice()
    {
        const int length = 300 * 1024;
        using var source = new MemorySource(Pattern(length));
        var node = new FormatNode("child", NodeKind.Leaf, new SourceRange(0, length));
        var request = Request(source, new NodeParseResult("Hex", node.Range, node.Range), node);
        using var writer = new StringWriter();
        var result = ForensicReportWriter.Write(writer, request);
        var report = writer.ToString();

        Assert.True(result.HexTruncated);
        Assert.Contains("[Hex truncated]", report, StringComparison.Ordinal);
        Assert.Contains("Export Raw", report, StringComparison.Ordinal);
        Assert.Contains("131,072", report, StringComparison.Ordinal);

        var headLastLine = (ForensicReportWriter.MaxHexHeadBytes / HexDumpFormatter.BytesPerLine) - 1;
        var tailFirstLine = HexDumpFormatter.GetLineCount(length) - (ForensicReportWriter.MaxHexTailBytes / HexDumpFormatter.BytesPerLine);
        Assert.Contains(
            HexDumpFormatter.RenderLine(source, false, 0, length, headLastLine),
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            HexDumpFormatter.RenderLine(source, false, 0, length, tailFirstLine),
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            HexDumpFormatter.RenderLine(source, false, 0, length, headLastLine + 100),
            report,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RawExportCopiesTheExactRangeWithoutAnExportBudget()
    {
        using var source = new MemorySource(Pattern(1024 * 1024));
        using var target = new MemoryStream();
        var written = RawRangeWriter.Write(target, source, new SourceRange(0, source.Length));

        Assert.Equal(source.Length, written);
        Assert.Equal(source.Length, target.Length);
        Assert.Equal(Pattern((int)source.Length), target.ToArray());
    }

    [Fact]
    public void RawExportHandlesPartialSourceReads()
    {
        using var source = new ShortReadSource();
        using var target = new MemoryStream();
        var written = RawRangeWriter.Write(target, source, new SourceRange(7, 11));
        Assert.Equal(11, written);
        Assert.Equal(Enumerable.Range(7, 11).Select(static value => checked((byte)value)).ToArray(), target.ToArray());
    }

    [Fact]
    public void RawExportSupportsCancellationBetweenChunks()
    {
        using var source = new MemorySource(new byte[200000]);
        using var target = new MemoryStream();
        using var cancel = new CancellationTokenSource();
        var progress = new CallbackProgress(_ => cancel.Cancel());
        Assert.Throws<OperationCanceledException>(
            () => RawRangeWriter.Write(target, source, new SourceRange(0, source.Length), progress, cancel.Token));
        Assert.True(target.Length > 0 && target.Length < source.Length);
    }

    [Fact]
    public void RawExportCannotClaimCompletionForAnUnreadableDeclaredLength()
    {
        using var source = new ShortReadSource(unreadable: true);
        using var target = new MemoryStream();
        Assert.Throws<SourceReadException>(() => RawRangeWriter.Write(target, source, new SourceRange(0, 32)));
    }

    [Fact]
    public void CancellationStopsTheReportBeforeAnyTextIsWritten()
    {
        using var source = new MemorySource(new byte[32]);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        using var writer = new StringWriter();
        var request = Request(source, new NodeParseResult("Test", new SourceRange(0, 32), new SourceRange(0, 32)));
        Assert.Throws<OperationCanceledException>(
            () => ForensicReportWriter.Write(writer, request, cancellationToken: cancel.Token));
        Assert.Equal(string.Empty, writer.ToString());
    }

    private static ForensicReportRequest Request(
        IRandomAccessSource source,
        NodeParseResult result,
        FormatNode? node = null)
    {
        node ??= new FormatNode("child", NodeKind.Leaf, result.Range);
        var root = new FormatNode(
            "root",
            NodeKind.Container,
            new SourceRange(0, source.Length),
            new SourceRange(0, source.Length),
            ImmutableArray.Create(node));
        var document = new ParseDocument(
            new RawParser(),
            root,
            ImmutableArray<Diagnostic>.Empty,
            ParseLimits.Default,
            "sample.bin",
            source.Length,
            ProbeResult.NoMatch);
        return new ForensicReportRequest
        {
            Source = source,
            Document = document,
            Node = node,
            Result = result,
        };
    }

    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        return bytes;
    }

    private sealed class ShortReadSource(bool unreadable = false) : IRandomAccessSource
    {
        public long Length => 32;
        public int Read(long offset, Span<byte> buffer)
        {
            if (unreadable) return 0;
            var count = checked((int)Math.Min(2, Math.Min(buffer.Length, Length - offset)));
            for (var index = 0; index < count; index++) buffer[index] = checked((byte)(offset + index));
            return count;
        }

        public void ReadExactly(long offset, Span<byte> buffer) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class CallbackProgress(Action<ParseProgress> callback) : IProgress<ParseProgress>
    {
        public void Report(ParseProgress value) => callback(value);
    }
}
