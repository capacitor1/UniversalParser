using UniversalParser.Core.Binary;
using UniversalParser.Core.Model;

namespace UniversalParser.Core.Validation;

/// <summary>
/// Structural invariant checks shared by tests, fixtures, and debug builds.
/// </summary>
public static class NodeTreeValidator
{
    /// <summary>
    /// Returns human-readable errors. When <paramref name="requirePayloadCoverage"/> is true,
    /// every non-empty container payload must be fully covered by its children.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        FormatNode root,
        long sourceLength,
        bool requirePayloadCoverage = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceLength);

        var errors = new List<string>();
        var sourceRange = new SourceRange(0, sourceLength);
        var stack = new Stack<(FormatNode Node, PathFrame Path)>();
        stack.Push((root, new PathFrame(root.Name, null)));

        while (stack.Count > 0)
        {
            var (node, pathFrame) = stack.Pop();
            if (!sourceRange.Contains(node.Range))
            {
                errors.Add($"{FormatPath(pathFrame)}: range {node.Range} is outside source {sourceRange}.");
            }

            if (node.PayloadRange is { } payload && !node.Range.Contains(payload))
            {
                errors.Add($"{FormatPath(pathFrame)}: payload range {payload} is outside node range {node.Range}.");
            }

            var childBounds = node.PayloadRange ?? node.Range;
            FormatNode? previous = null;
            foreach (var child in node.Children)
            {
                var childPath = new PathFrame(child.Name, pathFrame);
                if (!childBounds.Contains(child.Range))
                {
                    errors.Add(
                        $"{FormatPath(childPath)}: range {child.Range} is outside child bounds {childBounds}.");
                }

                if (previous is not null)
                {
                    if (child.Range.Offset < previous.Range.Offset)
                    {
                        errors.Add(
                            $"{FormatPath(pathFrame)}: child '{child.Name}' is not sorted after '{previous.Name}'.");
                    }

                    if (previous.Range.Overlaps(child.Range))
                    {
                        errors.Add(
                            $"{FormatPath(pathFrame)}: child '{previous.Name}' overlaps '{child.Name}'.");
                    }
                }

                stack.Push((child, childPath));
                previous = child;
            }

            if (!requirePayloadCoverage || node.Kind != NodeKind.Container || childBounds.IsEmpty)
            {
                continue;
            }

            var cursor = childBounds.Offset;
            foreach (var child in node.Children.OrderBy(static child => child.Range.Offset))
            {
                if (child.Range.Offset > cursor)
                {
                    errors.Add(
                        $"{FormatPath(pathFrame)}: uncovered range {new SourceRange(cursor, child.Range.Offset - cursor)}.");
                }

                if (child.Range.End > cursor)
                {
                    cursor = child.Range.End;
                }
            }

            if (cursor < childBounds.End)
            {
                errors.Add(
                    $"{FormatPath(pathFrame)}: uncovered trailing range {new SourceRange(cursor, childBounds.End - cursor)}.");
            }
        }

        return errors;
    }

    private static string FormatPath(PathFrame frame)
    {
        var parts = new Stack<string>();
        for (var current = frame; current is not null; current = current.Parent)
        {
            parts.Push(current.Name);
        }

        return string.Join('/', parts);
    }

    private sealed class PathFrame(string name, PathFrame? parent)
    {
        public string Name { get; } = name;
        public PathFrame? Parent { get; } = parent;
    }
}
