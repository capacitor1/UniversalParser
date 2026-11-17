using System.Globalization;
using System.Text;

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

    public static string FormatFixed1616Signed(uint raw) =>
        FormatFixed(unchecked((int)raw), 65536.0, raw);

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

        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (chars[index] < 0x20 || chars[index] == 0x7F)
            {
                chars[index] = '.';
            }
        }

        return new string(chars);
    }

    public static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            var length = bytes.Length;
            if (length >= 4 && bytes[^1] == 0 && bytes[^2] == 0)
            {
                length -= 2;
            }

            return Encoding.BigEndianUnicode.GetString(bytes[2..length]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            var length = bytes.Length;
            if (length >= 4 && bytes[^1] == 0 && bytes[^2] == 0)
            {
                length -= 2;
            }

            return Encoding.Unicode.GetString(bytes[2..length]);
        }

        var end = bytes.Length;
        if (end >= 1 && bytes[end - 1] == 0)
        {
            end--;
        }

        return Encoding.UTF8.GetString(bytes[..end]);
    }

    public static string ReadNullTerminatedText(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        var end = bytes.IndexOf((byte)0);
        if (end < 0)
        {
            end = bytes.Length;
        }

        return encoding.GetString(bytes[..end]);
    }
}
