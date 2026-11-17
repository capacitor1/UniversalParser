using System.Buffers.Binary;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;
using UniversalParser.Core.Validation;

namespace UniversalParser.Tests.Formats;

public sealed class BlendParserTests
{
    [Fact]
    public void CurrentHeaderBlocksAndMetadataAreParsed()
    {
        using var source = new MemorySource(BuildCurrentBlend());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.blend");

        Assert.Equal("blend", document.Parser.Id);
        Assert.Equal("Blender File", document.Root.Name);
        Assert.Equal(
            new[] { "GLOB", "TEST", "DATA", "DNA1", "ENDB" },
            document.Root.Children.Select(static child => child.Name));
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var root = engine.ParseNode(source, document, document.Root).Fields;
        Assert.Contains(root, static field => field.Key == "<HeaderSize>" && field.Value == "17");
        Assert.Contains(root, static field => field.Key == "<FileFormatVersion>" && field.Value == "1");
        Assert.Contains(root, static field => field.Key == "<PointerSize>" && field.Value == "8");
        Assert.Contains(root, static field => field.Key == "file_version" && field.Value == "0500");

        var global = engine.ParseNode(source, document, document.Root.Children[0]).Fields;
        Assert.Contains(global, static field => field.Key == "<SDNA_STRUCT>" && field.Value == "FileGlobal");
        Assert.Contains(global, static field => field.Key == "subvstr" && field.Value == "0500");
        Assert.Contains(global, static field => field.Key == "subversion" && field.Value == "119");
        Assert.Contains(global, static field => field.Key == "minversion" && field.Value == "405");
        Assert.Contains(global, static field => field.Key == "fileflags" && field.Value == "0");
        Assert.Contains(global, static field => field.Key == "filename" && field.Value == "test.blend");

        var thumbnail = engine.ParseNode(source, document, document.Root.Children[1]).Fields;
        Assert.Contains(thumbnail, static field => field.Key == "width" && field.Value == "2");
        Assert.Contains(thumbnail, static field => field.Key == "height" && field.Value == "1");
        Assert.Contains(thumbnail, static field => field.Key == "<ThumbnailDataLength>" && field.Value == "8");

        var data = engine.ParseNode(source, document, document.Root.Children[2]).Fields;
        Assert.Contains(data, static field => field.Key == "<SDNA_STRUCT>" && field.Value == "FileGlobal");
        Assert.Contains(data, static field => field.Key == "nr" && field.Value == "2");
        Assert.Contains(data, static field => field.Key == "<Note>" && field.Value.Contains("not interpreted", StringComparison.Ordinal));

        var dna = engine.ParseNode(source, document, document.Root.Children[3]).Fields;
        Assert.Contains(dna, static field => field.Key == "NAME_COUNT" && field.Value == "7");
        Assert.Contains(dna, static field => field.Key == "TYPE_COUNT" && field.Value == "4");
        Assert.Contains(dna, static field => field.Key == "TLEN_COUNT" && field.Value == "4");
        Assert.Contains(dna, static field => field.Key == "STRUCT_COUNT" && field.Value == "1");
        Assert.Contains(dna, static field => field.Key == "STRUCT[0]" && field.Value == "FileGlobal (7 field(s))");
    }

    [Fact]
    public void LegacyHeaderAndSmallBHeadAreParsed()
    {
        using var source = new MemorySource(BuildLegacyBlend(pointerSize: 8));
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "legacy.blend");

        Assert.Equal("blend", document.Parser.Id);
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var root = engine.ParseNode(source, document, document.Root).Fields;
        Assert.Contains(root, static field => field.Key == "<HeaderSize>" && field.Value == "12");
        Assert.Contains(root, static field => field.Key == "<FileFormatVersion>" && field.Value == "0");
        Assert.Contains(root, static field => field.Key == "pointer_size" && field.Value == "8");
        Assert.Contains(root, static field => field.Key == "file_version" && field.Value == "304");

