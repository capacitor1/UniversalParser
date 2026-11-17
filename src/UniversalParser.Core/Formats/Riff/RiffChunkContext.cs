using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>Everything a per-chunk field handler may need.</summary>
internal sealed class RiffChunkContext
{
    public RiffChunkContext(
        IRandomAccessSource source,
        RiffChunkLayout layout,
        Endian endian,
        ParseLimits limits,
        FieldList fields,
        string? parentType,
        CancellationToken cancellationToken = default)
    {
        Source = source;
        Layout = layout;
        Endian = endian;
        Limits = limits;
        Fields = fields;
        ParentType = parentType;
        CancellationToken = cancellationToken;
    }

    public IRandomAccessSource Source { get; }
    public RiffChunkLayout Layout { get; }
    public Endian Endian { get; }
    public ParseLimits Limits { get; }
    public FieldList Fields { get; }
    public string? ParentType { get; }
    public CancellationToken CancellationToken { get; }

    public long PayloadLength => Layout.PayloadRange.Length;
    public bool IsTruncated => Layout.Truncated;
    public string Id => Layout.Id;

    public SourceRange Range(long payloadOffset, long length) =>
        new(Layout.PayloadRange.Offset + payloadOffset, length);

    /// <summary>Reads at most <paramref name="maxBytes"/> from the payload without crossing its end.</summary>
    public byte[] ReadPayload(int maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        var want = (int)Math.Min(Math.Min((long)maxBytes, Limits.MaxBlobBytes), PayloadLength);
        if (want == 0)
        {
            return [];
        }

        var buffer = new byte[want];
        var read = RiffRead.ReadAtMost(Source, Layout.PayloadRange.Offset, buffer);
        if (read < want)
        {
            Fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"'{RiffFormatting.SanitizeFourCc(Id)}' payload is shorter than its chunk header declares.",
                Layout.PayloadRange);
            return buffer[..read];
        }

        return buffer;
    }

    public void AddUnparsedPayloadNote(long parsedBytes)
    {
        var remaining = PayloadLength - parsedBytes;
        if (remaining > 0)
        {
            Fields.Derived("<UnparsedPayloadLength>", remaining, Range(parsedBytes, remaining));
        }
    }

    public void AddTruncationWarning()
    {
        if (IsTruncated)
        {
            Fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"Chunk '{RiffFormatting.FormatFourCc(Id)}' declares {Layout.ResolvedSize} byte(s) but only {PayloadLength} are available.",
                Layout.NodeRange);
        }
    }
}
