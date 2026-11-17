using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Jpeg;

/// <summary>
/// JPEG marker parser. The tree is built from marker and segment headers only; entropy-coded
/// scan data and APP payloads are never decoded. Node fields are read lazily on selection.
/// </summary>
public sealed class JpegParser : IFormatParser
{
    public const string IdValue = "jpeg";

    public string Id => IdValue;
    public string DisplayName => "JPEG Image";
    public int ProbePriority => 160;
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

        Span<byte> prefix = stackalloc byte[2];
        var count = (int)Math.Min(source.Length, 2);
        var read = SourceRead.ReadAtMost(source, 0, prefix[..count]);
        if (read == 0 || prefix[0] != JpegConstants.MarkerPrefix)
        {
            return ProbeResult.NoMatch;
        }

        if (read == 1)
        {
            return ProbeResult.Weak("Partial JPEG marker prefix.");
        }

        return prefix[1] == JpegConstants.Soi
            ? ProbeResult.Strong("JPEG SOI marker.")
            : ProbeResult.NoMatch;
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        using var document = new JpegDocumentParser(context);
        return document.Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return JpegNodeParser.Parse(context, node);
    }
}
