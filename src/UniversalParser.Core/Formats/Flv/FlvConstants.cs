namespace UniversalParser.Core.Formats.Flv;

internal static class FlvConstants
{
    public const int FileHeaderLength = 9;
    public const int TagHeaderLength = 11;
    public const int PreviousTagSizeLength = 4;

    public const byte TagTypeAudio = 8;
    public const byte TagTypeVideo = 9;
    public const byte TagTypeScript = 18;

    public const int CodecIdAvc = 7;
    public const int CodecIdHevcLegacy = 12;

    public const int SoundFormatAac = 10;

    public const int VideoPacketTypeSequenceStart = 0;
    public const int VideoPacketTypeCodedFrames = 1;
    public const int VideoPacketTypeSequenceEnd = 2;
    public const int VideoPacketTypeCodedFramesX = 3;
    public const int VideoPacketTypeMetadata = 4;
    public const int VideoPacketTypeMpeg2TsSequenceStart = 5;
    public const int VideoPacketTypeMultitrack = 6;
    public const int VideoPacketTypeModEx = 7;

    public const int AvMultitrackTypeOneTrack = 0;
    public const int AvMultitrackTypeManyTracks = 1;
    public const int AvMultitrackTypeManyTracksManyCodecs = 2;
}
