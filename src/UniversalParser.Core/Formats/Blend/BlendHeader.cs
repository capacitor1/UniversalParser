using System.Globalization;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Blend;

internal enum BlendEndian
{
    Little,
    Big,
}

internal enum BlendHeaderError
{
    None,
    Truncated,
    NotBlend,
    Unsupported,
}

/// <summary>
/// Blender file header. Legacy files use the 12-byte version 0 header; current files use the
/// 17-byte version 1 header (BLO_core_blend_header.hh, "BLEND_FILE_FORMAT_VERSION_1").
/// </summary>
internal readonly record struct BlendHeader(
    int HeaderSize,
    int PointerSize,
    BlendEndian Endian,
    int FileFormatVersion,
    int FileVersion,
    string VersionText);

internal static class BlendHeaderReader
{
    public const int MagicLength = 7;
    public const int LegacyHeaderLength = 12;
    public const int CurrentHeaderLength = 17;

    public static ReadOnlySpan<byte> Magic => "BLENDER"u8;

    public static bool TryRead(
        IRandomAccessSource source,
        out BlendHeader header,
        out BlendHeaderError error)
    {
        ArgumentNullException.ThrowIfNull(source);
        header = default;
        error = BlendHeaderError.NotBlend;

        if (source.Length < MagicLength)
        {
            Span<byte> partial = stackalloc byte[(int)Math.Min(source.Length, MagicLength)];
            var read = SourceRead.ReadAtMost(source, 0, partial);
            error = partial[..read].SequenceEqual(Magic[..read])
                ? BlendHeaderError.Truncated
                : BlendHeaderError.NotBlend;
            return false;
        }

        Span<byte> bytes = stackalloc byte[CurrentHeaderLength];
        var count = (int)Math.Min(source.Length, CurrentHeaderLength);
        if (SourceRead.ReadAtMost(source, 0, bytes[..count]) < MagicLength)
        {
            error = BlendHeaderError.Truncated;
            return false;
        }

        if (!bytes[..MagicLength].SequenceEqual(Magic))
        {
            return false;
        }

        if (count < LegacyHeaderLength)
        {
            error = BlendHeaderError.Truncated;
            return false;
        }

        var pointerByte = bytes[7];
        if (pointerByte == (byte)'_' || pointerByte == (byte)'-')
        {
            var pointerSize = pointerByte == (byte)'_' ? 4 : 8;
            var endian = bytes[8] switch
            {
                (byte)'v' => BlendEndian.Little,
                (byte)'V' => BlendEndian.Big,
                _ => (BlendEndian?)null,
            };
            if (endian is null || !IsAsciiDigits(bytes[9..12]))
            {
                error = BlendHeaderError.Unsupported;
                return false;
            }

            var versionText = System.Text.Encoding.ASCII.GetString(bytes[9..12]);
            header = new BlendHeader(
                LegacyHeaderLength,
                pointerSize,
                endian.Value,
                0,
                int.Parse(versionText, CultureInfo.InvariantCulture),
                versionText);
            error = BlendHeaderError.None;
            return true;
        }

        if (count < CurrentHeaderLength)
        {
            error = BlendHeaderError.Truncated;
            return false;
        }

        if (bytes[7] != (byte)'1'
            || bytes[8] != (byte)'7'
            || bytes[9] != (byte)'-'
            || !IsAsciiDigits(bytes[10..12])
            || bytes[12] != (byte)'v'
            || !IsAsciiDigits(bytes[13..17]))
        {
            error = BlendHeaderError.Unsupported;
            return false;
        }

        if (bytes[10] != (byte)'0' || bytes[11] != (byte)'1')
        {
            error = BlendHeaderError.Unsupported;
            return false;
        }

        var currentVersionText = System.Text.Encoding.ASCII.GetString(bytes[13..17]);
        header = new BlendHeader(
            CurrentHeaderLength,
            8,
            BlendEndian.Little,
            1,
            int.Parse(currentVersionText, CultureInfo.InvariantCulture),
            currentVersionText);
        error = BlendHeaderError.None;
        return true;
    }

    private static bool IsAsciiDigits(ReadOnlySpan<byte> value)
    {
        foreach (var item in value)
        {
            if (item is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }
}
