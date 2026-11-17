using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Flv;

/// <summary>
/// FLV parser. The tree is built from the file header and tag headers only; tag bodies,
/// including AMF0 script metadata and Enhanced FLV CodecID 12 / FourCC headers, are produced
/// lazily by <see cref="ParseNode"/>.
/// </summary>
public sealed class FlvParser : IFormatParser
{
    public const string IdValue = "flv";

    public string Id => IdValue;
    public string DisplayName => "Flash Video";
    public int ProbePriority => 120;
    public bool IsFallback => false;

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        Span<byte> signature = stackalloc byte[3];
        if (source.Length < signature.Length || SourceRead.ReadAtMost(source, 0, signature) < signature.Length)
        {
            return ProbeResult.NoMatch;
        }

        return signature.SequenceEqual("FLV"u8)
            ? ProbeResult.Strong("FLV signature.")
            : ProbeResult.NoMatch;
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        using var document = new FlvDocumentParser(context);
        return document.Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return FlvNodeParser.Parse(context, node);
    }
}
