namespace UniversalParser.Core.Export;

/// <summary>
/// Outcome of a report write. <paramref name="HexTruncated"/> is true when the binary section was
/// larger than the fixed head/tail window; the report itself explains how to export the full range.
/// </summary>
public readonly record struct ReportWriteResult(long BytesWritten, bool HexTruncated);
