using System.Buffers.Binary;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Nbt;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;
using UniversalParser.Core.Validation;

namespace UniversalParser.Tests.Formats;

public sealed class NbtParserTests
{
    private const byte ByteType = 1;
    private const byte ShortType = 2;
    private const byte IntType = 3;
    private const byte LongType = 4;
    private const byte FloatType = 5;
    private const byte DoubleType = 6;
    private const byte ByteArrayType = 7;
    private const byte StringType = 8;
    private const byte ListType = 9;
    private const byte CompoundType = 10;
    private const byte IntArrayType = 11;
    private const byte LongArrayType = 12;

    [Fact]
    public void UncompressedCompoundAndAllTagTypesAreParsed()
    {
        using var source = new MemorySource(BuildComprehensiveNbt());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.dat");

        Assert.Equal("nbt", document.Parser.Id);
        Assert.Equal("Named Binary Tag", document.Root.Name);
        var rootTag = Assert.Single(document.Root.Children);
        Assert.Equal("Compound", rootTag.Name);
        Assert.Equal("Compound 'root'", engine.ParseNode(source, document, rootTag).Title);
        Assert.Equal(
            [
                "Byte", "Short", "Int", "Long", "Float", "Double", "String",
                "ByteArray", "IntArray", "LongArray", "List", "List", "Compound",
            ],
            rootTag.Children.Select(static child => child.Name));
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var rootFields = engine.ParseNode(source, document, document.Root).Fields;
        Assert.Contains(rootFields, static field => field.Key == "<RootTagType>" && field.Value == "10 (TAG_Compound)");
        Assert.Contains(rootFields, static field => field.Key == "<ChildCount>" && field.Value == "1");
        Assert.Contains(rootFields, static field => field.Key == "<Note>" && field.Value.Contains("Uncompressed", StringComparison.Ordinal));

        var compoundFields = engine.ParseNode(source, document, rootTag).Fields;
        Assert.Contains(compoundFields, static field => field.Key == "<ChildCount>" && field.Value == "13");
        Assert.Contains(compoundFields, static field => field.Key == "<EndTagPresent>" && field.Value == "true");

        var byteFields = engine.ParseNode(source, document, rootTag.Children[0]).Fields;
        Assert.Contains(byteFields, static field => field.Key == "name" && field.Value == "byte");
        Assert.Contains(byteFields, static field => field.Key == "type" && field.Value == "1 (TAG_Byte)");
        Assert.Contains(byteFields, static field => field.Key == "value" && field.Value == "-5");
        Assert.Contains(byteFields, static field => field.Key == "<UnsignedValue>" && field.Value == "251");

        var shortFields = engine.ParseNode(source, document, rootTag.Children[1]).Fields;
        Assert.Contains(shortFields, static field => field.Key == "value" && field.Value == "300");
        var intFields = engine.ParseNode(source, document, rootTag.Children[2]).Fields;
        Assert.Contains(intFields, static field => field.Key == "value" && field.Value == "70000");
        var longFields = engine.ParseNode(source, document, rootTag.Children[3]).Fields;
        Assert.Contains(longFields, static field => field.Key == "value" && field.Value == "1234567890123");

        var floatFields = engine.ParseNode(source, document, rootTag.Children[4]).Fields;
        Assert.Contains(floatFields, static field => field.Key == "value" && field.Value == "1.5");
        Assert.Contains(floatFields, static field => field.Key == "<RawBits>" && field.Value == "0x3FC00000");
        var doubleFields = engine.ParseNode(source, document, rootTag.Children[5]).Fields;
        Assert.Contains(doubleFields, static field => field.Key == "value" && field.Value == "2.25");
        Assert.Contains(doubleFields, static field => field.Key == "<RawBits>" && field.Value == "0x4002000000000000");

        var stringFields = engine.ParseNode(source, document, rootTag.Children[6]).Fields;
        Assert.Contains(stringFields, static field => field.Key == "length" && field.Value == "10");
        Assert.Contains(stringFields, static field => field.Key == "value" && field.Value == "Hello, NBT");

        var byteArrayFields = engine.ParseNode(source, document, rootTag.Children[7]).Fields;
        Assert.Contains(byteArrayFields, static field => field.Key == "length" && field.Value == "4");
        Assert.Contains(byteArrayFields, static field => field.Key == "<HexPreview>" && field.Value == "01 02 03 04");

        var intArrayFields = engine.ParseNode(source, document, rootTag.Children[8]).Fields;
        Assert.Contains(intArrayFields, static field => field.Key == "length" && field.Value == "2");
        Assert.Contains(intArrayFields, static field => field.Key == "<PreviewValues>" && field.Value == "10, -20");

        var longArrayFields = engine.ParseNode(source, document, rootTag.Children[9]).Fields;
        Assert.Contains(longArrayFields, static field => field.Key == "length" && field.Value == "2");
        Assert.Contains(longArrayFields, static field => field.Key == "<PreviewValues>" && field.Value == "30, -40");

        var list = rootTag.Children[10];
        Assert.Equal(3, list.Children.Length);
        Assert.All(list.Children, static child => Assert.Equal("Int", child.Name));
        var listFields = engine.ParseNode(source, document, list).Fields;
        Assert.Contains(listFields, static field => field.Key == "element_type" && field.Value == "3 (TAG_Int)");
        Assert.Contains(listFields, static field => field.Key == "length" && field.Value == "3");
        Assert.Contains(listFields, static field => field.Key == "<ChildCount>" && field.Value == "3");
        Assert.Equal("Int '[0]'", engine.ParseNode(source, document, list.Children[0]).Title);
        var listElementFields = engine.ParseNode(source, document, list.Children[0]).Fields;
        Assert.Contains(listElementFields, static field => field.Key == "<ElementType>" && field.Value == "3 (TAG_Int)");
        Assert.Contains(listElementFields, static field => field.Key == "<Index>" && field.Value == "0");
        Assert.Contains(listElementFields, static field => field.Key == "value" && field.Value == "1");

        var compoundList = rootTag.Children[11];
        var compoundElement = Assert.Single(compoundList.Children);
        Assert.Equal("Compound", compoundElement.Name);
        Assert.Equal("Compound '[0]'", engine.ParseNode(source, document, compoundElement).Title);
        Assert.Equal("String 'name'", engine.ParseNode(source, document, Assert.Single(compoundElement.Children)).Title);

        var nested = rootTag.Children[12];
        Assert.Equal("Compound 'nested'", engine.ParseNode(source, document, nested).Title);
        Assert.Equal("Byte 'b'", engine.ParseNode(source, document, Assert.Single(nested.Children)).Title);
    }

