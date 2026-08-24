using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace UniversalParser.Src.Parser.JPEG
{
    internal sealed class JPEGParser : IParser
    {
        public string ContainerTypeName => "JPEG Image";
        public FileStream FileStream { get; private set; }

        private bool _disposed;

        public JPEGParser(FileStream fs)
        {
            if (fs == null || !fs.CanRead)
                throw new ArgumentException("FileStream must be readable.");
            FileStream = fs;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try { FileStream?.Dispose(); } catch { }
                _disposed = true;
            }
        }

        // =========================
        // VALIDATION (simple & correct)
        // =========================
        public static bool IsValid(FileStream fs)
        {
            if (fs == null || !fs.CanRead)
                return false;

            long originalPos = fs.Position;

            try
            {
                if (fs.Length < 2)
                    return false;

                fs.Seek(0, SeekOrigin.Begin);

                int b1 = fs.ReadByte();
                int b2 = fs.ReadByte();

                // JPEG SOI: 0xFF 0xD8
                if (b1 == 0xFF && b2 == 0xD8)
                    return true;

                return false;
            }
            finally
            {
                fs.Seek(originalPos, SeekOrigin.Begin);
            }
        }

        // =========================
        // PARSE
        // =========================
        public async Task<Node> ParseAsync(
    IProgress<ParserProgress>? progress = null,
    CancellationToken cancellationToken = default)
        {
            //0
            FileStream.Seek(0, SeekOrigin.Begin);
            long fileLen = FileStream.Length;
            var root = new Node(Path.GetFileName(FileStream.Name), 0, (ulong)fileLen);
            DateTime start = DateTime.UtcNow;
            DateTime last = start;
            //1
            long position = 0;
            long lastPosition = -1;
            while (true)
            {
                long offset = position;
                int code = FileStream.ReadByte();
                if (code < 0)
                    break;
                if (code != 0xFF)
                {
                    if (lastPosition >= 0)
                    {
                        position++;
                        continue;
                    }
                    throw new InvalidDataException($"Invalid JPEG marker at position {position}: expected 0xFF, got 0x{code:X2}.");
                }
                position++;
                int marker = FileStream.ReadByte();
                if (marker < 0)
                    throw new EndOfStreamException("Unexpected end of stream while reading JPEG marker.");
                if (marker == 0xFF)
                    throw new InvalidDataException($"Invalid JPEG marker at position {position}: unexpected 0xFF byte.");
                if (marker == 0x00)
                {
                    if (lastPosition >= 0)
                    {
                        position++;
                        continue;
                    }
                    throw new InvalidDataException($"Invalid JPEG marker at position {position}: unexpected 0x00 byte.");
                }
                position++;
                if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
                {
                    if (lastPosition >= 0)
                    {
                        long diff = offset - lastPosition;
                        if(diff > 0)
                            root.SubNodes.Add(new Node($"SCAN",(ulong)lastPosition,(ulong)(position - lastPosition - 2)));
                        lastPosition = position;
                    }
                    root.SubNodes.Add(new Node($"FF{marker:X2}", (ulong)offset, 2));
                    if (marker == 0xD9)
                        break;
                    else
                        continue;
                }
                int lengthHi = FileStream.ReadByte();
                int lengthLo = FileStream.ReadByte();
                int segmentLength = (lengthHi << 8) | lengthLo;
                if (segmentLength < 0)
                    throw new EndOfStreamException("Unexpected end of stream while reading JPEG segment length.");
                if (segmentLength < 2)
                    throw new InvalidDataException($"Invalid JPEG segment length at position {position}: must be at least 2, got {segmentLength}.");
                position += segmentLength;
                root.SubNodes.Add(new Node($"FF{marker:X2}", (ulong)offset, (ulong)segmentLength + 2));
                FileStream.Seek(segmentLength - 2,SeekOrigin.Current);
                if (marker == 0xDA || lastPosition >= 0)
                {
                    lastPosition = position;
                }
                //2
                var now = DateTime.UtcNow;
                if ((now - last).TotalMilliseconds > 200 || position >= fileLen)
                {
                    double sec = (now - start).TotalSeconds;

                    progress?.Report(new ParserProgress
                    {
                        Fraction = fileLen == 0 ? 1 : (double)position / fileLen,
                        BytesRead = (ulong)position,
                        TotalBytes = (ulong)fileLen,
                        BytesPerSecond = sec > 0 ? position / sec : 0
                    });
                    Application.DoEvents();
                    last = now;
                }
            }

            return root;
        }

        public ParseResult ParseNode(Node node)
        {
            return JPEGDispatcher.Dispatch(this, node);
        }
    }
}