using System.Collections.Generic;

namespace UniversalParser.Src.Parser
{
    // Generic Node used to represent parsed data structures.
    public sealed class Node
    {
        public string NodeName { get; set; } = string.Empty;
        public ulong Position { get; set; }
        public ulong Length { get; set; }
        public List<Node> SubNodes { get; } = new List<Node>();

        public Node() { }
        public Node(string name, ulong pos, ulong len)
        {
            NodeName = name;
            Position = pos;
            Length = len;
        }
        public override string ToString() => NodeName;
    }
}
