# 01 — 剩余限制与已接受风险

> 适用于 v1.0.0。Phase 6/7 的逐项关闭记录已归档到
> `archive/01-issue-closeout-2026-11-17.md`；完整决策见 `02`。

---

## 1. 当前状态

- P0 无已知缺陷；P1 全部关闭或接受；Q 项全部有结论。
- 没有阻塞发布的未决问题。剩余内容都是**明确接受**的边界，不是待办。
- 后续新增格式或解码能力必须有明确任务；不会因版本发布自动解禁本页项目。

---

## 2. 明确不做

| 领域 | 限制 | 依据 |
|---|---|---|
| GUI | 字段搜索/过滤、字段内查找 | D-021 |
| GUI | Hex >4 GB、单节点 >2 GB 的自动化实测 | D-022 |
| 导出 | JSON 报告、SHA-256、旧版 TXT 兼容 | D-001 / D-022 |
| 导出 | 报告只含当前节点 Hex；完整二进制用 `Export Raw` | D-034 |
| 测试 | 各格式随机 fuzz、真实超大文件集 | D-022 |
| 解析 | payload 解码、播放、转封装、修复、完整校验 | D-006 |
| 诊断 | 跨块/跨字段语义校验（帧一致性、索引目标、码点组合等） | D-028 |

---

## 3. 格式保留边界

| 格式 | 保持不解析 / 未展开 |
|---|---|
| RIFF | DLS 深层 region/articulation/wave pool；Wwise HIRC 对象体与 DATA 媒体内容；未知 chunk 整块 `Unparsed` |
| EBML | `SimpleBlock`/`Block`/`BlockVirtual` 整块高熵 `Unparsed`（D-033）；未知元素整块 `Unparsed` |
| FLV | Enhanced audio；真实 HEVC 码流扫描；音视频 payload 与 AMF3 externalizable body |
| ASF | Data Packets 不切分；Index Blocks、加密/签名/脚本等未实现对象保持 `Unparsed` + Note |
| FBX | ASCII 交给 Raw；deflate 数组不解压；对象/连接语义不解析；未知 footer 整体 `Unparsed` |
| PNG | `IDAT`/`fdAT` 不解压；不做像素、色彩管理或跨块一致性校验 |
| JPEG | 未注册 APP 整段 `Unparsed`；多段 ICC 不跨段拼接 |
| ISOBMFF | 冷门 codec 私有 payload 不展开，按 D-017 加 `<Note>`；未知 box `Unparsed` |
| MIDI | SysEx / Sequencer-Specific payload 只显示长度，不解释内容 |
| PSD/PSB | RLE/ZIP 像素不解码；Adobe 私有 descriptor、adjustment/layer effect 与部分 mask/blending 细节可能只显示原始范围 |
| Ogg | Vorbis/Theora setup header、Speex comment packet、FLAC 后续 metadata block 与音视频 packet 不解码 |
| FLAC | 音频帧整体 `Unparsed`；frame header/subframe/CRC 与 MD5 不解析 |
| Blender | 仅无压缩；ZSTD/Gzip 按 Raw；运行时内存块、指针目标、嵌套结构体不解析 |
| Protobuf | 不加载 `.proto`；enum/packed/map/oneof/默认值等 schema 语义不可恢复 |
| NBT | 仅无压缩；gzip/zlib NBT 与 MCA/MCR 按 Raw；不解释 Minecraft 业务语义 |
| 内嵌 | MakerNote / APIC 图像 / 缩略图只标注类型与长度；多段 ICC 不拼接 |

---

## 4. PSD/PSB 状态：不稳定

基础 section、image resources、layer records、channel data 与常用 additional info 已实现，
但 Adobe 私有/复杂结构仍可能导致：

- 部分节点只能显示原始范围或字段不完整；
- 在非 Adobe 生成的文件上出现字段边界偏差；
- 节点名（`FileHeader`、`LayerRecord[0]` 等）属于本工具命名，不是规范原文。

不要把 PSD 解析结果当作完整语义校验结果。该状态是已接受的发布风险（D-038）。

---

## 5. 配置与运行限制

- `universalparser.limits.json` 只覆盖文件中显式出现的属性；缺失项保持默认值。
  JSON 损坏时回退到 `ParseLimits.Default`，启动状态栏显示 `Limits configuration ignored: ...`。
- 格式探测窗口与长时间读取（如 PNG CRC）使用固定 **512 KiB** 阈值，不阻断执行，也不可配置（D-037）。
- 报告没有总字节预算；Fields 与 Structure 完整写入，Hex 固定首尾各 128 KiB，超限时报告明确提示
  使用 `Export Raw`。Raw 导出完整且不因大小拒绝（D-034）。
- 解析不设深度/节点数/时间上限；取消是唯一主动中止路径（D-032）。

---

## 6. 发布验证

- [x] `dotnet build`：0 warning / 0 error
- [x] `dotnet test`：117 passed
- [x] `dotnet format --verify-no-changes`
- [x] 单文件 framework-dependent 发布到 `dist/`，启动验证通过
- [x] 支持矩阵与 `05-implementation-history.md` 一致
- [x] 已知限制在字段 `<Note>`、报告说明与本文档中可见
