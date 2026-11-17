using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Fbx;

/// <summary>
/// Binary FBX parser. The tree is built from node records; property lists are read lazily and
/// deflate-compressed arrays are described by length instead of being decompressed.
/// </summary>
public sealed class FbxParser : IFormatParser
{
    public const string IdValue = "fbx";

    public string Id => IdValue;
    public string DisplayName => "Filmbox";
    public int ProbePriority => 140;
    public bool IsFallback => false;

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        if (source.Length < FbxConstants.BinaryMagicLength)
        {
            return ProbeResult.NoMatch;
        }

        Span<byte> magic = stackalloc byte[FbxConstants.BinaryMagicLength];
        if (SourceRead.ReadAtMost(source, 0, magic) < magic.Length
            || !magic.SequenceEqual(FbxConstants.BinaryMagic))
        {
            return ProbeResult.NoMatch;
        }

        return source.Length >= FbxConstants.HeaderLength
            ? ProbeResult.Strong("FBX binary file magic.")
            : ProbeResult.Weak("FBX binary magic found, but the 27-byte header is truncated.");
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        using var document = new FbxDocumentParser(context);
        return document.Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return FbxNodeParser.Parse(context, node);
    }
}
