using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Tests.Model;

public sealed class NodePathTests
{
    [Fact]
    public void RootIsAnEmptyPath()
    {
        var root = Container("root", Leaf("A"));

        Assert.Empty(NodePath.FindChildIndices(root, root)!);
    }

    [Fact]
    public void DirectChildReturnsASingleIndex()
    {
        var first = Leaf("A");
        var second = Leaf("B");
        var root = Container("root", first, second);

        Assert.Equal(new[] { 1 }, NodePath.FindChildIndices(root, second));
    }

    [Fact]
    public void NestedTargetReturnsEverySiblingIndex()
    {
        var target = Leaf("Target");
        var branch = Container("Branch", Leaf("X"), Container("Inner", target));
        var root = Container("root", Leaf("Skip"), branch);

        Assert.Equal(new[] { 1, 1, 0 }, NodePath.FindChildIndices(root, target));
    }

    [Fact]
    public void ForeignNodeIsNotFound()
    {
        var root = Container("root", Leaf("A"));

        Assert.Null(NodePath.FindChildIndices(root, Leaf("A")));
    }

    [Fact]
    public void DeepSiblingWalkKeepsThePathAligned()
    {
        // 300 siblings before the target: a length-based walk would drift, an index path cannot.
        var children = Enumerable.Range(0, 300)
            .Select(index => Leaf(FormattableString.Invariant($"N{index}")))
            .ToImmutableArray();
        var target = children[^1];
        var root = new FormatNode("root", NodeKind.Container, new SourceRange(0, 300), null, children);

        var path = NodePath.FindChildIndices(root, target);

        Assert.Equal(new[] { 299 }, path);
        Assert.Same(target, root.Children[path![0]]);
    }

    private static FormatNode Leaf(string name) =>
        new(name, NodeKind.Leaf, new SourceRange(0, 1));

    private static FormatNode Container(string name, params FormatNode[] children) =>
        new(name, NodeKind.Container, new SourceRange(0, children.Length), null, children.ToImmutableArray());
}
