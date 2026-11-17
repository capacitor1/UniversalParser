namespace UniversalParser.Core.Formats.Embedded;

/// <summary>
/// Reverses ID3 unsynchronisation: a 0x00 inserted after 0xFF before 0x00 or 0xE0..0xFF is removed.
/// The scan is single pass and allocates only when an inserted byte exists.
/// </summary>
internal static class Id3Unsynchronisation
{
    public static byte[] Restore(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty)
        {
            return [];
        }

        var removeCount = 0;
        for (var index = 1; index < input.Length - 1; index++)
        {
            if (input[index - 1] == 0xFF
                && input[index] == 0x00
                && (input[index + 1] == 0x00 || (input[index + 1] & 0xE0) == 0xE0))
            {
                removeCount++;
                index++;
            }
        }

        if (removeCount == 0)
        {
            return input.ToArray();
        }

        var result = new byte[input.Length - removeCount];
        var output = 0;
        for (var index = 0; index < input.Length; index++)
        {
            if (index + 1 < input.Length
                && input[index] == 0xFF
                && input[index + 1] == 0x00
                && (index + 2 >= input.Length
                    || input[index + 2] == 0x00
                    || (input[index + 2] & 0xE0) == 0xE0))
            {
                result[output++] = input[index++];
                continue;
            }

            result[output++] = input[index];
        }

        return result;
    }
}
