using System.Globalization;
using System.Text;

namespace UniversalParser.Core.Formatting;

/// <summary>
/// Printable form for metadata text. Control characters are escaped so one field stays on one
/// display line; file content is never translated or replaced with ASCII lookalikes.
/// </summary>
internal static class TextEscaping
{
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\0':
                    builder.Append("\\0");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < 0x20 || character == 0x7F)
                    {
                        builder.Append("\\x").Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
