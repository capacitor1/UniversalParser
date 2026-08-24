using System;
using System.Collections.Generic;
using UniversalParser.Src.Parser.MPEG.Boxes.StsdEx;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Stsd
    {
        private const int MaxEntryBody = 8 * 1024 * 1024;   // sanity cap against corrupt sizes

        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);
            fs.Position = (long)node.Position;

            long boxEnd = (long)(node.Position + node.Length);

            uint declaredSize = reader.ReadUInt32BE();
            string type = reader.ReadFourCC();
            if (declaredSize == 1) { reader.ReadUInt32BE(); reader.ReadUInt32BE(); }  // largesize

            byte version = reader.ReadByte();
            uint flags = (uint)((reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte());

            var dataLines = new List<(string K, string V)>();
            var w = new LineWriter(dataLines);
            w.Add("version", version);
            w.Add("flags", $"0x{flags:X6}");

            uint entryCount = reader.ReadUInt32BE();
            w.Add("entry_count", entryCount);

            // Optional but recommended: resolve hdlr of the parent trak so unknown fourcc
            // can still be parsed as video/audio.  Adapt to your Node API:
            //   string handler = parser.FindHandlerType(node);   // "vide" / "soun" / ...
            string handler = null;

            for (uint i = 0; i < entryCount; i++)
            {
                long entryStart = fs.Position;
                if (entryStart + 8 > boxEnd) { w.Note("truncated: entry header crosses box end"); break; }

                long entrySize = reader.ReadUInt32BE();
                string entryType = reader.ReadFourCC();
                int header = 8;

                if (entrySize == 1)
                {
                    entrySize = (long)(((ulong)reader.ReadUInt32BE() << 32) | reader.ReadUInt32BE());
                    header = 16;
                }
                else if (entrySize == 0)
                {
                    entrySize = boxEnd - entryStart;    // extends to the end of stsd
                }

                if (entrySize < header || entryStart + entrySize > boxEnd)
                {
                    w.Note($"entry[{i}] '{entryType}' has invalid size {entrySize} -> abort");
                    break;
                }

                long bodyLen = entrySize - header;
                using (w.Push($"entry[{i}]"))
                {
                    w.Add("type", entryType);
                    w.Add("size", entrySize);

                    if (bodyLen > MaxEntryBody)
                    {
                        w.Add("body", $"{bodyLen} bytes, too large to parse (skipped)");
                    }
                    else
                    {
                        byte[] body = new byte[bodyLen];
                        reader.ReadExactly(body);       // position is exactly entryStart + header
                        try
                        {
                            SampleEntry.Parse(entryType, body, w, handler);
                        }
                        catch (Exception ex)
                        {
                            w.Add("error", $"{ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }

                fs.Position = entryStart + entrySize;   // always realign
            }

            return new ParseResult
            {
                Title = $"SampleDescription '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}