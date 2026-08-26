using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace UniversalParser.Src.Parser.FBX
{
    internal sealed class FBXParser : IParser
    {
        public string ContainerTypeName => "Kaydara FBX Binary";

        public FileStream FileStream { get; }

        public FBXParserOptions Options { get; }

        /// <summary>
        /// FBX 二进制文件版本，例如 6100、7400、7500、7700。
        /// </summary>
        public uint Version { get; private set; }

        /// <summary>
        /// FBX 7.5 及以上版本使用 64 位节点字段。
        /// </summary>
        public bool UsesExtendedNodeRecords =>
            FBXUtil.IsExtendedVersion(Version);

        private readonly bool _ownsStream;
        private bool _disposed;

        public FBXParser(
            FileStream fs,
            bool ownsStream = true,
            FBXParserOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(fs);

            if (!fs.CanRead)
            {
                throw new ArgumentException(
                    "FileStream must be readable.",
                    nameof(fs));
            }

            if (!fs.CanSeek)
            {
                throw new ArgumentException(
                    "FileStream must be seekable.",
                    nameof(fs));
            }

            FileStream = fs;
            _ownsStream = ownsStream;
            Options = options ?? new FBXParserOptions();
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
                Debug.WriteLine(
                    $"[FBXParser] Dispose failed: {ex}");
            }
        }

        // ============================================================
        // VALIDATION
        // ============================================================

        public static bool IsValid(FileStream fs)
        {
            if (fs is null ||
                !fs.CanRead ||
                !fs.CanSeek ||
                fs.Length < FBXUtil.HeaderLength)
            {
                return false;
            }

            try
            {
                Span<byte> header =
                    stackalloc byte[FBXUtil.HeaderLength];

                if (!TryReadExactlyAt(
                        fs.SafeFileHandle,
                        header,
                        0))
                {
                    return false;
                }

                if (!FBXUtil.IsBinarySignature(header))
                    return false;

                uint version =
                    FBXUtil.ReadUInt32LE(
                        header.Slice(
                            FBXUtil.VersionOffset,
                            4));

                // FBX 版本没有严格的连续范围，
                // 这里仅排除明显不合理的值。
                return version >= 1000 &&
                       version <= 100_000;
            }
            catch
            {
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
        // PARSE
        // ============================================================

        public async Task<Node> ParseAsync(
            IProgress<ParserProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long fileLength = FileStream.Length;

            if (fileLength < FBXUtil.HeaderLength)
            {
                throw new InvalidDataException(
                    "File is too small to be a binary FBX file.");
            }

            byte[] fileHeader =
                ArrayPool<byte>.Shared.Rent(
                    FBXUtil.HeaderLength);

            try
            {
                int headerRead = await ReadAtAsync(
                    0,
                    fileHeader.AsMemory(
                        0,
                        FBXUtil.HeaderLength),
                    cancellationToken);

                if (headerRead != FBXUtil.HeaderLength)
                {
                    throw new InvalidDataException(
                        "Unable to read the binary FBX header.");
                }

                if (!FBXUtil.IsBinarySignature(
                        fileHeader.AsSpan(
                            0,
                            FBXUtil.HeaderLength)))
                {
                    throw new InvalidDataException(
                        "The file is not a binary FBX file.");
                }

                Version =
                    FBXUtil.ReadUInt32LE(
                        fileHeader.AsSpan(
                            FBXUtil.VersionOffset,
                            4));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(fileHeader);
            }

            string fileName;

            try
            {
                fileName = Path.GetFileName(FileStream.Name);
            }
            catch
            {
                fileName = "(unknown)";
            }

            var root = new Node(
                fileName,
                0,
                (ulong)fileLength);

            var progressState = new ProgressState(
                progress,
                fileLength,
                Options.ProgressIntervalMilliseconds);

            using var reader = new FBXBufferedReader(
                FileStream.SafeFileHandle,
                fileLength,
                Options.ReadBufferSize);

            long nodeStart = FBXUtil.HeaderLength;

            await ParseNodeSequenceAsync(
                root,
                nodeStart,
                fileLength,
                depth: 0,
                reader,
                progressState,
                cancellationToken);

            progressState.ReportFinal();

            return root;
        }

        private async Task ParseNodeSequenceAsync(
            Node parent,
            long start,
            long end,
            int depth,
            FBXBufferedReader reader,
            ProgressState progressState,
            CancellationToken cancellationToken)
        {
            if (start < 0 || end <= start)
                return;

            if (depth > Options.MaxDepth)
            {
                AddSyntheticNode(
                    parent,
                    "<max depth reached>",
                    start,
                    end - start);

                return;
            }

            long position = start;
            int childCount = 0;

            int nullRecordLength =
                GetNullRecordLength();

            while (position <= end - nullRecordLength)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (childCount >= Options.MaxChildrenPerNode)
                {
                    AddSyntheticNode(
                        parent,
                        "<child limit reached>",
                        position,
                        end - position);

                    return;
                }

                var headerResult =
                    await TryReadNodeHeaderAsync(
                        position,
                        end,
                        reader,
                        cancellationToken);

                if (!headerResult.Success)
                {
                    AddSyntheticNode(
                        parent,
                        "<unparsed data>",
                        position,
                        end - position);

                    return;
                }

                FBXNodeHeader header =
                    headerResult.Header;

                // FBX 节点列表结束标记。
                // null-record 本身不是一个正常节点。
                if (header.EndOffset == 0 &&
                    header.PropertyCount == 0 &&
                    header.PropertyListLength == 0 &&
                    header.NameLength == 0)
                {
                    progressState.Report(
                        Math.Min(
                            end,
                            position + nullRecordLength));

                    return;
                }

                long nodeEnd = header.EndOffset;

                if (nodeEnd <= position)
                {
                    AddSyntheticNode(
                        parent,
                        "<invalid node boundary>",
                        position,
                        end - position);

                    return;
                }

                nodeEnd = Math.Min(nodeEnd, end);
                nodeEnd = Math.Min(nodeEnd, FileStream.Length);

                if (nodeEnd <= position)
                {
                    AddSyntheticNode(
                        parent,
                        "<invalid node length>",
                        position,
                        end - position);

                    return;
                }

                long nodeLength = nodeEnd - position;

                var node = new Node(
                    FBXUtil.Sanitize(header.Name),
                    (ulong)position,
                    (ulong)nodeLength);

                parent.SubNodes.Add(node);
                childCount++;

                if (header.IsTruncated)
                    node.NodeName += " [truncated]";

                if (Options.ParseChildren &&
                    depth < Options.MaxDepth &&
                    header.ChildrenOffset < nodeEnd)
                {
                    await ParseNodeSequenceAsync(
                        node,
                        header.ChildrenOffset,
                        nodeEnd,
                        depth + 1,
                        reader,
                        progressState,
                        cancellationToken);
                }
                else if (header.ChildrenOffset < nodeEnd &&
                         depth >= Options.MaxDepth)
                {
                    AddSyntheticNode(
                        node,
                        "<max depth reached>",
                        header.ChildrenOffset,
                        nodeEnd - header.ChildrenOffset);
                }

                progressState.Report(nodeEnd);

                // EndOffset 是下一个节点的绝对边界，
                // 不需要额外对齐。
                if (nodeEnd <= position)
                {
                    AddSyntheticNode(
                        parent,
                        "<parser stopped>",
                        position,
                        end - position);

                    return;
                }

                position = nodeEnd;
            }

            if (position < end)
            {
                AddSyntheticNode(
                    parent,
                    "<unparsed data>",
                    position,
                    end - position);
            }
        }

        private int GetNullRecordLength()
        {
            return FBXUtil.GetFixedNodeHeaderLength(Version);
        }

        // ============================================================
        // ASYNC NODE HEADER
        // ============================================================

        /// <summary>
        /// 异步读取节点头。
        ///
        /// 不能使用 out 参数，因此返回：
        /// (Success, Header)
        /// </summary>
        private async ValueTask<(
            bool Success,
            FBXNodeHeader Header)> TryReadNodeHeaderAsync(
                long position,
                long parentEnd,
                FBXBufferedReader reader,
                CancellationToken cancellationToken)
        {
            int fixedHeaderLength =
                FBXUtil.GetFixedNodeHeaderLength(Version);

            long fileLength = FileStream.Length;

            if (position < 0 ||
                parentEnd <= position ||
                position > fileLength - fixedHeaderLength ||
                position + fixedHeaderLength > parentEnd)
            {
                return (false, default);
            }

            byte[] fixedHeader =
                ArrayPool<byte>.Shared.Rent(
                    fixedHeaderLength);

            try
            {
                int read = await reader.ReadAtAsync(
                    position,
                    fixedHeader.AsMemory(
                        0,
                        fixedHeaderLength),
                    cancellationToken);

                if (read != fixedHeaderLength)
                    return (false, default);

                ulong endOffset;
                ulong propertyCount;
                ulong propertyListLength;
                byte nameLength;

                if (FBXUtil.IsExtendedVersion(Version))
                {
                    endOffset =
                        BinaryPrimitives.ReadUInt64LittleEndian(
                            fixedHeader.AsSpan(0, 8));

                    propertyCount =
                        BinaryPrimitives.ReadUInt64LittleEndian(
                            fixedHeader.AsSpan(8, 8));

                    propertyListLength =
                        BinaryPrimitives.ReadUInt64LittleEndian(
                            fixedHeader.AsSpan(16, 8));

                    nameLength =
                        fixedHeader[24];
                }
                else
                {
                    endOffset =
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            fixedHeader.AsSpan(0, 4));

                    propertyCount =
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            fixedHeader.AsSpan(4, 4));

                    propertyListLength =
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            fixedHeader.AsSpan(8, 4));

                    nameLength =
                        fixedHeader[12];
                }

                // FBX null-record。
                if (FBXUtil.IsZeroRecord(
                        endOffset,
                        propertyCount,
                        propertyListLength,
                        nameLength))
                {
                    return
                    (
                        true,
                        new FBXNodeHeader
                        {
                            NodeStart = position,
                            EndOffset = 0,
                            PropertyCount = 0,
                            PropertyListLength = 0,
                            NameLength = 0,
                            Name = string.Empty,
                            FixedHeaderLength =
                                fixedHeaderLength,
                            ActualPropertyLength = 0,
                            ChildrenOffset =
                                position + fixedHeaderLength,
                            IsTruncated = false,
                        }
                    );
                }

                if (nameLength > Options.MaxNodeNameLength)
                    return (false, default);

                long nameOffset =
                    position + fixedHeaderLength;

                long propertyOffset =
                    nameOffset + nameLength;

                if (nameOffset < position ||
                    propertyOffset < nameOffset ||
                    propertyOffset > parentEnd)
                {
                    return (false, default);
                }

                string name = string.Empty;

                if (nameLength > 0)
                {
                    byte[] nameBuffer =
                        ArrayPool<byte>.Shared.Rent(
                            nameLength);

                    try
                    {
                        int nameRead =
                            await reader.ReadAtAsync(
                                nameOffset,
                                nameBuffer.AsMemory(
                                    0,
                                    nameLength),
                                cancellationToken);

                        if (nameRead != nameLength)
                            return (false, default);

                        name = FBXUtil.DecodeNodeName(
                            nameBuffer.AsSpan(
                                0,
                                nameLength));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(
                            nameBuffer);
                    }
                }

                long declaredEnd =
                    FBXUtil.ClampToLong(endOffset);

                bool invalidEnd =
                    declaredEnd <= position ||
                    declaredEnd > fileLength;

                long actualEnd =
                    declaredEnd > position
                        ? declaredEnd
                        : parentEnd;

                actualEnd = Math.Min(
                    actualEnd,
                    parentEnd);

                actualEnd = Math.Min(
                    actualEnd,
                    fileLength);

                long propertyLength =
                    FBXUtil.ClampToLong(
                        propertyListLength);

                long availablePropertyLength =
                    actualEnd > propertyOffset
                        ? actualEnd - propertyOffset
                        : 0;

                long actualPropertyLength =
                    Math.Min(
                        propertyLength,
                        availablePropertyLength);

                long childrenOffset =
                    propertyOffset + actualPropertyLength;

                bool propertyTruncated =
                    actualPropertyLength < propertyLength;

                return
                (
                    true,
                    new FBXNodeHeader
                    {
                        NodeStart = position,
                        EndOffset = actualEnd,
                        PropertyCount = propertyCount,
                        PropertyListLength =
                            propertyListLength,
                        NameLength = nameLength,
                        Name = name,
                        FixedHeaderLength =
                            fixedHeaderLength,
                        ActualPropertyLength =
                            actualPropertyLength,
                        ChildrenOffset = childrenOffset,
                        IsTruncated =
                            propertyTruncated ||
                            invalidEnd ||
                            actualEnd < declaredEnd,
                    }
                );
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(
                    fixedHeader);
            }
        }

        // ============================================================
        // SYNC NODE HEADER
        // ============================================================

        /// <summary>
        /// 在 GUI 点选节点、Dispatcher 分发时使用的同步读取版本。
        ///
        /// 这里不能使用 ParseAsync 内部的 FBXBufferedReader，
        /// 因为该 reader 只属于一次 ParseAsync 调用。
        /// 使用 RandomAccess 读取不会改变 FileStream.Position。
        /// </summary>
        public bool TryGetNodeHeader(
            Node node,
            out FBXNodeHeader header)
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            ArgumentNullException.ThrowIfNull(node);

            header = default;

            long fileLength =
                FileStream.Length;

            long position =
                (long)node.Position;

            long nodeLength =
                (long)node.Length;

            if (position < FBXUtil.HeaderLength ||
                nodeLength <= 0 ||
                position >= fileLength)
            {
                return false;
            }

            long end;

            // 防止 position + nodeLength 溢出。
            if (nodeLength > fileLength - position)
                end = fileLength;
            else
                end = position + nodeLength;

            if (end <= position)
                return false;

            return TryReadNodeHeaderSync(
                position,
                end,
                out header);
        }

        private bool TryReadNodeHeaderSync(
            long position,
            long parentEnd,
            out FBXNodeHeader header)
        {
            header = default;

            int fixedHeaderLength =
                FBXUtil.GetFixedNodeHeaderLength(Version);

            long fileLength =
                FileStream.Length;

            if (position < 0 ||
                parentEnd <= position ||
                position > fileLength - fixedHeaderLength ||
                position + fixedHeaderLength > parentEnd)
            {
                return false;
            }

            Span<byte> fixedHeader =
                stackalloc byte[
                    FBXUtil.ExtendedNodeHeaderLength];

            if (!TryReadExactlyAt(
                    FileStream.SafeFileHandle,
                    fixedHeader[..fixedHeaderLength],
                    position))
            {
                return false;
            }

            ulong endOffset;
            ulong propertyCount;
            ulong propertyListLength;
            byte nameLength;

            if (FBXUtil.IsExtendedVersion(Version))
            {
                endOffset =
                    BinaryPrimitives.ReadUInt64LittleEndian(
                        fixedHeader[..8]);

                propertyCount =
                    BinaryPrimitives.ReadUInt64LittleEndian(
                        fixedHeader.Slice(8, 8));

                propertyListLength =
                    BinaryPrimitives.ReadUInt64LittleEndian(
                        fixedHeader.Slice(16, 8));

                nameLength =
                    fixedHeader[24];
            }
            else
            {
                endOffset =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        fixedHeader[..4]);

                propertyCount =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        fixedHeader.Slice(4, 4));

                propertyListLength =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        fixedHeader.Slice(8, 4));

                nameLength =
                    fixedHeader[12];
            }

            // FBX null-record。
            if (FBXUtil.IsZeroRecord(
                    endOffset,
                    propertyCount,
                    propertyListLength,
                    nameLength))
            {
                header = new FBXNodeHeader
                {
                    NodeStart = position,
                    EndOffset = 0,
                    PropertyCount = 0,
                    PropertyListLength = 0,
                    NameLength = 0,
                    Name = string.Empty,
                    FixedHeaderLength =
                        fixedHeaderLength,
                    ActualPropertyLength = 0,
                    ChildrenOffset =
                        position + fixedHeaderLength,
                    IsTruncated = false,
                };

                return true;
            }

            if (nameLength > Options.MaxNodeNameLength)
                return false;

            long nameOffset =
                position + fixedHeaderLength;

            long propertyOffset =
                nameOffset + nameLength;

            if (nameOffset < position ||
                propertyOffset < nameOffset ||
                propertyOffset > parentEnd)
            {
                return false;
            }

            string name = string.Empty;

            if (nameLength > 0)
            {
                byte[] nameBuffer =
                    ArrayPool<byte>.Shared.Rent(
                        nameLength);

                try
                {
                    Span<byte> nameSpan =
                        nameBuffer.AsSpan(
                            0,
                            nameLength);

                    if (!TryReadExactlyAt(
                            FileStream.SafeFileHandle,
                            nameSpan,
                            nameOffset))
                    {
                        return false;
                    }

                    name = FBXUtil.DecodeNodeName(
                        nameSpan);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(
                        nameBuffer);
                }
            }

            long declaredEnd =
                FBXUtil.ClampToLong(
                    endOffset);

            bool invalidEnd =
                declaredEnd <= position ||
                declaredEnd > fileLength;

            long actualEnd =
                declaredEnd > position
                    ? declaredEnd
                    : parentEnd;

            actualEnd = Math.Min(
                actualEnd,
                parentEnd);

            actualEnd = Math.Min(
                actualEnd,
                fileLength);

            if (actualEnd < propertyOffset)
                actualEnd = propertyOffset;

            long propertyLength =
                FBXUtil.ClampToLong(
                    propertyListLength);

            long availablePropertyLength =
                actualEnd > propertyOffset
                    ? actualEnd - propertyOffset
                    : 0;

            long actualPropertyLength =
                Math.Min(
                    propertyLength,
                    availablePropertyLength);

            long childrenOffset =
                propertyOffset + actualPropertyLength;

            bool propertyTruncated =
                actualPropertyLength < propertyLength;

            header = new FBXNodeHeader
            {
                NodeStart = position,
                EndOffset = actualEnd,
                PropertyCount = propertyCount,
                PropertyListLength =
                    propertyListLength,
                NameLength = nameLength,
                Name = name,
                FixedHeaderLength =
                    fixedHeaderLength,
                ActualPropertyLength =
                    actualPropertyLength,
                ChildrenOffset = childrenOffset,
                IsTruncated =
                    propertyTruncated ||
                    invalidEnd ||
                    actualEnd < declaredEnd,
            };

            return true;
        }

        // ============================================================
        // POSITIONAL READ
        // ============================================================

        /// <summary>
        /// 同步定位读取。
        /// 用于 GUI 点选节点时读取具体数据。
        /// </summary>
        public int ReadAt(
            long offset,
            Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            if (destination.IsEmpty)
                return 0;

            long fileLength =
                FileStream.Length;

            if (offset < 0 ||
                offset >= fileLength)
            {
                return 0;
            }

            int max = (int)Math.Min(
                destination.Length,
                fileLength - offset);

            int total = 0;

            while (total < max)
            {
                int read = RandomAccess.Read(
                    FileStream.SafeFileHandle,
                    destination.Slice(
                        total,
                        max - total),
                    offset + total);

                if (read <= 0)
                    break;

                total += read;
            }

            return total;
        }

        private async ValueTask<int> ReadAtAsync(
            long offset,
            Memory<byte> destination,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            if (destination.IsEmpty)
                return 0;

            long fileLength =
                FileStream.Length;

            if (offset < 0 ||
                offset >= fileLength)
            {
                return 0;
            }

            int max = (int)Math.Min(
                destination.Length,
                fileLength - offset);

            int total = 0;

            while (total < max)
            {
                int read = await RandomAccess.ReadAsync(
                    FileStream.SafeFileHandle,
                    destination.Slice(
                        total,
                        max - total),
                    offset + total,
                    cancellationToken);

                if (read <= 0)
                    break;

                total += read;
            }

            return total;
        }

        // ============================================================
        // RAW DATA / DISPATCH
        // ============================================================

        public Stream CreateRawStream(
            long offset,
            long length)
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            long fileLength =
                FileStream.Length;

            offset = Math.Clamp(
                offset,
                0,
                fileLength);

            length = Math.Clamp(
                length,
                0,
                fileLength - offset);

            return new OffsetStream(
                FileStream,
                offset,
                length);
        }

        public ParseResult ParseNode(Node node)
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            return FBXDispatcher.Dispatch(
                this,
                node);
        }

        // ============================================================
        // SYNTHETIC NODE
        // ============================================================

        private static void AddSyntheticNode(
            Node parent,
            string name,
            long offset,
            long length)
        {
            if (length <= 0)
                return;

            parent.SubNodes.Add(
                new Node(
                    name,
                    (ulong)offset,
                    (ulong)length));
        }

        // ============================================================
        // PROGRESS
        // ============================================================

        private sealed class ProgressState
        {
            private readonly IProgress<ParserProgress>? _progress;
            private readonly long _totalBytes;
            private readonly int _intervalMilliseconds;
            private readonly Stopwatch _stopwatch;

            private long _maximumPosition;
            private long _lastReportMilliseconds =
                long.MinValue;

            public ProgressState(
                IProgress<ParserProgress>? progress,
                long totalBytes,
                int intervalMilliseconds)
            {
                _progress = progress;
                _totalBytes = totalBytes;

                _intervalMilliseconds =
                    Math.Max(
                        50,
                        intervalMilliseconds);

                _stopwatch =
                    Stopwatch.StartNew();
            }

            public void Report(long position)
            {
                if (position > _maximumPosition)
                    _maximumPosition = position;

                if (_progress is null)
                    return;

                long elapsed =
                    _stopwatch.ElapsedMilliseconds;

                if (elapsed - _lastReportMilliseconds <
                    _intervalMilliseconds)
                {
                    return;
                }

                _lastReportMilliseconds =
                    elapsed;

                Emit();
            }

            public void ReportFinal()
            {
                _maximumPosition =
                    _totalBytes;

                if (_progress is null)
                    return;

                _lastReportMilliseconds =
                    _stopwatch.ElapsedMilliseconds;

                Emit();
            }

            private void Emit()
            {
                long position = Math.Clamp(
                    _maximumPosition,
                    0,
                    Math.Max(0, _totalBytes));

                double seconds =
                    _stopwatch.Elapsed.TotalSeconds;

                _progress!.Report(
                    new ParserProgress
                    {
                        Fraction =
                            _totalBytes > 0
                                ? Math.Clamp(
                                    (double)position /
                                    _totalBytes,
                                    0.0,
                                    1.0)
                                : 1.0,

                        BytesRead =
                            (ulong)position,

                        TotalBytes =
                            (ulong)Math.Max(
                                0,
                                _totalBytes),

                        BytesPerSecond =
                            seconds > 0
                                ? position / seconds
                                : 0.0,
                    });
            }
        }
    }
}