using UniversalParser.Core.Model;

namespace UniversalParser.Core.Statistics;

/// <summary>
/// A bounded, source-independent snapshot of the immutable structure tree. The walk is iterative so
/// a pathological container cannot exhaust the stack; statistics never touch the file source.
/// </summary>
public sealed record StatisticsSnapshot(
    long NodeCount,
    int MaxDepth,
    IReadOnlyList<NodeKindRank> LargestByKind,
    IReadOnlyList<NameStat> Names,
    IReadOnlyList<StatisticsShare> ByteShares,
    IReadOnlyList<StatisticsShare> KindShares)
{
    /// <summary>
    /// Largest nodes kept per kind. The UI filters by kind and then ranks the merged result, so a
    /// kind must not be crowded out by a larger kind before the filter is applied.
    /// </summary>
    public const int LargestNodeCount = 200;

    /// <summary>Distinct names are few in practice; the cap only guards a pathological tree.</summary>
    private const int TopNameCount = 1000;

    /// <param name="root">Document root; it is excluded from the node count.</param>
    /// <param name="topSlices">
    /// Maximum entries in the byte chart, including the synthetic "Other names" slice.
    /// </param>
    /// <param name="token">Cancels the walk when the statistics window closes.</param>
    public static StatisticsSnapshot Compute(
        FormatNode root,
        int topSlices,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentOutOfRangeException.ThrowIfLessThan(topSlices, 2);

        long nodeCount = 0;
        long framingBytes = 0;
        var maxDepth = 0;
        var byName = new Dictionary<string, NameStat>(StringComparer.Ordinal);
        var byKind = new Dictionary<NodeKind, long>();
        var largestQueues = new Dictionary<NodeKind, PriorityQueue<FormatNode, long>>();
        var stack = new Stack<(FormatNode Node, int Depth, int NextChild)>();
        stack.Push((root, 0, -1));

        // Resume parent frames instead of pushing every sibling: traversal memory is O(depth),
        // even when a container has a million children.
        while (stack.TryPop(out var frame))
        {
            token.ThrowIfCancellationRequested();
            var (node, depth, nextChild) = frame;
            if (nextChild >= 0)
            {
                if (nextChild < node.Children.Length)
                {
                    stack.Push((node, depth, nextChild + 1));
                    stack.Push((node.Children[nextChild], depth + 1, -1));
                }

                continue;
            }

            maxDepth = Math.Max(maxDepth, depth);
            var ownedBytes = node.Range.Length;
            foreach (var child in node.Children)
            {
                token.ThrowIfCancellationRequested();
                ownedBytes -= child.Range.Length;
            }

            // Parser trees have disjoint children. Keep a malformed tree from producing a
            // negative pie slice; range validation remains the parser's responsibility.
            ownedBytes = Math.Max(0, ownedBytes);
            if (depth == 0)
            {
                framingBytes = ownedBytes;
            }
            else
            {
                nodeCount++;
                byKind[node.Kind] = byKind.GetValueOrDefault(node.Kind) + 1;
                if (byName.TryGetValue(node.Name, out var stat))
                {
                    byName[node.Name] = stat with
                    {
                        Count = stat.Count + 1,
                        TotalSize = stat.TotalSize + node.Range.Length,
                        OwnedBytes = stat.OwnedBytes + ownedBytes,
                        MaxSize = Math.Max(stat.MaxSize, node.Range.Length),
                    };
                }
                else
                {
                    byName.Add(node.Name, new NameStat(node.Name, 1, node.Range.Length, ownedBytes, node.Range.Length));
                }

                if (!largestQueues.TryGetValue(node.Kind, out var queue))
                {
                    queue = new PriorityQueue<FormatNode, long>();
                    largestQueues.Add(node.Kind, queue);
                }

                queue.Enqueue(node, node.Range.Length);
                if (queue.Count > LargestNodeCount)
                {
                    queue.Dequeue();
                }
            }

            if (!node.Children.IsEmpty)
            {
                stack.Push((node, depth, 0));
            }
        }

        token.ThrowIfCancellationRequested();
        var byteShares = byName.Values
            .Where(static stat => stat.OwnedBytes > 0)
            .Select(static stat => new StatisticsShare(stat.Name, stat.OwnedBytes))
            .ToList();
        if (framingBytes > 0)
        {
            byteShares.Add(new StatisticsShare("Document framing", framingBytes));
        }

        var countedBytes = byteShares.Sum(static share => share.Value);
        if (countedBytes < root.Range.Length)
        {
            byteShares.Add(new StatisticsShare("Not counted", root.Range.Length - countedBytes));
        }

        var orderedBytes = byteShares
            .OrderByDescending(static share => share.Value)
            .ThenBy(static share => share.Name, StringComparer.Ordinal)
            .ToList();
        var chartShares = orderedBytes.Count <= topSlices
            ? orderedBytes
            : orderedBytes.Take(topSlices - 1).ToList();
        if (chartShares.Count < orderedBytes.Count)
        {
            var otherBytes = orderedBytes.Skip(chartShares.Count).Sum(static share => share.Value);
            chartShares.Add(new StatisticsShare("Other names", otherBytes));
        }

        var largestByKind = largestQueues
            .OrderBy(static pair => pair.Key)
            .Select(static pair => new NodeKindRank(
                pair.Key,
                pair.Value.UnorderedItems
                    .Select(static entry => entry.Element)
                    .OrderByDescending(static node => node.Range.Length)
                    .ThenBy(static node => node.Range.Offset)
                    .ToArray()))
            .ToArray();

        // Every distinct name is listed; names that occur once are shown greyed out by the UI so
        // the reader can still see them without pretending they repeat.
        var names = byName.Values
            .OrderByDescending(static stat => stat.Count)
            .ThenByDescending(static stat => stat.TotalSize)
            .ThenBy(static stat => stat.Name, StringComparer.Ordinal)
            .Take(TopNameCount)
            .ToArray();

        var kindShares = byKind
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key)
            .Select(static pair => new StatisticsShare(NodeKindNames.Plural(pair.Key), pair.Value))
            .ToArray();

        token.ThrowIfCancellationRequested();
        return new StatisticsSnapshot(nodeCount, maxDepth, largestByKind, names, chartShares, kindShares);
    }
}
