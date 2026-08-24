using System;
using System.Collections.Generic;
using System.IO;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Mvhd
    {
        public static ParseResult Parse(MPEGParser parser, Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            var fs = parser.FileStream;

            if (fs.Length < (long)(node.Position + 8))
                throw new InvalidDataException("Box is truncated.");

            var reader = new MpegReader(fs);
            fs.Position = (long)node.Position;

            // =========================
            // Box Header
            // =========================
            uint size = reader.ReadUInt32BE();
            string type = reader.ReadFourCC();

            // =========================
            // FullBox Header
            // =========================
            byte version = reader.ReadByte();
            byte flags1 = reader.ReadByte();
            byte flags2 = reader.ReadByte();
            byte flags3 = reader.ReadByte();

            uint flags = (uint)((flags1 << 16) | (flags2 << 8) | flags3);

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}")
            };

            // =========================
            // Time fields
            // =========================
            ulong creationTime;
            ulong modificationTime;
            uint timescale;
            ulong duration;

            if (version == 0)
            {
                creationTime = reader.ReadUInt32BE();
                modificationTime = reader.ReadUInt32BE();
                timescale = reader.ReadUInt32BE();
                duration = reader.ReadUInt32BE();
            }
            else if (version == 1)
            {
                creationTime = reader.ReadUInt64BE();
                modificationTime = reader.ReadUInt64BE();
                timescale = reader.ReadUInt32BE();
                duration = reader.ReadUInt64BE();
            }
            else
            {
                throw new InvalidDataException($"Unsupported mvhd version: {version}");
            }

            dataLines.Add(("creation_time", creationTime.ToString()));
            dataLines.Add(("<creation_time>", ConvertMvhdTime(creationTime).ToString("yyyy-MM-dd HH:mm:ss")));

            dataLines.Add(("modification_time", modificationTime.ToString()));
            dataLines.Add(("<modification_time>", ConvertMvhdTime(modificationTime).ToString("yyyy-MM-dd HH:mm:ss")));
            dataLines.Add(("timescale", timescale.ToString()));
            dataLines.Add(("duration", duration.ToString()));
            if (timescale != 0)
            {
                double seconds = (double)duration / timescale;
                dataLines.Add(("<duration_seconds>", seconds.ToString("F3")));
            }

            // =========================
            // Rate (16.16)
            // =========================
            uint rateRaw = reader.ReadUInt32BE();
            double rate = rateRaw / 65536.0;

            dataLines.Add(("rate", rateRaw.ToString()));
            dataLines.Add(("<rate>", rate.ToString("F6")));

            // =========================
            // Volume (8.8)
            // =========================
            ushort volumeRaw = reader.ReadUInt16BE();
            double volume = volumeRaw / 256.0;

            dataLines.Add(("volume", volumeRaw.ToString()));
            dataLines.Add(("<volume>", volume.ToString("F3")));

            // =========================
            // Reserved (10 bytes total)
            // =========================
            ushort reserved1 = reader.ReadUInt16BE();
            uint reserved2 = reader.ReadUInt32BE();
            uint reserved3 = reader.ReadUInt32BE();

            dataLines.Add(("reserved[3]", reserved1.ToString()));
            dataLines.Add(("", reserved2.ToString()));
            dataLines.Add(("", reserved3.ToString()));

            // =========================
            // Matrix (9 * 32-bit)
            // =========================
            var matrix = new uint[9];
            for (int i = 0; i < 9; i++)
            {
                matrix[i] = reader.ReadUInt32BE();
            }

            dataLines.Add(($"matrix[9]", matrix[0].ToString()));
            for (int i = 1; i < 9; i++)
                dataLines.Add(("", matrix[i].ToString()));

            // =========================
            // Pre-defined (6 * 32-bit)
            // =========================
            var preDefined = new uint[6];
            for (int i = 0; i < 6; i++)
            {
                preDefined[i] = reader.ReadUInt32BE();
            }

            dataLines.Add(($"pre_defined[6]", preDefined[0].ToString()));
            for (int i = 1; i < 6; i++)
                dataLines.Add(("", preDefined[i].ToString()));

            // =========================
            // Next Track ID
            // =========================
            uint nextTrackID = reader.ReadUInt32BE();
            dataLines.Add(("next_track_ID", nextTrackID.ToString()));

            // =========================
            // Result
            // =========================
            return new ParseResult
            {
                
                Title = $"MovieHeader '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
        public static DateTime ConvertMvhdTime(ulong value)
        {
            // ISO BMFF time origin = 1904-01-01
            var epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(value);
        }
    }
}