using System.Globalization;
using System.Text;

namespace UniversalParser.Core.Formats.IsoBmff;

/// <summary>
/// Decodes ISO BMFF four-character codes. Printable ASCII is shown directly; the Apple
/// metadata prefix 0xA9 becomes '©'; other bytes are escaped as \xNN so a damaged type
/// never breaks the tree.
/// </summary>
internal static class IsoBmffFourCc
{
    public static string Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4)
        {
            throw new ArgumentException("A four-character code must be exactly 4 bytes.", nameof(bytes));
        }

        if (bytes[0] == 0xA9)
        {
            var builder = new StringBuilder(5);
            builder.Append('©');
            for (var index = 1; index < 4; index++)
            {
                Append(builder, bytes[index]);
            }

            return builder.ToString();
        }

        var printable = true;
        for (var index = 0; index < 4; index++)
        {
            if (!IsPrintable(bytes[index]))
            {
                printable = false;
                break;
            }
        }

        if (printable)
        {
            return Encoding.ASCII.GetString(bytes);
        }

        var escaped = new StringBuilder(16);
        foreach (var value in bytes)
        {
            Append(escaped, value);
        }

        return escaped.ToString();
    }

    public static bool IsPrintable(byte value) => value is >= 0x20 and <= 0x7E;

    public static bool IsValidType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4)
        {
            return false;
        }

        foreach (var value in bytes)
        {
            if (value != 0xA9 && !IsPrintable(value))
            {
                return false;
            }
        }

        return true;
    }

    private static void Append(StringBuilder builder, byte value)
    {
        if (IsPrintable(value))
        {
            builder.Append((char)value);
        }
        else
        {
            builder.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }
    }
}
