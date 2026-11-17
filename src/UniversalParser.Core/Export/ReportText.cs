using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Export;

/// <summary>Escaping helpers for the plain-text forensic report.</summary>
public static class ReportText
{
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder? builder = null;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            string? replacement = character switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ when char.IsControl(character) => string.Create(
                    CultureInfo.InvariantCulture,
                    $"\\x{(int)character:X2}"),
                _ => null,
            };

            if (replacement is null)
            {
                builder?.Append(character);
                continue;
            }

            builder ??= new StringBuilder(value.Length + 16).Append(value, 0, index);
            builder.Append(replacement);
        }

        return builder?.ToString() ?? value;
    }

    /// <summary>
    /// Turns a node name into a safe file name. The value never comes from file content paths, but
    /// 4CCs can still contain path or wildcard characters.
    /// </summary>
    public static string SanitizeFileName(string? name, string fallback = "node")
    {
        var value = name?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(
                character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
                || Array.IndexOf(invalid, character) >= 0
                || char.IsControl(character)
                    ? '_'
                    : character);
        }

        var result = builder.ToString().Trim(' ', '.');
        if (result.Length == 0)
        {
            return fallback;
        }

        if (result.Length > 96)
        {
            var cut = 96;
            if (char.IsHighSurrogate(result[cut - 1]))
            {
                cut--;
            }

            result = result[..cut].TrimEnd(' ', '.');
            if (result.Length == 0)
            {
                return fallback;
            }
        }

        var dot = result.IndexOf('.');
        var stem = dot < 0 ? result : result[..dot];
        return IsReservedDeviceName(stem) ? "_" + result : result;
    }

    private static bool IsReservedDeviceName(string stem)
    {
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && stem[3] is >= '1' and <= '9')
        {
            return true;
        }

        return false;
    }

    public static string FormatRange(SourceRange range) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"0x{range.Offset:X8}+0x{range.Length:X8}");
}
