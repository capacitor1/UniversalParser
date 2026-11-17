namespace UniversalParser.Core.Formats.Embedded;

/// <summary>ID3v2 synchsafe and big-endian integer helpers.</summary>
internal static class Id3Number
{
    public static int UIntBe(ReadOnlySpan<byte> data)
    {
        var value = 0;
        for (var index = 0; index < data.Length && index < 4; index++)
        {
            value = (value << 8) | data[index];
        }

        return value;
    }

    public static int SyncSafe32(ReadOnlySpan<byte> data)
    {
        var value = 0;
        for (var index = 0; index < data.Length && index < 4; index++)
        {
            value = (value << 7) | (data[index] & 0x7F);
        }

        return value;
    }

    public static bool IsSyncSafe(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if ((value & 0x80) != 0)
            {
                return false;
            }
        }

        return true;
    }

    public static int ToLength(int value) => value < 0 ? 0 : value;
}
