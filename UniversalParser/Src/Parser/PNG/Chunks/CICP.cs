namespace UniversalParser.Src.Parser.PNG.Chunks
{
    /// <summary>
    /// cICP - Coding-Independent Code Points (ITU-T H.273)
    /// PNG 3rd ed. 11.3.2.6 | type = 63 49 43 50 | payload 固定 4 字节
    /// </summary>
    internal static class CICP
    {
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

            if (type != "cICP")
                throw new InvalidDataException($"Expected cICP but got '{type}'");

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult)
            };

            if (length != 4)
            {
                dataLines.Add(("<Error>", $"cICP payload must be exactly 4 bytes, got {length}"));
            }
            else
            {
                reader.Seek(pos + 8);   // CRC 校验后流位置不可信

                byte primaries = reader.ReadByte();
                byte transfer  = reader.ReadByte();
                byte matrix    = reader.ReadByte();
                byte range     = reader.ReadByte();

                dataLines.Add(("ColorPrimaries",     $"{primaries} - {H273.Primaries(primaries)}"));
                dataLines.Add(("TransferFunction",   $"{transfer} - {H273.Transfer(transfer)}"));
                dataLines.Add(("MatrixCoefficients", $"{matrix} - {H273.Matrix(matrix)}"));
                dataLines.Add(("VideoFullRangeFlag", range switch
                {
                    0 => "0 - Narrow (studio) range",
                    1 => "1 - Full range",
                    _ => $"{range} - invalid"
                }));

                if (WellKnown(primaries, transfer) is { } known)
                    dataLines.Add(("Interpretation", known));

                if (matrix != 0)
                    dataLines.Add(("<Warning>", "MatrixCoefficients shall be 0 (PNG carries RGB only)"));
                if (range > 1)
                    dataLines.Add(("<Warning>", "VideoFullRangeFlag shall be 0 or 1"));
                if (primaries == 2 || transfer == 2)
                    dataLines.Add(("<Warning>", "'Unspecified' (2) defeats the purpose of cICP"));
                if (primaries is 0 or 3 || transfer is 0 or 3)
                    dataLines.Add(("<Warning>", "Reserved H.273 code point in use"));

                dataLines.Add(("<Note>", "Highest-precedence color chunk; overrides iCCP / sRGB / gAMA / cHRM"));
            }

            return new ParseResult
            {
                Title = "ColorSpace 'cICP'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }

        private static string? WellKnown(byte cp, byte tf) => (cp, tf) switch
        {
            (1, 13)            => "sRGB",
            (1, 1)             => "BT.709 (HDTV)",
            (12, 13)           => "Display P3",
            (11, 17)           => "DCI-P3 (ST 428-1)",
            (9, 16)            => "BT.2100 PQ  ==> HDR10",
            (9, 18)            => "BT.2100 HLG",
            (9, 14) or (9, 15) => "BT.2020 (SDR wide gamut)",
            (10, 8)            => "CIE XYZ, linear",
            _                  => null
        };

        /// <summary>ITU-T H.273 code point 名称表（常用值）。</summary>
        private static class H273
        {
            public static string Primaries(byte v) => v switch
            {
                0  => "Reserved",
                1  => "BT.709 / sRGB / sYCC",
                2  => "Unspecified",
                3  => "Reserved",
                4  => "BT.470-6 System M (NTSC 1953)",
                5  => "BT.470-6 System B,G / BT.601 625 (PAL/SECAM)",
                6  => "BT.601 525 / SMPTE 170M",
                7  => "SMPTE 240M",
                8  => "Generic film (Illuminant C)",
                9  => "BT.2020 / BT.2100",
                10 => "SMPTE ST 428-1 (CIE XYZ)",
                11 => "SMPTE RP 431-2 (DCI-P3)",
                12 => "SMPTE EG 432-1 (Display P3)",
                22 => "EBU Tech 3213-E",
                _  => "Unknown/Reserved"
            };

            public static string Transfer(byte v) => v switch
            {
                0  => "Reserved",
                1  => "BT.709",
                2  => "Unspecified",
                3  => "Reserved",
                4  => "Gamma 2.2 (BT.470-6 System M)",
                5  => "Gamma 2.8 (BT.470-6 System B,G)",
                6  => "BT.601",
                7  => "SMPTE 240M",
                8  => "Linear",
                9  => "Logarithmic 100:1",
                10 => "Logarithmic 316.22777:1",
                11 => "IEC 61966-2-4 (xvYCC)",
                12 => "BT.1361 extended gamut",
                13 => "sRGB / sYCC (IEC 61966-2-1)",
                14 => "BT.2020 10-bit",
                15 => "BT.2020 12-bit",
                16 => "SMPTE ST 2084 (PQ) / BT.2100 PQ",
                17 => "SMPTE ST 428-1",
                18 => "ARIB STD-B67 (HLG) / BT.2100 HLG",
                _  => "Unknown/Reserved"
            };

            public static string Matrix(byte v) => v switch
            {
                0  => "Identity (RGB/GBR)",
                1  => "BT.709",
                2  => "Unspecified",
                4  => "FCC",
                5  => "BT.470 B,G",
                6  => "BT.601 / SMPTE 170M",
                7  => "SMPTE 240M",
                8  => "YCgCo",
                9  => "BT.2020 non-constant luminance",
                10 => "BT.2020 constant luminance",
                11 => "SMPTE ST 2085 (YDzDx)",
                12 => "Chromaticity-derived non-constant luminance",
                13 => "Chromaticity-derived constant luminance",
                14 => "ICtCp",
                _  => "Unknown/Reserved"
            };
        }
    }
}