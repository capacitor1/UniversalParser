using System.Collections.Concurrent;
using System.Text;

namespace UniversalParser.Core.Formatting;

/// <summary>
/// Process-wide encoding instances shared by parsers, the exporter and the GUI. Reusing one
/// instance per policy keeps decoder/encoder configuration identical everywhere instead of
/// re-creating <see cref="UTF8Encoding"/> objects at each call site.
/// </summary>
public static class TextEncodings
{
    /// <summary>UTF-8 without a byte-order mark; malformed input is replaced.</summary>
    public static Encoding Utf8 { get; } = new UTF8Encoding(false, false);

    /// <summary>UTF-8 without a byte-order mark; malformed input throws.</summary>
    public static Encoding StrictUtf8 { get; } = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>UTF-16 little-endian without a byte-order mark; malformed input throws.</summary>
    public static Encoding StrictUtf16Le { get; } = new UnicodeEncoding(false, false, throwOnInvalidBytes: true);

    /// <summary>UTF-16 big-endian without a byte-order mark; malformed input throws.</summary>
    public static Encoding StrictUtf16Be { get; } = new UnicodeEncoding(true, false, throwOnInvalidBytes: true);

    /// <summary>ISO-8859-1; every byte maps to the code point with the same value.</summary>
    public static Encoding Latin1 { get; } = Encoding.Latin1;

    /// <summary>Windows-1252, the usual fallback for legacy 8-bit metadata.</summary>
    public static Encoding Windows1252 { get; } =
        CodePagesEncodingProvider.Instance.GetEncoding(1252)
        ?? throw new InvalidOperationException("Windows-1252 is not available.");

    /// <summary>7-bit ASCII; non-ASCII bytes decode to '?'.</summary>
    public static Encoding Ascii { get; } = Encoding.ASCII;

    private static readonly ConcurrentDictionary<int, Encoding> StrictByCodePage = new();

    /// <summary>
    /// Strict variant of an existing encoding: malformed byte sequences throw instead of being
    /// replaced, so the caller can show the raw bytes as hex instead of U+FFFD.
    /// </summary>
    public static Encoding Strict(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.CodePage switch
        {
            65001 => StrictUtf8,
            1200 => StrictUtf16Le,
            1201 => StrictUtf16Be,
            28591 => Latin1, // ISO-8859-1 defines every byte value.
            1252 => Windows1252, // Windows-1252 also maps every byte value in .NET.
            _ => StrictByCodePage.GetOrAdd(
                encoding.CodePage,
                static codePage => Encoding.GetEncoding(
                    codePage,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback)),
        };
    }
}
