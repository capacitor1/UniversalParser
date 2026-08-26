using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace UniversalParser.Src.Parser.FLV
{
    internal sealed class FLVParser : IParser
    {
        public string ContainerTypeName => "Flash Video";
        public FileStream FileStream { get; }
        public FLVParserOptions Options { get; }

        public byte Version { get; private set; }
        public bool HasAudio { get; private set; }
        public bool HasVideo { get; private set; }
        public uint DataOffset { get; private set; }

        private readonly bool _ownsStream;
        private bool _disposed;

        public FLVParser(
            FileStream fileStream,
            bool ownsStream = true,
            FLVParserOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(fileStream);

            if (!fileStream.CanRead)
                throw new ArgumentException("FileStream must be readable.", nameof(fileStream));

            if (!fileStream.CanSeek)
                throw new ArgumentException("FileStream must be seekable.", nameof(fileStream));

            FileStream = fileStream;
            _ownsStream = ownsStream;
            Options = options ?? new FLVParserOptions();
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
                Debug.WriteLine($"[FLVParser] Dispose failed: {ex}");
            }
        }

        // ============================================================
        // VALIDATION
        // ============================================================

        public static bool IsValid(FileStream fileStream)
        {
            if (fileStream is null || !fileStream.CanRead || !fileStream.CanSeek)
                return false;

            try
            {
                if (fileStream.Length < FLVUtil.MinimumHeaderSize)
                    return false;

                Span<byte> header = stackalloc byte[FLVUtil.MinimumHeaderSize];

                if (!TryReadExactlyAt(fileStream.SafeFileHandle, header, 0))
                    return false;

                if (header[0] != (byte)'F'
                    || header[1] != (byte)'L'
                    || header[2] != (byte)'V')
                {
                    return false;
                }

                // 当前公开规范版本为 1。保留未来版本的识别能力，但拒绝 0。
                if (header[3] == 0)
                    return false;

                uint dataOffset = FLVUtil.ReadUInt32BE(header.Slice(5, 4));

                if (dataOffset < FLVUtil.MinimumHeaderSize)
                    return false;

                // DataOffset 可以正好等于文件尾，表示只有 Header、没有 PreviousTagSize0。
                return dataOffset <= fileStream.Length;
            }
            catch
            {
                return false;
            }
        }

        // ============================================================
        // PARSE
        // ============================================================

        public async Task<Node> ParseAsync(
            IProgress<ParserProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long fileLength = FileStream.Length;

            if (fileLength < FLVUtil.MinimumHeaderSize)
                throw new InvalidDataException("File is too small to contain an FLV header.");

            var reader = new PositionalReader(
                FileStream.SafeFileHandle,
                fileLength,
                Options.ReadBufferSize);

            byte[] header = new byte[FLVUtil.MinimumHeaderSize];

            if (await reader.ReadAtAsync(0, header, cancellationToken) != header.Length)
                throw new InvalidDataException("Unable to read the FLV header.");

            if (header[0] != 'F' || header[1] != 'L' || header[2] != 'V')
                throw new InvalidDataException("The file does not have an FLV signature.");

            Version = header[3];

            byte typeFlags = header[4];
            HasAudio = (typeFlags & 0x04) != 0;
            HasVideo = (typeFlags & 0x01) != 0;
            DataOffset = FLVUtil.ReadUInt32BE(header.AsSpan(5, 4));

            if (DataOffset < FLVUtil.MinimumHeaderSize)
                throw new InvalidDataException("DataOffset is smaller than the mandatory FLV header.");

            if (DataOffset > fileLength)
                throw new InvalidDataException("DataOffset exceeds the physical file size.");

            var root = new Node(
                Path.GetFileName(FileStream.Name),
                0,
                (ulong)fileLength);

            /*
             * 将 PreviousTagSize0 归入 FLV Header 节点。
             *
             * 这样节点布局完全连续：
             *   FLV = Header + optional header extension + PreviousTagSize0
             *   Tag = TagHeader + TagData + following PreviousTagSize
             */
            long firstPreviousTagSizeLength =
                DataOffset + FLVUtil.PreviousTagSizeFieldSize <= fileLength
                    ? FLVUtil.PreviousTagSizeFieldSize
                    : Math.Max(0, fileLength - DataOffset);

            long flvNodeLength = DataOffset + firstPreviousTagSizeLength;

            root.SubNodes.Add(new Node(
                "FLV",
                0,
                (ulong)flvNodeLength));

            var progressState = new ProgressState(progress, fileLength);
            progressState.Report(flvNodeLength);

            long position = DataOffset + FLVUtil.PreviousTagSizeFieldSize;

            // 文件刚好在 DataOffset 或不完整 PreviousTagSize0 处结束
            if (position > fileLength)
            {
                progressState.ReportFinal();
                return root;
            }

            byte[] tagHeader = new byte[FLVUtil.TagHeaderSize];
            int tagCount = 0;

            while (position + FLVUtil.TagHeaderSize <= fileLength)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (tagCount >= Options.MaxTagCount)
                {
                    long remaining = fileLength - position;

                    if (remaining > 0)
                    {
                        root.SubNodes.Add(new Node(
                            "<tag limit reached>",
                            (ulong)position,
                            (ulong)remaining));
                    }

                    break;
                }

                int read = await reader.ReadAtAsync(position, tagHeader, cancellationToken);

                if (read != FLVUtil.TagHeaderSize)
                    break;

                byte first = tagHeader[0];
                byte tagType = (byte)(first & 0x1F);
                uint dataSize = FLVUtil.ReadUInt24BE(tagHeader.AsSpan(1, 3));

                long dataStart = position + FLVUtil.TagHeaderSize;
                long declaredDataEnd = dataStart + dataSize;
                long actualDataEnd = Math.Min(declaredDataEnd, fileLength);
                long actualDataSize = Math.Max(0, actualDataEnd - dataStart);

                bool hasPreviousTagSize =
                    declaredDataEnd + FLVUtil.PreviousTagSizeFieldSize <= fileLength;

                long tagEnd = hasPreviousTagSize
                    ? declaredDataEnd + FLVUtil.PreviousTagSizeFieldSize
                    : actualDataEnd;

                if (tagEnd <= position)
                    break;

                string tagNodeName = FLVUtil.GetTagNodeName(tagType);
                string dataNodeName = FLVUtil.GetTagDataNodeName(tagType);

                var tagNode = new Node(
                    tagNodeName,
                    (ulong)position,
                    (ulong)(tagEnd - position));

                /*
                 * 每个 Tag 在构树阶段立即创建且只创建一个 TagData 子节点。
                 * 即使 DataSize 为 0，也保留零长度子节点，从而保持结构一致。
                 */
                tagNode.SubNodes.Add(new Node(
                    dataNodeName,
                    (ulong)dataStart,
                    (ulong)actualDataSize));

                root.SubNodes.Add(tagNode);
                tagCount++;

                progressState.Report(tagEnd);

                if (declaredDataEnd > fileLength)
                {
                    // 当前 Tag 被截断，不可能再有下一个合法 Tag。
                    break;
                }

                if (!hasPreviousTagSize)
                    break;

                position = tagEnd;
            }

            if (position < fileLength
                && position + FLVUtil.TagHeaderSize > fileLength)
            {
                long remaining = fileLength - position;

                if (remaining > 0)
                {
                    root.SubNodes.Add(new Node(
                        "<unparsed data>",
                        (ulong)position,
                        (ulong)remaining));
                }
            }

            progressState.ReportFinal();
            return root;
        }

        // ============================================================
        // HEADER / TAG INFORMATION
        // ============================================================

        public bool TryGetTagHeader(Node node, out FLVTagHeader header)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(node);

            header = default;

            if (node.NodeName is not ("AudioTag" or "VideoTag" or "ScriptDataTag" or "UnknownTag"))
                return false;

            long tagStart = (long)node.Position;
            long nodeLength = (long)node.Length;
            long fileLength = FileStream.Length;

            if (tagStart < 0
                || nodeLength < FLVUtil.TagHeaderSize
                || tagStart + FLVUtil.TagHeaderSize > fileLength)
            {
                return false;
            }

            Span<byte> buffer = stackalloc byte[FLVUtil.TagHeaderSize];

            if (!TryReadExactlyAt(FileStream.SafeFileHandle, buffer, tagStart))
                return false;

            byte first = buffer[0];
            byte reserved = (byte)(first >> 6);
            bool filter = (first & 0x20) != 0;
            byte tagType = (byte)(first & 0x1F);

            uint dataSize = FLVUtil.ReadUInt24BE(buffer.Slice(1, 3));
            uint timestamp = FLVUtil.ReadUInt24BE(buffer.Slice(4, 3));
            byte timestampExtended = buffer[7];
            uint streamId = FLVUtil.ReadUInt24BE(buffer.Slice(8, 3));

            long dataStart = tagStart + FLVUtil.TagHeaderSize;
            long nodeEnd = Math.Min(tagStart + nodeLength, fileLength);
            long actualDataSize = Math.Min(
                dataSize,
                Math.Max(0, nodeEnd - dataStart));

            long previousTagSizeOffset = dataStart + dataSize;
            bool hasPreviousTagSize =
                previousTagSizeOffset + FLVUtil.PreviousTagSizeFieldSize <= nodeEnd;

            uint previousTagSize = 0;

            if (hasPreviousTagSize)
            {
                Span<byte> previous = stackalloc byte[4];

                if (TryReadExactlyAt(
                    FileStream.SafeFileHandle,
                    previous,
                    previousTagSizeOffset))
                {
                    previousTagSize = FLVUtil.ReadUInt32BE(previous);
                }
                else
                {
                    hasPreviousTagSize = false;
                }
            }

            header = new FLVTagHeader
            {
                TagStart = tagStart,
                Reserved = reserved,
                Filter = filter,
                TagType = tagType,
                DataSize = dataSize,
                Timestamp = timestamp,
                TimestampExtended = timestampExtended,
                StreamID = streamId,
                ActualDataSize = actualDataSize,
                HasPreviousTagSize = hasPreviousTagSize,
                PreviousTagSize = previousTagSize
            };

            return true;
        }

        public bool TryGetParentTagHeader(Node dataNode, out FLVTagHeader header)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(dataNode);

            header = default;

            if (dataNode.NodeName is not (
                "AudioTagData"
                or "VideoTagData"
                or "ScriptDataTagData"
                or "UnknownTagData"))
            {
                return false;
            }

            long tagStart = (long)dataNode.Position - FLVUtil.TagHeaderSize;

            if (tagStart < 0)
                return false;

            string expectedTagName = dataNode.NodeName switch
            {
                "AudioTagData" => "AudioTag",
                "VideoTagData" => "VideoTag",
                "ScriptDataTagData" => "ScriptDataTag",
                _ => "UnknownTag"
            };

            /*
             * TryGetTagHeader 只依赖 Node 的位置、长度和名称。
             * 临时 Node 的 Length 取 Header + Data + 可能的 4 字节 footer。
             */
            long temporaryLength =
                FLVUtil.TagHeaderSize
                + (long)dataNode.Length
                + FLVUtil.PreviousTagSizeFieldSize;

            var temporaryNode = new Node(
                expectedTagName,
                (ulong)tagStart,
                (ulong)temporaryLength);

            return TryGetTagHeader(temporaryNode, out header);
        }

        // ============================================================
        // DATA ACCESS
        // ============================================================

        public int ReadAt(long offset, Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (destination.IsEmpty)
                return 0;

            long fileLength = FileStream.Length;

            if (offset < 0 || offset >= fileLength)
                return 0;

            int maximum = (int)Math.Min(destination.Length, fileLength - offset);
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

            return new OffsetStream(FileStream, offset, length);
        }

        public FLVReader CreateReader(long offset, long length) =>
            new(CreateRawStream(offset, length));

        public ParseResult ParseNode(Node node)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return FLVDispatcher.Dispatch(this, node);
        }

        // ============================================================
        // POSITIONAL I/O
        // ============================================================

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
                int bufferSize)
            {
                _handle = handle;
                _fileLength = fileLength;
                _buffer = new byte[Math.Clamp(
                    bufferSize,
                    4 * 1024,
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

                if (destination.Length > _buffer.Length)
                {
                    return await ReadDirectAsync(
                        position,
                        destination,
                        cancellationToken);
                }

                bool cacheHit =
                    _bufferStart >= 0
                    && position >= _bufferStart
                    && position + destination.Length <= _bufferStart + _bufferLength;

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

                long cacheOffset = position - _bufferStart;
                int available = (int)Math.Min(
                    destination.Length,
                    _bufferLength - cacheOffset);

                if (available <= 0)
                    return 0;

                _buffer.AsMemory((int)cacheOffset, available)
                    .CopyTo(destination);

                return available;
            }

            private async ValueTask<int> ReadDirectAsync(
                long position,
                Memory<byte> destination,
                CancellationToken cancellationToken)
            {
                int maximum = (int)Math.Min(
                    destination.Length,
                    _fileLength - position);

                int total = 0;

                while (total < maximum)
                {
                    int read = await RandomAccess.ReadAsync(
                        _handle,
                        destination.Slice(total, maximum - total),
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
        // PROGRESS
        // ============================================================

        private sealed class ProgressState(
            IProgress<ParserProgress>? progress,
            long totalBytes)
        {
            private const int ReportIntervalMilliseconds = 200;

            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private long _lastReportMilliseconds = -ReportIntervalMilliseconds;
            private long _maximumPosition;

            public void Report(long position)
            {
                if (position > _maximumPosition)
                    _maximumPosition = position;

                if (progress is null)
                    return;

                long elapsed = _stopwatch.ElapsedMilliseconds;

                if (elapsed - _lastReportMilliseconds < ReportIntervalMilliseconds)
                    return;

                _lastReportMilliseconds = elapsed;
                Emit(elapsed);
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
                long position = Math.Clamp(
                    _maximumPosition,
                    0,
                    Math.Max(0, totalBytes));

                double elapsedSeconds = elapsedMilliseconds / 1000.0;

                progress!.Report(new ParserProgress
                {
                    Fraction = totalBytes > 0
                        ? Math.Clamp((double)position / totalBytes, 0.0, 1.0)
                        : 1.0,

                    BytesRead = (ulong)position,
                    TotalBytes = (ulong)Math.Max(0, totalBytes),

                    BytesPerSecond = elapsedSeconds > 0
                        ? position / elapsedSeconds
                        : 0.0
                });
            }
        }
    }
}