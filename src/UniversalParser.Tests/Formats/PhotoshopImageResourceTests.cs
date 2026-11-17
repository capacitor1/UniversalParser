using System.Buffers.Binary;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Tests.Formats;

public sealed class PhotoshopImageResourceTests
{
    [Fact]
    public void VersionInfoResourceReadsCountedUnicodeStringsWithoutGuessedNull()
    {
        using var source = new MemorySource(BuildJpeg());
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "sample.jpg");

        var app13 = Assert.Single(document.Root.Children, static child => child.Name == "APP13");
        var result = engine.ParseNode(source, document, app13);

        Assert.Contains(
            result.Fields,
            static field => field.Key == "Resource[0].ID" && field.Value == "1057 (Version Info)");
        Assert.Contains(
            result.Fields,
            static field => field.Key == "Resource[0].Writer name" && field.Value == "Adobe Photoshop");
        Assert.Contains(
            result.Fields,
            static field => field.Key == "Resource[0].Reader name" && field.Value == "Adobe Photoshop CS6");
        Assert.Contains(
            result.Fields,
            static field => field.Key == "Resource[0].File version" && field.Value == "1");
        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
    }

    private static byte[] BuildJpeg()
    {
        using var file = new MemoryStream();
        file.Write([0xFF, 0xD8]);

        using var payload = new MemoryStream();
        WriteAscii(payload, "Photoshop 3.0\0");
        WriteAscii(payload, "8BIM");
        WriteUInt16(payload, 1057);
        payload.WriteByte(0); // Empty Pascal name
        payload.WriteByte(0); // Name padding

        using var data = new MemoryStream();
        WriteUInt32(data, 1); // Version
        data.WriteByte(1); // hasRealMergedData
        WriteUnicodeString(data, "Adobe Photoshop");
        WriteUnicodeString(data, "Adobe Photoshop CS6");
        WriteUInt32(data, 1); // File version

        WriteUInt32(payload, (uint)data.Length);
        data.Position = 0;
        data.CopyTo(payload);

        file.Write([0xFF, 0xED]);
        WriteUInt16(file, checked((ushort)(payload.Length + 2)));
        payload.Position = 0;
        payload.CopyTo(file);
        file.Write([0xFF, 0xD9]);
        return file.ToArray();
    }

    private static void WriteUnicodeString(Stream stream, string value)
    {
        var bytes = Encoding.BigEndianUnicode.GetBytes(value);
        WriteUInt32(stream, (uint)(bytes.Length / 2));
        stream.Write(bytes);
    }

    private static void WriteAscii(Stream stream, string value) =>
        stream.Write(Encoding.ASCII.GetBytes(value));

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
}
