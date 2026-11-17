using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Png;

/// <summary>
/// PNG/APNG chunk parser. The tree is built from chunk headers only; chunk data and CRC validation
/// are read lazily when a node is selected.
/// </summary>
public sealed class PngParser : IFormatParser
{
    public const string IdValue = "png";

    public string Id => IdValue;
    public string DisplayName => "Portable Network Graphics";
    public int ProbePriority => 150;
    public bool IsFallback => false;

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        if (source.Length == 0)
        {
            return ProbeResult.NoMatch;
        }

        var count = (int)Math.Min(source.Length, PngConstants.SignatureLength);
        Span<byte> signature = stackalloc byte[PngConstants.SignatureLength];
        var read = SourceRead.ReadAtMost(source, 0, signature[..count]);
        if (read == 0)
        {
            return ProbeResult.NoMatch;
        }

        var matches = signature[..read].SequenceEqual(PngConstants.Signature[..read]);
        if (!matches)
        {
            return ProbeResult.NoMatch;
        }

        return read == PngConstants.SignatureLength
            ? ProbeResult.Strong("PNG signature.")
            : ProbeResult.Weak("Partial PNG signature; the 8-byte signature is truncated.");
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        return new PngDocumentParser(context).Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return PngNodeParser.Parse(context, node);
    }
}
