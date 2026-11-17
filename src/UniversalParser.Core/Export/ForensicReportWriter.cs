using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Export;

/// <summary>Everything the forensic report needs for one selected node.</summary>
public sealed record ForensicReportRequest
{
    public required IRandomAccessSource Source { get; init; }
    public required ParseDocument Document { get; init; }
    public required FormatNode Node { get; init; }
    public required NodeParseResult Result { get; init; }

    /// <summary>Human readable node path, for example "moov / trak / mdia".</summary>
    public string? NodePath { get; init; }

    public bool IncludeHex { get; init; } = true;

    public bool IncludeDocumentDiagnostics { get; init; } = true;
}

/// <summary>
/// Streams a UTF-8 plain-text forensic report. Never builds the whole document in memory and checks
/// cancellation per line. The field and structure sections are always complete; only the binary
/// section uses the fixed 128 KiB + 128 KiB head/tail window defined by the product decision.
/// </summary>
public static class ForensicReportWriter
{
    /// <summary>First bytes of the binary section kept in a report when the range is oversized.</summary>
    public const long MaxHexHeadBytes = 128L * 1024;

    /// <summary>Last bytes of the binary section kept in a report when the range is oversized.</summary>
    public const long MaxHexTailBytes = 128L * 1024;

    /// <summary>Ranges up to this size are written completely.</summary>
    public const long MaxHexBytes = MaxHexHeadBytes + MaxHexTailBytes;

    private const int MaxHexHeadLines = (int)(MaxHexHeadBytes / HexDumpFormatter.BytesPerLine);
    private const int MaxHexTailLines = (int)(MaxHexTailBytes / HexDumpFormatter.BytesPerLine);

