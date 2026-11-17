using System.Buffers.Binary;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;
using UniversalParser.Core.Validation;

namespace UniversalParser.Tests.Formats;

public sealed class OggParserTests
{
    [Fact]
    public void VorbisPagesAndCommentsAreParsed()
    {
        using var source = new MemorySource(BuildVorbis());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.ogg");

        Assert.Equal("ogg", document.Parser.Id);
        Assert.Equal("Ogg Encapsulation Format", document.Root.Name);
        Assert.Equal(4, document.Root.Children.Length);
        Assert.All(document.Root.Children, static child => Assert.Equal("Page", child.Name));
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var identification = engine.ParseNode(source, document, document.Root.Children[0]).Fields;
        Assert.Contains(identification, static field => field.Key == "header_type_flag" && field.Value.Contains("beginning of stream", StringComparison.Ordinal));
        Assert.Contains(identification, static field => field.Key == "<CRC check>" && field.Value == "OK");
        Assert.Contains(identification, static field => field.Key == "<Packet[0].Header>" && field.Value == "Vorbis identification header");
        Assert.Contains(identification, static field => field.Key == "Packet[0].vorbis_version" && field.Value == "0");
        Assert.Contains(identification, static field => field.Key == "Packet[0].audio_channels" && field.Value == "2");
        Assert.Contains(identification, static field => field.Key == "Packet[0].audio_sample_rate" && field.Value == "44100");
        Assert.Contains(identification, static field => field.Key == "Packet[0].blocksize_0" && field.Value == "8 (256 samples)");

        var comment = engine.ParseNode(source, document, document.Root.Children[1]).Fields;
        Assert.Contains(comment, static field => field.Key == "<Packet[0].Header>" && field.Value == "Vorbis comment header");
        Assert.Contains(comment, static field => field.Key == "Packet[0].vendor_string" && field.Value == "test vendor");
        Assert.Contains(comment, static field => field.Key == "Packet[0].user_comment_list_length" && field.Value == "1");
        Assert.Contains(comment, static field => field.Key == "Packet[0].user_comment[0]" && field.Value == "TITLE=Sample");
        Assert.Contains(comment, static field => field.Key == "Packet[0].framing_bit" && field.Value == "1");

        var root = engine.ParseNode(source, document, document.Root).Fields;
        Assert.Contains(root, static field => field.Key == "<PageCount>" && field.Value == "4");
        Assert.Contains(root, static field => field.Key == "<Stream[0].Codec>" && field.Value == "Vorbis");
    }

    [Fact]
    public void OpusHeadersAreParsed()
    {
        using var source = new MemorySource(BuildOpus());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.opus");

        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var head = engine.ParseNode(source, document, document.Root.Children[0]).Fields;
        Assert.Contains(head, static field => field.Key == "<Packet[0].Header>" && field.Value == "OpusHead");
        Assert.Contains(head, static field => field.Key == "Packet[0].Version" && field.Value == "1");
        Assert.Contains(head, static field => field.Key == "Packet[0].Output Channel Count" && field.Value == "2");
        Assert.Contains(head, static field => field.Key == "Packet[0].Pre-skip" && field.Value == "312");
        Assert.Contains(head, static field => field.Key == "Packet[0].Input Sample Rate" && field.Value == "48000");

        var tags = engine.ParseNode(source, document, document.Root.Children[1]).Fields;
        Assert.Contains(tags, static field => field.Key == "<Packet[0].Header>" && field.Value == "OpusTags");
        Assert.Contains(tags, static field => field.Key == "Packet[0].Vendor String" && field.Value == "test vendor");
        Assert.Contains(tags, static field => field.Key == "Packet[0].User Comment #0 String" && field.Value == "ARTIST=Tester");

        var root = engine.ParseNode(source, document, document.Root).Fields;
        Assert.Contains(root, static field => field.Key == "<Stream[0].Codec>" && field.Value == "Opus");
    }

