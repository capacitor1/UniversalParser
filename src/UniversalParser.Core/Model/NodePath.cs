namespace UniversalParser.Core.Model;

/// <summary>
/// Locates a node inside an immutable tree by child index. The GUI uses the result to expand lazy
/// tree views; the helper stays UI-free so the walk is unit-testable.
/// </summary>
public static class NodePath
{
    /// <summary>
    /// Returns the child indices from <paramref name="root"/> to <paramref name="target"/>, or null
    /// when the target is not part of this tree. An empty array means the target is the root.
    /// </summary>
    public static int[]? FindChildIndices(FormatNode root, FormatNode target)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(root, target))
        {
            return [];
        }

        var path = new List<int>();
        var stack = new Stack<(FormatNode Node, int NextChild)>();
        stack.Push((root, 0));
        while (stack.TryPop(out var frame))
        {
            if (frame.NextChild >= frame.Node.Children.Length)
            {
                // This subtree is exhausted; drop its index from the current path.
                if (path.Count > 0)
                {
                    path.RemoveAt(path.Count - 1);
                }

                continue;
            }

            stack.Push((frame.Node, frame.NextChild + 1));
            var child = frame.Node.Children[frame.NextChild];
            path.Add(frame.NextChild);
            if (ReferenceEquals(child, target))
            {
                return [.. path];
            }

            if (child.Children.IsEmpty)
            {
                path.RemoveAt(path.Count - 1);
            }
            else
            {
                stack.Push((child, 0));
            }
        }

        return null;
    }
}
