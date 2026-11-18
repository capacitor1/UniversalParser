using System.Buffers;
using System.Globalization;
using System.Text;

namespace UniversalParser.Core.Formatting;

/// <summary>
/// Shared text decoding policy. Declared encodings are decoded strictly and malformed content is
/// shown as hex; null terminators are preserved as <c>\0</c>; text without a declared encoding is
/// predicted as UTF-8 first and Windows-1252 second.
/// </summary>
internal static class TextDecoding
{
    /// <summary>
    /// Decodes with the declared encoding in strict mode and escapes control characters. When the
    /// byte sequence is invalid for that encoding the raw bytes are shown as <c>\xNN</c> instead
    /// of U+FFFD replacement characters.
    /// </summary>
    public static string Display(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        try
        {
            return TextEscaping.Escape(TextEncodings.Strict(encoding).GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            return Hex(bytes);
        }
    }

    /// <summary>
    /// Like <see cref="Display"/>, but a NUL terminator is preserved as an escaped <c>\0</c>
    /// and bytes after it (padding or the next field) are not shown.
    /// </summary>
    public static string DisplayNullTerminated(
        ReadOnlySpan<byte> bytes,
        Encoding encoding,
        int terminatorSize = 1)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(terminatorSize);
        var index = IndexOfTerminator(bytes, terminatorSize);
        if (index < 0)
        {
            return Display(bytes, encoding);
        }

        return Display(bytes[..index], encoding) + "\\0";
    }

    /// <summary>
    /// Decodes text whose encoding is not declared. Strict UTF-8 is preferred, then Windows-1252;
    /// data that is not plausible text in either encoding is shown as hex.
    /// </summary>
    public static string DisplayPredicted(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        if (TryStrictUtf8(bytes, out var utf8) && IsPlausibleText(utf8))
        {
            return TextEscaping.Escape(utf8);
        }

        var legacy = TextEncodings.Windows1252.GetString(bytes);
        return IsPlausibleText(legacy) ? TextEscaping.Escape(legacy) : Hex(bytes);
    }

    /// <summary>
    /// Predicts the encoding for a NUL-terminated value and keeps the terminator as <c>\0</c>.
    /// </summary>
    public static string DisplayPredictedNullTerminated(ReadOnlySpan<byte> bytes)
    {
        var index = bytes.IndexOf((byte)0);
        if (index < 0)
        {
            return DisplayPredicted(bytes);
        }

        return DisplayPredicted(bytes[..index]) + "\\0";
    }

    /// <summary>Renders every byte as <c>\xNN</c>.</summary>
    public static string Hex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var length = (long)bytes.Length * 4;
        if (length > Array.MaxLength)
        {
            throw new ArgumentException("The hex string would exceed the maximum length.", nameof(bytes));
        }

        return string.Create((int)length, bytes, static (destination, source) =>
        {
            var offset = 0;
            foreach (var value in source)
            {
                destination[offset] = '\\';
                destination[offset + 1] = 'x';
                value.TryFormat(destination[(offset + 2)..], out _, "X2", CultureInfo.InvariantCulture);
                offset += 4;
            }
        });
    }

    /// <summary>Decodes bounded UTF-8 text without misdiagnosing a code point cut by a byte limit.</summary>
    public static string Utf8(ReadOnlySpan<byte> bytes, bool complete, out bool invalidEncoding)
    {
        char[]? rented = null;
        Span<char> characters = bytes.Length <= 256
            ? stackalloc char[bytes.Length]
            : (rented = ArrayPool<char>.Shared.Rent(bytes.Length)).AsSpan(0, bytes.Length);
        invalidEncoding = false;
        try
        {
            int count;
            try
            {
                // A non-final decoder holds an incomplete final code point instead of replacing it.
                count = TextEncodings.StrictUtf8.GetDecoder().GetChars(bytes, characters, flush: complete);
            }
            catch (DecoderFallbackException)
            {
                invalidEncoding = true;
                count = TextEncodings.Utf8.GetDecoder().GetChars(bytes, characters, flush: complete);
            }

            return new string(characters[..count]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    public static string Escaped(ReadOnlySpan<byte> bytes, Encoding encoding) => Display(bytes, encoding);

    private static bool TryStrictUtf8(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = TextEncodings.StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool IsPlausibleText(string text)
    {
        foreach (var character in text)
        {
            if (char.IsControl(character) && character is not '\t' and not '\r' and not '\n')
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOfTerminator(ReadOnlySpan<byte> bytes, int terminatorSize)
    {
        if (terminatorSize == 1)
        {
            return bytes.IndexOf((byte)0);
        }

        for (var index = 0; index + terminatorSize <= bytes.Length; index += terminatorSize)
        {
            if (bytes.Slice(index, terminatorSize).IndexOfAnyExcept((byte)0) < 0)
            {
                return index;
            }
        }

        return -1;
    }
}
