namespace UniversalParser.Core.Export;

/// <summary>Half-open character-column ranges for one byte span in a rendered hex line.</summary>
public readonly record struct HexColumnSpan(int HexStart, int HexEnd, int AsciiStart, int AsciiEnd);
