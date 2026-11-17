using System.Globalization;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Formats.Midi;

/// <summary>One decoded event boundary. Payload bytes stay in the source and are read lazily.</summary>
internal readonly record struct MidiEventScan(
    long Start,
    long End,
    long DeltaTime,
    byte Status,
    bool RunningStatus,
    bool IsMeta,
    bool IsSysEx,
    byte MetaType,
    long DataOffset,
    long DataLength,
    string Name);

/// <summary>
/// Walks one SMF track event at a time. Only the framing bytes are consumed, so a large SysEx or
/// meta payload is skipped by length instead of being copied into memory.
/// </summary>
internal static class MidiEventScanner
{
    public static bool TryScan(
        SourceReader reader,
        ref int runningStatus,
        out MidiEventScan scan,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(reader);
        scan = default;
        error = string.Empty;
        var start = reader.Position;
        if (!TryReadVlq(reader, out var deltaTime, out error))
        {
            return false;
        }

        if (reader.Remaining == 0)
        {
            error = "The event ends after its delta-time.";
            return false;
        }

        var candidate = reader.ReadByte();
        byte status;
        long dataStart;
        var running = false;
        if (candidate < 0x80)
        {
            if (runningStatus == 0)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Running status is used at offset {start} without a preceding status byte.");
                return false;
            }

            status = checked((byte)runningStatus);
            dataStart = reader.Position - 1;
            running = true;
        }
        else
        {
            status = candidate;
            dataStart = reader.Position;
        }

        if (status is >= 0x80 and <= 0xEF)
        {
            var dataLength = GetChannelDataLength(status);
            // With running status the first data byte was already consumed while peeking at it.
            var remainingData = running ? dataLength - 1 : dataLength;
            if (reader.Remaining < remainingData)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Channel event 0x{status:X2} at offset {start} needs {dataLength} data byte(s) but only {reader.Remaining + (running ? 1 : 0)} remain.");
                return false;
            }

            reader.Skip(remainingData);
            runningStatus = status;
            scan = new MidiEventScan(
                start,
                reader.Position,
                deltaTime,
                status,
                running,
                false,
                false,
                0,
                dataStart,
                dataLength,
                MidiFormatting.FormatChannelEvent(status));
            return true;
        }

        if (status is 0xF0 or 0xF7)
        {
            if (!TryReadVlq(reader, out var length, out error))
            {
                return false;
            }

            if (length > reader.Remaining)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"SysEx at offset {start} declares {length} byte(s) but only {reader.Remaining} remain.");
                return false;
            }

            var payloadStart = reader.Position;
            reader.Skip(length);
            runningStatus = 0;
            scan = new MidiEventScan(
                start,
                reader.Position,
                deltaTime,
                status,
                running,
                false,
                true,
                0,
                payloadStart,
                length,
                status == 0xF0 ? "SysEx" : "SysExEscape");
            return true;
        }

        if (status == 0xFF)
        {
            if (reader.Remaining == 0)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Meta event at offset {start} is missing its meta type byte.");
                return false;
            }

            var metaType = reader.ReadByte();
            if (!TryReadVlq(reader, out var length, out error))
            {
                return false;
            }

            if (length > reader.Remaining)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Meta event 0x{metaType:X2} at offset {start} declares {length} byte(s) but only {reader.Remaining} remain.");
                return false;
            }

            var payloadStart = reader.Position;
            reader.Skip(length);
            runningStatus = 0;
            scan = new MidiEventScan(
                start,
                reader.Position,
                deltaTime,
                status,
                running,
                true,
                false,
                metaType,
                payloadStart,
                length,
                MidiFormatting.FormatMetaEvent(metaType));
            return true;
        }

        var systemDataLength = status switch
        {
            0xF1 => 1,
            0xF2 => 2,
            0xF3 => 1,
            _ => 0,
        };
        if (reader.Remaining < systemDataLength)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"System event 0x{status:X2} at offset {start} needs {systemDataLength} data byte(s) but only {reader.Remaining} remain.");
            return false;
        }

        reader.Skip(systemDataLength);
        runningStatus = 0;
        scan = new MidiEventScan(
            start,
            reader.Position,
            deltaTime,
            status,
            running,
            false,
            false,
            0,
            dataStart,
            systemDataLength,
            MidiFormatting.FormatSystemEvent(status));
        return true;
    }

    public static int GetChannelDataLength(byte status) => (status & 0xF0) switch
    {
        0xC0 or 0xD0 => 1,
        _ => 2,
    };

    public static bool TryReadVlq(SourceReader reader, out long value, out string error)
    {
        ArgumentNullException.ThrowIfNull(reader);
        value = 0;
        error = string.Empty;
        for (var index = 0; index < MidiConstants.MaxVlqBytes; index++)
        {
            if (reader.Remaining == 0)
            {
                error = "A variable-length quantity is truncated.";
                return false;
            }

            var current = reader.ReadByte();
            value = (value << 7) | (long)(current & 0x7F);
            if ((current & 0x80) == 0)
            {
                return true;
            }
        }

        error = "A variable-length quantity exceeds four bytes.";
        return false;
    }
}