    [Fact]
    public void TheoraIdentificationAndCommentAreParsed()
    {
        using var source = new MemorySource(BuildTheora());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.ogv");

        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var identification = engine.ParseNode(source, document, document.Root.Children[0]).Fields;
        Assert.Contains(identification, static field => field.Key == "<Packet[0].Header>" && field.Value == "Theora identification header");
        Assert.Contains(identification, static field => field.Key == "Packet[0].VMAJ" && field.Value == "3");
        Assert.Contains(identification, static field => field.Key == "Packet[0].VMIN" && field.Value == "2");
        Assert.Contains(identification, static field => field.Key == "Packet[0].FMBW" && field.Value == "20");
        Assert.Contains(identification, static field => field.Key == "<Packet[0].FrameWidth>" && field.Value == "320");
        Assert.Contains(identification, static field => field.Key == "<Packet[0].FrameHeight>" && field.Value == "240");
        Assert.Contains(identification, static field => field.Key == "Packet[0].PICW" && field.Value == "320");
        Assert.Contains(identification, static field => field.Key == "Packet[0].FRN" && field.Value == "25");
        Assert.Contains(identification, static field => field.Key == "Packet[0].FRD" && field.Value == "1");
        Assert.Contains(identification, static field => field.Key == "<Packet[0].FramesPerSecond>" && field.Value == "25");
        Assert.Contains(identification, static field => field.Key == "Packet[0].QUAL" && field.Value == "18");
        Assert.Contains(identification, static field => field.Key == "Packet[0].KFGSHIFT" && field.Value == "6");

        var comment = engine.ParseNode(source, document, document.Root.Children[1]).Fields;
        Assert.Contains(comment, static field => field.Key == "<Packet[0].Header>" && field.Value == "Theora comment header");
        Assert.Contains(comment, static field => field.Key == "Packet[0].user_comment[0]" && field.Value == "ENCODER=Tester");
    }

