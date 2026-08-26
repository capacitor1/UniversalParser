using System;
using System.Collections.Generic;
using System.Text;

namespace UniversalParser.Src.Parser.ID3
{
    internal static class ID3String
    {
        public static readonly Encoding EncodingIso88591 =
            Encoding.Latin1;

        private static readonly Encoding Utf16Le =
            new UnicodeEncoding(false, false, true);

        private static readonly Encoding Utf16Be =
            new UnicodeEncoding(true, false, true);

        private static readonly Encoding Utf8 =
            new UTF8Encoding(false, false);

        public static string Decode(ReadOnlySpan<byte> data, Encoding encoding)
        {
            return encoding.GetString(data);
        }

        public static string Decode(ReadOnlySpan<byte> data, byte encoding)
        {
            return encoding switch
            {
                0 => EncodingIso88591.GetString(data),
                1 => DecodeUtf16WithBom(data),
                2 => Utf16Be.GetString(data),
                3 => Utf8.GetString(data),
                _ => EncodingIso88591.GetString(data)
            };
        }

        public static string[] DecodeTextValues(
            ReadOnlySpan<byte> data,
            byte encoding)
        {
            string value = Decode(data, encoding);

            if (value.Length == 0)
                return Array.Empty<string>();

            string[] values = value.Split('\0');
            var result = new List<string>(values.Length);

            foreach (string item in values)
            {
                if (item.Length != 0)
                    result.Add(item);
            }

            return result.ToArray();
        }

        public static string EncodingName(byte encoding)
        {
            return encoding switch
            {
                0 => "ISO-8859-1",
                1 => "UTF-16",
                2 => "UTF-16BE",
                3 => "UTF-8",
                _ => "UNKNOWN"
            };
        }

        private static string DecodeUtf16WithBom(ReadOnlySpan<byte> data)
        {
            if (data.Length >= 2)
            {
                if (data[0] == 0xFF && data[1] == 0xFE)
                    return Utf16Le.GetString(data[2..]);

                if (data[0] == 0xFE && data[1] == 0xFF)
                    return Utf16Be.GetString(data[2..]);
            }

            return Utf16Le.GetString(data);
        }
    }
}