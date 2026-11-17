using System.Buffers;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Export;

/// <summary>
/// Copies a complete raw range with bounded memory and cancellation. Raw export is intentionally
/// unlimited: it is the escape hatch that keeps the report's fixed hex window from hiding data.
/// </summary>
public static class RawRangeWriter
{
    public static long Write(Stream target, IRandomAccessSource source, SourceRange range,
        IProgress<ParseProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!new SourceRange(0, source.Length).Contains(range)) throw new ArgumentOutOfRangeException(nameof(range));

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var processed = 0L;
            while (processed < range.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = checked((int)Math.Min(buffer.Length, range.Length - processed));
                var read = SourceRead.ReadAtMost(source, range.Offset + processed, buffer.AsSpan(0, count));
                if (read != count) throw new SourceReadException(range.Offset + processed, count, read);
                target.Write(buffer, 0, read);
                processed += read;
                progress?.Report(new ParseProgress("Exporting raw", processed, range.Length));
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ParseProgress("Done", range.Length, range.Length));
            return processed;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
