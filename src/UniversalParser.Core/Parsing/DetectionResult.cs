using System.Collections.Immutable;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Parsing;

public sealed record DetectionResult(
    IFormatParser Parser,
    ProbeResult Probe,
    ImmutableArray<Diagnostic> Diagnostics);
