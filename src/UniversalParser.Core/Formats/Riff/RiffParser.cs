using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>
/// RIFF/RF64/BW64/RIFX container parser. The chunk tree is built eagerly from headers only;
/// payload fields are produced lazily by <see cref="ParseNode"/>.
/// </summary>
public sealed class RiffParser : IFormatParser
{
    public const string IdValue = "riff";

    private static readonly HashSet<string> RootIds = new(StringComparer.Ordinal)
    {
        "RIFF", "RF64", "BW64", "RIFX",
    };

    public string Id => IdValue;
    public string DisplayName => "Resource Interchange File Format";
    public int ProbePriority => 100;
    public bool IsFallback => false;

    public ProbeResult Probe(IRandomAccessSource source, ParseLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        Span<byte> header = stackalloc byte[4];
        if (source.Length < header.Length)
        {
            return ProbeResult.NoMatch;
        }

        var read = RiffRead.ReadAtMost(source, 0, header);
        if (read < 4)
        {
            return ProbeResult.NoMatch;
        }

        var id = RiffFormatting.DecodeFourCc(header);
        return RootIds.Contains(id)
            ? ProbeResult.Strong($"RIFF family signature '{id}'.")
            : ProbeResult.NoMatch;
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        return new RiffDocumentParser(context).Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return RiffNodeParser.Parse(context, node);
    }
}
