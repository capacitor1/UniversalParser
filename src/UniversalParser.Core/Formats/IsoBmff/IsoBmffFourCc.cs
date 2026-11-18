using UniversalParser.Core.Model;

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

        Span<char> characters = stackalloc char[16];
        var length = 0;
        if (bytes[0] == 0xA9)
        {
            characters[length++] = '©';
            bytes = bytes[1..];
        }

        length += RawIdentifier.WriteBytes(bytes, characters[length..]);
        return new string(characters[..length]);
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

}
