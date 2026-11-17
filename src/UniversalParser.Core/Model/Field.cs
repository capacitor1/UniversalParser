using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Model;

/// <summary>
/// A display field. Raw keys are official/descriptive names and never use angle brackets;
/// angle brackets are reserved for derived values, notes, and parser errors.
/// </summary>
public sealed class Field
{
    public Field(
        string key,
        string value,
        FieldKind kind = FieldKind.Raw,
        SourceRange? range = null,
        string? rawValue = null,
        string? unit = null,
        string? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == FieldKind.Raw && key.StartsWith('<'))
        {
            throw new ArgumentException("Raw field keys must not use angle brackets.", nameof(key));
        }

        Key = key;
        Value = value;
        Kind = kind;
        Range = range;
        RawValue = rawValue;
        Unit = unit;
        Encoding = encoding;
    }

    public string Key { get; }
    public string Value { get; }
    public FieldKind Kind { get; }
    public SourceRange? Range { get; }
    public string? RawValue { get; }
    public string? Unit { get; }
    public string? Encoding { get; }

    public static Field Raw(string key, string value, SourceRange? range = null, string? rawValue = null) =>
        new(key, value, FieldKind.Raw, range, rawValue);

    public static Field Derived(string key, string value, SourceRange? range = null, string? rawValue = null) =>
        new(key, value, FieldKind.Derived, range, rawValue);

    public static Field Note(string value, string key = "<Note>", SourceRange? range = null) =>
        new(key, value, FieldKind.Note, range);

    public static Field Warning(string value, string key = "<Warning>", SourceRange? range = null) =>
        new(key, value, FieldKind.Warning, range);

    public static Field Error(string value, string key = "<Error>", SourceRange? range = null) =>
        new(key, value, FieldKind.Error, range);
}
