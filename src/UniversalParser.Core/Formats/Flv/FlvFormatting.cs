using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Flv;

/// <summary>
/// FLV display helpers. Raw numeric values stay visible; enum meanings are appended inline
/// in parentheses so the original byte is never replaced by a human-readable label.
/// </summary>
internal static class FlvFormatting
{
    private static readonly FrozenDictionary<int, string> TagTypes = new Dictionary<int, string>()
    {
        [8] = "audio",
        [9] = "video",
        [18] = "script",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> CodecIds = new Dictionary<int, string>()
    {
        [1] = "JPEG",
        [2] = "Sorenson H.263",
        [3] = "Screen video",
        [4] = "On2 VP6",
        [5] = "On2 VP6 with alpha channel",
        [6] = "Screen video version 2",
        [7] = "AVC",
        [8] = "Real H.263",
        [9] = "MPEG-4",
        [12] = "HEVC",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> SoundFormats = new Dictionary<int, string>()
    {
        [0] = "Linear PCM, platform-endian",
        [1] = "ADPCM",
        [2] = "MP3",
        [3] = "Linear PCM, little-endian",
        [4] = "Nellymoser 16 kHz mono",
        [5] = "Nellymoser 8 kHz mono",
        [6] = "Nellymoser",
        [7] = "G.711 A-law logarithmic PCM",
        [8] = "G.711 mu-law logarithmic PCM",
        [10] = "AAC",
        [11] = "Speex",
        [14] = "MP3 8 kHz",
        [15] = "Device-specific sound",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> SoundRates = new Dictionary<int, string>()
    {
        [0] = "5.5 kHz",
        [1] = "11 kHz",
        [2] = "22 kHz",
        [3] = "44 kHz",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> FrameTypes = new Dictionary<int, string>()
    {
        [1] = "key frame",
        [2] = "inter frame",
        [3] = "disposable inter frame",
        [4] = "generated key frame",
        [5] = "video info/command frame",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> AvcPacketTypes = new Dictionary<int, string>()
    {
        [0] = "AVC sequence header",
        [1] = "AVC NALU",
        [2] = "AVC end of sequence",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> HevcPacketTypes = new Dictionary<int, string>()
    {
        [0] = "HEVC sequence header",
        [1] = "HEVC NALU",
        [2] = "HEVC end of sequence",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> AacPacketTypes = new Dictionary<int, string>()
    {
        [0] = "AAC sequence header",
        [1] = "AAC raw",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> VideoPacketTypes = new Dictionary<int, string>()
    {
        [0] = "SequenceStart",
        [1] = "CodedFrames",
        [2] = "SequenceEnd",
        [3] = "CodedFramesX",
        [4] = "Metadata",
        [5] = "MPEG2TSSequenceStart",
        [6] = "Multitrack",
        [7] = "ModEx",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> VideoCommands = new Dictionary<int, string>()
    {
        [0] = "StartSeek",
        [1] = "EndSeek",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> MultitrackTypes = new Dictionary<int, string>()
    {
        [0] = "OneTrack",
        [1] = "ManyTracks",
        [2] = "ManyTracksManyCodecs",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> FourCcNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["avc1"] = "AVC",
        ["hvc1"] = "HEVC",
        ["hev1"] = "HEVC",
        ["av01"] = "AV1",
        ["vp08"] = "VP8",
        ["vp09"] = "VP9",
        ["vvc1"] = "VVC",
    }.ToFrozenDictionary();

    public static string FormatTagType(int value) => FormatValue(value, TagTypes);

    public static string FormatCodecId(int value) => FormatValue(value, CodecIds);

    public static string FormatSoundFormat(int value) => FormatValue(value, SoundFormats);

    public static string FormatSoundRate(int value) => FormatValue(value, SoundRates);

    public static string FormatSoundSize(int value) => value == 0 ? "0 (8-bit samples)" : "1 (16-bit samples)";

    public static string FormatSoundType(int value) => value == 0 ? "0 (mono)" : "1 (stereo)";

    public static string FormatFrameType(int value) => FormatValue(value, FrameTypes);

    public static string FormatAvcPacketType(int value) => FormatValue(value, AvcPacketTypes);

    public static string FormatHevcPacketType(int value) => FormatValue(value, HevcPacketTypes);

    public static string FormatAacPacketType(int value) => FormatValue(value, AacPacketTypes);

    public static string FormatVideoPacketType(int value) => FormatValue(value, VideoPacketTypes);

    public static string FormatVideoCommand(int value) => FormatValue(value, VideoCommands);

    public static string FormatMultitrackType(int value) => FormatValue(value, MultitrackTypes);

    public static string FormatFourCc(ReadOnlySpan<byte> bytes)
    {
        var text = DecodeFourCc(bytes);
        var formatted = RawIdentifier.FormatBytes(bytes);
        return FourCcNames.TryGetValue(text, out var name) ? $"{formatted} ({name})" : formatted;
    }

    public static string FormatFlags(byte value)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"0x{value:X2}");
        var names = new List<string>(2);
        if ((value & 0x01) != 0)
        {
            names.Add("audio");
        }

        if ((value & 0x04) != 0)
        {
            names.Add("video");
        }

        return names.Count == 0 ? text : $"{text} ({string.Join(" | ", names)})";
    }

    public static string DecodeFourCc(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    public static string FormatNumber(double value) => ValueFormat.FloatingPoint(value);

    public static string FormatDateMilliseconds(double value)
    {
        if (!double.IsFinite(value) || value < long.MinValue || value > long.MaxValue)
        {
            return "out of range";
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Truncate(value))
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "out of range";
        }
    }

    public static string FormatSigned24(uint raw)
    {
        var signed = (raw & 0x800000) == 0
            ? (int)raw
            : unchecked((int)(raw | 0xFF000000));
        return signed.ToString(CultureInfo.InvariantCulture);
    }

    public static string EscapeText(string value) => TextEscaping.Escape(value, escapeNull: false);

    public static string SanitizeKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
        {
            return "_";
        }

        var escaped = EscapeText(key);
        return escaped.StartsWith('<') ? "_" + escaped : escaped;
    }

    public static string FormatHex(ReadOnlySpan<byte> bytes, int maxBytes) => HexFormatting.Format(bytes, maxBytes);

    private static string FormatValue(int value, IReadOnlyDictionary<int, string> names) =>
        ValueFormat.Enum(value, names);
}
