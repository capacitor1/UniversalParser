using System;

namespace UniversalParser.Src.Parser.ID3
{
    internal static class ID3Tables
    {
        public static string PictureType(byte value)
        {
            return value switch
            {
                0x00 => "PICTURE_TYPE_OTHER",
                0x01 => "PICTURE_TYPE_32X32_PIXELS",
                0x02 => "PICTURE_TYPE_OTHER_ICON",
                0x03 => "PICTURE_TYPE_FRONT_COVER",
                0x04 => "PICTURE_TYPE_BACK_COVER",
                0x05 => "PICTURE_TYPE_LEAFLET_PAGE",
                0x06 => "PICTURE_TYPE_MEDIA",
                0x07 => "PICTURE_TYPE_LEAD_ARTIST",
                0x08 => "PICTURE_TYPE_ARTIST",
                0x09 => "PICTURE_TYPE_CONDUCTOR",
                0x0A => "PICTURE_TYPE_BAND",
                0x0B => "PICTURE_TYPE_COMPOSER",
                0x0C => "PICTURE_TYPE_LYRICIST",
                0x0D => "PICTURE_TYPE_RECORDING_LOCATION",
                0x0E => "PICTURE_TYPE_DURING_RECORDING",
                0x0F => "PICTURE_TYPE_DURING_PERFORMANCE",
                0x10 => "PICTURE_TYPE_VIDEO_CAPTURE",
                0x11 => "PICTURE_TYPE_BRIGHT_COLOURED_FISH",
                0x12 => "PICTURE_TYPE_ILLUSTRATION",
                0x13 => "PICTURE_TYPE_BAND_LOGO",
                0x14 => "PICTURE_TYPE_PUBLISHER_LOGO",
                _ => Unknown("PICTURE_TYPE", value)
            };
        }

        public static string TimestampFormat(byte value)
        {
            return value switch
            {
                0x01 => "TIMESTAMP_FORMAT_MPEG_FRAMES",
                0x02 => "TIMESTAMP_FORMAT_MILLISECONDS",
                _ => Unknown("TIMESTAMP_FORMAT", value)
            };
        }

        public static string EventType(byte value)
        {
            return value switch
            {
                0x00 => "EVENT_PADDING",
                0x01 => "EVENT_END_OF_INITIAL_SILENCE",
                0x02 => "EVENT_INTRO_START",
                0x03 => "EVENT_MAIN_PART_START",
                0x04 => "EVENT_MAIN_PART_END",
                0x05 => "EVENT_OUTRO_START",
                0x06 => "EVENT_OUTRO_END",
                0x07 => "EVENT_VERSE_START",
                0x08 => "EVENT_VERSE_END",
                0x09 => "EVENT_REFRAIN_START",
                0x0A => "EVENT_REFRAIN_END",
                0x0B => "EVENT_INTERLUDE_START",
                0x0C => "EVENT_INTERLUDE_END",
                0x0D => "EVENT_THEME_START",
                0x0E => "EVENT_THEME_END",
                0x0F => "EVENT_SOLO_START",
                0x10 => "EVENT_SOLO_END",
                0x11 => "EVENT_INTRO_END",
                0x12 => "EVENT_MAIN_PART_START",
                0x13 => "EVENT_MAIN_PART_END",
                0x14 => "EVENT_BREATHING",
                0x15 => "EVENT_BREAK",
                0x16 => "EVENT_ORIGINAL_MEDIA_START",
                0x17 => "EVENT_ORIGINAL_MEDIA_END",
                0x18 => "EVENT_COMMERCIAL_START",
                0x19 => "EVENT_COMMERCIAL_END",
                0xFA => "EVENT_AUDIO_END",
                0xFB => "EVENT_AUDIO_FILE_END",
                0xFC => "EVENT_RESERVED",
                _ => Unknown("EVENT_TYPE", value)
            };
        }

        public static string ReceivedAs(byte value)
        {
            return value switch
            {
                0x00 => "RECEIVED_AS_OTHER",
                0x01 => "RECEIVED_AS_STANDARD_CD_ALBUM",
                0x02 => "RECEIVED_AS_COMPRESSED_AUDIO_ON_CD",
                0x03 => "RECEIVED_AS_FILE_OVER_THE_INTERNET",
                0x04 => "RECEIVED_AS_STREAM_OVER_THE_INTERNET",
                0x05 => "RECEIVED_AS_AS_NOTE_SHEET",
                0x06 => "RECEIVED_AS_AS_NOTE_SHEET_WITHOUT_AUDIO",
                0x07 => "RECEIVED_AS_MUSICAL_REFERENCE",
                0x08 => "RECEIVED_AS_STANDARD_CD_SINGLE",
                0x09 => "RECEIVED_AS_COMPRESSED_AUDIO_SINGLE",
                0x0A => "RECEIVED_AS_OTHER_COMPACT_DISC",
                0x0B => "RECEIVED_AS_OTHER_LEGAL_AUDIO",
                0x0C => "RECEIVED_AS_NON_COMMERCIAL",
                _ => Unknown("RECEIVED_AS", value)
            };
        }

        public static string ChannelType(int value)
        {
            return value switch
            {
                0x00 => "CHANNEL_OTHER",
                0x01 => "CHANNEL_MASTER_VOLUME",
                0x02 => "CHANNEL_RIGHT",
                0x03 => "CHANNEL_LEFT",
                0x04 => "CHANNEL_RIGHT_REAR",
                0x05 => "CHANNEL_LEFT_REAR",
                0x06 => "CHANNEL_CENTER",
                0x07 => "CHANNEL_BASS",
                _ => Unknown("CHANNEL", value)
            };
        }

        private static string Unknown(string prefix, int value)
        {
            return prefix + "_" + ID3Format.Hex(value);
        }
    }
}