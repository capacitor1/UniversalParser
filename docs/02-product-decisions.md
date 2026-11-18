# 02 — 产品决策记录

> 适用于 v1.0.1。当前生效规则在本文件；D-001 ~ D-043 的完整理由、实现含义、
> 验收标准与历史背景见 `archive/02-product-decisions-full.md`。
> 两者冲突时，以本文件的当前规则为准；需要恢复历史细节时再查 archive。

---

## 1. 决策登记

| 编号 | 决策 | 状态 |
|---|---|---|
| D-001 | 导出不要求兼容旧版 TXT，报告服务故障分析 | 生效 |
| D-002 | padding 计入节点长度，禁止看不见的空洞 | 生效 |
| D-003 | RawData 惰性读取，不整块加载 | 生效 |
| D-004 | 必须支持超大文件，64 位 offset/length | 生效 |
| D-005 | 原始数据优先、官方命名优先 | 生效 |
| D-006 | 只做原始解析，不做 payload 解码/播放/校验 | 生效 |
| D-007 | GUI 全英文；文件内容语言按原编码保留 | 生效 |
| D-008 | `ParseLimits` 是唯一读取内存限制来源，可 JSON 部分覆盖 | 生效 |
| D-009 | ASF Data Packets 不按 GUID object 展开 | 生效 |
| D-010 | FBX 数组属性不解压 | 生效 |
| D-011 | 小体量、简单压缩的元数据/文本可有界解码 | 生效 |
| D-012 | 保持旧版视觉，用懒加载/虚拟化重做性能路径 | 生效（Hex 预算部分被 D-034 取代） |
| D-013 | 节点标题与树名分离，身份字段不重复 | 生效 |
| D-014 | 格式显示名使用全称 | 生效 |
| D-015 | 加载/解析/导出进度必须持续反馈 | 生效 |
| D-016 | AVI `LIST:movi` 整体不展开 | 生效 |
| D-017 | 已知但未实现的区域用 `<Note>` 标注 | 生效 |
| D-018 | 内嵌 EXIF 与 ID3v2 完整解析 | 生效 |
| D-019 | ISOBMFF 补齐 ASC/SPS/PPS/FLAC/fdel/alis | 生效 |
| D-020 | 未完成下载/零填充文件必须安全终止 | 生效 |
| D-021 | GUI 复制、Hex 选区与 Statistics 视图 | 生效 |
| D-022 | 本版本明确不做的功能边界 | 生效 |
| D-023 | JPEG APPn 按 Identifier 分派 | 生效 |
| D-024 | Hex 列坐标与 Statistics 交互 | 生效 |
| D-025 | 根节点只显示文件系统信息与探测结果 | 生效 |
| D-026 | 进程内视图设置与统计交互 | 生效 |
| D-027 | 菜单快捷键与 `Assets` PNG 树图标 | 生效 |
| D-028 | 诊断只针对格式本身，不做语义校验 | 生效 |
| D-029 | FBX footer 只解析可验证的外层 framing | 生效 |
| D-030 | 报告导出总字节预算 | **已被 D-034 取代** |
| D-031 | EBML/JPEG/FBX/FLV 建树使用固定 16 MiB 读取窗口 | 生效 |
| D-032 | 不设软件级解析性能限制 | 生效 |
| D-033 | EBML block 元素按高熵数据整块 Unparsed | 生效 |
| D-034 | 报告无总预算，Hex 固定首尾各 128 KiB | 生效 |
| D-035 | 新增 Standard MIDI File 支持 | 生效 |
| D-036 | 内嵌 ICC profile 完整解析 | 生效 |
| D-037 | 探测与长时间读取使用固定 512 KiB 阈值 | 生效 |
| D-038 | PSD/PSB 支持与共享 8BIM 解析 | 生效（PSD 不稳定） |
| D-039 | Ogg 容器支持与 page 级命名 | 生效 |
| D-040 | 原生 FLAC 支持与全大写字段 | 生效 |
| D-041 | Blender .blend 支持（仅无压缩） | 生效 |
| D-042 | Protocol Buffers 原始解析（不加载 `.proto`） | 生效 |
| D-043 | Minecraft NBT 原始解析（仅无压缩） | 生效 |

---

## 2. 当前生效规则

### 2.1 产品边界

- 只按格式规范切分容器/块、提取字段、给出必要的人类可读解释。
- 不做播放器、转码/转封装、文件修复、完整校验器，也不兼容旧版导出文本（D-001、D-006）。
- 默认不解码 payload。仅允许小体量、压缩/变换简单的元数据/文本有界解码（D-011）：
  PNG `iCCP`/`zTXt`/压缩 `iTXt`；`IDAT`/`fdAT`、FBX deflate 数组、音视频帧、JPEG scan
  仍不解码。
