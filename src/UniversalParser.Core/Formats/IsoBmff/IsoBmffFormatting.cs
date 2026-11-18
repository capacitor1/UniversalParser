using System.Globalization;
using System.Text;
using UniversalParser.Core.Formatting;

namespace UniversalParser.Core.Formats.IsoBmff;

/// <summary>Display helpers shared by ISOBMFF box parsers.</summary>
internal static class IsoBmffFormatting
{
    private const int HexLimit = 64;

    private static readonly DateTime IsoEpoch = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static string FormatTime(ulong seconds)
    {
        try
        {
            if (seconds > 253_402_300_799UL)
            {
                return "<out of range>";
            }

            return IsoEpoch.AddSeconds(seconds).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "<out of range>";
        }
    }

    public static string FormatLanguage(ushort packed)
    {
        var c0 = (packed >> 10) & 0x1F;
        var c1 = (packed >> 5) & 0x1F;
        var c2 = packed & 0x1F;
        if (c0 is < 1 or > 26 || c1 is < 1 or > 26 || c2 is < 1 or > 26)
        {
            return $"0x{packed:X4}";
        }

        return new string([(char)(0x60 + c0), (char)(0x60 + c1), (char)(0x60 + c2)]);
    }

    public static string FormatFixed1616(uint raw) =>
        FormatFixed(unchecked((int)raw), 65536.0, raw);

    public static string FormatFixed1616Signed(uint raw) => FormatFixed1616(raw);

    public static string FormatFixed88(ushort raw) =>
        FormatFixed(unchecked((short)raw), 256.0, raw);

    public static string FormatFixed230(uint raw) =>
        FormatFixed(unchecked((int)raw), 1073741824.0, raw);

    private static string FormatFixed(double value, double scale, uint raw)
    {
        var result = value / scale;
        var text = Math.Abs(result - Math.Round(result)) < 1e-9
            ? Math.Round(result).ToString("0", CultureInfo.InvariantCulture)
            : result.ToString("0.######", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{text} (0x{raw:X8})");
    }

    public static string Hex(ReadOnlySpan<byte> bytes, int maxBytes = HexLimit)
    {
        if (bytes.IsEmpty)
        {
            return "(empty)";
        }

        var count = Math.Min(bytes.Length, maxBytes);
        var builder = new StringBuilder(count * 3);
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
        }

        if (count < bytes.Length)
        {
            builder.Append(" ...");
        }

        return builder.ToString();
    }

    public static string Display(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "<empty>";
        }

        return TextEscaping.Escape(value);
    }

    public static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return TextDecoding.Display(bytes[2..], Encoding.BigEndianUnicode);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return TextDecoding.Display(bytes[2..], Encoding.Unicode);
        }

        // Length-prefixed strings may carry NUL padding; keep exactly one escaped terminator.
        var end = bytes.Length;
        while (end > 0 && bytes[end - 1] == 0)
        {
            end--;
        }

        var text = TextDecoding.Display(bytes[..end], Encoding.UTF8);
        return end < bytes.Length ? text + "\\0" : text;
    }

    public static string ReadNullTerminatedText(ReadOnlySpan<byte> bytes, Encoding encoding) =>
        TextDecoding.DisplayNullTerminated(bytes, encoding);
}
