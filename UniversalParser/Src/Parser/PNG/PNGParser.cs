using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniversalParser.Src.Parser.PNG.Chunks;

namespace UniversalParser.Src.Parser.PNG
{
    public sealed class PNGParser : IParser
    {
        public string ContainerTypeName => "PNG Image";
        public FileStream FileStream { get; private set; }

        private bool _disposed;

        private static readonly byte[] PngSignature =
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        ];

        public PNGParser(FileStream fs)
        {
            if (fs == null || !fs.CanRead)
                throw new ArgumentException("FileStream must be readable.");

            FileStream = fs;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try { FileStream.Dispose(); } catch { }
                _disposed = true;
            }
        }

        // =========================
        // VALIDATION
        // =========================
        public static bool IsValid(FileStream fs)
        {
            if (fs.Length < 8) return false;

            long old = fs.Position;
            fs.Seek(0, SeekOrigin.Begin);

            Span<byte> sig = stackalloc byte[8];
            fs.ReadExactly(sig);

            fs.Seek(old, SeekOrigin.Begin);

            for (int i = 0; i < 8; i++)
            {
                if (sig[i] != PngSignature[i])
                    return false;
            }

            return true;
        }

        // =========================
        // PARSE
        // =========================
        public async Task<Node> ParseAsync(
    IProgress<ParserProgress>? progress = null,
    CancellationToken cancellationToken = default)
        {
            FileStream.Seek(0, SeekOrigin.Begin);

            // read signature (async)
            byte[] sig = new byte[8];
            await FileStream.ReadExactlyAsync(sig, cancellationToken);

            var root = new Node(Path.GetFileName(FileStream.Name), 0, (ulong)FileStream.Length);

            long pos = 8;
            long fileLen = FileStream.Length;

            DateTime start = DateTime.UtcNow;
            DateTime last = start;

            byte[] header = new byte[8];

            while (pos + 12 <= fileLen)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileStream.Seek(pos, SeekOrigin.Begin);

                // =========================
                // async read chunk header
                // =========================
                await FileStream.ReadExactlyAsync(header, cancellationToken);

                uint length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
                string type = Encoding.ASCII.GetString(header, 4, 4);

                long dataStart = pos + 8;
                long dataEnd = dataStart + length + 4; // +CRC

                if (dataEnd > fileLen)
                    break;

                var node = new Node(type, (ulong)pos, (ulong)(dataEnd - pos));
                root.SubNodes.Add(node);

                pos = dataEnd;

                // =========================
                // progress
                // =========================
                var now = DateTime.UtcNow;
                if ((now - last).TotalMilliseconds > 200 || pos >= fileLen)
                {
                    double sec = (now - start).TotalSeconds;

                    progress?.Report(new ParserProgress
                    {
                        Fraction = fileLen == 0 ? 1 : (double)pos / fileLen,
                        BytesRead = (ulong)pos,
                        TotalBytes = (ulong)fileLen,
                        BytesPerSecond = sec > 0 ? pos / sec : 0
                    });

                    last = now;
                }
            }

            return root;
        }

        public ParseResult ParseNode(Node node)
        {
            try
            {
                return PNGDispatcher.Dispatch(this, node);
            }
            catch (Exception ex)
            {
                return new ParseResult
                {
                    Title = "[Parser Error]",
                    Position = node.Position,
                    Length = node.Length,
                    DataLines =
                    [
                        ("<Error>", ex.GetType().Name),
                        ("<Message>", ex.Message)
                    ],
                    RawData = new MemoryStream(Encoding.ASCII.GetBytes(ex.Message)),
                };
            }
        }
    }
}