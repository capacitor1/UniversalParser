using System;

namespace UniversalParser.Src.Parser.ID3
{
    internal static class ID3Number
    {
        public static int UIntBE(ReadOnlySpan<byte> data)
        {
            int value = 0;

            for (int i = 0; i < data.Length && i < 4; i++)
                value = (value << 8) | data[i];

            return value;
        }

        public static int SyncSafe32(ReadOnlySpan<byte> data)
        {
            int value = 0;

            for (int i = 0; i < data.Length && i < 4; i++)
                value = (value << 7) | (data[i] & 0x7F);

            return value;
        }

        public static int SyncSafe(byte[] data)
        {
            return SyncSafe32(data);
        }

        public static bool IsSyncSafe(ReadOnlySpan<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if ((data[i] & 0x80) != 0)
                    return false;
            }

            return true;
        }

        public static int ToLength(int value)
        {
            return value < 0 ? 0 : value;
        }
    }
}