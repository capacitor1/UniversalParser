# 08 — 格式笔记

> **归档快照（2026-11-17）。** 非当前规范；当前格式笔记见 `../08-format-notes.md`。
> 本文件保留已删除样本的来源/生成命令、实现文件列表与逐项测试名等历史细节。

> 记录每个格式的规范来源、范围语义、已知坑、实现状态。
> 实现阶段每迁移一个格式，必须补充/修正本文件。

---

## 1. 通用规则

- 原始字段名以官方规范为准。
- 规范使用 snake_case / camelCase / PascalCase，就保持。
- 规范只给出带空格的 prose label（如 `Bit depth`）时，优先保留规范写法，不要擅自改成 `BitDepth`。
- 规范没有命名字段时，用描述性英文键，不加尖括号。
- 所有 offset/length 使用 64 位。
- 所有大 payload 只记录范围，不整块读取。
- 截断/非法长度必须有诊断。
- 诊断只针对格式本身：截断、长度、签名、块内枚举、CRC 等；跨块/跨字段语义不检查（D-028）。
- 节点范围默认覆盖 padding；例外必须在此记录。
- 节点标题与树名规则见 `03-style-guide.md` 第 5 节；块身份值（4CC/GUID/marker/节点名）不再作为字段重复输出。
- 文档根节点显示格式全称（D-014），不显示 parser id 或缩写。
- Hex 只按当前节点范围渲染，行尾不得读入下一个块的字节。
- 解析不设时间/深度/节点数限制（D-032），取消是唯一主动中止路径。报告无总字节预算；Fields/Structure 完整写入，Hex 段固定首尾各 128 KiB（D-034）；Raw 导出完整且无大小拒绝。

---

## 2. MPEG / ISOBMFF

### 规范

- ISO/IEC 14496-12（ISO Base Media File Format）
- 相关：ISO/IEC 14496-15（AVC/HEVC）、ISO/IEC 23008-12（HEIF）、AV1 ISOBMFF 等

### 原始标识

- Box 4CC：`ftyp`、`moov`、`trak`、`mdia`、`minf`、`stbl`、`stsd`、`stco`、`co64`、`udta`、`meta` 等
- `uuid` box 使用 16 字节 extended type

### 范围

- 普通 box：`size`（4 字节）+ `type`（4 字节）+ payload
- `size == 1`：`largesize`（8 字节）
- `size == 0`：延伸到父容器末尾 / 文件末尾
- `uuid`：header 额外 16 字节
- FullBox：payload 前 1 字节 `version` + 3 字节 `flags`

### 已知坑

- `(int)payloadSize` 截断
- `Data` box 的 type 21 可能是 1/2/3 字节，`BitConverter.ToInt32` 会抛
- `ReadNullTerminatedString` 无界扫描
- `size == 0` 在嵌套容器中的边界
- `stsd` 内部结构复杂，容易越界
- `stsd` 音频 sample entry：6 字节 reserved 之后的 `revision`(u16) + `vendor`(u32) 必须跳过，
  否则会把 `channelcount`/`samplesize` 当成采样率
- QuickTime 元数据容器 `©nam`/`©cmt`/`©ART`/`©too` 等以 `0xA9` 开头的 4CC 是容器，不是未知 box
- 大 `mdat` / `covr` 不能整块读

### 实现状态

- 命名空间 `UniversalParser.Core.Formats.IsoBmff`，parser id `isobmff`，显示名 `ISO Base Media File Format`，probe priority 170。
- 标题使用 `FileType 'ftyp'`、`(QuickTime)Title '©nam'` 形式；树显示 4CC。
- 树只读 box header：32 位 `size`、`size == 1` 的 64 位 `largesize`、`size == 0` 延伸到父范围末尾、`uuid` 的 16 字节 user type；越界 size 夹到父范围末尾并产生 `TRUNCATED_PAYLOAD`。
- 容器：`moov/trak/edts/mdia/minf/dinf/stbl/mvex/moof/traf/mfra/udta/ilst/ipro/sinf/schi/rinf/fiin/paen/meco/iprp/ipco/grpl/strk/strd/cinf/tref/trgr/hnti/hinf/----/tapt/meta/iinf/iref/dref/trep/gmhd`。
- 容器前缀：`meta` 通过探测区分 ISO FullBox 与 Apple 无 version/flags 变体；`iinf` 按 version 选择 16/32 位 entry_count；`iref` 跳过 4 字节；`dref` 跳过 8 字节；`ipro`/`fiin` 跳过 6 字节；`trep` 跳过 8 字节。`PayloadRange` 从第一个子 box 开始，保证 `NodeTreeValidator` 覆盖检查成立。
- `stsd` 仍是强制叶子；sample entry 与 codec 配置输出为 `entry[N].<child>.<field>` 字段，而不是树节点。
- 已覆盖 box：
  - 通用：`ftyp`、`styp`、`uuid`、`mdat`、`free`、`skip`、`wide`、`Xtra`、未知 box。
  - 电影/轨道：`mvhd`、`tkhd`、`mdhd`、`hdlr`、`elst`、`vmhd`、`smhd`、`url `、`urn `、`alis`、`iods`、`gmin`、`text`、`tcmi`、`clef/prof/enof`、`csgm`。
  - sample table：`stsd`、`stts`、`stsc`、`stsz`、`stco`、`co64`、`stss`、`sdtp`、`ctts`、`sbgp`、`sgpd`、`cslg`。
  - 分片：`mehd`、`mfhd`、`trex`、`tfdt`、`tfhd`、`trun`、`sidx`。
  - HEIF/AVIF：`pitm`、`iloc`、`idat`、`infe`、`ispe`、`irot`、`imir`、`pixi`、`colr`、`auxC`、`av1C`、`hvcC`、`clli`、`ipma`、entity-to-group。
  - QuickTime 元数据：`keys`、`mean`、`name`、`data`、`kywd`、`thmb`、纯文本（`clfn`/`reel`/`scen`/`shot`/`slno`/`manu`/`modl`/`cmid`/`swre`/`date`/`name`）、`©xxx`、`covr`、`stik`、`hdvd`。
  - 3GPP：`titl`、`dscp`、`perf`、`auth`、`gnre`、`cprt`、`coll`、`albm`、`kywd`、`clfn`、`reel`、`scen`、`shot`、`slno`、`manu`、`modl`、`cmid`、`swre`、`date`、`name`、`thmb`。
  - 引用/分组：`iref` 的 `dimg/base/eroi/evir/exbl/mask/mint/pred/prem/rpds/auxr/fdel/auxl/cdsc`、`tref` 的 `hint/hind/sync/subt/scal/sbas/tmcd/adda/adrc/avcp/swfr/swto/deps/cdtg/shsc/cdep`、`grpl` 的 entity-to-group 4CC。
  - sample entry codec：`avcC`（含 SPS/PPS 几何）、`hvcC/hvcE`、`esds`（含完整 AudioSpecificConfig）、`av1C`、`dfLa`（全部 metadata block）、`dOps`、`dac3`、`dec3`、`dvcC/dvvC/dvwC`、`vpcC`、`SmDm`、`CoLL`、`btrt`、`pasp`、`colr`、`clap`、`srat`、`frma`、`schm`、`sinf/schi/wave` 递归、`uri /uriI`、`name`、`damr`、`alac`、`mhaC`、`pcmC`、`chnl`、`dawp`、`d263`、`enda`；未知 codec 记录 `size` + 有界 hex。

### 上下文相关 4CC

- 树的 `stableId` 编码为 `isobmff:{start}|{parentType}`，节点解析时取回父 box 类型。
- `thmb`：父为 `udta`/`meta` 时是 3GPP thumbnail，父为 `iref`/`tref` 时是引用；无父上下文时探测前 4 字节是否为全零 FullBox。
- `iref` 子 box 的 item ID 宽度：父 `iref` 的 version 被容器前缀跳过，因此按 payload 长度匹配 `4 + 2n`（16 位）或 `8 + 4n`（32 位）；两者都匹配时选 16 位并给出警告。
- `stsd` 内部的 `hvcC`/`av1C`/`colr`/`keys`/`text` 与 `ipco`/`gmhd` 下的同名 4CC 使用不同字段命名，由树位置决定。

### 限制

- 所有表循环上限来自 `ParseLimits.MaxDataLines`，超限产生 `LIMIT_RECORDS`。
- 大 payload（`mdat`、`idat`、`covr` 图像、FLAC PICTURE）只输出长度与有界 hex 前缀，不解码。
- 已补全：`esds` DecoderSpecificInfo → AudioSpecificConfig（AOT 31、采样率表、显式 24 位频率、SBR/PS、GASpecificConfig）；H.264 SPS/PPS 几何（去 emulation-prevention、scaling list、UE/SE）；FLAC `dfLa` 全部 metadata block（STREAMINFO/APPLICATION/SEEKTABLE/VORBIS_COMMENT/CUESHEET/PICTURE）；HEIF `infe` v1 的 `fdel`；`alis` tag 表。
- `esds` DecoderConfigDescriptor 的 `buffer_size_db` 是 24 位（不是 32 位）；误按 32 位读取会错位并丢掉后面的 DecoderSpecificInfo。
- `alis` 的结束标记 `0xFFFF` 只有 2 字节，tag 循环条件必须是“剩余 ≥ 2”，否则会漏掉结束标记。
- 64 位 size 的 `largesize > long.MaxValue` 被拒绝为非法长度。

### Fixtures

- `fixtures/isobmff/basic.mp4`（H.264 + AAC，ffmpeg）
- `fixtures/isobmff/basic.mov`
- `fixtures/isobmff/basic.3gp`
- `fixtures/isobmff/basic.m4a`
- `fixtures/isobmff/fragmented.mp4`（`frag_keyframe+empty_moov`）
- `fixtures/isobmff/basic.avif`（libaom-av1 still picture）
- 合成测试覆盖 64 位 size、`size == 0`、`uuid`、截断、trailing、`stsd` avc1、音频 sample entry、`tref`/`thmb` 上下文、entity-to-group、`trun` 样本行。

---

## 3. RIFF

### 规范

