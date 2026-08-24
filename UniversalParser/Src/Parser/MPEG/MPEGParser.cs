using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalParser.Src.Parser.MPEG
{
    // MPEG-4 / ISO Base Media File Format box parser
    // Implements full recursive parsing compliant with ISO/IEC 14496-12
    public sealed class MPEGParser : IParser
    {
        public string ContainerTypeName => "MPEG Container";
        public FileStream FileStream { get; private set; }

        private bool _disposed = false;
        private readonly byte[] _readBuffer = new byte[32 * 1024]; // 32KB buffer for efficiency

        private const int MinBoxSize = 8;    // size(4) + type(4)
        private const int LargeHeader = 16;  // size==1 时：size(4) + type(4) + largesize(8)
        private const int MaxDepth = 64;     // 防止损坏文件导致 async 栈溢出
        // QuickTime metadata item atoms use a 1-based key index as their "type" field.
        // Bound it so that random garbage is not mistaken for an item.
        private const uint MaxIlstKeyIndex = 0xFFFF;

        public MPEGParser(FileStream fileStream)
        {
            if (fileStream == null || !fileStream.CanRead)
                throw new ArgumentException("FileStream must be readable.");
            FileStream = fileStream;
        }
        // Records the parent box type for every node position, so that leaf parsers can
        // resolve 4CCs whose meaning depends on the containing box. For example 'thmb' is
        // an item reference inside 'iref' but a track reference inside 'tref'.
        private readonly Dictionary<ulong, string> _parentTypeByPosition = new Dictionary<ulong, string>();

        public string? GetParentType(Node node)
            => _parentTypeByPosition.TryGetValue(node.Position, out string? type) ? type : null;

        public void Dispose()
        {
            if (!_disposed)
            {
                try { FileStream?.Dispose(); } catch { }
                _disposed = true;
            }
        }

        // ==================================================================
        // 校验
        // ==================================================================

        // ISO BMFF：第一个有意义的 box 应为 ftyp（片段流为 styp/moof；部分 QuickTime 直接以 moov 开头）
        // 允许在它之前出现 free / skip / wide / pnot 之类的填充 box
        public static bool IsValid(FileStream fs)
        {
            if (fs == null || !fs.CanRead || fs.Length < MinBoxSize) return false;

            long originalPos = fs.Position;
            try
            {
                long fileLength = fs.Length;
                long position = 0;
                var header = new byte[LargeHeader];

                for (int probe = 0; probe < 8 && position + MinBoxSize <= fileLength; probe++)
                {
                    fs.Seek(position, SeekOrigin.Begin);
                    if (ReadExact(fs, header, 0, 8) < 8) return false;

                    uint size32 = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(header, 0, 4));
                    if (!IsPrintableType(new ReadOnlySpan<byte>(header, 4, 4))) return false;

                    string boxType = BoxName.ReadBoxType(header, 4);

                    long headerSize = 8;
                    long total;

                    if (size32 == 1)
                    {
                        headerSize = LargeHeader;
                        if (position + headerSize > fileLength) return false;
                        fs.Seek(position + 8, SeekOrigin.Begin);
                        if (ReadExact(fs, header, 0, 8) < 8) return false;

                        ulong large = BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(header, 0, 8));
                        if (large > long.MaxValue) return false;
                        total = (long)large;
                    }
                    else if (size32 == 0)
                    {
                        total = fileLength - position;
                    }
                    else
                    {
                        total = size32;
                    }

                    if (total < headerSize || position + total > fileLength) return false;

                    if (boxType == "ftyp" || boxType == "styp" || boxType == "moof" || boxType == "moov")
                        return true;

                    if (boxType != "free" && boxType != "skip" && boxType != "wide" && boxType != "pnot")
                        return false;

                    position += total;
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { fs.Seek(originalPos, SeekOrigin.Begin); } catch { }
            }
        }

        // ==================================================================
        // 解析入口
        // ==================================================================

        public async Task<Node> ParseAsync(IProgress<ParserProgress>? progress = null,
                                           CancellationToken cancellationToken = default)
        {
            long fileLength = FileStream.Length;
            var root = new Node(Path.GetFileName(FileStream.Name), 0, (ulong)fileLength);

            var reporter = new ProgressReporter(progress, fileLength);

            // 顶层与嵌套层用同一套扫描逻辑，不再有两份重复代码
            await ScanBoxesAsync(root, null, 0, fileLength, 0, reporter, cancellationToken)
                  .ConfigureAwait(false);

            reporter.Report(fileLength, force: true);
            return root;
        }

        /// <summary>
        /// 扫描 [start, end) 区间内的兄弟 box 序列，挂到 parent 下。
        /// start 已经是「第一个子 box 的绝对偏移」，end 是「父 box 的绝对末尾」——
        /// 这两个值分开传，就不需要 IncreaseOffsetSp / DecreaseOffsetSp 那种改指针的补丁了。
        /// </summary>
        private async Task ScanBoxesAsync(Node parent, string? parentType, long start, long end,
            int depth, ProgressReporter reporter, CancellationToken ct)
        {
            if (depth > MaxDepth) return;

            // Inside 'ilst' the child atom "type" field is not a 4CC. QuickTime stores the
            // 1-based index of the matching entry in the sibling 'keys' atom instead.
            bool numericChildTypes = parentType == "ilst";

            long position = start;

            while (position + MinBoxSize <= end)
            {
                ct.ThrowIfCancellationRequested();

                BoxHeader? maybeHeader = await ReadBoxHeaderAsync(position, end, numericChildTypes, ct)
                    .ConfigureAwait(false);
                if (maybeHeader == null) break;   // header incomplete / illegal 4CC / unusable size


                BoxHeader header = maybeHeader.Value;
                long boxEnd = position + header.TotalSize;

                var node = new Node(header.Type, (ulong)position, (ulong)header.TotalSize);
                parent.SubNodes.Add(node);
                if (parentType != null)
                    _parentTypeByPosition[(ulong)position] = parentType;

                if (!header.Truncated && IsContainerBox(header.Type, parentType))
                {
                    long payloadStart = position + header.HeaderSize;   // 8 或 16，不再写死
                    long childrenStart = await ResolveChildrenStartAsync(header.Type, payloadStart, boxEnd, ct)
                                               .ConfigureAwait(false);

                    if (childrenStart >= payloadStart && childrenStart + MinBoxSize <= boxEnd)
                    {
                        await ScanBoxesAsync(node, header.Type, childrenStart, boxEnd,
                                             depth + 1, reporter, ct).ConfigureAwait(false);
                    }
                }

                position = boxEnd;

                if (depth == 0) reporter.Report(position);

                if (header.Truncated) break;   // size 越界已被钳制，后面的偏移不可信，停止本层扫描
            }
        }

        // ==================================================================
        // box 头
        // ==================================================================

        private readonly struct BoxHeader
        {
            public BoxHeader(string type, long headerSize, long totalSize, bool truncated)
            {
                Type = type;
                HeaderSize = headerSize;
                TotalSize = totalSize;
                Truncated = truncated;
            }

            public string Type { get; }
            public long HeaderSize { get; }   // 8 或 16
            public long TotalSize { get; }    // 含 header 的完整长度
            public bool Truncated { get; }    // size 字段越界，已被钳制到父 box 末尾
        }

        private async Task<BoxHeader?> ReadBoxHeaderAsync(long position, long limit,
            bool allowNumericType, CancellationToken ct)
        {
            if (limit - position < MinBoxSize) return null;
            if (await ReadAtAsync(position, 8, ct).ConfigureAwait(false) < 8) return null;

            uint size32 = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(_readBuffer, 0, 4));

            // The type must be resolved before the largesize read overwrites _readBuffer.
            var typeSpan = new ReadOnlySpan<byte>(_readBuffer, 4, 4);
            string boxType;

            if (IsPrintableType(typeSpan))
            {
                boxType = BoxName.ReadBoxType(_readBuffer, 4);
            }
            else if (allowNumericType)
            {
                uint keyIndex = BinaryPrimitives.ReadUInt32BigEndian(typeSpan);
                if (keyIndex == 0 || keyIndex > MaxIlstKeyIndex) return null;
                boxType = FormatKeyIndex(keyIndex);
            }
            else
            {
                return null;
            }

            long headerSize = 8;
            long total;

            if (size32 == 1)
            {
                headerSize = LargeHeader;
                if (limit - position < headerSize) return null;
                if (await ReadAtAsync(position + 8, 8, ct).ConfigureAwait(false) < 8) return null;

                ulong large = BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(_readBuffer, 0, 8));
                if (large > long.MaxValue) return null;   // 防 (long) 强转成负数
                total = (long)large;
            }
            else if (size32 == 0)
            {
                total = limit - position;                  // 延伸到父 box / 文件末尾
            }
            else
            {
                total = size32;
            }

            if (total < headerSize) return null;           // size==1 时最小 16，不再是 8

            bool truncated = false;
            if (position + total > limit)
            {
                total = limit - position;
                truncated = true;
            }

            if (total < headerSize) return null;

            return new BoxHeader(boxType, headerSize, total, truncated);
        }

        // ==================================================================
        // 容器前缀：取代 IncreaseOffsetSp / DecreaseOffsetSp
        // ==================================================================

        /// <summary>
        /// 求「第一个子 box 的绝对偏移」。
        /// 按类型给出候选前缀，逐个探测哪个位置真的是一个合法 box 头。
        /// </summary>
        private async Task<long> ResolveChildrenStartAsync(string boxType, long payloadStart, long boxEnd,
                                                           CancellationToken ct)
        {
            List<int> candidates = await GetChildPrefixCandidatesAsync(boxType, payloadStart, boxEnd, ct)
                                         .ConfigureAwait(false);

            // 绝大多数容器就是 0，省掉一次探测 IO
            if (candidates.Count == 1 && candidates[0] == 0) return payloadStart;

            foreach (int prefix in candidates)
            {
                long pos = payloadStart + prefix;
                if (pos < payloadStart || pos + MinBoxSize > boxEnd) continue;

                if (await LooksLikeBoxAsync(pos, boxEnd, ct).ConfigureAwait(false))
                    return pos;
            }

            // 全部探测失败（空容器或未知变体）：退回首选前缀，子扫描会自然地什么也找不到
            long fallback = payloadStart + candidates[0];
            return fallback > boxEnd ? boxEnd : fallback;
        }

        /// <summary>
        /// box header 结束 → 第一个子 box 之间的字节数候选，按优先级排列。
        /// 这些字节按需求「只是 version/flags/计数一类的少量字段」，直接跳过、不解析。
        /// </summary>
        private async Task<List<int>> GetChildPrefixCandidatesAsync(string boxType, long payloadStart,
                                                                    long boxEnd, CancellationToken ct)
        {
            switch (boxType)
            {
                // FullBox 容器。ISO 规范里有 4 字节 version/flags；
                // 但 Apple/QuickTime 的 meta 没有，子 box 紧跟 header —— 靠探测自动区分
                case "meta":
                    return Dedup(4, 0);

                // FullBox + entry_count（version 0 为 16 位，其余为 32 位）
                case "iinf":
                {
                    int version = await ReadVersionAsync(payloadStart, boxEnd, ct).ConfigureAwait(false);
                    int preferred = 4 + (version == 0 ? 2 : 4);
                    return Dedup(preferred, 6, 8, 4);
                }

                // FullBox + 16 位计数
                case "ipro":   // ItemProtectionBox : protection_count
                case "fiin":   // FDItemInformationBox : entry_count
                    return Dedup(6, 4);

                // FullBox + 32 位字段
                case "dref":   // DataReferenceBox : entry_count
                case "trep":   // TrackExtensionPropertiesBox : track_ID
                    return Dedup(8, 4);

                // 纯 FullBox 容器
                case "iref":   // ItemReferenceBox
                    return Dedup(4, 0);

                default:
                    return Dedup(0);
            }
        }

        private async Task<int> ReadVersionAsync(long payloadStart, long boxEnd, CancellationToken ct)
        {
            if (boxEnd - payloadStart < 1) return -1;
            if (await ReadAtAsync(payloadStart, 1, ct).ConfigureAwait(false) < 1) return -1;
            return _readBuffer[0];
        }

        /// <summary>指定位置看起来是否是一个合法 box 头（用于前缀探测）。</summary>
        private async Task<bool> LooksLikeBoxAsync(long position, long limit, CancellationToken ct)
        {
            if (limit - position < MinBoxSize) return false;
            if (await ReadAtAsync(position, 8, ct).ConfigureAwait(false) < 8) return false;

            uint size32 = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(_readBuffer, 0, 4));
            if (!IsPrintableType(new ReadOnlySpan<byte>(_readBuffer, 4, 4))) return false;

            if (size32 == 0) return true;                          // 延伸到父 box 末尾
            if (size32 == 1) return limit - position >= LargeHeader;

            return size32 >= MinBoxSize && position + size32 <= limit;
        }

        // ==================================================================
        // 容器类型表
        // ==================================================================

        // Check if a box type is a container box that can have child boxes
        public static readonly HashSet<string> ContainerBoxes =
