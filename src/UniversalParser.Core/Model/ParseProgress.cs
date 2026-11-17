namespace UniversalParser.Core.Model;

public readonly record struct ParseProgress(string Stage, long Processed, long Total, string? Message = null)
{
    public double? Fraction => Total > 0 ? Math.Clamp((double)Processed / Total, 0d, 1d) : null;
}