- Microsoft RIFF specification
- 具体 chunk 规范：WAV、AVI、WebP、WEM 等
- [OpenDML AVI File Format Extensions 1.02](http://www.jmcgowan.com/odmlff2.pdf) §3 / §5：super / standard / field index 与 `dmlh`。

### 原始标识

- `RIFF` / `RF64` / `LIST`
- chunk 4CC：`fmt `、`data`、`fact`、`cue `、`avih`、`idx1`、`VP8X` 等
- LIST type：`INFO`、`adtl` 等

### 范围

- chunk：`ckID`（4 字节）+ `ckSize`（4 字节）+ data
- `ckSize` 不含 header
- 奇数长度后有 1 字节 pad
- **新项目决策：pad 计入节点长度，保证范围连续**

### 已知坑

- pad 必须计入 `Length`，否则节点之间出现空隙
- 嵌套 LIST 的 pad
- RF64 的 64 位 size
- 顶层 trailing data
- 真正未知的 chunk ID：整个 block（header + payload + pad）为 `NodeKind.Unparsed`
- `LIST` 的 type 是 payload 前 4 字节
- AVI `LIST:movi` 整个块作为 `Unparsed`，不展开子块（D-016）
- 短 chunk 的 Hex 最后一行容易读入下一个 chunk 的字节；渲染必须按节点范围截断，不能固定读满 16 字节
- 身份字段：树显示 4CC（`RIFF:WAVE`、`LIST:INFO`），`ckID` 不再作为字段重复输出

### Fixtures

- WAV 奇数 chunk
- AVI 嵌套 LIST
- WebP
- RF64
- 截断
- 非法 size
- 顶层 trailing

### 实现要点

- 根 chunk 支持 `RIFF` / `RIFX` / `RF64` / `BW64`；`RIFX` 子块使用大端。
- `RF64` / `BW64` 的 `ds64` 在根 chunk 扫描时读取；`data` 的 `0xFFFFFFFF` size 优先取 `ds64.DataSize`，缺失时回退到可用范围并产生诊断。
- `LIST` 的 type 后递归；未知 LIST type 仍递归，避免把可见子块当成 raw payload。
- AVI `LIST:movi` 例外：整个块保持 `Unparsed`，只显示 `listType`、长度与说明 Note。
- WebP `ANMF` 的 16 字节帧头之后递归子 chunk；`VP8X` / `ANIM` 提供常用字段。
- WAVE `fmt ` 支持 WAVEFORMATEX 与 EXTENSIBLE GUID；`data` 不做 payload 解码；`wavl` 作为无类型容器递归 `data`/`slnt`。
- AVI `avih` / `strh` / `strf` / `idx1` 提供常用字段；OpenDML `dmlh` 显示 `dwTotalFrames`；
  `indx` 按 OpenDML AVI File Format Extensions 解析 `wLongsPerEntry`、`bIndexSubType`、`bIndexType`、
  `nEntriesInUse`、`dwChunkId` 与条目（也注册 `ix00`–`ix99`）：
  - `bIndexType = 0`（AVI_INDEX_OF_INDEXES）：`aIndex[n].qwOffset` / `dwSize` / `dwDuration`；
  - `bIndexType = 1`（AVI_INDEX_OF_CHUNKS）：`qwBaseOffset`、`dwReserved3`、`aIndex[n].dwOffset` / `dwSize`，
    `dwSize` 第 31 位表示非关键帧（只作行内解释）；
  - `bIndexSubType = 1`（AVI_INDEX_2FIELD）：再读 `aIndex[n].dwOffsetField2`；
  - `AVI_INDEX_IS_DATA = 0x80` 及其它未覆盖布局保留 `aIndex[n].adw[k]` 与未解释长度；不猜冷门结构。
  - `wLongsPerEntry` 决定步长；逐条读取，未使用 capacity 与有界省略区间仍可查看。
  - 不跟随索引指针、不校验目标块或 duration；`dmlh` 的 4 字节 `dwTotalFrames` 之后保留扩展范围。
- ANI `anih` / `icon`（ICONDIR）已解析。
- DLS `colh` / `vers` / `msyn` / `insh` 已解析；`ins ` / `lrgn` / `rgn ` / `rgn2` / `wave` 作为容器递归；`ptbl` / `rgnh` / `wsmp` 等保留 raw。
- Wwise `BKHD` / `DIDX` / `HIRC` 提供 header/index/count；HIRC 对象体和 DATA 媒体内容不解码。
- 仓库样本 `*.bank` 实际是 `RIFF 'FEV '`（FMOD event bank），不是 Wwise BKHD；通用 RIFF 树和 `LIST` 解析可正常覆盖，`FMT ` / `BNKI` 等 FMOD 块已登记名称但无字段 handler，按 `Raw` 保留；真正未知的 chunk ID 才整块 `Unparsed`。

---

## 4. EBML

### 规范

- RFC 8794（EBML）
- RFC 9559（Matroska / WebM schema）
- 旧 Matroska 文件中仍常见的 legacy 元素作为兼容补充

### 原始标识

- Element ID（VINT，1–4 字节）
- Element Data Size（VINT，1–8 字节）
- 常见：`EBML`、`Segment`、`Info`、`Tracks`、`Cluster`、`Cues`、`Tags`
- 全局元素：`Void`、`CRC-32`
- 官方字段名保持原样：`EBMLVersion`、`DocType`、`TimestampScale`、`SimpleBlock`、`CRC-32`、`DateUTC` 等。
- 树显示 schema 名（`EBMLVersion`、`Segment`），标题为 `Segment '0x18538067'`；`<ElementID>` 不再作为字段重复输出。

### 范围

- element：ID + data size + payload
- 已知 size：payload 严格落在声明范围内；父容器不足时截断到父边界并诊断。
- unknown size：只有 `Segment` / `Cluster` 允许；其他 master 或叶子出现 unknown size 会产生 `UNKNOWN_SIZE` 诊断。
- unknown-size `Segment`：在下一个 `EBML` 或 `Segment` 前结束；否则延伸到文件末尾。
- unknown-size `Cluster`：在下一个顶层元素（`Segment` 子元素，例如新的 `Cluster`）前结束；`Timestamp` / `SimpleBlock` 等 Cluster 内部子元素不会终止它；否则延伸到父元素末尾。
- `Void` / `CRC-32` 是全局元素，永远不终止 unknown-size 父元素。
- 未知元素：整个 block（header + payload）作为 `NodeKind.Unparsed`，不产生子节点。
- `SimpleBlock`（0xA3）、`Block`（0xA1）、`BlockVirtual`（0xA2）是高熵音视频 payload，整块作为 `NodeKind.Unparsed`：
  只保留范围、长度派生字段与短 Note，不解析 track/timecode/flags/lacing/帧数据，也不做内联字节预览（D-033）。
- 已知 EBML 叶子：`NodeKind.Leaf`；`Void`：`NodeKind.Padding`；master：`NodeKind.Container`。
- 容器内或顶层的 trailing/非法 header：显式 `NodeKind.Unparsed` 节点，范围覆盖剩余字节。
- 根 `EBML File` 是合成节点，范围覆盖整个文件；其 `PayloadRange` 也是整个文件。
- unknown-size 父节点若遇到无法继续的边界，剩余范围用显式节点覆盖，占位节点不得越过父范围。

### 已知坑

- VINT 前导位/长度；ID 最多 4 字节，size 最多 8 字节。
- data size 全 1 位表示 unknown size，不是普通长度。
- unknown-size 边界不能简单延伸到父末尾，否则会吞掉后续 `Segment`/`Cluster`。
- `CRC-32` 官方名带连字符；不要写成 `CRC32`。
- `Void` 的 payload 是 padding，不应按普通二进制字段展示。
- 文本可能包含非法 UTF-8；解码不能抛异常。
- 日期是自 2001-01-01 UTC 起的 signed nanoseconds；派生 ISO 值可能超出 `DateTimeOffset` 范围。
- 大 `SimpleBlock` / `CodecPrivate` / `Tags` 不能整块进内存；block 只按 `Unparsed` 记录范围。
- unknown-size 父节点遇到无法继续的边界时，剩余范围必须显式覆盖，且占位节点落在父范围内。
- unknown size 不写进节点标题；边界按 `Segment` / `Cluster` 规则计算，不能一律延伸到父末尾。

### Fixtures

- 普通 mkv/webm
- unknown-size `Segment`
- unknown-size `Cluster` 后跟下一个 `Cluster`
- unknown element
- `Void` / `CRC-32`
- 截断 payload / 非法 VINT / 顶层 trailing
- `SimpleBlock` / `Block` / `BlockVirtual` 高熵 `Unparsed`
- 超深嵌套

### 实现要点

- 新增 `Formats/Ebml/`：schema、VINT/header reader、document tree parser、lazy node parser、formatting、parser registration。
- schema 共 234 个元素定义（RFC 9559 + legacy）。
- 三个真实样本 `Samples/EBML/42324134652.mkv`、`bd7e2f485ae83124.webm`、`sticker (9).webm` 通过：
  - `NodeTreeValidator.Validate(..., requirePayloadCoverage: true)`
  - 每个节点 `ParseNode` 无 `Error` 诊断
  - 顶层 `EBML` + `Segment` 及已知子元素存在
- 测试：`EbmlParserTests`（合成 VINT/unknown-size/截断/字段）、`EbmlSampleTests`（真实样本扫描）。

## 5. FLV

### 规范

- Adobe Flash Video File Format Specification（v10.1 或更新）
- [Veovera Enhanced RTMP v2](https://veovera.org/docs/enhanced/enhanced-rtmp-v2.html)，版本 `v2-2026-01-31-r2`：视频 Multitrack framing。
- [Adobe AMF3 Specification, January 2013（原文存档）](https://web.archive.org/web/20220122035930/https://www.adobe.com/content/dam/acom/en/devnet/pdf/amf-file-format-spec.pdf)：§1.3 / §2.2 / §3，含 Vector / Dictionary。

### 原始标识

- Header：`Signature`（`FLV`）、`Version`、`Flags`、`DataOffset`
- Tag：`TagType`、`DataSize`、`Timestamp`、`TimestampExtended`、`StreamID`
- `PreviousTagSize`
- 树显示语义名（`FLVHeader`、`TagHeader`、`ScriptTagData`），标题为 `FLVHeader 'FLV'`、`TagHeader '0x12'`；`Signature` 不再作为字段重复输出。

### 范围

- FLV header：9 字节 + `DataOffset` 扩展 + `PreviousTagSize0`（4 字节）
- Tag：11 字节 header + data + `PreviousTagSize`（4 字节）
- 新项目节点长度包含 `PreviousTagSize`

### 已知坑

- `PreviousTagSize0` 在 header 后
- tag 的 `PreviousTagSize` 是前一个 tag 的总大小（11 + data）
- 截断 tag
- tag 数不设上限，循环由范围和取消保护
- 未知 tag type
- 节点标题使用原始 tag 类型；identifier 只作为解释。

### Fixtures

- 普通 FLV
- audio/video/script tag
- 截断 tag
- 未知 tag
- 大量 tag（无节点预算）
- 顶层 trailing

### 实现要点

- 新增 `Formats/Flv/`：`FlvParser`、`FlvDocumentParser`、`FlvNodeParser`、`FlvAmf0Parser`、`FlvFormatting`、`FlvConstants`。
- 树节点：
  - 合成根 `FLV File` 覆盖整个文件。
  - `FLV Header` 覆盖 9 字节头 + `DataOffset` 扩展区。
  - `PreviousTagSize0` 单独节点。
  - 每个 tag 为 `Container`，子节点为 `Tag Header`、`Tag Data`、`PreviousTagSize`；tag 节点范围包含 `PreviousTagSize`。
  - 未知 `TagType` 的 `Tag Data` 为 `NodeKind.Unparsed`。
  - 截断/顶层 trailing 显式 `Unparsed`/`Synthetic` 节点。
- 字段命名按规范：`Signature`、`Version`、`Flags`、`DataOffset`、`PreviousTagSize0`、`TagType`、`Filter`、`DataSize`、`Timestamp`、`TimestampExtended`、`StreamID`、`PreviousTagSize`、`SoundFormat`、`SoundRate`、`SoundSize`、`SoundType`、`AACPacketType`、`FrameType`、`CodecID`、`AVCPacketType`、`HEVCPacketType`、`CompositionTime`。
- Enhanced FLV / Enhanced RTMP：
  - 识别 `IsExHeader`、`VideoFrameType`、`VideoPacketType`、`VideoFourCc`。
  - 支持 `SequenceStart` / `CodedFrames` / `SequenceEnd` / `CodedFramesX` / `Metadata` / `MPEG2TSSequenceStart` / `Multitrack` / `ModEx` 的头部字段。
  - `CodedFrames` 对 AVC/HEVC/VVC 读取 `compositionTimeOffset`；`CodedFramesX` 派生为 0。
  - `Metadata` 按 AMF0 名称-值对解析（例如 `colorInfo`）。
  - `ModEx` 读取 `modExDataSize`、`modExData`、`videoPacketModExType`、`videoTimestampNanoOffset`。
  - Multitrack（`VideoPacketType = 6`）按 E-RTMP v2 展开：
    - 头部字节高 4 位是 `videoMultitrackType`（`AvMultitrackType`：0 OneTrack、1 ManyTracks、2 ManyTracksManyCodecs），低 4 位是 track 的 `videoPacketType`；
    - `ManyTracksManyCodecs` 时每条 track 前有独立 `videoFourCc`，否则共用既有 `VideoFourCc` 字段；
    - 每条 track 读取 `videoTrackId`（UI8）；非 `OneTrack` 时再读 `sizeOfVideoTrack`（UI24）并按其范围前进；
    - track 内按 packet type 读取 `compositionTimeOffset` 等头部字段，媒体 payload 仍不解码；
    - `sizeOfVideoTrack` 从该字段之后开始计数，包括 CTS/metadata；零长度 `SequenceEnd` track 合法，
      ID/size 已经推进偏移，不能误报死循环。AMF metadata 按 track 前缀解析，不能读入下一 track。
    - 循环受 `MaxDataLines`、范围与取消令牌限制，越界只产生诊断。
- 传统 CodecID 12（HEVC 旧扩展）：
  - `FrameType` + `CodecID = 12` + `HEVCPacketType` + `CompositionTime`。
  - 只做头部字段，不解码 HEVC 码流；文档中标注这是 legacy 约定，Enhanced FLV 用 `hvc1`/`hev1`。
- Script tag：
  - AMF0 解析 `onMetaData` 与任意名称-值对。
  - 元数据键按文件原样显示，嵌套对象用 `parent.child`，数组用 `path[index]`。
  - 支持 Number/Boolean/String/Object/ECMA Array/Strict Array/Date/Long String/XML/Typed Object；AMF0 marker `0x11` 进入 AMF3。
  - AMF3 支持：Undefined/Null/False/True/Integer(U29)/Double/String/Date/Array/Object/XMLDocument/XML/ByteArray/Vector/Dictionary；
    维护 string/object/traits 三张表，字符串与 traits 引用用于布局解析；object 引用显示 `@ref n`
    与已知目标，不递归展开对象图。空字符串不进 string table；表在每个 script tag 重置。
  - AMF3 externalizable object 的 payload 长度无法从规范得知，只显示 `<ClassName>` 与 Note，停止该值并保留剩余范围。
  - AMF3 的 U29、字符串与条目数同样受 `MaxStringBytes` / `MaxDataLines` 限制；嵌套深度不设软件上限。
  - ByteArray 只读有界 Hex 前缀并按长度跳过，不猜 zlib/嵌套 AMF；UTF-8 字节预算切中字符时只显示完整字符前缀。
  - 字符串受 `MaxStringBytes`、值数量受 `MaxDataLines` 限制。
- `PreviousTagSize` 与 `PreviousTagSize0` 都会校验期望值，不匹配产生 `PREVIOUSTAGSIZE_MISMATCH`。
- 未知/加密/过滤 tag：`Filter = 1` 时只显示头部与 `<PayloadLength>`，不解析 payload。
- 已知限制：
  - 不解码音频/视频 payload。
  - Enhanced audio（SoundFormat 9 扩展）暂不展开。
  - 加密/过滤 payload 不解析。
  - AMF3 externalizable object 不解析内部 payload。
- 样本：
  - `fixtures/flv/minimal-av.flv`、`audio-only.flv`、`video-only.flv`：ffmpeg 生成（H.264/AAC）。
  - `fixtures/flv/synthetic-hevc.flv`：`generate_synthetic_hevc.py` 生成，覆盖 Enhanced `hvc1` 与 legacy CodecID 12 头部；payload 为占位字节，不可解码。
  - 生成命令与预期见 `fixtures/flv/README.md`。
- 测试：`FlvParserTests`（合成结构、截断、缺 PreviousTagSize、未知 tag、无子节点预算、PreviousTagSize 校验、AMF0、Codec 12 legacy/enhanced）、`FlvSampleTests`（真实/合成样本树覆盖与全节点解析）。

---

## 6. ASF

### 规范

- Microsoft Advanced Systems Format (ASF) Specification, Revision 01.20.05, June 2010。
- 开发时使用官方 PDF 提取文本作对照；提取文件只作参考，不进入仓库。
- GUID 名称另参考 getID3 `module.audio-video.asf.php`；非规范 GUID 必须在文档和字段中明确标注来源。

### 原始标识

- 每个 object：`Object ID` GUID（16 字节）+ `Object Size` QWORD（8 字节，little-endian，**包含 24 字节 object 头**）+ payload。
- Header Object：`Number of Header Objects` DWORD、`Reserved1` BYTE = 1、`Reserved2` BYTE = 2。
- Header Extension Object：`Reserved Field 1` GUID、`Reserved Field 2` WORD = 6、`Header Extension Data Size` DWORD。
- Data Object：`File ID` GUID、`Total Data Packets` QWORD、`Reserved` WORD = `0x0101`、`Data Packets`。
- 文件级 object：Header Object、Data Object、Simple Index / Index Object；Header 内可嵌套 Header Extension。
- 树显示 object 语义名（`HeaderObject`、`FilePropertiesObject`），标题为 `FilePropertiesObject '{75B22630-668E-11CF-A6D9-00AA0062CE6C}'`；`Object GUID` 不再作为字段重复输出。

### 范围

- object 总长度 = `Object Size`；payload 长度 = `Object Size - 24`。
- Header Object 的直接子 object 从 object 头后 6 字节（即文件内 offset + 30）开始。
- Header Extension 的子 object 从 object 头后 22 字节（offset + 46）开始；子数据长度由 `Header Extension Data Size` 限定。
- Data Object 的 packet 区域从 object 头后 26 字节（offset + 50）开始。
- `Object Size = 0` 只允许用于 Data Object，且要求 File Properties 的 Broadcast Flag 已置位；首版把该 Data Object 截到源文件末尾。
- Data Object 内的 packet 不是 GUID object，不建立逐 packet 节点；整个 packet 区域是一个 `Unparsed` 子节点。

### 已知坑

- GUID 字节序：`Guid.ToByteArray()` / `new Guid(ReadOnlySpan<byte>)` 与 ASF 磁盘布局一致（前三段混合端序）；不要手工按全大端解释。
- `Padding Object` 没有 `Padding Data Size` 字段；只有 object 头和原始 `Padding Data`，长度 = `Object Size - 24`。
- `Object Size` 是总长度，不是 payload 长度。
- Header Object 的 `Number of Header Objects` 只统计直接 header object；不匹配时报告 `SCHEMA_MISMATCH`，但仍保留所有可解析 child。
- Header Extension 的 `Header Extension Data Size` 只描述子对象数据；超出 object 末尾时截断并报告。
- Stream Properties 的 `Reserved` DWORD 在真实 WMV 中可能非 0（`book-1.wmv` 为 `0x2B371113`）；不要因此报警。
- Stream Properties 的 `Flags`：bit 0–6 是 Stream Number，bit 7–14 保留，bit 15 是 Encrypted Content Flag；不要用 `0x0001` 当加密标志掩码。
- Metadata Object 的 BOOL 是 2 字节；Extended Content Description 的 BOOL 是 4 字节。
- Content Description / Extended Content Description 的字符串长度单位是字节；Codec List 的 name/description 长度单位是 WCHAR（×2）。
- Metadata / Metadata Library 的 `Name Length` 与 `Data Length` 单位是字节。
- Index Object 三种变体的首字段不同：
  - Index Object：`Index Entry Time Interval` DWORD
  - Media Object Index：`Index Entry Count Interval` DWORD
  - Timecode Index：`Reserved` DWORD
- Simple Index 的 `Index Entry Time Interval` 是 QWORD，不是 DWORD。
- 真实样本中的 `D9AADE20-7C17-4F9C-BC28-8555DD98E2A2` 不在官方规范对象表中；getID3 / Audio-WMA 称为 `Index Placeholder Object`，首版按该名称显示，内容仍保持 `Unparsed`。
- UTF-16 字符串经常带结尾 NUL；按声明长度读取，用 `\0` 转义显示，不擅自截断文件内容。
- 未知 GUID，或已知但未实现的 object，整块保留为 `Unparsed`，显示 GUID 与 `<UnparsedLength>`，不猜字段。
- Data Object 的 `Total Data Packets` 与 packet 区域长度不做强校验；packet 层不解析，避免把高熵 payload 当作结构。

### 实现要点

- 新增 `src/UniversalParser.Core/Formats/Asf/`：

| 文件 | 职责 |
|---|---|
| `AsfParser.cs` | 注册、Probe、Parse/ParseNode 入口 |
| `AsfDocumentParser.cs` | GUID object 树、Header/Header Extension/Data 容器、范围与占位节点 |
| `AsfNodeParser.cs` | 惰性读取各 object 字段与 Data Packets 区域 |
| `AsfFieldReader.cs` | 有界字段读取、UTF-16/截断/限制处理 |
| `AsfObjectHeader.cs` | 解析后的 object header 记录 |
| `AsfFormatting.cs` | GUID、时间、FileTime、标志、FourCC、UTF-16 显示 |
| `AsfConstants.cs` | 官方 GUID、固定长度、保留值 |
| `AsfGuids.cs` | object/stream/error-correction GUID 名称映射 |

- 注册：`ParserRegistry.CreateDefault()` 首位加入 `AsfParser`，`ProbePriority = 130`；Probe 检查文件头 16 字节是否为 `ASF_Header_Object`，命中返回 `Strong`。
- 已解析 object：
  - Header、Data、Simple Index
  - File Properties、Stream Properties、Header Extension、Extended Stream Properties
  - Content Description、Extended Content Description
  - Stream Bitrate Properties、Codec List、Language List
  - Metadata、Metadata Library、Compatibility、Padding
  - Index / Media Object Index / Timecode Index 的公共头部与 specifier；Index Blocks 保留为未解析
- Stream Properties：
  - `ASF_Video_Media` 解析 `Encoded Image Width/Height`、`Reserved Flags`、`Format Data Size`、40 字节 `BITMAPINFOHEADER`（`biCompression` 显示 FourCC）。
  - `ASF_Audio_Media` 解析 `WAVEFORMATEX` 字段，codec data 保留范围。
  - 其它 stream type 只保留 type-specific 范围。
- Data Object：
  - 只建立 `Data Packets` 一个 `Unparsed` 子节点，稳定 ID 为 `asf:data-packets:{offset}`。
  - 不按 packet 切分，不解码 packet header/payload，不显示高熵内容。
  - `Object Size = 0` 的 Data Object 延伸到文件末尾。
- 诊断：
  - 复用 `TRUNCATED_HEADER`、`TRUNCATED_PAYLOAD`、`TRUNCATED_CHILD`、`INVALID_LENGTH`、`INVALID_SIGNATURE`、`SCHEMA_MISMATCH`、`LIMIT_STRING`、`UNPARSED_PAYLOAD`。
  - 新增 `LIMIT_RECORDS`，用于描述符/元数据/Index entry 等记录数超限。
- 测试：
  - `AsfParserTests`：Probe、正常文件树覆盖与全节点解析、截断 Data Object、短 object header、非法 Object Size、未知 GUID、Header object count 不匹配、Data Packets 单区域、零长度 Data Object、逐节点范围一致性。
  - `AsfSampleTests`：所有 `fixtures/asf/*.wmv|*.asf|*.wma` 树覆盖、全节点无 Error、真实样本无 Warning、`book-1.wmv` 关键字段断言。

### Fixtures

- `fixtures/asf/book-1.wmv`：真实 WMV3 样本，8,357,239 字节；来源与预期见 `fixtures/asf/README.md`。
- 结构覆盖：Header（6 个直接 object，含 Header Extension、Extended Content Description、Padding、Index Placeholder）、Data Object、Simple Index。
- 预期：树覆盖通过；所有节点解析无 Error/Warning；`Data Packets Count = 522`、`Maximum Bitrate = 8037126`、视频 `WMV3` 1280×720、Simple Index 14 个 entry。

---

## 7. FBX（二进制）

### 规范

- Autodesk 没有公开完整的二进制 FBX 规范；FBX SDK 是闭源许可。
- 主要依据：Blender Foundation 公开发布的 *FBX binary file format specification*（Alexander Gessler 整理，Campbell Barton 审阅，public domain），覆盖 FBX 7.x 的 header、node record、property 与 array 编码。
- 64 位记录头的依据：Autodesk FBX 7.5 发布说明（2016+ 使用 64 位 block header 支持 > 2GB 文件）。
- footer 没有 Autodesk 完整公开规范；参考 [Blender `encode_bin.py`](https://github.com/blender/blender-addons/blob/main/io_scene_fbx/encode_bin.py)
  与 [FbxWriter `FbxBinary.cs`](https://github.com/hamish-milne/FbxWriter/blob/master/Fbx/FbxBinary.cs)，
  以仓库 Blender 7400 / SDK 7500 样本复核共同 framing；footer code/id 不作通用解密（D-029）。
- 非官方资料之间对 header 第 21/22 字节的解释不一致；本解析器按实际字节展示，并始终按 little-endian 读取。

### 原始标识

- 文件头 27 字节：
  - 0–19：`Kaydara FBX Binary  `（20 字节，末尾两个空格）
  - 20：`0x00`
  - 21：`0x1A`（观察值）
  - 22：`0x00`（观察值）
  - 23–26：`Version`（uint32 little-endian，例如 7400）
- node record（FBX < 7500）：
  - `EndOffset` uint32
  - `NumProperties` uint32
  - `PropertyListLen` uint32
  - `NameLen` uint8
  - `Name`（NameLen 字节，无 NUL 结尾）
  - 属性列表（PropertyListLen 字节）
  - 可选子节点列表 + null record（13 字节全零）
- node record（FBX ≥ 7500）：前三个字段改为 uint64，null record 变为 25 字节全零。
- property：`TypeCode`（1 字节）+ 数据。
  - 标量：`Y` int16、`C` bool（1 字节）、`I` int32、`F` float、`D` double、`L` int64。
  - 字符串/原始数据：`S`、`R`，均为 `Length` uint32 + 数据。
  - 数组：`f` float、`d` double、`l` int64、`i` int32、`b` bool；字段为 `ArrayLength` uint32、`Encoding` uint32、`CompressedLength` uint32 + 数据。
- `Encoding = 0`：原始数组；`Encoding = 1`：deflate（zlib）压缩数组。
- 树显示节点名，标题为 `Node 'Model'`；`Name` 不再作为字段重复输出。

### 范围

- node 范围 = `[record start, EndOffset)`；`EndOffset` 是**文件绝对偏移**，不是相对长度。
- 有子节点时：子节点从属性列表之后开始，到 `EndOffset - nullRecordSize` 结束；null record 属于 node 范围但不属于 `payloadRange`。
- 没有子节点时：`EndOffset` 正好等于属性列表末尾。
- 文件头 `[0, 27)` 是显式 `Header` 节点。
- 顶层节点列表从 27 开始；现代 FBX 文件**没有**包住所有顶层节点的匿名 root record（Blender 文档的“top-level object record”在真实文件中可缺失）。若遇到匿名节点，仍按普通节点显示为 `<unnamed node>`。
- 顶层 null record 之后是 footer。footer 在树中是显式 `Footer` 节点，并拆成可验证的子项：
  - 16 字节 footer code/id（不透明原始字节）；
  - 4–20 字节零填充；Blender 先写 4 字节零再对齐，SDK 可先对齐再写 4 字节零，FbxWriter 固定 20 字节；
  - 4 字节 version 回显（小端）；从已确认的固定尾部结构定界，不假设它一定 16 字节对齐；
  - 120 字节零；
  - 16 字节固定尾部签名 `F8 5A 8C 6A DE F5 D9 7E EC E9 0C E3 75 8F 29 0B`。
- 只有尾部签名、零填充与范围符合已确认的 framing 时才拆开；否则整个 footer 为 `Unparsed` + Note。
  version 与 header 是否一致不检查。`Footer ID` / `Version` / `Trailer` 是本工具为未正式命名字节区间取的描述性键。
- 典型 7400 文件 footer 总计 183 字节（13 字节 null record + 170 字节 footer）；7500 的 null record 为 25 字节。

### 已知坑

- `EndOffset` 是绝对偏移；按相对长度解释会立即错位。
- 版本 7500 起前三个字段是 64 位；用 32 位解析 7.5 文件会读出荒谬的 `EndOffset`。
- null record 只在有子节点列表时出现；叶子节点没有 13/25 字节结尾。
- `EndOffset` 包含 null record；计算 `payloadRange` 时必须减去 null record 长度。
- 顶层节点列表不一定有匿名 root record；不要假设第一个 record 包住整个文件。
- 属性长度可变，不能按固定大小跳过；树阶段直接按 `PropertyListLen` 跳过，字段阶段再逐项解析。
- `TypeCode` 大小写敏感：`I` 是 int32，`i` 是 int32 数组；`S` 是字符串，`s` 未定义。
- 数组的 `Encoding = 1` 是 deflate；本解析器**不解压**，只展示 `ArrayLength`、`Encoding`、`CompressedLength` 与派生未压缩长度。
- `Encoding = 0` 时 `CompressedLength` 应等于 `ArrayLength × 元素大小`；不一致时报告 `SCHEMA_MISMATCH`。
- 字符串是字节序列，通常是 UTF-8，也可能含 NUL；非法 UTF-8 报告 `INVALID_ENCODING`，非法字节显示为替换字符。
- `NameLen` 只有 1 字节，节点名最长 255 字节。
- footer 内容没有官方规范；不要按 node record 解析 footer，否则会把 16 字节 id/尾部签名当成 `EndOffset`。
- footer id 不是顶层 `FileId` 节点；顶层 `FileId` 是正常 FBX node，footer id 是独立的不透明 16 字节块，不能互相替代。
- footer 结构校验失败时不得硬解字段；整体回退 `Unparsed`，避免把旧版/非 SDK 文件误读成 footer 字段。
- 截断必须走诊断 + 显式节点，不能拼字符串后缀改节点名。
- 文件头属于框架数据，但本实现用显式 `Header` 节点表示，而不是“忽略且不显示”。

### 实现要点

- 新增 `src/UniversalParser.Core/Formats/Fbx/`：

| 文件 | 职责 |
|---|---|
| `FbxParser.cs` | 注册、Probe、Parse/ParseNode 入口 |
| `FbxDocumentParser.cs` | 文件头、node record 树、child/null record、footer、范围与占位节点 |
| `FbxNodeParser.cs` | 惰性读取 node header 与属性列表 |
| `FbxFieldReader.cs` | 有界 little-endian 读取、截断转诊断 |
| `FbxNodeHeader.cs` | 解析后的 node record header |
| `FbxFormatting.cs` | UTF-8 转义、类型名、浮点、Hex 显示 |
| `FbxConstants.cs` | magic、版本阈值、记录长度、footer magic |

- 注册：`ParserRegistry.CreateDefault()` 首位加入 `FbxParser`，`ProbePriority = 140`；Probe 检查 20 字节 `Kaydara FBX Binary  `，完整 header 返回 `Strong`，只有 magic 返回 `Weak`。
- 支持：
  - FBX < 7500：32 位 node record；FBX ≥ 7500：64 位 node record。
  - 全部标量类型、`S`/`R`、`f`/`d`/`l`/`i`/`b` 数组。
  - 数组 `Encoding = 0` 显示前 16 个元素预览；`Encoding = 1` 只显示声明长度与 Note，不解压。
  - 顶层 null record / footer 识别；符合已确认 framing 时拆出 code/id、padding、version、120 字节零区与 trailer，否则整块 `Unparsed`。
- 诊断：
  - 复用 `TRUNCATED_HEADER`、`TRUNCATED_PAYLOAD`、`TRUNCATED_CHILD`、`INVALID_LENGTH`、`INVALID_SIGNATURE`、`INVALID_ENCODING`、`SCHEMA_MISMATCH`、`UNSUPPORTED_VERSION`、`LIMIT_RECORDS`、`LIMIT_STRING`、`UNPARSED_PAYLOAD`。
  - 未新增诊断码。
- 测试：
  - `FbxParserTests`：Probe（强/弱/拒绝）、32 位正常文件、64 位文件、截断、非法 `EndOffset`、截断属性列表、未知属性类型、非法 UTF-8、deflate 数组不解压、逐节点范围一致性。
  - `FbxSampleTests`：`fixtures/fbx/*.fbx` 树覆盖、全节点无 Error、无 Warning、真实样本关键字段与节点总数。

### Fixtures

- `fixtures/fbx/Kirisame_Body.fbx`：真实二进制 FBX 7400，4,886,188 字节；来源与预期见 `fixtures/fbx/README.md`。
- `fixtures/fbx/sdk-blendshape-7500.fbx`：Autodesk FBX SDK 2020.3.7 导出的公开最小场景，29,280 字节；来源、固定 commit 与 hash 见目录 README。
- 结构覆盖：11 个顶层节点（FBXHeaderExtension、FileId、CreationTime、Creator、GlobalSettings、Documents、References、Definitions、Objects、Connections、Takes）、18,893 个 node record、最大嵌套 4 层、footer 183 字节。
- 属性覆盖：`S`、`L`、`d`、`I`、`D`、`i`、`C`、`R`；数组同时包含 `Encoding = 0` 与 `Encoding = 1`。
- 预期：树覆盖通过；所有节点解析无 Error/Warning；`Version = 7400`；`TopLevelNodeCount = 11`；`Creator` 为 Blender FBX IO。

---

## 8. PNG

### 规范

- W3C PNG Specification, Third Edition（当前名称）
- APNG Specification（`acTL`/`fcTL`/`fdAT`）

### 原始标识

- signature：`89 50 4E 47 0D 0A 1A 0A`，8 字节。
- chunk：`Length`（4 字节，big-endian）+ `Chunk Type`（4 字节）+ `Chunk Data` + `CRC`（4 字节，big-endian）。
- chunk 节点范围 = `12 + dataLength`；`Length` 不含 length/type/CRC。
- `Length` 最大为 `2^31 - 1`；超过时报告 `INVALID_LENGTH`。

### 字段命名

- 使用 Third Edition 的名称：`Color type`、`Greyscale`、`Truecolor`、`Indexed-color`、
  `Greyscale with alpha`、`Truecolor with alpha`；不再使用旧文档的 `Colour type`。
- APNG 字段保持规范的小写 snake_case：`num_frames`、`num_plays`、`sequence_number`、
  `width`、`height`、`x_offset`、`y_offset`、`delay_num`、`delay_den`、`dispose_op`、`blend_op`。
- 枚举值显示为“原始数字 + 短含义”，例如 `3 (Indexed-color)`、`0 (Perceptual)`。
- 派生值用尖括号：`<PayloadLength>`、`<Timestamp>`、
  `<FrameDelaySeconds>`、`<CompressedProfileLength>`、`<TextUTF8>`。
- 身份值只在标题出现：chunk 节点不再输出 `Chunk Type`；signature 标题为 `Signature '\x89PNG\x0D\x0A\x1A\x0A'`。

### 首版支持的 chunk

```text
IHDR  PLTE  IDAT  IEND  tRNS  cHRM  gAMA  iCCP  sBIT  sRGB  cICP  mDCV  cLLI  bKGD  hIST
pHYs  sPLT  eXIf  tIME  tEXt  zTXt  iTXt  acTL  fcTL  fdAT  oFFs
```

未列出的已知类型与未知类型都保留为 `NodeKind.Unparsed`，只显示 Note，不猜字段。

### cICP

- PNG Third Edition §11.3.2.6：4 个 1 字节 ITU-T H.273 码点，顺序固定为
  `Color primaries`、`Transfer function`、`Matrix coefficients`、`Video full range flag`。
- 枚举按“原始数字 + 短含义”显示，例如 `9 (BT.2020)`、`16 (SMPTE ST 2084 (PQ))`、
  `0 (Identity)`、`1 (Full range)`；未赋值的保留码点只显示数字。
- 非 0 `Matrix coefficients` 仍按原值解释；不诊断码点组合的颜色语义（D-028）。
- `Video full range flag` 只允许 0/1；其它值报告 `INVALID_ENCODING`。
- 长度超过 4 字节时报告 `SCHEMA_MISMATCH`，不足 4 字节时按截断字段报告 `TRUNCATED_PAYLOAD`。

### PLTE / tRNS / bKGD 颜色空间语义

- `PLTE`：条目固定为 8-bit RGB；显示 `Entry[n].Red/Green/Blue`，并派生 `<PaletteEntryCount>` 与
  `<Entry[n].Color> = #RRGGBB`。`PLTE` 的条目数只按块长度展示，不检查像素索引是否越界（D-028）。
- `tRNS`：
  - 颜色类型 0/2：2 字节/样本，样本深度小于 16 时只使用低位；派生 `<TransparentSample>` 给出掩码后的有效值，
    不因高位非零报警。
  - 颜色类型 3：逐字节 alpha；派生 `<PaletteAlphaEntryCount>`，并 Note 说明未列出的调色板条目 alpha 视为 255。
- `bKGD`：
  - 颜色类型 0/4：`Greyscale`；颜色类型 2/6：`Red`/`Green`/`Blue`；颜色类型 3：`Palette index`。
  - 派生 `<BackgroundColor> = #RRGGBB`：索引色通过读取 `PLTE` 对应条目解析；灰度/真彩按 IHDR 位深掩码并缩放到 8-bit。
  - 只做颜色解释，不检查背景色是否真的被使用（D-028）。

### mDCV / cLLI

- `mDCV`（W3C PNG Third Edition §11.3.2.7）：24 字节。
  - 红/绿/蓝主色度各 `x`,`y` 与白点 `x`,`y`（uint16）：存储值 × 0.00002；
  - 最大/最小亮度（uint32）：存储值 × 0.0001 cd/m²。字节序均为 big-endian。
  - 字段：`Red x`、`Red y`、`Green x`、`Green y`、`Blue x`、`Blue y`、`White point x`、`White point y`、
    `Maximum luminance`、`Minimum luminance`；原始数字 + 括号内缩放值。
- `cLLI`（§11.3.2.8）：8 字节。
  - `Maximum Content Light Level (MaxCLL)`、`Maximum Frame-Average Light Level (MaxFALL)`（uint32，除 0.0001 cd/m²）。
  - 原始数字 + 括号内缩放值；0 表示未知/未计算，不报警，不检查 MaxFALL/MaxCLL 与像素或彼此的语义关系。
- 长度不足按截断字段诊断；多余字节按 `SCHEMA_MISMATCH` 诊断。
- 不检查 `mDCV` 是否与 `cICP` 配套、码点组合是否合理（D-028）。

### CRC

- CRC-32 覆盖 **chunk type + chunk data**，不覆盖 `Length` 与 CRC 本身；多项式 `0xEDB88320`，
  初值/终值异或 `0xFFFFFFFF`。
- 文档阶段不校验 CRC；选中节点时流式计算，避免打开大文件就遍历全部 IDAT。
- 所有 chunk（含 `IDAT`/`fdAT`）都在选中节点时计算 CRC，不再按块类型或大小跳过。
- CRC 覆盖范围超过 512 KiB 时继续计算，并通过 `ParseContext.ReportProgress` 更新 GUI 进度条；该阈值固定，不可配置（D-037）。
- 字段只有 `CRC`：
  - 校验通过：`CRC = 0xXXXXXXXX`（文件值）
  - 校验失败：`CRC = 0xXXXXXXXX (0xYYYYYYYY)`，前为文件值，括号内为计算值，并报告 `CRC_MISMATCH`
- 不再输出 `<ComputedCRC>` / `<CRCStatus>`。

### 截断与 trailing

- chunk header 不足 8 字节：剩余字节成为 `<trailing data>`，报告 `TRUNCATED_HEADER`。
- chunk 声明长度超过剩余字节：节点延伸到文件末尾，报告 `TRUNCATED_PAYLOAD`。
- 缺少 IEND：报告 `TRUNCATED_CHILD`。
- IEND 后仍有字节：显式 `<trailing data>` 节点，报告 `TRAILING_DATA`。
- 不允许不可见空洞；截断 chunk 的节点范围包含所有可读字节。

### 文本编码

- `tEXt` 规范声明 ISO-8859-1。真实文件常写入 UTF-8；当字节是合法 UTF-8 且含非 ASCII 时，
  额外给出 `<TextUTF8>` 与 Note，两种解释都可见。
- `iTXt` 的 keyword/language tag 按 Latin-1 读取，translated keyword 与 text 按 UTF-8 读取。
- 非法 UTF-8 报告 `INVALID_ENCODING`，非法字节显示为替换字符。
- 控制字符转义为 `\0`、`\r`、`\n`、`\t`、`\xNN`。
- 文本受 `MaxStringBytes` 限制；超出只显示前缀并报告 `LIMIT_STRING`。

### payload 边界

- `IDAT`、`fdAT` 只显示压缩数据长度，不解压。
- D-011 例外：`iCCP`、`zTXt`、压缩的 `iTXt` 是体量小、压缩方式简单的元数据/文本，使用有界 zlib 解压：
  - `iCCP`：解压 ICC profile，显示 `<DecompressedProfileLength>`，然后交给 `IccProfileParser` 完整解析 profile header、tag table 与常见 tag type（D-036）。
  - `zTXt`：解压文本，按 ISO-8859-1 显示 `<Text>`。
  - 压缩 `iTXt`：解压文本，按 UTF-8 显示 `<Text>`。
  - 输出上限分别使用 `MaxStringBytes` / `MaxBlobBytes`；超过上限只显示前缀并报告 `LIMIT_STRING` / `LIMIT_BLOB`。
  - 解压失败报告 `INVALID_ENCODING`，不伪造内容。
- `eXIf` 复用 `ExifParser` 完整解析 IFD0 / Exif / GPS / Interop，入口是 TIFF header。
- `PLTE`/`tRNS`/`bKGD`/`hIST`/`sPLT` 的条目数受 `MaxDataLines` 限制。

### APNG

- `acTL`、`fcTL`、`fdAT` 作为普通 chunk 展示，不组装帧。
- `num_plays = 0` 表示无限循环，加 Note。
- `delay_den = 0` 按规范视为 100。
- 不检查 frame 与 IHDR/默认图、帧数、序号之间的语义一致性；只读取各 chunk 本身的字段（D-028）。

### 已知坑

- chunk 越界要产出诊断与显式 trailing 节点，不能 `break` 丢数据。
- 错误时用诊断表达，不把异常文本当 RawData。
- `iTXt`/`iCCP`/`eXIf` 整块读取，可能 OOM；新实现按长度前缀/上限读取，解压输出也受 `ParseLimits` 限制。
- CRC 不应在文档阶段全量校验，否则打开大 PNG 会遍历所有 IDAT；节点选中时再计算，超过 512 KiB 用进度条反馈。
- `eXIf` 的 `II`/`MM` 比较必须限定前 2 字节，不能与 4 字节 marker 整体比较。
- 非 ASCII chunk type 需要转义显示。

### Fixtures

目录 `fixtures/png/`：

- 5 个真实 PNG：大 IDAT、多 IDAT、iCCP、iTXt XMP、cHRM、eXIf、oFFs、sBIT、tEXt 中文。
- `apng-basic.png`：`generate_apng.py` 生成的 2 帧 APNG。
- `cicp-hdr.png`：`generate_cicp.py` 生成，`cICP = 09 10 00 01`（BT.2020 + PQ + identity + full range）。
- `hdr-metadata.png` / `indexed-colors.png`：`generate_phase6_metadata.py` 生成，分别覆盖 `mDCV`/`cLLI` 和 PLTE/透明度/背景解释。
- 截断、CRC 错误、非法 length、未知 chunk、trailing 由内存测试构造，不放进“无警告”样本集。
- 详细布局与预期见 `fixtures/png/README.md`。

---

## 9. JPEG

### 规范

- ITU-T T.81（JPEG）
- JFIF 1.02
- EXIF（CIPA DC-008）
- [Adobe Photoshop File Formats Specification（2019-11）](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/) §Image Resources：稳定的官方 8BIM resource block / ID / 常用数据布局；
  ResolutionInfo 再对照 [Adobe Photoshop File Format Summary](https://www.fileformat.info/format/psd/egff.htm) 的结构定义。
- ISO/IEC 16684-1（XMP）——仅作为文本读取，不解析 XML

### 原始标识

- SOI `FFD8`、EOI `FFD9`。
- marker：`FFxx`；`FF` fill bytes 可以出现在 marker 前。
- 带长度 segment：`FFxx` + `Length`（2 字节，big-endian，含自身 2 字节）+ payload。
- 常见 marker：`APP0`/`APP1`/`APP2`、`DQT`、`DHT`、`DRI`、`SOF0/1/2/3/5/6/7/9/10/11/13/14/15`、`SOS`、`COM`。
- standalone marker：`SOI`、`EOI`、`RST0`–`RST7`、`TEM`。

### 范围

- 普通 segment 节点长度 = `2 + Length`；`Length` 不含 marker，含 length 字段本身。
- `SCAN` 节点只表示 SOS header 之后的 entropy-coded data，不含下一个 marker。
- `SCAN` 内的 `FF00` stuffing 与 `FFD0`–`FFD7` restart marker 属于 scan 范围。
- EOI 后 trailing 显式表示。

### 字段命名

- 使用 T.81/JFIF 的语义名称：`Precision`、`Height`、`Width`、`Number of components`、
  `Component[n].Component identifier`、`Component[n].Sampling factors`、
  `Component[n].Quantization table destination`、`Spectral selection start/end`、
  `Successive approximation`、`Restart interval`。
- 组合字节保留原始字段，再用尖括号给出拆分解释：
  `Component[n].Sampling factors` → `<...Horizontal sampling factor>` / `<...Vertical sampling factor>`；
  `Component[n].Td/Ta` → `<...DC entropy coding table selector>` / `<...AC ...>`。
- APP0–APP15 统一输出 `Identifier`（NUL 终止字符串，NUL 不计入值）与 `<ApplicationFormat>`；
  `Exif`、标准 XMP、`Photoshop 3.0` 继续解析；其余仍保留 `<UnparsedLength>`（D-023）。
- APP1/Exif：`Identifier`、`<ExifPayloadLength>`、`<ByteOrder>`、`TIFF magic`、`First IFD offset`、
  `IFD0.*` 等；第二个 NUL 属于 Exif 包装头，不是 Identifier 的一部分。
- 树显示 mnemonic（`SOI`、`APP0`），标题为 `StartOfImage '\xFF\xD8'`、`StartOfFrameBaselineDct '\xFF\xC0'`；`Marker` / `<MarkerName>` 不再作为字段重复输出。

### APP identifier dispatcher（D-023）

```text
Identifier                      Content
Exif                            TIFF / Exif（完整解析）
ICC_PROFILE                     ICC profile（单段完整解析；多段不拼接）
Photoshop 3.0                   Photoshop image resources / 8BIM（解析资源块列表）
Adobe                           Adobe TN 5116，5 字节签名 + u16 version + flags（未解析）
http://ns.adobe.com/xap/1.0/    XMP packet（原文文本返回，不解析 XML）
JFIF / JFXX                     JFIF header / extension（未解析）
```

- 识别只依赖 Identifier，不依赖 APP 序号；`Exif` 可以出现在任意 APPn。
- ICC 段：`Identifier`（`ICC_PROFILE\0`）之后是 `Marker sequence number` 与 `Number of markers` 两个 u8，再往后是该段的 profile 字节。
  - 仅当 `Number of markers == 1` 时把剩余字节作为完整 profile 交给 `IccProfileParser`（D-036）。
  - `Number of markers > 1` 时只显示分段框架、`<ProfileChunkLength>` 与 `<UnparsedLength>`，不跨段拼接。
  - 编号为 0 或大于总数时报告 `INVALID_ENCODING`，段内容保持 `Unparsed`。
- XMP 段：`Identifier` 之后是 UTF-8 XML 文本；字段显示 `Text`（完整原文，保留 BOM/换行/空白）与 `<PacketByteLength>`，
  不做 XML 结构解析；超过 `MaxStringBytes` 时只显示前缀并报告 `LIMIT_STRING`。
- Photoshop 段：`Identifier` 之后是连续的 image resource block：
  - `Signature`（4 字节，通常 `8BIM`）、`ID`（u16 BE）、`Name`（Pascal 字符串，补齐偶数）、
    `Size`（u32 BE）、`Data`（补齐偶数）。
  - 资源列表按 `Resource[n].` 前缀输出；常用资源解析字段，其余只显示 ID/Name/Size 与 Note。
  - 与 PSD/PSB 共用 `PhotoshopImageResources`；资源字段名和解释与 PSD `ImageResources` 完全一致。
  - 小型公开资源：1000 Image properties、1005 ResolutionInfo、1006 Pascal alpha names、1008 Caption、
    1009 Border、1010 Color、1011 Print flags、1019 Dot range、1022 Quick Mask、1024 Layer state、
    1026 Layer groups、1032 Grid/guides、1033/1036 Thumbnail header、1034 Copyright flag、1035 URL、
    1037 Angle、1039 ICC profile、1040/1041/1042 flags、1043 Spot halftone、1044 IDs seed、
    1045 Unicode alpha names、1046 Palette count、1047 Transparency index、1049 Altitude、1050 Slices、
    1051 Workflow URL、1052 Jump To XPEP、1053 Alpha identifiers、1054 URL list、1057 Version Info、
    1058/1059 EXIF、1060 XMP、1061 Digest、1062 Print scale、1064 Aspect ratio、1069 Layer selection IDs、
    1072 Layer groups enabled、1073 Color samplers、1086/1087 Auto Save strings、10000 Print flags info。
  - `ResolutionInfo` 顺序是 hRes/hResUnit/widthUnit/vRes/vResUnit/heightUnit；`Pixel Aspect Ratio` 是 u32 version + **一个** double（x/y 比值），不是两个 double。
  - Counted Unicode 字符串只按 u32 code-unit count 读，不额外吃一个猜测的 NUL，也不修剪文件里的 NUL。
    真实 Photoshop 的 resource 1057 与 PSD `luni` 都不写通用规范段落提到的 2 字节 null；吃它会错位到 File version。
  - `8B64` 是 additional-layer information 签名，不按 image resource block 猜读。
  - 资源数据越界、Pascal name/padding 不完整、Size 超出 segment 时保留已有字段并诊断；
    1028 IPTC、复杂 descriptor（1065/1074–1088/3000 等）、私有/未知资源只列 framing + 未解释长度，不虚称已解码。
- `Adobe` 没有 NUL 终止符：固定 5 字节 `"Adobe"`，随后是两字节 version。不要把 version
  的高字节 `0x00` 当成字符串结束。
- 未注册的 Identifier 不产生 Note/Warning，整段保留 `<UnparsedLength>`。
- 没有 NUL 终止符的 APP payload（例如部分 APP12 picture info）只给出一条 Note，不判定为截断。
- Identifier 超过 `MaxStringBytes` 时停止读取、报告 `LIMIT_STRING`，payload 仍完整保留。
- 已知但未启用解析的 APP 段不产生诊断；树节点为 `Unparsed`，Hex 覆盖完整 segment。

### scan 范围算法

1. 从 SOS payload 末尾开始。
2. 普通字节继续。
3. 遇到 `FF`：
   - 连续 `FF` 是 fill bytes，继续找 code。
   - code `00`：stuffed byte，继续。
   - code `D0`–`D7`：restart marker，继续。
   - 其他 code：下一个顶层 marker，scan 结束。
4. 返回第一个顶层 marker 的 `FF` 位置。

渐进式 JPEG 会有多个 SOS/SCAN，按同一规则循环解析。

### 已知坑

- segment 长度必须校验，越界截断到父范围并诊断。
- `SCAN` 范围特殊：不能把 scan 内的 `FF00`/RST 当成 segment。
- 解析不能在 UI 线程同步执行，也不允许 `Application.DoEvents()`。
- segment 数、DQT/DHT 表项、COM 文本都需要限制。
- APPn 内容由 Identifier 决定；APP1 不一定是 Exif，不能再按 APP 序号假设内容。
- `Adobe` 签名的 version 高字节常为 `0x00`，不能按 NUL 终止字符串读取。
- 截断 segment、非法 length、EOI 后 trailing。
- `FF` fill bytes 可能出现在 marker 前。

### Fixtures

目录 `fixtures/jpeg/`：

- `baseline-420.jpg`、`baseline-444.jpg`：baseline，4:2:0 / 4:4:4。
- `progressive-420.jpg`：多个 SOS/SCAN。
- `grayscale.jpg`：单分量。
- `exif.jpg`：APP1 `Exif\0\0`。
- 截断 segment、非法 length、EOI 后 trailing、未知 marker 由内存测试构造。
- 详细布局见 `fixtures/jpeg/README.md`。

---

## 10. MIDI（Standard MIDI File）

### 规范

- Standard MIDI Files 1.0（MIDI Association / MMA）
- 扩展名 `.mid` / `.midi`；文件以 `MThd` 开头

### 原始标识

- Chunk id：`MThd`（header）、`MTrk`（track）
- 事件：`delta_time` 后跟 status byte；`0xFF` 为 meta event，`0xF0` / `0xF7` 为 SysEx

### 范围与树

- `MThd`：8 字节 chunk header + 6 字节标准 header（`format`、`ntrks`、`division`）；长度大于 6 的额外字节不解释。
- `MTrk`：8 字节 chunk header + 声明长度的 track data；每个事件是独立子节点，范围精确覆盖
  `delta_time + status + data`。
- 未知 chunk 整块 `Unparsed`；零填充、截断 chunk、缺失 `EndOfTrack`、事件越界均有诊断。
- 不按 `ntrks` 校验实际 track 数量（跨块语义，D-028）。

### 事件支持

- 全部 channel voice 消息：Note Off / Note On / Polyphonic Key Pressure / Control Change /
  Program Change / Channel Pressure / Pitch Bend。
- running status：事件首字节小于 `0x80` 时复用上一 status；SysEx / meta / system 消息清除 running status。
- SysEx：`F0` 与 `F7` escape，按 VLQ 长度跳过 payload；只显示长度与 Note，不解释内容。
- Meta：Sequence Number、Text/Copyright/Track Name/Instrument/Lyric/Marker/Cue/Program/Device、
  Channel Prefix、MIDI Port、End of Track、Set Tempo、SMPTE Offset、Time Signature、Key Signature、
  Sequencer-Specific；文本按 Latin-1 解码并转义控制字符。
- 派生解释：`<NoteName>`、`<TempoBPM>`、`<TimeSignature>`、`<KeySignature>`、`<SmpteOffset>`、
  `<PitchBend>`、`<ControllerName>` 等。
- 字段逐行输出，不把一条事件压成单行；track 的事件子节点顺序就是完整的音符/事件表。

### 已知坑

- running status 的事件少一个 status 字节，跳过数据时必须扣掉已经读出的首数据字节。
- VLQ 最多 4 字节；超过 4 字节或截断必须报诊断，不能无限读。
- `EndOfTrack` 后的字节是 trailing data，必须显式节点，不能静默吞掉。
- SysEx 长度包含其后的数据字节；不要把 `F7` 当成独立事件重复计数。

### Fixtures

- `fixtures/midi/MUSIC_01.MID`：3 tracks，含 Track Name、SMPTE Offset、Time Signature、Key Signature、Tempo。
- `fixtures/midi/Saigon_Neon.mid`：15 tracks，含大量 note/controller 事件与文本 meta。
- 两个样本均要求解析无 Error，且每个 `MTrk` 都有事件子节点。

---

## 11. PSD / PSB（Adobe Photoshop）

> **状态：不稳定。** 基础 section、image resources、layer records 与常用 additional info 已实现，
> 但 Adobe PSD/PSB 规范包含大量私有/复杂结构（descriptor、adjustment/layer effect、厂商扩展、
> 部分 mask/blending 细节）。当前仍可能有节点只能显示原始范围，或在非 Adobe 生成的文件上出现
> 字段边界偏差；不要把本解析器当作完整 PSD 语义校验器。节点名 `FileHeader`、`LayerRecord[0]`
> 等也是本工具命名，并非 Adobe 规范原文。

### 规范

- [Adobe Photoshop File Formats Specification（2019-11）](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/)
- PSD version 1 与 PSB version 2 共用全部 section；PSB 只加宽少数长度字段。

### 原始标识

- 文件签名 `8BPS`（4 字节）。
- Header 固定 26 字节：`Signature`、`Version`（u16）、6 字节 `Reserved`、`Channels`（u16）、
  `Height`（u32）、`Width`（u32）、`Depth`（u16）、`Color Mode`（u16）。
- 顶层 section 顺序：`ColorModeData` → `ImageResources` → `LayerAndMaskInformation` → `ImageData`。

### 范围与树

- `ColorModeData`：u32 length + data；indexed 模式通常 768 字节。data 不解释。
- `ImageResources`：u32 length + 连续 8BIM block；每个 block 是 `Resource[n] 'id (name)'` 子节点。
  与 JPEG APP13 `Photoshop 3.0` 共用 `PhotoshopImageResources`，字段命名完全一致。
- `LayerAndMaskInformation`：PSD 为 u32 length，PSB 为 u64 length。payload 依次是：
  - `LayerInfo`：PSD u32 / PSB u64 length；`LayerCount`（i16，负数表示首个 alpha 通道是合并透明度）、
    `LayerRecords`、`ChannelImageData`。
  - `GlobalLayerMaskInfo`：u32 length + data；overlay color space、4 个 color component、opacity、kind。
  - additional layer information block（`8BIM` / `8B64` + 4CC key + length + data）。
- `LayerRecord`：`top`/`left`/`bottom`/`right`（4×i32）、`Number of channels`（u16）、每通道
  `Channel ID`（i16）与 `Channel data length`（PSD u32 / PSB u64）、blend mode signature/key、
  opacity、clipping、flags、filler、extra data length。extra data 再分：
  - layer mask data（u32 length + data）
  - layer blending ranges（u32 length + data）
  - layer name（Pascal string，补齐到 4 字节）
  - additional layer information blocks
- `ChannelImageData`：按 layer record 顺序，每个 channel 一个 `ChannelData[n]` 节点；长度来自 layer record。
- `ImageData`：u16 compression（0 raw / 1 RLE / 2 ZIP / 3 ZIP+prediction）+ data；只显示 compression、
  长度与 Note，不解 RLE/ZIP，不解码像素。
- 每个 byte 必须落在节点内；layer record 扫描失败或 channel data 长度无法归属时，剩余范围生成显式
  `Unparsed` 节点并给诊断。

### PSB 长度字段

- 8 字节：`LayerAndMaskInformation` length、`LayerInfo` length、每个 channel 的 `Channel data length`。
- additional layer information 的 8 字节 length 仅用于这些 key：
  `LMsk`、`Lr16`、`Lr32`、`Layr`、`Mt16`、`Mt32`、`Mtrn`、`Alph`、`FMsk`、`lnk2`、`FEid`、`FXid`、`PxSD`；
  其它 key 仍为 u32。
- `ColorModeData`、`ImageResources`、`GlobalLayerMaskInfo` 的 length 始终是 u32。

### Additional Layer Information

- signature 必须是 `8BIM` 或 `8B64`；key 是 4CC；length 后跟 data。
- 已解释：
  - `luni`：Unicode layer name（u32 count + UTF-16BE code units）。
  - `lyid`：Layer ID（u32）。
  - `lspf`：Protected setting（u32 + 位解释）。
  - `lsct` / `lsdk`：Section divider type（u32 + 可选 blend signature/key）。
  - `clbl` / `infx` / `knko`：1 字节 boolean + 3 字节 padding。
  - `iOpa`：Fill opacity（u8）。
  - `lyvr`：Version（u32）。
  - `fxrp`：Reference point（两个 big-endian double）。
- 其它 key 只显示 signature/key/length 与 Note；data 保留在 Hex。

### 已知坑

- Counted Unicode string 只按 u32 count 读 UTF-16BE code units，**不消费猜测的 2 字节 null**。
  真实 Photoshop 文件（`luni`、JPEG resource 1057）不写该 null；消费它会把后续字段整体错位。
- Layer name 的 Pascal 字符串补齐到 **4 字节**；image resource 的 Pascal name 补齐到 **2 字节**，两者不同。
- Layer mask data 长度为 0 时仍占 4 字节 length 字段；不要把它当成缺失。
- Layer channel data length 包含 2 字节 compression 字段。
- 不按 `LayerCount` 之外的关系校验 layer/channel 语义（D-028）。

### Fixtures

- `fixtures/psd/1.psd`：真实 PSD，1920×3840，RGB，92 layers，含 5 个 image resource（1005/1006/1036/1039/1045）
  与 `lspf`/`luni`/`tsly`/`lsct` additional info。
- 合成 PSD/PSB 由 `PsdParserTests` 构造，覆盖 section 顺序、资源字段、layer record、channel data、
  additional info、PSB 8 字节长度与树覆盖。

---

## 12. Ogg（Ogg Encapsulation Format）

### 规范

- [RFC 3533（The Ogg Encapsulation Format Version 0）](https://www.rfc-editor.org/rfc/rfc3533)
- [Vorbis I specification](https://xiph.org/vorbis/doc/Vorbis_I_spec.html)（identification / comment / setup）
- [RFC 7845（Ogg Encapsulation for the Opus Audio Codec）](https://www.rfc-editor.org/rfc/rfc7845)
- [Theora specification](https://www.theora.org/doc/Theora.pdf)（identification / comment / setup）
- Speex `SpeexHeader`（`speex_header.h`）
- [FLAC-to-Ogg mapping](https://xiph.org/flac/ogg_mapping.html)
- [Ogg Skeleton draft](https://datatracker.ietf.org/doc/draft-swhited-ogg-skeleton/)（fishead / fisbone）

### 命名

- parser id 固定为 `ogg`，根节点显示 `Ogg Encapsulation Format`。
- `.ogg` / `.oga` / `.ogv` / `.opus` / `.spx` / `.ogx` 都是同一种 Ogg 物理比特流；扩展名只表达内部
  codec 或用途，不单独拆 parser。
  - `.ogv` 通常是 Ogg + Theora，可再复用 Vorbis 音轨。
  - Opus 是独立 codec（RFC 6716），其 Ogg 映射由 RFC 7845 定义；`OpusTags` 复用 Vorbis comment
    结构，但 Opus 不是“Vorbis 的一种”。
- 树只有一种物理容器节点 `Page`（RFC 3533 的 page），不按扩展名或 codec 造伪节点。
- 字段名使用各自规范的原始标识符：RFC 3533 用 snake_case；Opus 用 RFC 7845 的字段标签；
  Theora 用 `VMAJ`/`PICW` 等规范缩写；Speex 用 `speex_header.h` 的成员名。
- 完整 packet 的 header 字段加 `Packet[n].` 前缀；`n` 是该 packet 在本页内的起始序号（从 0 开始）。
  跨页续传的 packet 不在当前页解析。

### Page header 与 lacing

- `capture_pattern` 固定 `OggS`；header 长度 = 27 + `number_page_segments`。
- 字段：`stream_structure_version`、`header_type_flag`、`granule_position`、
  `bitstream_serial_number`、`page_sequence_number`、`CRC_checksum`、`number_page_segments`、
  `segment_table[n]`。
- `header_type_flag` 位：`0x01` continued packet、`0x02` beginning of stream、`0x04` end of stream。
- 多字节整数一律 little-endian。
- `granule_position == -1`（全 `FF`）表示本页没有完成的 packet。
- lacing：255 表示 packet 未结束；小于 255 的值结束当前 packet；0 是合法空 packet。
- 每个完成的 packet 是一个语义边界；音频/视频 packet 只显示长度，不解码。
- 根节点摘要中的 `FirstPage` / `LastPage` 是 page 在物理树中的序号（从 0 开始），不是
  `page_sequence_number`。
- CRC：多项式 `0x04C11DB7`、初值 0、不反射、无最终 XOR；校验时把 `CRC_checksum` 字段置 0。
  选择页面时计算并显示 `<CRC check>`；不匹配报 `CRC_MISMATCH`。

### Codec header（完整 packet 且开头 magic 匹配时）

| Magic | 结构 | 字段 |
|---|---|---|
| `0x01 "vorbis"` | Vorbis identification | `vorbis_version`、`audio_channels`、`audio_sample_rate`、`bitrate_maximum/nominal/minimum`、`blocksize_0/1`、`framing_flag` |
| `0x03 "vorbis"` | Vorbis comment | `vendor_length`、`vendor_string`、`user_comment_list_length`、`user_comment[n].length`、`user_comment[n]`、`framing_bit` |
| `0x05 "vorbis"` | Vorbis setup | 只标注 codebooks 未解释 |
| `OpusHead` | Opus identification | `Version`、`Output Channel Count`、`Pre-skip`、`Input Sample Rate`、`Output Gain`、`Channel Mapping Family`、`Stream Count`、`Coupled Count`、`Channel Mapping[n]` |
| `OpusTags` | Opus comment | `Vendor String Length`、`Vendor String`、`User Comment List Length`、`User Comment #n String Length`、`User Comment #n String` |
| `0x80 "theora"` | Theora identification | `VMAJ/VMIN/VREV`、`FMBW/FMBH`、`PICW/PICH`、`PICX/PICY`、`FRN/FRD`、`PARN/PARD`、`CS`、`NOMBR`、`QUAL`、`KFGSHIFT`、`PF`、`Res` |
| `0x81 "theora"` | Theora comment | 与 Vorbis comment 相同结构，无 framing bit |
| `0x82 "theora"` | Theora setup | 只标注量化/Huffman 表未解释 |
| `Speex   ` | Speex header | `speex_version`、`speex_version_id`、`header_size`、`rate`、`mode`、`mode_bitstream_version`、`nb_channels`、`bitrate`、`frame_size`、`vbr`、`frames_per_packet`、`extra_headers`、`reserved1/2` |
| `0x7F "FLAC"` | Ogg FLAC mapping | mapping 版本、header packet 数、`fLaC`、STREAMINFO 元数据块头与字段 |
| `fishead\0` | Skeleton ident | `Version major/minor`、presentation/basetime 分子分母、`UTC`、v4 的 `Segment length` / `Content byte offset` |
| `fisbone\0` | Skeleton secondary | 消息头偏移、serial、header packet 数、granulerate、`Basegranule`、`Preroll`、`Granuleshift`、`Message header[n]` |

- Theora identification 的最后 16 bit 按 MSB-first 拆成 `QUAL`（6）、`KFGSHIFT`（5）、`PF`（2）、
  `Res`（3）。
- Comment 的长度字段统一按 little-endian 读取（Vorbis/Opus/Theora 是同一内存布局）。
- Theora identification 的 `PICW`/`PICH` 固定读 24 bit（规范要求保留字节对齐，虽然 20 bit 已足够）。

### 已知坑与限制

- header packet 跨页时不解析：树是 page 级物理结构，字段读取不跨节点范围；只给 Note。
- Speex comment packet 没有固定 magic，无法只凭单页可靠识别，暂不解析。
- FLAC 只解析首包 mapping + STREAMINFO；后续 metadata block（含 VORBIS_COMMENT）与音频帧只显示为
  packet。
- 不把 granule position 换算成秒：换算依赖 codec 与 Skeleton 的 granulerate，属于跨块语义（D-028）。
- 不做 packet 重组、不校验 page sequence 连续性、不解码任何音视频帧。
- 未知 codec 的 BOS 页只显示 `Unknown`，不猜 codec。

### Fixtures

- `fixtures/ogg/a053_003_000277.ogg`、`freebgm_05018lhq.ogg`：用户提供的真实 Vorbis 文件。
- `fixtures/ogg/tone_vorbis.ogg`、`tone_opus.opus`、`tone_theora.ogv`、`tone_speex.spx`、
  `tone_flac.oga`：本机 ffmpeg 生成的 1 秒测试音/测试图，覆盖 Vorbis/Opus/Theora/Speex/FLAC 的
  header 与 CRC。

---

## 13. FLAC（Free Lossless Audio Codec）

### 规范

- [RFC 9639（Free Lossless Audio Codec，2024-12）](https://www.rfc-editor.org/rfc/rfc9639)
- [Xiph FLAC format 索引](https://xiph.org/flac/format.html)（旧结构名到 RFC 章节的映射）
- Ogg FLAC 映射见 RFC 9639 §10.1 与 [FLAC-to-Ogg mapping](https://xiph.org/flac/ogg_mapping.html)；
  两种封装共用 `FlacStreamInfo` 与 `VorbisComment`。

### 命名

- parser id `flac`，根节点 `Free Lossless Audio Codec`；扩展名 `.flac`。
- **结构名与字段名全部大写**：
  - 节点名使用规范的全大写结构名：`STREAMINFO`、`PADDING`、`APPLICATION`、`SEEKTABLE`、
    `VORBIS_COMMENT`、`CUESHEET`、`PICTURE`、`RESERVED`，以及 `SEEKPOINT`、`CUESHEET_TRACK`、
    `CUESHEET_TRACK_INDEX`。
  - 字段名使用全大写 snake_case：`MINIMUM_BLOCK_SIZE`、`SAMPLE_RATE`、`TRACK_OFFSET` 等。
    规范正文部分字段是 prose 描述，本工具按术语转写为大写标识符。
- 文件签名 `fLaC` 是根节点的固定前缀，不单独建节点；根节点 payload 从偏移 4 开始。

### 结构

- 4 字节签名 `fLaC`，随后是元数据块序列（至少一个 STREAMINFO），最后是音频帧。
- 元数据块 header：1 bit `LAST_METADATA_BLOCK_FLAG`、7 bit `BLOCK_TYPE`、24 bit `LENGTH`，
  数据紧随 header；`BLOCK_TYPE` 已知时不重复输出该字段（节点名已表达），7–126 的 RESERVED 与
  127（禁止值）会输出 `BLOCK_TYPE` 数值。
- `STREAMINFO`（类型 0，必须首块且最多一个，34 字节）：`MINIMUM_BLOCK_SIZE`、
  `MAXIMUM_BLOCK_SIZE`、`MINIMUM_FRAME_SIZE`、`MAXIMUM_FRAME_SIZE`、20 bit `SAMPLE_RATE`、
  3 bit `(CHANNELS-1)`、5 bit `(BITS_PER_SAMPLE-1)`、36 bit `TOTAL_SAMPLES`、128 bit
  `MD5_SIGNATURE`。`CHANNELS` / `BITS_PER_SAMPLE` 显示还原后的值。
- `PADDING`（类型 1）：只显示 `LENGTH` 与 Note。
- `APPLICATION`（类型 2）：`APPLICATION_ID`（32 bit，显示为 hex + 可打印 ASCII）与
  `<DATA_LENGTH>`；应用数据不解释。
- `SEEKTABLE`（类型 3）：每个 `SEEKPOINT` 18 字节，字段 `SAMPLE_NUMBER`、`OFFSET`、
  `FRAME_SAMPLES`；建树时为每个点生成子节点。长度不是 18 的倍数时，剩余范围显式
  `<unparsed data>` 并诊断。
- `VORBIS_COMMENT`（类型 4）：与 Ogg 共用 `VorbisComment`，但使用大写键
  `VENDOR_LENGTH`、`VENDOR_STRING`、`USER_COMMENT_LIST_LENGTH`、`USER_COMMENT[n].LENGTH`、
  `USER_COMMENT[n]`；没有 framing bit，4 字节长度一律 little-endian。
- `CUESHEET`（类型 5）：396 字节固定头（`MEDIA_CATALOG_NUMBER` 128、`LEAD_IN` 64、
  `CD_FLAG` 1 bit + 7 bit reserved、258 字节 reserved、`TRACK_COUNT` 8）；每个
  `CUESHEET_TRACK` 36 字节固定头（`TRACK_OFFSET`、`TRACK_NUMBER`、`ISRC`、`TRACK_TYPE`、
  `PRE_EMPHASIS`、`INDEX_POINT_COUNT`），随后 `INDEX_POINT_COUNT` 个
  `CUESHEET_TRACK_INDEX`（12 字节：`INDEX_OFFSET`、`INDEX_NUMBER`）。track/index 都是子节点。
- `PICTURE`（类型 6）：`PICTURE_TYPE`、`MEDIA_TYPE_LENGTH`/`MEDIA_TYPE`、
  `DESCRIPTION_LENGTH`/`DESCRIPTION`、`WIDTH`、`HEIGHT`、`COLOR_DEPTH`、`COLORS`、
  `DATA_LENGTH`；图像数据不 decoding。
- 音频帧：最后一个元数据块之后的全部字节是一个 `<audio frames>` Unparsed 节点，只显示
  `<AudioDataLength>`；frame header/subframe/CRC 不解析。
- 每个 byte 必须落在节点内；截断 metadata、未知 block、cuesheet 越界等保留显式范围与诊断。

### 已知坑

- 除 Vorbis comment 的 4 字节长度外，FLAC 所有多字节整数都是 big-endian。
- `MINIMUM_BLOCK_SIZE` / `MAXIMUM_BLOCK_SIZE` 必须在 16–65535 且 min ≤ max；STREAMINFO 的
  channels/bits 存储值是实际值减 1。
- `TOTAL_SAMPLES = 0` 与全 0 `MD5_SIGNATURE` 都表示未知。
- 不解析 frame 边界，也不校验 frame CRC / MD5；不要把音频数据里的 `0xFFF8` 当 metadata。
- Ogg FLAC 的 STREAMINFO 与 native FLAC 完全同构，字段名同为全大写。

### Fixtures

- `fixtures/flac/拼音师 - 太清 TaiQing.flac`：用户提供的真实 24-bit/192 kHz FLAC（201 MB），
  STREAMINFO + SEEKTABLE（347 点）+ VORBIS_COMMENT + PADDING + PICTURE。
- `fixtures/flac/Trance Gate.flac`：用户提供的真实 FLAC（libFLAC 1.3.1，3 个 `riff` APPLICATION 块）。
- `fixtures/flac/tone.flac`：本机 ffmpeg 生成，含 VORBIS_COMMENT + PADDING。
- 合成 CUESHEET/SEEKTABLE/PICTURE 由 `FlacParserTests` 构造。

---

## 14. Blender（.blend）

### 规范

- Blender `BLO_core_blend_header.hh` / `BLO_core_bhead.hh`：文件头与块头版本（`BHead4`、
  `SmallBHead8`、`LargeBHead8`）。
- Blender `DNA1` SDNA 表：`SDNA` / `NAME` / `TYPE` / `TLEN` / `STRC` 段。
- 归档的 Blender File Format 文档（DNA、块序列、`ENDB`、`TEST` 缩略图）。

### 命名

- parser id `blend`，根节点 `Blender File`；扩展名 `.blend`。
- 树节点使用文件中的 4 字节 block code（NUL 截断）：`REND`、`TEST`、`GLOB`、`WM`、`DATA`、
  `DNA1`、`ENDB` 以及 `OB`/`ME`/`SCE` 等 ID 块。
- 块头字段使用 Blender 源码名：`len`、`old`、`SDNAnr`、`nr`；根字段为 `endian`、`file_version`，
  v0 的 `pointer_size` 是原始头字节，v1 的指针大小固定为 8，以 `<PointerSize>` 派生字段显示；
  `<HeaderSize>` / `<FileFormatVersion>` 也是派生值。
- 每个块的 SDNA 结构名以 `<SDNA_STRUCT>` 派生字段显示。
- `GLOB` 的字段直接使用 DNA 字段名（`subvstr`、`subversion`、`filename` 等）。

### 文件头与块头

- 旧版 12 字节头：`BLENDER` + `_`/`-`（4/8 字节指针）+ `v`/`V` + 3 位版本。
- 当前 17 字节头：`BLENDER` + `17` + `-` + `01` + `v` + 4 位版本（Blender 5.x）。
- 块头：
  - `BHead4`：`code`、`len`、`old`（4 字节）、`SDNAnr`、`nr`，共 20 字节。
  - `SmallBHead8`：`code`、`len`、`old`（8 字节）、`SDNAnr`、`nr`，共 24 字节。
  - `LargeBHead8`：`code`、`SDNAnr`、`old`（8 字节）、64 位 `len`、64 位 `nr`，共 32 字节。
- 旧文件可能以 8 字节的 `ENDB` + 0 作为终止符。
- 块数据长度从块头计算；截断块保留实际范围并诊断。

### DNA1（SDNA）

- `DNA1` 块数据：`SDNA` + `NAME`（字符串表）+ `TYPE`（类型名）+ `TLEN`（类型大小，紧跟在
  `TYPE` 之后、没有单独 count）+ `STRC`（结构定义）。各段按 4 字节对齐。
- 结构定义是 `type_index` + `field_count` + `(field_type, field_name)` 对。
- 本解析器把 SDNA 作为字段显示：`NAME_COUNT`、`TYPE_COUNT`、`TLEN_COUNT`、`STRUCT_COUNT`，
  以及 `NAME[n]`、`TYPE[n]`、`TLEN[n]`、`STRUCT[n]`；不展开每个结构的完整字段表。
- 结构名用于块的 `<SDNA_STRUCT>`；`SDNAnr` 是结构索引，不是类型索引。

### 已知元数据块

- `GLOB`：按 DNA 的 `FileGlobal` 结构计算字段偏移，解析顶层字段。指针显示为原文件地址
  （`0x... (file address)`），不解析指向的对象。`_pad*` 字段不展开。
- `TEST`：缩略图头 `width`、`height`（int32），`<ThumbnailDataLength> = width*height*4`；
  像素数据不解码。
- `ENDB`：结束块；旧版 8 字节终止符单独处理。
- 其余块（`DATA`、`WM`、`USER`、ID 块等）通常是运行时内存镜像，保持 `Unparsed`，只显示
  块头与 `<SDNA_STRUCT>`，并给 Note。

### 限制

- 仅支持未压缩 `.blend`。ZSTD/Gzip 压缩文件以压缩魔数开头，探测不匹配，按 RawData 处理，
  不解压。
- 不解析指针、链表、数组内容、嵌套结构体或对象语义；不校验 ID/库/版本关系。
- `DNA1` 的 `TLEN` 没有 count 字段，长度等于 `TYPE_COUNT`；不要把它当 u32 count 读取。
- SDNA 的字符串段/类型段有 4 字节对齐填充，读取时必须跳过。
- SDNA 结构字段偏移依赖 C 对齐；`FileGlobal` 布局与 DNA 声明不一致时只报告，不猜值。

### Fixtures

- 用户提供的临时 `Kirisame_Body.blend`（Blender 5.0，17 字节头，41,645,786 字节，33,443 个块）
  用于真实回归，验证后按用户要求删除；测试不依赖该文件。
- 测试使用合成的 v1（17 字节头）与 v0（12 字节头，32/64 位指针）样本，覆盖 DNA、GLOB、TEST、
  DATA、ENDB 和 ZSTD→Raw。

---

## 15. Protocol Buffers（无 .proto 描述符）

### 规范

- [Protocol Buffers Encoding](https://protobuf.dev/programming-guides/encoding/)：key、varint、
  wire type 0/1/2/3/4/5、length-delimited、group。
- 不使用 `.proto`、descriptor set、反射注册表或字段名数据库。

### 命名

- parser id `protobuf`，根节点 `Protocol Buffers`。
- TreeView 只显示 wire type 的规范名称：`varint`、`fixed64`、`length-delimited`、`start-group`、
  `fixed32`；不显示字段号、括号、payload 类型等额外字符。
- 字段号与 payload 分类在右侧解析视图显示：
  - 标题为 `VarintField '1'`、`LengthDelimitedField '2'`、`Fixed64Field '3'`、
    `Fixed32Field '4'`、`GroupField '6'`；单引号内是原始 `field_number`。
  - 原始字段：`field_number`、`wire_type`（`0 (varint)` 等）、`key`（`0x08`）、
    `value`、`length`、`end_group_field_number`。
- 派生字段：`<PayloadKind>`（`string` / `message` / `bytes` / `empty`）、`<Text>`、
  `<HexPreview>`、`<FieldCount>`、`<PayloadLength>`、`<UnsignedValue>`、`<SignedValue>`、`<Note>`。

### wire format

- key：`(field_number << 3) | wire_type`，varint 编码。
- wire type 0 `varint`：最多 10 字节；第 10 字节只能是 `0x00` / `0x01`，否则无效。
- wire type 1 `fixed64`：8 字节 little-endian。
- wire type 2 `length-delimited`：varint 长度 + payload。
- wire type 3/4 `start-group` / `end-group`：嵌套字段直到同字段号的 end-group；end-group key 属于
  group 节点范围，但不属于 group 的 payload 范围。
- wire type 5 `fixed32`：4 字节 little-endian。
- field number 0 与 wire type 6/7 非法；遇到后停止该层解析，剩余范围成为 `<unparsed data>`。

### length-delimited 分类

没有 schema 时，length-delimited 的 payload 可能是 string、bytes 或嵌套 message。显示分类顺序：

1. payload 为空 → `empty`。
2. payload 是合法、可打印的 UTF-8（允许 tab/CR/LF，不允许 NUL 与其他控制字符）→ `string`。
3. 否则完整 payload 能按 wire format 严格解析 → `message`，递归生成子节点。
4. 否则 `bytes`，只显示前 32 字节 Hex 前缀。

分类只影响派生 `<PayloadKind>` 与是否建子节点，不改变字段范围、长度或原始字节。可打印但实际是
二进制 bytes 的 payload 会被显示为 string；这是无描述符时的已知歧义。

### 范围与树

- 每个字段节点范围 = key + value（length-delimited 包含长度 varint + payload；group 包含 start/end key）。
- length-delimited 嵌套 message：节点 `Container`，`PayloadRange` 是 payload；子节点必须覆盖整个 payload。
- group：节点 `Container`，`PayloadRange` 是 start-group 与 end-group 之间的 body；没有匹配的
  end-group 时节点延伸到父范围末尾并报 `TRUNCATED_PAYLOAD`。
- 重复字段号不合并，每个编码实例都是独立兄弟节点。
- 截断的 length-delimited 字段范围延伸到可读末尾，节点为 `Unparsed`，报 `TRUNCATED_PAYLOAD`。
- 非法 key/varint/wire type 后停止当前消息层，剩余范围成为显式 `<unparsed data>` 并诊断。
- 顶层每个 byte 必须落在字段节点或显式未解析节点内。

### 探测

- protobuf 没有 magic，不能像 PNG/FLAC 一样强匹配。
- 固定 512 KiB 窗口内完整、合法的 wire 结构：
  - 完整且至少两个字段、且至少一个 length-delimited payload 是文本或嵌套消息 → `Strong`；
  - 完整、存在文本/嵌套证据，或窗口边界切断一个合法字段尾部 → `Weak`；
  - 其他 → `NoMatch`。
- `ProbePriority = 90`，低于全部 magic 格式；已有格式强匹配优先，无法形成合法结构的文件落到 RawData。
- 探测只看 framing 和有限 payload 采样，不构建树。

### 诊断

- `INVALID_FIELD_NUMBER`：field number 为 0 或超过 29 位。
- `INVALID_WIRE_TYPE`：wire type 6/7。
- `INVALID_VINT`：varint 超长/溢出。
- `TRUNCATED_HEADER` / `TRUNCATED_PAYLOAD`：key、长度或 payload 截断。
- `SCHEMA_MISMATCH`：无 start-group 的 end-group。
- `LIMIT_STRING` / `INVALID_ENCODING`：文本前缀超限或非法 UTF-8。

### 已知坑

- length-delimited 无 schema 时没有唯一类型；不要为了“树更漂亮”把任意 bytes 猜成 message。
- 嵌套 message 判定必须要求整个 payload 严格解析，不能只解析前缀。
- group 的 end-group key 不能重复计入子节点；group payload 必须排除 end key。
- 64 位 varint 的负 int32 会显示为很大无符号值，`<SignedValue>` 只作为派生解释。
- packed repeated、map、oneof、enum、默认值都需要 `.proto`，不能从 wire bytes 恢复。
- 探测不能抢走 magic 格式；不要提高优先级高到与 PNG/JPEG/RIFF 竞争。

### 测试

- `ProtobufParserTests` 使用内存构造消息，覆盖：
  - varint 150、UTF-8 string、opaque bytes、fixed64、fixed32、嵌套 message；
  - start/end group 与未终止 group；
  - 重复字段号；
  - 截断 length-delimited、非法 field number、非法 varint、无 start-group 的 end-group；
  - 探测 Strong/Weak、文本/零填充回退 Raw、单 varint 字段弱匹配、超过 512 KiB 探测窗口的字段尾部。

---

## 16. NBT（Named Binary Tag，Minecraft）

### 规范

- [NBT format](https://minecraft.wiki/w/NBT_format)：tag type、名称、payload、List/Compound 结构。
- 只支持**无压缩** NBT；gzip/zlib 压缩 NBT 与 MCA/MCR 区域文件不解析，按 RawData 处理。

### 命名

- parser id `nbt`，根节点 `Named Binary Tag`。
- **TreeView 只显示规范 tag 类型名**：
  `End`、`Byte`、`Short`、`Int`、`Long`、`Float`、`Double`、`ByteArray`、`String`、`List`、
  `Compound`、`IntArray`、`LongArray`。
  - 不显示 tag 名称、引号、索引、括号或其他额外字符。
- tag 名称 / 列表索引在右侧标题显示：
  - 命名 tag：`Compound 'root'`、`Int 'health'`、`String 'name'`；
  - 列表元素：`Int '[0]'`、`Compound '[1]'`；
  - 根 tag 没有名称时：`Compound ''`。
- 命名 tag 原始字段：
  - `type`：`1 (TAG_Byte)`、`10 (TAG_Compound)` 等；
  - `name`：原始 tag 名称（UTF-8 解码后转义控制字符）；
  - `value`：标量/字符串值；
  - `length`：String/ByteArray/IntArray/LongArray 的原始长度。
- 列表字段：`element_type`、`length`；元素节点使用派生 `<ElementType>` 与 `<Index>`。
- 其他派生字段：`<HexPreview>`、`<PreviewValues>`、`<ChildCount>`、`<EndTagPresent>`、
  `<RawBits>`、`<PayloadLength>`。

### 二进制结构

- 全部多字节整数与浮点数为 big-endian。
- 命名 tag：1 字节 `type` + `u16` 名称长度 + UTF-8 名称 + payload。
- `TAG_Byte`：1 字节有符号；`TAG_Short`：2 字节；`TAG_Int`：4 字节；`TAG_Long`：8 字节。
- `TAG_Float`：4 字节 IEEE-754；`TAG_Double`：8 字节 IEEE-754。
- `TAG_Byte_Array`：`i32` 长度 + 字节；`TAG_Int_Array`：`i32` 长度 + `i32` 元素；
  `TAG_Long_Array`：`i32` 长度 + `i64` 元素。
- `TAG_String`：`u16` 字节长度 + UTF-8 字节。
- `TAG_List`：1 字节元素类型 + `i32` 元素数 + 无名称元素；元素类型 0 且元素数 0 表示空列表。
- `TAG_Compound`：命名 tag 序列 + `TAG_End`；`TAG_End` 属于 compound 编码范围，但不是子节点。
- 根 tag 通常是 `TAG_Compound`，名称可为空。

### 范围与树

- 根节点 `Named Binary Tag` 是合成节点；其下是实际根 tag，之后若有字节则为显式
  `<trailing data>`。
- 命名 tag 节点范围包含 `type`、名称和 payload；compound 范围包含最后的 `TAG_End`。
- compound 的 `PayloadRange` 排除 `TAG_End`；子节点必须覆盖该 payload。
- 列表元素节点范围只覆盖元素 payload，不包含列表头的元素类型/长度。
- 大数组不逐元素建节点，只显示 `length` 与有界预览。
- 列表元素逐个建节点；列表本身是 container。
- 截断/负长度：节点成为 `Unparsed`，范围延伸到可读末尾并诊断；compound 缺少 `TAG_End` 时保留
  已解析子节点、范围到父末尾并报 `TRUNCATED_CHILD`。

### 探测

- NBT 没有 magic；探测要求根 tag 为 `TAG_Compound`。
- 固定 512 KiB 窗口内验证完整 framing：
  - 根 compound 完整覆盖文件 → `Strong`；
  - 窗口在 tag 中间结束，或根 tag 后有 trailing bytes → `Weak`；
  - gzip/zlib 首字节、MCA 区域表、非 compound 根或结构非法 → `NoMatch`。
- `ProbePriority = 95`，低于 magic 格式；压缩 NBT 与 MCA 最终落到 RawData。

### 诊断

- `INVALID_TAG_TYPE`：type > 12、`TAG_End` 出现在命名 tag 位置、列表元素类型非法。
- `INVALID_LENGTH`：负数数组/列表长度。
- `TRUNCATED_HEADER` / `TRUNCATED_PAYLOAD`：名称、长度或值截断。
- `TRUNCATED_CHILD`：compound 缺少 `TAG_End`，或列表元素不足。
- `INVALID_ENCODING` / `LIMIT_STRING`：字符串/名称非法 UTF-8 或超过 `MaxStringBytes`。
- `TRAILING_DATA`：根 tag 后仍有字节。

### 已知坑

- NBT 字符串历史上可能使用 Java modified UTF-8；本解析器按标准 UTF-8 解码，极端编码只报诊断，
  原始字节仍可查看。
- `TAG_End` 的 type 字节是 compound 的结束标记，不是独立节点；不要为它创建子节点。
- 列表元素没有名称；不要把父列表名称重复到每个元素。
- 数组长度是 `i32`，不是 `u32`；负值必须诊断，不能当作巨大正数。
- 不要把 gzip/zlib 压缩 NBT 当无压缩 NBT 探测；首字节不是 `TAG_Compound`。
- 不要把 MCA 的 location/timestamp 表当 NBT。

### 测试

- `NbtParserTests` 使用内存构造 NBT，覆盖：
  - 12 种 tag、空 compound、空 list、list of Int、list of Compound、嵌套 compound；
  - 命名 tag 名称与列表索引只出现在右侧解析视图；
  - 缺失 `TAG_End`、负数组长度、gzip/zlib/MCA-like 回退 Raw；
  - 探测 `Strong` 与树覆盖校验。

---

## 17. RawData

### 规范

- 无

### 范围

- 整个文件
- 根下单个 `Data` 节点

### 用途

- 未知格式兜底
- 损坏文件兜底
- 不抛异常

### 注意

- 仍要遵守大文件/惰性读取
- 不把 RawData 整块进内存
- 报告标明“未识别格式”

---

## 18. 内嵌 ID3 / EXIF / ICC

### 定位

不是顶层格式探测目标；由容器格式在对应位置调用。

### ID3v2

- 规范：ID3v2.2 / 2.3 / 2.4
- 入口：RIFF `id3 `（WAVE）；AIFF/裸 MP3 未来可复用同一 `Id3Parser`
- 字段：Header、Extended Header、Footer、Frames（`frame[n].` 前缀）
- 注意：synchsafe 尺寸、unsynchronisation、帧编码、压缩/加密帧只保留 payload
- v2.4 帧尺寸优先按 synchsafe 读取；若普通 big-endian 读法才落在合法帧边界，则接受普通读法（部分编码器不合规）
- CHAP/CTOC 子帧复用同一帧循环；APIC 图像只标注类型与长度，不解码
- 文本编码保留；中文按声明编码

### EXIF

- 规范：CIPA DC-008 / Exif；TIFF 6.0 字段类型
- 入口：JPEG `APP1`（跳过 `Exif\0\0`）、PNG `eXIf`（直接 TIFF header）与 PSD resource 1058/1059（TIFF header 存在时）
- 结构：TIFF header、IFD0 → IFD1 链、Exif/GPS/Interop 子 IFD
- 注意：字节序、所有 offset 相对 TIFF header、越界、循环 IFD、大字符串
- 字段名使用官方标签名（`IFD0.Make`、`ByteOrder`、`TIFFMagic`、`IFDOffset`），解释值保留在值侧
- MakerNote 只识别厂商签名并标注“未解析”；缩略图只做范围检查，不解码图像
- GPS 生成 `Computed.GPSPosition` 汇总

### ICC profile

- 规范：ICC.1:2022（profile version 4.4.0.0）
- 入口：PNG `iCCP`（zlib 解压后）、JPEG `APP2` `ICC_PROFILE`（单段）与 PSD resource 1039
- 结构：128 字节 header → tag count（u32）→ tag table（每项 12 字节：signature、offset、size）→ tag payload
- 字段名使用规范原名：`Profile size`、`Preferred CMM type`、`Profile version`、`Profile/device class`、
  `Data colour space`、`Profile connection space (PCS)`、`Rendering intent`、`Profile ID` 等
- tag 数据用 `Tag[n].` 前缀；常见 tag type（`XYZ `、`curv`、`para`、`text`、`desc`、`mluc`、`sig `、
  `sf32`、`ui32`、`ui16`、`ui08`、`dtim`、`meas`、`view`、`chrm`）解析为字段
- LUT 类只解析通道数、矩阵、网格声明与 CLUT precision，不展开采样点
- 未知 tag type 只给 Note，原始字节仍可通过 Hex 查看
- 不做颜色变换，不做跨 tag 一致性校验；JPEG 多段 profile 不拼接

### 限制

- 最大字符串/条目
- 最大 IFD 深度（6）/ 总 IFD 数（32）/ IFD 链长（8）
- ICC tag 条目、数组元素与 LUT 网格声明受 `MaxDataLines`；tag 数据范围受 profile size 约束
- JPEG 多段 ICC 不拼接，只保留分段框架
- 截断诊断

---

## 19. 格式状态建议

| 格式 | 状态 | 备注 |
|---|---|---|
| Raw | 稳定 | 兜底 |
| RIFF | 稳定 | `LIST:movi` 整体不展开 |
| EBML | 稳定 | 重点测试 unknown size |
| FLV | 稳定 | Enhanced audio 未展开；Multitrack/AMF3 已支持 |
| FBX | 稳定 | 32/64 位记录；数组只记录长度；footer 可验证时拆出 id/version/trailer |
| PNG | 稳定 | CRC 按需计算（含 `IDAT`/`fdAT`，>512 KiB 显示进度）；`eXIf` 完整解析；`iCCP` 解压后完整解析 ICC profile；`cICP` 按 H.273 码点展示；`mDCV`/`cLLI` 已解析 |
| JPEG | 稳定 | APPn 按 Identifier 分派；Exif/XMP/Photoshop 8BIM/单段 ICC 解析，其余保留 `Unparsed` |
| IsoBmff | 稳定 | 最大；ASC/SPS/PPS/FLAC/`fdel`/`alis` 已补全 |
| MIDI | 稳定 | `MThd`/`MTrk`、逐事件节点、running status/SysEx/meta；SysEx payload 不解释 |
| Ogg | 稳定 | `Page` 级物理树、lacing、CRC；Vorbis/Opus/Theora/Speex/FLAC/Skeleton header；setup header 与音视频 packet 不解码 |
| FLAC | 稳定 | `fLaC` + metadata block 树（STREAMINFO/PADDING/APPLICATION/SEEKTABLE/VORBIS_COMMENT/CUESHEET/PICTURE）；SEEKPOINT/track/index 子节点；音频帧整体 `Unparsed` |
| Blender | 稳定（无压缩） | 块序列 + SDNA 标注；DNA1/GLOB/TEST/ENDB 解析，其余内存块 `Unparsed`；ZSTD/Gzip 按 RawData |
| Protobuf | 稳定（启发式） | 不加载 `.proto`；TreeView 只显示 wire type 规范名；`field_number`/`wire_type`/`key` 在右侧解析视图；length-delimited 文本/嵌套消息/字节分类是显示启发式；无 magic，低优先级弱探测 |
| NBT | 稳定（无压缩） | 12 种 tag、命名 tag/列表元素/compound 树；TreeView 只显示规范类型名，名称/索引在右侧解析视图；数组只显示长度与预览；压缩 NBT/MCA 按 RawData |
| PSD/PSB | 不稳定 | 基础 section/resource/layer 已完成，但 Adobe 私有 descriptor、adjustment/layer effect 与部分 mask/blending 细节仍可能只显示原始范围；部分节点名是本工具命名 |
| ASF | 稳定 | Data Packets 不切分；Index Blocks/加密对象未展开 |
| ID3/EXIF | 稳定 | ID3v2.2/2.3/2.4 与 EXIF IFD 完整解析 |
