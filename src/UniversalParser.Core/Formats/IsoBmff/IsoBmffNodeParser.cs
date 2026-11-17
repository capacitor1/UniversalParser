using System.Collections.Immutable;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.IsoBmff;

/// <summary>
/// Re-reads one box on demand and dispatches to its field parser. Box payloads are bounded by the
/// node range; nothing outside the selected box is read.
/// </summary>
internal static class IsoBmffNodeParser
{
    /// <summary>
    /// Recovers the containing box type from the stable id. Some 4CCs ('thmb', 'auxl', 'cdsc')
    /// mean different things under 'iref', 'tref' or 'udta'.
    /// </summary>
    public static string? GetParentType(FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.StableId is null)
        {
            return null;
        }

        var separator = node.StableId.IndexOf('|');
        if (separator < 0 || separator == node.StableId.Length - 1)
        {
            return null;
        }

        return node.StableId[(separator + 1)..];
    }

    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "isobmff-root")
        {
            return ParseRoot(node);
        }

        if (node.Name.StartsWith('<'))
        {
            return ParseSynthetic(node);
        }

        if (node.Name.StartsWith("key[", StringComparison.Ordinal))
        {
            return ParseMetadataItem(node);
        }

        var fields = context.CreateFieldList();
        if (!IsoBmffBoxHeader.TryRead(
                context.Source,
                node.Range.Offset,
                node.Range.End,
                allowNumericType: false,
                out var header,
                out var error))
        {
            fields.Warning(
                DiagnosticCodes.TruncatedHeader,
                $"The box header could not be read ({error}).",
                node.Range);
            return Result(node, node.Name, fields);
        }

        var data = new IsoBmffBoxReader(context.Source, header.PayloadOffset, header.PayloadLength, fields);
        var title = IsoBmffBoxParsers.Dispatch(context, node, header, data, fields);
        return Result(node, title, fields);
    }

    private static NodeParseResult ParseRoot(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<FileSize>", node.Range.Length, node.Range);
        fields.Derived("<BoxCount>", node.Children.Length, node.Range);
        fields.Note("Synthetic document root; select a box to inspect its fields.", node.Range);
        return Result(node, "ISO Base Media File", fields);
    }

    private static NodeParseResult ParseSynthetic(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<Length>", node.Range.Length, node.Range);
        fields.Note("This synthetic range is not a parsed box.", node.Range);
        return Result(node, node.Name, fields);
    }

    private static NodeParseResult ParseMetadataItem(FormatNode node)
    {
        var fields = new FieldList(int.MaxValue);
        fields.Derived("<MetadataItem>", node.Name, node.Range);
        fields.Note("Select a child 'data' atom to inspect the metadata value.", node.Range);
        return Result(node, $"(QuickTime)ItemIndex '{node.Name}'", fields);
    }

    private static NodeParseResult Result(FormatNode node, string title, FieldList fields) =>
        new(
            title,
            node.Range,
            node.Range,
            fields.ToImmutable(),
            fields.Diagnostics.ToImmutableArray());
}
