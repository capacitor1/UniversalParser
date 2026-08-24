using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace UniversalParser.Src.Parser.RIFF
{
    internal sealed class RIFFParser : IParser
    {
        public string ContainerTypeName => "RIFF Container";
        public FileStream FileStream { get; }

        /// <summary>RIFX 为大端，其余为小端（A2）。</summary>
        public bool IsBigEndian { get; private set; }

        /// <summary>根签名：RIFF / RIFX / RF64 / BW64。</summary>
        public string RootId { get; private set; } = string.Empty;

        /// <summary>根 form 类型：WAVE / AVI  / WEBP / ACON ...</summary>
        public string FormType { get; private set; } = string.Empty;

        public RIFFParserOptions Options { get; }

        private readonly bool _ownsStream;
        private bool _disposed;

        /// <param name="ownsStream">false 时 Dispose 不关闭外部传入的流（B7）。</param>
        public RIFFParser(FileStream fs, bool ownsStream = true, RIFFParserOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(fs);
            if (!fs.CanRead) throw new ArgumentException("FileStream must be readable.", nameof(fs));
            if (!fs.CanSeek) throw new ArgumentException("FileStream must be seekable.", nameof(fs));

            FileStream = fs;
            _ownsStream = ownsStream;
            Options = options ?? new RIFFParserOptions();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_ownsStream) return;
            try { FileStream.Dispose(); }
            catch (Exception ex) { Debug.WriteLine($"[RIFFParser] Dispose failed: {ex}"); }
        }

        // ============================================================
        // VALIDATION
        // ============================================================

        /// <summary>
        /// 宽松校验：只要签名与 form 合法就接受。
        /// 现实中大量文件（流式录制、被截断的下载）ckSize 是错的，
        /// 用 size 严格判定会误杀；size 异常在解析阶段以 [truncated] 呈现。
        /// 全程使用 RandomAccess，不会改动 fs.Position（B8）。
        /// </summary>
        public static bool IsValid(FileStream fs)
        {
            if (fs is null || !fs.CanRead || !fs.CanSeek) return false;

            try
            {
                if (fs.Length < RIFFUtil.TypedContainerHeaderSize) return false;

                Span<byte> header = stackalloc byte[RIFFUtil.TypedContainerHeaderSize];
                if (!TryReadExactlyAt(fs.SafeFileHandle, header, 0)) return false;   // A8：短读不再当非法

                string signature = RIFFUtil.DecodeFourCC(header[..4]);
                if (!RIFFUtil.RootSignatures.Contains(signature)) return false;      // A9：认 RF64/BW64

                // form 必须是可打印 4CC，避免把随机数据认成 RIFF
                return RIFFUtil.IsPrintableFourCC(header.Slice(8, 4));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RIFFParser] IsValid failed: {ex}");
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
            if (fileLength < RIFFUtil.TypedContainerHeaderSize)
                throw new InvalidDataException("File is too small to be a RIFF container.");

            var reader = new PositionalReader(FileStream.SafeFileHandle, fileLength, Options.ReadBufferSize);

            byte[] header = new byte[RIFFUtil.TypedContainerHeaderSize];
            if (await reader.ReadAtAsync(0, header.AsMemory(0, header.Length), cancellationToken) != header.Length)
                throw new InvalidDataException("Unable to read the 12-byte RIFF header.");

            string? rootId = RIFFUtil.TryDecodeFourCC(header, 0);
            if (rootId is null || !RIFFUtil.RootSignatures.Contains(rootId))
                throw new InvalidDataException($"Unsupported signature '{RIFFUtil.Sanitize(rootId ?? "????")}'.");

            RootId = rootId;
            IsBigEndian = rootId == "RIFX";                                  // A2
            uint declaredSize = RIFFUtil.ReadUInt32(header, 4, IsBigEndian);
            FormType = RIFFUtil.TryDecodeFourCC(header, 8) ?? "????";

            // A1 + A8：用 ckSize 限定顶层扫描范围，并对 RF64 占位值 / 写坏的 size 做兜底
            long rootEnd;
            if (declaredSize == uint.MaxValue || declaredSize < RIFFUtil.FourCCSize)
                rootEnd = fileLength;                 // RF64/BW64 占位值，或流式写入未回填
            else
                rootEnd = Math.Min(RIFFUtil.ChunkHeaderSize + (long)declaredSize, fileLength);

            if (rootEnd < RIFFUtil.TypedContainerHeaderSize)
                rootEnd = fileLength;

            string prefix = rootId == "RIFF" ? string.Empty : $"[{RIFFUtil.Sanitize(rootId)}] ";
            var root = new Node(
                $"{prefix}{RIFFUtil.Sanitize(FormType)}: '{Path.GetFileName(FileStream.Name)}'",
                0,
                (ulong)fileLength);

            var progressState = new ProgressState(progress, fileLength);

            await ParseChunkSequenceAsync(
                root,
                RIFFUtil.TypedContainerHeaderSize,
                rootEnd,
                depth: 1,
                reader,
                progressState,
                cancellationToken);

            // A1：RIFF 块之后的尾部数据单独呈现，而不是继续当 chunk 硬解
            if (rootEnd < fileLength)
            {
                long extra = fileLength - rootEnd;
                root.SubNodes.Add(new Node(
                    $"<trailing data> ({RIFFUtil.FormatBytes(extra)})", (ulong)rootEnd, (ulong)extra));
            }

            progressState.ReportFinal();   // B6：保证一定会有 100%
            return root;
        }

        /// <summary>
        /// 解析 [start, end) 区间内的块序列。容器与顶层走完全同一套逻辑（修掉 A3 的根源：
        /// 顶层与嵌套是两份不同代码）。
        /// </summary>
        private async Task ParseChunkSequenceAsync(
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
            byte[] buffer = new byte[RIFFUtil.TypedContainerHeaderSize];

            while (pos + RIFFUtil.ChunkHeaderSize <= end)
            {
                ct.ThrowIfCancellationRequested();

                if (await reader.ReadAtAsync(pos, buffer.AsMemory(0, RIFFUtil.ChunkHeaderSize), ct)
                    != RIFFUtil.ChunkHeaderSize)
                {
                    AddSynthetic(parent, "<unreadable data>", pos, end - pos);
                    stopped = true;
                    break;
                }

                // B2：4CC 非法说明已经脱轨，继续硬解只会产生成千上万个乱码节点
                string? id = RIFFUtil.TryDecodeFourCC(buffer, 0);
                if (id is null)
                {
                    AddSynthetic(parent, "<unrecognized data>", pos, end - pos);
                    stopped = true;
                    break;
                }

                // B3：节点数量上限
                if (childCount >= Options.MaxChildrenPerContainer)
                {
                    AddSynthetic(parent,
                        $"<listing stopped, {RIFFUtil.FormatBytes(end - pos)} not expanded>", pos, end - pos);
                    stopped = true;
                    break;
                }

                uint size = RIFFUtil.ReadUInt32(buffer, 4, IsBigEndian);

                long payloadStart = pos + RIFFUtil.ChunkHeaderSize;
                long declaredEnd = payloadStart + size;          // size 最大 uint.MaxValue，long 运算不会溢出
                long payloadEnd = Math.Min(declaredEnd, end);    // 越界则裁剪到父块边界
                bool truncated = declaredEnd > end;

                var node = new Node(RIFFUtil.Sanitize(id), (ulong)pos, (ulong)(payloadEnd - pos));
                parent.SubNodes.Add(node);
                childCount++;

                bool typedContainer = RIFFUtil.TypedContainers.Contains(id)
                                      || Options.ExtraTypedContainerIds.Contains(id);

                if (typedContainer)
                {
                    // A3 + A5：RIFF/RIFX/RF64/BW64/LIST 一律先读 4 字节类型码，且必须先确认放得下
                    string? typeCode = null;
                    if (payloadEnd - payloadStart >= RIFFUtil.FourCCSize)
                    {
                        int read = await reader.ReadAtAsync(
                            payloadStart, buffer.AsMemory(0, RIFFUtil.FourCCSize), ct);
                        if (read == RIFFUtil.FourCCSize)
                            typeCode = RIFFUtil.TryDecodeFourCC(buffer, 0);
                    }

                    if (typeCode is null)
                    {
                        node.NodeName = $"{RIFFUtil.Sanitize(id)} (malformed)";
                    }
                    else
                    {
                        // A6：保留 "LIST"，同时展示类型码；分发不再依赖显示名（C1）
                        node.NodeName = $"{RIFFUtil.Sanitize(id)} ({RIFFUtil.Sanitize(typeCode)})";

                        long innerStart = payloadStart + RIFFUtil.FourCCSize;
                        if (depth < Options.MaxDepth)
                        {
                            await ParseChunkSequenceAsync(
                                node, innerStart, payloadEnd, depth + 1, reader, progressState, ct);
                        }
                        else
                        {
                            AddSynthetic(node, "<max depth reached>", innerStart, payloadEnd - innerStart);
                        }
                    }
                }
                else if (Options.PlainContainerIds.Contains(id) && depth < Options.MaxDepth)
                {
                    // 无类型码的裸容器：负载直接是子块序列
                    await ParseChunkSequenceAsync(
                        node, payloadStart, payloadEnd, depth + 1, reader, progressState, ct);
                }
                else if (Options.HeaderedContainerIds.TryGetValue(id, out int innerHeaderSize)
                         && depth < Options.MaxDepth)
                {
                    long innerStart = payloadStart + innerHeaderSize;
                    if (innerStart < payloadEnd)
                    {
                        await ParseChunkSequenceAsync(
                            node, innerStart, payloadEnd, depth + 1, reader, progressState, ct);
                    }
                }

                if (truncated) node.NodeName += " [truncated]";

                progressState.Report(payloadEnd);

                long next = RIFFUtil.Align2(payloadEnd);
                if (next <= pos)
                {
                    // B9：兜底，保证严格前进，任何情况下都不会死循环
                    Debug.Fail($"RIFF chunk at 0x{pos:X} did not advance.");
                    break;
                }
                pos = next;
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
        // 供 Chunk 解析器使用的 API
        // ============================================================

        /// <summary>
        /// 重新读取节点对应的块头（C3）。这样 Node 不需要携带任何额外字段，
        /// 而 Chunk 解析器也不用自己猜负载偏移是 +8 还是 +12（A7）。
        /// </summary>
        public bool TryGetChunkHeader(Node node, out RIFFChunkHeader header)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(node);

            header = default;

            long chunkStart = (long)node.Position;
            long nodeLength = (long)node.Length;
            long fileLength = FileStream.Length;

            if (chunkStart < 0 || nodeLength < RIFFUtil.ChunkHeaderSize) return false;
            if (chunkStart + RIFFUtil.ChunkHeaderSize > fileLength) return false;

            long nodeEnd = Math.Min(chunkStart + nodeLength, fileLength);

            Span<byte> buffer = stackalloc byte[RIFFUtil.TypedContainerHeaderSize];
            int need = nodeEnd - chunkStart >= RIFFUtil.TypedContainerHeaderSize
                ? RIFFUtil.TypedContainerHeaderSize
                : RIFFUtil.ChunkHeaderSize;

            if (!TryReadExactlyAt(FileStream.SafeFileHandle, buffer[..need], chunkStart)) return false;
            if (!RIFFUtil.IsPrintableFourCC(buffer[..4])) return false;   // 合成节点（<trailing data> 等）

            string id = RIFFUtil.DecodeFourCC(buffer[..4]);
            uint size = RIFFUtil.ReadUInt32(buffer.Slice(4, 4), IsBigEndian);

            string? typeCode = null;
            bool typedContainer = RIFFUtil.TypedContainers.Contains(id)
                                  || Options.ExtraTypedContainerIds.Contains(id);
            if (typedContainer && need == RIFFUtil.TypedContainerHeaderSize
                               && RIFFUtil.IsPrintableFourCC(buffer.Slice(8, 4)))
            {
                typeCode = RIFFUtil.DecodeFourCC(buffer.Slice(8, 4));
            }

            long payloadStart = chunkStart + (typeCode is null
                ? RIFFUtil.ChunkHeaderSize
                : RIFFUtil.TypedContainerHeaderSize);

            // 容器的 ckSize 含 4 字节类型码，要扣掉；下溢用 Math.Max 兜住（A7）
            long declaredPayload = typeCode is null
                ? size
                : Math.Max(0, (long)size - RIFFUtil.FourCCSize);

            long available = Math.Max(0, nodeEnd - payloadStart);

            header = new RIFFChunkHeader
            {
                Id = id,
                TypeCode = typeCode,
                DeclaredSize = size,
                ChunkStart = chunkStart,
                PayloadStart = payloadStart,
                DeclaredPayloadLength = declaredPayload,
                PayloadLength = Math.Min(declaredPayload, available),
            };
            return true;
        }

        /// <summary>定位读，不影响 FileStream.Position（B8），可安全在解析进行中调用。</summary>
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
                int read = RandomAccess.Read(FileStream.SafeFileHandle, destination.Slice(total, max - total), offset + total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        /// <summary>创建裁剪过范围的原始数据视图（构造 OffsetStream 前先 clamp，避免越界）。</summary>
        public Stream CreateRawStream(long offset, long length)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long fileLength = FileStream.Length;
            offset = Math.Clamp(offset, 0, fileLength);
            length = Math.Clamp(length, 0, fileLength - offset);
            return new OffsetStream(FileStream, offset, length);
        }

        /// <summary>基于块负载的读取器，已带好字节序。</summary>
        public RIFFReader CreatePayloadReader(in RIFFChunkHeader header) =>
            new(CreateRawStream(header.PayloadStart, header.PayloadLength), IsBigEndian);

        public ParseResult ParseNode(Node node)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return RIFFDispatcher.Dispatch(this, node);
        }

        // ============================================================
        // 内部：带缓冲的定位读取器（B4）
        // ============================================================

        /// <summary>
        /// RIFF 解析的特点是海量 8 字节小读。逐个 Seek+Read 会被系统调用拖死，
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
        // 内部：进度上报（B5 / B6）
        // ============================================================

        private sealed class ProgressState(IProgress<ParserProgress>? progress, long totalBytes)
        {
            private const int ThrottleMs = 200;
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();   // B5：单调时钟
            private long _lastReportMs = -ThrottleMs;
            private long _maxPosition;

            public void Report(long position)
            {
                if (position > _maxPosition) _maxPosition = position;       // 全局单调，嵌套容器也正确
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