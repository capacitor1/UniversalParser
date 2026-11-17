# 02 — 产品决策记录

> **归档快照（2026-11-17）。** 非当前规范；当前生效规则见 `../02-product-decisions.md`。
> 本文件保留 D-001 ~ D-043 的完整理由、实现含义与验收记录，含已被取代的 D-030。

> 本文件是**最高优先级的项目决策来源**。
> 用户已明确回答的事项，后续实现不得违背；如要改变，必须先改本文件并说明原因。
> 状态：Accepted（2026-10-18）

---

## D-001 导出不兼容旧版文本

### 决策

导出功能**不要求**与旧版 TXT 逐字节兼容。

### 原因

导出以**故障分析**为目标：文件格式损坏、解析失败、行为异常时，用户需要把报告交给：

- AI 大模型
- 文件查看器开发者
- 格式研究者

报告要回答“文件为什么炸”，而不是复刻旧 UI 的文本格式。

### 实现含义

- 可以重新设计报告结构。
- 报告必须包含足够的上下文：
  - 文件名、大小
  - 解析器名称/版本
  - 文件格式探测结果
  - 完整节点路径与 source range
  - 原始字段 + 人类可读解释
  - 诊断（Note / Warning / Error），带错误码与范围
  - 相关 Hex（按需、可分页/截断）
  - 读取/导出限制是否触发（字符串/Blob/表格行/导出字节）
- 报告必须可读、可搜索、可复制；推荐同时提供结构化 JSON（可选）。
- 不为了“像旧版”而保留旧版的歧义、未转义 tab、魔法键。

### 验收

- 报告能独立说明“哪个节点、哪个范围、哪个字段、什么错误、解析器做了什么”。
- 报告中的 Raw Hex 与 source range 一致。
- 大文件导出可流式进行，不一次性拼巨大字符串。

---

## D-002 padding 计入节点长度，禁止看不见的空洞

### 决策

原则上：

- 节点 `Length` 计入 padding。
- 整个文件不应存在“看不到”的空洞。
- 子节点必须覆盖父节点范围；未覆盖区间显式表示为 `Padding` / `Unparsed` / `Trailing` / `Ignored` 节点。

### 例外

某些非严格块式格式可以忽略一部分格式框架数据，例如：

- FBX 的 footer（未知/加密块）

例外必须：

1. 在 `08-format-notes.md` 记录；
2. 尽量用显式合成节点表示；
3. 在报告中说明“该范围被忽略/未解析”；
4. 不能静默跳过。

### 实现含义

- RIFF 的 1 字节 pad 计入 chunk 范围，或显式生成 `Padding` 节点；二选一，但全局一致。
- 顶层 trailing bytes 必须成为节点或诊断。
- 截断节点必须有明确 `truncated` 诊断，不能只把长度改小。
- 节点范围必须满足：`child.Offset >= parent.Offset`、`child.End <= parent.End`、兄弟不重叠。

### 验收

- 对任意样本，树的所有节点范围并集 + 显式 gap 节点 = 文件范围（例外格式除外）。
- 不允许出现“父节点长度 100，子节点只覆盖 60，剩余 40 无任何说明”。

---

## D-003 RawData 惰性读取

### 决策

RawData 按需读取，不允许把大块数据整块加载进内存。

### 原因

- 旧实现已有 `OffsetStream` 思路，但所有权和生命周期混乱。
- 大文件/大节点不能靠 `MemoryStream`。
- Hex Viewer 只需要当前可见行。

### 实现含义

- 解析结果只保存 `SourceRange`，不保存 `Stream`。
- Hex Viewer 通过 `IRandomAccessSource` + range 读取。
- 导出 Raw 时流式复制，不整块读入。
- 可选优化：
  - 64KB 顺序缓冲
  - 内存映射（仅作为可选 source 实现）
  - 小范围缓存（例如当前 Hex 页）
- 任何“读取整个 payload”的 API 必须显式命名，并受 `MaxBlobBytes` 限制。

### 验收

- 打开 100GB 文件不因 RawData 预加载而 OOM。
- 选择一个 10GB 节点不会一次性分配 10GB。
- 关闭文件后 source 被释放，Hex 不再可读。

---

## D-004 必须支持超大文件

### 决策

必须支持超大文件场景。

### 实现含义

- 所有 offset / length / 行号 / 文件大小使用 64 位。
- 禁止未经检查的 `(int)` / `(uint)` 转换。
- 文件大小、节点范围、Hex 行号、导出进度都使用 `long`。
- GUI 中受 `int` 限制的控件必须处理：
  - `ListView.VirtualListSize` 是 `int`
  - `VScrollBar.Maximum` 是 `int`
  - 字符串/数组长度是 `int`
- 解决方案：
  - 分页/分段虚拟化
  - 自定义滚动模型（逻辑行号 `long`，映射到当前页）
  - 只对当前可见页建立 `int` 索引
- Hex 行数不能再用 `int` 直接计算。
- 导出必须流式，不能拼接整个 Hex 字符串。

### 验收

- 至少用稀疏/合成大文件测试 >4GB、>2GB 单节点、>34GB 行数边界。
- 不出现负数行号、溢出、错误 offset。
- UI 不因文件大小而卡死。

---

## D-005 原始数据优先，官方命名优先

### 决策

这是全局字段/显示风格，详细规则见 `03-style-guide.md`。

核心：

1. **原始字段名遵循官方规范。**
   - 官方 snake_case → snake_case
   - 官方 camelCase → camelCase
   - 官方 PascalCase → PascalCase
   - 官方只给出带空格的 prose label（如 `Bit depth`）→ 保留规范写法，不擅自改成 `BitDepth`
   - 不翻译、不擅自统一大小写
2. **官方没有明确命名的原始字段**：使用描述性英文键，**不加尖括号**。
3. **原始值优先**：
   - 默认：`official_name = raw_value`
   - 可短写解释：`official_name = raw_value (human_readable)`
   - 解释太长/单行放不下：使用独立尖括号条目
4. **尖括号只允许用于**：
   - 解释后创造出的非原始值（payload length 通常属于此类）
   - 极度重要、必须提示的短人类可读 Note
   - 解析器内部错误
5. **原始字段绝不允许加尖括号。** 尖括号标记的是“非原始条目”，不是“看起来像元数据的原始字段”。
6. 如果规范已有原始 `size` / `length` 字段，先展示它；不要用 `<PayloadLength>` 取代它。需要时再额外给派生条目。

### 例子

```text
data_flag_example = 0F (C1|C2|H1)
```

- `data_flag_example` 是官方字段名
- `0F` 是原始值
- `(C1|C2|H1)` 是短解释

如果解释太长：

```text
data_flag_example = 0F
<DecodedMeaning> = data_flag_example: C1 | C2 | H1 | ...（长文本）
```

如果 payload 不能/无需解析：

```text
size = 1234
<PayloadLength> = 1234
<Note> = Payload is not parsed because ...
```

### 验收

- 任意原始字段都不以 `<` 开头。
- 任意 `<...>` 条目都能说明“为什么不是原始值”。
- 字段顺序：原始字段在前，派生/解释在后，诊断最后。

---

## D-006 只做原始解析

### 决策

坚持旧项目的产品边界：

- 只按格式规范切分容器/块
- 只提取字段
- 只做必要的人类可读解释
- **不做** payload 解码、校验、转封装、播放、渲染

### 实现含义

- 不实现视频/音频解码。
- 默认不实现压缩数据解压；例外见 D-011：体量小、压缩/变换简单的元数据/文本可以解码并显示。
- 大块 payload 仍然不解码，例如 PNG `IDAT`/`fdAT`、FBX deflate 数组、JPEG entropy scan。
- 不实现完整 CRC/校验（如果是单块内校验且为块本身值，比如PNG的CRC32，则需要校验；但如果某校验值跨多块或需要解码内容，与其他规则冲突，则不校验）。
- 不实现“修复文件”。
- 不实现“导出为其他容器”。

### 验收

- 新功能若需要解码 payload，默认拒绝。
- 已确认的例外：
  1. ID3/EXIF 这类“嵌套”的关键简易元数据格式，以及 ICC profile（D-036），需要解析。
  2. D-011 允许的小体量、简单压缩/变换的元数据/文本，例如 PNG `iCCP`、`zTXt`、压缩 `iTXt`。
- 例外不适用于大块数据：PNG `IDAT`/`fdAT`、FBX deflate 数组、视频/音频帧等仍然只记录范围与长度。
- 解析器只读取容器结构所需的字节；解压必须有输出上限并支持取消。

---

## D-007 GUI 全英文，文件内容语言保留

### 决策

- GUI 标签、菜单、状态、错误摘要：**全英文**。
- 文件内容中的中文/其他语言：按文件声明的编码原样保留，不翻译、不强制 ASCII。
- 不因为 GUI 是英文，就把文件里的本地化文本转成英文。

### 实现含义

- UI 字符串集中管理，默认英文。
- 字段值使用格式声明的编码：
  - ASCII / Latin-1 / UTF-8 / UTF-16 等按规范
  - 无法确定编码时，保留原始字节的可逆表示，并在诊断中说明
- 不把非 ASCII 字符替换成 `?` 或 `.`（Hex ASCII 区域除外）。
- 导出报告保留原始文本；必要时同时给出转义形式。

### 验收

- 中文标题、歌词、注释、文件名在字段列表中正确显示。
- GUI 不出现中文标签（除文件内容本身）。
- 编码错误有诊断，不静默乱码。

---

## D-008 全局读取/导出内存限制

### 决策

`ParseLimits` 只保留**单个字段值与表格行**的内存保护，不再包含任何解析性能/结构限制（D-032）。默认值声明在 Core 的 `ParseLimits` 中；解析器只能通过 `ParseContext.Limits` 读取，不允许在格式内部另建不可配置的硬编码上限。格式探测窗口与长时间读取的进度阈值固定为 512 KiB，不属于 `ParseLimits`（D-037）。
允许外挂 JSON 覆盖：

- 默认文件名：`universalparser.limits.json`
- 默认位置：应用程序可执行文件旁边
- 只覆盖文件中显式出现的属性；未出现项保持内置默认值
- 加载后必须通过 `ParseLimits.Validate()`
- 配置损坏时不得静默使用半套值；启动层应通过 `ParseLimitsConfiguration.TryLoad` 取得错误并提示，或明确回退到默认值

