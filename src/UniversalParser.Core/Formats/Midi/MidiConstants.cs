namespace UniversalParser.Core.Formats.Midi;

/// <summary>Standard MIDI File constants from the SMF 1.0 specification.</summary>
internal static class MidiConstants
{
    public const int ChunkHeaderLength = 8;
    public const int HeaderDataLength = 6;
    public const int MaxVlqBytes = 4;

    public const byte EndOfTrackMetaType = 0x2F;
    public const byte SetTempoMetaType = 0x51;
    public const byte TimeSignatureMetaType = 0x58;
    public const byte KeySignatureMetaType = 0x59;
    public const byte SmpteOffsetMetaType = 0x54;
    public const byte SequenceNumberMetaType = 0x00;
    public const byte ChannelPrefixMetaType = 0x20;
    public const byte MidiPortMetaType = 0x21;
    public const byte SequencerSpecificMetaType = 0x7F;

    public static ReadOnlySpan<byte> HeaderChunkId => "MThd"u8;
    public static ReadOnlySpan<byte> TrackChunkId => "MTrk"u8;

    public static bool IsTextMetaType(byte metaType) => metaType is >= 0x01 and <= 0x09;
}
