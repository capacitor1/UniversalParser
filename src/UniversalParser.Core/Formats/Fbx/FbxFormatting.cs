using System.Collections.Frozen;
using System.Text.Unicode;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;

namespace UniversalParser.Core.Formats.Fbx;

/// <summary>
/// FBX display helpers. Strings are byte sequences decoded as UTF-8 with replacement; control
/// characters are escaped so a malformed name cannot break the field layout.
/// </summary>
internal static class FbxFormatting
{
    private static readonly FrozenDictionary<char, string> TypeNames = new Dictionary<char, string>()
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
    }.ToFrozenDictionary();

    public static string FormatTypeCode(char code) =>
        TypeNames.TryGetValue(code, out var name) ? $"{code} ({name})" : $"{code} (unknown)";

    public static bool IsKnownTypeCode(char code) => TypeNames.ContainsKey(code);

    public static bool IsValidUtf8(ReadOnlySpan<byte> bytes) => Utf8.IsValid(bytes);

    public static string FormatVersionFormat(uint version) =>
        FbxConstants.IsWide(version)
            ? "64-bit offsets (FBX 7.5 and later)"
            : "32-bit offsets (FBX 7.4 and earlier)";

    public static string FormatText(ReadOnlySpan<byte> utf8Bytes) =>
        TextDecoding.Escaped(utf8Bytes, TextEncodings.Utf8);

    public static string EscapeText(string value) => TextEscaping.Escape(value);

    public static string FormatSingle(float value) =>
        ValueFormat.FloatingPoint(value);

    public static string FormatDouble(double value) =>
        ValueFormat.FloatingPoint(value);

    public static string FormatHex(ReadOnlySpan<byte> bytes, int maxBytes) =>
        HexFormatting.Format(bytes, maxBytes);
}
