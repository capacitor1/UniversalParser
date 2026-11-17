using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Psd;

/// <summary>
/// Adobe Photoshop document parser. PSD (version 1) and PSB (version 2) share the same section
/// layout, so one parser handles both; only a few length fields widen in PSB.
/// </summary>
public sealed class PsdParser : IFormatParser
{
    public const string IdValue = "psd";
    public const string DisplayNameValue = "Adobe Photoshop Document";

    public string Id => IdValue;
    public string DisplayName => DisplayNameValue;
    public int ProbePriority => 155;
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

        var count = (int)Math.Min(source.Length, PsdConstants.SignatureLength);
        Span<byte> signature = stackalloc byte[PsdConstants.SignatureLength];
        var read = SourceRead.ReadAtMost(source, 0, signature[..count]);
        if (read == 0 || !signature[..read].SequenceEqual(PsdConstants.Signature[..read]))
        {
            return ProbeResult.NoMatch;
        }

        if (read < PsdConstants.SignatureLength)
        {
            return ProbeResult.Weak("Partial Photoshop document signature; the '8BPS' id is truncated.");
        }

        return ProbeResult.Strong("Adobe Photoshop document '8BPS' signature.");
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        return new PsdDocumentParser(context).Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return PsdNodeParser.Parse(context, node);
    }
}
