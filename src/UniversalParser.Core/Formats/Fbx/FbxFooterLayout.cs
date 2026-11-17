using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Fbx;

/// <summary>
/// Observed binary FBX footer layout. There is no public Autodesk footer specification; this shape
/// is recognised using the fixed trailer signature and bounded zero-filled framing before any field is
/// exposed. See docs/02-product-decisions.md D-029.
/// </summary>
internal readonly record struct FbxFooterLayout(
    SourceRange NullRecord,
    SourceRange Id,
    SourceRange Padding,
    SourceRange Version,
    SourceRange ZeroFill,
    SourceRange Trailer)
{
    private const int IdLength = 16;
    private const int ZeroFillLength = 120;

    public static bool TryRead(
        IRandomAccessSource source,
        long start,
        long end,
        int nullRecordLength,
        out FbxFooterLayout layout)
    {
        ArgumentNullException.ThrowIfNull(source);
        layout = default;
        if (start < 0 || end > source.Length || end < start
            || end - start < nullRecordLength + IdLength + 4 + 4 + ZeroFillLength + 16)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[128];
        if (!ReadAllZero(source, start, nullRecordLength))
        {
            return false;
        }

        var idStart = start + nullRecordLength;
        var idEnd = idStart + IdLength;
        // SDK and Blender put alignment padding on different sides of the mandatory four zeros.
        // The fixed-size trailer identifies the version field without guessing either alignment.
        var versionOffset = end - (4 + ZeroFillLength + 16);
        var paddingLength = versionOffset - idEnd;
        if (paddingLength is < 4 or > 20 || !ReadAllZero(source, idEnd, checked((int)paddingLength)))
        {
            return false;
        }

        var zeroStart = versionOffset + 4;
        if (!ReadAllZero(source, zeroStart, ZeroFillLength))
        {
            return false;
        }

        var trailerStart = zeroStart + ZeroFillLength;
        if (SourceRead.ReadAtMost(source, trailerStart, buffer[..16]) != 16
            || !buffer[..16].SequenceEqual(FbxConstants.FooterTrailerMagic))
        {
            return false;
        }

        layout = new FbxFooterLayout(
            new SourceRange(start, nullRecordLength),
            new SourceRange(idStart, IdLength),
            new SourceRange(idEnd, paddingLength),
            new SourceRange(versionOffset, 4),
            new SourceRange(zeroStart, ZeroFillLength),
            new SourceRange(trailerStart, 16));
        return true;
    }

    private static bool ReadAllZero(IRandomAccessSource source, long offset, int count)
    {
        Span<byte> buffer = stackalloc byte[128];
        if (count > buffer.Length || SourceRead.ReadAtMost(source, offset, buffer[..count]) != count)
        {
            return false;
        }

        return buffer[..count].IndexOfAnyExcept((byte)0) < 0;
    }

}
