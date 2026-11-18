# 05 — 实现历史与覆盖矩阵

> 已完成阶段的压缩记录。完整格式细节见 `08-format-notes.md`；
> Phase 6/7 的逐项关闭记录见 `archive/01-issue-closeout-2026-11-17.md`。

---

## 1. 阶段历史

| 阶段 | 日期 | 结果 |
|---|---|---|
| Phase 0 | 2026-10-18 | 产品决策、风格、架构、迁移顺序、检查清单 |
| Phase 1 | 2026-10-18 | Core source/range/reader、不可变节点、字段/诊断、`ParseLimits`、registry/probe、RawData、树校验器 |
| Phase 2 | 2026-10-18 | RIFF/RIFX/RF64/BW64/LIST、pad 范围、`ds64`、WAVE/AVI/WebP/ANI/DLS/Wwise 子集 |
| Phase 3 | 2026-10-18 ~ 11-15 | EBML（VINT/unknown size/RFC 9559）、FLV（AMF0/AMF3/Enhanced）、ASF（GUID object 树）、FBX（32/64 位 record/footer） |
| Phase 4 | 2026-11-15 | PNG/APNG（23 类 chunk、CRC）、JPEG（marker/segment/Exif/ICC）、ISOBMFF（box/sample table/分片/HEIF/QuickTime/3GPP） |
| Phase 5 | 2026-11-16 | GUI 重写：旧版布局/字体、`ParseSession`、树懒加载、虚拟字段列表、64 位 Hex、后台节点解析、流式导出 |
| Phase 6 | 2026-11-16 ~ 17 | BUG 修复与性能：读取窗口、Hex 坐标、零填充保护、EXIF/ID3 完整解析、统计视图、移除深度/节点数/时间限制（D-032）、EBML block 高熵化（D-033）、测试收缩为框架层 |
| Phase 7 | 2026-11-17 | 导出重做（D-034）、MIDI（D-035）、ICC（D-036）、512 KiB 阈值（D-037）、PSD/PSB（D-038）、Ogg（D-039）、FLAC（D-040）、Blender（D-041）、Protobuf（D-042）、NBT（D-043） |
| Phase 8 | 2026-11-18 | 文档压缩与归档；无注释 limits 示例；v1.0.0 单文件（framework-dependent）发布到 `dist/` |
| 维护 | 2026-11-19 | issue#1：统一文本规则、共享编码/转义/有界读取、`FrozenDictionary`、Protobuf 派生预览、诊断与 `RollForward` |
| 发布 | 2026-11-19 | v1.0.1 本地发布：`dist/UniversalParser_1.0.1.exe` + limits 示例；未做 git 提交 |
| 发布收尾 | 2026-11-19 | 手动审查通过后推送 `cce6ec8` 与 `v1.0.1`，发布 GitHub Release 两个附件；校验哈希与启动，保留 Issue #1 正文并补充关闭评论 |

Phase 6 起测试范围收缩为框架底层与部分格式回归；不再保留旧格式/GUI 测试。
当前 `dotnet test` 为 **170 passed**。

---

## 2. 格式覆盖矩阵

