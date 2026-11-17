using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Ogg;

/// <summary>
/// Ogg encapsulation parser (RFC 3533). The tree is the physical page sequence; header
/// packets carried by complete page packets are interpreted as page fields. Audio and video
/// packets are never decoded.
/// </summary>
public sealed class OggParser : IFormatParser
{
    public const string IdValue = "ogg";
    public const string DisplayNameValue = "Ogg Encapsulation Format";

    public string Id => IdValue;
    public string DisplayName => DisplayNameValue;
    public int ProbePriority => 165;
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

        Span<byte> signature = stackalloc byte[OggPage.CapturePatternLength];
        var count = (int)Math.Min(source.Length, OggPage.CapturePatternLength);
        var read = SourceRead.ReadAtMost(source, 0, signature[..count]);
        if (read == 0 || !signature[..read].SequenceEqual(OggPage.CapturePattern[..read]))
        {
            return ProbeResult.NoMatch;
        }

        if (read < OggPage.CapturePatternLength)
        {
            return ProbeResult.Weak("Partial Ogg capture pattern.");
        }

        return ProbeResult.Strong("Ogg page capture pattern 'OggS'.");
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        using var document = new OggDocumentParser(context);
        return document.Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return OggNodeParser.Parse(context, node);
    }
}
