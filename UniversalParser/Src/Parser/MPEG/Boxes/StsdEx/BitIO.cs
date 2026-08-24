using System;

namespace UniversalParser.Src.Parser.MPEG.Boxes.StsdEx
{
    internal ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _d;
        private int _bit;
        public bool Bad;

        public BitReader(ReadOnlySpan<byte> data) { _d = data; _bit = 0; Bad = false; }

        public int BitsLeft => Math.Max(0, _d.Length * 8 - _bit);

        public uint U(int n)
        {
            if (n <= 0) return 0;
            if (n > 32 || BitsLeft < n) { Bad = true; return 0; }
            uint v = 0;
            for (int i = 0; i < n; i++)
            {
                v = (v << 1) | (uint)((_d[_bit >> 3] >> (7 - (_bit & 7))) & 1);
                _bit++;
            }
            return v;
        }

        public bool Flag() => U(1) != 0;

        public void Skip(int n)
        {
            if (BitsLeft < n) { Bad = true; _bit = _d.Length * 8; }
            else _bit += n;
        }

        /// <summary>ue(v) — unsigned Exp-Golomb.</summary>
        public uint UE()
        {
            int zeros = 0;
            while (true)
            {
                if (BitsLeft == 0) { Bad = true; return 0; }
                if (U(1) != 0) break;
                if (++zeros > 31) { Bad = true; return 0; }
            }
            if (zeros == 0) return 0;
            return (1u << zeros) - 1 + U(zeros);
        }

        /// <summary>se(v) — signed Exp-Golomb.</summary>
        public int SE()
        {
            uint k = UE();
            return (k & 1) != 0 ? (int)((k + 1) >> 1) : -(int)(k >> 1);
        }
    }

    internal static class BitIO
    {
        /// <summary>Remove emulation_prevention_three_byte: 00 00 03 -> 00 00 (NAL -> RBSP).</summary>
        public static byte[] Unescape(ReadOnlySpan<byte> nal)
        {
            var outBuf = new byte[nal.Length];
            int n = 0, zeros = 0;
            for (int i = 0; i < nal.Length; i++)
            {
                byte b = nal[i];
                if (zeros >= 2 && b == 0x03) { zeros = 0; continue; }
                outBuf[n++] = b;
                zeros = b == 0 ? zeros + 1 : 0;
            }
            Array.Resize(ref outBuf, n);
            return outBuf;
        }
    }
}