    [Fact]
    public void CrcMismatchIsReported()
    {
        var bytes = BuildVorbis();
        bytes[^1] ^= 0xFF;
        using var source = new MemorySource(bytes);
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.ogg");

        var result = engine.ParseNode(source, document, document.Root.Children[^1]);
        Assert.Contains(result.Fields, static field => field.Key == "<CRC check>" && field.Value.StartsWith("mismatch", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == DiagnosticCodes.CrcMismatch);
    }

    [Fact]
    public void RegistryDetectsOgg()
    {
        using var source = new MemorySource(BuildVorbis());
        var registry = ParserRegistry.CreateDefault();

        var detection = registry.Detect(source, ParseLimits.Default);

        Assert.Equal("ogg", detection.Parser.Id);
        Assert.Equal(ProbeConfidence.Strong, detection.Probe.Confidence);
    }

    private static byte[] BuildVorbis()
    {
        using var file = new MemoryStream();
        var identification = BuildVorbisIdentification();
        var comment = BuildVorbisComment();
        var setup = BuildPacket(0x05, "vorbis", [0x42, 0x43, 0x56, 0x00]);
        var audio = new byte[] { 0x11, 0x22, 0x33, 0x44 };

        file.Write(BuildPage(0x02, 0, 1234, 0, [identification]));
        file.Write(BuildPage(0x00, 0, 1234, 1, [comment]));
        file.Write(BuildPage(0x00, 0, 1234, 2, [setup]));
        file.Write(BuildPage(0x04, 44032, 1234, 3, [audio]));
        return file.ToArray();
    }

    private static byte[] BuildOpus()
    {
        using var file = new MemoryStream();
        var head = new MemoryStream();
        head.Write("OpusHead"u8);
        head.WriteByte(1); // Version
        head.WriteByte(2); // Output channel count
        WriteUInt16(head, 312); // Pre-skip
        WriteUInt32(head, 48000); // Input sample rate
        WriteUInt16(head, 0); // Output gain
        head.WriteByte(0); // Channel mapping family

        using var tags = new MemoryStream();
        tags.Write("OpusTags"u8);
        WriteVendorAndComments(tags, "test vendor", ["ARTIST=Tester"]);

        file.Write(BuildPage(0x02, 0, 99, 0, [head.ToArray()]));
        file.Write(BuildPage(0x00, 0, 99, 1, [tags.ToArray()]));
        file.Write(BuildPage(0x04, 960, 99, 2, [new byte[] { 0xAA, 0xBB }]));
        return file.ToArray();
    }

    private static byte[] BuildTheora()
    {
        using var file = new MemoryStream();
        var identification = new MemoryStream();
        identification.WriteByte(0x80);
        identification.Write("theora"u8);
        identification.WriteByte(3); // VMAJ
        identification.WriteByte(2); // VMIN
        identification.WriteByte(1); // VREV
        WriteUInt16BE(identification, 20); // FMBW
        WriteUInt16BE(identification, 15); // FMBH
        WriteUInt24BE(identification, 320); // PICW
        WriteUInt24BE(identification, 240); // PICH
        identification.WriteByte(0); // PICX
        identification.WriteByte(0); // PICY
        WriteUInt32BE(identification, 25); // FRN
        WriteUInt32BE(identification, 1); // FRD
        WriteUInt24BE(identification, 1); // PARN
        WriteUInt24BE(identification, 1); // PARD
        identification.WriteByte(2); // CS
        WriteUInt24BE(identification, 80000); // NOMBR
        WriteUInt16BE(identification, (18 << 10) | (6 << 5)); // QUAL, KFGSHIFT, PF=0, Res=0

        using var comment = new MemoryStream();
        comment.WriteByte(0x81);
        comment.Write("theora"u8);
        WriteVendorAndComments(comment, "test vendor", ["ENCODER=Tester"]);

        var setup = BuildPacket(0x82, "theora", [0x01, 0x02, 0x03]);

        file.Write(BuildPage(0x02, 0, 7, 0, [identification.ToArray()]));
        file.Write(BuildPage(0x00, 0, 7, 1, [comment.ToArray()]));
        file.Write(BuildPage(0x00, 0, 7, 2, [setup]));
        file.Write(BuildPage(0x04, 25, 7, 3, [new byte[] { 0x00 }]));
        return file.ToArray();
    }

    private static byte[] BuildVorbisIdentification()
    {
        using var packet = new MemoryStream();
        packet.WriteByte(0x01);
        packet.Write("vorbis"u8);
        WriteUInt32(packet, 0); // vorbis_version
        packet.WriteByte(2); // audio_channels
        WriteUInt32(packet, 44100); // audio_sample_rate
        WriteInt32(packet, -1); // bitrate_maximum
        WriteInt32(packet, 128000); // bitrate_nominal
        WriteInt32(packet, -1); // bitrate_minimum
        packet.WriteByte(8 | (11 << 4)); // blocksize_0 / blocksize_1
        packet.WriteByte(1); // framing_flag
        return packet.ToArray();
    }

    private static byte[] BuildVorbisComment()
    {
        using var packet = new MemoryStream();
        packet.WriteByte(0x03);
        packet.Write("vorbis"u8);
        WriteVendorAndComments(packet, "test vendor", ["TITLE=Sample"]);
        packet.WriteByte(1); // framing_bit
        return packet.ToArray();
    }

    private static byte[] BuildPacket(byte type, string signature, byte[] payload)
    {
        using var packet = new MemoryStream();
        packet.WriteByte(type);
        packet.Write(Encoding.ASCII.GetBytes(signature));
        packet.Write(payload);
        return packet.ToArray();
    }

    private static void WriteVendorAndComments(Stream stream, string vendor, string[] comments)
    {
        var vendorBytes = Encoding.UTF8.GetBytes(vendor);
        WriteUInt32(stream, (uint)vendorBytes.Length);
        stream.Write(vendorBytes);
        WriteUInt32(stream, (uint)comments.Length);
        foreach (var comment in comments)
        {
            var bytes = Encoding.UTF8.GetBytes(comment);
            WriteUInt32(stream, (uint)bytes.Length);
            stream.Write(bytes);
        }
    }

    private static byte[] BuildPage(byte headerType, ulong granule, uint serial, uint sequence, byte[][] packets)
    {
        var lacing = new List<byte>();
        using var payload = new MemoryStream();
        foreach (var packet in packets)
        {
            var remaining = packet.Length;
            while (remaining >= 255)
            {
                lacing.Add(255);
                remaining -= 255;
            }

            lacing.Add((byte)remaining);
            payload.Write(packet);
        }

        using var page = new MemoryStream();
        page.Write("OggS"u8);
        page.WriteByte(0); // stream_structure_version
        page.WriteByte(headerType);
        WriteUInt64(page, granule);
        WriteUInt32(page, serial);
        WriteUInt32(page, sequence);
        WriteUInt32(page, 0); // CRC placeholder
        page.WriteByte((byte)lacing.Count);
        page.Write(lacing.ToArray());
        payload.Position = 0;
        payload.CopyTo(page);

        var bytes = page.ToArray();
        var crc = ComputeCrc(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(22, 4), crc);
        return bytes;
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> data)
    {
        var crc = 0u;
        foreach (var value in data)
        {
            crc ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
            }
        }

        return crc;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt16BE(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32BE(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt24BE(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