        var global = engine.ParseNode(source, document, document.Root.Children[0]).Fields;
        Assert.Contains(global, static field => field.Key == "subvstr" && field.Value == "0500");
        Assert.Contains(global, static field => field.Key == "filename" && field.Value == "test.blend");
    }

    [Fact]
    public void Legacy32BitHeaderIsParsed()
    {
        using var source = new MemorySource(BuildLegacyBlend(pointerSize: 4));
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "legacy32.blend");

        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));
        var root = engine.ParseNode(source, document, document.Root).Fields;
        Assert.Contains(root, static field => field.Key == "pointer_size" && field.Value == "4");
        Assert.Contains(root, static field => field.Key == "file_version" && field.Value == "304");
    }

    [Fact]
    public void LegacyEightByteEndbIsAccepted()
    {
        using var source = new MemorySource(BuildLegacyBlendWithShortEnd());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "legacy-endb.blend");

        Assert.Equal(
            new[] { "GLOB", "DNA1", "ENDB" },
            document.Root.Children.Select(static child => child.Name));
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var end = engine.ParseNode(source, document, document.Root.Children[2]);
        Assert.Contains(end.Fields, static field => field.Key == "<Note>" && field.Value.Contains("Legacy 8-byte ENDB", StringComparison.Ordinal));
    }

    [Fact]
    public void RegistryDetectsBlend()
    {
        using var source = new MemorySource(BuildCurrentBlend());
        var registry = ParserRegistry.CreateDefault();

        var detection = registry.Detect(source, ParseLimits.Default);

        Assert.Equal("blend", detection.Parser.Id);
        Assert.Equal(ProbeConfidence.Strong, detection.Probe.Confidence);
    }

    [Fact]
    public void ZstdCompressedBlendFallsBackToRaw()
    {
        using var source = new MemorySource(new byte[] { 0x28, 0xB5, 0x2F, 0xFD, 0x00, 0x00, 0x00, 0x00 });
        var registry = ParserRegistry.CreateDefault();

        var detection = registry.Detect(source, ParseLimits.Default);

        Assert.Equal("raw", detection.Parser.Id);
    }

    private static byte[] BuildCurrentBlend()
    {
        using var file = new MemoryStream();
        file.Write("BLENDER"u8);
        file.Write("17-01v0500"u8);

        WriteBlockV1(file, "GLOB", 0, 0x1000, GlobData().Length, 1, GlobData());
        WriteBlockV1(file, "TEST", 0, 0x2000, 16, 1, ThumbnailData());
        WriteBlockV1(file, "DATA", 0, 0x3000, 72, 2, new byte[72]);
        WriteBlockV1(file, "DNA1", 0, 0x4000, DnaData().Length, 1, DnaData());
        WriteBlockV1(file, "ENDB", 0, 0, 0, 0, []);
        return file.ToArray();
    }

    private static byte[] BuildLegacyBlend(int pointerSize)
    {
        using var file = new MemoryStream();
        file.Write("BLENDER"u8);
        file.WriteByte(pointerSize == 4 ? (byte)'_' : (byte)'-');
        file.WriteByte((byte)'v');
        file.Write("304"u8);

        WriteBlockLegacy(file, pointerSize, "GLOB", 0, 0x1000, GlobData().Length, 1, GlobData());
        WriteBlockLegacy(file, pointerSize, "DNA1", 0, 0x2000, DnaData().Length, 1, DnaData());
        WriteBlockLegacy(file, pointerSize, "ENDB", 0, 0, 0, 0, []);
        return file.ToArray();
    }

    private static byte[] BuildLegacyBlendWithShortEnd()
    {
        using var file = new MemoryStream();
        file.Write("BLENDER"u8);
        file.WriteByte((byte)'-');
        file.WriteByte((byte)'v');
        file.Write("304"u8);
        WriteBlockLegacy(file, 8, "GLOB", 0, 0x1000, GlobData().Length, 1, GlobData());
        WriteBlockLegacy(file, 8, "DNA1", 0, 0x2000, DnaData().Length, 1, DnaData());
        file.Write("ENDB"u8);
        WriteUInt32LE(file, 0);
        return file.ToArray();
    }

    private static byte[] GlobData()
    {
        using var data = new MemoryStream();
        data.Write("0500"u8);
        WriteInt16LE(data, 119);
        WriteInt16LE(data, 405);
        WriteInt16LE(data, 85);
        data.Write(new byte[6]);
        WriteInt32LE(data, 0);
        var filename = new byte[16];
        Encoding.ASCII.GetBytes("test.blend").CopyTo(filename, 0);
        data.Write(filename);
        Assert.Equal(36, data.Length);
        return data.ToArray();
    }

    private static byte[] ThumbnailData()
    {
        using var data = new MemoryStream();
        WriteInt32LE(data, 2);
        WriteInt32LE(data, 1);
        data.Write(new byte[8]);
        return data.ToArray();
    }

    private static byte[] DnaData()
    {
        using var dna = new MemoryStream();
        dna.Write("SDNA"u8);
        dna.Write("NAME"u8);
        var names = new[] { "subvstr[4]", "subversion", "minversion", "minsubversion", "_pad[6]", "fileflags", "filename[16]" };
        WriteUInt32LE(dna, (uint)names.Length);
        foreach (var name in names)
        {
            dna.Write(Encoding.ASCII.GetBytes(name));
            dna.WriteByte(0);
        }

        Pad4(dna);
        dna.Write("TYPE"u8);
        var types = new[] { "char", "short", "int", "FileGlobal" };
        WriteUInt32LE(dna, (uint)types.Length);
        foreach (var type in types)
        {
            dna.Write(Encoding.ASCII.GetBytes(type));
            dna.WriteByte(0);
        }

        Pad4(dna);
        dna.Write("TLEN"u8);
        WriteUInt16LE(dna, 1);
        WriteUInt16LE(dna, 2);
        WriteUInt16LE(dna, 4);
        WriteUInt16LE(dna, 36);
        Pad4(dna);
        dna.Write("STRC"u8);
        WriteUInt32LE(dna, 1);
        WriteUInt16LE(dna, 3); // FileGlobal type
        WriteUInt16LE(dna, 7); // field count
        WriteField(dna, 0, 0); // char subvstr[4]
        WriteField(dna, 1, 1); // short subversion
        WriteField(dna, 1, 2); // short minversion
        WriteField(dna, 1, 3); // short minsubversion
        WriteField(dna, 0, 4); // char _pad[6]
        WriteField(dna, 2, 5); // int fileflags
        WriteField(dna, 0, 6); // char filename[16]
        return dna.ToArray();
    }

    private static void WriteField(Stream stream, ushort type, ushort name)
    {
        WriteUInt16LE(stream, type);
        WriteUInt16LE(stream, name);
    }

    private static void WriteBlockV1(
        Stream stream,
        string code,
        int sdnaIndex,
        ulong oldAddress,
        long length,
        long count,
        byte[] data)
    {
        WriteCode(stream, code);
        WriteInt32LE(stream, sdnaIndex);
        WriteUInt64LE(stream, oldAddress);
        WriteInt64LE(stream, length);
        WriteInt64LE(stream, count);
        stream.Write(data);
    }

    private static void WriteBlockLegacy(
        Stream stream,
        int pointerSize,
        string code,
        int sdnaIndex,
        ulong oldAddress,
        long length,
        long count,
        byte[] data)
    {
        WriteCode(stream, code);
        WriteInt32LE(stream, (int)length);
        if (pointerSize == 4)
        {
            WriteUInt32LE(stream, (uint)oldAddress);
        }
        else
        {
            WriteUInt64LE(stream, oldAddress);
        }

        WriteInt32LE(stream, sdnaIndex);
        WriteInt32LE(stream, (int)count);
        stream.Write(data);
    }

    private static void WriteCode(Stream stream, string code)
    {
        var bytes = new byte[4];
        Encoding.ASCII.GetBytes(code).AsSpan(0, Math.Min(4, code.Length)).CopyTo(bytes);
        stream.Write(bytes);
    }

    private static void Pad4(Stream stream)
    {
        while ((stream.Length & 3) != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void WriteInt16LE(Stream stream, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt16LE(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt32LE(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32LE(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt64LE(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64LE(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
