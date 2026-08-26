using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

//TODO: ASF 解析器框架已完成；具体对象解析（FileProperties/StreamProperties/ContentDescription/
//      ExtendedContentDescription/CodecList/Marker/ScriptCommand/HeaderExtension 子对象等）以及
//      Data Object 的 packet 级解析（含 payload parsing 位域）尚未实现。

namespace UniversalParser.Src.Parser.ASF
{
    internal sealed class ASFParser : IParser
    {
        public string ContainerTypeName => "Advanced Systems Format";
        public FileStream FileStream { get; }

        public ASFParserOptions Options { get; }

        private readonly bool _ownsStream;
        private bool _disposed;

        /// <param name="ownsStream">false 时 Dispose 不关闭外部传入的流。</param>
        public ASFParser(FileStream fs, bool ownsStream = true, ASFParserOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(fs);
            if (!fs.CanRead) throw new ArgumentException("FileStream must be readable.", nameof(fs));
            if (!fs.CanSeek) throw new ArgumentException("FileStream must be seekable.", nameof(fs));

            FileStream = fs;
            _ownsStream = ownsStream;
            Options = options ?? new ASFParserOptions();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_ownsStream) return;
            try { FileStream.Dispose(); }
            catch (Exception ex) { Debug.WriteLine($"[ASFParser] Dispose failed: {ex}"); }
        }

        // ============================================================
        // VALIDATION
        // ============================================================

