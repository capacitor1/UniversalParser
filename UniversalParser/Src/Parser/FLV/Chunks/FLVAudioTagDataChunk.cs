using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.FLV.Chunks
{
    internal static class FLVAudioTagDataChunk
    {
        private static readonly Dictionary<byte, string> SoundFormats = new()
        {
            [0] = "Linear PCM, platform endian",
            [1] = "ADPCM",
            [2] = "MP3",
            [3] = "Linear PCM, little endian",
            [4] = "Nellymoser 16 kHz mono",
            [5] = "Nellymoser 8 kHz mono",
            [6] = "Nellymoser",
            [7] = "G.711 A-law logarithmic PCM",
            [8] = "G.711 mu-law logarithmic PCM",
            [9] = "Reserved",
            [10] = "AAC",
            [11] = "Speex",
            [14] = "MP3 8 kHz",
            [15] = "Device-specific sound"
        };

        private static readonly string[] SoundRates =
        [
            "5.5 kHz",
            "11 kHz",
            "22 kHz",
            "44 kHz"
        ];

        public static ParseResult Parse(
            FLVParser parser,
            Node node)
        {
            ArgumentNullException.ThrowIfNull(parser);
            ArgumentNullException.ThrowIfNull(node);

            long payloadLength = (long)node.Length;
            var dataLines = new List<(string K, string V)>();

            if (payloadLength < 1)
            {
                dataLines.Add((
                    "<Error>",
                    "AudioTagData does not contain the mandatory audio header."));

                if (payloadLength > 0)
                {
                    dataLines.Add((
                        "<PayloadLength>",
                        FLVUtil.FormatBytes(payloadLength)));
                }

                return Build(parser, node, dataLines);
            }

            Span<byte> header = stackalloc byte[2];
            int read = parser.ReadAt(
                (long)node.Position,
                header[..(int)Math.Min(2, payloadLength)]);

            if (read < 1)
            {
                dataLines.Add((
                    "<Error>",
                    "Unable to read the audio header."));
                dataLines.Add((
                    "<PayloadLength>",
                    FLVUtil.FormatBytes(payloadLength)));

                return Build(parser, node, dataLines);
            }

            byte first = header[0];

            byte soundFormat = (byte)(first >> 4);
            byte soundRate = (byte)((first >> 2) & 0x03);
            byte soundSize = (byte)((first >> 1) & 0x01);
            byte soundType = (byte)(first & 0x01);

            dataLines.Add(("SoundFormat", soundFormat.ToString()));
            dataLines.Add((
                "<SoundFormat>",
                SoundFormats.TryGetValue(soundFormat, out string? format)
                    ? format
                    : "Unknown or experimental format"));

            dataLines.Add(("SoundRate", soundRate.ToString()));
            dataLines.Add(("<SoundRate>", SoundRates[soundRate]));

            dataLines.Add(("SoundSize", soundSize.ToString()));
            dataLines.Add((
                "<SoundSize>",
                soundSize == 0 ? "8-bit samples" : "16-bit samples"));

            dataLines.Add(("SoundType", soundType.ToString()));
            dataLines.Add((
                "<SoundType>",
                soundType == 0 ? "Mono" : "Stereo"));

            long parsedBytes = 1;

            // AAC AudioData = AACPacketType + Data
            if (soundFormat == 10)
            {
                if (read >= 2)
                {
                    byte aacPacketType = header[1];

                    dataLines.Add((
                        "AACPacketType",
                        aacPacketType.ToString()));

                    dataLines.Add((
                        "<AACPacketType>",
                        aacPacketType switch
                        {
                            0 => "AAC sequence header",
                            1 => "AAC raw",
                            _ => "Unknown or experimental AAC packet type"
                        }));

                    parsedBytes = 2;
                }
                else
                {
                    dataLines.Add((
                        "<Warning>",
                        "AACPacketType is missing."));
                }
            }

            /*
             * 编码音频帧或 AudioSpecificConfig 不在 FLV 层解释，
             * 因而剩余数据就是“不需要由 FLV 解析器解析”的 payload。
             */
            long unparsed = Math.Max(0, payloadLength - parsedBytes);

            if (unparsed > 0)
            {
                dataLines.Add((
                    "<PayloadLength>",
                    FLVUtil.FormatBytes(unparsed)));
            }

            return Build(parser, node, dataLines);
        }

        private static ParseResult Build(
            FLVParser parser,
            Node node,
            List<(string K, string V)> dataLines) =>
            new()
            {
                Title = FLVUtil.MakeTitle("AudioTagData", node.NodeName),
                Position = node.Position,
                Length = node.Length,
                DataLines = dataLines,
                RawData = parser.CreateRawStream(
                    (long)node.Position,
                    (long)node.Length)
            };
    }
}