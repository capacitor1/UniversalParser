using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>OpenDML 1.02 index structures, read one entry at a time without following references.</summary>
internal sealed class RiffOpenDmlParser
{
    private readonly RiffChunkContext _context;
    private readonly SourceReader _reader;

    private RiffOpenDmlParser(RiffChunkContext context)
    {
        _context = context;
        _reader = new SourceReader(context.Source, context.Layout.PayloadStart, context.PayloadLength);
    }

    public static bool IsIndexId(string id) => id.Length == 4 && id.StartsWith("ix", StringComparison.Ordinal)
        && id[2] is >= '0' and <= '9' && id[3] is >= '0' and <= '9';

    public static void ParseHeader(RiffChunkContext context)
    {
        var parser = new RiffOpenDmlParser(context);
        if (parser.UInt32("dwTotalFrames", out _))
        {
            context.AddUnparsedPayloadNote(4);
        }
    }

    public static void ParseIndex(RiffChunkContext context) => new RiffOpenDmlParser(context).ReadIndex();

    private void ReadIndex()
    {
        if (!UInt16("wLongsPerEntry", out var words)
            || !Byte("bIndexSubType", out var subType, FormatSubType)
            || !Byte("bIndexType", out var indexType, FormatType)
            || !UInt32("nEntriesInUse", out var used)
            || !FourCc("dwChunkId"))
        {
            return;
        }

        if (indexType == 1)
        {
            if (!UInt64("qwBaseOffset") || !UInt32("dwReserved3", out _))
            {
                return;
            }
        }
        else
        {
            for (var index = 0; index < 3; index++)
            {
                if (!UInt32($"dwReserved[{index}]", out _))
                {
                    return;
                }
            }
        }

        if (words == 0)
        {
            _context.Fields.Warning(DiagnosticCodes.InvalidLength,
                "OpenDML index wLongsPerEntry cannot be zero.", _context.Range(0, 2));
            return;
        }

        var stride = words * 4L;
        var arrayRange = _reader.CurrentRange;
        var capacity = _reader.Remaining / stride;
        if (_reader.Remaining % stride != 0)
        {
            _context.Fields.Warning(DiagnosticCodes.InvalidLength,
                "The index array ends with an incomplete entry.", arrayRange);
        }

        if (used > capacity)
        {
            _context.Fields.Warning(DiagnosticCodes.TruncatedPayload,
                $"The index declares {used} used entries, but only {capacity} complete entries fit.", arrayRange);
        }

        var expectedWords = indexType switch { 0 => 4, 1 when subType == 0 => 2, 1 when subType == 1 => 3, _ => 0 };
        var knownLayout = expectedWords > 0 && words >= expectedWords;
        if (expectedWords > 0 && words < expectedWords)
        {
            _context.Fields.Warning(DiagnosticCodes.InvalidLength,
                $"Index type {indexType} / subtype {subType} needs {expectedWords} DWORDs per entry, not {words}.",
                _context.Range(0, 4));
        }

        if (!knownLayout)
        {
            _context.Fields.Note("This index layout is not interpreted; bounded raw DWORD entries are shown.", arrayRange);
        }

        var rows = Math.Min(Math.Min(used, capacity), _context.Limits.MaxDataLines);
        var shown = 0L;
        for (; shown < rows && !_context.Fields.IsFull; shown++)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            var entryStart = _reader.Position;
            var prefix = string.Create(CultureInfo.InvariantCulture, $"aIndex[{shown}].");
            if (knownLayout && indexType == 0)
            {
                _ = UInt64(prefix + "qwOffset");
                _ = UInt32(prefix + "dwSize", out _);
                _ = UInt32(prefix + "dwDuration", out _);
            }
            else if (knownLayout && indexType == 1)
            {
                _ = UInt32(prefix + "dwOffset", out _);
                _ = UInt32(prefix + "dwSize", out _, describeSize: true);
                if (subType == 1)
                {
                    _ = UInt32(prefix + "dwOffsetField2", out _);
                }
            }

            // Wider or unfamiliar entries retain their raw words instead of guessing a layout.
            var firstWord = knownLayout ? expectedWords : 0;
            for (var word = firstWord; word < words && !_context.Fields.IsFull; word++)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
                _ = UInt32(prefix + $"adw[{word}]", out _);
            }

            _reader.Skip(entryStart + stride - _reader.Position);
        }

        if (shown < Math.Min(used, capacity))
        {
            _context.Fields.Warning(DiagnosticCodes.LimitRecords,
                $"Only {shown} of {used} used index entries are shown.", _reader.CurrentRange);
        }

        _context.Fields.Derived("<IndexEntryCapacity>", capacity, arrayRange);
        if (capacity > used)
        {
            _context.Fields.Derived("<UnusedEntryCount>", capacity - used, arrayRange);
        }

        _context.AddUnparsedPayloadNote(_reader.Position - _context.Layout.PayloadStart);
    }

    private bool Require(int bytes, string key)
    {
        if (_reader.Remaining >= bytes)
        {
            return true;
        }

        _context.Fields.Warning(DiagnosticCodes.TruncatedPayload, $"Field '{key}' is truncated.", _reader.CurrentRange);
        return false;
    }

    private bool UInt16(string key, out ushort value)
    {
        value = 0;
        if (!Require(2, key)) return false;
        var start = _reader.Position;
        value = _reader.ReadUInt16(_context.Endian);
        _context.Fields.Raw(key, value, new SourceRange(start, 2));
        return true;
    }

    private bool Byte(string key, out byte value, Func<byte, string> format)
    {
        value = 0;
        if (!Require(1, key)) return false;
        var start = _reader.Position;
        value = _reader.ReadByte();
        _context.Fields.Raw(key, format(value), new SourceRange(start, 1));
        return true;
    }

    private bool UInt32(string key, out uint value, bool describeSize = false)
    {
        value = 0;
        if (!Require(4, key)) return false;
        var start = _reader.Position;
        value = _reader.ReadUInt32(_context.Endian);
        var text = describeSize
            ? string.Create(CultureInfo.InvariantCulture,
                $"0x{value:X8} (length {value & 0x7FFFFFFF}, {((value & 0x80000000) == 0 ? "keyframe" : "non-keyframe")})")
            : value.ToString(CultureInfo.InvariantCulture);
        _context.Fields.Raw(key, text, new SourceRange(start, 4));
        return true;
    }

    private bool UInt64(string key)
    {
        if (!Require(8, key)) return false;
        var start = _reader.Position;
        _context.Fields.Raw(key, _reader.ReadUInt64(_context.Endian), new SourceRange(start, 8));
        return true;
    }

    private bool FourCc(string key)
    {
        if (!Require(4, key)) return false;
        var start = _reader.Position;
        Span<byte> bytes = stackalloc byte[4];
        _reader.ReadExactly(bytes);
        _context.Fields.Raw(key, RiffFormatting.FormatFourCc(bytes), new SourceRange(start, 4));
        return true;
    }

    private static string FormatType(byte value) => value switch
    {
        0 => "0 (AVI_INDEX_OF_INDEXES)",
        1 => "1 (AVI_INDEX_OF_CHUNKS)",
        0x80 => "128 (AVI_INDEX_IS_DATA)",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private static string FormatSubType(byte value) => value == 1
        ? "1 (AVI_INDEX_2FIELD)" : value.ToString(CultureInfo.InvariantCulture);
}
