using System;
using System.IO;
using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;
using UniversalParser.Src.Parser;

public static class PNGCRCValidator
{
    public static string Validate(
        Stream baseStream,
        long chunkOffset,
        uint len)
    {
        // 1. read file CRC (always needed for reporting)
        byte[] crcBytes = new byte[4];

        baseStream.Position = chunkOffset + 8 + len;
        baseStream.ReadExactly(crcBytes, 0, 4);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(crcBytes);

        uint fileCrc = BitConverter.ToUInt32(crcBytes, 0);

        if (!Settings.CheckPNGCrc32)
            return $"0x{fileCrc:X8}";

        // 2. CRC stream: type + data
        var crc32 = new Crc32();

        using var region = new OffsetStream(
            baseStream,
            chunkOffset + 4,
            4 + len);

        byte[] buffer = new byte[256 * 1024];

        while (true)
        {
            int read = region.Read(buffer,0, buffer.Length);

            if (read <= 0)
                break;

            crc32.Append(buffer.AsSpan(0, read));
        }

        uint realCrc = BitConverter.ToUInt32(crc32.GetCurrentHash(), 0);

        return realCrc == fileCrc
            ? $"0x{fileCrc:X8}"
            : $"0x{fileCrc:X8} (Corrupted in file: actual 0x{realCrc:X8})";
    }
}