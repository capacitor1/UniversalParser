using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.Core.Formats.Riff;

/// <summary>
/// Re-reads a selected node from the source. Tree parsing never stores a second copy of the
/// payload; the fields are produced lazily from the same immutable source range.
/// </summary>
internal static class RiffNodeParser
{
    public static NodeParseResult Parse(ParseContext context, FormatNode node)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);

        if (node.StableId == "riff-root")
        {
            return new NodeParseResult(
                node.Name,
                node.Range,
                node.Range,
                [
                    Field.Derived("<FileSize>", node.Range.Length.ToString(CultureInfo.InvariantCulture)),
                    Field.Derived("<ChunkCount>", node.Children.Length.ToString(CultureInfo.InvariantCulture)),
                    Field.Note("Synthetic document root; select a child chunk to inspect its fields."),
                ]);
        }

        if (node.Name.StartsWith('<'))
        {
            return new NodeParseResult(
                node.Name,
                node.Range,
                node.Range,
                [
                    Field.Derived("<Length>", node.Range.Length.ToString(CultureInfo.InvariantCulture)),
                    Field.Note("This synthetic range is not a parsed RIFF chunk."),
                ]);
        }

        var (id, endian, parentType) = ResolveIdentity(node);
        if (node.Range.Length < 8)
        {
            return Failure(node, DiagnosticCodes.TruncatedHeader, "The node is shorter than a RIFF chunk header.");
        }

        if (!RiffRead.TryReadChunkHeader(context.Source, node.Range.Offset, node.Range.End, endian, out var headerId, out var rawSize))
        {
            return Failure(node, DiagnosticCodes.TruncatedHeader, "The RIFF chunk header could not be read.");
        }

        var resolvedSize = ResolveSize(context, node, id, rawSize);
        var layout = RiffChunkLayout.Create(id, node.Range.Offset, rawSize, resolvedSize, node.Range.End);
        var fields = context.CreateFieldList();
        fields.Raw("ckSize", rawSize, new SourceRange(node.Range.Offset + 4, 4));
        if (!string.Equals(headerId, id, StringComparison.Ordinal))
        {
            fields.Warning(
                DiagnosticCodes.InvalidSignature,
                $"The node identifies '{RiffFormatting.FormatFourCc(id)}' but the source contains '{RiffFormatting.FormatFourCc(headerId)}'.",
                new SourceRange(node.Range.Offset, 4));
        }

        var chunkContext = new RiffChunkContext(
            context.Source,
            layout,
            endian,
            context.Limits,
            fields,
            parentType: parentType,
            context.CancellationToken);
        if (RiffDocumentParser.IsRootChunk(id))
        {
            AddTypeField(fields, context.Source, layout, "formType", "RIFF form type");
            if (rawSize == uint.MaxValue)
            {
                fields.Derived("<ResolvedSize>", resolvedSize, new SourceRange(node.Range.Offset + 4, 4));
            }
        }
        else if (id == "LIST")
        {
            AddTypeField(fields, context.Source, layout, "listType", "LIST type");
        }

        if (node.Kind == NodeKind.Unparsed)
        {
            fields.Derived("<UnparsedLength>", node.Range.Length, node.Range);
            fields.Derived("<PayloadLength>", layout.PayloadRange.Length, layout.PayloadRange);
            if (id == "LIST" && ReadType(context.Source, layout) == "movi")
            {
                fields.Note("AVI 'movi' list contains movie data chunks; the entire list is left unparsed.", node.Range);
            }
            else
            {
                fields.Note("Unknown chunk; the entire block is unparsed.", node.Range);
            }

            chunkContext.AddTruncationWarning();
        }
        else if (id == "ANMF")
        {
            RiffChunkHandlers.AnmfHeader(chunkContext);
        }
        else if (RiffChunkHandlers.IsKnown(id))
        {
            RiffChunkHandlers.Parse(chunkContext);
        }
        else
        {
            fields.Derived("<PayloadLength>", layout.PayloadRange.Length, layout.PayloadRange);
            fields.Note("Payload is not parsed.", layout.PayloadRange);
            chunkContext.AddTruncationWarning();
        }

        if (RiffDocumentParser.IsRootChunk(id) || id == "LIST")
        {
            chunkContext.AddTruncationWarning();
        }

        return new NodeParseResult(
            RiffDocumentParser.BuildTitle(id, parentType),
            node.Range,
            node.Range,
            fields.ToImmutable(),
            ImmutableArray.CreateRange(fields.Diagnostics));
    }

    private static string? ReadType(IRandomAccessSource source, RiffChunkLayout layout)
    {
        if (layout.PayloadRange.Length < 4)
        {
            return null;
        }

        Span<byte> bytes = stackalloc byte[4];
        return RiffRead.ReadAtMost(source, layout.PayloadStart, bytes) == 4
            ? RiffFormatting.DecodeFourCc(bytes)
            : null;
    }


    private static long ResolveSize(ParseContext context, FormatNode node, string id, uint rawSize)
    {
        if (rawSize != uint.MaxValue)
        {
            return rawSize;
        }

        if (RiffDocumentParser.IsRootChunk(id))
        {
            var ds64 = RiffDs64Info.TryRead(context.Source, node.Range.Offset + 12, node.Range.End, context.Diagnostics);
            if (ds64 is not null)
            {
                return ds64.RiffSize > long.MaxValue ? long.MaxValue : (long)ds64.RiffSize;
            }
        }

        if (RiffDocumentParser.IsRootChunk(id) || id is "LIST" or "ANMF")
        {
            return Math.Max(0, node.Range.Length - 8);
        }

        return node.PayloadRange?.Length ?? Math.Max(0, node.Range.Length - 8);
    }

    private static void AddTypeField(FieldList fields, IRandomAccessSource source, RiffChunkLayout layout, string key, string description)
    {
        if (layout.PayloadRange.Length < 4)
        {
            fields.Warning(
                DiagnosticCodes.TruncatedPayload,
                $"The {description} is missing.",
                layout.PayloadRange);
            return;
        }

        Span<byte> bytes = stackalloc byte[4];
        if (RiffRead.ReadAtMost(source, layout.PayloadStart, bytes) == 4)
        {
            fields.Raw(key, RiffFormatting.FormatFourCc(bytes), new SourceRange(layout.PayloadStart, 4));
        }
        else
        {
            fields.Warning(DiagnosticCodes.TruncatedPayload, $"The {description} could not be read.", layout.PayloadRange);
        }
    }

    private static (string Id, Endian Endian, string? ParentType) ResolveIdentity(FormatNode node)
    {
        if (node.StableId is { } stable && stable.StartsWith("riff:", StringComparison.Ordinal))
        {
            var parts = stable.Split(':', 4);
            if (parts.Length >= 3)
            {
                return (
                    parts[2],
                    parts[1] == "be" ? Endian.Big : Endian.Little,
                    parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null);
            }
        }

        var name = node.Name;
        if (name.StartsWith("RIFF '", StringComparison.Ordinal)) return ("RIFF", Endian.Little, null);
        if (name.StartsWith("RF64 '", StringComparison.Ordinal)) return ("RF64", Endian.Little, null);
        if (name.StartsWith("BW64 '", StringComparison.Ordinal)) return ("BW64", Endian.Little, null);
        if (name.StartsWith("RIFX '", StringComparison.Ordinal)) return ("RIFX", Endian.Big, null);
        if (name.StartsWith("List '", StringComparison.Ordinal)) return ("LIST", Endian.Little, null);
        if (name.StartsWith("AnimationFrame '", StringComparison.Ordinal)) return ("ANMF", Endian.Little, null);

        var firstQuote = name.IndexOf('\'');
        var lastQuote = name.LastIndexOf('\'');
        return firstQuote >= 0 && lastQuote > firstQuote
            ? (name[(firstQuote + 1)..lastQuote], Endian.Little, null)
            : ("????", Endian.Little, null);
    }

    private static NodeParseResult Failure(FormatNode node, string code, string message) =>
        new(
            node.Name,
            node.Range,
            node.Range,
            ImmutableArray.Create(Field.Error(message, range: node.Range)),
            ImmutableArray.Create(new Diagnostic(DiagnosticSeverity.Error, code, message, node.Range)));
}
