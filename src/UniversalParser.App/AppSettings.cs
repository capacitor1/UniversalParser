namespace UniversalParser.App;

/// <summary>UI-only tunables. Parsing limits live in <c>UniversalParser.Core.Model.ParseLimits</c>.</summary>
internal static class AppSettings
{
    /// <summary>Maximum rows used when sizing the (virtual) field list.</summary>
    public const int DataLineMaxShowCount = 1000;

    /// <summary>How many parsed nodes are memoised; the field list re-parses on selection otherwise.</summary>
    public const int NodeParseCacheSize = 128;

    /// <summary>Nodes expanded per UI tick; keeps the window responsive.</summary>
    public const int TreeExpandBatch = 200;

    public const int ExportBufferSize = 64 * 1024;

    public const long LargeExportWarningBytes = 128L * 1024 * 1024;

    public const int DefaultTopChartSlices = 10;
    public const int MinTopChartSlices = 3;
    public const int MaxTopChartSlices = 10;

    /// <summary>
    /// Maximum entries in the statistics byte chart, including the "Other names" slice. Only the
    /// statistics window reads this; it is a process-local setting and is never persisted.
    /// </summary>
    public static int TopChartSlices
    {
        get => Volatile.Read(ref field);
        set => Volatile.Write(ref field, Math.Clamp(value, MinTopChartSlices, MaxTopChartSlices));
    } = DefaultTopChartSlices;
}
