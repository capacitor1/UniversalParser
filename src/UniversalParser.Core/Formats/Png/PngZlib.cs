using System.IO.Compression;

namespace UniversalParser.Core.Formats.Png;

/// <summary>
/// Bounded zlib decompression for the small metadata payloads allowed by decision D-011
/// (iCCP, zTXt, compressed iTXt). The output is capped by ParseLimits, so a malicious
/// deflate stream cannot expand without bound. Large image/frame payloads are never passed here.
/// </summary>
internal static class PngZlib
{
    private const int BufferSize = 16 * 1024;

    public static bool TryDecompress(
        ReadOnlySpan<byte> compressed,
        int maxOutputBytes,
        CancellationToken cancellationToken,
        out byte[] output,
        out bool exceeded)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputBytes);

        output = [];
        exceeded = false;
        if (compressed.IsEmpty)
        {
            return false;
        }

        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var result = new MemoryStream();
            var buffer = new byte[BufferSize];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = zlib.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                if (result.Length + read > maxOutputBytes)
                {
                    var allowed = maxOutputBytes - (int)result.Length;
                    if (allowed > 0)
                    {
                        result.Write(buffer, 0, allowed);
                    }

                    exceeded = true;
                    break;
                }

                result.Write(buffer, 0, read);
            }

            output = result.ToArray();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
