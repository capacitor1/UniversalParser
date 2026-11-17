namespace UniversalParser.Core.Binary;

/// <summary>Bounded helper reads shared by format parsers.</summary>
public static class SourceRead
{
    /// <summary>
    /// Fills up to <paramref name="buffer"/>.Length bytes at <paramref name="offset"/>.
    /// Returns the number of bytes actually read; never reads past the source end.
    /// </summary>
    public static int ReadAtMost(IRandomAccessSource source, long offset, Span<byte> buffer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var total = 0;
        var position = offset;
        while (total < buffer.Length && position < source.Length)
        {
            var read = source.Read(position, buffer[total..]);
            if (read <= 0)
            {
                break;
            }

            // Defensive: clamp a faulty source that reports more than the buffer or file can hold.
            var available = Math.Max(0, source.Length - position);
            var count = (int)Math.Min(read, Math.Min(buffer.Length - total, available));
            if (count <= 0)
            {
                break;
            }

            total += count;
            position += count;
            if (count < read)
            {
                break;
            }
        }

        return total;
    }
}