    public static ReportWriteResult Write(
        TextWriter writer,
        ForensicReportRequest request,
        IProgress<ParseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        request.Document.Limits.Validate();
        var counting = new ReportByteCountingWriter(writer);
        var hexTruncated = WriteCore(counting, request, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        counting.Flush();
        progress?.Report(new ParseProgress("Done", 1, 1));
        return new ReportWriteResult(counting.BytesWritten, hexTruncated);
    }

    private static bool WriteCore(
        TextWriter writer,
        ForensicReportRequest request,
        IProgress<ParseProgress>? progress,
        CancellationToken cancellationToken)
    {
        var diagnostics = CollectDiagnostics(request);
        var hexLines = request.IncludeHex && !request.Result.RawRange.IsEmpty
            ? HexDumpFormatter.GetLineCount(request.Result.RawRange.Length)
            : 0;
        var emittedHexLines = GetEmittedHexLineCount(request.Result.RawRange.Length, hexLines);

        var total = request.Result.Fields.Length + emittedHexLines + 8;
        var processed = 0L;

        void Step(string stage)
        {
            processed++;
            if (processed % 64 == 0)
            {
                progress?.Report(new ParseProgress(stage, processed, total));
            }
        }

        writer.WriteLine("UniversalParser Forensic Report");
        writer.WriteLine("================================");
        writer.WriteLine();

        cancellationToken.ThrowIfCancellationRequested();

        writer.WriteLine("1. Summary");
        writer.WriteLine($"   File: {ReportText.Escape(Path.GetFileName(request.Document.FileName))}");
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"   Size: {request.Source.Length:N0} bytes (0x{request.Source.Length:X16})"));
        writer.WriteLine(
            $"   Detected format: {ReportText.Escape(request.Document.ParserDisplayName)} ({request.Document.ParserId})");
        writer.WriteLine($"   Node: {ReportText.Escape(request.NodePath ?? request.Node.Name)}");
        writer.WriteLine($"   Node name: {ReportText.Escape(request.Node.Name)}");
        writer.WriteLine($"   Node kind: {request.Node.Kind}");
        writer.WriteLine($"   Node title: {ReportText.Escape(request.Result.Title)}");
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"   Node range: {ReportText.FormatRange(request.Result.Range)} (offset {request.Result.Range.Offset:N0}, length {request.Result.Range.Length:N0})"));
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"   Raw range: {ReportText.FormatRange(request.Result.RawRange)} (offset {request.Result.RawRange.Offset:N0}, length {request.Result.RawRange.Length:N0})"));
        writer.WriteLine(
            $"   Diagnostics: {diagnostics.Length} ({(diagnostics.IsEmpty ? "none" : diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error) + " error(s)")})");
        writer.WriteLine();
        Step("summary");

        writer.WriteLine("2. Diagnostics");
        if (diagnostics.IsEmpty)
        {
            writer.WriteLine("   (none)");
        }
        else
        {
            foreach (var diagnostic in diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var range = diagnostic.Range is { } value ? ReportText.FormatRange(value) : "-";
                writer.WriteLine(
                    $"   [{diagnostic.Severity}] {diagnostic.Code}: {ReportText.Escape(diagnostic.Message)} ({range})");
                if (!string.IsNullOrEmpty(diagnostic.Detail))
                {
                    writer.WriteLine($"      Detail: {ReportText.Escape(diagnostic.Detail)}");
                }

                Step("diagnostics");
            }
        }

        writer.WriteLine();
        writer.WriteLine("3. Structure");
        WriteStructure(writer, request, cancellationToken);

        writer.WriteLine();
        writer.WriteLine("4. Fields");
        if (request.Result.Fields.IsEmpty)
        {
            writer.WriteLine("   (none)");
        }
        else
        {
            foreach (var field in request.Result.Fields)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var suffix = field.Kind == FieldKind.Raw ? string.Empty : $"    ({field.Kind})";
                writer.WriteLine($"   {ReportText.Escape(field.Key)} = {ReportText.Escape(field.Value)}{suffix}");
                Step("fields");
            }
        }

        writer.WriteLine();
        writer.WriteLine("5. Hex");
        var hexTruncated = WriteHex(writer, request, hexLines, cancellationToken, Step);

        writer.WriteLine();
        writer.WriteLine("6. Read Limits");
        var limits = request.Document.Limits;
        writer.WriteLine($"   MaxStringBytes = {limits.MaxStringBytes}");
        writer.WriteLine($"   MaxBlobBytes = {limits.MaxBlobBytes}");
        writer.WriteLine($"   MaxDataLines = {limits.MaxDataLines}");
        var limitHit = diagnostics.Any(static diagnostic => diagnostic.Code.StartsWith("LIMIT_", StringComparison.Ordinal));
        writer.WriteLine($"   Limit reached: {(limitHit ? "yes" : "no")}");
        writer.WriteLine();
        Step("limits");

        return hexTruncated;
    }

    private static long GetEmittedHexLineCount(long rawLength, long hexLines)
    {
        if (hexLines == 0)
        {
            return 0;
        }

        return rawLength > MaxHexBytes ? MaxHexHeadLines + MaxHexTailLines : hexLines;
    }

    private static bool WriteHex(
        TextWriter writer,
        ForensicReportRequest request,
        long hexLines,
        CancellationToken cancellationToken,
        Action<string> step)
    {
        if (!request.IncludeHex)
        {
            writer.WriteLine("   (disabled)");
            return false;
        }

        if (hexLines == 0)
        {
            writer.WriteLine("   (empty range)");
            return false;
        }

        var rawRange = request.Result.RawRange;
        var wide = request.Source.Length > uint.MaxValue;
        writer.WriteLine("   Range: " + ReportText.FormatRange(rawRange));
        writer.WriteLine("   " + HexDumpFormatter.FormatHeader(wide));
        Span<char> buffer = stackalloc char[HexDumpFormatter.MaxLineLength];

        var truncated = rawRange.Length > MaxHexBytes;
        if (!truncated)
        {
            for (var line = 0L; line < hexLines; line++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteHexLine(writer, request.Source, wide, rawRange, line, buffer);
                step("hex");
            }

            return false;
        }

        for (var line = 0L; line < MaxHexHeadLines; line++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteHexLine(writer, request.Source, wide, rawRange, line, buffer);
            step("hex");
        }

        // The two windows are written as separate blocks so the jump is never mistaken for a
        // continuous dump; the omitted byte count stays visible to a reader.
        var omitted = rawRange.Length - MaxHexHeadBytes - MaxHexTailBytes;
        writer.WriteLine();
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"   [Hex truncated] The binary range is {rawRange.Length:N0} bytes; this report contains only the first {MaxHexHeadBytes:N0} bytes and the last {MaxHexTailBytes:N0} bytes ({omitted:N0} bytes omitted)."));
        writer.WriteLine(
            "   [Hex truncated] The complete binary data is not included in this report; export the raw range separately (Export Raw) to obtain it.");
        writer.WriteLine();

        var tailStart = hexLines - MaxHexTailLines;
        for (var line = tailStart; line < hexLines; line++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteHexLine(writer, request.Source, wide, rawRange, line, buffer);
            step("hex");
        }

        return true;
    }

    private static void WriteHexLine(
        TextWriter writer,
        IRandomAccessSource source,
        bool wide,
        SourceRange range,
        long line,
        Span<char> buffer)
    {
        var length = HexDumpFormatter.RenderLine(
            source,
            wide,
            range.Offset,
            range.Length,
            line,
            buffer);
        if (length > 0)
        {
            writer.WriteLine($"   {new string(buffer[..length])}");
        }
    }

    private static ImmutableArray<Diagnostic> CollectDiagnostics(ForensicReportRequest request)
    {
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        builder.AddRange(request.Result.Diagnostics);
        if (request.IncludeDocumentDiagnostics)
        {
            builder.AddRange(request.Document.Diagnostics);
        }

        return builder.ToImmutable();
    }

    private static void WriteStructure(
        TextWriter writer,
        ForensicReportRequest request,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<(FormatNode Node, int NextChild, int Depth)>();
        stack.Push((request.Node, -1, 0));
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (node, child, depth) = stack.Pop();
            if (child == -1)
            {
                writer.Write("   ");
                for (var index = 0; index < depth; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.Write("  ");
                }
                writer.WriteLine($"{ReportText.Escape(node.Name)}  [{node.Kind}]  {ReportText.FormatRange(node.Range)}");
                child = 0;
            }
            if (child < node.Children.Length)
            {
                // Retain just one continuation per ancestor, not all siblings at once.
                stack.Push((node, child + 1, depth));
                stack.Push((node.Children[child], -1, depth + 1));
            }
        }
    }
}
