using System;
using System.Collections.Generic;
using System.IO;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Mdhd
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            var fs = parser.FileStream;
            var reader = new MpegReader(fs);

            fs.Position = (long)node.Position;

            reader.ReadUInt32BE(); // size ignored
            string type = reader.ReadFourCC();

            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{f1<<16|f2<<8|f3:X6}")
            };

            ulong creation, modification;
            uint timescale;
            ulong duration;

            if (version == 0)
            {
                creation = reader.ReadUInt32BE();
                modification = reader.ReadUInt32BE();
                timescale = reader.ReadUInt32BE();
                duration = reader.ReadUInt32BE();
            }
            else
            {
                creation = reader.ReadUInt64BE();
                modification = reader.ReadUInt64BE();
                timescale = reader.ReadUInt32BE();
                duration = reader.ReadUInt64BE();
            }

            dataLines.Add(("creation_time", creation.ToString()));
            dataLines.Add(("<creation_time>", Mvhd.ConvertMvhdTime(creation).ToString("yyyy-MM-dd HH:mm:ss")));

            dataLines.Add(("modification_time", modification.ToString()));
            dataLines.Add(("<modification_time>", Mvhd.ConvertMvhdTime(modification).ToString("yyyy-MM-dd HH:mm:ss")));

            dataLines.Add(("timescale", timescale.ToString()));
            dataLines.Add(("duration", duration.ToString()));

            // language (ISO: 15-bit packed)
            ushort langRaw = reader.ReadUInt16BE();
            string lang = DecodeIsoLanguage(langRaw);

            dataLines.Add(("language", langRaw.ToString()));
            dataLines.Add(("<language>", lang));

            ushort preDefined = reader.ReadUInt16BE();
            dataLines.Add(("pre_defined", preDefined.ToString()));

            return new ParseResult
            {
                
                Title = $"MediaHeader '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }

        private static string DecodeIsoLanguage(ushort value)
        {
            // ISO-639-2/T packed 3x5-bit
            char c1 = (char)(((value >> 10) & 0x1F) + 0x60);
            char c2 = (char)(((value >> 5) & 0x1F) + 0x60);
            char c3 = (char)((value & 0x1F) + 0x60);

            return new string([c1, c2, c3]);
        }
    }
}