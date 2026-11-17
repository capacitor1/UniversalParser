# 04 — 架构

> 本文件只定义结构、职责与硬性不变量，不包含实现代码。

---

## 1. 分层

```text
UniversalParser.Core/
  Binary/     只读随机访问源、缓冲、端序读取
  Model/      Range、Node、Result、Field、Diagnostic、Limits
  Parsing/    Parser 契约、Context、Registry、Session、Progress
  Formats/    IsoBmff Riff Ebml Flv Asf Fbx Png Jpeg Midi Psd Ogg Flac Blend Protobuf Nbt Raw Embedded
  Export/     Forensic report writer
  Statistics/ 不依赖 WinForms 的统计快照
  Validation/ 树不变量校验

UniversalParser.App/     WinForms GUI、Hex、虚拟字段列表、进度/取消
UniversalParser.Tests/   Core 与格式回归测试
```

原则：`Core` 不引用 WinForms；`App` 只消费 `Core` 的结果与 source；`Tests` 可直接构造内存 source。

---

## 2. 核心抽象

### 2.1 只读随机访问源

- 64 位 `Length`；按 `offset + buffer` 读取；不暴露可变 `Position`；线程安全；可释放。
- `FileSource`（系统随机读）、`MemorySource`（测试）、`BufferedReadSource`（顺序窗口）、
  `ParseCheckpointSource`（取消检查视图）。
- 所有读取检查 `offset >= 0`、`offset + count <= Length`，不返回超过请求的数据。
- 读取失败抛内部异常，由 parser 层转成诊断。

### 2.2 范围

统一使用 64 位半开区间 `[Offset, Offset + Length)`；`Length >= 0`；`Offset + Length` 用
`checked` 计算；派生 `End`、`Contains`、`Overlaps`、`IsEmpty`。节点范围不能越出 source。

### 2.3 节点

- `Name`、`Kind`（`Container`/`Leaf`/`Raw`/`Synthetic`/`Padding`/`Ignored`/`Unparsed`）、
  `Range`、可选 `PayloadRange`、不可变 `Children`、可选 `FormatId`/`StableId`。
- 节点不可变；子节点在父范围内；兄弟按 offset 排序且不重叠；未覆盖区间显式表示。
- 不把 `[truncated]` 拼进 `Name`。

### 2.4 字段

- `Key`（官方原始名或尖括号派生名）、`Value`（已格式化）、
  `Kind`（`Raw`/`Derived`/`Note`/`Warning`/`Error`）、可选 `RawValue`/`Range`/`Unit`/`Encoding`。
- `Raw` 字段的 `Key` 不允许尖括号；原始值不因派生解释而丢失；
  顺序由 parser 控制：原始在前，派生/诊断在后。

### 2.5 解析结果

- `Title`、`Range`、`Fields`、`Diagnostics`；可选 `RawRange`（默认等于 `Range`）、
  `Subtree`（仅当节点解析需要展示子结构时）。
- 不包含 `Stream RawData`。GUI 需要 Hex 时使用 `source + result.RawRange`。

### 2.6 诊断

- `Severity`（Note/Warning/Error）、`Code`（稳定英文码）、`Message`（英文短句）、
  可选 `Range`/`Detail`。
- 解析失败是诊断，不是异常文本字段；诊断不改变 `RawRange`；可导出、过滤、着色。
- 诊断只针对格式本身（D-028）。

### 2.7 限制

`ParseLimits` 只约束单次读取的内存（D-008、D-032）：

| 键 | 默认 | 约束 |
|---|---:|---|
| `MaxStringBytes` | 4 MiB | 单个字符串 |
| `MaxBlobBytes` | 256 MiB | 单个 Blob / 解压输出 |
| `MaxDataLines` | 10,000 | 表格/记录循环 |

- `ParseLimits.Default` 是唯一内置来源；`ParseLimitsConfiguration` 可从可执行文件旁的
  `universalparser.limits.json` 读取部分覆盖；解析器只通过 `ParseContext.Limits` 使用。