        /// <summary>首个对象必须是 Header Object，且 Object Size 至少 24 字节（MS-ASF 2.2.1）。</summary>
        public static bool IsValid(FileStream fs)
        {
            if (fs is null || !fs.CanRead || !fs.CanSeek) return false;

            try
            {
                if (fs.Length < ASFUtil.ObjectHeaderSize) return false;

                Span<byte> buffer = stackalloc byte[ASFUtil.ObjectHeaderSize];
                if (!TryReadExactlyAt(fs.SafeFileHandle, buffer, 0)) return false;

                var guid = new Guid(buffer[..16]);
                if (guid != ASFUtil.HeaderObject) return false;

                ulong size = BinaryPrimitives.ReadUInt64LittleEndian(buffer[16..]);
                return size >= ASFUtil.ObjectHeaderSize;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ASFParser] IsValid failed: {ex}");
                return false;
            }
        }

        private static bool TryReadExactlyAt(SafeFileHandle handle, Span<byte> destination, long offset)
        {
            int total = 0;
            while (total < destination.Length)
            {
                int read = RandomAccess.Read(handle, destination[total..], offset + total);
                if (read <= 0) return false;
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
            if (fileLength < ASFUtil.ObjectHeaderSize)
                throw new InvalidDataException("File is too small to be an ASF container.");

            var reader = new PositionalReader(FileStream.SafeFileHandle, fileLength, Options.ReadBufferSize);

            byte[] first = new byte[ASFUtil.ObjectHeaderSize];
            if (await reader.ReadAtAsync(0, first.AsMemory(0, first.Length), cancellationToken) != first.Length)
                throw new InvalidDataException("Unable to read the ASF Header Object.");

            var firstGuid = new Guid(first.AsSpan(0, 16));
            if (firstGuid != ASFUtil.HeaderObject)
                throw new InvalidDataException(
                    $"First object is not the ASF Header Object ({ASFUtil.GuidDisplay(firstGuid)}).");

            ulong firstSize = BinaryPrimitives.ReadUInt64LittleEndian(first.AsSpan(16, 8));
            if (firstSize < ASFUtil.ObjectHeaderSize)
                throw new InvalidDataException("ASF Header Object size is invalid.");

            var root = new Node(Path.GetFileName(FileStream.Name), 0, (ulong)fileLength);

            var progressState = new ProgressState(progress, fileLength);

            // 顶层对象序列覆盖整个文件：Header Object → Data Object → Index Object(s) → 可能的尾部数据
            await ParseObjectSequenceAsync(
                root, 0, fileLength, depth: 1, reader, progressState, cancellationToken);

            progressState.ReportFinal();
            return root;
        }

        /// <summary>
        /// 解析 [start, end) 区间内的对象序列。顶层与嵌套容器走同一套逻辑。
        /// </summary>
        private async Task ParseObjectSequenceAsync(
            Node parent,
            long start,
            long end,
            int depth,
            PositionalReader reader,
            ProgressState progressState,
            CancellationToken ct)
        {
            long pos = start;
            int childCount = 0;
            bool stopped = false;
            byte[] buffer = new byte[ASFUtil.ObjectHeaderSize];
            byte[] structure = new byte[8];

            while (pos + ASFUtil.ObjectHeaderSize <= end)
            {
                ct.ThrowIfCancellationRequested();

                if (await reader.ReadAtAsync(pos, buffer.AsMemory(0, ASFUtil.ObjectHeaderSize), ct)
                    != ASFUtil.ObjectHeaderSize)
                {
                    AddSynthetic(parent, "<unreadable data>", pos, end - pos);
                    stopped = true;
                    break;
                }

                var guid = new Guid(buffer.AsSpan(0, 16));
                ulong sizeRaw = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(16, 8));

                // 全零 GUID 或大小不足 24 字节 → 已脱离对象边界，停止硬解
                if (guid == Guid.Empty || sizeRaw < ASFUtil.ObjectHeaderSize)
                {
                    AddSynthetic(parent, "<unrecognized data>", pos, end - pos);
                    stopped = true;
                    break;
                }

                if (childCount >= Options.MaxChildrenPerContainer)
                {
                    AddSynthetic(parent,
                        $"<listing stopped, {ASFUtil.FormatBytes(end - pos)} not expanded>", pos, end - pos);
                    stopped = true;
                    break;
                }

                long payloadStart = pos + ASFUtil.ObjectHeaderSize;

                bool truncated;
                long payloadEnd;
                if (sizeRaw > (ulong)(end - pos))
                {
                    payloadEnd = end;      // 越界则裁剪到父边界
                    truncated = true;
                }
                else
                {
                    payloadEnd = pos + (long)sizeRaw;
                    truncated = false;
                }

                string? name = ASFUtil.TryGetObjectName(guid);
                string nodeName = name is null
                    ? $"UnknownObject ({ASFUtil.GuidShort(guid)})"
                    : $"{name} ({ASFUtil.GuidShort(guid)})";

                var node = new Node(nodeName, (ulong)pos, (ulong)(payloadEnd - pos));
                parent.SubNodes.Add(node);
                childCount++;

                if (ASFUtil.IsContainer(guid) && depth < Options.MaxDepth)
                {
                    long innerStart = payloadStart + ASFUtil.ContainerStructureSize(guid);
                    long innerEnd = payloadEnd;

                    if (guid == ASFUtil.HeaderExtensionObject && payloadEnd - payloadStart >= 8)
                    {
                        // ExtensionDataSize 字段声明扩展数据区大小，用它限定子对象范围
                        if (await reader.ReadAtAsync(payloadStart, structure.AsMemory(0, 8), ct) == 8)
                        {
                            uint extensionDataSize =
                                BinaryPrimitives.ReadUInt32LittleEndian(structure.AsSpan(4, 4));
                            long declaredInnerEnd = innerStart + extensionDataSize;
                            if (declaredInnerEnd >= innerStart && declaredInnerEnd < innerEnd)
                                innerEnd = declaredInnerEnd;
                        }
                    }

                    await ParseObjectSequenceAsync(
                        node, innerStart, innerEnd, depth + 1, reader, progressState, ct);
                }
                else if (ASFUtil.IsContainer(guid))
                {
                    AddSynthetic(node, "<max depth reached>", payloadStart, payloadEnd - payloadStart);
                }

                if (truncated) node.NodeName += " [truncated]";

                progressState.Report(payloadEnd);

                // 兜底：必须严格前进，任何情况下都不会死循环
                if (payloadEnd <= pos)
                {
                    Debug.Fail($"ASF object at 0x{pos:X} did not advance.");
                    break;
                }
                pos = payloadEnd;
            }

            long leftover = end - pos;
            if (!stopped && leftover > 0)
                AddSynthetic(parent, $"<unparsed {leftover} byte(s)>", pos, leftover);
        }

        private static void AddSynthetic(Node parent, string name, long offset, long length)
        {
            if (length <= 0) return;
            parent.SubNodes.Add(new Node(name, (ulong)offset, (ulong)length));
        }

        // ============================================================
        // 供 Object 解析器使用的 API
        // ============================================================

        /// <summary>
        /// 重新读取节点对应的对象头。合成节点（&lt;...&gt; 命名）一律返回 false。
        /// </summary>
        public bool TryGetObjectHeader(Node node, out ASFObjectHeader header)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(node);

            header = default;

            if (node.NodeName.StartsWith('<'))
                return false; // 合成节点：尾部数据 / 损坏区域 / 截断占位

            long objectStart = (long)node.Position;
            long nodeLength = (long)node.Length;
            long fileLength = FileStream.Length;

            if (objectStart < 0 || nodeLength < ASFUtil.ObjectHeaderSize) return false;
            if (objectStart + ASFUtil.ObjectHeaderSize > fileLength) return false;

            long nodeEnd = Math.Min(objectStart + nodeLength, fileLength);

            Span<byte> buffer = stackalloc byte[ASFUtil.ObjectHeaderSize];
            if (!TryReadExactlyAt(FileStream.SafeFileHandle, buffer, objectStart)) return false;

            var guid = new Guid(buffer[..16]);
            ulong sizeRaw = BinaryPrimitives.ReadUInt64LittleEndian(buffer[16..]);
            if (guid == Guid.Empty || sizeRaw < ASFUtil.ObjectHeaderSize) return false;

            long payloadStart = objectStart + ASFUtil.ObjectHeaderSize;
            long available = Math.Max(0, nodeEnd - payloadStart);

            long declaredPayload;
            if (sizeRaw > (ulong)(nodeEnd - objectStart))
                declaredPayload = available; // 截断
            else
                declaredPayload = (long)sizeRaw - ASFUtil.ObjectHeaderSize;

            header = new ASFObjectHeader
            {
                Guid = guid,
                Name = ASFUtil.TryGetObjectName(guid),
                ObjectStart = objectStart,
                PayloadStart = payloadStart,
                DeclaredSize = (long)Math.Min(sizeRaw, (ulong)long.MaxValue),
                PayloadLength = Math.Min(declaredPayload, available),
            };
            return true;
        }

        /// <summary>定位读，不影响 FileStream.Position，可安全在解析进行中调用。</summary>
        public int ReadAt(long offset, Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (destination.IsEmpty) return 0;

            long fileLength = FileStream.Length;
            if (offset < 0 || offset >= fileLength) return 0;

            int max = (int)Math.Min(destination.Length, fileLength - offset);
            int total = 0;
            while (total < max)
            {
                int read = RandomAccess.Read(
                    FileStream.SafeFileHandle, destination.Slice(total, max - total), offset + total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        /// <summary>创建裁剪过范围的原始数据视图。</summary>
        public Stream CreateRawStream(long offset, long length)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long fileLength = FileStream.Length;
            offset = Math.Clamp(offset, 0, fileLength);
            length = Math.Clamp(length, 0, fileLength - offset);
            return new OffsetStream(FileStream, offset, length);
        }

        /// <summary>基于对象负载的读取器（ASF 一律小端，GUID 混合端序由 ReadGuid 处理）。</summary>
        public ASFReader CreatePayloadReader(in ASFObjectHeader header) =>
            new(CreateRawStream(header.PayloadStart, header.PayloadLength));

        public ParseResult ParseNode(Node node)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ASFDispatcher.Dispatch(this, node);
        }

        // ============================================================
        // 内部：带缓冲的定位读取器
        // ============================================================

        /// <summary>
        /// ASF 解析的特点也是海量小读（对象头 24 字节）。逐个 Seek+Read 会被系统调用拖死，
        /// 这里加 64KB 顺序缓冲，同时用 RandomAccess 保证不动 FileStream.Position。
        /// 注意：非线程安全，仅在单次 ParseAsync 调用链内部使用。
        /// </summary>
        private sealed class PositionalReader
        {
            private readonly SafeFileHandle _handle;
            private readonly long _fileLength;
            private readonly byte[] _buffer;
            private long _bufferStart = -1;
            private int _bufferLength;

            public PositionalReader(SafeFileHandle handle, long fileLength, int capacity)
            {
                _handle = handle;
                _fileLength = fileLength;
                _buffer = new byte[Math.Clamp(capacity, 4096, 4 * 1024 * 1024)];
            }

            public async ValueTask<int> ReadAtAsync(long position, Memory<byte> destination, CancellationToken ct)
            {
                if (destination.IsEmpty) return 0;
                if (position < 0 || position >= _fileLength) return 0;

                int count = destination.Length;
                if (count > _buffer.Length)
                    return await ReadDirectAsync(position, destination, ct);

                bool hit = _bufferStart >= 0
                           && position >= _bufferStart
                           && position + count <= _bufferStart + _bufferLength;

                if (!hit)
                {
                    _bufferStart = position;
                    _bufferLength = 0;

                    int want = (int)Math.Min(_buffer.Length, _fileLength - position);
                    while (_bufferLength < want)
                    {
                        int read = await RandomAccess.ReadAsync(
                            _handle, _buffer.AsMemory(_bufferLength, want - _bufferLength),
                            position + _bufferLength, ct);
                        if (read <= 0) break;
                        _bufferLength += read;
                    }
                }

                int available = (int)Math.Min(count, _bufferStart + _bufferLength - position);
                if (available <= 0) return 0;

                _buffer.AsMemory((int)(position - _bufferStart), available).CopyTo(destination);
                return available;
            }

            private async ValueTask<int> ReadDirectAsync(long position, Memory<byte> destination, CancellationToken ct)
            {
                int total = 0;
                int max = (int)Math.Min(destination.Length, _fileLength - position);
                while (total < max)
                {
                    int read = await RandomAccess.ReadAsync(
                        _handle, destination.Slice(total, max - total), position + total, ct);
                    if (read <= 0) break;
                    total += read;
                }
                return total;
            }
        }

        // ============================================================
        // 内部：进度上报
        // ============================================================

        private sealed class ProgressState(IProgress<ParserProgress>? progress, long totalBytes)
        {
            private const int ThrottleMs = 200;
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private long _lastReportMs = -ThrottleMs;
            private long _maxPosition;

            public void Report(long position)
            {
                if (position > _maxPosition) _maxPosition = position;
                if (progress is null) return;

                long ms = _stopwatch.ElapsedMilliseconds;
                if (ms - _lastReportMs < ThrottleMs) return;

                _lastReportMs = ms;
                Emit(ms);
            }

            public void ReportFinal()
            {
                if (progress is null) return;
                _maxPosition = totalBytes;
                Emit(_stopwatch.ElapsedMilliseconds);
            }

            private void Emit(long elapsedMs)
            {
                double seconds = elapsedMs / 1000.0;
                long position = Math.Clamp(_maxPosition, 0, Math.Max(0, totalBytes));

                progress!.Report(new ParserProgress
                {
                    Fraction = totalBytes > 0 ? Math.Clamp((double)position / totalBytes, 0.0, 1.0) : 1.0,
                    BytesRead = (ulong)position,
                    TotalBytes = (ulong)Math.Max(0, totalBytes),
                    BytesPerSecond = seconds > 0 ? position / seconds : 0.0,
                });
            }
        }
    }
}