[
    // ---- 纯 Box 容器（prefix = 0）----
    "moov",
    "trak",
    "edts",
    "mdia",
    "minf",
    "dinf",
    "stbl",
    "mvex",
    "moof",
    "traf",
    "mfra",
    "udta",
    "ilst",
    "ipro",
    "sinf",
    "schi",
    "rinf",   // RestrictedSchemeInfoBox
    "fiin",
    "paen",
    "meco",
    "iprp",
    "ipco",
    "grpl",   // GroupsListBox
    "strk",   // SubTrackBox
    "strd",   // SubTrackDefinitionBox
    "cinf",   // CompleteTrackInformationBox
    "tref",   // TrackReferenceBox
    "trgr",   // TrackGroupBox
    "hnti",   // QuickTime hint info
    "hinf",   // QuickTime hint statistics
    "----",
    "tapt",
    // ---- 带少量前缀字段的容器（prefix 见 GetChildPrefixCandidatesAsync）----
    "meta",
    "iinf",   // 新增：现在按目录树展开成 infe 子节点
    "iref",
    "dref",
    "trep",
    
    "gmhd",
    "tmcd"
];

        // 明确当作叶子的 box：即使结构上含子 box，也不再往下拆
        public static readonly HashSet<string> ForcedLeafBoxes =