- 树深度、每节点子数、总节点数、解析时长均无软件级上限。
- 探测窗口与长时间读取的进度阈值固定 512 KiB，不属于 `ParseLimits`（D-037）。
- 触发限制时只截断当前字符串/Blob/表格并产生 `LIMIT_STRING`/`LIMIT_BLOB`/`LIMIT_RECORDS`，
  保留已解析结构与原始范围。导出没有总字节限制，也不产生 `LIMIT_EXPORT`（D-034）。

### 2.8 取消与检查点

- `ParseContext` 提供取消检查、进度报告与 `ParseCheckpointSource`，不持有时间预算。
- `ParseCheckpointSource` 每次读取前检查取消；只有 source/token 的内嵌解析器也不能绕过取消。
- 用户取消抛 `OperationCanceledException`；部分结果由上层丢弃，不伪装成格式错误。
- 单节点用 `ParseContext.CreateFieldList` 注册字段收集器；失败时取回部分字段。
- 同步 I/O / BCL 内部计算不能强制中断，取消在下一个安全点生效。

---

## 3. Parser 契约

每个格式 parser 提供：

- `Id`：稳定英文标识，如 `isobmff`
- `DisplayName`：英文全称
- `ProbePriority` + `Probe`：读取有限 header，返回 `NoMatch`/`WeakMatch`/`StrongMatch`
- `Parse`：构建节点树
- `ParseNode`：解析单节点字段

`ParseContext` 提供 `Source`、`Reader`、`Limits`、`Diagnostics`、`Progress`、
`CancellationToken` 与当前格式配置。

规则：

- parser 不持有 GUI 控件、不弹窗、不直接写文件、不依赖 `FileStream.Position`。
- 所有循环检查取消；单次读取受 `ParseLimits` 内存限制。
- `ParserEngine.Parse` 先上报 `Detecting format`，再 `Parsing`，结束 `Done`。
- 文档解析通过 `ReportProgress(stage, processed, total)` 上报；合并到 100 ms 一次，
  最终位置必达；`processed` 用文件绝对偏移，`total` 用 source 长度；嵌套递归不得让进度倒退。
- GUI 在首个真实进度前用 Marquee，首个 `Processed > 0` 后切换连续进度；导出复用同一契约。

---

## 4. 注册与探测

注册表是显式列表：`IsoBmff`、`Jpeg`、`Ogg`、`Blend`、`Png`、`Flac`、`Psd`、`Midi`、`Fbx`、
`Asf`、`Flv`、`Ebml`、`Riff`、`Nbt`、`Protobuf`、`Raw`（兜底，必须唯一）。

- 强匹配优先；同优先级按注册顺序；`Raw` 永远最后。
- 探测只读有限 header，不构建树；失败不抛异常。
- 无 magic 的 Protobuf/NBT 使用低优先级（90/95）与结构证据；已有 magic 格式优先。
- 探测结果写入报告，便于分析误判。

---

## 5. 范围与覆盖不变量

### 5.1 默认严格覆盖

```text
parent.Range = union(children.Range, explicit gaps)
```

- padding 计入节点范围。
- 未解析区间成为 `Unparsed`/`Padding`/`Trailing` 节点。
- 截断节点范围是实际可读范围，并有诊断。

### 5.2 例外

非严格块式格式的框架数据必须显式表示（例如 FBX `Header` 与 `Footer`、AVI `LIST:movi`、
EBML block 元素）。要求：在 `08-format-notes.md` 记录、报告说明忽略范围、
尽量生成 `Ignored`/`Unparsed` 节点；不允许静默跳过。

### 5.3 校验

`NodeTreeValidator` 校验：子节点在父节点内、兄弟不重叠、排序正确、范围不越界、
截断标记与诊断一致、`ParseNode` 返回范围与节点范围一致。测试必须开启。

---

