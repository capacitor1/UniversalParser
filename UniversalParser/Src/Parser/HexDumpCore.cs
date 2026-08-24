using System;
using System.IO;

internal static class HexDumpCore
{
    private const int BytesPerLine = 16;

    public static int GetLineCount(long length)
        => (int)((length + BytesPerLine - 1) / BytesPerLine);

    /// <summary>
    /// 关键修正：
    /// baseOffset = 外部传入的全局偏移
    /// lineOffset = 当前行偏移
    /// </summary>
    public static void RenderLine(
        Stream stream,
        bool is64,
        long baseOffset,
        long lineIndex,
        Span<char> output,
        out int len)
    {
        byte[] buffer = new byte[BytesPerLine];

        long offset = lineIndex * BytesPerLine;

        long oldPos = stream.Position;
        stream.Position = offset;

        int read = stream.Read(buffer, 0, BytesPerLine);

        stream.Position = oldPos;

        int pos = 0;
        //
        if(read == 0)
        {
            len = 0;
            return;
        }
        // =========================
        // Offset（使用全局 offset）
        // =========================
        long goffset = baseOffset + offset;
        if (is64)
            AppendHex16(output, ref pos, goffset);
        else
            AppendHex8(output, ref pos, (uint)goffset);

        output[pos++] = ' ';
        output[pos++] = ' ';

        // =========================
        // Hex
        // =========================
        for (int i = 0; i < BytesPerLine; i++)
        {
            if (i < read)
                AppendByte(output, ref pos, buffer[i]);
            else
            {
                output[pos++] = ' ';
                output[pos++] = ' ';
            }

            output[pos++] = ' ';
        }

        output[pos++] = ' ';
        output[pos++] = '|';
        output[pos++] = ' ';
        output[pos++] = ' ';

        // =========================
        // ASCII
        // =========================
        for (int i = 0; i < read; i++)
        {
            byte b = buffer[i];
            output[pos++] = (b >= 32 && b <= 126) ? (char)b : '.';
        }

        len = pos;
    }

    private static void AppendHex8(Span<char> dst, ref int pos, uint value)
    {
        string hex = value.ToString("X8");
        hex.AsSpan().CopyTo(dst[pos..]);
        pos += 8;
    }
    private static void AppendHex16(Span<char> dst, ref int pos, long value)
    {
        string hex = value.ToString("X16");
        hex.AsSpan().CopyTo(dst[pos..]);
        pos += 16;
    }

    private static void AppendByte(Span<char> dst, ref int pos, byte b)
    {
        const string hex = "0123456789ABCDEF";
        dst[pos++] = hex[b >> 4];
        dst[pos++] = hex[b & 0xF];
    }
}