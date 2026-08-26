using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalParser.Src.Parser.RawData
{
    /// <summary>
    /// 文件类型检测全部失败后使用的兜底解析器。
    /// 整个文件被视为一个名为 Data 的块。
    /// </summary>
    internal sealed class RawDataParser : IParser
    {
        public string ContainerTypeName => "Raw Data";

        public FileStream FileStream { get; }

        private readonly bool _ownsStream;
        private bool _disposed;

        public RawDataParser(FileStream fs, bool ownsStream = true)
        {
            ArgumentNullException.ThrowIfNull(fs);

            if (!fs.CanRead)
                throw new ArgumentException("FileStream must be readable.", nameof(fs));

            FileStream = fs;
            _ownsStream = ownsStream;
        }

        /// <summary>
        /// Raw Data 是最终兜底格式，因此只要流可读就始终有效。
        /// </summary>
        public static bool IsValid(FileStream fs)
        {
            return fs is not null && fs.CanRead;
        }

        public Task<Node> ParseAsync(
            IProgress<ParserProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            long fileLength = FileStream.Length;

            var node = new Node(
                Path.GetFileName(FileStream.Name),
                0,
                (ulong)fileLength);
            node.SubNodes.Add(new Node(
                "Data",
                0,
                (ulong)fileLength));

            progress?.Report(new ParserProgress
            {
                Fraction = 1.0,
                BytesRead = (ulong)fileLength,
                TotalBytes = (ulong)fileLength,
                BytesPerSecond = 0.0,
            });

            return Task.FromResult(node);
        }

        public ParseResult ParseNode(Node node)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(node);

            return Data.Parse(this, node);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            if (_ownsStream)
                FileStream.Dispose();
        }
    }
}