using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Parsing;

/// <summary>
/// Owns one opened source and its parsed document. The GUI keeps exactly one session; disposing it
/// releases the file handle so opening another file can never leave two sources alive.
/// </summary>
public sealed class ParseSession : IDisposable
{
    private readonly ParserEngine _engine;
    private IRandomAccessSource? _source;
    private bool _disposed;

    private ParseSession(ParserEngine engine, IRandomAccessSource source, ParseDocument document)
    {
        _engine = engine;
        _source = source;
        Document = document;
    }

    public ParseDocument Document { get; }

    public IRandomAccessSource Source =>
        _source ?? throw new ObjectDisposedException(nameof(ParseSession));

    public string? FilePath => _source is FileSource file ? file.FilePath : null;

    public DateTime? CreationTimeUtc => _source is FileSource file ? file.CreationTimeUtc : null;

    public FileAttributes? FileAttributes => _source is FileSource file ? file.Attributes : null;

    public long Length => _source?.Length ?? throw new ObjectDisposedException(nameof(ParseSession));

    public bool IsDisposed => _disposed;

    /// <summary>
    /// Opens a file, detects its format and builds the tree. A cancelled or failed open disposes the
    /// source before rethrowing, so the caller never has to clean up a half-open session.
    /// </summary>
    public static ParseSession Open(
        string path,
        ParserEngine engine,
        ParseLimits? limits = null,
        IProgress<ParseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(engine);
        cancellationToken.ThrowIfCancellationRequested();

        var source = FileSource.Open(path);
        try
        {
            var document = engine.Parse(
                source,
                Path.GetFileName(path),
                limits,
                progress,
                cancellationToken);
            return new ParseSession(engine, source, document);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    public static Task<ParseSession> OpenAsync(
        string path,
        ParserEngine engine,
        ParseLimits? limits = null,
        IProgress<ParseProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Open(path, engine, limits, progress, cancellationToken), cancellationToken);

    public NodeParseResult ParseNode(
        FormatNode node,
        IProgress<ParseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        var source = Source;
        cancellationToken.ThrowIfCancellationRequested();
        return _engine.ParseNode(source, Document, node, progress, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source?.Dispose();
        _source = null;
    }
}
