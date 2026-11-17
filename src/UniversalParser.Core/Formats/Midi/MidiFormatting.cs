using System.Globalization;
using System.Text;

namespace UniversalParser.Core.Formats.Midi;

/// <summary>
/// Display helpers for Standard MIDI File values. Official SMF field names stay lowercase
/// (format, ntrks, division, delta_time, meta_type) and every human-readable expansion is derived.
/// </summary>
internal static class MidiFormatting
{
    private static readonly string[] NoteNames =
    [
        "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B",
    ];

    private static readonly Dictionary<int, string> ControllerNames = new()
    {
        [0] = "Bank Select MSB",
        [1] = "Modulation Wheel",
        [2] = "Breath Controller",
        [4] = "Foot Controller",
        [5] = "Portamento Time",
        [6] = "Data Entry MSB",
        [7] = "Channel Volume",
        [8] = "Balance",
        [10] = "Pan",
        [11] = "Expression Controller",
        [32] = "Bank Select LSB",
        [38] = "Data Entry LSB",
        [64] = "Sustain Pedal",
        [65] = "Portamento On/Off",
        [66] = "Sostenuto Pedal",
        [67] = "Soft Pedal",
        [68] = "Legato Footswitch",
        [69] = "Hold 2",
        [71] = "Sound Controller 2 (Resonance)",
        [72] = "Sound Controller 3 (Release Time)",
        [73] = "Sound Controller 4 (Attack Time)",
        [74] = "Sound Controller 5 (Brightness)",
        [84] = "Portamento Control",
        [91] = "Effects 1 Depth (Reverb)",
        [92] = "Effects 2 Depth (Tremolo)",
        [93] = "Effects 3 Depth (Chorus)",
        [94] = "Effects 4 Depth (Detune)",
        [95] = "Effects 5 Depth (Phaser)",
        [120] = "All Sound Off",
        [121] = "Reset All Controllers",
        [122] = "Local Control On/Off",
        [123] = "All Notes Off",
        [124] = "Omni Mode Off",
        [125] = "Omni Mode On",
        [126] = "Mono Mode On",
        [127] = "Poly Mode On",
    };

    public static string FormatHeaderFormat(ushort format) => format switch
    {
        0 => "single track",
        1 => "multiple simultaneous tracks",
        2 => "multiple independent sequences",
        _ => "undefined",
    };

