using System.Text;

namespace UniversalParser.Core.Formatting;

/// <summary>Decodes bounded UTF-8 text without misdiagnosing a code point cut by a byte limit.</summary>
internal static class TextDecoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    public static string Utf8(ReadOnlySpan<byte> bytes, bool complete, out bool invalidEncoding)
    {
        var characters = new char[bytes.Length];
        invalidEncoding = false;
        int count;
        try
        {
            // A non-final decoder holds an incomplete final code point instead of replacing it.
            count = StrictUtf8.GetDecoder().GetChars(bytes, characters, flush: complete);
        }
        catch (DecoderFallbackException)
        {
            invalidEncoding = true;
            count = Encoding.UTF8.GetDecoder().GetChars(bytes, characters, flush: complete);
        }
        return new string(characters, 0, count);
    }
}
