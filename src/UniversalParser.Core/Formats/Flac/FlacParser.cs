using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Flac;

/// <summary>
/// Native FLAC parser (RFC 9639). The tree is the metadata block sequence plus one explicit
/// audio frame region; block payloads are read lazily and audio frames are never decoded.
/// </summary>
public sealed class FlacParser : IFormatParser
{
    public const string IdValue = "flac";
    public const string DisplayNameValue = "Free Lossless Audio Codec";

    public string Id => IdValue;
    public string DisplayName => DisplayNameValue;
    public int ProbePriority => 152;
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

        Span<byte> signature = stackalloc byte[FlacConstants.SignatureLength];
        var count = (int)Math.Min(source.Length, FlacConstants.SignatureLength);
        var read = SourceRead.ReadAtMost(source, 0, signature[..count]);
        if (read == 0 || !signature[..read].SequenceEqual(FlacConstants.Signature[..read]))
        {
            return ProbeResult.NoMatch;
        }

        if (read < FlacConstants.SignatureLength)
        {
            return ProbeResult.Weak("Partial FLAC 'fLaC' signature.");
        }

        return ProbeResult.Strong("Native FLAC 'fLaC' signature.");
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        return new FlacDocumentParser(context).Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return FlacNodeParser.Parse(context, node);
    }
}
