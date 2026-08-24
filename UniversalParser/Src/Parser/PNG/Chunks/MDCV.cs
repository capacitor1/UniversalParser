namespace UniversalParser.Src.Parser.PNG.Chunks
{
    /// <summary>
    /// mDCV - Mastering Display Color Volume (SMPTE ST 2086)
    /// PNG 3rd ed. 11.3.2.7 | type = 6D 44 43 56 | payload 固定 24 字节
    /// 注意：原色顺序为 R,G,B（不是 ST 2086 / HEVC SEI 的 G,B,R）。
    /// </summary>
    internal static class MDCV
    {
        private const double ChromaScale = 0.00002; // raw * 0.00002 = x or y
        private const double LumScale    = 0.0001;  // raw * 0.0001  = cd/m2

        public static ParseResult Parse(PNGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var fs = parser.FileStream;
            var reader = new PngReader(fs);

            long pos = (long)node.Position;
            reader.Seek(pos);

            uint length = reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            if (type != "mDCV")
                throw new InvalidDataException($"Expected mDCV but got '{type}'");

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult)
            };

            if (length != 24)
            {
                dataLines.Add(("<Error>", $"mDCV payload must be exactly 24 bytes, got {length}"));
            }
            else
            {
                reader.Seek(pos + 8);

                ushort rx = reader.ReadUInt16BE(), ry = reader.ReadUInt16BE();
                ushort gx = reader.ReadUInt16BE(), gy = reader.ReadUInt16BE();
                ushort bx = reader.ReadUInt16BE(), by = reader.ReadUInt16BE();
                ushort wx = reader.ReadUInt16BE(), wy = reader.ReadUInt16BE();
                uint maxLum = reader.ReadUInt32BE();
                uint minLum = reader.ReadUInt32BE();

                dataLines.Add(("Primary.Red",   Chroma(rx, ry)));
                dataLines.Add(("Primary.Green", Chroma(gx, gy)));
                dataLines.Add(("Primary.Blue",  Chroma(bx, by)));
                dataLines.Add(("WhitePoint",    Chroma(wx, wy)));
                dataLines.Add(("Luminance.Max", $"{maxLum * LumScale:0.####} cd/m2 (raw {maxLum})"));
                dataLines.Add(("Luminance.Min", $"{minLum * LumScale:0.####} cd/m2 (raw {minLum})"));

                if (MatchGamut(rx, ry, gx, gy, bx, by) is { } gamut)
                    dataLines.Add(("Gamut", gamut));
                if (MatchWhitePoint(wx, wy) is { } wp)
                    dataLines.Add(("Illuminant", wp));

                foreach (var (n, v) in new (string, ushort)[]
                {
                    ("Red.x", rx), ("Red.y", ry), ("Green.x", gx), ("Green.y", gy),
                    ("Blue.x", bx), ("Blue.y", by), ("White.x", wx), ("White.y", wy)
                })
                {
                    if (v > 50000)
                        dataLines.Add(("<Warning>", $"{n} = {v} exceeds 50000 (chromaticity > 1.0)"));
                }

                if (maxLum != 0 && minLum != 0 && maxLum <= minLum)
                    dataLines.Add(("<Warning>", "Luminance.Max should be greater than Luminance.Min"));
                if ((rx | ry | gx | gy | bx | by) == 0)
                    dataLines.Add(("<Warning>", "All primaries are zero - chunk carries no information"));
            }

            return new ParseResult
            {
                Title = "ColorSpace 'mDCV'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }

        private static string Chroma(ushort x, ushort y)
            => $"x={x * ChromaScale:0.#####}, y={y * ChromaScale:0.#####}  (raw {x}, {y})";

        private static bool Near(ushort v, int target, int tol = 150) => Math.Abs(v - target) <= tol;

        private static string? MatchGamut(ushort rx, ushort ry, ushort gx, ushort gy, ushort bx, ushort by)
        {
            if (Near(rx, 32000) && Near(ry, 16500) && Near(gx, 15000) && Near(gy, 30000) && Near(bx, 7500) && Near(by, 3000))
                return "BT.709 / sRGB primaries";
            if (Near(rx, 35400) && Near(ry, 14600) && Near(gx, 8500) && Near(gy, 39850) && Near(bx, 6550) && Near(by, 2300))
                return "BT.2020 / BT.2100 primaries";
            if (Near(rx, 34000) && Near(ry, 16000) && Near(gx, 13250) && Near(gy, 34500) && Near(bx, 7500) && Near(by, 3000))
                return "P3 primaries (DCI-P3 / Display P3)";
            return null;
        }

        private static string? MatchWhitePoint(ushort wx, ushort wy)
        {
            if (Near(wx, 15635, 60) && Near(wy, 16450, 60)) return "D65";
            if (Near(wx, 15700, 60) && Near(wy, 17550, 60)) return "DCI white (~0.314, 0.351)";
            if (Near(wx, 17284, 60) && Near(wy, 17925, 60)) return "D50";
            return null;
        }
    }
}