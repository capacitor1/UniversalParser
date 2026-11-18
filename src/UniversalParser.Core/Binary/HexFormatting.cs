using System.Globalization;

namespace UniversalParser.Core.Binary;

/// <summary>Bounded hexadecimal formatting shared by format-specific field emitters.</summary>
internal static class HexFormatting
{
    public static string Format(ReadOnlySpan<byte> bytes, int maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        var count = Math.Min(bytes.Length, maxBytes);
        var truncated = bytes.Length > count;
        var length = (long)count * 3 - (count > 0 ? 1 : 0) + (truncated ? 4 : 0);
        if (length > Array.MaxLength)
        {
            throw new ArgumentException("The formatted string would exceed the maximum length.", nameof(bytes));
        }

        return string.Create((int)length, bytes[..count], static (destination, source) =>
            {
                var offset = 0;
                foreach (var value in source)
                {
                    if (offset > 0)
                    {
                        destination[offset++] = ' ';
                    }

                    value.TryFormat(destination[offset..], out _, "X2", CultureInfo.InvariantCulture);
                    offset += 2;
                }

                if (offset < destination.Length)
                {
                    " ...".AsSpan().CopyTo(destination[offset..]);
                }
            });
    }

    internal static void WriteEscapedByte(byte value, Span<char> destination)
    {
        destination[0] = '\\';
        destination[1] = 'x';
        value.TryFormat(destination[2..], out _, "X2", CultureInfo.InvariantCulture);
    }
}
