using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Parsing;

/// <summary>
/// Coordinates detection, parsing, and lazy node parsing. Parser exceptions are converted
/// into a failed document with a structured diagnostic.
/// </summary>
public sealed class ParserEngine
{
    private readonly ParserRegistry _registry;

    public ParserEngine(ParserRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public ParseDocument Parse(
        IRandomAccessSource source,
        string? fileName = null,
        ParseLimits? limits = null,
        IProgress<ParseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= ParseLimitsConfiguration.Current;
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new DiagnosticBag();
        var context = new ParseContext(source, limits, fileName, progress, diagnostics, cancellationToken);
        DetectionResult? detection = null;
        try
        {
            progress?.Report(new ParseProgress("Detecting format", 0, source.Length));
            detection = _registry.Detect(context.Source, limits, cancellationToken);
            foreach (var diagnostic in detection.Diagnostics) diagnostics.Add(diagnostic);
            progress?.Report(new ParseProgress("Parsing", 0, source.Length));
            var root = detection.Parser.Parse(context);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ParseProgress("Done", source.Length, source.Length));
            return new ParseDocument(
                detection.Parser,
                root,
                diagnostics.ToImmutable(),
                limits,
                fileName,
                source.Length,
                detection.Probe);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var parser = detection?.Parser ?? _registry.Parsers.Single(static parser => parser.IsFallback);
            var range = new SourceRange(0, source.Length);
            diagnostics.Error(
                DiagnosticCodes.ParseFailed,
                $"Parser '{parser.Id}' failed: {exception.Message}",
                range,
                exception.ToString());
            var root = CreateFailureRoot(range);
            return new ParseDocument(
                parser,
                root,
                diagnostics.ToImmutable(),
                limits,
                fileName,
                source.Length,
                detection?.Probe ?? ProbeResult.NoMatch);
        }
    }

    public NodeParseResult ParseNode(
        IRandomAccessSource source,
        ParseDocument document,
        FormatNode node,
        IProgress<ParseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new DiagnosticBag();
        var context = new ParseContext(
            source,
            document.Limits,
            document.FileName,
            progress,
            diagnostics,
            cancellationToken);
        try
        {
            var result = document.Parser.ParseNode(context, node);
            if (result is null)
            {
                throw new InvalidOperationException($"Parser '{document.Parser.Id}' returned a null node result.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return diagnostics.Count == 0
                ? result
                : new NodeParseResult(
                    result.Title,
                    result.Range,
                    result.RawRange,
                    result.Fields,
                    result.Diagnostics.AddRange(diagnostics.ToImmutable()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var diagnostic = new Diagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCodes.ParseFailed,
                $"Node parse failed for '{node.Name}': {exception.Message}",
                node.Range,
                exception.ToString());
            diagnostics.Add(diagnostic);
            return PartialNodeResult(context, node, Field.Error($"Node parse failed: {exception.Message}", range: node.Range));
        }
    }

    private static NodeParseResult PartialNodeResult(ParseContext context, FormatNode node, Field message)
    {
        var partial = context.PartialFields;
        var fields = partial?.ToImmutable() ?? ImmutableArray<Field>.Empty;
        var diagnostics = context.Diagnostics.ToImmutable();
        if (partial is not null) diagnostics = diagnostics.AddRange(partial.Diagnostics);
        return new NodeParseResult(node.Name, node.Range, node.Range, fields.Add(message), diagnostics);
    }

    private static FormatNode CreateFailureRoot(SourceRange range)
    {
        var unparsed = new FormatNode("<unparsed data>", NodeKind.Unparsed, range);
        return new FormatNode(
            "<parse failed>",
            NodeKind.Synthetic,
            range,
            range,
            ImmutableArray.Create(unparsed));
    }
}
