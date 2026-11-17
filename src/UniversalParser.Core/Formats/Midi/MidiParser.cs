using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Midi;

/// <summary>
/// Standard MIDI File parser. The tree keeps the MThd chunk, every MTrk chunk, and every event
/// inside each track. Event payloads stay in the source and are read when a node is selected.
/// </summary>
public sealed class MidiParser : IFormatParser
{
    public const string IdValue = "midi";
    public const string DisplayNameValue = "Musical Instrument Digital Interface";

    public string Id => IdValue;
    public string DisplayName => DisplayNameValue;
    public int ProbePriority => 145;
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

        var count = (int)Math.Min(source.Length, 4);
        Span<byte> signature = stackalloc byte[4];
        var read = SourceRead.ReadAtMost(source, 0, signature[..count]);
        if (read == 0 || !signature[..read].SequenceEqual(MidiConstants.HeaderChunkId[..read]))
        {
            return ProbeResult.NoMatch;
        }

        return read == 4
            ? ProbeResult.Strong("Standard MIDI File MThd chunk signature.")
            : ProbeResult.Weak("Partial Standard MIDI File signature; the MThd chunk id is truncated.");
    }

    public FormatNode Parse(ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ThrowIfCancellationRequested();
        return new MidiDocumentParser(context).Parse();
    }

    public NodeParseResult ParseNode(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        context.ThrowIfCancellationRequested();
        return MidiNodeParser.Parse(context, node);
    }
}
