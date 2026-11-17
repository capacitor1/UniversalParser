using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Asf;

/// <summary>
/// ASF parser. The tree is built from GUID-delimited object headers only. Data Object packets
/// are embedded payload bytes, not GUID objects, so they are exposed as one unparsed region and
/// never decoded.
/// </summary>
public sealed class AsfParser : IFormatParser
{
    public const string IdValue = "asf";

    public string Id => IdValue;
    public string DisplayName => "Advanced Systems Format";
    public int ProbePriority => 130;
    public bool IsFallback => false;

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        Span<byte> bytes = stackalloc byte[16];
        if (source.Length < bytes.Length || SourceRead.ReadAtMost(source, 0, bytes) < bytes.Length)
        {
            return ProbeResult.NoMatch;
        }

        return new Guid(bytes) == AsfConstants.HeaderObject
            ? ProbeResult.Strong("ASF Header Object GUID.")
            : ProbeResult.NoMatch;
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        return new AsfDocumentParser(context).Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return AsfNodeParser.Parse(context, node);
    }
}
