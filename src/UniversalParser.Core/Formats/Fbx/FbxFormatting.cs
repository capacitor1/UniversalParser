using System.Globalization;
using System.Text;
using System.Text.Unicode;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Fbx;

/// <summary>
/// FBX display helpers. Strings are byte sequences decoded as UTF-8 with replacement; control
/// characters are escaped so a malformed name cannot break the field layout.
/// </summary>
internal static class FbxFormatting
{
    private static readonly Dictionary<char, string> TypeNames = new()
    {
        ['Y'] = "int16",
        ['C'] = "bool",
        ['I'] = "int32",
        ['F'] = "float",
        ['D'] = "double",
        ['L'] = "int64",
        ['S'] = "string",
        ['R'] = "raw",
        ['f'] = "float array",
        ['d'] = "double array",
        ['l'] = "int64 array",
        ['i'] = "int32 array",
        ['b'] = "bool array",
    };

    public static string FormatTypeCode(char code) =>
        TypeNames.TryGetValue(code, out var name) ? $"{code} ({name})" : $"{code} (unknown)";

    public static bool IsKnownTypeCode(char code) => TypeNames.ContainsKey(code);

    public static bool IsValidUtf8(ReadOnlySpan<byte> bytes) => Utf8.IsValid(bytes);

    public static string FormatVersionFormat(uint version) =>
        FbxConstants.IsWide(version)
            ? "64-bit offsets (FBX 7.5 and later)"
            : "32-bit offsets (FBX 7.4 and earlier)";

    public static string FormatText(ReadOnlySpan<byte> utf8Bytes)
    {
        if (utf8Bytes.IsEmpty)
        {
            return string.Empty;
        }

        return EscapeText(Encoding.UTF8.GetString(utf8Bytes));
    }

    public static string EscapeText(string value)
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

    public static string FormatSingle(float value) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);

    public static string FormatDouble(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);

    public static string FormatHex(ReadOnlySpan<byte> bytes, int maxBytes) =>
        HexFormatting.Format(bytes, maxBytes);
}