首版不做“按格式 id 覆盖”。如果未来需要，必须先改本文件并说明理由。

### 默认值

```text
MaxStringBytes  = 4 MiB
MaxBlobBytes    = 256 MiB
MaxDataLines    = 10,000
```

### 原因

- 这些限制防止单个超长字符串、Blob、表格或导出文件把内存撑爆，属于读取安全，不是“防解析爆炸”。
- 旧项目不同 parser 各自使用不同上限，难以审计；统一放在 Core 后，新增格式不会重复发明限制。
- 外挂 JSON 让用户不必重新编译即可处理特殊大文件；默认值仍保持可预测。

### 实现含义

- `ParseLimits.Default` 是唯一内置默认值来源。
- `ParseLimitsConfiguration` 负责 JSON 读取、部分覆盖和校验。
- `ParserEngine.Parse` 的 `limits` 参数仍可显式传入；未传入时使用 `ParseLimitsConfiguration.Current`（进程内只加载一次，缺失/损坏时回退默认值）。
- GUI 启动时应加载配置并把最终 limits 写入根节点元数据/报告。
- 树深度、子节点数、节点总数与解析时长不受 `ParseLimits` 约束。

---

## D-009 ASF Data Packets 不是 GUID object 层

### 决策

ASF 的树只按 GUID object 切分。Data Object 内部的 Data Packets 不作为独立节点展开，不解析 packet header、长度、时间戳或 payload；每个 Data Object 只建立一个 `Data Packets` 的 `Unparsed` 区域。

### 原因

- ASF Object 由 16 字节 GUID + 8 字节 `Object Size` 定界；Data Packet 嵌入在 Data Object payload 内部，不是 GUID 定界结构。
- packet 层属于高熵实时数据；按 object 树逐 packet 展开会制造大量节点，并把 payload 误当结构。
- 产品边界只做原始解析，不解码/校验/转封装 payload。
- 单区域表示仍满足“无不可见空洞”：Data Object 的 payload 范围被一个显式 `Unparsed` 子节点完整覆盖。

### 实现含义

- `Data Packets` 节点类型：`NodeKind.Unparsed`。
- 稳定 ID：`asf:data-packets:{offset}`。
- `ParseNode` 只返回范围、`<UnparsedLength>` 与 Note，不读取 packet 内容。
- `Object Size = 0` 仅允许 Data Object（Broadcast），范围延伸到文件末尾；其它 object 报 `INVALID_LENGTH`。
- 后续如果要做 packet 级分析，必须新增独立功能/视图，并先修改本决策；不能悄悄把 packet 塞进 object 树。

---

## D-010 FBX 数组属性不解压

### 决策

二进制 FBX 的数组属性（`f`/`d`/`l`/`i`/`b`）只按文件中的声明展示，不调用 zlib/deflate 解压：

- `ArrayLength`、`Encoding`、`CompressedLength` 原样显示。
- `Encoding = 0`（原始数组）显示前 16 个元素预览。
- `Encoding = 1`（deflate）只显示长度与 Note，不展开元素。
- D-011 的“小体量简单压缩数据”例外不适用于 FBX 数组：几何数组通常很大，解压代价高。
- footer 不再全部当黑盒：按 D-029 的可验证观察结构显示 footer id、版本回显和尾部签名；
  footer id 的派生规则未知，只作为不透明字节展示，不猜测语义。

### 原因

- 产品边界是原始解析，不做 payload 解码；几何数组属于大块数据。
- 解压会引入额外内存/CPU，并让“原始字节”与“解释值”的边界变得模糊。
- 长度字段足以完成结构故障分析；需要元素级分析时应新增独立视图，而不是塞进默认字段列表。

### 实现含义

- `Property[i].UncompressedDataLength` 是派生值（`ArrayLength × 元素大小`）。
- `Encoding = 0` 的 `CompressedLength` 与派生长度不一致时报告 `SCHEMA_MISMATCH`。
- `Encoding` 不是 0/1 时报告 `INVALID_ENCODING`，按 `CompressedLength` 跳过。
- footer 不再整块不解释：按 D-029 的可验证观察结构显示 footer id、版本回显和尾部签名；
  footer id 的派生规则未知，只作为不透明字节展示，不猜测语义。
- 后续如果要做数组解压，必须先修改本决策并说明内存上限。

---

## D-011 小体量、简单压缩/变换的数据可解码

### 决策

默认仍然不解码高熵 payload，但允许对**体量小、压缩/变换简单、且属于元数据/文本**的数据解码并显示。

首批适用：

- PNG `iCCP`：解压 ICC profile，并用完整 ICC profile 解析器解析（D-036）。
- PNG `zTXt`：解压文本，按 ISO-8859-1 显示。
- PNG `iTXt`（`compression flag = 1`）：解压文本，按 UTF-8 显示。

仍然不解码：

- PNG `IDAT`/`fdAT`：图像/帧像素数据。
- FBX deflate 数组：见 D-010。
- 视频/音频帧、JPEG entropy scan、其他大块压缩 payload。

### 原因

- 这些数据是元数据或文本，体量通常很小，解码后能显著提升故障分析价值。
- 压缩方式单一（zlib/deflate），实现简单，不需要引入完整编解码器。
- 通过 `ParseLimits` 设置输出上限后，风险可控，不会改变“原始解析器”的产品定位。

### 实现含义

- 解压必须有输出上限：文本使用 `MaxStringBytes`，其他元数据使用 `MaxBlobBytes`。
- 解压过程必须支持取消；超过上限时停止，只显示前缀并报告 `LIMIT_STRING` / `LIMIT_BLOB`。
- 解压失败报告 `INVALID_ENCODING`，保留原始压缩长度，不伪造内容。
- 原始压缩字节仍然可见；解压结果是派生字段或 Note，不替代原始长度字段。
- 不得因为本例外把大块 payload 整块读入内存。

### 验收

- 解压输出不会超过配置上限。
- 解压失败不抛异常、不产生假数据。
- 大块 payload（IDAT/fdAT/FBX 数组）仍然只记录长度与范围。

---

## D-012 GUI 保持旧版视觉，但用懒加载/虚拟化重做性能路径

### 决策

- 视觉严格沿用旧版 `Form1`：窗口 `1264x681`、`AutoScaleMode.Font`、左侧 300px 树、右侧面板位置、`Fira Code` 等宽字体（未安装时回退 Consolas/Cascadia Mono）、相同的菜单与上下文菜单文案。
- 结构仍用 WinForms `TreeView`，但**只做按节点懒加载**：初始只创建根与第一层节点，展开时才创建子节点；一个节点展开时一次性创建它的全部子节点，不做分页。
- `Expand All` 改为分批异步执行，每 200 个节点让出 UI，可随时取消；不再设置节点数上限。
- 字段列表使用 `ListView.VirtualMode`，只创建可见行；高度按行数增长但上限 1000 行，超出部分用滚动查看。
- Hex 按 16 字节/行、只渲染可见行；偏移 64 位；行数超过 `int` 时滚动条映射到 1,000,000 个位置，不直接绑定 `int.MaxValue`。
- 根节点显示文件级元数据，右侧不再空白；当前字段范围以 D-025 为准。
- 节点解析放到后台线程，按“加载代次 + 选择代次”丢弃过期结果；关闭/重开文件时取消并释放旧 source。
- 导出：`Export Raw` 流式复制范围；`Export Analysis` 使用 `ForensicReportWriter` 写纯文本报告，默认 Hex 最多 4096 行，可取消、有进度、文件名 sanitize。
- 正式 UI 不显示调试信息（加载耗时、控件高度等）；状态栏只显示业务状态、诊断数量或导出结果。

### 原因

- 旧版 `TreeView.ExpandAll()`、`MainStage` 上的同步 `ParseNode`、共享 `FileStream.Position` 在大文件上会明显卡顿或出错。
- WinForms `TreeView` 没有真正的虚拟化 API；懒加载 + 分批展开是最小风险的等价方案。
- `ListView.VirtualMode` 与只渲染可见 Hex 行可以在不改变视觉的前提下把内存与绘制成本压到常数级。
- 保持旧布局/字体可以降低用户迁移成本，同时修掉旧版的跨线程、重入、旧结果残留与 64 位截断问题。

### 实现含义

- 不得使用 `Application.DoEvents()`、不得关闭 `CheckForIllegalCrossThreadCalls`。
- GUI 只持有 `ParseSession`（source + document），不持有 `FileStream.Position`。
- 关闭文件必须取消所有后台任务并释放 source、清空树/字段/Hex/节点缓存。
- 解析异常或取消后不得显示上一个节点的结果。
- Hex/报告读取通过 `IRandomAccessSource`，不共享可变流位置。

### 验收

- 打开、关闭、重复打开同一文件不泄漏句柄，且不残留旧结果。
- 大文件（树大、节点大）选择节点时 UI 不冻结；`Expand All` 可停止。
- Hex 偏移与 source range 一致；超过 4GB 时显示 16 位偏移。
- 导出可取消，取消后不留下完整假报告（部分文件删除）。
- 视觉与旧版一致：菜单、控件位置、字体族/字号、两列字段列表、16 字节/行 Hex。

---

## D-013 节点标题与树名分离，身份字段不重复

### 决策

- 右侧详情面板标题统一为 `(OptionalMarker)NoSpaceTitle 'RawID'`；左侧树只显示 `ProcessedTagId`（最接近的已处理语义名）。
- `OptionalMarker` 只用于私有子项，例如 QuickTime 的 `(QuickTime)`；标准 ISO box 不加。
- `RawID` 必须与文件字节一一对应：
  - 可打印单字节原样输出，包括 4CC 末尾空格
  - 不可打印字节用 `\xNN`
  - GUID 用大括号，同 ASF
  - 部分 varint ID 用 `0x...`，同 EBML
- 已经出现在标题里的身份值（4CC / GUID / marker / 节点名 / Element ID）不再作为字段重复输出。

### 原因

- 旧版树与标题混用可读名和原始 ID，部分格式还在字段区重复同一身份值，信息密度低且难以对照字节。
- 标题需要可复制、可逆的原始标识；树需要快速语义定位，两者职责不同。

### 实现含义

