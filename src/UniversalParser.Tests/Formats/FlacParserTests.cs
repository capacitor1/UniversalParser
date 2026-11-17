using System.Buffers.Binary;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;
using UniversalParser.Core.Validation;

namespace UniversalParser.Tests.Formats;

public sealed class FlacParserTests
{
    [Fact]
    public void MetadataBlocksAndStreamInfoAreParsed()
    {
        using var source = new MemorySource(BuildFlac());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.flac");

        Assert.Equal("flac", document.Parser.Id);
        Assert.Equal("Free Lossless Audio Codec", document.Root.Name);
        Assert.Equal(
            new[] { "STREAMINFO", "VORBIS_COMMENT", "SEEKTABLE", "PICTURE", "PADDING", "<audio frames>" },
            document.Root.Children.Select(static child => child.Name));
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var streamInfo = engine.ParseNode(source, document, document.Root.Children[0]).Fields;
        Assert.Contains(streamInfo, static field => field.Key == "LAST_METADATA_BLOCK_FLAG" && field.Value == "0");
        Assert.Contains(streamInfo, static field => field.Key == "LENGTH" && field.Value == "34");
        Assert.Contains(streamInfo, static field => field.Key == "MINIMUM_BLOCK_SIZE" && field.Value == "4096");
        Assert.Contains(streamInfo, static field => field.Key == "MAXIMUM_BLOCK_SIZE" && field.Value == "4096");
        Assert.Contains(streamInfo, static field => field.Key == "SAMPLE_RATE" && field.Value == "44100");
        Assert.Contains(streamInfo, static field => field.Key == "CHANNELS" && field.Value == "2");
        Assert.Contains(streamInfo, static field => field.Key == "BITS_PER_SAMPLE" && field.Value == "16");
        Assert.Contains(streamInfo, static field => field.Key == "TOTAL_SAMPLES" && field.Value == "44100");

        var comment = engine.ParseNode(source, document, document.Root.Children[1]).Fields;
        Assert.Contains(comment, static field => field.Key == "VENDOR_STRING" && field.Value == "test vendor");
        Assert.Contains(comment, static field => field.Key == "USER_COMMENT_LIST_LENGTH" && field.Value == "1");
        Assert.Contains(comment, static field => field.Key == "USER_COMMENT[0]" && field.Value == "TITLE=Sample");

        var seekTable = document.Root.Children[2];
        Assert.Equal(2, seekTable.Children.Length);
        Assert.All(seekTable.Children, static child => Assert.Equal("SEEKPOINT", child.Name));
        var seekPoint = engine.ParseNode(source, document, seekTable.Children[1]).Fields;
        Assert.Contains(seekPoint, static field => field.Key == "SAMPLE_NUMBER" && field.Value == "44100");
        Assert.Contains(seekPoint, static field => field.Key == "OFFSET" && field.Value == "1234");
        Assert.Contains(seekPoint, static field => field.Key == "FRAME_SAMPLES" && field.Value == "4096");

        var picture = engine.ParseNode(source, document, document.Root.Children[3]).Fields;
        Assert.Contains(picture, static field => field.Key == "PICTURE_TYPE" && field.Value == "3 (Front cover)");
        Assert.Contains(picture, static field => field.Key == "MEDIA_TYPE" && field.Value == "image/png");
        Assert.Contains(picture, static field => field.Key == "DESCRIPTION" && field.Value == "cover");
        Assert.Contains(picture, static field => field.Key == "WIDTH" && field.Value == "1");
        Assert.Contains(picture, static field => field.Key == "DATA_LENGTH" && field.Value == "4");

        Assert.Equal("<audio frames>", document.Root.Children[5].Name);
    }

    [Fact]
    public void CuesheetTracksAndIndexesAreListed()
    {
        using var source = new MemorySource(BuildCuesheetFlac());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "cuesheet.flac");

        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var cuesheet = Assert.Single(document.Root.Children, static child => child.Name == "CUESHEET");
        Assert.Equal(2, cuesheet.Children.Length);
        Assert.All(cuesheet.Children, static child => Assert.Equal("CUESHEET_TRACK", child.Name));

        var firstTrack = cuesheet.Children[0];
        Assert.Equal(2, firstTrack.Children.Length);
        Assert.All(firstTrack.Children, static child => Assert.Equal("CUESHEET_TRACK_INDEX", child.Name));

