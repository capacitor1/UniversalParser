using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Protobuf;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;
using UniversalParser.Core.Validation;

namespace UniversalParser.Tests.Formats;

public sealed class ProtobufParserTests
{
    [Theory]
    [InlineData(0UL, 0L)]
    [InlineData(1UL, -1L)]
    [InlineData(2UL, 1L)]
    [InlineData(3UL, -2L)]
    [InlineData(4294967295UL, -2147483648L)]
    [InlineData(18446744073709551614UL, long.MaxValue)]
    [InlineData(ulong.MaxValue, long.MinValue)]
    public void ZigZagPreviewPreservesRawVarint(ulong encoded, long decoded)
    {
        using var stream = new MemoryStream();
        WriteKey(stream, 1, 0);
        WriteVarint(stream, encoded);
        using var source = new MemorySource(stream.ToArray());
        var parser = new ProtobufParser();
        var root = parser.Parse(new ParseContext(source, ParseLimits.Default));
        var node = Assert.Single(root.Children);
        var result = parser.ParseNode(new ParseContext(source, ParseLimits.Default), node);

        Assert.Contains(result.Fields, field => field.Key == "value"
            && field.Kind == FieldKind.Raw && field.Value == encoded.ToString(CultureInfo.InvariantCulture));
        var preview = Assert.Single(result.Fields, static field => field.Key == "<ZigZagValue>");
        Assert.Equal(FieldKind.Derived, preview.Kind);
        Assert.Equal(decoded.ToString(CultureInfo.InvariantCulture), preview.Value);
        Assert.Equal(result.Fields.Single(static field => field.Key == "value").Range, preview.Range);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(0x3FC00000U, "1.5")]
    [InlineData(0x80000000U, "-0")]
    [InlineData(0x7F800000U, "Infinity")]
    [InlineData(0xFF800000U, "-Infinity")]
    [InlineData(0x7FC00000U, "NaN")]
    public void Fixed32PreviewInterpretsIeee754Bits(uint bits, string expected)
    {
        Span<byte> bytes = stackalloc byte[5];
        bytes[0] = 0x0D;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[1..], bits);
        using var source = new MemorySource(bytes.ToArray());
        AssertFixedPreview(source, "<FloatValue>", expected, $"0x{bits:X8}");
    }

    [Theory]
    [InlineData(0x3FF8000000000000UL, "1.5")]
    [InlineData(0x8000000000000000UL, "-0")]
    [InlineData(0x7FF0000000000000UL, "Infinity")]
    [InlineData(0xFFF0000000000000UL, "-Infinity")]
    [InlineData(0x7FF8000000000000UL, "NaN")]
    public void Fixed64PreviewInterpretsIeee754Bits(ulong bits, string expected)
    {
        Span<byte> bytes = stackalloc byte[9];
        bytes[0] = 0x09;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[1..], bits);
        using var source = new MemorySource(bytes.ToArray());
        AssertFixedPreview(source, "<DoubleValue>", expected, $"0x{bits:X16}");
    }

    private static void AssertFixedPreview(MemorySource source, string key, string expected, string rawValue)
    {
        var parser = new ProtobufParser();
        var root = parser.Parse(new ParseContext(source, ParseLimits.Default));
        var result = parser.ParseNode(new ParseContext(source, ParseLimits.Default), Assert.Single(root.Children));
        var raw = Assert.Single(result.Fields, static field => field.Key == "value");
        Assert.Equal(rawValue, raw.Value);
        Assert.Equal(FieldKind.Raw, raw.Kind);
        var preview = Assert.Single(result.Fields, field => field.Key == key);
        Assert.Equal(expected, preview.Value);
        Assert.Equal(FieldKind.Derived, preview.Kind);
        Assert.Equal(raw.Range, preview.Range);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void RawFieldsAndNestedMessageAreParsed()
    {
        using var source = new MemorySource(BuildMessage());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.bin");

        Assert.Equal("protobuf", document.Parser.Id);
        Assert.Equal("Protocol Buffers", document.Root.Name);
        Assert.Equal(
            [
                "varint",
                "length-delimited",
                "fixed64",
                "fixed32",
                "length-delimited",
            ],
            document.Root.Children.Select(static child => child.Name));
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var rootFields = engine.ParseNode(source, document, document.Root).Fields;
        Assert.Contains(rootFields, static field => field.Key == "<FileSize>" && field.Value == "31");
        Assert.Contains(rootFields, static field => field.Key == "<TopLevelFieldCount>" && field.Value == "5");

        var varint = engine.ParseNode(source, document, document.Root.Children[0]).Fields;
        Assert.Equal("VarintField '1'", engine.ParseNode(source, document, document.Root.Children[0]).Title);
        Assert.Contains(varint, static field => field.Key == "field_number" && field.Value == "1");
        Assert.Contains(varint, static field => field.Key == "wire_type" && field.Value == "0 (varint)");
        Assert.Contains(varint, static field => field.Key == "key" && field.Value == "0x08");
        Assert.Contains(varint, static field => field.Key == "value" && field.Value == "150");

        var text = engine.ParseNode(source, document, document.Root.Children[1]).Fields;
        Assert.Contains(text, static field => field.Key == "wire_type" && field.Value == "2 (length-delimited)");
        Assert.Contains(text, static field => field.Key == "length" && field.Value == "5");
        Assert.Contains(text, static field => field.Key == "<PayloadKind>" && field.Value == "string");
        Assert.Contains(text, static field => field.Key == "<Text>" && field.Value == "hello");

        var fixed64 = engine.ParseNode(source, document, document.Root.Children[2]).Fields;
        Assert.Contains(fixed64, static field => field.Key == "value" && field.Value == "0x1122334455667788");
        Assert.Contains(fixed64, static field => field.Key == "<UnsignedValue>" && field.Value == "1234605616436508552");

        var fixed32 = engine.ParseNode(source, document, document.Root.Children[3]).Fields;
        Assert.Contains(fixed32, static field => field.Key == "value" && field.Value == "0xDEADBEEF");
        Assert.Contains(fixed32, static field => field.Key == "<UnsignedValue>" && field.Value == "3735928559");

        var nested = document.Root.Children[4];
        Assert.Equal(2, nested.Children.Length);
        Assert.Equal(["varint", "length-delimited"], nested.Children.Select(static child => child.Name));
        var nestedFields = engine.ParseNode(source, document, nested).Fields;
        Assert.Contains(nestedFields, static field => field.Key == "<PayloadKind>" && field.Value == "message");
        Assert.Contains(nestedFields, static field => field.Key == "<FieldCount>" && field.Value == "2");

        var nestedVarint = engine.ParseNode(source, document, nested.Children[0]).Fields;
        Assert.Contains(nestedVarint, static field => field.Key == "value" && field.Value == "7");
    }

    [Fact]
    public void GroupsAreParsedIncludingTheirEndGroupKey()
    {
        using var source = new MemorySource(BuildGroup());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "group.bin");

        var group = Assert.Single(document.Root.Children);
        Assert.Equal("start-group", group.Name);
        Assert.Equal(4, group.Range.Length);
        Assert.Equal(2, group.PayloadRange?.Length);
        Assert.Equal(["varint"], group.Children.Select(static child => child.Name));
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var fields = engine.ParseNode(source, document, group).Fields;
        Assert.Equal("GroupField '6'", engine.ParseNode(source, document, group).Title);
        Assert.Contains(fields, static field => field.Key == "field_number" && field.Value == "6");
        Assert.Contains(fields, static field => field.Key == "wire_type" && field.Value == "3 (start-group)");
        Assert.Contains(fields, static field => field.Key == "<PayloadLength>" && field.Value == "2");
        Assert.Contains(fields, static field => field.Key == "end_group_field_number" && field.Value == "6");
    }

    [Fact]
    public void UnterminatedGroupIsDiagnosedAndClamped()
    {
        using var source = new MemorySource(BuildUnterminatedGroup());
        var parser = new ProtobufParser();
        var context = new ParseContext(source, ParseLimits.Default);
        var root = parser.Parse(context);

        var group = Assert.Single(root.Children);
        Assert.Equal("start-group", group.Name);
        Assert.Equal(group.Range.End, group.PayloadRange?.End);
        Assert.Contains(context.Diagnostics.Items, static diagnostic => diagnostic.Code == DiagnosticCodes.TruncatedPayload);

        var fields = parser.ParseNode(new ParseContext(source, ParseLimits.Default), group).Fields;
        Assert.Contains(fields, static field => field.Key == "<Warning>" && field.Value.Contains("no matching end-group", StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatedFieldNumbersStaySeparateSiblings()
    {
        using var source = new MemorySource(new byte[] { 0x08, 0x01, 0x08, 0x02 });
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "repeated.bin");

        Assert.Equal(2, document.Root.Children.Length);
        Assert.All(document.Root.Children, static child => Assert.Equal("varint", child.Name));
        Assert.Equal(
            ["1", "2"],
            document.Root.Children.Select(child => engine.ParseNode(source, document, child).Fields
                .Single(static field => field.Key == "value").Value));
    }

    [Fact]
    public void TruncatedLengthDelimitedPayloadIsUnparsedWithDiagnostic()
    {
        using var source = new MemorySource(new byte[] { 0x0A, 0x05, 0x61, 0x62 });
        var parser = new ProtobufParser();
        var context = new ParseContext(source, ParseLimits.Default);
        var root = parser.Parse(context);

        var node = Assert.Single(root.Children);
        Assert.Equal("length-delimited", node.Name);
        Assert.Equal(NodeKind.Unparsed, node.Kind);
        Assert.Equal(4, node.Range.Length);
        Assert.Contains(context.Diagnostics.Items, static diagnostic => diagnostic.Code == DiagnosticCodes.TruncatedPayload);
        Assert.Empty(NodeTreeValidator.Validate(root, source.Length, requirePayloadCoverage: true));

        var fields = parser.ParseNode(new ParseContext(source, ParseLimits.Default), node).Fields;
        Assert.Contains(fields, static field => field.Key == "length" && field.Value == "5");
        Assert.Contains(fields, static field => field.Key == "<Warning>");
    }

    [Fact]
    public void InvalidKeyStopsParsingWithUnparsedRemainder()
    {
        using var source = new MemorySource(new byte[] { 0x00, 0x01, 0x02 });
        var parser = new ProtobufParser();
        var context = new ParseContext(source, ParseLimits.Default);
        var root = parser.Parse(context);

        var node = Assert.Single(root.Children);
        Assert.Equal("<unparsed data>", node.Name);
        Assert.Equal(3, node.Range.Length);
        Assert.Contains(context.Diagnostics.Items, static diagnostic => diagnostic.Code == DiagnosticCodes.InvalidFieldNumber);
    }

    [Fact]
    public void InvalidVarintAndUnexpectedEndGroupAreDiagnosed()
    {
        using (var source = new MemorySource(new byte[] { 0x08, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 }))
        {
            var parser = new ProtobufParser();
            var context = new ParseContext(source, ParseLimits.Default);
            _ = parser.Parse(context);
            Assert.Contains(context.Diagnostics.Items, static diagnostic => diagnostic.Code == DiagnosticCodes.InvalidVint);
        }

        using (var source = new MemorySource(new byte[] { 0x0C }))
        {
            var parser = new ProtobufParser();
            var context = new ParseContext(source, ParseLimits.Default);
            _ = parser.Parse(context);
            Assert.Contains(context.Diagnostics.Items, static diagnostic => diagnostic.Code == DiagnosticCodes.SchemaMismatch);
        }
    }

    [Fact]
    public void ProbeAcceptsMessageWhoseFieldCrossesTheProbeWindow()
    {
        var payload = new byte[600 * 1024];
        Array.Fill(payload, (byte)'A');
        using var stream = new MemoryStream();
        WriteKey(stream, 1, 2);
        WriteVarint(stream, (ulong)payload.Length);
        stream.Write(payload);

        using var source = new MemorySource(stream.ToArray());
        var registry = ParserRegistry.CreateDefault();
        var detection = registry.Detect(source, ParseLimits.Default);

        Assert.Equal("protobuf", detection.Parser.Id);
        Assert.Equal(ProbeConfidence.Weak, detection.Probe.Confidence);
    }

    [Fact]
    public void OpaqueBytePayloadIsKeptAsBytes()
    {
        using var source = new MemorySource(new byte[] { 0x0A, 0x03, 0xFF, 0xFE, 0xFD });
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "bytes.bin");

        var node = Assert.Single(document.Root.Children);
        Assert.Equal("length-delimited", node.Name);
        Assert.Equal(NodeKind.Leaf, node.Kind);
        Assert.Empty(node.Children);

        var fields = engine.ParseNode(source, document, node).Fields;
        Assert.Contains(fields, static field => field.Key == "<PayloadKind>" && field.Value == "bytes");
        Assert.Contains(fields, static field => field.Key == "<HexPreview>" && field.Value == "FF FE FD");
    }

    [Fact]
    public void ProbeRecognizesProtobufAndRejectsUnrelatedBytes()
    {
        var registry = ParserRegistry.CreateDefault();

        using (var source = new MemorySource(BuildMessage()))
        {
            var detection = registry.Detect(source, ParseLimits.Default);
            Assert.Equal("protobuf", detection.Parser.Id);
            Assert.Equal(ProbeConfidence.Strong, detection.Probe.Confidence);
        }

        using (var source = new MemorySource(new byte[] { 0x0A, 0x01, 0x41 }))
        {
            var detection = registry.Detect(source, ParseLimits.Default);
            Assert.Equal("protobuf", detection.Parser.Id);
            Assert.Equal(ProbeConfidence.Weak, detection.Probe.Confidence);
        }

        using (var source = new MemorySource(new byte[] { 0x08, 0x96, 0x01 }))
        {
            var detection = registry.Detect(source, ParseLimits.Default);
            Assert.Equal("protobuf", detection.Parser.Id);
            Assert.Equal(ProbeConfidence.Weak, detection.Probe.Confidence);
        }

        using (var source = new MemorySource(Encoding.ASCII.GetBytes("hello world")))
        {
            var detection = registry.Detect(source, ParseLimits.Default);
            Assert.Equal("raw", detection.Parser.Id);
        }

        using (var source = new MemorySource(new byte[16]))
        {
            var detection = registry.Detect(source, ParseLimits.Default);
            Assert.Equal("raw", detection.Parser.Id);
        }
    }

    private static byte[] BuildMessage()
    {
        using var stream = new MemoryStream();
        WriteKey(stream, 1, 0);
        WriteVarint(stream, 150);
        var text = Encoding.UTF8.GetBytes("hello");
        WriteKey(stream, 2, 2);
        WriteVarint(stream, (ulong)text.Length);
        stream.Write(text);
        WriteKey(stream, 3, 1);
        stream.Write([0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11]);
        WriteKey(stream, 4, 5);
        stream.Write([0xEF, 0xBE, 0xAD, 0xDE]);

        using var nested = new MemoryStream();
        WriteKey(nested, 1, 0);
        WriteVarint(nested, 7);
        var nestedText = Encoding.UTF8.GetBytes("x");
        WriteKey(nested, 2, 2);
        WriteVarint(nested, (ulong)nestedText.Length);
        nested.Write(nestedText);
        WriteKey(stream, 5, 2);
        WriteVarint(stream, (ulong)nested.Length);
        nested.Position = 0;
        nested.CopyTo(stream);
        return stream.ToArray();
    }

    private static byte[] BuildGroup()
    {
        using var stream = new MemoryStream();
        WriteKey(stream, 6, 3);
        WriteKey(stream, 1, 0);
        WriteVarint(stream, 1);
        WriteKey(stream, 6, 4);
        return stream.ToArray();
    }

    private static byte[] BuildUnterminatedGroup()
    {
        using var stream = new MemoryStream();
        WriteKey(stream, 6, 3);
        WriteKey(stream, 1, 0);
        WriteVarint(stream, 1);
        return stream.ToArray();
    }

    private static void WriteKey(Stream stream, uint fieldNumber, int wireType)
    {
        WriteVarint(stream, ((ulong)fieldNumber << 3) | (uint)wireType);
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[10];
        var count = 0;
        do
        {
            var item = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                item |= 0x80;
            }

            buffer[count++] = item;
        }
        while (value != 0);

        stream.Write(buffer[..count]);
    }
}