| 格式 | Parser id | 根节点 | 树命名 | 主要覆盖 |
|---|---|---|---|---|
| ISOBMFF | `isobmff` | `ISO Base Media File Format` | 4CC | 通用/电影/轨道/media/sample table/分片/QuickTime/3GPP/HEIF-AVIF；`stsd` 为叶子，sample entry/codec 以 `entry[n].` 字段输出 |
| RIFF | `riff` | `Resource Interchange File Format` | 4CC（`RIFF:WAVE`、`LIST:INFO`） | RIFF/RIFX/RF64/BW64、WAVE/AVI/WebP/ANI/DLS/Wwise；OpenDML `indx`/`ix##`/`dmlh`；`LIST:movi` 整体 `Unparsed` |
| EBML | `ebml` | `Extensible Binary Meta Language` | schema 名 | VINT、unknown size、RFC 9559 schema、Matroska/WebM；`SimpleBlock`/`Block`/`BlockVirtual` 高熵 `Unparsed` |
| FLV | `flv` | `Flash Video` | 语义名 | header/tag、AMF0/AMF3、Enhanced 视频 Multitrack、传统 CodecID 12；不解码媒体 |
| ASF | `asf` | `Advanced Systems Format` | object 语义名 | GUID object 树、Header/Data/Index、Properties/Content/Metadata/Codec/Padding；Data Packets 单一 `Unparsed` |
| FBX | `fbx` | `Filmbox` | 节点名 | 二进制 header、32/64 位 record、全部标量/数组类型、footer 可验证 framing；deflate 数组不解压 |
| PNG/APNG | `png` | `Portable Network Graphics` | 4CC | signature/chunk、`cICP`/`mDCV`/`cLLI`、PLTE/tRNS/bKGD 解释、CRC、文本/解压文本、`iCCP` 完整 ICC、APNG |
| JPEG | `jpeg` | `JPEG Image` | mnemonic | marker/segment、APPn Identifier 分派、Exif、XMP 原文、8BIM、单段 ICC、DQT/DHT/DRI/SOF/SOS/COM/SCAN |
| MIDI | `midi` | `Musical Instrument Digital Interface` | `MThd`/`MTrk`/事件名 | MThd、MTrk、逐事件 `delta_time`/status/data、running status、SysEx、全部标准 meta event |
| PSD/PSB | `psd` | `Adobe Photoshop Document` | section/record/block 名 | FileHeader、ColorModeData、ImageResources、LayerAndMaskInformation、ImageData；RLE/ZIP 像素不解码；**不稳定** |
| Ogg | `ogg` | `Ogg Encapsulation Format` | `Page` | page header/lacing/CRC；Vorbis/Opus/Theora/Speex/FLAC/Skeleton header；packet 字段加 `Packet[n].` 前缀 |
| FLAC | `flac` | `Free Lossless Audio Codec` | 全大写结构名 | `fLaC` + metadata block：STREAMINFO/PADDING/APPLICATION/SEEKTABLE/VORBIS_COMMENT/CUESHEET/PICTURE；音频帧整体 `Unparsed` |
| Blender | `blend` | `Blender File` | block code 4CC | 12/17 字节头、三种块头；DNA1/GLOB/TEST/ENDB；SDNA 标注；其余内存块 `Unparsed`；ZSTD/Gzip 按 Raw |
| Protobuf | `protobuf` | `Protocol Buffers` | wire type 规范名 | 不加载 `.proto`；varint/fixed32/fixed64/length-delimited/group；`field_number`/`wire_type`/`key`/值；文本/嵌套/字节启发式 |
| NBT | `nbt` | `Named Binary Tag` | 规范 tag 类型名 | 仅无压缩；12 种 tag、列表/compound；名称/索引在右侧视图；压缩 NBT/MCA 按 Raw |
| Raw | `raw` | `Raw Data` | — | 未知文件整块 `Raw` |

内嵌解析器：

| 组件 | 入口 | 覆盖 |
|---|---|---|
| EXIF | JPEG APP1、PNG `eXIf`、PSD 1058/1059 | IFD0→IFD1、Exif/GPS/Interop、MakerNote/缩略图标注 |
| ID3v2 | RIFF `id3 ` | v2.2/2.3/2.4、扩展头/footer、常用帧、CHAP/CTOC |
| ICC | PNG `iCCP`、JPEG APP2 单段、PSD 1039 | 128 字节 header、tag table、常见 tag type；LUT 只记结构 |
| 8BIM | JPEG APP13、PSD ImageResources | 常用 image resource 字段；私有/复杂资源保留 framing |

---

## 3. 关键契约

- 所有 offset/length 为 64 位；节点不可变。
- 子节点在父节点内，兄弟不重叠，未覆盖区间显式节点。
- 解析失败返回部分结果 + 诊断，不伪造 RawData。
- 大 payload 只记录范围；D-011 仅允许小体量、简单压缩元数据有界解码。
- 诊断只管格式，不验证跨块关系/语义内容（D-028）；未知/私有结构不猜字段。
- 解析不设时间/深度/节点数限制（D-032）；报告无总字节预算，Hex 固定首尾窗口（D-034）；
  Raw 导出完整。
- GUI 只消费 `ParseSession`（source + document），不持有可变流位置。
- 加载进度持续上报；无进度时 Marquee，不允许“卡住不动”。

---

## 4. 样本与测试策略

- 自动化测试只使用内存构造的最小样本，不依赖 `fixtures/`。
- `fixtures/` 只放临时真实样本，用于手工回归/字段质量检查；验证后删除。
- 记录来源、生成命令与预期结果时才允许加入样本；不提交来源不明的大文件。
- 当前保留：source/range/reader、node/field/diagnostic、engine/registry/session、
  树校验、导出/limits，以及 PSD/PSB、Ogg、FLAC、Blender、Protobuf、NBT 与 PNG CRC 回归。