## 6. 错误与截断

| 层级 | 责任 |
|---|---|
| Source | 读取/边界，不解释格式 |
| Reader | 长度、端序、字符串上限 |
| Format parser | 解释结构，生成节点/诊断 |
| Orchestrator | 捕获未处理异常，生成 ParseError |
| GUI | 只展示，不修复数据 |

规则：

1. 探测失败 → Raw 兜底，不抛。
2. 树构建损坏 → 部分树 + 合成节点 + 诊断。
3. 单节点解析失败 → 真实范围 + 尽量保留原始字段 + 错误诊断；不伪造 RawData。
4. 顶层未捕获异常 → orchestrator 返回错误结果 + 诊断。
5. 异常文本永不进入 RawData；GUI 不在异常后保留旧结果。

常用诊断码：`TRUNCATED_HEADER`、`TRUNCATED_PAYLOAD`、`TRUNCATED_CHILD`、
`INVALID_LENGTH`、`LENGTH_OVERFLOW`、`INVALID_VINT`、`INVALID_FIELD_NUMBER`、
`INVALID_WIRE_TYPE`、`INVALID_TAG_TYPE`、`LIMIT_STRING`、`LIMIT_BLOB`、`LIMIT_RECORDS`。
完整列表见 `Model/DiagnosticCodes.cs`。

---

## 7. 大文件与 GUI

### 7.1 解析

- 64 位 offset/length；不整文件读入。
- 顺序格式用缓冲 source，随机格式用随机读；大 payload 只记录范围。
- EBML/JPEG/FBX/FLV 建树使用固定 16 MiB 前向窗口，小于 1 MiB 不启用；
  窗口在 document parser 的 `using` 生命周期内，Dispose 时释放，不跨文件缓存（D-031）。
- 回退/跨窗口读取按请求 offset 重新填充；大于窗口的请求直接透传底层 source。

### 7.2 GUI 状态与生命周期

只保存：当前 source、parser、根节点、选中节点、结果、`CancellationTokenSource`。
不保存共享 `FileStream`、parser 内部 `Position`、已 Dispose 的 `Stream` 或旧结果引用。

打开文件：取消旧解析 → 释放旧 source → 清空结果/树/列表/Hex → 创建新 source →
探测 → 异步解析 → 构建树。

关闭文件：取消 → 释放 source → 释放结果 → 清空树/列表/Hex/缓存 → 重置 UI。

### 7.3 线程

解析与导出在后台；UI 更新走 `Progress`/`InvokeAsync`；不使用 `Application.DoEvents()`；
不关闭跨线程检查；取消是协作式的；关闭时取消所有任务。

### 7.4 根节点

根节点只显示文件系统信息与探测结果（D-025）：`File name`、`File size`、`Creation time`（UTC）、
`File attributes`、`Detected format`、`Probe confidence`。诊断仍在状态栏/报告中。
字段由 `MainForm.BuildRootFields` 生成。

### 7.5 实现映射与性能

| 旧版 | 新版 |
|---|---|
| `Form1` + `FileStream` + `IParser` | `MainForm` + `ParseSession`（`FileSource` + `ParseDocument`） |
| `TreeView` 同步 `ExpandAll` | 懒加载 + 分批异步 `Expand All`，可取消、无节点数上限 |
| `ListView` 虚拟模式（`Tag` 存数据） | `VirtualFieldListView` + `ImmutableArray<Field>`，OwnerDraw |
| `HexViewer` + 共享 `Stream.Position` | `HexViewer` + `IRandomAccessSource` + `HexDumpFormatter`（64 位行号） |
| 同步 `ParseNode` | 后台 `Task.Run` + 取消 + 代次校验 |
| 手写导出 | `ForensicReportWriter`（流式、取消、进度、转义、sanitize） |
| `CheckForIllegalCrossThreadCalls = false` | 移除；进度经 `IProgress<T>` 回 UI 线程 |
| 手绘/resx 图标 | `TreeIcons` 从嵌入的 48 px PNG 缩放（D-027） |

