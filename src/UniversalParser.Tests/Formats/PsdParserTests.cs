using System.Buffers.Binary;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;
using UniversalParser.Core.Validation;

namespace UniversalParser.Tests.Formats;

public sealed class PsdParserTests
{
    [Fact]
    public void PsdSectionsAndImageResourceAreListed()
    {
        using var source = new MemorySource(BuildPsd());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.psd");

        Assert.Equal("psd", document.Parser.Id);
        Assert.Equal("Adobe Photoshop Document", document.Root.Name);
        Assert.Equal(
            new[] { "FileHeader", "ColorModeData", "ImageResources", "LayerAndMaskInformation", "ImageData" },
            document.Root.Children.Select(static child => child.Name));
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var header = engine.ParseNode(source, document, document.Root.Children[0]).Fields;
        Assert.Contains(header, static field => field.Key == "Version" && field.Value == "1 (PSD)");
        Assert.Contains(header, static field => field.Key == "Channels" && field.Value == "3");
        Assert.Contains(header, static field => field.Key == "Width" && field.Value == "1");
        Assert.Contains(header, static field => field.Key == "Height" && field.Value == "1");
        Assert.Contains(header, static field => field.Key == "Color Mode" && field.Value == "3 (RGB)");

        var resources = document.Root.Children[2];
        var resource = Assert.Single(resources.Children);
        Assert.StartsWith("Resource[0]", resource.Name, StringComparison.Ordinal);
        var resourceFields = engine.ParseNode(source, document, resource).Fields;
        Assert.Contains("1005 (", resource.Name, StringComparison.Ordinal);
        Assert.Contains(resourceFields, static field => field.Key == "hResUnit" && field.Value == "1 (pixels per inch)");
        Assert.Contains(resourceFields, static field => field.Key == "vResUnit" && field.Value == "1 (pixels per inch)");
    }

    [Fact]
    public void PsbLayerRecordChannelDataAndAdditionalInfoAreListed()
    {
        using var source = new MemorySource(BuildPsb());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.psb");

        Assert.Equal("psd", document.Parser.Id);
        Assert.Empty(NodeTreeValidator.Validate(document.Root, source.Length, requirePayloadCoverage: true));

        var header = engine.ParseNode(source, document, document.Root.Children[0]).Fields;
        Assert.Contains(header, static field => field.Key == "Version" && field.Value == "2 (PSB, large document format)");

        var layerAndMask = document.Root.Children[3];
        var layerInfo = Assert.Single(
            layerAndMask.Children,
            static child => child.Name == "LayerInfo");
        Assert.Equal("LayerCount", layerInfo.Children[0].Name);
        var records = Assert.Single(layerInfo.Children, static child => child.Name == "LayerRecords");
        var record = Assert.Single(records.Children);
        Assert.Equal(
            new[] { "LayerRecordHeader", "LayerMaskData", "LayerBlendingRanges", "LayerName", "AdditionalInfo 'luni'" },
            record.Children.Select(static child => child.Name));

        var recordHeader = engine.ParseNode(source, document, record.Children[0]).Fields;
        Assert.Contains(recordHeader, static field => field.Key == "Top" && field.Value == "0");
        Assert.Contains(recordHeader, static field => field.Key == "Bottom" && field.Value == "1");
        Assert.Contains(recordHeader, static field => field.Key == "Number of channels" && field.Value == "1");
        Assert.Contains(recordHeader, static field => field.Key == "Channel ID[0]" && field.Value == "0 (Red)");
        Assert.Contains(recordHeader, static field => field.Key == "Channel data length[0]" && field.Value == "4");
        Assert.Contains(recordHeader, static field => field.Key == "Blend mode key" && field.Value == "norm (Normal)");
        Assert.Contains(recordHeader, static field => field.Key == "Opacity" && field.Value == "255");

        var nameFields = engine.ParseNode(source, document, record.Children[3]).Fields;
        Assert.Contains(nameFields, static field => field.Key == "Layer name" && field.Value == "Layer");

        var unicodeFields = engine.ParseNode(source, document, record.Children[4]).Fields;
        Assert.Contains(unicodeFields, static field => field.Key == "Key" && field.Value == "luni (Unicode layer name)");
        Assert.Contains(unicodeFields, static field => field.Key == "Unicode layer name" && field.Value == "レイヤー");

        var channelContainer = Assert.Single(layerInfo.Children, static child => child.Name == "ChannelImageData");
        var channel = Assert.Single(channelContainer.Children);
        var channelFields = engine.ParseNode(source, document, channel).Fields;
        Assert.Contains(channelFields, static field => field.Key == "Compression" && field.Value == "0 (Raw)");
    }

