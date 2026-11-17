using System.Globalization;
using System.Text;

namespace UniversalParser.Core.Model;

/// <summary>
/// Formats a raw identifier exactly as it appears in the file for use inside a title:
/// printable single-byte characters stay as text, everything else becomes \xNN. GUIDs use the
/// canonical curly-braced form required for ASF.
/// </summary>
public static class RawIdentifier
{
    public static string FormatBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(bytes.Length + 8);
        foreach (var value in bytes)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                builder.Append((char)value);
            }
            else
            {
                builder.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    public static string FormatText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return FormatBytes(Encoding.Latin1.GetBytes(value));
    }

    public static string FormatGuid(Guid value) =>
        "{" + value.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant() + "}";
}