- 诊断只针对格式本身：截断、长度、签名、块内字段/版本/保留位/枚举、块内 CRC（D-028）。
  跨块/跨字段语义即使明显错误也不产生诊断；解释只能是派生字段或 `<Note>`。
- 已知但未实现的区域一律 `<Note>` + 原始范围，不用假装已解析（D-017）。
- 明确不做：字段搜索/过滤、JSON 报告、SHA-256、fuzz、旧版导出兼容（D-021、D-022）。

### 2.2 树、范围与原始数据

- 节点不可变；子节点完全位于父范围内；兄弟节点按 offset 排序且不重叠。
- padding 计入节点长度；未覆盖区间用 `Padding` / `Unparsed` / `Trailing` / `Ignored` 显式表示，
  不允许不可见空洞（D-002）。
- 特殊格式允许忽略部分框架数据，但必须记录并尽量用显式合成节点表示。
- RawData 默认等于节点完整编码范围；按需从 source 读取，不整块加载（D-003）。
- 全部 offset/length/行号为 64 位，禁止无检查的 `int`/`uint` 截断（D-004）。
- 零填充/未完成下载：读到全零 header 必须终止并生成 `<zero fill>`，不能继续造节点（D-020）。
- AVI `LIST:movi` 整块 `Unparsed`（D-016）；EBML `SimpleBlock`/`Block`/`BlockVirtual`
  整块高熵 `Unparsed`，不做内联预览（D-033）。
- 解析失败返回部分结果 + 结构化诊断，不把异常文本塞进 RawData。

### 2.3 命名与显示

- 原始字段名严格来自官方规范，保持官方大小写与写法；不翻译、不合并命名风格（D-005）。
- 官方没有明确命名的原始字段用描述性英文键，不加尖括号。
- 显示优先级：`official_name = raw_value`，短解释写成 `raw_value (human)`；
  长解释另起尖括号条目。尖括号只用于派生值、关键短 Note、解析器错误。
- 规范已有 `size`/`length` 时先展示原始字段，再给 `<PayloadLength>` 等派生值。
- 标题格式：`(OptionalMarker)NoSpaceTitle 'RawID'`；树只显示最接近的已处理语义名（D-013）。
- 已出现在标题中的 4CC/GUID/marker/节点名不再作为字段重复输出。
- 文档根节点使用格式全称（D-014）；parser id 保持稳定小写。
- GUI 标签/菜单/状态/错误摘要全英文；文件内容语言按声明编码原样保留（D-007）。

### 2.4 限制、性能与导出

- `ParseLimits` 只有 `MaxStringBytes`（默认 4 MiB）、`MaxBlobBytes`（默认 256 MiB）、
  `MaxDataLines`（默认 10,000）。解析器只能通过 `ParseContext.Limits` 读取，不得在格式内
  另建不可配置上限（D-008）。
- 可选覆盖文件：可执行文件旁的 `universalparser.limits.json`，只覆盖显式出现的属性；
  损坏时回退默认值并在状态栏提示。示例见仓库根目录与 `dist/`。
- 不设解析深度、子节点数、总节点数、解析时长上限（D-032）；取消是唯一主动中止路径。
- 探测窗口与可能处理较大范围的操作（如 PNG CRC）使用固定 512 KiB 阈值；超过阈值继续执行，
  通过进度条反馈，不截断、不跳过、不产生限制 Note（D-037）。
- EBML/JPEG/FBX/FLV 建树使用固定 16 MiB 顺序读取窗口；小于 1 MiB 不启用，硬上限 16 GiB
  物理内存，不可配置（D-031）。
- 报告导出无总字节预算；Fields 与 Structure 完整写入（D-034）。
- Hex 是唯一二进制窗口：范围 `<= 256 KiB` 完整写入；更大时写首 128 KiB + 尾 128 KiB，
  中间给人类可读说明并提示使用 `Export Raw`。Raw 导出完整、流式、不因大小拒绝。
- 导出与解析均可取消、有进度；文件名 sanitize；文本转义控制字符。

### 2.5 GUI

- 布局/字体/菜单沿用旧版；树懒加载，展开时一次性创建全部子节点；`Expand All` 分批异步、
  每 200 个节点让出 UI、可取消、无节点数上限（D-012）。
- 字段列表虚拟化，只创建可见行；Hex 只渲染可见行，64 位行号，滚动条用 `Int128` 映射。
- 节点解析在后台线程；按加载/选择代次丢弃过期结果；关闭文件取消任务、释放 source、
  清空结果与缓存。禁止 `Application.DoEvents()` 与关闭跨线程检查。
- 进度契约：检测 → 解析 → Done；`ReportProgress` 合并到 100 ms 一次；无真实进度前用 Marquee，
  不允许进度停在 0（D-015）。
- 根节点字段固定为 `File name`、`File size`、`Creation time`（UTC）、`File attributes`、
  `Detected format`、`Probe confidence`，不显示内部计数或 limits（D-025）。