[
    "stsd",   // 自身字段复杂（sample entry 有 78/28/44 字节固定前缀），整体当普通 box
    "mdat",
    "free",
    "skip",
    "wide",
    "uuid",
    "mere",
    "infe"    // 由 Infe.cs 解析字段
];
        public static readonly HashSet<string> NonContainerBoxes =//qt
        [
            "desc",
            "ldes",
            "covr",
        ];

        private static bool IsContainerBox(string boxType, string? parentType)
        {
            if (ForcedLeafBoxes.Contains(boxType)) return false;
            if (ContainerBoxes.Contains(boxType)) return true;

            // Apple 'ilst' 里每个子 box 的名字就是元数据键（如 '©nam'、'covr'），
            // 键 box 内部还有 'data' —— 这类 box 名不可枚举，只能靠父级上下文判定
            if (parentType == "ilst") return true;

            return false;
        }

        private static bool IsContainerBox(string boxType) => IsContainerBox(boxType, null);

        public ParseResult ParseNode(Node node)
        {
            //补丁：不暴露A9开头容器
            if (node.NodeName[0] == (char)0xA9)
            {
                return MPEGBoxDispatcher.Dispatch(this, node);
            }
            else if (NonContainerBoxes.Contains(node.NodeName))
            {
                return MPEGBoxDispatcher.Dispatch(this, node);
            }
            // 已经拆出子节点的（含 ilst 下的 '©xxx' 这类动态容器）统一走容器渲染
            if (node.SubNodes.Count > 0 || IsContainerBox(node.NodeName))
                return Boxes.Boxes.Parse(this, node);

            return MPEGBoxDispatcher.Dispatch(this, node);
        }

        // ==================================================================
        // 工具
        // ==================================================================

        /// <summary>把 [position, position+count) 读入 _readBuffer 起始处，循环补齐短读。</summary>
        private async Task<int> ReadAtAsync(long position, int count, CancellationToken ct)
        {
            FileStream.Seek(position, SeekOrigin.Begin);

            int total = 0;
            while (total < count)
            {
                int n = await FileStream.ReadAsync(_readBuffer, total, count - total, ct).ConfigureAwait(false);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        private static int ReadExact(Stream s, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buffer, offset + total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        // 4CC 合法性：可打印 ASCII，另外放行 Apple 元数据键的 0xA9（©）
        private static bool IsPrintableType(ReadOnlySpan<byte> type)
        {
            for (int i = 0; i < type.Length; i++)
            {
                byte b = type[i];
                bool ok = (b >= 0x20 && b <= 0x7E) || b == 0xA9;
                if (!ok) return false;
            }
            return true;
        }
        // QuickTime metadata item atom: the type field carries a 1-based index into 'keys'.
        // Rendered with a stable ASCII name so the tree stays readable.
        private static string FormatKeyIndex(uint keyIndex) => $"key[{keyIndex}]";

        private static List<int> Dedup(params int[] values)
        {
            var list = new List<int>(values.Length);
            foreach (int v in values)
                if (!list.Contains(v)) list.Add(v);
            return list;
        }

        private sealed class ProgressReporter
        {
            private const int ReportIntervalMs = 200;

            private readonly IProgress<ParserProgress>? _progress;
            private readonly long _totalBytes;
            private readonly DateTime _startTime = DateTime.UtcNow;
            private DateTime _lastReport = DateTime.MinValue;

            public ProgressReporter(IProgress<ParserProgress>? progress, long totalBytes)
            {
                _progress = progress;
                _totalBytes = totalBytes;
            }

            public void Report(long processed, bool force = false)
            {
                if (_progress == null) return;

                var now = DateTime.UtcNow;
                if (!force &&
                    (now - _lastReport).TotalMilliseconds < ReportIntervalMs &&
                    processed < _totalBytes)
                    return;

                double seconds = (now - _startTime).TotalSeconds;
                double speed = seconds > 0 ? processed / seconds : 0.0;
                double fraction = _totalBytes > 0 ? (double)processed / _totalBytes : 0.0;

                _progress.Report(new ParserProgress
                {
                    Fraction = Math.Clamp(fraction, 0.0, 1.0),
                    BytesRead = (ulong)Math.Max(processed, 0),
                    TotalBytes = (ulong)Math.Max(_totalBytes, 0),
                    BytesPerSecond = speed
                });

                _lastReport = now;
            }
        }
    }
}