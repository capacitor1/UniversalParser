using UniversalParser.Core.Model;

namespace UniversalParser.Core.Statistics;

/// <summary>Stable display labels for <see cref="NodeKind"/> in charts and ranking lists.</summary>
public static class NodeKindNames
{
    public static string Singular(NodeKind kind) => kind switch
    {
        NodeKind.Container => "Container",
        NodeKind.Leaf => "Leaf",
        NodeKind.Raw => "Raw data",
        NodeKind.Padding => "Padding",
        NodeKind.Ignored => "Ignored",
        NodeKind.Unparsed => "Unparsed",
        NodeKind.Synthetic => "Synthetic",
        _ => kind.ToString(),
    };

    public static string Plural(NodeKind kind) => kind switch
    {
        NodeKind.Container => "Containers",
        NodeKind.Leaf => "Leaves",
        _ => Singular(kind),
    };
}