- 字段右键 `Copy Key`/`Copy Value`/`Copy Row`；Hex 支持拖选与复制；滚动条保证最后一行可达（D-021）。
- `Settings` 只影响进程内：百分比小数位 1–6（默认 3）、Top N 3/5/8/10（默认 10）；
  百分比统一经 `ValueFormat.Percentage`，不在调用点硬编码精度（D-026）。
- Statistics：饼图 + 图例 + 悬停；`Largest nodes` 带 `Node type` 复选框（每类保留前 200 候选），
  双击/右键/Enter 定位节点；`Repeated names` 唯一名称灰显（D-024、D-026）。
- 每个菜单命令都有快捷键并在菜单显示：`Ctrl+O`、`Ctrl+W`、`Ctrl+Shift+E`、
  `Ctrl+Shift+C`、`Ctrl+Shift+S`（D-027）。
- 树图标只来自 `src/UniversalParser.App/Assets` 的 48 px PNG（嵌入资源），缩放到 16 px；
  根/关闭容器/展开容器/叶子/选中固定映射（D-027）。

### 2.6 格式专项

- ASF：Data Object 内 packet 区域只建立一个 `Unparsed` 节点（D-009）。
- EXIF：JPEG APP1、PNG `eXIf`、PSD 1058/1059 共用 IFD walker；ID3v2 支持 v2.2/2.3/2.4
  （D-018）。MakerNote/缩略图只标注，不解码。
- ISOBMFF：补齐 ASC、H.264 SPS/PPS 几何、全部 FLAC metadata block、HEIF `fdel`、`alis`
  tag 表；冷门 codec 私有 payload 不继续展开（D-019）。
- JPEG APP0–APP15 按 payload 开头的 Identifier 分派，不按 APP 序号猜测；注册 Exif、ICC、
  XMP、Photoshop 8BIM、Adobe；其余保持 `Unparsed`（D-023、D-036）。
- FBX footer 仅当尾部签名、零填充与范围符合已确认 framing 时拆成
  `Footer ID`/`padding`/`version`/`trailer`；否则整块 `Unparsed`，不解密 id（D-029）。
- MIDI：`MThd` + 每个 `MTrk`；track 内每个事件是独立子节点，支持 running status、SysEx
  与全部标准 meta event（D-035）。
- ICC：解析 128 字节 header、tag table 与常见 tag type；LUT 只记结构；JPEG 多段不拼接（D-036）。
- PSD/PSB：5 个顶层 section；共享 `PhotoshopImageResources`；PSB 只在规范位置用 8 字节长度；
  Unicode 字符串不消费猜测的 2 字节 null；像素不解码（D-038）。
- Ogg：单一 `ogg` parser，page 级物理树；`.ogg/.oga/.ogv/.opus/.spx` 不拆 parser；
  解析 lacing/CRC 与 Vorbis/Opus/Theora/Speex/FLAC/Skeleton header（D-039）。
- FLAC：`fLaC` + metadata block 树；结构名与字段全大写；音频帧整体 `Unparsed`（D-040）。
- Blender：仅无压缩；支持 12/17 字节头与三种块头；DNA1/GLOB/TEST/ENDB 解析，
  其余内存块 `Unparsed`；ZSTD/Gzip 按 Raw（D-041）。
- Protobuf：不加载 `.proto`；保留 `field_number`/`wire_type`/`key` 与原始值；
  length-delimited 的 string/message/bytes 是显示启发式；低优先级探测（D-042）。
- NBT：仅无压缩；12 种 tag；TreeView 只显示规范类型名，名称/索引在右侧视图；
  压缩 NBT 与 MCA/MCR 按 Raw（D-043）。

---

## 3. `ParseLimits` 配置

```json
{
  "maxStringBytes": 4194304,
  "maxBlobBytes": 268435456,
  "maxDataLines": 10000
}
```

- 复制为可执行文件旁的 `universalparser.limits.json`；只写需要改的键。
- `maxStringBytes`：单次字符串读取上限（int）。
- `maxBlobBytes`：单次 Blob/解压输出上限（long）。
- `maxDataLines`：单次表格/记录循环上限（int）。
- 超限只截断当前值并产生 `LIMIT_STRING` / `LIMIT_BLOB` / `LIMIT_RECORDS`，保留结构与范围。

---

## 4. 非目标

- 不做播放器、转码/转封装、文件修复、完整格式校验器。
- 不兼容旧版导出文本。
- 不为“统一好看”改写官方字段名，不为“人类可读”隐藏原始值。
- 不做跨块/跨字段语义判断。
- 不把无 schema 的 protobuf/NBT 内容猜成业务字段。

---

## 5. 待定事项

当前没有待用户确认的设计问题。新增能力必须先有明确任务，并先改本文件再改代码。
