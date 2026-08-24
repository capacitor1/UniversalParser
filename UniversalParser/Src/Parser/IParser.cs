using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalParser.Src.Parser
{
    // Progress report used by parsers to report rich progress data
    public readonly struct ParserProgress
    {
        // fraction: 0.0 .. 1.0
        public double Fraction { get; init; }
        // bytes read so far
        public ulong BytesRead { get; init; }
        // total bytes, 0 if unknown
        public ulong? TotalBytes { get; init; }
        // instantaneous speed in bytes/sec
        public double BytesPerSecond { get; init; }
    }

    // Generic parser interface for all container parsers
    public interface IParser : IDisposable
    {
        string ContainerTypeName { get; }

        // FileStream is owned by parser for its lifetime
        FileStream FileStream { get; }

        // Parse the file
        Task<Node> ParseAsync(IProgress<ParserProgress>? progress = null, CancellationToken cancellationToken = default);

        // Static validation method
        static abstract bool IsValid(FileStream fs);

        //parse
        ParseResult ParseNode(Node node);
    }
}