- 共享 `RawIdentifier.FormatBytes` / `FormatText` / `FormatGuid` 生成 `RawID`；各格式 `GetTitle` / `BuildTitle` 统一调用。
- 每个格式的标题函数只负责 `NoSpaceTitle` 与 marker，不拼接 `[truncated]` 等状态。
- 删除重复身份字段：PNG `Chunk Type`、FLV `Signature`、ASF `Object GUID`、JPEG `Marker`/`<MarkerName>`、FBX `Name`、EBML `<ElementID>`。
- 原始 `size` / `length` / version 等结构字段不受影响，仍需保留。

### 验收

- 同一节点：树名是语义名或 4CC，右侧标题是 `NoSpaceTitle 'RawID'`。
- `©nam`、`\xFF\xD8`、`{GUID}`、`0x18538067` 分别按规则显示。
- 字段列表不出现标题已包含的身份值。

---

## D-014 格式显示名使用全称

### 决策

- GUI 文档根节点、右上角文件标题、报告 `Detected format` 使用格式全称，不使用缩写。
- 全称固定为：
  - `ISO Base Media File Format`
  - `Resource Interchange File Format`
  - `Extensible Binary Meta Language`
  - `Flash Video`
  - `Advanced Systems Format`
  - `Filmbox`
  - `Portable Network Graphics`
  - `JPEG Image`
  - `Musical Instrument Digital Interface`
  - `Adobe Photoshop Document`
  - `Ogg Encapsulation Format`
  - `Free Lossless Audio Codec`
  - `Blender File`
  - `Protocol Buffers`
  - `Named Binary Tag`
  - `Raw Data`
- parser id 保持稳定小写（`isobmff`/`riff`/`ebml`/`flv`/`asf`/`fbx`/`png`/`jpeg`/`midi`/`psd`/`ogg`/`flac`/`blend`/`protobuf`/`nbt`/`raw`）。

### 原因

- 缩写对不熟悉格式的用户不可读；旧版根节点/标题只显示 `ASF`、`RIFF` 等。

### 验收

- 打开文件后，文档根节点与右上角标题显示全称；报告 `Detected format` 显示全称 + id。

---

## D-015 加载进度必须持续反馈

### 决策

- 检测、解析、导出阶段必须持续上报进度。
- 解析循环按已处理偏移上报；`ParseContext.ReportProgress` 合并到 100ms 一次，最终位置必达。
- 在首个真实进度到达前，进度条使用 Marquee 动画，不允许停在 0。

### 原因

- 只在上层偶尔上报时，长文件加载会长时间停在 0%，用户会以为程序卡死。

### 验收

- 打开大文件时进度条持续移动，信息区显示已处理/总量/速度。
- 小文件快速完成；不出现“0% 卡住”。

---

## D-016 AVI `LIST:movi` 整体不解析

### 决策

- RIFF/AVI 中 `LIST:movi` 整个块作为 `NodeKind.Unparsed`，不创建子节点。
- 节点范围仍覆盖 header + type + payload + pad；字段显示 `ckSize`、`listType`、`<UnparsedLength>`、`<PayloadLength>` 和说明 Note。

### 原因

- `movi` 是视频数据块序列，解析器本来就不解释内容；展开会产生大量无用节点和性能开销。

### 验收

- AVI 树的 `LIST:movi` 无子节点；树覆盖校验通过；Hex 可查看整个 `movi` 范围。

---

## D-017 部分实现/未实现区域用 `<Note>` 标注

### 决策

- 不新增 GUI 标记控件、不新增节点 Kind。
- “已知但未展开”的区域（例如 ASF Index Blocks、加密对象、ISOBMFF 部分 codec 内部）统一在字段区追加一条
  `<Note> = ...`，说明该区域属于“已知但本版本未实现”还是“规范本身不可再分”。
- 未知 4CC / 未知 GUID / 未知 box 仍按原规则整块 `Unparsed`，不使用 `<Note>` 冒充“已解析”。

### 原因

- 用户需要区分“格式本来如此”和“解析器还没做”，但不应为此引入新的 UI 概念或额外点击。

### 验收

- 选中未实现对象时，字段区最后一条是 `<Note>`，内容明确写出未实现范围。
- 不出现新增的树图标、颜色或第二列状态标记。

---

## D-018 内嵌 EXIF 与 ID3v2 完整解析

### 决策

- EXIF（PNG `eXIf`、JPEG APP1 `Exif\0\0`）完整解析 IFD0 → IFD1、Exif IFD、GPS IFD、Interop IFD。
- ID3v2（RIFF WAV `id3 `）完整解析 v2.2/v2.3/v2.4：tag header、extended header、footer、unsynchronisation、全部已支持 frame。
- 实现移植自 `reference/EXIF` 与 `reference/ID3`，保留其字段名与解释风格；适配到 `IRandomAccessSource` + 有界读取 + `FieldList`。
- MakerNote 内容、APIC 图片字节、缩略图字节只标注类型与长度，不解码图像。
- IFD 遍历使用固定深度（6）/ 总数（32）/ 链长（8）防循环；字符串与 Blob 上限来自 `ParseLimits`。

### 原因

- 这两个是“内嵌二进制片段”，不是容器，属于原始解析范围内必须展开的元数据。

### 验收

- `fixtures/jpeg/exif.jpg` 能列出 IFD0/Exif/GPS 标签及解释值。
- 构造的 WAV + ID3v2.3 样本能列出 tag header 与 frame。
- 损坏/循环 IFD、非法 frame 长度只产生诊断，不抛异常。

---

## D-019 ISOBMFF 补齐范围

### 决策

- 补齐：`esds` 的 DecoderSpecificInfo / AudioSpecificConfig 完整字段；H.264 `avcC` 中 SPS/PPS 的 profile/level/宽高；全部 FLAC metadata block（不只 STREAMINFO）；HEIF `fdel`；`alis` 的 tag 类型名。
- 不为了“完整规范”继续展开冷门 codec 私有 payload；未覆盖部分按 D-017 加 `<Note>`。
- 字段名以 ISO/IEC 14496-12/15、ISO/IEC 14496-10、FLAC 规范、Apple QuickTime 文档为准；查不到官方名时用描述性英文键。

### 原因

- 这些字段在故障分析中会被直接查看，且旧版解析器已有相近能力。

### 验收

- `esds` 输出 objectTypeIndication、streamType、bufferSizeDB、maxBitrate、avgBitrate、audioObjectType、samplingFrequency、channelConfiguration。
- `avcC` 输出 profile_idc、level_idc、SPS 解析出的宽高与 frame_mbs_only_flag。
- `fdel` 输出完整字段；`alis` tag 显示语义名而非 `tag=0x...`。

---

## D-020 未完成下载/零填充文件的保护

### 决策

- 任何“读到全零 header”的容器循环都必须终止，而不是按 8/12 字节步进继续造节点。
- RIFF：`id == 0` 且 `size == 0` 视为零填充，追加一个 `<zero fill>` 节点覆盖剩余范围并停止。
- PNG：chunk length 与 type 全零视为零填充，追加 `<zero fill>` 并停止。
- 其他格式保持 `next <= position` 的推进检查；缺失的补上。
- 不要求对 >4GB 或随机 fuzz 做自动化验收；零填充场景必须有自动化测试。

### 原因

- 多线程下载器中断后，文件尾部常是大片 0x00；逐块推进会在节点上限前制造数百万空节点。

### 验收

- 合法 header + 约 1 MB 全零的每种格式样本都能在 1 秒内完成，节点数很小，并有 `<zero fill>` 或 `<unparsed data>` 诊断。

---

## D-021 GUI 复制、Hex 选区与统计视图

### 决策

- 字段列表右键菜单：`Copy Key`、`Copy Value`、`Copy Row`。
- Hex 支持鼠标拖选；右键 `Copy Selection`、`Copy as Hex`、`Copy as ASCII`；无选区时复制当前行。
- 不做字段搜索/过滤（语义难以定义）。
- 新增独立“Statistics”弹窗：按节点体积排序、按同名节点数量排序等基础统计。
- Hex 滚动条：`Maximum = ScrollSteps + LargeChange - 1`，映射用 `Int128` 计算，保证最后一行可达。

### 原因

- 复制是故障分析最高频操作；统计视图能快速定位异常大块/重复块。

### 验收

- 超大节点滚到底时最后一行完整可见，偏移正确。
- 复制内容与 source range 字节一致；统计窗口不阻塞主窗口。

---

## D-022 明确不做的功能（本版本边界）

### 决策

- 不输出 JSON 报告；TXT 报告即可。
- 不计算 SHA-256。
- 不做 ASF Data Packet 独立视图。
- 不做 FBX deflate 数组元素级视图。
- JPEG APPn 按 Identifier 识别；本版本启用 Exif、ICC profile、XMP 文本和 Photoshop 8BIM 资源块解析，其余保留 `Unparsed`（D-023 / D-036）。
- RIFF 的 GM.DLS 内部块按“尽可能支持”处理；查不到规范的私有块保持 `Unparsed`。
- 其他容器型格式（如 MP3 裸流）留待后续版本；MIDI 已由 D-035 新增，不作为本条的暂缓项。

### 原因

- 这些项对当前故障分析价值低或成本高，用户已明确不要求。

### 验收

- 上述区域不出现“未实现”缺陷；需要说明时使用 D-017 的 `<Note>`。

---

## D-023 JPEG APPn 使用 Identifier dispatcher

### 决策

- 所有 APP0–APP15 共用可扩展的 Identifier 注册表，内容类型由签名决定，不由 APP 序号决定。
- 注册 `Exif`、`ICC_PROFILE`、`Photoshop 3.0`、`Adobe`、`http://ns.adobe.com/xap/1.0/`，并记录已有的 `JFIF` / `JFXX` 标识。
- 内容 handler：
  - `Exif`：完整 TIFF/IFD 解析。
  - `ICC_PROFILE`：读取 marker sequence number 与 marker count；仅当 `marker count == 1` 时按完整 ICC profile 解析，多段 profile 不跨段拼接（D-036）。
  - `http://ns.adobe.com/xap/1.0/`：XMP 本身是可读 XML 文本，不解析 XML 结构；直接把整段文本作为原始文本字段返回，使该 APP 段不再显示为 `Unparsed`。
  - `Photoshop 3.0`：按 Adobe Photoshop File Formats Specification 解析 8BIM image resource block 列表；
    常用资源解析字段，其余资源只显示 ID / Name / Size 与“未解释”Note；不解析像素、路径点或缩略图图像。
