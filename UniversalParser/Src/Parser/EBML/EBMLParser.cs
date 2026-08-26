using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace UniversalParser.Src.Parser.EBML
{
    internal sealed class EBMLParser : IParser
    {
        public string ContainerTypeName => "EBML Container";

        public FileStream FileStream { get; }

        public EBMLParserOptions Options { get; }
        /// <summary>
        /// 当前文件中 Segment Element 的负载起点。
        /// SeekPosition 使用相对于此位置的偏移。
        /// </summary>
        public long? SegmentPayloadStart { get; private set; }

        private readonly bool _ownsStream;
        private bool _disposed;

        public EBMLParser(
            FileStream fileStream,
            bool ownsStream = true,
            EBMLParserOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(fileStream);

            if (!fileStream.CanRead)
                throw new ArgumentException("FileStream must be readable.", nameof(fileStream));

            if (!fileStream.CanSeek)
                throw new ArgumentException("FileStream must be seekable.", nameof(fileStream));

            FileStream = fileStream;
            _ownsStream = ownsStream;
            Options = options ?? new EBMLParserOptions();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (!_ownsStream)
                return;

            try
            {
                FileStream.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EBMLParser] Dispose failed: {ex}");
            }
        }

        // ============================================================
        // Validation
        // ============================================================

        /// <summary>
        /// EBML 文件必须以 EBML Header Element 0x1A45DFA3 开始。
        /// 验证过程不会修改 FileStream.Position。
        /// </summary>
        public static bool IsValid(FileStream fileStream)
        {
            if (fileStream is null || !fileStream.CanRead || !fileStream.CanSeek)
                return false;

            try
            {
                // 最短情况：
                // EBML ID 4 bytes + Data Size 1 byte
                if (fileStream.Length < 5)
                    return false;

                Span<byte> buffer = stackalloc byte[EBMLUtil.MaxElementHeaderLength];

                int requested = (int)Math.Min(
                    buffer.Length,
                    fileStream.Length);

                if (!TryReadExactlyAt(
                        fileStream.SafeFileHandle,
                        buffer[..requested],
                        0))
                {
                    return false;
                }

                if (!EBMLUtil.TryDecodeElementId(
                        buffer,
                        out ulong elementId,
                        out int idLength))
                {
                    return false;
                }

                if (elementId != EBMLUtil.EbmlHeaderId)
                    return false;

                if (!EBMLUtil.TryDecodeDataSize(
                        buffer[idLength..],
                        out ulong dataSize,
                        out int sizeLength,
                        out bool unknownSize))
                {
                    return false;
                }

                // EBML Header 不允许未知大小。
                if (unknownSize)
                    return false;

                long headerLength = idLength + sizeLength;
                long availablePayload = fileStream.Length - headerLength;

                return dataSize <= (ulong)availablePayload;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EBMLParser] IsValid failed: {ex}");
                return false;
            }
        }

        private static bool TryReadExactlyAt(
            SafeFileHandle handle,
            Span<byte> destination,
            long offset)
        {
            int total = 0;

            while (total < destination.Length)
            {
                int read = RandomAccess.Read(
                    handle,
                    destination[total..],
                    offset + total);

                if (read <= 0)
                    return false;

                total += read;
            }

            return true;
        }

        // ============================================================
        // Parse
        // ============================================================

        public async Task<Node> ParseAsync(
            IProgress<ParserProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long fileLength = FileStream.Length;
            if (fileLength < 5)
                throw new InvalidDataException("File is too small to contain an EBML Header.");

            var reader = new PositionalReader(
                FileStream.SafeFileHandle,
                fileLength,
                Options.ReadBufferSize);

            EBMLElementHeader? firstHeaderResult =
                await TryReadElementHeaderAsync(
                    0,
                    fileLength,
                    reader,
                    cancellationToken);

            if (firstHeaderResult is null)
            {
                throw new InvalidDataException(
                    "Unable to read the first EBML Element.");
            }

            EBMLElementHeader firstHeader = firstHeaderResult.Value;

            if (firstHeader.ElementId != EBMLUtil.EbmlHeaderId)
            {
                throw new InvalidDataException(
                    $"The first Element is {firstHeader.FormattedId}, expected 0x1A45DFA3.");
            }
            SegmentPayloadStart = await FindSegmentPayloadStartAsync(
                firstHeader.PayloadEnd,
                fileLength,
                reader,
                cancellationToken);

            string fileName = Path.GetFileName(FileStream.Name);

            var root = new Node(
                fileName,
                0,
                (ulong)fileLength);

            var progressState = new ProgressState(progress, fileLength);

            await ParseElementSequenceAsync(
                root,
                start: 0,
                end: fileLength,
                depth: 0,
                reader,
                progressState,
                cancellationToken);

            progressState.ReportFinal();
            return root;
        }
        private async ValueTask<long?> FindSegmentPayloadStartAsync(
            long start,
            long end,
            PositionalReader reader,
            CancellationToken cancellationToken)
        {
            long position = start;

            while (position + 2 <= end)
            {
                EBMLElementHeader? result =
                    await TryReadElementHeaderAsync(
                        position,
                        end,
                        reader,
                        cancellationToken);

                if (result is null)
                    return null;

                EBMLElementHeader header = result.Value;

                if (header.ElementId == EBMLUtil.SegmentId)
                    return header.PayloadStart;

                if (header.ElementLength <= 0)
                    return null;

                long next = header.ElementStart + header.ElementLength;

                if (next <= position)
                    return null;

                position = next;
            }

            return null;
        }

        private async Task ParseElementSequenceAsync(
            Node parent,
            long start,
            long end,
            int depth,
            PositionalReader reader,
            ProgressState progressState,
            CancellationToken cancellationToken)
        {
            long position = start;
            int childCount = 0;
            bool stopped = false;

            while (position < end)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (childCount >= Options.MaxChildrenPerContainer)
                {
                    AddSyntheticNode(
                        parent,
                        "<listing stopped>",
                        position,
                        end - position);

                    stopped = true;
                    break;
                }

                EBMLElementHeader? headerResult =
                    await TryReadElementHeaderAsync(
                        position,
                        end,
                        reader,
                        cancellationToken);

                if (headerResult is null)
                {
                    AddSyntheticNode(
                        parent,
                        "<unrecognized data>",
                        position,
                        end - position);

                    stopped = true;
                    break;
                }

                EBMLElementHeader header = headerResult.Value;

                string readableName = EBMLSchema.GetName(header.ElementId);
                string nodeName = readableName == "Unknown"
                    ? header.FormattedId
                    : $"{readableName} ({header.FormattedId})";

                if (header.IsUnknownSize)
                    nodeName += " [unknown size]";

                if (header.IsTruncated)
                    nodeName += " [truncated]";

                var node = new Node(
                    nodeName,
                    (ulong)header.ElementStart,
                    (ulong)header.ElementLength);

                parent.SubNodes.Add(node);
                childCount++;

                if (header.IsMaster && header.PayloadLength > 0)
                {
                    if (depth < Options.MaxDepth)
                    {
                        await ParseElementSequenceAsync(
                            node,
                            header.PayloadStart,
                            header.PayloadEnd,
                            depth + 1,
                            reader,
                            progressState,
                            cancellationToken);
                    }
                    else
                    {
                        AddSyntheticNode(
                            node,
                            "<max depth reached>",
                            header.PayloadStart,
                            header.PayloadLength);
                    }
                }

                progressState.Report(header.PayloadEnd);

                long next = header.PayloadEnd;

                if (next <= position)
                {
                    Debug.Fail(
                        $"EBML Element at 0x{position:X} did not advance.");

                    AddSyntheticNode(
                        parent,
                        "<parser stopped>",
                        position,
                        end - position);

                    stopped = true;
                    break;
                }

                position = next;

                // 已知大小 Element 越过父边界时已经被裁剪；
                // 继续扫描没有意义。
                if (header.IsTruncated)
                {
                    stopped = true;
                    break;
                }

                // 未知大小的 Element 使用父范围作为终点，因此不会有后续 sibling。
                if (header.IsUnknownSize)
                {
                    stopped = true;
                    break;
                }
            }

            if (!stopped && position < end)
            {
                AddSyntheticNode(
                    parent,
                    "<unparsed data>",
                    position,
                    end - position);
            }
        }

        private async ValueTask<EBMLElementHeader?> TryReadElementHeaderAsync(
            long elementStart,
            long parentEnd,
            PositionalReader reader,
            CancellationToken cancellationToken)
        {
            if (elementStart < 0 || elementStart >= parentEnd)
                return null;

            byte[] buffer = new byte[EBMLUtil.MaxElementHeaderLength];

            int available = (int)Math.Min(
                buffer.Length,
                parentEnd - elementStart);

            // 最短 Element Header：
            // 1 byte Element ID + 1 byte Data Size
            if (available < 2)
                return null;

            int read = await reader.ReadAtAsync(
                elementStart,
                buffer.AsMemory(0, available),
                cancellationToken);

            if (read <= 0)
                return null;

            ReadOnlySpan<byte> span = buffer.AsSpan(0, read);

            if (!EBMLUtil.TryDecodeElementId(
                    span,
                    out ulong elementId,
                    out int idLength))
            {
                return null;
            }

            if (span.Length <= idLength)
                return null;

            if (!EBMLUtil.TryDecodeDataSize(
                    span[idLength..],
                    out ulong declaredDataSize,
                    out int sizeLength,
                    out bool unknownSize))
            {
                return null;
            }

            int headerLength = idLength + sizeLength;
            long payloadStart = elementStart + headerLength;

            if (payloadStart > parentEnd)
                return null;

            long availablePayload = parentEnd - payloadStart;
            long payloadLength;

            if (unknownSize)
            {
                // 未知大小 Element 的实际范围由父 Element 边界决定
                payloadLength = availablePayload;
            }
            else
            {
                payloadLength = declaredDataSize > (ulong)availablePayload
                    ? availablePayload
                    : (long)declaredDataSize;
            }

            bool isMaster = EBMLSchema.IsMaster(
                elementId,
                Options);

            return new EBMLElementHeader
            {
                ElementId = elementId,
                ElementIdLength = idLength,
                DataSizeLength = sizeLength,
                DeclaredDataSize = declaredDataSize,
                IsUnknownSize = unknownSize,
                IsMaster = isMaster,
                ElementStart = elementStart,
                PayloadStart = payloadStart,
                PayloadLength = payloadLength,
            };
        }

        private static void AddSyntheticNode(
            Node parent,
            string name,
            long offset,
            long length)
        {
            if (length <= 0)
                return;

            parent.SubNodes.Add(
                new Node(name, (ulong)offset, (ulong)length));
        }

        // ============================================================
        // Element parser API
        // ============================================================

        public bool TryGetElementHeader(
            Node node,
            out EBMLElementHeader header)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(node);

            header = default;

            long elementStart = (long)node.Position;
            long nodeLength = (long)node.Length;
            long fileLength = FileStream.Length;

            if (elementStart < 0 || nodeLength < 2 || elementStart >= fileLength)
                return false;

            long nodeEnd = Math.Min(
                elementStart + nodeLength,
                fileLength);

            int available = (int)Math.Min(
                EBMLUtil.MaxElementHeaderLength,
                nodeEnd - elementStart);

            if (available < 2)
                return false;

            Span<byte> buffer = stackalloc byte[EBMLUtil.MaxElementHeaderLength];

            if (!TryReadExactlyAt(
                    FileStream.SafeFileHandle,
                    buffer[..available],
                    elementStart))
            {
                return false;
            }

            ReadOnlySpan<byte> data = buffer[..available];

            if (!EBMLUtil.TryDecodeElementId(
                    data,
                    out ulong elementId,
                    out int idLength))
            {
                return false;
            }

            if (data.Length <= idLength)
                return false;

            if (!EBMLUtil.TryDecodeDataSize(
                    data[idLength..],
                    out ulong declaredDataSize,
                    out int sizeLength,
                    out bool unknownSize))
            {
                return false;
            }

            int headerLength = idLength + sizeLength;
            long payloadStart = elementStart + headerLength;

            if (payloadStart > nodeEnd)
                return false;

            long availablePayload = nodeEnd - payloadStart;
            long payloadLength;

            if (unknownSize)
            {
                payloadLength = availablePayload;
            }
            else
            {
                payloadLength = declaredDataSize > (ulong)availablePayload
                    ? availablePayload
                    : (long)declaredDataSize;
            }

            header = new EBMLElementHeader
            {
                ElementId = elementId,
                ElementIdLength = idLength,
                DataSizeLength = sizeLength,
                DeclaredDataSize = declaredDataSize,
                IsUnknownSize = unknownSize,
                IsMaster = EBMLSchema.IsMaster(elementId, Options),
                ElementStart = elementStart,
                PayloadStart = payloadStart,
                PayloadLength = payloadLength,
            };

            return true;
        }

        /// <summary>
        /// 从指定绝对偏移读取数据，不修改 FileStream.Position。
        /// </summary>
        public int ReadAt(long offset, Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (destination.IsEmpty)
                return 0;

            long fileLength = FileStream.Length;

            if (offset < 0 || offset >= fileLength)
                return 0;

            int maximum = (int)Math.Min(
                destination.Length,
                fileLength - offset);

            int total = 0;

            while (total < maximum)
            {
                int read = RandomAccess.Read(
                    FileStream.SafeFileHandle,
                    destination.Slice(total, maximum - total),
                    offset + total);

                if (read <= 0)
                    break;

                total += read;
            }

            return total;
        }

        public Stream CreateRawStream(long offset, long length)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long fileLength = FileStream.Length;

            offset = Math.Clamp(offset, 0, fileLength);
            length = Math.Clamp(length, 0, fileLength - offset);

            return new OffsetStream(
                FileStream,
                offset,
                length);
        }

        public EBMLReader CreatePayloadReader(
            in EBMLElementHeader header) =>
            new(CreateRawStream(
                header.PayloadStart,
                header.PayloadLength));

        public ParseResult ParseNode(Node node)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return EBMLDispatcher.Dispatch(this, node);
        }

        // ============================================================
        // Buffered positional reader
        // ============================================================

        private sealed class PositionalReader
        {
            private readonly SafeFileHandle _handle;
            private readonly long _fileLength;
            private readonly byte[] _buffer;

            private long _bufferStart = -1;
            private int _bufferLength;

            public PositionalReader(
                SafeFileHandle handle,
                long fileLength,
                int capacity)
            {
                _handle = handle;
                _fileLength = fileLength;
                _buffer = new byte[Math.Clamp(
                    capacity,
                    4096,
                    4 * 1024 * 1024)];
            }

            public async ValueTask<int> ReadAtAsync(
                long position,
                Memory<byte> destination,
                CancellationToken cancellationToken)
            {
                if (destination.IsEmpty)
                    return 0;

                if (position < 0 || position >= _fileLength)
                    return 0;

                int requested = destination.Length;

                if (requested > _buffer.Length)
                {
                    return await ReadDirectAsync(
                        position,
                        destination,
                        cancellationToken);
                }

                bool cacheHit =
                    _bufferStart >= 0 &&
                    position >= _bufferStart &&
                    position + requested <= _bufferStart + _bufferLength;

                if (!cacheHit)
                {
                    _bufferStart = position;
                    _bufferLength = 0;

                    int wanted = (int)Math.Min(
                        _buffer.Length,
                        _fileLength - position);

                    while (_bufferLength < wanted)
                    {
                        int read = await RandomAccess.ReadAsync(
                            _handle,
                            _buffer.AsMemory(
                                _bufferLength,
                                wanted - _bufferLength),
                            position + _bufferLength,
                            cancellationToken);

                        if (read <= 0)
                            break;

                        _bufferLength += read;
                    }
                }

                int available = (int)Math.Min(
                    requested,
                    _bufferStart + _bufferLength - position);

                if (available <= 0)
                    return 0;

                _buffer.AsMemory(
                    (int)(position - _bufferStart),
                    available).CopyTo(destination);

                return available;
            }

            private async ValueTask<int> ReadDirectAsync(
                long position,
                Memory<byte> destination,
                CancellationToken cancellationToken)
            {
                int total = 0;

                int maximum = (int)Math.Min(
                    destination.Length,
                    _fileLength - position);

                while (total < maximum)
                {
                    int read = await RandomAccess.ReadAsync(
                        _handle,
                        destination.Slice(
                            total,
                            maximum - total),
                        position + total,
                        cancellationToken);

                    if (read <= 0)
                        break;

                    total += read;
                }

                return total;
            }
        }

        // ============================================================
        // Progress
        // ============================================================

        private sealed class ProgressState(
            IProgress<ParserProgress>? progress,
            long totalBytes)
        {
            private const int ThrottleMilliseconds = 200;

            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private long _lastReportMilliseconds = -ThrottleMilliseconds;
            private long _maximumPosition;

            public void Report(long position)
            {
                if (position > _maximumPosition)
                    _maximumPosition = position;

                if (progress is null)
                    return;

                long milliseconds = _stopwatch.ElapsedMilliseconds;

                if (milliseconds - _lastReportMilliseconds < ThrottleMilliseconds)
                    return;

                _lastReportMilliseconds = milliseconds;
                Emit(milliseconds);
            }

            public void ReportFinal()
            {
                if (progress is null)
                    return;

                _maximumPosition = totalBytes;
                Emit(_stopwatch.ElapsedMilliseconds);
            }

            private void Emit(long elapsedMilliseconds)
            {
                double seconds = elapsedMilliseconds / 1000.0;

                long position = Math.Clamp(
                    _maximumPosition,
                    0,
                    Math.Max(0, totalBytes));

                progress!.Report(new ParserProgress
                {
                    Fraction = totalBytes > 0
                        ? Math.Clamp(
                            (double)position / totalBytes,
                            0.0,
                            1.0)
                        : 1.0,

                    BytesRead = (ulong)position,
                    TotalBytes = (ulong)Math.Max(0, totalBytes),

                    BytesPerSecond = seconds > 0
                        ? position / seconds
                        : 0.0,
                });
            }
        }
    }
}