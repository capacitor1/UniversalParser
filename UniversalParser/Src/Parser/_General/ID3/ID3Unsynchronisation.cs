using System;

namespace UniversalParser.Src.Parser.ID3
{
    internal static class ID3Unsynchronisation
    {
        public static byte[] Restore(ReadOnlySpan<byte> input)
        {
            if (input.IsEmpty)
                return Array.Empty<byte>();

            int removeCount = 0;

            for (int i = 1; i < input.Length - 1; i++)
            {
                if (input[i - 1] == 0xFF &&
                    input[i] == 0x00 &&
                    (input[i + 1] == 0x00 ||
                     (input[i + 1] & 0xE0) == 0xE0))
                {
                    removeCount++;
                    i++;
                }
            }

            if (removeCount == 0)
                return input.ToArray();

            byte[] result = new byte[input.Length - removeCount];
            int output = 0;

            for (int i = 0; i < input.Length; i++)
            {
                if (i + 1 < input.Length &&
                    input[i] == 0xFF &&
                    input[i + 1] == 0x00 &&
                    (i + 2 >= input.Length ||
                     input[i + 2] == 0x00 ||
                     (input[i + 2] & 0xE0) == 0xE0))
                {
                    result[output++] = input[i++];
                    continue;
                }

                result[output++] = input[i];
            }

            return result;
        }
    }
}