- 其它标识只显示 Identifier / 类型与未解析范围，节点保持 `Unparsed`。
- Identifier 通常是 NUL 结尾字符串；Exif 的第二个零字节属于其包装头，必须跳过后再进入 TIFF。
- Adobe TN 5116 的 `Adobe` 是固定 5 字节签名，没有 NUL 终止符；后续两字节是版本号，须完整保留在未解析范围内。
- 删除“APP1 不是 Exif”之类 Note。合法的 XMP / ICC / Photoshop APP 段不因为未识别内容而产生诊断。
- Identifier 读取受 segment 范围、`MaxStringBytes` 与取消令牌限制；读不完整时不 dispatch，完整原始数据仍可查看。
- XMP 文本读取受 `MaxStringBytes` 限制；超限时只显示前缀并报告 `LIMIT_STRING`，节点仍可查看完整 segment Hex。
- 8BIM 资源块循环受 `MaxDataLines` 与取消令牌限制；资源长度越界或结构不完整时报告诊断并保留已解析资源。

---

## D-024 Hex 坐标与 Statistics 交互

### 决策

- Hex 表头、文字、选区与鼠标命中共用列布局；字体测量与绘制使用相同的 GDI 文本参数，字体 / DPI 改变后重新计算。
- 字节跨度 → 列跨度只允许通过 `HexDumpFormatter.GetColumnSpan` 计算，选区绘制不得自行做 `first * 3` 之类的推导。
- `Statistics(&S)` 提供菜单访问键，并使用 `Ctrl+Shift+S` 直接打开；图标与主窗口一致。
- Statistics 提供彩色二维饼图和对应的数值 / 百分比图例，鼠标悬停时扇区与图例联动高亮，并显示详细数值。
- 文件体积占比按节点独占字节汇总，避免把父节点与子节点范围重复累加；节点数量占比不计合成文档根。
- 最大节点与同名节点排名继续提供可复制的精确列表；统计在后台计算并可随窗口关闭取消。

---

## D-025 根节点只显示文件系统信息和探测结果

### 决策

- 根节点字段严格按顺序保留：`File name`、`File size`、`Creation time`、`File attributes`、`Detected format`、`Probe confidence`。
- 创建时间和属性来自打开的磁盘文件句柄，在后台打开文件时取得；不是 Exif / 容器内嵌时间。
- 创建时间使用明确标注的 UTC；文件属性显示 Windows 属性名称与原始十六进制 flags。
- 删除根节点字段中的 Top-level nodes、Diagnostics、Errors、Warnings、Limit reached 及所有 limits 数值。

---

## D-026 进程内视图设置与统计交互

### 决策

- `Settings(&S)` 提供两个只在本进程内生效的选项，不写配置文件，退出后恢复默认：
  - `Percentage decimals`：1、2、3、4、5、6，默认 **3**。
  - `Top node names`：3、5、8、10，默认 **10**（列表最大项）。
- 百分比小数位是全 GUI 的唯一来源：`UniversalParser.Core.Formatting.ValueFormat.PercentageDecimals`。
  统计数据、解析器字段值与导出报告中的任何百分比都必须经 `ValueFormat.Percentage` 格式化，
  不允许在调用点硬编码 `P1`。
- Top N 只影响 Statistics 的 `File composition` 饼图：最多 N 个条目（含聚合的 `Other names`）；
  名称数量不超过 N 时不出现 `Other names`。
- Statistics 只保留功能性说明：`Largest nodes` 的 `Sizes include child ranges…` 提示保留；
  饼图说明与悬停提示等装饰性文案删除。
- `Largest nodes` 的 `Node type` 列与类型复选框：默认全选；只勾选部分类型时，仅这些类型的节点参与排序；
  全选时等价于全部节点参与排序。为保证筛选后仍是真实前 200 名，Core 按 `NodeKind` 分别保留前 200 个候选，
  UI 合并选中类型后再排序截断。
- `Largest nodes` 的行唯一对应一个节点，因此双击、右键 `Go to node` 或 Enter 会关闭统计窗口，
  并在主窗口树中展开、选中该节点。`Repeated names` 不提供导航：同名节点不唯一，无法确定目标。
- `Repeated names` 列出全部不同名称；`Count = 1` 的行用浅灰色显示，表示该名称只出现一次。

---

## D-027 菜单快捷键与树图标

### 决策

- 每个菜单命令都要有快捷键，并在菜单中显示，不要求用户先打开菜单再按字母：
  `Ctrl+O` Open、`Ctrl+W` Close、`Ctrl+Shift+E` Expand All、`Ctrl+Shift+C` Collapse All、
  `Ctrl+Shift+S` Statistics。
- 树图标唯一来源是 `src/UniversalParser.App/Assets` 的 48 px PNG，作为嵌入资源随程序发布：
  `Root48`（根）、`Folder48`（未展开容器）、`OpenedFolder48`（展开容器）、`File48`（叶子）、
  `Checked48`（选中）。不再在代码里绘制图标，也不依赖 resx 位图。
- 图标在启动时用高质量插值缩放到 16 px，保持旧版树行高。
- 根节点始终使用 `Root48`，展开/折叠不得改写它的图标。
- 任何节点被选中时都显示 `Checked48`；展开/折叠只改变未选中状态下的容器图标。

---

## D-028 诊断只针对格式本身，不做语义校验

### 决策

- 对每个块/box/chunk 只做**结构解析**：切分范围、读取字段、按规范解释本块内的值。
- 诊断只针对**格式本身**：
  - 截断、长度越界、长度溢出；
  - 签名/标识错误；
  - 块内字段宽度、版本、保留位、枚举值不符合格式定义；
  - 块内 CRC / 校验和；
  - 块内结构无法继续（例如缺少必需的子结构）。
- **不做跨块/跨字段语义校验**。只要格式本身解析无误，语义内容即使明显错误也不产生诊断。
  明确不做：
  - APNG 帧与默认图是否一致（P2-14）；
  - RF64 `ds64` table 条目是否等于真实数据量（P2-11）；
  - `mDCV` 是否与 `cICP` 配套、`cICP` 码点组合是否“合理”；
  - 调色板索引是否被像素数据实际使用、`tRNS` 与 `PLTE` 的语义匹配；
  - FBX `Connections` 引用的对象是否存在、FBX 属性语义是否自洽；
  - ASF Data Packet 的内容是否可播放。
- 有用的解释可以作为派生字段或 `<Note>` 显示，但不得升级为 `Warning` / `Error`。

### 原因

- 本项目是原始解析器，不是播放器、校验器或 payload 查看器（D-006）。
- 语义校验通常需要解码 payload、跨块建立对象图或引入规范之外的假设，容易产生误报。
- 故障分析首先需要确认“结构读对了吗”；把语义错误混进诊断会掩盖真正的格式故障。

### 实现含义

- 新解析器只增加格式级诊断；不为“看起来不对”的语义关系添加 `SCHEMA_MISMATCH`。
- 已有的块内枚举/长度/CRC 检查保留，因为它们描述的是格式本身。
- `01-open-issues.md` 的 P2-11、P2-14 以本决策关闭。

---

## D-029 FBX footer 的可验证观察结构

### 决策

- 用户补充：仅在查到稳定资料时完善 footer，否则保持 `Unparsed`。已查到可复核的 framing 资料，
  但没有完整 Autodesk 官方 footer 规范；本次只解析各来源共同确认的外层结构：
  - 顶层 null record（13 / 25 字节，属于 footer 范围）；
  - 16 字节 footer code/id（不透明原始字节，不是顶层 `FileId` 属性）；
  - 零填充（已验证的写法为 4–20 字节）；
  - 4 字节 version 回显（小端）；
  - 120 字节零；
  - 16 字节固定尾部签名 `F8 5A 8C 6A DE F5 D9 7E EC E9 0C E3 75 8F 29 0B`。
- footer 是显式节点，并拆成可验证的子节点：`Footer ID`、`Footer padding`、`Footer version`、`Footer trailer`。
- 不假设 version 一定 16 字节对齐：Blender 先写 4 字节零再对齐，SDK 样本可在对齐后再写 4 字节零，
  FbxWriter 使用固定 20 字节零。用已确认的固定尾部结构从末尾定界 version，保留全部 padding。
- 只有完整通过外层结构识别（尾部签名、零填充与范围符合这些已确认布局）才展开；
  任何一项不匹配时，整个 footer 保持一个 `Unparsed` 节点，并加 `<Note>`，不猜测字段。
- **不解密、不推导 footer code/id 的语义**。FbxWriter 有时间戳混淆算法，但 Blender 仍采用固定时间
  的 workaround，不能把该算法当成覆盖所有导出器的稳定规范。只展示原始 16 字节，不猜加密块内部字段。
- version 与文件头是否一致是语义关系，不检查（D-028）。
- 如果未来拿到官方规范，必须先改本决策再调整字段。

### 原因