    [Fact]
    public void RegistryDetectsPhotoshopDocument()
    {
        using var source = new MemorySource(BuildPsd());
        var registry = ParserRegistry.CreateDefault();

        var detection = registry.Detect(source, ParseLimits.Default);

        Assert.Equal("psd", detection.Parser.Id);
        Assert.Equal(ProbeConfidence.Strong, detection.Probe.Confidence);
    }

    private static byte[] BuildPsd()
    {
        using var file = new MemoryStream();
        WriteHeader(file, version: 1, channels: 3, width: 1, height: 1);

        WriteUInt32(file, 0); // Color mode data
        WriteUInt32(file, 28); // Image resources length
        WriteResolutionResource(file);
        WriteUInt32(file, 0); // Layer and mask information
        WriteUInt16(file, 0); // Raw image data
        file.Write([0x10, 0x20, 0x30]);
        return file.ToArray();
    }

    private static byte[] BuildPsb()
    {
        using var file = new MemoryStream();
        WriteHeader(file, version: 2, channels: 1, width: 2, height: 1);

        WriteUInt32(file, 0); // Color mode data
        WriteUInt32(file, 0); // Image resources

        using var layerInfoData = new MemoryStream();
        WriteInt16(layerInfoData, 1); // Layer count
        WriteUInt32(layerInfoData, 0); // Top
        WriteUInt32(layerInfoData, 0); // Left
        WriteUInt32(layerInfoData, 1); // Bottom
        WriteUInt32(layerInfoData, 2); // Right
        WriteUInt16(layerInfoData, 1); // Number of channels
        WriteInt16(layerInfoData, 0); // Channel ID
        WriteUInt64(layerInfoData, 4); // Channel data length (compression + 2 pixels)
        WriteAscii(layerInfoData, "8BIM");
        WriteAscii(layerInfoData, "norm");
        layerInfoData.WriteByte(255); // Opacity
        layerInfoData.WriteByte(0); // Clipping
        layerInfoData.WriteByte(0); // Flags
        layerInfoData.WriteByte(0); // Filler

        using var extra = new MemoryStream();
        WriteUInt32(extra, 0); // Layer mask data
        WriteUInt32(extra, 0); // Layer blending ranges
        WritePascalName(extra, "Layer");
        WriteAscii(extra, "8BIM");
        WriteAscii(extra, "luni");
        WriteUInt32(extra, 12); // Unicode layer name length
        WriteUInt32(extra, 4); // Four UTF-16 code units
        extra.Write(Encoding.BigEndianUnicode.GetBytes("レイヤー"));

        var extraBytes = extra.ToArray();
        WriteUInt32(layerInfoData, (uint)extraBytes.Length);
        layerInfoData.Write(extraBytes);

        layerInfoData.Write([0, 0]); // Channel data: compression 0 + two pixels

        using var layerAndMask = new MemoryStream();
        var layerInfoBytes = layerInfoData.ToArray();
        WriteUInt64(layerAndMask, (ulong)layerInfoBytes.Length);
        layerAndMask.Write(layerInfoBytes);
        WriteUInt32(layerAndMask, 0); // Global layer mask info

        var layerAndMaskBytes = layerAndMask.ToArray();
        WriteUInt64(file, (ulong)layerAndMaskBytes.Length);
        file.Write(layerAndMaskBytes);

        WriteUInt16(file, 0); // Raw image data
        file.Write([0x10, 0x20]);
        return file.ToArray();
    }

    private static void WriteHeader(Stream stream, ushort version, ushort channels, uint width, uint height)
    {
        WriteAscii(stream, "8BPS");
        WriteUInt16(stream, version);
        stream.Write(new byte[6]);
        WriteUInt16(stream, channels);
        WriteUInt32(stream, height);
        WriteUInt32(stream, width);
        WriteUInt16(stream, 8);
        WriteUInt16(stream, 3);
    }

    private static void WriteResolutionResource(Stream stream)
    {
        WriteAscii(stream, "8BIM");
        WriteUInt16(stream, 1005);
        stream.WriteByte(0); // Empty Pascal name
        stream.WriteByte(0); // Name padding
        WriteUInt32(stream, 16);
        WriteUInt32(stream, 72u << 16);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 1);
        WriteUInt32(stream, 72u << 16);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 1);
    }

    private static void WritePascalName(Stream stream, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
        var total = 1 + bytes.Length;
        var padding = (4 - (total & 3)) & 3;
        stream.Write(new byte[padding]);
    }

    private static void WriteAscii(Stream stream, string value) =>
        stream.Write(Encoding.ASCII.GetBytes(value));

    private static void WriteInt16(Stream stream, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
