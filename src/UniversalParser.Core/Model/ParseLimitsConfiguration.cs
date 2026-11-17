using System.Text.Json;

namespace UniversalParser.Core.Model;

/// <summary>
/// Loads the optional <c>universalparser.limits.json</c> override file. The built-in
/// <see cref="ParseLimits.Default"/> values remain the single source of truth; the file only
/// replaces the properties it explicitly contains.
/// </summary>
public static class ParseLimitsConfiguration
{
    public const string DefaultFileName = "universalparser.limits.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private static readonly Lazy<ParseLimits> CachedCurrent = new(
        LoadCurrent,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Process-wide limits loaded once from <see cref="DefaultPath"/>. A missing or malformed
    /// file falls back to <see cref="ParseLimits.Default"/>; the error is exposed by
    /// <see cref="CurrentError"/>.
    /// </summary>
    public static ParseLimits Current => CachedCurrent.Value;

    /// <summary>Error from loading <see cref="Current"/>, or null when the file was absent/valid.</summary>
    public static string? CurrentError { get; private set; }

    /// <summary>Default lookup location: next to the application executable.</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, DefaultFileName);

    /// <summary>
    /// Loads limits from <paramref name="path"/>, or from <see cref="DefaultPath"/> when omitted.
    /// A missing file returns <see cref="ParseLimits.Default"/>; a malformed file throws so the
    /// caller can decide whether to continue with defaults.
    /// </summary>
    public static ParseLimits Load(string? path = null)
    {
        path ??= DefaultPath;
        return File.Exists(path) ? LoadFromFile(path) : ParseLimits.Default;
    }

    public static ParseLimits LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = File.ReadAllText(path);
        return Deserialize(json);
    }

    public static ParseLimits Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var file = JsonSerializer.Deserialize<ParseLimitsFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("The limits configuration is empty.");
        return file.ApplyTo(ParseLimits.Default);
    }

    /// <summary>Non-throwing variant for startup paths that must keep working with bad config.</summary>
    public static bool TryLoad(string? path, out ParseLimits limits, out string? error)
    {
        try
        {
            limits = Load(path);
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            limits = ParseLimits.Default;
            error = exception.Message;
            return false;
        }
    }

    private static ParseLimits LoadCurrent()
    {
        if (TryLoad(null, out var limits, out var error))
        {
            return limits;
        }

        CurrentError = error;
        return ParseLimits.Default;
    }
}