    public static string FormatDivision(ushort division)
    {
        if ((division & 0x8000) == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{division} ticks per quarter note");
        }

        var framesPerSecond = -(sbyte)(division >> 8);
        var ticksPerFrame = division & 0xFF;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"SMPTE {framesPerSecond} fps, {ticksPerFrame} ticks per frame");
    }

    public static string FormatChannelEvent(byte status) => (status & 0xF0) switch
    {
        0x80 => "NoteOff",
        0x90 => "NoteOn",
        0xA0 => "PolyphonicKeyPressure",
        0xB0 => "ControlChange",
        0xC0 => "ProgramChange",
        0xD0 => "ChannelPressure",
        0xE0 => "PitchBend",
        _ => "ChannelEvent",
    };

    public static string FormatChannelEventDescription(byte status) => (status & 0xF0) switch
    {
        0x80 => "Note Off",
        0x90 => "Note On",
        0xA0 => "Polyphonic Key Pressure",
        0xB0 => "Control Change",
        0xC0 => "Program Change",
        0xD0 => "Channel Pressure",
        0xE0 => "Pitch Bend",
        _ => "Channel Event",
    };

    public static string FormatSystemEvent(byte status) => status switch
    {
        0xF1 => "TimeCodeQuarterFrame",
        0xF2 => "SongPositionPointer",
        0xF3 => "SongSelect",
        0xF4 => "UndefinedSystemEvent",
        0xF5 => "UndefinedSystemEvent",
        0xF6 => "TuneRequest",
        0xF8 => "TimingClock",
        0xF9 => "UndefinedSystemEvent",
        0xFA => "Start",
        0xFB => "Continue",
        0xFC => "Stop",
        0xFD => "UndefinedSystemEvent",
        0xFE => "ActiveSensing",
        _ => "SystemEvent",
    };

    public static string FormatSystemEventDescription(byte status) => status switch
    {
        0xF1 => "MIDI Time Code Quarter Frame",
        0xF2 => "Song Position Pointer",
        0xF3 => "Song Select",
        0xF6 => "Tune Request",
        0xF8 => "Timing Clock",
        0xFA => "Start",
        0xFB => "Continue",
        0xFC => "Stop",
        0xFE => "Active Sensing",
        _ => "Undefined System Common/Real-Time message",
    };

    public static string FormatMetaEvent(byte metaType) => metaType switch
    {
        0x00 => "SequenceNumber",
        0x01 => "TextEvent",
        0x02 => "CopyrightNotice",
        0x03 => "TrackName",
        0x04 => "InstrumentName",
        0x05 => "Lyric",
        0x06 => "Marker",
        0x07 => "CuePoint",
        0x08 => "ProgramName",
        0x09 => "DeviceName",
        0x20 => "ChannelPrefix",
        0x21 => "MidiPort",
        0x2F => "EndOfTrack",
        0x51 => "SetTempo",
        0x54 => "SmpteOffset",
        0x58 => "TimeSignature",
        0x59 => "KeySignature",
        0x7F => "SequencerSpecific",
        _ => "MetaEvent",
    };

    public static string FormatMetaEventDescription(byte metaType) => metaType switch
    {
        0x00 => "Sequence Number",
        0x01 => "Text Event",
        0x02 => "Copyright Notice",
        0x03 => "Sequence/Track Name",
        0x04 => "Instrument Name",
        0x05 => "Lyric",
        0x06 => "Marker",
        0x07 => "Cue Point",
        0x08 => "Program Name",
        0x09 => "Device Name",
        0x20 => "MIDI Channel Prefix",
        0x21 => "MIDI Port",
        0x2F => "End of Track",
        0x51 => "Set Tempo",
        0x54 => "SMPTE Offset",
        0x58 => "Time Signature",
        0x59 => "Key Signature",
        0x7F => "Sequencer-Specific Meta Event",
        _ => "Undefined Meta Event",
    };

    public static string FormatNote(byte note)
    {
        var octave = (note / 12) - 1;
        return string.Create(CultureInfo.InvariantCulture, $"{NoteNames[note % 12]}{octave}");
    }

    public static string FormatController(byte controller) =>
        ControllerNames.TryGetValue(controller, out var name) ? name : "Undefined Controller";

    public static string FormatTempoBpm(uint microsecondsPerQuarterNote)
    {
        if (microsecondsPerQuarterNote == 0)
        {
            return "undefined (tempo is zero)";
        }

        var bpm = 60_000_000m / microsecondsPerQuarterNote;
        return string.Create(CultureInfo.InvariantCulture, $"{bpm:0.###} BPM");
    }

    public static string FormatTimeSignature(byte numerator, byte denominatorPower)
    {
        if (denominatorPower >= 31)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{numerator}/2^{denominatorPower} (undefined denominator)");
        }

        var denominator = 1 << denominatorPower;
        return string.Create(CultureInfo.InvariantCulture, $"{numerator}/{denominator}");
    }

    public static string FormatKeySignature(byte sharpsOrFlats, byte mode)
    {
        var count = (sbyte)sharpsOrFlats;
        var accidentals = count switch
        {
            > 0 => count == 1 ? "1 sharp" : $"{count} sharps",
            < 0 => count == -1 ? "1 flat" : $"{-count} flats",
            _ => "no sharps or flats",
        };
        var tonality = mode switch
        {
            0 => "major",
            1 => "minor",
            _ => "undefined mode",
        };
        return $"{accidentals}, {tonality}";
    }

    public static string FormatText(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return EscapeText(encoding.GetString(bytes));
    }

    public static string EscapeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\0':
                    builder.Append("\\0");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < 0x20 || character == 0x7F)
                    {
                        builder.Append("\\x").Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
