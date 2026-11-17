using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Model;

public enum DiagnosticSeverity
{
    Note,
    Warning,
    Error,
}

/// <summary>
/// Structured parser diagnostic. Codes are stable English identifiers for tests and reports.
/// </summary>
public sealed record Diagnostic
{
    public Diagnostic(
        DiagnosticSeverity severity,
        string code,
        string message,
        SourceRange? range = null,
        string? detail = null)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Severity = severity;
        Code = code;
        Message = message;
        Range = range;
        Detail = detail;
    }

    public DiagnosticSeverity Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public SourceRange? Range { get; }
    public string? Detail { get; }
}
