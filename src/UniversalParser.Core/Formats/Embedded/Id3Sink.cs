namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Collects ID3 fields in presentation order. A scope becomes a dotted key prefix so nested
/// structures (extended header, frames, CHAP/CTOC sub-frames) keep a readable path.
/// </summary>
internal readonly struct Id3Sink
{
    private readonly List<(string Key, string Value)> _lines;

    public Id3Sink(List<(string Key, string Value)> lines, string? prefix = null)
    {
        _lines = lines ?? throw new ArgumentNullException(nameof(lines));
        Path = prefix ?? string.Empty;
    }

    public string Path { get; }

    public List<(string Key, string Value)> Lines => _lines;

    public Id3Sink Key(string name) => Scope(name);

    public Id3Sink Scope(string name) => new(_lines, Join(Path, name));

    public void Text(string key, string? value) => Add(key, value ?? string.Empty);

    public void Number(string key, int value) => Add(key, value.ToString());

    public void Number(string key, long value) => Add(key, value.ToString());

    public void Verbatim(string key, string? value) => Add(key, value ?? string.Empty);

    public void Payload(string key, ReadOnlySpan<byte> payload, string type = "payload") =>
        Add(key, Id3Format.Payload(type, payload.Length));

    private void Add(string key, string value) => _lines.Add((Join(Path, key), value));

    private static string Join(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        return left + "." + right;
    }
}
