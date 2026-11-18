using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formatting;

/// <summary>
/// Printable form for metadata text. Control characters are escaped so one field stays on one
/// display line; file content is never translated or replaced with ASCII lookalikes.
/// </summary>
internal static class TextEscaping
{
    /// <summary>
    /// Preserves each caller's NUL, C1 and quotation-mark policy. A bounded preview stops before
    /// the next input character once its escaped output reaches the limit, then appends an ellipsis.
    /// </summary>
    public static string Escape(
        string value,
        bool escapeNull = true,
        bool escapeAllControls = false,
        bool escapeQuote = false,
        int maxOutputLength = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(maxOutputLength);
        long length = 0;
        var count = value.Length;
        var truncated = false;
        var changed = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (length >= maxOutputLength)
            {
                count = index;
                truncated = true;
                length++;
                break;
            }

            var characterLength = GetLength(value[index], escapeNull, escapeAllControls, escapeQuote);
            length += characterLength;
            changed |= characterLength != 1;
        }

        if (!changed && !truncated)
        {
            return value;
        }

        if (length > Array.MaxLength)
        {
            throw new ArgumentException("The escaped string would exceed the maximum length.", nameof(value));
        }

        return string.Create(
            (int)length,
            (value, count, escapeNull, escapeAllControls, escapeQuote, truncated),
            static (destination, state) =>
            {
                var offset = 0;
                foreach (var character in state.value.AsSpan(0, state.count))
                {
                    var characterLength = GetLength(
                        character, state.escapeNull, state.escapeAllControls, state.escapeQuote);
                    if (characterLength == 1)
                    {
                        destination[offset] = character;
                    }
                    else if (characterLength == 2)
                    {
                        destination[offset] = '\\';
                        destination[offset + 1] = character switch
                        {
                            '\0' => '0',
                            '\r' => 'r',
                            '\n' => 'n',
                            '\t' => 't',
                            _ => '"',
                        };
                    }
                    else
                    {
                        // Unicode Cc characters are all in the single-byte C0/C1 ranges.
                        HexFormatting.WriteEscapedByte((byte)character, destination[offset..]);
                    }

                    offset += characterLength;
                }

                if (state.truncated)
                {
                    destination[offset] = '…';
                }
            });
    }

    private static int GetLength(char character, bool escapeNull, bool escapeAllControls, bool escapeQuote) =>
        character switch
        {
            '\0' when escapeNull => 2,
            '\r' or '\n' or '\t' => 2,
            '"' when escapeQuote => 2,
            _ when (escapeAllControls
                ? char.IsControl(character)
                : character < 0x20 || character == 0x7F) => 4,
            _ => 1,
        };
}
