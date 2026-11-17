using UniversalParser.Core.Model;

namespace UniversalParser.Core.Statistics;

/// <summary>Largest nodes retained for one node kind, ordered by descending size.</summary>
public sealed record NodeKindRank(NodeKind Kind, IReadOnlyList<FormatNode> Nodes);
