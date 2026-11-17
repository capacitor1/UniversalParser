using System.Collections.Immutable;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;
using UniversalParser.Core.Validation;

namespace UniversalParser.Tests.Validation;

public sealed class NodeTreeValidatorTests
{
    [Fact]
    public void AcceptsCoveredTree()
    {
        var root = Node("root", 0, 10, Node("a", 0, 4), Node("b", 4, 6));

        Assert.Empty(NodeTreeValidator.Validate(root, 10, requirePayloadCoverage: true));
    }

    [Fact]
    public void ReportsGapsWhenCoverageIsRequired()
    {
        var root = Node("root", 0, 10, Node("a", 0, 4), Node("b", 5, 5));

        Assert.Empty(NodeTreeValidator.Validate(root, 10));
        Assert.Contains(
            NodeTreeValidator.Validate(root, 10, requirePayloadCoverage: true),
            error => error.Contains("uncovered", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsOverlapsAndUnsortedChildren()
    {
        var root = Node("root", 0, 10, Node("b", 5, 5), Node("a", 0, 6));

        var errors = NodeTreeValidator.Validate(root, 10);

        Assert.Contains(errors, error => error.Contains("overlaps", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("not sorted", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsChildrenOutsideParent()
    {
        var root = Node("root", 0, 10, Node("child", 9, 2));

        Assert.Contains(
            NodeTreeValidator.Validate(root, 10),
            error => error.Contains("outside child bounds", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsPayloadRangeOutsideNode()
    {
        Assert.Throws<ArgumentException>(() =>
            new FormatNode("bad", NodeKind.Container, new SourceRange(0, 4), new SourceRange(1, 4)));
    }

    private static FormatNode Node(string name, long offset, long length, params FormatNode[] children)
    {
        var range = new SourceRange(offset, length);
        return new FormatNode(
            name,
            children.Length == 0 ? NodeKind.Leaf : NodeKind.Container,
            range,
            range,
            children.Length == 0 ? ImmutableArray<FormatNode>.Empty : ImmutableArray.CreateRange(children));
    }
}