        var cuesheetFields = engine.ParseNode(source, document, cuesheet).Fields;
        Assert.Contains(cuesheetFields, static field => field.Key == "MEDIA_CATALOG_NUMBER" && field.Value == "CATALOG");
        Assert.Contains(cuesheetFields, static field => field.Key == "LEAD_IN" && field.Value == "88200");
        Assert.Contains(cuesheetFields, static field => field.Key == "CD_FLAG" && field.Value == "1");
        Assert.Contains(cuesheetFields, static field => field.Key == "TRACK_COUNT" && field.Value == "2");

        var trackFields = engine.ParseNode(source, document, firstTrack).Fields;
        Assert.Contains(trackFields, static field => field.Key == "TRACK_NUMBER" && field.Value == "1");
        Assert.Contains(trackFields, static field => field.Key == "ISRC" && field.Value == "ISRC00000001");
        Assert.Contains(trackFields, static field => field.Key == "INDEX_POINT_COUNT" && field.Value == "2");

        var indexFields = engine.ParseNode(source, document, firstTrack.Children[1]).Fields;
        Assert.Contains(indexFields, static field => field.Key == "INDEX_OFFSET" && field.Value == "1000");
        Assert.Contains(indexFields, static field => field.Key == "INDEX_NUMBER" && field.Value == "2");

