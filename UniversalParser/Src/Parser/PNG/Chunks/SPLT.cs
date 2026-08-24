using System.Buffers.Binary;

namespace UniversalParser.Src.Parser.PNG.Chunks
{
    /// <summary>
    /// sPLT - Suggested Palette
    /// PNG 3rd ed. 11.3.4.4 | type = 73 50 4C 54
    /// name(1-79) + '\0' + sampleDepth(1) + N * (6 bytes @depth8 | 10 bytes @depth16)
    /// </summary>
    internal static class SPLT
    {
        private const int NameScanLimit = 80;          // 79 字节名字 + NUL
        private const int PreviewCount  = 8;
        private const int MaxEntryBytes = 4 * 1024 * 1024;

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

            if (type != "sPLT")
                throw new InvalidDataException($"Expected sPLT but got '{type}'");

            var crcResult = PNGCRCValidator.Validate(fs, pos, length);

            var dataLines = new List<(string K, string V)>
            {
                ("CRC32", crcResult)
            };

            if (length > PngReader.MaxChunkLength)
            {
                dataLines.Add(("<Error>", $"Chunk length {length} exceeds 2^31-1"));
                return Result(node, fs, dataLines);
            }

            reader.Seek(pos + 8);
            long remaining = length;

            // ---- 1) palette name ----
            int scan = (int)Math.Min(remaining, NameScanLimit);
            var (name, consumed, terminated) = reader.ReadNullTerminatedLatin1(scan);
            remaining -= consumed;

            if (!terminated)
            {
                dataLines.Add(("<Error>", "No null separator found within the first 80 bytes"));
                return Result(node, fs, dataLines);
            }

            dataLines.Add(("PaletteName", $"\"{name}\" ({consumed - 1} bytes)"));

            if (!IsValidName(name))
                dataLines.Add(("<Warning>",
                    "Palette name violates keyword rules (1-79 printable Latin-1, no leading/trailing/consecutive spaces)"));

            // ---- 2) sample depth ----
            if (remaining < 1)
            {
                dataLines.Add(("<Error>", "Missing sample depth byte"));
                return Result(node, fs, dataLines);
            }

            byte sampleDepth = reader.ReadByte();
            remaining -= 1;
            dataLines.Add(("SampleDepth", sampleDepth.ToString()));

            int entrySize = sampleDepth switch { 8 => 6, 16 => 10, _ => 0 };
            if (entrySize == 0)
            {
                dataLines.Add(("<Error>", $"sPLT sample depth shall be 8 or 16, got {sampleDepth}"));
                return Result(node, fs, dataLines);
            }

            dataLines.Add(("EntrySize", $"{entrySize} bytes"));

            // ---- 3) entries ----
            if (remaining % entrySize != 0)
            {
                dataLines.Add(("<Error>",
                    $"{remaining} bytes of entry data is not divisible by {entrySize}"));
                return Result(node, fs, dataLines);
            }

            long count = remaining / entrySize;
            dataLines.Add(("EntryCount", count.ToString()));

            if (remaining > MaxEntryBytes)
            {
                dataLines.Add(("<Warning>",
                    $"Entry block is {remaining} bytes; detail parsing skipped"));
                return Result(node, fs, dataLines);
            }

            byte[] block = reader.ReadBytes((int)remaining);
            bool wide = sampleDepth == 16;
            int lastFreq = int.MaxValue;
            bool orderOk = true;

            for (int i = 0; i < count; i++)
            {
                var e = block.AsSpan(i * entrySize, entrySize);

                int r, g, b, a, f;
                if (wide)
                {
                    r = BinaryPrimitives.ReadUInt16BigEndian(e);
                    g = BinaryPrimitives.ReadUInt16BigEndian(e[2..]);
                    b = BinaryPrimitives.ReadUInt16BigEndian(e[4..]);
                    a = BinaryPrimitives.ReadUInt16BigEndian(e[6..]);
                    f = BinaryPrimitives.ReadUInt16BigEndian(e[8..]);
                }
                else
                {
                    r = e[0]; g = e[1]; b = e[2]; a = e[3];
                    f = BinaryPrimitives.ReadUInt16BigEndian(e[4..]);
                }

                if (f > lastFreq) orderOk = false;
                lastFreq = f;

                if (i < PreviewCount)
                    dataLines.Add(($"Entry[{i}]", $"RGBA({r}, {g}, {b}, {a})  freq={f}"));
            }

            if (count > PreviewCount)
                dataLines.Add(("...", $"{count - PreviewCount} more entries omitted"));

            if (!orderOk)
                dataLines.Add(("<Warning>", "Entries shall appear in decreasing order of frequency"));

            return Result(node, fs, dataLines);
        }

        private static ParseResult Result(Node node, Stream fs, List<(string K, string V)> dataLines)
            => new ParseResult
            {
                Title = "SuggestedPalette 'sPLT'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };

        /// <summary>与 tEXt keyword 相同的规则：1-79 字节可打印 Latin-1，无首尾/连续空格。</summary>
        private static bool IsValidName(string s)
        {
            if (s.Length is 0 or > 79) return false;
            if (s[0] == ' ' || s[^1] == ' ') return false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool printable = c is (>= '\u0020' and <= '\u007E') or (>= '\u00A1' and <= '\u00FF');
                if (!printable) return false;
                if (c == ' ' && i > 0 && s[i - 1] == ' ') return false;
            }
            return true;
        }
    }
}