约束：

- 树只在展开时创建子节点，一次创建该节点全部子节点；`Expand All` 每 200 个节点让出 UI。
- 字段列表只创建可见行，高度上限 1000 行。
- Hex 只渲染可见行；滚动条用 `Int128` 映射，保证最后一行可达。
- 节点结果缓存 128 项；重复选择不重复解析。
- 打开/关闭/导出全部走取消令牌。

### 7.6 视图设置、统计与图标

- 统计计算在 Core（`StatisticsSnapshot`）：迭代式树遍历、`O(depth)` 内存、
  字节占比按节点独占字节、节点计数不含合成根。
- `Largest nodes` 按 `NodeKind` 各保留前 200 个候选，UI 过滤后合并排序。
- 视图设置是进程内静态值，不落盘：`ValueFormat.PercentageDecimals`（1–6，默认 3）、
  `AppSettings.TopChartSlices`（3/5/8/10，默认 10）。
- `Largest nodes` 双击/右键/Enter 定位节点；`Repeated names` 唯一名称灰显。
- 树图标：`Root=0`、`FolderClosed=1`、`FolderOpen=2`、`File=3`、`Checked=4`；
  选中一律 `Checked`，根节点不参与展开/折叠改图标。
- 菜单快捷键：`Ctrl+O`、`Ctrl+W`、`Ctrl+Shift+E`、`Ctrl+Shift+C`、`Ctrl+Shift+S`。
- 字体：Fira Code → Cascadia Mono → Consolas → 泛等宽回退。

---

## 8. 导出架构

- 输入：source、根节点、选中节点、结果、诊断、limits、parser 信息。
- 输出：UTF-8 纯文本；流式写、转义、可取消、有进度。
- `ForensicReportWriter` 无总字节预算；Fields/Structure 始终完整写入。
- Hex 段使用 `MaxHexHeadBytes = 128 KiB` / `MaxHexTailBytes = 128 KiB`：
  `<= 256 KiB` 完整输出；超出时首尾两段分开写，并输出 `[Hex truncated]` 说明，
  提示使用 `Export Raw`。
- `Write` 返回 `ReportWriteResult`（字节数、Hex 是否使用首尾窗口）。
- 结构迭代器只保留每个祖先的一个 continuation，不一次压入全部兄弟节点。
- `RawRangeWriter` 流式复制完整范围，不接收 `ParseLimits`，不因大小拒绝；提前 EOF 抛读异常。

Hex 渲染契约：

- `HexDumpFormatter.RenderLine(source, wideOffset, baseOffset, length, lineIndex)`：
  `baseOffset + length` 是节点/RawRange 边界，最后一行最多读取剩余字节；
  不允许为对齐读取下一个节点。
- 行数由 `GetLineCount(length)` 决定；ASCII 区同样只覆盖实际字节。
- GUI 与报告必须传节点范围长度，不能固定传 16。
- 表头/文字/选区/鼠标命中共用 `GetHexColumn`/`GetAsciiColumn`/`FormatHeader`；
  字节跨度到列统一走 `GetColumnSpan`。
- 列坐标用与绘制相同的 `TextRenderer` 字体/flags 逐列测量；字体/DPI 变化后重测。

---

## 9. 依赖方向

```text
App → Core
Tests → Core
Core → BCL only
Formats → Core
Export → Core
```

禁止：`Core → WinForms`、`Format → UI`、`GUI → 直接读文件`、`Export → parser 内部流`。

---

## 10. 架构验收

- Core 可独立构建/测试；任意格式 parser 不引用 WinForms。
- 任意节点解析不修改 source 全局位置。
- 任意解析失败都有诊断；任意截断都有范围与诊断。
- 任意大文件路径不使用 int 截断。
- GUI 关闭后无残留 source/result。
- 导出可流式、可取消、可分析。
