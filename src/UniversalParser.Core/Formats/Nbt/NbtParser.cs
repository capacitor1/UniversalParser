using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Nbt;

/// <summary>
/// Uncompressed Minecraft NBT parser. The probe accepts a valid root TAG_Compound whose framing
/// can be verified; gzip/zlib-compressed NBT and MCA region files do not start with that structure
/// and remain Raw Data. Tree labels are canonical tag type names only.
/// </summary>
public sealed class NbtParser : IFormatParser
{
    public const string IdValue = NbtConstants.ParserId;
    public const string DisplayNameValue = NbtConstants.DisplayName;

    public string Id => IdValue;
    public string DisplayName => DisplayNameValue;
    public int ProbePriority => 95;
    public bool IsFallback => false;

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        if (source.Length < 4)
        {
            return ProbeResult.NoMatch;
        }

        var window = (long)Math.Min(source.Length, ParseThresholds.LargeOperationBytes);
        if (!NbtScanner.TryScan(source, window, cancellationToken, out var result) || !result.Success)
        {
            return ProbeResult.NoMatch;
        }

        var description = string.Create(
            CultureInfo.InvariantCulture,
            $"uncompressed NBT root TAG_Compound: {result.TagCount} tag(s).");
        return result.Complete
            ? ProbeResult.Strong(description)
            : ProbeResult.Weak(description);
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        return new NbtDocumentParser(context).Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return NbtNodeParser.Parse(context, node);
    }
}