        var leadOut = engine.ParseNode(source, document, cuesheet.Children[1]).Fields;
        Assert.Contains(leadOut, static field => field.Key == "TRACK_NUMBER" && field.Value == "170");
        Assert.Contains(leadOut, static field => field.Key == "INDEX_POINT_COUNT" && field.Value == "0");
    }

    [Fact]
    public void RegistryDetectsNativeFlac()
    {
        using var source = new MemorySource(BuildFlac());
        var registry = ParserRegistry.CreateDefault();

        var detection = registry.Detect(source, ParseLimits.Default);

        Assert.Equal("flac", detection.Parser.Id);
        Assert.Equal(ProbeConfidence.Strong, detection.Probe.Confidence);
    }

    private static byte[] BuildFlac()
    {
        using var file = new MemoryStream();
        file.Write("fLaC"u8);

        WriteBlock(file, last: false, type: 0, BuildStreamInfo());
        WriteBlock(file, last: false, type: 4, BuildComment("test vendor", ["TITLE=Sample"]));
        WriteBlock(
            file,
            last: false,
            type: 3,
            BuildSeekTable(
                (0, 0, 4096),
                (44100, 1234, 4096)));
        WriteBlock(file, last: false, type: 6, BuildPicture());
        WriteBlock(file, last: true, type: 1, new byte[8]);
        file.Write([0xFF, 0xF8, 0x00, 0x00, 0x11, 0x22, 0x33, 0x44]);
        return file.ToArray();
    }

    private static byte[] BuildCuesheetFlac()
    {
        using var file = new MemoryStream();
        file.Write("fLaC"u8);
        WriteBlock(file, last: false, type: 0, BuildStreamInfo());
        WriteBlock(file, last: true, type: 5, BuildCuesheet());
        file.Write([0xFF, 0xF8, 0x01, 0x02]);
        return file.ToArray();
    }

    private static byte[] BuildStreamInfo()
    {
        using var block = new MemoryStream();
        WriteUInt16BE(block, 4096); // MINIMUM_BLOCK_SIZE
        WriteUInt16BE(block, 4096); // MAXIMUM_BLOCK_SIZE
        WriteUInt24BE(block, 100); // MINIMUM_FRAME_SIZE
        WriteUInt24BE(block, 200); // MAXIMUM_FRAME_SIZE
        WritePackedAudioProperties(block, sampleRate: 44100, channels: 2, bitsPerSample: 16, totalSamples: 44100);
        block.Write(new byte[16]); // MD5_SIGNATURE
        Assert.Equal(34, block.Length);
        return block.ToArray();
    }

    private static byte[] BuildComment(string vendor, string[] comments)
    {
        using var block = new MemoryStream();
        var vendorBytes = Encoding.UTF8.GetBytes(vendor);
        WriteUInt32LE(block, (uint)vendorBytes.Length);
        block.Write(vendorBytes);
        WriteUInt32LE(block, (uint)comments.Length);
        foreach (var comment in comments)
        {
            var bytes = Encoding.UTF8.GetBytes(comment);
            WriteUInt32LE(block, (uint)bytes.Length);
            block.Write(bytes);
        }

        return block.ToArray();
    }

    private static byte[] BuildSeekTable(params (ulong SampleNumber, ulong Offset, ushort FrameSamples)[] points)
    {
        using var block = new MemoryStream();
        foreach (var (sampleNumber, offset, frameSamples) in points)
        {
            WriteUInt64BE(block, sampleNumber);
            WriteUInt64BE(block, offset);
            WriteUInt16BE(block, frameSamples);
        }

        return block.ToArray();
    }

    private static byte[] BuildPicture()
    {
        using var block = new MemoryStream();
        WriteUInt32BE(block, 3); // PICTURE_TYPE
        var mediaType = Encoding.UTF8.GetBytes("image/png");
        WriteUInt32BE(block, (uint)mediaType.Length);
        block.Write(mediaType);
        var description = Encoding.UTF8.GetBytes("cover");
        WriteUInt32BE(block, (uint)description.Length);
        block.Write(description);
        WriteUInt32BE(block, 1); // WIDTH
        WriteUInt32BE(block, 1); // HEIGHT
        WriteUInt32BE(block, 24); // COLOR_DEPTH
        WriteUInt32BE(block, 0); // COLORS
        WriteUInt32BE(block, 4); // DATA_LENGTH
        block.Write([0x89, 0x50, 0x4E, 0x47]);
        return block.ToArray();
    }

    private static byte[] BuildCuesheet()
    {
        using var block = new MemoryStream();
        var catalog = new byte[128];
        Encoding.ASCII.GetBytes("CATALOG").CopyTo(catalog, 0);
        block.Write(catalog);
        WriteUInt64BE(block, 88200); // LEAD_IN
        block.WriteByte(0x80); // CD_FLAG
        block.Write(new byte[258]);
        block.WriteByte(2); // TRACK_COUNT

        WriteCuesheetTrack(block, offset: 0, number: 1, isrc: "ISRC00000001", indexCount: 2);
        WriteCuesheetIndex(block, offset: 0, number: 1);
        WriteCuesheetIndex(block, offset: 1000, number: 2);

        WriteCuesheetTrack(block, offset: 44100, number: 170, isrc: "\0\0\0\0\0\0\0\0\0\0\0\0", indexCount: 0);
        return block.ToArray();
    }

    private static void WriteCuesheetTrack(Stream stream, ulong offset, byte number, string isrc, byte indexCount)
    {
        WriteUInt64BE(stream, offset);
        stream.WriteByte(number);
        var isrcBytes = new byte[12];
        Encoding.ASCII.GetBytes(isrc).AsSpan(0, Math.Min(12, isrc.Length)).CopyTo(isrcBytes);
        stream.Write(isrcBytes);
        stream.WriteByte(0); // TRACK_TYPE = 0, PRE_EMPHASIS = 0
        stream.Write(new byte[13]);
        stream.WriteByte(indexCount);
    }

    private static void WriteCuesheetIndex(Stream stream, ulong offset, byte number)
    {
        WriteUInt64BE(stream, offset);
        stream.WriteByte(number);
        stream.Write(new byte[3]);
    }

    private static void WriteBlock(Stream stream, bool last, byte type, byte[] data)
    {
        Assert.InRange(data.Length, 0, 0xFFFFFF);
        stream.WriteByte((byte)((last ? 0x80 : 0) | type));
        stream.WriteByte((byte)(data.Length >> 16));
        stream.WriteByte((byte)(data.Length >> 8));
        stream.WriteByte((byte)data.Length);
        stream.Write(data);
    }

    private static void WritePackedAudioProperties(Stream stream, uint sampleRate, byte channels, byte bitsPerSample, ulong totalSamples)
    {
        var packed = ((ulong)sampleRate << 44)
            | ((ulong)(channels - 1) << 41)
            | ((ulong)(bitsPerSample - 1) << 36)
            | (totalSamples & 0x0FFFFFFFFFUL);
        WriteUInt64BE(stream, packed);
    }

    private static void WriteUInt16BE(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt24BE(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteUInt32BE(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32LE(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt64BE(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
