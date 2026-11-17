namespace UniversalParser.Core.Statistics;

// Inclusive sizes can count the same bytes at multiple depths, so use decimal for their sum.
public sealed record NameStat(string Name, long Count, decimal TotalSize, long OwnedBytes, long MaxSize)
{
    /// <summary>True when the name occurs once; the statistics window greys these rows out.</summary>
    public bool IsUnique => Count == 1;
}
