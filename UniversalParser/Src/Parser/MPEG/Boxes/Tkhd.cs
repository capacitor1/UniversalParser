using System;
using System.Collections.Generic;
using System.IO;

namespace UniversalParser.Src.Parser.MPEG.Boxes
{
    internal static class Tkhd
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
            // header
            // =========================
            reader.ReadUInt32BE(); // size (ignored by rule)
            string type = reader.ReadFourCC();

            // =========================
            // full box
            // =========================
            byte version = reader.ReadByte();
            byte f1 = reader.ReadByte();
            byte f2 = reader.ReadByte();
            byte f3 = reader.ReadByte();

            uint flags = (uint)((f1 << 16) | (f2 << 8) | f3);

            var dataLines = new List<(string K, string V)>
            {
                ("version", version.ToString()),
                ("flags", $"0x{flags:X6}")
            };

            // =========================
            // time fields
            // =========================
            ulong creationTime;
            ulong modificationTime;
            uint trackID;
            uint reserved;

            if (version == 0)
            {
                creationTime = reader.ReadUInt32BE();
                modificationTime = reader.ReadUInt32BE();
                trackID = reader.ReadUInt32BE();
                reserved = reader.ReadUInt32BE(); // always 0
            }
            else
            {
                creationTime = reader.ReadUInt64BE();
                modificationTime = reader.ReadUInt64BE();
                trackID = reader.ReadUInt32BE();
                reserved = reader.ReadUInt32BE();
            }

            dataLines.Add(("creation_time", creationTime.ToString()));
            dataLines.Add(("<creation_time>", Mvhd.ConvertMvhdTime(creationTime).ToString("yyyy-MM-dd HH:mm:ss")));

            dataLines.Add(("modification_time", modificationTime.ToString()));
            dataLines.Add(("<modification_time>", Mvhd.ConvertMvhdTime(modificationTime).ToString("yyyy-MM-dd HH:mm:ss")));

            dataLines.Add(("track_ID", trackID.ToString()));
            dataLines.Add(("reserved", reserved.ToString()));

            // =========================
            // duration
            // =========================
            ulong duration = version == 0
                ? reader.ReadUInt32BE()
                : reader.ReadUInt64BE();

            dataLines.Add(("duration", duration.ToString()));

            // =========================
            // layer / group / volume
            // =========================
            uint reserved1 = reader.ReadUInt32BE();
            uint reserved2 = reader.ReadUInt32BE();

            dataLines.Add(("reserved1", reserved1.ToString()));
            dataLines.Add(("reserved2", reserved2.ToString()));
            ushort layer = reader.ReadUInt16BE();
            ushort alternateGroup = reader.ReadUInt16BE();
            ushort volumeRaw = reader.ReadUInt16BE();

            double volume = volumeRaw / 256.0;

            dataLines.Add(("layer", layer.ToString()));
            dataLines.Add(("alternate_group", alternateGroup.ToString()));

            dataLines.Add(("volume", volumeRaw.ToString()));
            dataLines.Add(("<volume>", volume.ToString("F3")));

            // =========================
            // matrix
            // =========================
            var matrix = new uint[9];
            for (int i = 0; i < 9; i++)
                matrix[i] = reader.ReadUInt32BE();

            dataLines.Add(("matrix[9]", matrix[0].ToString()));
            for (int i = 1; i < 9; i++)
                dataLines.Add(("", matrix[i].ToString()));

            // =========================
            // width / height (16.16)
            // =========================
            uint widthRaw = reader.ReadUInt32BE();
            uint heightRaw = reader.ReadUInt32BE();

            double width = widthRaw / 65536.0;
            double height = heightRaw / 65536.0;

            dataLines.Add(("width", widthRaw.ToString()));
            dataLines.Add(("<width>", width.ToString("F3")));

            dataLines.Add(("height", heightRaw.ToString()));
            dataLines.Add(("<height>", height.ToString("F3")));

            // =========================
            // result
            // =========================
            return new ParseResult
            {
                
                Title = $"TrackHeader '{type}'",
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = new OffsetStream(fs, (long)node.Position, (long)node.Length)
            };
        }
    }
}