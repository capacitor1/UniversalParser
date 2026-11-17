namespace UniversalParser.Core.Parsing;

/// <summary>
/// Fixed thresholds for byte ranges that may take long enough to be worth reporting progress.
/// They are deliberately not configurable limits: crossing a threshold never stops or truncates
/// the work, it only turns on progress reporting (or widens the detection window).
/// </summary>
internal static class ParseThresholds
{
    /// <summary>
    /// 512 KiB. Ranges at or below this run silently; larger ranges keep running but report
    /// progress through <see cref="ParseContext.ReportProgress"/> so the GUI bar stays active.
    /// </summary>
    public const int LargeOperationBytes = 512 * 1024;
}
