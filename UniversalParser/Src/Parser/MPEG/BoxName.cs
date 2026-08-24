using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.MPEG
{
    internal class BoxName
    {
        public static string ReadBoxType(Span<byte> buffer, int offset)
        {
            byte b0 = buffer[offset + 0];
            byte b1 = buffer[offset + 1];
            byte b2 = buffer[offset + 2];
            byte b3 = buffer[offset + 3];

            // =========================
            // 1. standard ASCII fast path
            // =========================
            if (IsAscii(b0) && IsAscii(b1) && IsAscii(b2) && IsAscii(b3))
            {
                return new string(new[]
                {
            (char)b0,
            (char)b1,
            (char)b2,
            (char)b3
        });
            }

            // =========================
            // 2. Apple special atom (©xxx)
            // =========================
            if (b0 == 0xA9)
            {
                return "©" + EscapeAscii(b1) + EscapeAscii(b2) + EscapeAscii(b3);
            }

            // =========================
            // 3. fallback: full escape
            // =========================
            return EscapeByte(b0) + EscapeByte(b1) + EscapeByte(b2) + EscapeByte(b3);
        }

        public static bool IsAscii(byte b)
        {
            // printable ASCII range
            return b >= 0x20 && b <= 0x7E;
        }

        public static string EscapeAscii(byte b)
        {
            if (b >= 0x20 && b <= 0x7E)
                return ((char)b).ToString();

            return $"\\x{b:X2}";
        }

        public static string EscapeByte(byte b)
        {
            return $"\\x{b:X2}";
        }
    }
}
