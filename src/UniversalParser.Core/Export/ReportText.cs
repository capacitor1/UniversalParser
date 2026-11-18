using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;

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

        return TextEscaping.Escape(value, escapeNull: false, escapeAllControls: true);
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

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(
                character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
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

        var dot = result.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? result.AsSpan() : result.AsSpan(0, dot);
        return IsReservedDeviceName(stem) ? "_" + result : result;
    }

    private static bool IsReservedDeviceName(ReadOnlySpan<char> stem)
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
            && stem[3] is >= '1' and <= '9' or '¹' or '²' or '³')
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
