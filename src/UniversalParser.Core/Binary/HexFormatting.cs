using System.Text;

namespace UniversalParser.Core.Binary;

/// <summary>Bounded hexadecimal formatting shared by format-specific field emitters.</summary>
internal static class HexFormatting
{
    public static string Format(ReadOnlySpan<byte> bytes, int maxBytes)
    {
        var count = Math.Min(bytes.Length, maxBytes);
        var text = Convert.ToHexString(bytes[..count]);
        var builder = new StringBuilder(text.Length + text.Length / 2);
        for (var index = 0; index < text.Length; index += 2)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(text, index, 2);
        }

        if (bytes.Length > count)
        {
            builder.Append(" ...");
        }

        return builder.ToString();
    }
}
