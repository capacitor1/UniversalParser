using System;
using System.Collections.Generic;

namespace UniversalParser.Src.Parser.ID3
{
    internal static class ID3Flags
    {
        // ID3v2 tag header flags.
        public const byte TagUnsynchronisation = 0x80;
        public const byte TagExtendedHeader = 0x40;
        public const byte TagExperimental = 0x20;
        public const byte TagFooterPresent = 0x10;

        // ID3v2.2 tag header flags.
        public const byte TagCompressionV22 = 0x40;

        // ID3v2.3 extended header flags.
        public const ushort ExtendedV3CrcDataPresent = 0x8000;

        // ID3v2.4 extended header flags.
        public const byte ExtendedV4TagIsAnUpdate = 0x40;
        public const byte ExtendedV4CrcDataPresent = 0x20;
        public const byte ExtendedV4TagRestrictions = 0x10;

        // ID3v2.3 frame flags.
        public const ushort FrameV3TagAlterPreservation = 0x8000;
        public const ushort FrameV3FileAlterPreservation = 0x4000;
        public const ushort FrameV3ReadOnly = 0x2000;
        public const ushort FrameV3Compression = 0x0080;
        public const ushort FrameV3Encryption = 0x0040;
        public const ushort FrameV3GroupingIdentity = 0x0020;

        // ID3v2.4 frame flags.
        public const ushort FrameV4TagAlterPreservation = 0x4000;
        public const ushort FrameV4FileAlterPreservation = 0x2000;
        public const ushort FrameV4ReadOnly = 0x1000;
        public const ushort FrameV4GroupingIdentity = 0x0040;
        public const ushort FrameV4Compression = 0x0008;
        public const ushort FrameV4Encryption = 0x0004;
        public const ushort FrameV4Unsynchronisation = 0x0002;
        public const ushort FrameV4DataLengthIndicator = 0x0001;

        // ID3v2.4 CTOC flags.
        public const byte TableOfContentsTopLevel = 0x02;
        public const byte TableOfContentsOrdered = 0x01;

        public static string TagFlags(byte flags, byte version)
        {
            var names = new List<string>();

            if ((flags & TagUnsynchronisation) != 0)
                names.Add("FLAG_UNSYNCHRONISATION");

            if ((flags & TagExtendedHeader) != 0)
                names.Add("FLAG_EXTENDED_HEADER");

            if ((flags & TagExperimental) != 0)
                names.Add("FLAG_EXPERIMENTAL");

            if (version >= 4 && (flags & TagFooterPresent) != 0)
                names.Add("FLAG_FOOTER_PRESENT");

            if (version == 2 && (flags & TagCompressionV22) != 0)
                names.Add("FLAG_COMPRESSION");

            return Format(flags, names);
        }

        public static string ExtendedFlagsV3(ushort flags)
        {
            var names = new List<string>();

            if ((flags & ExtendedV3CrcDataPresent) != 0)
                names.Add("FLAG_CRC_DATA_PRESENT");

            return Format(flags, names, 4);
        }

        public static string ExtendedFlagsV4(byte flags)
        {
            var names = new List<string>();

            if ((flags & ExtendedV4TagIsAnUpdate) != 0)
                names.Add("FLAG_TAG_IS_AN_UPDATE");

            if ((flags & ExtendedV4CrcDataPresent) != 0)
                names.Add("FLAG_CRC_DATA_PRESENT");

            if ((flags & ExtendedV4TagRestrictions) != 0)
                names.Add("FLAG_TAG_RESTRICTIONS");

            return Format(flags, names);
        }

        public static string FrameFlags(ushort flags, byte version)
        {
            var names = new List<string>();

            if (version == 3)
            {
                if ((flags & FrameV3TagAlterPreservation) != 0)
                    names.Add("FLAG_TAG_ALTER_PRESERVATION");

                if ((flags & FrameV3FileAlterPreservation) != 0)
                    names.Add("FLAG_FILE_ALTER_PRESERVATION");

                if ((flags & FrameV3ReadOnly) != 0)
                    names.Add("FLAG_READ_ONLY");

                if ((flags & FrameV3Compression) != 0)
                    names.Add("FLAG_COMPRESSION");

                if ((flags & FrameV3Encryption) != 0)
                    names.Add("FLAG_ENCRYPTION");

                if ((flags & FrameV3GroupingIdentity) != 0)
                    names.Add("FLAG_GROUPING_IDENTITY");
            }
            else
            {
                if ((flags & FrameV4TagAlterPreservation) != 0)
                    names.Add("FLAG_TAG_ALTER_PRESERVATION");

                if ((flags & FrameV4FileAlterPreservation) != 0)
                    names.Add("FLAG_FILE_ALTER_PRESERVATION");

                if ((flags & FrameV4ReadOnly) != 0)
                    names.Add("FLAG_READ_ONLY");

                if ((flags & FrameV4GroupingIdentity) != 0)
                    names.Add("FLAG_GROUPING_IDENTITY");

                if ((flags & FrameV4Compression) != 0)
                    names.Add("FLAG_COMPRESSION");

                if ((flags & FrameV4Encryption) != 0)
                    names.Add("FLAG_ENCRYPTION");

                if ((flags & FrameV4Unsynchronisation) != 0)
                    names.Add("FLAG_UNSYNCHRONISATION");

                if ((flags & FrameV4DataLengthIndicator) != 0)
                    names.Add("FLAG_DATA_LENGTH_INDICATOR");
            }

            return Format(flags, names, 4);
        }

        public static string TableOfContentsFlags(byte flags)
        {
            var names = new List<string>();

            if ((flags & TableOfContentsTopLevel) != 0)
                names.Add("FLAG_TOP_LEVEL");

            if ((flags & TableOfContentsOrdered) != 0)
                names.Add("FLAG_ORDERED");

            return Format(flags, names);
        }

        public static string Restrictions(byte value)
        {
            var names = new List<string>();

            int tagSize = (value >> 6) & 0x03;
            int textEncoding = (value >> 5) & 0x01;
            int textFieldSize = (value >> 3) & 0x03;
            int imageEncoding = (value >> 2) & 0x01;
            int imageSize = value & 0x03;

            names.Add("TAG_SIZE_" + tagSize);
            names.Add(textEncoding == 0
                ? "TEXT_ENCODING_RESTRICTED"
                : "TEXT_ENCODING_UNRESTRICTED");
            names.Add("TEXT_FIELD_SIZE_" + textFieldSize);
            names.Add(imageEncoding == 0
                ? "IMAGE_ENCODING_RESTRICTED"
                : "IMAGE_ENCODING_UNRESTRICTED");
            names.Add("IMAGE_SIZE_" + imageSize);

            return Format(value, names);
        }

        private static string Format(
            int value,
            IReadOnlyList<string> names,
            int digits = 2)
        {
            string result = ID3Format.Hex(value, digits);

            if (names.Count == 0)
                return result;

            return result + " (" + string.Join(" | ", names) + ")";
        }
    }
}