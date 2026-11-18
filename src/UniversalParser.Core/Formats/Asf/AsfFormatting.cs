using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Asf;

/// <summary>
/// ASF display helpers. Raw numeric values remain visible; names and derived times are appended
/// inline or emitted as explicit angle-bracket fields.
/// </summary>
internal static class AsfFormatting
{
    private static readonly FrozenDictionary<ushort, string> CodecTypes = new Dictionary<ushort, string>()
    {
        [0x0001] = "video codec",
        [0x0002] = "audio codec",
        [0xFFFF] = "unknown codec",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<ushort, string> ValueTypes = new Dictionary<ushort, string>()
    {
        [0x0000] = "Unicode string",
        [0x0001] = "BYTE array",
        [0x0002] = "BOOL",
        [0x0003] = "DWORD",
        [0x0004] = "QWORD",
        [0x0005] = "WORD",
        [0x0006] = "GUID",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<ushort, string> WaveFormatTags = new Dictionary<ushort, string>()
    {
        [0x0001] = "WAVE_FORMAT_PCM",
        [0x0002] = "WAVE_FORMAT_ADPCM",
        [0x0003] = "WAVE_FORMAT_IEEE_FLOAT",
        [0x0050] = "WAVE_FORMAT_MPEG",
        [0x0055] = "WAVE_FORMAT_MPEGLAYER3",
        [0x0161] = "WAVE_FORMAT_WMAUDIO2",
        [0x0162] = "WAVE_FORMAT_WMAUDIO3",
        [0x0163] = "WAVE_FORMAT_WMAUDIO_LOSSLESS",
        [0x0164] = "WAVE_FORMAT_WMSPEECH",
    }.ToFrozenDictionary();

    public static string FormatGuid(Guid guid) => RawIdentifier.FormatGuid(guid);

    public static string FormatObjectGuid(Guid guid) =>
        AsfGuids.TryGetObjectName(guid, out var name)
            ? $"{FormatGuid(guid)} ({name})"
            : FormatGuid(guid);

    public static string FormatStreamType(Guid guid) =>
        AsfGuids.TryGetStreamTypeName(guid, out var name)
            ? $"{FormatGuid(guid)} ({name})"
            : FormatGuid(guid);

    public static string FormatErrorCorrectionType(Guid guid) =>
        AsfGuids.TryGetErrorCorrectionName(guid, out var name)
            ? $"{FormatGuid(guid)} ({name})"
            : FormatGuid(guid);

    public static string FormatFlags(uint value, params (uint Mask, string Name)[] flags)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"0x{value:X8}");
        var names = new List<string>(flags.Length);
        foreach (var (mask, name) in flags)
        {
            if ((value & mask) != 0)
            {
                names.Add(name);
            }
        }

        return names.Count == 0 ? text : $"{text} ({string.Join(" | ", names)})";
    }

    public static string FormatCodecType(ushort value) => FormatValue(value, CodecTypes);

    public static string FormatStreamPropertiesFlags(ushort value)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"0x{value:X4}");
        var parts = new List<string>(2)
        {
            string.Create(CultureInfo.InvariantCulture, $"Stream Number={value & 0x007F}"),
        };

        if ((value & 0x8000) != 0)
        {
            parts.Add("Encrypted Content Flag");
        }

        return $"{text} ({string.Join(", ", parts)})";
    }

    public static string FormatBitrateRecordFlags(ushort value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"0x{value:X4} (Stream Number={value & 0x007F})");

    public static string FormatValueType(ushort value) => FormatValue(value, ValueTypes);

    public static string FormatWaveFormatTag(ushort value) => FormatValue(value, WaveFormatTags);

    public static string FormatCompression(uint value, ReadOnlySpan<byte> rawBytes)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"0x{value:X8}");
        if (rawBytes.Length != 4)
        {
            return text;
        }

        foreach (var symbol in rawBytes)
        {
            if (symbol is < 0x20 or > 0x7E)
            {
                return text;
            }
        }

        return $"{value} ({text}, '{Encoding.ASCII.GetString(rawBytes)}')";
    }

    public static string FormatText(ReadOnlySpan<byte> utf16Bytes)
    {
        if (utf16Bytes.Length == 0)
        {
            return string.Empty;
        }

        var evenLength = utf16Bytes.Length - (utf16Bytes.Length & 1);
        return TextDecoding.Display(utf16Bytes[..evenLength], Encoding.Unicode);
    }

    public static string EscapeText(string value) => TextEscaping.Escape(value);

    public static string FormatFileTime(ulong fileTime)
    {
        if (fileTime > long.MaxValue)
        {
            return "out of range";
        }

        try
        {
            return DateTimeOffset.FromFileTime((long)fileTime)
                .ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "out of range";
        }
    }

    public static string FormatHundredNanoseconds(ulong value) =>
        (value / 10_000.0).ToString("0.###", CultureInfo.InvariantCulture);

    public static string FormatHex(ReadOnlySpan<byte> bytes, int maxBytes) =>
        HexFormatting.Format(bytes, maxBytes);

    private static string FormatValue(ushort value, IReadOnlyDictionary<ushort, string> names) =>
        names.TryGetValue(value, out var name)
            ? string.Create(CultureInfo.InvariantCulture, $"0x{value:X4} ({name})")
            : string.Create(CultureInfo.InvariantCulture, $"0x{value:X4}");
}
