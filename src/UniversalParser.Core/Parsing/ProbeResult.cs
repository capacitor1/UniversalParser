namespace UniversalParser.Core.Parsing;

public enum ProbeConfidence
{
    None,
    Weak,
    Strong,
}

public readonly record struct ProbeResult(ProbeConfidence Confidence, string? Reason = null)
{
    public static ProbeResult NoMatch => new(ProbeConfidence.None);
    public bool IsMatch => Confidence != ProbeConfidence.None;

    public static ProbeResult Weak(string? reason = null) => new(ProbeConfidence.Weak, reason);

    public static ProbeResult Strong(string? reason = null) => new(ProbeConfidence.Strong, reason);
}
