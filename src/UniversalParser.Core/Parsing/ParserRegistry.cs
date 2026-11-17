using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Asf;
using UniversalParser.Core.Formats.Blend;
using UniversalParser.Core.Formats.Ebml;
using UniversalParser.Core.Formats.Fbx;
using UniversalParser.Core.Formats.Flac;
using UniversalParser.Core.Formats.Flv;
using UniversalParser.Core.Formats.IsoBmff;
using UniversalParser.Core.Formats.Jpeg;
using UniversalParser.Core.Formats.Midi;
using UniversalParser.Core.Formats.Nbt;
using UniversalParser.Core.Formats.Ogg;
using UniversalParser.Core.Formats.Png;
using UniversalParser.Core.Formats.Protobuf;
using UniversalParser.Core.Formats.Psd;
using UniversalParser.Core.Formats.Raw;
using UniversalParser.Core.Formats.Riff;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Parsing;

/// <summary>
/// Explicit parser registry. Detection is deterministic: strong match, then weak match,
/// then the single fallback parser. Probe failures become diagnostics, never crashes.
/// </summary>
public sealed class ParserRegistry
{
    private readonly ImmutableArray<IFormatParser> _parsers;

    public ParserRegistry(IEnumerable<IFormatParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        var list = parsers.ToImmutableArray();
        if (list.IsEmpty)
        {
            throw new ArgumentException("At least one parser is required.", nameof(parsers));
        }

        if (list.Any(static parser => parser is null))
        {
            throw new ArgumentException("Parsers must not contain null entries.", nameof(parsers));
        }

        var duplicateId = list
            .GroupBy(static parser => parser.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException($"Duplicate parser id '{duplicateId.Key}'.", nameof(parsers));
        }

        if (list.Count(static parser => parser.IsFallback) != 1)
        {
            throw new ArgumentException("Exactly one fallback parser is required.", nameof(parsers));
        }

        _parsers = list;
    }

    public IReadOnlyList<IFormatParser> Parsers => _parsers;

    public static ParserRegistry CreateDefault() => new(
    [
        new IsoBmffParser(),
        new JpegParser(),
        new OggParser(),
        new BlendParser(),
        new PngParser(),
        new FlacParser(),
        new PsdParser(),
        new MidiParser(),
        new FbxParser(),
        new AsfParser(),
        new FlvParser(),
        new EbmlParser(),
        new RiffParser(),
        new NbtParser(),
        new ProtobufParser(),
        new RawParser(),
    ]);

    public IFormatParser GetRequired(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _parsers.FirstOrDefault(parser => string.Equals(parser.Id, id, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Parser '{id}' is not registered.");
    }

    public DetectionResult Detect(
        IRandomAccessSource source,
        ParseLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        var diagnostics = new List<Diagnostic>();
        IFormatParser? bestWeak = null;
        var bestWeakProbe = ProbeResult.NoMatch;

        foreach (var parser in _parsers
                     .Where(static parser => !parser.IsFallback)
                     .OrderByDescending(static parser => parser.ProbePriority))
        {
            cancellationToken.ThrowIfCancellationRequested();

            ProbeResult probe;
            try
            {
                probe = parser.Probe(source, limits, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    DiagnosticCodes.ProbeFailed,
                    $"Probe for '{parser.Id}' failed: {exception.Message}",
                    new SourceRange(0, Math.Min(source.Length, (long)ParseThresholds.LargeOperationBytes)),
                    exception.ToString()));
                continue;
            }

            if (probe.Confidence == ProbeConfidence.Strong)
            {
                return new DetectionResult(parser, probe, ImmutableArray.CreateRange(diagnostics));
            }

            if (probe.Confidence == ProbeConfidence.Weak && bestWeak is null)
            {
                bestWeak = parser;
                bestWeakProbe = probe;
            }
        }

        if (bestWeak is not null)
        {
            return new DetectionResult(bestWeak, bestWeakProbe, ImmutableArray.CreateRange(diagnostics));
        }

        var fallback = _parsers.First(static parser => parser.IsFallback);
        return new DetectionResult(fallback, ProbeResult.NoMatch, ImmutableArray.CreateRange(diagnostics));
    }
}
