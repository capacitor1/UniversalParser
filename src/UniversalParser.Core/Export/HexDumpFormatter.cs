using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Export;

/// <summary>
/// Classic 16-bytes-per-line hex dump formatting shared by the GUI and the report writer.
/// Line indices and offsets stay 64-bit; only the rendered span is sized in <see cref="int"/>.
/// </summary>
public static class HexDumpFormatter
{
    public const int BytesPerLine = 16;

    /// <summary>Maximum characters a rendered line can occupy.</summary>
    public const int MaxLineLength = 16 + 2 + (BytesPerLine * 3) + 4 + BytesPerLine;

    public static int GetHexColumn(bool wideOffset) => (wideOffset ? 16 : 8) + 2;

    public static int GetAsciiColumn(bool wideOffset) => GetHexColumn(wideOffset) + (BytesPerLine * 3) + 4;

    /// <summary>
    /// Maps a byte span on one line to half-open character-column ranges in the hex and ASCII
    /// fields. Selection painting and tests use these columns, so a highlight can never drift from
    /// the characters rendered by <see cref="RenderLine(IRandomAccessSource, bool, long, long, long)"/>.
    /// </summary>
    public static HexColumnSpan GetColumnSpan(bool wideOffset, int firstByte, int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstByte);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);
        if (firstByte + byteCount > BytesPerLine)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), "The byte span exceeds one hex line.");
        }

        var hex = GetHexColumn(wideOffset);
        var ascii = GetAsciiColumn(wideOffset);
        var hexStart = hex + (firstByte * 3);
        // The last hex cell's trailing space is not part of the highlight.
        var hexEnd = hex + ((firstByte + byteCount) * 3) - 1;
        var asciiStart = ascii + firstByte;
        return new HexColumnSpan(hexStart, hexEnd, asciiStart, asciiStart + byteCount);
    }

    public static string FormatHeader(bool wideOffset)
    {
        const string digits = "0123456789ABCDEF";
        Span<char> header = stackalloc char[MaxLineLength];
        header.Fill(' ');
        var hex = GetHexColumn(wideOffset);
        var ascii = GetAsciiColumn(wideOffset);
        for (var index = 0; index < BytesPerLine; index++)
        {
            header[hex + (index * 3)] = '+';
            header[hex + (index * 3) + 1] = digits[index];
        }

        header[ascii - 3] = '|';
        digits.AsSpan().CopyTo(header[ascii..]);
        return new string(header[..(ascii + BytesPerLine)]);
    }

    public static long GetLineCount(long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return (length / BytesPerLine) + (length % BytesPerLine == 0 ? 0 : 1);
    }

    /// <summary>
    /// Renders one line of a bounded range. Returns the number of characters written, or 0 when the
    /// line starts at or past the end of the range. The read is clamped to the range end so a short
    /// node never shows the bytes of whatever follows it.
    /// </summary>
    public static int RenderLine(
        IRandomAccessSource source,
        bool wideOffset,
        long baseOffset,
        long length,
        long lineIndex,
        Span<char> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(baseOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(lineIndex);
        if (destination.Length < MaxLineLength)
        {
            throw new ArgumentException(
                $"Destination must hold at least {MaxLineLength} characters.",
                nameof(destination));
        }

        if (lineIndex > (long.MaxValue / BytesPerLine))
        {
            return 0;
        }

        var relative = lineIndex * BytesPerLine;
        if (relative >= length)
        {
            return 0;
        }

        if (baseOffset > long.MaxValue - relative)
        {
            return 0;
        }

        var offset = baseOffset + relative;
        if (offset >= source.Length)
        {
            return 0;
        }

        var remaining = length - relative;
        var wanted = (int)Math.Min(BytesPerLine, remaining);
        Span<byte> buffer = stackalloc byte[BytesPerLine];
        var read = SourceRead.ReadAtMost(source, offset, buffer[..wanted]);
        if (read <= 0)
        {
            return 0;
        }

        return RenderLine(wideOffset, offset, buffer[..read], destination);
    }

    /// <summary>
    /// Renders one line from bytes already read by the caller. <paramref name="bytes"/> must contain
    /// at most <see cref="BytesPerLine"/> bytes starting at <paramref name="offset"/>.
    /// </summary>
    public static int RenderLine(
        bool wideOffset,
        long offset,
        ReadOnlySpan<byte> bytes,
        Span<char> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (!wideOffset && offset > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "Offsets above 0xFFFFFFFF require wideOffset formatting.");
        }

        if (bytes.Length > BytesPerLine)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "A hex line cannot exceed 16 bytes.");
        }

        if (destination.Length < MaxLineLength)
        {
            throw new ArgumentException(
                $"Destination must hold at least {MaxLineLength} characters.",
                nameof(destination));
        }

        var read = bytes.Length;
        var position = 0;
        if (wideOffset)
        {
            AppendHex(destination, ref position, (ulong)offset, 16);
        }
        else
        {
            AppendHex(destination, ref position, (uint)offset, 8);
        }

        destination[position++] = ' ';
        destination[position++] = ' ';

        for (var index = 0; index < BytesPerLine; index++)
        {
            if (index < read)
            {
                const string digits = "0123456789ABCDEF";
                destination[position++] = digits[bytes[index] >> 4];
                destination[position++] = digits[bytes[index] & 0xF];
            }
            else
            {
                destination[position++] = ' ';
                destination[position++] = ' ';
            }

            destination[position++] = ' ';
        }

        destination[position++] = ' ';
        destination[position++] = '|';
        destination[position++] = ' ';
        destination[position++] = ' ';

        for (var index = 0; index < read; index++)
        {
            var value = bytes[index];
            destination[position++] = value is >= 32 and <= 126 ? (char)value : '.';
        }

        return position;
    }

    /// <summary>Renders one line into a new string; convenient for tests and small reports.</summary>
    public static string RenderLine(
        IRandomAccessSource source,
        bool wideOffset,
        long baseOffset,
        long length,
        long lineIndex)
    {
        Span<char> buffer = stackalloc char[MaxLineLength];
        var written = RenderLine(source, wideOffset, baseOffset, length, lineIndex, buffer);
        return written == 0 ? string.Empty : new string(buffer[..written]);
    }

    public static string FormatOffset(long offset, bool wide) =>
        wide ? $"0x{offset:X16}" : $"0x{offset:X8}";

    private static void AppendHex(Span<char> destination, ref int position, ulong value, int width)
    {
        const string digits = "0123456789ABCDEF";
        for (var index = width - 1; index >= 0; index--)
        {
            destination[position + index] = digits[(int)(value & 0xF)];
            value >>= 4;
        }

        position += width;
    }
}