    [Fact]
    public void EmptyCompoundIsDetectedAndParsed()
    {
        using var source = new MemorySource(new byte[] { 0x0A, 0x00, 0x00, 0x00 });
        var registry = ParserRegistry.CreateDefault();
        var detection = registry.Detect(source, ParseLimits.Default);
        Assert.Equal("nbt", detection.Parser.Id);
        Assert.Equal(ProbeConfidence.Strong, detection.Probe.Confidence);

        var engine = new ParserEngine(registry);
        var document = engine.Parse(source, "empty.dat");
        var rootTag = Assert.Single(document.Root.Children);
        Assert.Equal("Compound", rootTag.Name);
        Assert.Equal("Compound ''", engine.ParseNode(source, document, rootTag).Title);
        var fields = engine.ParseNode(source, document, rootTag).Fields;
        Assert.Contains(fields, static field => field.Key == "<ChildCount>" && field.Value == "0");
        Assert.Contains(fields, static field => field.Key == "<EndTagPresent>" && field.Value == "true");
    }

    [Fact]
    public void EmptyListIsValidAndCovered()
    {
        using var stream = new MemoryStream();
        WriteRootHeader(stream, string.Empty);
        WriteList(stream, "list", 0, 0, static _ => { });
        stream.WriteByte(0);

        using var source = new MemorySource(stream.ToArray());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "empty-list.dat");
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var rootTag = Assert.Single(document.Root.Children);
        var list = Assert.Single(rootTag.Children);
        Assert.Equal("List", list.Name);
        Assert.Empty(list.Children);
        var fields = engine.ParseNode(source, document, list).Fields;
        Assert.Contains(fields, static field => field.Key == "element_type" && field.Value == "0 (TAG_End)");
        Assert.Contains(fields, static field => field.Key == "length" && field.Value == "0");
        Assert.Contains(fields, static field => field.Key == "<Note>" && field.Value.Contains("Empty TAG_List", StringComparison.Ordinal));
    }

    [Fact]
    public void CompressedAndRegionFilesStayRaw()
    {
        var registry = ParserRegistry.CreateDefault();

        using (var source = new MemorySource(new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00 }))
        {
            Assert.Equal("raw", registry.Detect(source, ParseLimits.Default).Parser.Id);
        }

        using (var source = new MemorySource(new byte[] { 0x78, 0x9C, 0x01, 0x00, 0x00, 0x00 }))
        {
            Assert.Equal("raw", registry.Detect(source, ParseLimits.Default).Parser.Id);
        }

        using (var source = new MemorySource(new byte[8192]))
        {
            Assert.Equal("raw", registry.Detect(source, ParseLimits.Default).Parser.Id);
        }

        using (var source = new MemorySource(new byte[] { 0x0A, 0x00, 0x00, 0x01 }))
        {
            Assert.Equal("raw", registry.Detect(source, ParseLimits.Default).Parser.Id);
        }
    }

    [Fact]
    public void MissingEndTagAndTruncatedValuesAreDiagnosed()
    {
        using var source = new MemorySource(BuildMissingEndNbt());
        var parser = new NbtParser();
        var context = new ParseContext(source, ParseLimits.Default);
        var root = parser.Parse(context);

        Assert.Contains(context.Diagnostics.Items, static diagnostic => diagnostic.Code == DiagnosticCodes.TruncatedChild);
        Assert.Empty(NodeTreeValidator.Validate(root, source.Length, requirePayloadCoverage: true));

        var compound = Assert.Single(root.Children);
        Assert.Equal("Compound", compound.Name);
        var byteTag = Assert.Single(compound.Children);
        Assert.Equal("Byte", byteTag.Name);
        var compoundFields = parser.ParseNode(new ParseContext(source, ParseLimits.Default), compound).Fields;
        Assert.Contains(compoundFields, static field => field.Key == "<EndTagPresent>" && field.Value == "false");
        Assert.Contains(compoundFields, static field => field.Key == "<Warning>");
    }

    [Fact]
    public void NegativeArrayLengthIsRejected()
    {
        using var source = new MemorySource(BuildNegativeArrayLengthNbt());
        var parser = new NbtParser();
        var context = new ParseContext(source, ParseLimits.Default);
        var root = parser.Parse(context);

        Assert.Contains(context.Diagnostics.Items, static diagnostic => diagnostic.Code == DiagnosticCodes.InvalidLength);
        Assert.Empty(NodeTreeValidator.Validate(root, source.Length, requirePayloadCoverage: true));

        var compound = Assert.Single(root.Children);
        var array = Assert.Single(compound.Children);
        Assert.Equal("ByteArray", array.Name);
        Assert.Equal(NodeKind.Unparsed, array.Kind);
    }

    [Fact]
    public void ProbeRecognizesUncompressedCompound()
    {
        using var source = new MemorySource(BuildComprehensiveNbt());
        var detection = ParserRegistry.CreateDefault().Detect(source, ParseLimits.Default);
        Assert.Equal("nbt", detection.Parser.Id);
        Assert.Equal(ProbeConfidence.Strong, detection.Probe.Confidence);
    }

    private static byte[] BuildComprehensiveNbt()
    {
        using var stream = new MemoryStream();
        WriteRootHeader(stream, "root");
        WriteByte(stream, "byte", -5);
        WriteShort(stream, "short", 300);
        WriteInt(stream, "int", 70000);
        WriteLong(stream, "long", 1234567890123);
        WriteFloat(stream, "float", 1.5f);
        WriteDouble(stream, "double", 2.25);
        WriteString(stream, "string", "Hello, NBT");
        WriteByteArray(stream, "bytes", [1, 2, 3, 4]);
        WriteIntArray(stream, "ints", [10, -20]);
        WriteLongArray(stream, "longs", [30, -40]);
        WriteList(stream, "list", IntType, 3, (list) =>
        {
            WriteIntPayload(list, 1);
            WriteIntPayload(list, 2);
            WriteIntPayload(list, 3);
        });
        WriteList(stream, "compounds", CompoundType, 1, (list) =>
        {
            WriteString(list, "name", "x");
            list.WriteByte(0);
        });
        WriteCompound(stream, "nested", (nested) => WriteByte(nested, "b", 1));
        stream.WriteByte(0);
        return stream.ToArray();
    }

    private static byte[] BuildMissingEndNbt()
    {
        using var stream = new MemoryStream();
        WriteRootHeader(stream, string.Empty);
        WriteByte(stream, "b", 5);
        return stream.ToArray();
    }

    private static byte[] BuildNegativeArrayLengthNbt()
    {
        using var stream = new MemoryStream();
        WriteRootHeader(stream, string.Empty);
        WriteTagHeader(stream, ByteArrayType, "arr");
        WriteInt32BE(stream, -1);
        stream.WriteByte(0);
        return stream.ToArray();
    }

    private static void WriteRootHeader(Stream stream, string name) =>
        WriteTagHeader(stream, CompoundType, name);

    private static void WriteTagHeader(Stream stream, byte type, string name)
    {
        stream.WriteByte(type);
        var bytes = Encoding.UTF8.GetBytes(name);
        Assert.InRange(bytes.Length, 0, ushort.MaxValue);
        WriteUInt16BE(stream, (ushort)bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteByte(Stream stream, string name, sbyte value)
    {
        WriteTagHeader(stream, ByteType, name);
        stream.WriteByte(unchecked((byte)value));
    }

    private static void WriteShort(Stream stream, string name, short value)
    {
        WriteTagHeader(stream, ShortType, name);
        WriteUInt16BE(stream, unchecked((ushort)value));
    }

    private static void WriteInt(Stream stream, string name, int value)
    {
        WriteTagHeader(stream, IntType, name);
        WriteInt32BE(stream, value);
    }

    private static void WriteIntPayload(Stream stream, int value) => WriteInt32BE(stream, value);

    private static void WriteLong(Stream stream, string name, long value)
    {
        WriteTagHeader(stream, LongType, name);
        WriteInt64BE(stream, value);
    }

    private static void WriteFloat(Stream stream, string name, float value)
    {
        WriteTagHeader(stream, FloatType, name);
        WriteUInt32BE(stream, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }

    private static void WriteDouble(Stream stream, string name, double value)
    {
        WriteTagHeader(stream, DoubleType, name);
        WriteUInt64BE(stream, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
    }

    private static void WriteString(Stream stream, string name, string value)
    {
        WriteTagHeader(stream, StringType, name);
        var bytes = Encoding.UTF8.GetBytes(value);
        Assert.InRange(bytes.Length, 0, ushort.MaxValue);
        WriteUInt16BE(stream, (ushort)bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteByteArray(Stream stream, string name, byte[] value)
    {
        WriteTagHeader(stream, ByteArrayType, name);
        WriteInt32BE(stream, value.Length);
        stream.Write(value);
    }

    private static void WriteIntArray(Stream stream, string name, int[] value)
    {
        WriteTagHeader(stream, IntArrayType, name);
        WriteInt32BE(stream, value.Length);
        foreach (var item in value)
        {
            WriteInt32BE(stream, item);
        }
    }

    private static void WriteLongArray(Stream stream, string name, long[] value)
    {
        WriteTagHeader(stream, LongArrayType, name);
        WriteInt32BE(stream, value.Length);
        foreach (var item in value)
        {
            WriteInt64BE(stream, item);
        }
    }

    private static void WriteList(Stream stream, string name, byte elementType, int count, Action<Stream> writeElements)
    {
        WriteTagHeader(stream, ListType, name);
        stream.WriteByte(elementType);
        WriteInt32BE(stream, count);
        writeElements(stream);
    }

    private static void WriteCompound(Stream stream, string name, Action<Stream> writeChildren)
    {
        WriteTagHeader(stream, CompoundType, name);
        writeChildren(stream);
        stream.WriteByte(0);
    }

    private static void WriteUInt16BE(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32BE(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt32BE(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt64BE(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64BE(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
