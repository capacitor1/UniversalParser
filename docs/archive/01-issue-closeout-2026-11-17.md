# 01 — Phase 6 结论与 Phase 7 入口

> **归档快照（2026-11-17）。** 非当前规范；当前限制见 `../01-open-issues.md`。
> 保留 Phase 6 逐项关闭结论与 Phase 7 新增格式的入口记录。

> 2026-11-16 用户决策已落实；Phase 6 收尾验证：2026-11-17。实现历史见 `05`，产品决策见 `02`。

## 1. 本次结论

Phase 6 不只是代码加固，也包括 BUG 修复、字段补齐与细节微调。本次指定工作已完成：

| 编号 | 结论 |
|---|---|
| P2-08 | 已补：AVI OpenDML `dmlh`、`indx` / `ix##`；super、standard、two-field 条目及有界未知布局 |
| P2-11、P2-14 | 不做，按产品边界关闭：不逐项验证 RF64 table 的语义，也不校验 APNG 帧与默认图的一致性（D-028） |
| P2-13 | 已补：PLTE RGB、tRNS alpha/有效样本、bKGD 背景解释；保留原始位深和值，不做像素/色彩管理解码 |
| P2-16 | 已补：XMP 完整原文文本返回，不解析 XML/RDF；Adobe 官方文档明确，已实现 8BIM framing 与常用资源。其它 APP 标识仍 `Unparsed`（D-023） |
| P2-17、P2-20 | 暂缓：Enhanced audio 和真实 HEVC FLV 扫描，不用占位样本冒充真实样本 |
| P2-18、P2-19 | 已补：三种视频 Multitrack；AMF3 U29、三张引用表、所有标准类型（含 Vector/Dictionary）与部分结果保护 |
| P2-23 | 已按稳定参考补外层 footer framing。Blender / SDK padding 布局不同，已用实际样本验证；footer code/id 只作为原始 16 字节返回，不猜通用解密。未知 footer 整体 `Unparsed`（D-029） |
| P2-28 | 已由 D-034 取代：报告不再有 `MaxExportBytes` 总预算，字段/结构完整写入，Hex 固定首尾 128 KiB；Raw 导出不再因大小拒绝。解析时间限制已移除（D-032） |
| P2-30 | 已补：PNG `mDCV` / `cLLI` 全部字段、规范顺序/精度与长度/CRC 诊断 |

2026-11-17 追加：移除全部软件级解析性能限制（深度/子节点数/总节点数/时间，D-032）；
EBML `SimpleBlock` / `Block` / `BlockVirtual` 改为高熵 `Unparsed`，取消 256 字节内联预览（D-033）。

Phase 6 最终收尾：FBX/FLV 建树改用顺序读取窗口；Hex 只读取可见区一次；修复 `Expand All`
分页、导出/加载共享状态、ISOBMFF 大 data atom 分配、PNG 大块 CRC、ASF 循环与 EXIF/ID3 预分配。
测试范围收缩为框架底层测试；格式解析/GUI 测试已移除，后续按需重写。

2026-11-17 Phase 7 追加：报告导出取消 `MaxExportBytes`，已解析字段/结构完整写入，Hex 固定首尾
各 128 KiB 并提示使用 Raw 导出完整数据（D-034）；新增 Standard MIDI File 支持，track 事件逐个
列出（D-035）。

2026-11-17 Phase 7 追加：新增 ICC profile 完整解析（D-036）。PNG `iCCP` 解压后不再只读 header，
而是解析 128 字节 header、tag table 与常见 tag type；JPEG `APP2` `ICC_PROFILE` 仅在
`marker count == 1` 时解析，多段 profile 只显示分段框架，不跨段拼接。

2026-11-17 Phase 7 追加：新增 Adobe Photoshop PSD/PSB 解析（D-038）。PSD/PSB 与 JPEG APP13
`Photoshop 3.0` 共用同一个 8BIM image resource 解析器，并补齐常用 resource 字段；PSD 树覆盖
5 个顶层 section、layer records、channel data、global mask 与 additional layer information。
按用户要求移除 MIDI/ICC 格式测试，改为新增合成 PSD/PSB 回归。PSD/PSB 随后标记为**不稳定**：
Adobe 私有/复杂结构仍可能导致部分节点只显示原始范围或字段不完整（D-038）。

2026-11-17 Phase 7 追加：新增 Ogg 支持（D-039）。parser id `ogg`，根节点 `Ogg Encapsulation Format`；
`.ogg`/`.oga`/`.ogv`/`.opus`/`.spx` 共用同一 parser。树为 `Page` 级物理结构，解析 lacing、CRC、
Vorbis/Opus/Theora/Speex/FLAC/Skeleton header；setup header 与音视频 packet 不解码。

2026-11-17 Phase 7 追加：新增原生 FLAC 支持（D-040）。parser id `flac`，根节点
`Free Lossless Audio Codec`；metadata 结构名与字段全大写，SEEKTABLE/CUESHEET 生成子节点；
音频帧整体保持 `Unparsed`。与 Ogg FLAC 共用 STREAMINFO 与 Vorbis comment 解析。

2026-11-17 Phase 7 追加：新增无压缩 Blender .blend 支持（D-041）。parser id `blend`，根节点
`Blender File`；支持 12/17 字节头与 BHead4/SmallBHead8/LargeBHead8；DNA1 提供 SDNA 结构名，
GLOB/TEST/ENDB 解析，其余内存块保持 `Unparsed`。ZSTD/Gzip 压缩文件不探测为 blend，按 RawData 处理。

