using System.Buffers.Binary;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Ebml;

/// <summary>
/// EBML/Matroska/WebM parser. The tree is built from element headers only; payload fields
/// are produced lazily by <see cref="ParseNode"/>.
/// </summary>
public sealed class EbmlParser : IFormatParser
{
    public const string IdValue = "ebml";

    public string Id => IdValue;
    public string DisplayName => "Extensible Binary Meta Language";
    public int ProbePriority => 110;
    public bool IsFallback => false;

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        Span<byte> signature = stackalloc byte[4];
        if (source.Length < signature.Length || SourceRead.ReadAtMost(source, 0, signature) < signature.Length)
        {
            return ProbeResult.NoMatch;
        }

        return BinaryPrimitives.ReadUInt32BigEndian(signature) == EbmlSchema.EbmlHeaderId
            ? ProbeResult.Strong("EBML header signature 0x1A45DFA3.")
            : ProbeResult.NoMatch;
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        using var document = new EbmlDocumentParser(context);
        return document.Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return EbmlNodeParser.Parse(context, node);
    }
}
