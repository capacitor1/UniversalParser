using System.Collections.Immutable;
using UniversalParser.Core.Binary;

namespace UniversalParser.Core.Model;

/// <summary>
/// Immutable structural node. Range is the full encoded range; PayloadRange optionally
/// identifies the child-bearing portion after a node's own header.
/// </summary>
public sealed class FormatNode
{
    public FormatNode(
        string name,
        NodeKind kind,
        SourceRange range,
        SourceRange? payloadRange = null,
        ImmutableArray<FormatNode>? children = null,
        string? formatId = null,
        string? stableId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (payloadRange is { } payload && !range.Contains(payload))
        {
            throw new ArgumentException("Payload range must be contained by the node range.", nameof(payloadRange));
        }

        var childArray = children.GetValueOrDefault();
        if (childArray.IsDefault)
        {
            childArray = ImmutableArray<FormatNode>.Empty;
        }

        if (childArray.Any(static child => child is null))
        {
            throw new ArgumentException("Children must not contain null entries.", nameof(children));
        }

        Name = name;
        Kind = kind;
        Range = range;
        PayloadRange = payloadRange;
        Children = childArray;
        FormatId = formatId;
        StableId = stableId;
    }

    public string Name { get; }
    public NodeKind Kind { get; }
    public SourceRange Range { get; }
    public SourceRange? PayloadRange { get; }
    public ImmutableArray<FormatNode> Children { get; }
    public string? FormatId { get; }
    public string? StableId { get; }

    public bool IsContainer => Kind == NodeKind.Container;
}
