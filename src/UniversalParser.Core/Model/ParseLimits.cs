namespace UniversalParser.Core.Model;

/// <summary>
/// Memory guardrails for reading field values, table rows and export output. They are not
/// structural or performance limits: tree depth, node count and parse duration are unbounded.
/// </summary>
public sealed record ParseLimits
{
    public int MaxStringBytes { get; init; } = 4 << 20;
    public long MaxBlobBytes { get; init; } = 256L << 20;
    public int MaxDataLines { get; init; } = 10_000;

    public static ParseLimits Default { get; } = new();

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxStringBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBlobBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDataLines);
    }
}
