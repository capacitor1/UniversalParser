using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Ebml;

/// <summary>EBML value formatting helpers. Text decoding never throws on malformed bytes.</summary>
internal static class EbmlFormatting
{
    public const int MaxInlineBinaryBytes = 256;

    private static readonly UTF8Encoding Utf8 = new(false, throwOnInvalidBytes: false);
    private static readonly DateTimeOffset EbmlEpoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<ulong, string> TrackTypes = new()
    {
        [0x01] = "video",
        [0x02] = "audio",
        [0x03] = "complex",
        [0x04] = "logo",
        [0x05] = "subtitle",
        [0x06] = "button",
        [0x07] = "control",
        [0x08] = "metadata",
    };

    public static string FormatId(ulong id) => $"0x{id:X}";

    public static ulong ReadUnsigned(ReadOnlySpan<byte> data)
    {
        ulong value = 0;
        foreach (var next in data)
        {
            value = (value << 8) | next;
        }

        return value;
    }

    public static long ReadSigned(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        long value = (sbyte)data[0];
        for (var index = 1; index < data.Length; index++)
        {
            value = (value << 8) | data[index];
        }

        return value;
    }

    public static float ReadSingle(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadSingleBigEndian(data);

    public static double ReadDouble(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadDoubleBigEndian(data);

    public static string FormatFloat(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static string FormatFloat(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static string FormatDate(long nanoseconds)
    {
        var ticks = nanoseconds / 100;
        if (nanoseconds < 0 && nanoseconds % 100 != 0)
        {
            ticks--; // floor division for sub-tick negative values
        }

        try
        {
            return EbmlEpoch.AddTicks(ticks).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "out of range";
        }
    }

    public static string DecodeText(ReadOnlySpan<byte> data)
    {
        var end = data.Length;
        while (end > 0 && data[end - 1] == 0)
        {
            end--;
        }

        return Utf8.GetString(data[..end]);
    }

    public static string FormatHex(ReadOnlySpan<byte> data) => HexFormatting.Format(data, MaxInlineBinaryBytes);

    public static bool TryGetTrackType(ulong value, out string? name) => TrackTypes.TryGetValue(value, out name);
}
