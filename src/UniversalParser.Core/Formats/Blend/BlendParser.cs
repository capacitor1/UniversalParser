using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Blend;

/// <summary>
/// Blender .blend parser (uncompressed files only). The tree is the block sequence from the
/// blend header; SDNA labels each block, known metadata blocks are interpreted, and runtime
/// memory blocks stay unparsed.
/// </summary>
public sealed class BlendParser : IFormatParser
{
    public const string IdValue = "blend";
    public const string DisplayNameValue = "Blender File";

    public string Id => IdValue;
    public string DisplayName => DisplayNameValue;
    public int ProbePriority => 167;
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

        Span<byte> prefix = stackalloc byte[BlendHeaderReader.MagicLength];
        var count = (int)Math.Min(source.Length, BlendHeaderReader.MagicLength);
        var read = SourceRead.ReadAtMost(source, 0, prefix[..count]);
        if (read == 0 || !prefix[..read].SequenceEqual(BlendHeaderReader.Magic[..read]))
        {
            return ProbeResult.NoMatch;
        }

        if (read < BlendHeaderReader.MagicLength)
        {
            return ProbeResult.Weak("Partial Blender file signature.");
        }

        if (!BlendHeaderReader.TryRead(source, out var header, out var error) || error != BlendHeaderError.None)
        {
            return ProbeResult.Weak($"Blender signature found, but the header is not supported ({error}).");
        }

        return ProbeResult.Strong(
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Blender file header, pointer size {header.PointerSize}."));
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        return new BlendDocumentParser(context).Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return BlendNodeParser.Parse(context, node);
    }
}