2026-11-17 Phase 7 追加：新增 Protocol Buffers 原始解析（D-042）。parser id `protobuf`，根节点
`Protocol Buffers`；不加载任何 `.proto` 描述符，只按 wire format 保留 `field_number`、`wire_type`、
`key` 与原始值；length-delimited payload 按“可打印 UTF-8 文本 / 合法嵌套消息 / 原始字节”启发式分类，
嵌套消息递归建树；group 与重复字段号保留为独立节点。protobuf 没有文件魔数，探测使用低优先级
（`ProbePriority = 90`）与结构/文本/嵌套证据，已有 magic 格式优先，非法结构回退 RawData。

2026-11-17 Phase 7 追加：新增无压缩 Minecraft NBT（D-043）。parser id `nbt`，根节点
`Named Binary Tag`；支持 12 种 tag、命名 tag、列表元素与 compound 递归树；TreeView 只显示
`Compound`/`Byte`/`List` 等规范类型名，tag 名称与列表索引在右侧解析视图显示；gzip/zlib 压缩 NBT
与 MCA/MCR 区域文件不匹配，按 RawData 处理。

**边界：对每个块只做解析，诊断只管格式本身。** 长度、截断、签名、无法解析的编码与单块 CRC
保留诊断；语义内容即使严重错误也不检查，不因跨块/跨字段关系加 `SCHEMA_MISMATCH`。
为语义显示而读必要上下文不等于语义校验（例如按 IHDR 位深解释 tRNS/bKGD）。

## 2. 当前状态

- P0 无已知缺陷；P1 全部关闭或接受（P1-04 不追求旧版 ISOBMFF parity）；Q 全部有结论。
- 本次必补项不再作为待办；已删除“按需补”“可加一致性诊断”等过期建议。
- 本批次完成不代表自动增加 fuzz、真实 HEVC 或其它格式/codec 的范围；后续工作仍需有明确任务。

## 3. 暂缓与已接受限制

| 编号 | 领域 | 限制 / 处理 |
|---|---|---|
| P2-01 | GUI | 字段搜索/过滤不做（D-021） |
| P2-04 | GUI | `Expand All` 已移除 50,000 上限，仅保留分批异步与取消 |
| P2-05、P2-06 | 导出 | JSON 报告、SHA-256 不做（D-022） |
| P2-07 | 导出 | 报告只含当前节点 Hex，保持 |
| P2-09 | RIFF | DLS 深层 region/articulation/wave pool 保持原覆盖 |
| P2-10 | RIFF | Wwise HIRC 对象体 / DATA 媒体内容不解码 |
| P2-15 | PNG | 已关闭：所有 chunk（含 `IDAT`/`fdAT`）在选中时都重算 CRC；超过 512 KiB 只影响进度显示，不再跳过校验或产生 Note（D-037） |
| P2-17、P2-20 | FLV | Enhanced audio / 真实 HEVC 扫描暂缓 |
| P2-21、P2-22、P2-24 | FBX | ASCII 交给 Raw；deflate 数组不解压；对象/连接语义不解析 |
| P2-25、P2-26 | ASF | Data Packet 不展开；加密/签名/脚本等区域保持显式未解释与 Note |
| P2-27 | ISOBMFF | 拒绝 `largesize > long.MaxValue`，接受 |

本次新增解析的保留边界：其它 JPEG APP、私有/复杂 Photoshop resource、PSD RLE/ZIP 像素与
descriptor/adjustment 深层结构、PSD layer effect/部分 mask/blending 细节、Ogg 的 Vorbis/Theora
setup header、Speex comment packet、FLAC 后续 metadata block 与全部音视频 packet、原生 FLAC 的
frame header/subframe/CRC 与音频载荷、Blender 的运行时内存块/指针目标/嵌套结构体与 ZSTD/Gzip
压缩文件、Protobuf 的 `.proto` 语义/枚举/packed/map、NBT 的压缩与 MCA/区域语义、
AMF3 externalizable 自定义 body、未知 FBX footer 内容仍保持原始可见范围；不执行、
不猜字段，不转成伪数据。

PSD/PSB 当前状态为**不稳定**：基础结构可用，但 Adobe 私有/复杂结构仍可能导致部分节点只显示原始范围
或字段不完整，不能保证所有真实文件都得到完整、边界正确的节点（D-038）。

## 4. 本批次验证

- [x] `dotnet build`：0 warning / 0 error。
- [x] `dotnet test`：**117 passed**（仅框架底层测试，含 PSD/PSB、Ogg、FLAC、Blender、Protobuf 与 NBT 新格式回归）。
- [x] `dotnet format --verify-no-changes`。
- [x] 框架层（source/range/node/engine/export/limits）回归；本轮新增 PSD/PSB、Ogg、FLAC 与 Blender 格式回归；其余格式/GUI 测试仍按需重写。
- [x] 新样本有生成脚本或固定来源/commit/hash，目录 README 已列出；临时 Blender 样本验证后已删除。

## 5. Phase 7 入口

Phase 7 定义为“细节提升和文件支持性完善”，默认不做无任务驱动的新格式扩张。范围仍以本文件结论为准：

- 现有格式的字段细节、命名与解释打磨，不改变“原始值优先”的显示规则。
- 文件支持性完善：真实样本暴露的变体、可选块与边界布局；仍以显式 `Unparsed` + raw 前缀兜底。
- 已明确暂缓/不做的项（P2-11/14/17/20、D-021/022 等）不会因 Phase 7 自动解禁。
- 2026-11-17 用户指定新增：报告导出规则重做（D-034）、MIDI 支持（D-035）、PSD/PSB 支持（D-038）、Ogg 支持（D-039）、原生 FLAC 支持（D-040）、无压缩 Blender .blend 支持（D-041）、Protocol Buffers 原始解析（D-042）与无压缩 Minecraft NBT（D-043）；均已完成。

旧的“删除旧代码、文档收尾”工作顺延为 Phase 8。

