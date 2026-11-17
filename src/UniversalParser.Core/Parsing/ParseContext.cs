using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Parsing;

/// <summary>
/// Per-parse state shared by a format parser. It never exposes a mutable source position.
/// Tree depth, node count and parse duration are deliberately unbounded; only memory-facing
/// field limits remain.
/// </summary>
public sealed class ParseContext
{
    private long _lastProgressTick;
    private long _lastProgressProcessed;

    public ParseContext(
        IRandomAccessSource source,
        ParseLimits limits,
        string? fileName = null,
        IProgress<ParseProgress>? progress = null,
        DiagnosticBag? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        Source = new ParseCheckpointSource(source, ThrowIfCancellationRequested);
        Limits = limits;
        FileName = fileName;
        Progress = progress;
        Diagnostics = diagnostics ?? new DiagnosticBag();
        CancellationToken = cancellationToken;
    }

    public IRandomAccessSource Source { get; }
    public ParseLimits Limits { get; }
    public string? FileName { get; }
    public IProgress<ParseProgress>? Progress { get; }
    public DiagnosticBag Diagnostics { get; }
    public CancellationToken CancellationToken { get; }

    internal FieldList? PartialFields { get; private set; }

    internal FieldList CreateFieldList(int? maxFields = null)
    {
        var fields = new FieldList(maxFields ?? Limits.MaxDataLines);
        PartialFields = fields;
        return fields;
    }

    public SourceReader Reader(long offset, long length) => new(Source, offset, length);

    public SourceReader Reader(SourceRange range) => Reader(range.Offset, range.Length);

    public void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();

    public void Report(ParseProgress progress) => Progress?.Report(progress);

    public void Report(string stage, long processed, long total, string? message = null) =>
        Report(new ParseProgress(stage, processed, total, message));

    /// <summary>
    /// Reports tree-building progress without flooding the UI thread. Updates are coalesced to one
    /// per 100 ms, and the final position is always delivered so the bar reaches 100%.
    /// </summary>
    public void ReportProgress(string stage, long processed, long total, string? message = null)
    {
        if (Progress is null)
        {
            return;
        }

        total = Math.Max(0, total);
        processed = Math.Clamp(processed, 0, total);
        var isFinal = total == 0 || processed >= total;
        var now = Environment.TickCount64;
        if (!isFinal && (now - _lastProgressTick < 100 || processed == _lastProgressProcessed))
        {
            return;
        }

        _lastProgressTick = now;
        _lastProgressProcessed = processed;
        Progress.Report(new ParseProgress(stage, processed, total, message));
    }
}
