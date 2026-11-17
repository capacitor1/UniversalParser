using System.Buffers.Binary;
using System.Text;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Formats.Png;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Tests.Formats;

public sealed class PngCrcTests
{
    [Fact]
    public void LargeImageDataChunkComputesCrcAndReportsProgress()
    {
        const int payloadLength = ParseThresholds.LargeOperationBytes + 1024;
        using var source = new MemorySource(BuildPng(payloadLength));
        var engine = new ParserEngine(ParserRegistry.CreateDefault());
        var document = engine.Parse(source, "large.png");
        var chunk = Assert.Single(
            document.Root.Children,
            static child => child.Name.Contains("IDAT", StringComparison.Ordinal));
        var progress = new RecordingProgress();

        var result = engine.ParseNode(source, document, chunk, progress);

        Assert.Contains(
            result.Fields,
            static field => field.Key == "CRC" && field.Value.StartsWith("0xDEADBEEF (", StringComparison.Ordinal));
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Code == DiagnosticCodes.CrcMismatch);
        Assert.DoesNotContain(
            result.Fields,
            static field => field.Value.Contains("MaxProbeBytes", StringComparison.Ordinal));

        Assert.NotEmpty(progress.Items);
        var last = progress.Items[^1];
        Assert.Equal("PNG CRC", last.Stage);
        Assert.Equal(4L + payloadLength, last.Total);
        Assert.Equal(last.Total, last.Processed);
    }

    private static byte[] BuildPng(int payloadLength)
    {
        using var file = new MemoryStream();
        file.Write(PngConstants.Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8;
        WriteChunk(file, "IHDR", header);

        var payload = new byte[payloadLength];
        payload[0] = 0x78;
        payload[1] = 0x9C;
        WriteChunk(file, "IDAT", payload, crcOverride: 0xDEADBEEF);

        WriteChunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data, uint? crcOverride = null)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, typeBytes.Length);
        using var crcSource = new MemorySource(crcInput);
        var crc = crcOverride ?? PngCrc32.Compute(crcSource, 0, crcInput.Length, CancellationToken.None);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private sealed class RecordingProgress : IProgress<ParseProgress>
    {
        public List<ParseProgress> Items { get; } = [];

        public void Report(ParseProgress value) => Items.Add(value);
    }
}