- Blender 的公开说明明确写“footer 内容未知”；Autodesk 没有公开 footer 规范。
- 稳定参考：[Blender `encode_bin.py`](https://github.com/blender/blender-addons/blob/main/io_scene_fbx/encode_bin.py)、
  [FbxWriter `FbxBinary.cs`](https://github.com/hamish-milne/FbxWriter/blob/master/Fbx/FbxBinary.cs)。
  两者的 padding 不同，已通过仓库 Blender 7400 样本及有来源说明的 SDK 7500 样本复核。
- 采用“可验证才展开、否则整体回退”的方式，既提升故障分析价值，又不会把猜测写成事实。

### 验收

- `fixtures/fbx/Kirisame_Body.fbx` 能显示 footer id、version = 7400、尾部签名。
- 签名缺失、结构不匹配、长度不足时整体 `Unparsed`；version 值即使不同也只按原值展示。

---

## D-030 Phase 6 导出预算的落实

> **已被 D-034 取代（2026-11-17）。** 本节保留为历史记录；当前导出不再有总字节预算。

### 决策与实现口径（2026-11-17）

- 解析本身不设时间预算（D-032）；`ParseContext` 只在安全点检查用户取消。
- `MaxExportBytes` 是**整个导出文件**的预算，不是 Hex 源数据字节数：报告按 UTF-8 实际字节计数，
  包含调用方选择的 BOM、换行和 `LIMIT_EXPORT` 截断说明；常数级尾缓冲让完整报告恰好可在上限内完成。
- 报告超限保存明确标记的不完整报告，writer 返回字节数 / 是否超限，GUI 只显示截断 Warning，不显示“完整成功”。
- Raw 导出必须保持完整、逐字节等于 source range；范围超限时在写入前拒绝，让用户调整 JSON 上限，
  绝不静默生成截断且看似有效的二进制文件。读取提前 EOF 是错误，不上报虚假完成。
- 结构报告遍历只保存每个祖先的一个 continuation（`O(depth)` 内存），不一次压入所有兄弟节点。

### 验收

- ASCII/中文/emoji、自定义换行、UTF-8 BOM、极小预算与恰好等于上限的报告回归。
- Raw 精确范围、短读、提前 EOF、超限拒绝和分块取消回归。

---

## D-031 大文件顺序解析的读取窗口

### 决策

- EBML、JPEG、FBX 与 FLV 的建树阶段使用**固定 16 MiB 的顺序读取窗口**，把大量小 header 读取合并为大块 I/O。
  窗口只用于建树，不保存到 `ParseDocument`，解析结束即释放引用；不缓存、不复用、不跨文件保留。
- 用户允许以尖峰内存换磁盘吞吐，但硬性上限为 **16 GiB 物理内存，不可通过设置或 JSON 修改**。
  实际窗口固定为 16 MiB，远低于该上限；小于 1 MiB 的文件不启用窗口，避免无意义的分配。
  1–16 MiB 的文件窗口等于文件长度，峰值仍不超过 16 MiB。
- JPEG 熵编码扫描改为单次顺序扫描：缓冲区内一次处理 `0xFF` 填充、`FF00` stuffed byte 与
  `FFD0`–`FFD7` restart marker，不再因每个 stuffed byte 重读 64 KiB。
- EBML 的 `stableId` 按 element ID 缓存，避免为每个节点分配新的 `ebml:...` 字符串。
- EBML 的取消/进度检查按每 256 个元素一次执行。

### 原因

- 实测 JPEG 的瓶颈是每个 `FF00` 都重新读取 64 KiB，形成读放大；EBML 的瓶颈是每个元素一次
  小 `RandomAccess.Read`，在百万级元素文件上产生同等数量的系统调用。
- FBX 与 FLV 同样按“每条记录一个 header”扫描，磁盘读取实测仅约 7 MB/s；套用同一窗口后
  FBX 4.9 MiB 样本降到约 26 ms（约 178 MB/s）。
- 这些读取模式都与磁盘顺序读取能力无关，属于解析器自身的 I/O 放大。

### 验收

- 大 JPEG（64 MiB 扫描数据）、EBML（92 MiB / 190 万节点）、FBX（4.9 MiB / 百万级属性）与
  FLV（百万级 tag）回归；小于 1 MiB 的 fixture 仍走原路径。
- 超时/取消测试保持原语义：窗口不改变用户取消优先级，也不把取消伪装成解析失败。
- `BufferedReadSource` 的跨窗口读取、回退读取、超大请求绕过、`ReadExactly` 短读与 Dispose 回归。

---

## D-032 不设软件级解析性能限制

### 决策

- 移除 `MaxDepth`、`MaxChildrenPerNode`、`MaxNodes`、`MaxParseTime` 及其诊断码（`LIMIT_DEPTH` / `LIMIT_CHILDREN` / `LIMIT_NODES` / `LIMIT_TIME`）。
- 解析器不在格式内另设不可配置的深度、节点数或时间上限；遇到超大/恶意文件时，由用户取消、操作系统内存管理或进程终止兜底。
- 保留 `MaxStringBytes`、`MaxBlobBytes`、`MaxDataLines`（D-008），它们只约束单次读取的内存；导出总字节预算已按 D-034 取消。
- `ParseContext` 只保留取消检查与进度报告；`StatisticsSnapshot` 不再有节点预算或 `Truncated` 状态。
- GUI `Expand All` 不设节点数上限，仅分批异步执行并支持取消。

### 原因

- 用户明确选择“只移除性能类限制”，希望完整解析大型媒体文件，而不是在任意阈值处得到截断树。
- 性能限制与“原始解析、完整覆盖”目标冲突：截断会产生大量占位节点和额外诊断。
- 真正的安全边界是内存分配点（字符串/Blob/导出），而不是树结构本身。

### 验收

- 超深、超宽、超多节点的构造样本能完整建树；`dotnet test` 无 `LimitDepth` / `LimitNodes` / `LimitChildren` / `LimitTime` 引用。
- 取消仍能在建树与惰性节点解析中生效。

---

## D-033 EBML `SimpleBlock` / `Block` 按高熵数据处理

### 决策

- EBML 的 `SimpleBlock`（0xA3）、`Block`（0xA1）、`BlockVirtual`（0xA2）是交错音视频 payload，整块作为 `NodeKind.Unparsed`。
- 节点只保留范围、长度派生字段（`<ElementDataSize>` / `<UnparsedLength>` / `<PayloadLength>`）与一条短 Note；不解析 track number、时间码、flags、lacing 或帧数据，也不生成 256 字节内联预览。
- 其它未知/高熵 EBML 元素继续走既有 `Unparsed` 路径。

### 原因

- block 内容是高熵实时数据，逐帧展开既不符合“只切分容器”的产品边界，也会制造海量无意义节点。
- 用户明确取消旧的“256 字节内联显示”设计，避免把 payload 预览误当结构字段。

### 验收

- `SimpleBlock` / `Block` / `BlockVirtual` 节点类型为 `Unparsed`，字段中只有长度与 Note，无内联字节。
- 树覆盖校验仍通过，Hex 可查看整个 block 范围。

---

## D-034 报告导出取消总字节预算，Hex 固定首尾窗口

### 决策

- **彻底取消**旧的 `MaxExportBytes` 导出预算及其 JSON 覆盖、`LimitedReportWriter` 截断报告和
  `LIMIT_EXPORT` 路径。导出不再有总字节上限，也不在配置文件中定义。
- 报告中的二进制 Hex 段使用硬编码窗口：
  - 原始范围 `<= 256 KiB`：完整写入。
  - 原始范围 `> 256 KiB`：只写首 `128 KiB` 与尾 `128 KiB`；两段必须分开写入，中间以显式说明分隔。
  - 超限时报告必须包含一句人类可读的英文说明，指出完整二进制数据不在报告中，需要使用
    `Export Raw` 单独导出完整范围。
- “已解析键值对”（GUI 字段列表内容）**无论如何完整写入**报告，不设行数/字节数上限。
- “Structure”节点列表同样完整写入；不再保留结构行数上限。
- `Export Raw` 继续流式复制完整范围，不设导出预算；读取提前 EOF 仍是错误。

### 原因

- 故障分析报告的价值在于完整字段与结构；用总字节预算截断会丢掉真正有用的键值对。
- 二进制段往往远大于文本段，固定首尾窗口既保留头部签名/结构，也保留尾部异常，同时避免报告失控。
- 把“完整数据”交给 Raw 导出，报告与二进制各自承担明确职责。

### 实现含义

- `ParseLimits` 删除 `MaxExportBytes`；`ParseLimitsFile` 不再接受该属性（旧 JSON 中的该键被忽略）。
- `ForensicReportWriter.MaxHexHeadBytes` / `MaxHexTailBytes` 为固定常量 `128 KiB`，不读取配置。
- `ReportWriteResult` 返回 `(BytesWritten, HexTruncated)`；`HexTruncated` 只表示 Hex 段使用了首尾窗口。
- `RawRangeWriter.Write` 不再接收 `ParseLimits`，也不在写入前拒绝范围。
- 报告第 6 节改为 `Read Limits`，只列出读取限制。

### 验收

- 25,000 条字段全部出现在报告中。
- `256 KiB` Hex 完整输出且无截断说明；`300 KiB` Hex 输出首尾两段、中间省略说明和 `Export Raw` 提示。
- Raw 导出精确等于 source range，且不再因大小被拒绝。

---

## D-035 新增 Standard MIDI File 支持

### 决策

- 新增 `.mid` / `.midi` 的 Standard MIDI File 解析器：parser id `midi`，显示名
  `Musical Instrument Digital Interface`。
- 树结构：`MThd` header chunk + 每个 `MTrk` track chunk；track 内**每个事件都是独立子节点**，
  按文件顺序完整列出，不合并、不摘要。
- 事件字段逐行显示：`delta_time`、`status`、`meta_type`/`length`、通道事件数据、meta 数据。
  不把整条事件压成一行文本；只有文件本身把多个值编码进同一字段时才在同一行说明。
- 支持 running status、SysEx / SysEx escape、全部 channel voice 消息、全部标准 meta event。
- 文本类 meta event 按 Latin-1 解码并转义控制字符；Set Tempo、Time Signature、Key Signature、
  SMPTE Offset 提供派生的人类可读解释。
- SysEx / Sequencer-Specific payload 只显示长度与 Note，不解释内容；完整字节仍可通过 Hex / Raw 查看。
- MIDI 文件通常较小且不压缩，不引入额外性能优化；以完整、可读为首要目标。

### 原因

- 用户明确要求新增 MIDI 支持，并强调“必须完整解析和列出数据（如音符事件表）”。
- 事件级节点让音符、控制器、节拍和文本 meta 事件都能逐条查看，符合原始解析器定位。

### 实现含义

- 新增 `Formats/Midi/`：`MidiParser`、`MidiDocumentParser`、`MidiEventScanner`、`MidiNodeParser`、
  `MidiFormatting`、`MidiConstants`。
- 未知 chunk 整块 `Unparsed` 并报格式诊断；零填充、截断 chunk、缺失 End of Track 均有诊断。
- 不把 track payload 复制进树；建树只扫描事件边界，选中事件时再读取该事件数据。
- 不校验 `ntrks` 与实际 track 数等跨块语义（D-028）。

### 验收

- `fixtures/midi/MUSIC_01.MID` 与 `Saigon_Neon.mid` 解析无 Error；所有 track 都有事件节点。
- 合成样本能列出 `TrackName`、`SetTempo`、`NoteOn`、`NoteOff`、`EndOfTrack`，并正确解释
  running status 与 SysEx 边界。

---

## D-036 内嵌 ICC profile 完整解析

### 决策

- PNG `iCCP` 解压后按 ICC.1:2022（profile version 4.4.0.0）解析完整 profile，不再只显示头字段。
- JPEG APP2 `ICC_PROFILE` 在**单段内**解析：读取 marker sequence number 与 marker count，再按同一 ICC 解析器解析该段内的 profile 数据。
- 多段 ICC profile（`marker count > 1`）**不跨段拼接**：只显示分段框架、段内长度与 Note，段内容保持 `Unparsed`。跨块解析仍然禁止。
- 解析范围：128 字节 profile header、tag table，以及常见自描述 tag type：
  `XYZ `、`curv`、`para`、`text`、`desc`、`mluc`、`sig `、`sf32`、`ui32`、`ui16`、`ui08`、
  `dtim`、`meas`、`view`、`chrm`；LUT 类（`mft1` / `mft2` / `mAB ` / `mBA ` / `mpet`）
  只解析结构、矩阵与网格声明，不展开 LUT 采样点。
- 未知 tag type 保持 `Note` + 原始字节可见（D-017）。
- 不应用颜色变换，不做 profile 有效性或跨 tag 一致性校验（D-028）；只报告块内格式问题。

### 原因

- ICC.1:2022 是公开、稳定的官方规范；JPEG 嵌入分段规则由 ICC 嵌入规范与 libjpeg `jcicc.c` 明确。
- ICC profile 是影响颜色显示的关键元数据；header + tag table + 常见 tag type 的解析难度与 EXIF 相当，价值高于只显示头字段。

### 实现含义

- 新增 `Formats/Embedded/IccProfileParser.cs`、`IccFormatting.cs`、`IccCursor.cs`。
- PNG `iCCP`：解压上限沿用 `MaxBlobBytes`（D-011）；解压后交给 `IccProfileParser`。
- JPEG APP2：仅当 `marker count == 1` 时把该段剩余字节作为完整 profile 解析；编号越界或多段时保留分段框架与 `Unparsed`。
- tag 数据范围、字符串长度与数组元素数都受 `ParseLimits` 与取消令牌限制；越界只报诊断，不跨块读取。

### 验收

- PNG `iCCP` 能显示 header、tag table 与常见 tag type 字段。
- JPEG 单段 `ICC_PROFILE` 能显示分段编号与完整 profile；多段 profile 不拼接、不产生假字段。
- 真实系统 profile 解析不出现 Error 诊断。

---

## D-037 探针窗口与长时间读取使用固定 512 KiB 阈值

### 决策

- 从 `ParseLimits` 与 `universalparser.limits.json` 中彻底移除 `MaxProbeBytes`；旧配置里残留的该属性被忽略。
- 格式探测窗口、PNG chunk CRC 等可能处理较大字节范围的操作，统一使用**硬编码 512 KiB** 阈值，不可配置。
- 低于 512 KiB：直接执行，不显示额外进度。
- 高于 512 KiB：代码继续执行，不截断、不跳过、不产生“超过限制”Note；同时通过
  `ParseContext.ReportProgress` 复用 GUI 加载进度条，显示正确的 processed/total，让用户看到程序仍在工作。
- PNG 所有 chunk（含 `IDAT`/`fdAT`）在选中节点时都流式计算 CRC；只有取消令牌能中断。

### 原因

- `MaxProbeBytes` 原本是探测窗口大小，却被 PNG CRC 当成“超过就不校验”的门槛，导致大块只显示存储值并产生误导性 Note。
- 该值不是内存保护：CRC 是流式计算，内存占用固定；用 512 KiB 作为“是否需要显示进度”的分界，比阻止校验更符合原始解析器定位。
- 进度条已经存在，复用它可以覆盖长时间 CRC，而不需要新的配置项或新的弹窗。

### 实现含义

- 新增内部常量 `ParseThresholds.LargeOperationBytes = 512 * 1024`，不进入 JSON、不写入报告。
- `IsoBmffParser.Probe` 使用该常量作为探测窗口；探测只读 box header，不扫描整段数据。
- `PngCrc32.Compute` 接受进度回调，按 64 KiB 缓冲区推进并报告实际处理字节数。
- PNG 节点解析在超过阈值时报告 `PNG CRC` 进度；GUI 在首次收到进度时显示加载浮层，结束后隐藏。
- 报告 “Read Limits” 段不再输出 `MaxProbeBytes`。

### 验收

- 大 PNG 的 `IDAT` 或大 chunk 能显示计算后的 CRC 与 `CRC_MISMATCH`，不再出现 `MaxProbeBytes` Note。
- 超过 512 KiB 的 CRC 期间进度条持续更新并到达 100%，取消可中断。
- 旧 `universalparser.limits.json` 含 `maxProbeBytes` 时不影响加载。

---

## D-038 Adobe Photoshop PSD/PSB 支持与共享 8BIM 解析

### 决策

- 新增 `.psd` / `.psb` 支持：parser id `psd`，根节点显示名 `Adobe Photoshop Document`；PSD version 1 与 PSB version 2 由同一个 parser 处理。
- PSD/PSB 与 JPEG APP13 `Photoshop 3.0` 使用**同一个** `PhotoshopImageResources` 8BIM image resource 解析器；PSD `ImageResources` 的每个 block 作为子节点，JPEG 仍在 APP13 节点内以 `Resource[n].` 字段列出。
- PSD 树按规范切分：`FileHeader`、`ColorModeData`、`ImageResources`、`LayerAndMaskInformation`、`ImageData`。`LayerAndMaskInformation` 下再分 `LayerInfo`（`LayerCount`、`LayerRecords`、`ChannelImageData`）、`GlobalLayerMaskInfo` 和 additional layer information block；每个 `LayerRecord` 再分固定字段、mask、blending ranges、layer name 与 additional info block。
- PSB 只在规范指定的位置使用 8 字节长度：layer/mask section、layer info、channel data length，以及 `LMsk`/`Lr16`/`Lr32`/`Layr`/`Mt16`/`Mt32`/`Mtrn`/`Alph`/`FMsk`/`lnk2`/`FEid`/`FXid`/`PxSD` 的 additional info length。Color mode data、image resources、global layer mask info 仍为 4 字节。
- Image resource 1039（ICC Profile）复用 `IccProfileParser`；1058/1059 在数据以 TIFF header 开始时复用 `ExifParser`。其它私有或复杂资源只显示原始范围与 Note。
- 8BIM 的 Unicode string 只按 4 字节 count 读取 UTF-16 code units，**不消费猜测的 2 字节 null**。规范通用段落提到 null，但真实 Photoshop 文件（PSD `luni` 与 JPEG resource 1057）不写该 null；消费它会把后续字段整体错位。
- Layer channel data 与 merged image data 只显示 compression、长度与 Note，不解 RLE/ZIP；layer mask/blending ranges 只解析固定前缀，其余保持原始可见范围。

### 状态

- PSD/PSB 目前标记为**不稳定**：基础结构、image resources、layer records 与常用 additional info 可用，
  但 Adobe 私有 descriptor、adjustment/layer effect、厂商扩展与部分 mask/blending 细节仍可能只显示
  原始范围或字段不完整；不能保证所有真实文件都得到完整、边界正确的节点。
- 现有节点名（`FileHeader`、`ColorModeData`、`LayerRecord[0]` 等）是本工具命名，不是 Adobe 规范原文；
  由于 PSD 支持性本身不稳定，本轮不重命名，后续如需提升到稳定状态再统一。

### 原因

- PSD/PSB 是常见设计文件格式，且其 image resources 与 JPEG APP13 的 8BIM block 完全同构，复用解析器可避免两套字段命名和行为漂移。
- PSB 只是少数长度字段加宽，单独拆 parser 会重复整棵树的逻辑。
- 真实样本比通用规范的 null 描述更可靠；`fixtures/psd/1.psd` 与 `fixtures/8bim_jpeg/04 copy.jpg` 都证明 null 不存在。

### 实现含义

- 新增 `Formats/Psd/`：`PsdParser`、`PsdDocumentParser`、`PsdNodeParser`、`PsdLayout`、`PsdConstants`。
- 新增共享 `Formats/Embedded/PhotoshopImageResources.cs`、`BigEndianReader.cs`、`XmpText.cs`；删除 JPEG 私有的 `JpegPhotoshopResources.cs` 与 `JpegSegmentReader.cs`。
- 建树阶段只读 section/record header；layer records 逐条扫描以确定边界，channel data 与 image data 只记录范围。
- 每个 byte 必须落在某个节点内：截断或无法归属的尾部生成显式 `Unparsed` 节点并给诊断。
- 测试按用户要求移除 `MidiParserTests` 与 `IccProfileParserTests`；新增合成 PSD/PSB 回归，覆盖 section 顺序、资源字段、layer record/channel data、additional info、PSB 8 字节长度与树覆盖。

### 验收

- 真实 `fixtures/psd/1.psd` 打开后显示 5 个顶层 section、5 个 image resource、92 个 layer record、375 个 channel data 节点；树覆盖校验 0 错误。
- 真实 `fixtures/8bim_jpeg/04 copy.jpg` 的 APP13 能显示 23 个 resource，1057 的 Writer/Reader name 与 File version 正确，无截断诊断。
- PSB 合成样本的 8 字节 length 与 `luni` Unicode layer name 正确解析。

---

## D-039 Ogg 容器支持与命名

### 决策

- 新增 Ogg 支持：parser id `ogg`，根节点显示名 `Ogg Encapsulation Format`（RFC 3533 的名称）。
  `.ogg` / `.oga` / `.ogv` / `.opus` / `.spx` / `.ogx` 由同一个 parser 处理，不按扩展名拆成
  `ogv` / `opus` / `vorbis` 等多个 parser。
- 原因：Ogg 是容器，Vorbis/Opus/Theora/Speex/FLAC 是容器内的 codec。`.ogv` 只是 Ogg+Theora 的常见
  扩展名；Opus 是独立 codec（RFC 6716），其 Ogg 映射由 RFC 7845 定义，`OpusTags` 复用 Vorbis comment
  结构，但 Opus 不是 Vorbis 的一种。按 codec 拆 parser 会重复 page framing、CRC 与流识别逻辑。
- 树使用物理 page 结构：根节点下只有 `Page` 节点（RFC 3533 的 page），不按 codec 或扩展名造伪节点。
  完整 packet 的 header 以 `Packet[n].` 字段形式附着在所属 page 上；音频/视频 packet 不建子节点，
  避免把每帧都膨胀成树节点。
- 字段名使用各自规范的原始标识符：RFC 3533 的 snake_case（`granule_position`、
  `bitstream_serial_number`、`segment_table` 等）；RFC 7845 的 Opus 字段标签；Theora 的
  `VMAJ`/`PICW`/`KFGSHIFT` 等缩写；Speex `speex_header.h` 成员名；FLAC 规范字段名（全大写）。
- Page 级 CRC 按需计算并显示 `<CRC check>`；不匹配报 `CRC_MISMATCH`。CRC 参数与 RFC 3533 一致：
  多项式 `0x04C11DB7`、初值 0、不反射、无最终 XOR。
- Codec header 覆盖：Vorbis identification/comment、OpusHead/OpusTags、Theora
  identification/comment、Speex header、Ogg FLAC mapping + STREAMINFO、Skeleton fishead/fisbone。
  Vorbis/Theora setup header、Speex comment packet、FLAC 后续 metadata block 与音视频帧不解析。

### 状态

- Page framing、lacing、CRC、流识别与上述 codec header 标记为**稳定**；setup header 与 packet
  载荷按产品边界保持未解释。
- 不把 granule position 换算成时间：换算依赖 codec 与 Skeleton 的 granulerate，属于跨块语义（D-028）。

### 验收

- 真实 Vorbis 文件（`fixtures/ogg/a053_003_000277.ogg`、`freebgm_05018lhq.ogg`）打开后 page 树覆盖
  0 错误，CRC 全部 OK，comment 字段正确。
- 生成的 Vorbis/Opus/Theora/Speex/FLAC 样本分别显示对应 header 字段；OGV 根节点摘要列出 Theora
  与 Vorbis 两个 logical stream。

---

## D-040 原生 FLAC 支持与全大写字段

### 决策

- 新增 `.flac` 支持：parser id `flac`，根节点显示 `Free Lossless Audio Codec`；文件签名 `fLaC`。
- 元数据块节点使用规范的全大写结构名：`STREAMINFO`、`PADDING`、`APPLICATION`、`SEEKTABLE`、
  `VORBIS_COMMENT`、`CUESHEET`、`PICTURE`、`RESERVED`；SEEKTABLE 的每个点、CUESHEET 的 track/index
  分别使用 `SEEKPOINT`、`CUESHEET_TRACK`、`CUESHEET_TRACK_INDEX` 子节点。
- 所有字段键使用全大写 snake_case（`MINIMUM_BLOCK_SIZE`、`SAMPLE_RATE`、`TRACK_OFFSET` 等），
  遵循 FLAC 的字段大写约定。规范正文部分字段以 prose 描述，本工具按术语转写为大写标识符。
- 音频帧整体作为 `<audio frames>` Unparsed 节点：帧头/子帧/CRC 需要位级解码，超出原始解析边界，
  只显示 `<AudioDataLength>`。
- 与 Ogg FLAC 共用 `FlacStreamInfo` 与 `VorbisComment`（大写命名模式），避免两套 STREAMINFO/comment
  逻辑；Ogg 的 FLAC 资源字段也同步为大写。

### 原因

- FLAC 的元数据块结构稳定、字段少，适合逐块解析；音频帧是压缩载荷，按产品边界不做解码。
- Ogg FLAC 与原生 FLAC 的 STREAMINFO 完全同构，共用解析器可避免字段命名和行为漂移。
- 全大写命名符合 FLAC 规范/工具的传统显示方式，也满足用户对 FLAC 字段大写的要求。

### 限制

- 不解析 frame header、subframe、residual、frame CRC，也不校验 STREAMINFO 的 MD5。
- CUESHEET/SEEKTABLE 只按规范长度切分；语义合法性（track 编号、索引递增等）不检查（D-028）。
- PICTURE 只显示元数据与数据长度，不解码图像。

### 验收

- 真实 `fixtures/flac/拼音师 - 太清 TaiQing.flac`（24-bit/192 kHz）显示 STREAMINFO、SEEKTABLE
  （347 个 SEEKPOINT）、VORBIS_COMMENT、PADDING、PICTURE，树覆盖 0 错误。
- 真实 `fixtures/flac/Trance Gate.flac` 的 APPLICATION 块显示 `APPLICATION_ID = 0x72696666 (riff)`。
- 合成 CUESHEET 样本正确列出 2 个 `CUESHEET_TRACK` 与索引点，树覆盖 0 错误。

---

## D-041 Blender .blend 支持（仅无压缩）

### 决策

- 新增 `.blend` 支持：parser id `blend`，根节点显示 `Blender File`；只处理未压缩文件。
- ZSTD / Gzip 压缩的 .blend 文件**不解压**，文件以压缩魔数开头，探测不会匹配 `blend` parser，
  按 RawData 处理（用户明确要求 ZSTD 当作 RAW）。
- 同时支持两种头：
  - 旧版 12 字节头（`BLENDER` + 指针大小 + 端序 + 3 位版本），块头为 `BHead4`（20 字节）或
    `SmallBHead8`（24 字节）。
  - 当前 17 字节头（`BLENDER` + `17` + `-` + `01` + `v` + 4 位版本），块头统一为
    `LargeBHead8`（32 字节，`len`/`nr` 为 64 位）。
- 树节点是块序列，节点名使用文件里的 4 字节 block code（NUL 截断），例如 `GLOB`、`TEST`、
  `DATA`、`DNA1`、`ENDB`。块头字段使用 Blender 源码名：`len`、`old`、`SDNAnr`、`nr`。
- `DNA1`（SDNA）解析为字段：`NAME_COUNT`/`TYPE_COUNT`/`TLEN_COUNT`/`STRUCT_COUNT` 以及
  `NAME[n]`、`TYPE[n]`、`TLEN[n]`、`STRUCT[n]`；结构名用于给每个块标注 `<SDNA_STRUCT>`。
- 已知元数据块解析：
  - `GLOB` → `FileGlobal`：按 DNA 字段布局解析顶层字段（`subvstr`、`subversion`、
    `minversion`、`fileflags`、`filename`、颜色空间字段等）；指针只显示原文件地址，不解析目标。
  - `TEST` → 缩略图：`width`、`height`、`<ThumbnailDataLength>`；像素不解码。
  - `ENDB` → 结束块（含旧版 8 字节终止符）。
- 其余块（`DATA`、ID 块、`WM`、`USER` 等）是运行时内存数据，保持 `Unparsed`，只显示块头与
  `<SDNA_STRUCT>`，不猜字段值。

### 原因

- .blend 的块头与 SDNA 是稳定的文件级结构；真正的数据块大量是内存镜像，语义解析需要 Blender
  运行时，超出原始解析边界。
- 只支持无压缩版本可以避免引入 ZSTD/Gzip 解压器；压缩文件按 Raw 处理符合产品边界。

### 限制

- 不解析块间指针、链表、数组内容或嵌套结构体；不把 `old` 地址映射成对象。
- `GLOB` 的 `FileGlobal` 布局按 DNA 字段与 C 对齐规则计算；布局与 DNA 声明不一致时不猜字段。
- 不校验 ID 名称、库链接、版本兼容性等语义。
- 压缩 .blend（zstd/gzip）落到 RawData。

### 验收

- 临时真实样本 `Kirisame_Body.blend`（Blender 5.0，17 字节头）：33,443 个块、41,645,786 字节，
  树覆盖 0 错误；`GLOB` 显示 `subversion=119`、`minversion=405`、`filename`、颜色空间字段；
  `TEST` 显示 128×68 缩略图；`DNA1` 显示 5,223 个名字、1,109 个类型、970 个结构。
- 合成 v1（17 字节头）与 v0（12 字节头，32/64 位指针）样本覆盖两套块头、DNA、GLOB/TEST/DATA/ENDB。
- ZSTD 魔数样本的探测结果为 Raw。

---

## D-042 Protocol Buffers 原始解析（不加载 .proto 描述符）

### 决策

- 新增 `.pb` / `.protobuf` / 任意二进制 protobuf 消息支持：parser id `protobuf`，根节点显示名
  `Protocol Buffers`；**不加载任何 `.proto` 描述符**，不依赖字段名、消息类型或 schema。
- 只按 protobuf wire format 解析：`key = (field_number << 3) | wire_type`，保留原始
  `field_number`、`wire_type`、编码后的 `key` 和原始值。支持 wire type 0（varint）、1（fixed64）、
  2（length-delimited）、3/4（start-group/end-group）与 5（fixed32）。
- 每个编码字段是树上的一个节点；重复字段号保留为多个兄弟节点，不合并。TreeView 中只显示
  wire type 的规范名称（`varint`、`fixed64`、`length-delimited`、`start-group`、`fixed32`），
  不拼接字段号、括号、payload 类型等额外字符。原始 `field_number`、`wire_type` 与编码后的 `key`
  在右侧已解析视图（标题与字段）中显示；由于没有 `.proto` 语义名，它们必须显式保留，这是
  D-013 身份去重在“无 schema 字段”上的必要例外。
- length-delimited 字段的 payload 没有 schema 可判定类型，使用保守的显示启发式：
  1. payload 是合法的可打印 UTF-8 文本 → `string`；
  2. 否则完整 payload 能按 protobuf wire format 严格解析 → `message`，并递归生成子节点；
  3. 否则保持 `bytes`，只显示有界 Hex 前缀。
  该分类是显示解释，不改变原始范围和字节；`<PayloadKind>`、`<Text>`、`<HexPreview>` 都是派生字段。
- 不支持/不猜测的语义：不把 varint 强制解释成 `int32`/`sint32`/`bool`/enum，不把 fixed32 解释成
  float，不把 length-delimited 解释成 string 之外的业务类型，不还原 packed repeated，不做签名。
  只额外给出无符号值及必要的 64 位有符号解释（`<UnsignedValue>` / `<SignedValue>`）。
- 损坏或截断：保留已解析字段；截断的 length-delimited 字段成为显式 `Unparsed` 节点并报
  `TRUNCATED_PAYLOAD`；非法 key/varint/wire type 后的剩余字节成为显式 `<unparsed data>` 节点并报
  结构化诊断，不静默丢弃。
- 探测没有 magic：只对有限窗口内完整、合法的 wire 结构给出匹配。完整结构给 `Weak`；
  完整且至少两个字段并带文本/嵌套消息证据给 `Strong`；512 KiB 窗口切断合法字段尾部也给 `Weak`。
  `ProbePriority = 90`，低于所有 magic 格式，因此已有格式的强匹配优先；无法形成合法结构的文件仍落到 RawData。
- 不把 wire 层的启发式分类写成“已确认 schema”。没有 `.proto` 时，字段身份始终是编号 + wire type。

### 原因

- 用户明确要求新增 protobuf 原始解析，并且强调不加载描述符、保留原始 ID/类型名与可能的树形结构。
- protobuf 的 wire format 是自描述的；即使没有 `.proto`，字段编号、wire type、长度和值仍然可以
  无损切分。加载 `.proto` 会把工具变成 schema 驱动解析器，并引入描述符查找、版本匹配和类型系统
  依赖，超出“原始解析”的产品边界。
- length-delimited 同时承载 string、bytes 与嵌套 message，没有 schema 时不存在唯一答案；
  因此采用“文本优先、结构次之、否则 bytes”的保守启发式，并明确标记为派生解释。
- protobuf 没有文件魔数，不能像 PNG/FLAC 那样强探测；低优先级 + 弱匹配可以避免抢走已有格式。

### 实现含义

- 新增 `src/UniversalParser.Core/Formats/Protobuf/`：`ProtobufParser`、`ProtobufDocumentParser`、
  `ProtobufNodeParser`、`ProtobufRead`、`ProtobufMessageScanner`、`ProtobufConstants`。
- 注册：`ParserRegistry.CreateDefault()` 在 `RawParser` 之前加入 `ProtobufParser`。
- 字段键使用 wire format 术语：`field_number`、`wire_type`、`key`、`value`、`length`；
  派生条目使用 `<PayloadKind>`、`<Text>`、`<HexPreview>`、`<FieldCount>`、`<PayloadLength>`、
  `<UnsignedValue>`、`<SignedValue>`。
- 新增诊断码 `INVALID_FIELD_NUMBER`、`INVALID_WIRE_TYPE`；复用 `INVALID_VINT`、
  `TRUNCATED_HEADER`、`TRUNCATED_PAYLOAD`、`SCHEMA_MISMATCH`、`LIMIT_STRING`、
  `INVALID_ENCODING`。
- 文本读取受 `MaxStringBytes` 限制；字段数量受 `MaxDataLines` 限制；树深度、节点数与解析时长
  不设限（D-032）。不新增格式私有读取上限。
- 测试使用内存构造的 protobuf 消息，覆盖 varint/fixed64/fixed32/string/nested message/group、
  重复字段号、截断、非法 key/varint、探测强弱匹配与 Raw 兜底。

### 限制

- 没有 `.proto` 时无法知道字段名、枚举名、packed 语义、map、oneof、默认值或消息类型。
- 文本与嵌套 message 的分类是启发式；合法但全可打印的二进制度量可能被显示为 string。
- 超过 512 KiB 的 length-delimited payload 仍会递归建树，但探测阶段只在固定窗口内采样；
  超大文件可能以 `Weak` 匹配进入 protobuf。
- 不校验文件级或跨字段语义（D-028）。

### 验收

- 合成消息的树覆盖校验 0 错误；TreeView 只显示 `varint`/`fixed64`/`length-delimited`/`start-group`/
  `fixed32`；右侧解析视图显示原始 `field_number`、`wire_type`、`key` 和值。
- 嵌套 length-delimited payload 生成 `message` 子节点；文本 payload 显示 `<Text>`。
- 截断/非法结构保留已解析字段、显式未解析范围和诊断。
- magic 格式（PNG/JPEG/RIFF 等）不被 protobuf 探测抢走；随机/零填充文件仍落到 RawData。

---

## D-043 Minecraft NBT 原始解析（仅无压缩）

### 决策

- 新增 `.dat` / `.nbt` / 无压缩 NBT 消息支持：parser id `nbt`，根节点显示名 `Named Binary Tag`。
- **只支持无压缩 NBT**：
  - gzip、zlib 等压缩 NBT 不探测为 `nbt`，按 RawData 处理；
  - MCA / MCR 区域文件（region file）不按 NBT 解析，按 RawData 处理；
  - 不解压、不拼接 MCA 区块，也不解析 `.mca` 的 8 KiB location/timestamp 表。
- 完整支持 NBT tag 结构：
  - `TAG_End`(0)、`TAG_Byte`(1)、`TAG_Short`(2)、`TAG_Int`(3)、`TAG_Long`(4)、
    `TAG_Float`(5)、`TAG_Double`(6)、`TAG_Byte_Array`(7)、`TAG_String`(8)、
    `TAG_List`(9)、`TAG_Compound`(10)、`TAG_Int_Array`(11)、`TAG_Long_Array`(12)。
  - 多字节整数与浮点均为 big-endian；名称与字符串为 `u16` 长度 + UTF-8 字节。
  - `TAG_List` 为 1 字节元素类型 + `i32` 元素数 + 无名称元素；`TAG_Compound` 由命名 tag 序列和
    `TAG_End` 结束。
- **TreeView 只显示规范类型名**：`Compound`、`Byte`、`Short`、`Int`、`Long`、`Float`、`Double`、
  `ByteArray`、`String`、`List`、`IntArray`、`LongArray`。不显示 tag 名称、引号、索引或括号等额外字符。
- tag 名称、列表索引与具体值在右侧已解析视图中显示：
  - 根 tag / 命名 tag 标题：`Compound 'root'`、`Int 'health'`；
  - 列表元素标题：`Int '[0]'`、`Compound '[1]'`；
  - 字段：`type`、`name`、`value`、`length`、`element_type` 与派生 `<Index>`、`<ElementType>`、
    `<HexPreview>`、`<PreviewValues>`、`<ChildCount>`、`<EndTagPresent>`、`<RawBits>`。
- 大数组（Byte/Int/Long Array）不逐元素建节点，只显示 `length` 与有界预览；列表元素逐个建节点。
- 不支持/不猜测 Minecraft 语义：不解释 level.dat/chunk 的字段含义，不做 NBT path、默认值、枚举名、
  block entity 解码或跨 tag 语义校验。
- 损坏/截断：保留已解析 tag；截断的值成为 `Unparsed` 节点并诊断；缺少 `TAG_End`、负数组/列表长度、
  非法 tag type、根 tag 后的 trailing bytes 都有显式诊断或显式未解析节点。

### 原因

- 用户明确要求新增 Minecraft NBT，并限定无压缩；压缩 NBT 与 MCA 已超出“原始结构解析”的最小范围，
  且需要解压或区域索引逻辑，按要求交给 RawData。
- NBT 是自描述的 tag 树，tag type、名称、长度和值都在文件内；不需要外部 schema。
- TreeView 中只显示规范类型名可以让树保持稳定、可扫描；用户需要的名称与值在右侧解析视图中完整保留。

### 实现含义

- 新增 `src/UniversalParser.Core/Formats/Nbt/`：`NbtParser`、`NbtDocumentParser`、`NbtNodeParser`、
  `NbtRead`、`NbtScanner`、`NbtConstants`。
- 注册：`ParserRegistry.CreateDefault()` 在 `RawParser` 之前、`ProtobufParser` 之后加入 `NbtParser`；
  `ProbePriority = 95`。
- 探测：要求文件以 `TAG_Compound` 根 tag 开始；在固定 512 KiB 窗口内验证完整 NBT framing。
  完整根 compound 给 `Strong`；窗口截断或根 tag 后有 trailing bytes 给 `Weak`；gzip/zlib/MCA
  或非 compound 根不匹配。
- 文本读取受 `MaxStringBytes` 限制；字段数量受 `MaxDataLines` 限制；树深度、节点数与解析时长
  不设限（D-032）。
- 新增诊断码 `INVALID_TAG_TYPE`；复用 `INVALID_LENGTH`、`TRUNCATED_HEADER`、`TRUNCATED_PAYLOAD`、
  `TRUNCATED_CHILD`、`INVALID_ENCODING`、`LIMIT_STRING`、`TRAILING_DATA`。
- 测试使用内存构造的 NBT：全部 12 种 tag、空 compound、list of Int、list of Compound、嵌套 compound、
  缺少 `TAG_End`、负数组长度、压缩/MCA 回退 Raw、探测强匹配。

### 限制

- 仅无压缩 NBT；gzip/zlib 与 MCA/MCR 区域文件始终是 RawData。
- NBT 字符串按 UTF-8 解码；Java modified UTF-8 的极端编码（`C0 80`、代理对形式）可能显示为替换字符并
  报 `INVALID_ENCODING`，原始字节仍可通过 Hex/Raw 查看。
- 大数组只显示长度与预览，不逐元素建节点。
- 不校验 Minecraft 版本、chunk 语义或跨 tag 关系（D-028）。

### 验收

- 真实/合成无压缩 NBT 的树覆盖校验 0 错误。
- TreeView 只出现 `Compound`/`Byte`/`List` 等规范类型名；tag 名称与索引只在右侧标题/字段出现。
- list of Compound 与嵌套 compound 正确形成树。
- gzip/zlib/MCA-like 文件探测为 RawData。
- 截断、缺少 `TAG_End`、负数组长度有诊断且不产生不可见空洞。

---

## 非目标

- 不做播放器
- 不做转码/转封装
- 不做完整格式校验器
- 不做文件修复
- 不兼容旧版导出文本
- 不为了“统一好看”而改写官方字段名
- 不为了“人类可读”而隐藏原始值

---

## 待定事项

当前没有待用户确认的设计问题；剩余增强项见 `01-open-issues.md` §2。已关闭的要点：

- 报告主格式：纯文本（D-012）；Hex 默认 4096 行，滚动条映射到 1,000,000 个位置（D-012）。
- ASF Data Packet、FBX 数组元素级视图、报告 JSON、SHA-256、JPEG 私有 APPn：不做（D-022）。
- 跨块/跨字段语义校验（RF64 `ds64` 表项、APNG 帧一致性等）：不做（D-028）。
