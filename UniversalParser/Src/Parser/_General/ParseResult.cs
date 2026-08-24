public sealed class ParseResult
{
    public string Title { get; init; } = "";

    public ulong Position { get; init; }

    public ulong Length { get; init; }

    public List<(string K, string V)> DataLines { get; init; } = [];

    public Stream? RawData { get; init; }
}