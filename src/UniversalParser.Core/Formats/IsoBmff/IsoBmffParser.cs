using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.IsoBmff;

/// <summary>
/// ISO Base Media File Format parser: MP4, M4A, MOV, 3GP, HEIF/HEIC, AVIF and fragmented MP4.
/// The tree is built from box headers only; each box's fields are read lazily on selection.
/// </summary>
public sealed class IsoBmffParser : IFormatParser
{
    public const string IdValue = "isobmff";

    private const int MaxProbeBoxes = 8;

    private static readonly HashSet<string> LeadingBoxes = new(StringComparer.Ordinal)
    {
        "ftyp", "styp", "moov", "moof",
    };

    private static readonly HashSet<string> SkippableLeadingBoxes = new(StringComparer.Ordinal)
    {
        "free", "skip", "wide", "pnot",
    };

    public string Id => IdValue;
    public string DisplayName => "ISO Base Media File Format";
    public int ProbePriority => 170;
    public bool IsFallback => false;

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        if (source.Length < 8)
        {
            return ProbeResult.NoMatch;
        }

        var limit = Math.Min(source.Length, (long)ParseThresholds.LargeOperationBytes);
        var position = 0L;
        for (var index = 0; index < MaxProbeBoxes && position + 8 <= limit; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsoBmffBoxHeader.TryRead(source, position, limit, allowNumericType: false, out var header, out _))
            {
                return ProbeResult.NoMatch;
            }

            if (LeadingBoxes.Contains(header.Type))
            {
                return ProbeResult.Strong($"ISO BMFF '{header.Type}' box.");
            }

            if (!SkippableLeadingBoxes.Contains(header.Type))
            {
                return ProbeResult.NoMatch;
            }

            if (header.Truncated || header.End <= position)
            {
                return ProbeResult.NoMatch;
            }

            position = header.End;
        }

        return ProbeResult.NoMatch;
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        return new IsoBmffDocumentParser(context).Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return IsoBmffNodeParser.Parse(context, node);
    }
}
