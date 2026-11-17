using System.Buffers.Binary;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Fbx;

/// <summary>
/// FBX binary layout constants. Version 7.5 (7500) switched the three node-record lengths to
/// 64-bit values; everything else in the record is unchanged.
/// </summary>
internal static class FbxConstants
{
    /// <summary>File magic without the terminator: "Kaydara FBX Binary  " (two trailing spaces).</summary>
    public static ReadOnlySpan<byte> BinaryMagic => "Kaydara FBX Binary  "u8;

    public const int BinaryMagicLength = 20;
    public const int HeaderLength = 27;

    public const int NullRecordLength32 = 13;
    public const int NullRecordLength64 = 25;

    public const uint WideOffsetVersion = 7500;
    public const uint MinimumDocumentedVersion = 7000;

    /// <summary>Blender's fixed footer code; other exporters use different code bytes.</summary>
    public static ReadOnlySpan<byte> BlenderFooterCode =>
    [
        0xFA, 0xBC, 0xAB, 0x09, 0xD0, 0xC8, 0xD4, 0x66,
        0xB1, 0x76, 0xFB, 0x83, 0x1C, 0xF7, 0x26, 0x7E,
    ];

    /// <summary>Fixed trailer confirmed by independent FBX writers and SDK-produced files.</summary>
    public static ReadOnlySpan<byte> FooterTrailerMagic =>
    [
        0xF8, 0x5A, 0x8C, 0x6A, 0xDE, 0xF5, 0xD9, 0x7E,
        0xEC, 0xE9, 0x0C, 0xE3, 0x75, 0x8F, 0x29, 0x0B,
    ];

    public static bool IsWide(uint version) => version >= WideOffsetVersion;

    public static int RecordHeaderLength(uint version) => IsWide(version) ? 25 : 13;

    public static int NullRecordLength(uint version) => RecordHeaderLength(version);

    public static uint ReadVersion(IRandomAccessSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length < HeaderLength)
        {
            return 0;
        }

        Span<byte> buffer = stackalloc byte[4];
        return SourceRead.ReadAtMost(source, 23, buffer) == buffer.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer)
            : 0;
    }
}
