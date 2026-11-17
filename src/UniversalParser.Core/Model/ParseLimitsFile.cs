namespace UniversalParser.Core.Model;

/// <summary>
/// JSON shape for the optional external limit overrides. Every property is nullable so a
/// configuration file only needs to contain the values it wants to change.
/// </summary>
public sealed class ParseLimitsFile
{
    public int? MaxStringBytes { get; init; }
    public long? MaxBlobBytes { get; init; }
    public int? MaxDataLines { get; init; }

    public ParseLimits ApplyTo(ParseLimits defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        var limits = defaults with
        {
            MaxStringBytes = MaxStringBytes ?? defaults.MaxStringBytes,
            MaxBlobBytes = MaxBlobBytes ?? defaults.MaxBlobBytes,
            MaxDataLines = MaxDataLines ?? defaults.MaxDataLines,
        };

        limits.Validate();
        return limits;
    }
}
