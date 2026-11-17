# 08 — 格式笔记

> 记录当前实现依赖的规范来源、命名、范围语义与已知坑。
> 完整历史细节（含已删除样本与实现文件列表）见 `archive/08-format-notes-full.md`。
> 通用字段/节点规则见 `03-style-guide.md`。

---

## 1. 通用规则

- 原始字段名以官方规范为准，保持官方写法；无官方命名时用描述性英文键，不加尖括号。
- 所有 offset/length 为 64 位；大 payload 只记录范围；截断/非法长度必须有诊断。
- 诊断只针对格式本身：截断、长度、签名、块内枚举、块内 CRC；不检查跨块/跨字段语义（D-028）。
- 节点范围默认覆盖 padding；例外必须在本文件记录。
- 块身份值（4CC/GUID/marker/节点名）已在标题中，字段区不重复。
- 根节点显示格式全称（D-014）；Hex 只按当前节点范围渲染，不读下一个块。
- 不设解析时间/深度/节点数限制（D-032）；报告无总预算，Hex 固定首尾 128 KiB（D-034）。

---

## 2. ISOBMFF

**规范**：ISO/IEC 14496-12；相关 14496-15（AVC/HEVC）、23008-12（HEIF）、AV1 ISOBMFF。

**标识**：普通 box `size`(4) + `type`(4) + payload；`size == 1` 用 8 字节 `largesize`；
`size == 0` 延伸到父容器末尾；`uuid` 额外 16 字节 type；FullBox 有 version+flags。
树显示 4CC，标题如 `FileType 'ftyp'`、`(QuickTime)Title '©nam'`。

**容器**：`moov/trak/edts/mdia/minf/dinf/stbl/mvex/moof/traf/mfra/udta/ilst/ipro/sinf/schi/rinf/
fiin/paen/meco/iprp/ipco/grpl/strk/strd/cinf/tref/trgr/hnti/hinf/----/tapt/meta/iinf/iref/dref/
trep/gmhd`。容器前缀按规范跳过：`meta` 区分 ISO FullBox 与 Apple 无 version/flags；
`iinf` 的 entry_count 宽度随 version；`iref` 跳 4、`dref` 跳 8、`ipro`/`fiin` 跳 6、`trep` 跳 8。

**覆盖**：

- 通用：`ftyp`、`styp`、`uuid`、`mdat`、`free`、`skip`、`wide`、`Xtra`、未知 box。
- 电影/轨道：`mvhd/tkhd/mdhd/hdlr/elst/vmhd/smhd/url /urn /alis/iods/gmin/text/tcmi/
  clef/prof/enof/csgm`。
- sample table：`stsd/stts/stsc/stsz/stco/co64/stss/sdtp/ctts/sbgp/sgpd/cslg`。
- 分片：`mehd/mfhd/trex/tfdt/tfhd/trun/sidx`。
- HEIF/AVIF：`pitm/iloc/idat/infe/ispe/irot/imir/pixi/colr/auxC/av1C/hvcC/clli/ipma`
  entity-to-group。
- QuickTime/3GPP 元数据：`keys/mean/name/data`、`©xxx`、`covr`、`stik/hdvd`、
  `titl/dscp/perf/auth/gnre/cprt/coll/albm/kywd/clfn/reel/scen/shot/slno/manu/modl/cmid/
  swre/date/name/thmb`。
- 引用/分组：`iref` 的 `dimg/base/eroi/evir/exbl/mask/mint/pred/prem/rpds/auxr/fdel/auxl/cdsc`；
  `tref` 的常见 4CC；`grpl` entity-to-group。
- sample entry/codec：`avcC`（含 SPS/PPS 几何）、`hvcC/hvcE`、`esds`（含 AudioSpecificConfig）、
  `av1C`、`dfLa`（全部 metadata block）、`dOps/dac3/dec3/dvcC/dvvC/dvwC/vpcC`、
  `SmDm/CoLL/btrt/pasp/colr/clap/srat/frma/schm/sinf/schi/wave`、`uri /uriI/name`、
  `damr/alac/mhaC/pcmC/chnl/dawp/d263/enda`；未知 codec 记 size + 有界 hex。

**上下文相关 4CC**：

- 稳定 ID 编码为 `isobmff:{start}|{parentType}`，节点解析时取回父 box 类型。
- `thmb` 随父（`udta`/`meta` = 3GPP thumbnail；`iref`/`tref` = 引用）解释。
- `iref` 子 box 的 item ID 宽度按 payload 长度匹配 `4 + 2n` 或 `8 + 4n`；都匹配时选 16 位并警告。
- `stsd` 内与 `ipco`/`gmhd` 下的同名 4CC 使用不同字段命名。

**已知坑**：

- `stsd` 音频 sample entry：6 字节 reserved 后要跳过 `revision`(u16)+`vendor`(u32)，
  否则会把 `channelcount`/`samplesize` 当成采样率。
- `(int)payloadSize` 会截断；64 位 `largesize > long.MaxValue` 拒绝为非法长度。
- `Data` box 的 type 可能是 1/2/3 字节；不要用固定 `ToInt32`。
- `esds` 的 `buffer_size_db` 是 24 位，不是 32 位。
- `alis` 的结束标记 `0xFFFF` 只有 2 字节，循环条件必须是“剩余 ≥ 2”。
- 大 `mdat`/`idat`/`covr` 不能整块读；`stsd` 固定为叶子。
- 表循环上限来自 `MaxDataLines`，超限产生 `LIMIT_RECORDS`。

---

## 3. RIFF

**规范**：Microsoft RIFF；WAV/AVI/WebP/WEM 等子规范；OpenDML AVI Extensions 1.02 §3/§5。

**范围**：`ckID`(4) + `ckSize`(4) + data；`ckSize` 不含 header；奇数长度后有 1 字节 pad；
**pad 计入节点长度**，保证范围连续。根支持 `RIFF`/`RIFX`/`RF64`/`BW64`；`RIFX` 子块大端。

**覆盖**：

- `RF64`/`BW64` 的 `ds64` 在根扫描时读取；`data` 的 `0xFFFFFFFF` 优先取 `ds64.DataSize`，
  缺失时回退可用范围并诊断。
- `LIST` type 后递归；未知 LIST type 仍递归，避免把可见子块当 raw。
- AVI `LIST:movi` 整块 `Unparsed`，只显示 `listType`、长度与 Note（D-016）。
- WebP `ANMF` 16 字节帧头后递归；`VP8X`/`ANIM` 提供常用字段。
- WAVE `fmt ` 支持 WAVEFORMATEX/EXTENSIBLE GUID；`data` 不解码；`wavl` 递归 `data`/`slnt`。
- AVI `avih`/`strh`/`strf`/`idx1`；OpenDML `dmlh` 与 `indx`/`ix00`–`ix99`：
  `bIndexType = 0` 为 index-of-indexes，`= 1` 为 chunks；`AVI_INDEX_2FIELD` 多读
  `dwOffsetField2`；未覆盖布局保留 `aIndex[n].adw[k]` 与未解释长度。
- ANI `anih`/`icon`；DLS `colh/vers/msyn/insh`，`ins `/`lrgn`/`rgn `/`rgn2`/`wave` 递归。
- Wwise `BKHD/DIDX/HIRC` 只提供 header/index/count；HIRC 对象体与 DATA 不解码。
- FMOD `FEV ` bank 的 `FMT `/`BNKI` 等只登记名称，无字段 handler；真正未知 chunk 整块 `Unparsed`。

**已知坑**：嵌套 LIST 的 pad；RF64 64 位 size；顶层 trailing；短 chunk 的 Hex 最后一行
不得读入下一个 chunk；`ckID` 不在字段区重复。

---

## 4. EBML

**规范**：RFC 8794（EBML）；RFC 9559（Matroska/WebM schema）；legacy 元素作兼容补充。

**范围**：

- element = ID(VINT 1–4 字节) + data size(VINT 1–8 字节) + payload。
- unknown size 只允许 `Segment`/`Cluster`；其他 master 或叶子出现时产生 `UNKNOWN_SIZE`。
- unknown-size `Segment`：在下一个 `EBML` 或 `Segment` 前结束，否则到文件末尾。
- unknown-size `Cluster`：在下一个 Segment 子元素（如新 `Cluster`）前结束；
  `Timestamp`/`SimpleBlock` 等内部元素不终止它；否则到父元素末尾。
- `Void`/`CRC-32` 是全局元素，永不终止 unknown-size 父元素。
- 未知元素整块（header+payload）`Unparsed`，不产生子节点。
- `SimpleBlock`(0xA3)/`Block`(0xA1)/`BlockVirtual`(0xA2) 是高熵 payload，整块 `Unparsed`，
  只保留范围、长度派生字段与短 Note，不解析 track/timecode/flags/lacing，也不做内联预览（D-033）。
- 叶子 `Leaf`；`Void` 为 `Padding`；master 为 `Container`；trailing/非法 header 显式 `Unparsed`。
- 根 `EBML File` 是合成节点，范围覆盖整个文件。

**已知坑**：

- VINT 前导位/长度；data size 全 1 位表示 unknown，不是普通长度。
- `CRC-32` 官方名带连字符；不要写 `CRC32`。
- 日期是自 2001-01-01 UTC 的 signed nanoseconds；派生 ISO 值可能超出 `DateTimeOffset`。
- 文本可能非法 UTF-8，解码不能抛异常。
- unknown-size 边界不能一律延伸到父末尾，否则吞掉后续 `Segment`/`Cluster`。
- 大 `CodecPrivate`/`Tags` 不能整块进内存。

---

## 5. FLV

**规范**：Adobe FLV v10.1+；Veovera Enhanced RTMP v2（Multitrack）；Adobe AMF3（2013）。

**范围**：

- Header 9 字节 + `DataOffset` 扩展 + `PreviousTagSize0`(4)。
- Tag = 11 字节 header + data + `PreviousTagSize`(4)；**节点长度包含 `PreviousTagSize`**。
- 树：合成根 `FLV File`；`FLV Header`；`PreviousTagSize0`；每个 tag 为 Container，
  子节点 `Tag Header`/`Tag Data`/`PreviousTagSize`；未知 TagType 的 data 为 `Unparsed`。

**覆盖**：

- 字段按规范：`Signature/Version/Flags/DataOffset/PreviousTagSize0/TagType/Filter/DataSize/
  Timestamp/TimestampExtended/StreamID/PreviousTagSize` 与音频/视频/AMF 字段。
- Enhanced FLV：`IsExHeader`、`VideoFrameType`、`VideoPacketType`、`VideoFourCc`；
  `SequenceStart/CodedFrames/SequenceEnd/CodedFramesX/Metadata/MPEG2TSSequenceStart/
  Multitrack/ModEx` 头部；`CodedFrames` 对 AVC/HEVC/VVC 读 `compositionTimeOffset`。
- Multitrack（`VideoPacketType = 6`）：高 4 位 `videoMultitrackType`，低 4 位 packet type；
  `ManyTracksManyCodecs` 每 track 带独立 `videoFourCc`；每 track 读 `videoTrackId`，
  非 `OneTrack` 再读 UI24 `sizeOfVideoTrack` 并按范围前进；媒体 payload 不解码。
- 传统 CodecID 12：`HEVCPacketType` + `CompositionTime`，只做头部；legacy 约定。
- AMF0：`onMetaData` 与任意名称-值对；支持 Number/Boolean/String/Object/ECMA Array/
  Strict Array/Date/Long String/XML/Typed Object；marker `0x11` 进入 AMF3。
- AMF3：Undefined/Null/False/True/Integer(U29)/Double/String/Date/Array/Object/XMLDocument/
  XML/ByteArray/Vector/Dictionary；维护 string/object/traits 三表；引用显示 `@ref n`；
  空字符串不进 string table；表在每个 script tag 重置。
- AMF3 externalizable body 长度不可知，只显示 `<ClassName>` + Note 并停止该值。
- `PreviousTagSize`/`PreviousTagSize0` 校验，不匹配报 `PREVIOUSTAGSIZE_MISMATCH`。

**限制**：Enhanced audio、真实 HEVC 码流、加密/过滤 payload、AMF3 externalizable 不解析。
ByteArray 只读有界 Hex；文本受 `MaxStringBytes`，值数量受 `MaxDataLines`。

---

## 6. ASF

**规范**：Microsoft ASF Specification Rev 01.20.05（2010）；GUID 名称另参考 getID3，
非规范 GUID 必须标注来源。

**范围**：

- 每个 object：`Object ID` GUID(16) + `Object Size` QWORD(8, LE，**含 24 字节头**) + payload。
- Header Object 的子 object 从头后 6 字节（文件 offset +30）开始；
  Header Extension 的子 object 从 offset +46 开始，长度由 `Header Extension Data Size` 限定；
  Data Object 的 packet 区域从 offset +50 开始。
- `Object Size = 0` 只允许 Data Object，且要求 Broadcast Flag；实现截到文件末尾。
- Data Object 内 packet 不是 GUID object：整个区域是一个 `Unparsed` 子节点（D-009）。

**已知坑**：

- GUID 字节序与 `Guid.ToByteArray()` 一致（前三段混合端序）；不要手工按全大端解释。
- Padding Object 没有 `Padding Data Size`，长度 = `Object Size - 24`。
- `Object Size` 是总长度，不是 payload 长度。
- Stream Properties 的 `Reserved` 在真实 WMV 中可能非 0，不要报警；
  `Flags` bit 0–6 是 Stream Number，bit 15 才是 Encrypted Content Flag。
- Metadata Object 的 BOOL 是 2 字节；Extended Content Description 的 BOOL 是 4 字节。
- Content/Extended Content Description 字符串长度单位是字节；Codec List 是 WCHAR（×2）。
- Index 三种变体首字段不同（DWORD）；Simple Index 的 `Index Entry Time Interval` 是 QWORD。
- `D9AADE20-...` 不在官方表，getID3 称 Index Placeholder Object，内容仍 `Unparsed`。
- UTF-16 常带结尾 NUL；按声明长度读，用 `\0` 转义。
- 未知 GUID/未实现 object 整块 `Unparsed`，显示 GUID 与 `<UnparsedLength>`。

---

## 7. FBX（二进制）

**规范**：Blender 公开的 FBX binary format specification（Gessler/Barton）；
64 位记录参考 FBX 7.5 发布说明；footer 参考 Blender `encode_bin.py` 与 FbxWriter，
只解析共同 framing（D-029）。非官方资料对 header 第 21/22 字节解释不一致，按实际字节显示。

**结构**：

- 头 27 字节：`Kaydara FBX Binary  `(20) + 0x00 + 0x1A + 0x00 + `Version`(u32 LE)。
- record < 7500：`EndOffset`(u32)、`NumProperties`(u32)、`PropertyListLen`(u32)、
  `NameLen`(u8)、Name、属性列表、可选子节点 + 13 字节 null record。
- record ≥ 7500：前三个字段 u64，null record 25 字节。
- property：`TypeCode` + 数据。标量 `Y/C/I/F/D/L`；字符串/原始 `S`/`R`（u32 长度）；
  数组 `f/d/l/i/b`：`ArrayLength`(u32)、`Encoding`(u32)、`CompressedLength`(u32)。
- `Encoding = 0` 原始数组；`= 1` deflate，**不解压**，只显示长度与 Note。

**范围**：

- node 范围 = `[record start, EndOffset)`；`EndOffset` 是**文件绝对偏移**。
- 有子节点时到 `EndOffset - nullRecordSize`；null record 属于 node 范围但不属于 payload。
- 文件头 `[0,27)` 是显式 `Header`；顶层从 27 开始；现代 FBX 没有匿名 root record。
- footer 是显式 `Footer`，仅在尾部签名、零填充与范围符合已确认 framing 时拆成：
  16 字节 footer code/id（不透明）、4–20 字节零、4 字节 version 回显、
  120 字节零、16 字节签名 `F8 5A 8C 6A DE F5 D9 7E EC E9 0C E3 75 8F 29 0B`。
  否则整块 `Unparsed` + Note。footer id 不是顶层 `FileId`，不解密。

**已知坑**：

- `EndOffset` 按相对长度解释会立即错位；7500+ 用 32 位会读出荒谬值。
- null record 只在有子节点时出现；`payloadRange` 必须减去它。
- `TypeCode` 大小写敏感：`I` int32 / `i` int32 数组；`S` 字符串 / `s` 未定义。
- `Encoding = 0` 时 `CompressedLength` 应等于 `ArrayLength × 元素大小`，否则 `SCHEMA_MISMATCH`。
- 字符串通常是 UTF-8，也可能含 NUL；非法 UTF-8 报 `INVALID_ENCODING`。
- `NameLen` 只有 1 字节；截断走诊断 + 显式节点，不改节点名。

---

## 8. PNG

**规范**：W3C PNG Third Edition；APNG Specification。

**结构**：signature 8 字节；chunk = `Length`(4 BE) + `Chunk Type`(4) + Data + `CRC`(4 BE)；
节点范围 `12 + dataLength`；`Length` 最大 `2^31-1`。支持：

```text
IHDR PLTE IDAT IEND tRNS cHRM gAMA iCCP sBIT sRGB cICP mDCV cLLI bKGD hIST pHYs sPLT
eXIf tIME tEXt zTXt iTXt acTL fcTL fdAT oFFs
```

未列出的已知/未知类型保持 `Unparsed`，只显示 Note。

**命名**：Third Edition 的 `Color type`/`Greyscale`/`Truecolor`/`Indexed-color` 等；
APNG 保持 snake_case（`num_frames`、`sequence_number`、`delay_num` 等）；
枚举显示“数字 + 短含义”；派生值 `<PayloadLength>`、`<FrameDelaySeconds>`、`<TextUTF8>` 等；
chunk 节点不重复 `Chunk Type`。

**颜色**：

- `cICP`：4 个 1 字节 H.273 码点，顺序 `Color primaries`/`Transfer function`/
  `Matrix coefficients`/`Video full range flag`；只解释码点，不校验组合；full range 只允许 0/1。
- `PLTE`：8-bit RGB，派生 `<PaletteEntryCount>` 与 `<Entry[n].Color>`。
- `tRNS`：颜色类型 0/2 用 2 字节样本（<16 位只用低位，派生 `<TransparentSample>`）；
  类型 3 逐字节 alpha（未列条目视为 255）。
- `bKGD`：按颜色类型给 `Greyscale`/`Red,Green,Blue`/`Palette index`；
  派生 `<BackgroundColor>`，索引色读 `PLTE`，灰度/真彩按 IHDR 位深缩放。
- `mDCV` 24 字节：色度 ×0.00002、亮度 ×0.0001 cd/m²；`cLLI` 8 字节：
  MaxCLL/MaxFALL ×0.0001；原始数字 + 缩放值；0 表示未知，不报警。

**CRC**：覆盖 chunk type + data，不覆盖 Length/CRC；多项式 `0xEDB88320`。
文档阶段不校验；选中节点时流式计算；所有 chunk（含 IDAT/fdAT）都计算；
超过 512 KiB 继续执行并复用进度条（D-037）。字段只有 `CRC`，失败时
`CRC = 0x文件值 (0x计算值)` 并报 `CRC_MISMATCH`。

**截断**：header 不足 8 字节 → `<trailing data>` + `TRUNCATED_HEADER`；
声明长度超出剩余 → 节点到 EOF + `TRUNCATED_PAYLOAD`；缺 IEND → `TRUNCATED_CHILD`；
IEND 后字节 → `<trailing data>` + `TRAILING_DATA`。

**文本与解压**：`tEXt` 声明 Latin-1，合法 UTF-8 且含非 ASCII 时额外 `<TextUTF8>`；
`iTXt` keyword/language 用 Latin-1，translated keyword/text 用 UTF-8；
非法 UTF-8 报 `INVALID_ENCODING`。D-011 允许 `iCCP`、`zTXt`、压缩 `iTXt` 有界 zlib 解压；
输出受 `MaxStringBytes`/`MaxBlobBytes`。`iCCP` 解压后交 `IccProfileParser`（D-036）。
`eXIf` 复用 `ExifParser`；`IDAT`/`fdAT` 不解压。

**APNG**：`acTL`/`fcTL`/`fdAT` 作为普通 chunk，不组装帧；`num_plays = 0` 为无限循环；
`delay_den = 0` 视为 100；不检查帧与默认图/序号的语义一致性。

---

## 9. JPEG

**规范**：ITU-T T.81；JFIF 1.02；Exif CIPA DC-008；Adobe Photoshop File Formats Specification
（2019-11）§Image Resources；XMP 只作文本读取。

**范围**：SOI `FFD8`、EOI `FFD9`；marker `FFxx`，`FF` fill bytes 可在 marker 前；
带长度 segment = `FFxx` + `Length`(2 BE，含自身) + payload；节点长度 `2 + Length`。
`SCAN` 只表示 SOS header 后的熵编码数据，不含下一个 marker；`FF00` stuffing 与
`FFD0`–`FFD7` restart marker 属于 scan；EOI 后 trailing 显式表示。

**scan 算法**：从 SOS payload 末尾开始；普通字节继续；遇 `FF`：连续 `FF` 继续找 code；
`00` 为 stuffed byte；`D0`–`D7` 为 restart；其他 code 是下一个顶层 marker，scan 结束。
渐进式 JPEG 有多个 SOS/SCAN，按同一规则循环。

**字段**：T.81/JFIF 语义名（`Precision/Height/Width/Number of components/
Component[n].Component identifier/...`）；组合字节保留原始字段，再用尖括号拆分解释。
APP0–APP15 统一输出 `Identifier` 与 `<ApplicationFormat>`；树显示 mnemonic
（`SOI`/`APP0`），标题用 `StartOfImage '\xFF\xD8'`，不重复 `Marker`。

**APP Identifier 分派（D-023）**：

| Identifier | 内容 |
|---|---|
| `Exif` | TIFF/Exif 完整解析（跳过 `Exif\0\0`） |
| `ICC_PROFILE` | 单段 ICC profile；`marker count > 1` 不拼接 |
| `Photoshop 3.0` | 8BIM image resources（与 PSD 共用） |
| `Adobe` | 固定 5 字节签名 + u16 version + flags（未解析） |
| `http://ns.adobe.com/xap/1.0/` | XMP 原文文本，不解析 XML |
| `JFIF` / `JFXX` | JFIF header/extension（未解析） |

- 识别只依赖 Identifier，不依赖 APP 序号；`Exif` 可出现在任意 APPn。
- ICC：Identifier 后是 `Marker sequence number`/`Number of markers` 两个 u8；
  仅 `count == 1` 时交 `IccProfileParser`；编号 0 或超总数报 `INVALID_ENCODING`。
- XMP：字段 `Text`（完整原文，保留 BOM/换行/空白）+ `<PacketByteLength>`；
  超 `MaxStringBytes` 只显示前缀并报 `LIMIT_STRING`。
- 8BIM 资源：`Signature`(4，通常 `8BIM`)、`ID`(u16 BE)、`Name`(Pascal，补偶数)、
  `Size`(u32 BE)、`Data`(补偶数)；`Resource[n].` 前缀。常用资源解析，其余只列 framing；
  1028 IPTC、复杂 descriptor（1065/1074–1088/3000 等）与私有资源不虚称已解码。
- `ResolutionInfo` 顺序 hRes/hResUnit/widthUnit/vRes/vResUnit/heightUnit；
  Pixel Aspect Ratio 是 u32 version + **一个** double。
- Counted Unicode 字符串只按 u32 code-unit count 读，**不消费猜测的 2 字节 null**。
- `8B64` 是 additional-layer signature，不按 image resource 猜读。
- `Adobe` 后两字节 version 不得被当作 NUL 终止符。

**已知坑**：segment 长度必须校验；`APP1` 不一定是 Exif；`FF` fill bytes 可在 marker 前；
未知/未启用 APP 不产生诊断，整段 `Unparsed`。

---

## 10. MIDI

**规范**：Standard MIDI Files 1.0（MMA）。

**结构**：`MThd` = 8 字节 chunk header + 6 字节标准头（`format`/`ntrks`/`division`），
更长只保留额外字节；`MTrk` = 8 字节 header + 声明长度；每个事件是独立子节点，
范围精确覆盖 `delta_time + status + data`。未知 chunk 整块 `Unparsed`；
零填充/截断/缺 `EndOfTrack`/事件越界有诊断；不按 `ntrks` 校验实际 track 数（D-028）。

**事件**：全部 channel voice 消息；running status（首字节 < 0x80 复用上一 status，
SysEx/meta/system 清除）；SysEx `F0`/`F7` 按 VLQ 长度跳过，只显示长度；
全部标准 meta（文本、Channel Prefix、MIDI Port、End of Track、Set Tempo、SMPTE Offset、
Time Signature、Key Signature、Sequencer-Specific）；文本按 Latin-1 并转义控制字符。
派生 `<NoteName>`、`<TempoBPM>`、`<TimeSignature>`、`<KeySignature>`、`<SmpteOffset>`、
`<PitchBend>`、`<ControllerName>`。事件逐行输出，不压成单行。

**已知坑**：running status 事件少一个 status 字节；VLQ 最多 4 字节；`EndOfTrack` 后
字节是 trailing；SysEx 长度包含其后数据字节。

---

## 11. PSD / PSB

> **状态：不稳定。** 基础 section/resource/layer 已实现，但 Adobe 私有/复杂结构
> （descriptor、adjustment/layer effect、厂商扩展、部分 mask/blending）可能只显示原始范围
> 或字段不完整；节点名是本工具命名。不要当作完整语义校验器（D-038）。

**规范**：Adobe Photoshop File Formats Specification（2019-11）；PSD v1 与 PSB v2 共用 section，
PSB 只加宽少数长度字段。

**结构**：

- 签名 `8BPS`；Header 26 字节：`Signature/Version(u16)/Reserved(6)/Channels(u16)/
  Height(u32)/Width(u32)/Depth(u16)/Color Mode(u16)`。
- 顺序：`ColorModeData` → `ImageResources` → `LayerAndMaskInformation` → `ImageData`。
- `ColorModeData`：u32 length + data（indexed 通常 768 字节），不解释。
- `ImageResources`：u32 length + 连续 8BIM block，每 block 为 `Resource[n] 'id (name)'`；
  与 JPEG APP13 共用 `PhotoshopImageResources`。
- `LayerAndMaskInformation`：PSD u32 / PSB u64；含 `LayerInfo`（`LayerCount` 为 i16，
  负数表示首个 alpha 是合并透明度；`LayerRecords`、`ChannelImageData`）、
  `GlobalLayerMaskInfo`（u32 length + overlay 字段）、additional layer information
  （`8BIM`/`8B64` + 4CC key + length + data）。
- `LayerRecord`：top/left/bottom/right、通道数与每通道 `Channel ID`/`Channel data length`、
  blend signature/key、opacity、clipping、flags、filler、extra length；extra 再含 mask、
  blending ranges、Pascal layer name（补到 4 字节）、additional info。
- `ChannelImageData`：按 layer record 顺序每 channel 一个 `ChannelData[n]`。
- `ImageData`：u16 compression（0 raw/1 RLE/2 ZIP/3 ZIP+prediction）+ data；
  只显示 compression/长度/Note，不解像素。
- 扫描失败或 channel data 无法归属时，剩余范围生成 `Unparsed` + 诊断。

**PSB 8 字节长度**：`LayerAndMaskInformation`、`LayerInfo`、每 channel 的
`Channel data length`；additional info 的 8 字节 length 仅用于
`LMsk/Lr16/Lr32/Layr/Mt16/Mt32/Mtrn/Alph/FMsk/lnk2/FEid/FXid/PxSD`。
`ColorModeData/ImageResources/GlobalLayerMaskInfo` 仍是 u32。

**Additional info 已解释**：`luni`（u32 count + UTF-16BE）、`lyid`（u32）、`lspf`（u32+位）、
`lsct`/`lsdk`（u32 + 可选 blend）、`clbl`/`infx`/`knko`（1 字节 + 3 字节 padding）、
`iOpa`（u8）、`lyvr`（u32）、`fxrp`（两个 BE double）。其余只显示 signature/key/length + Note。

**已知坑**：Counted Unicode 不消费猜测的 2 字节 null；layer name Pascal 补到 4 字节，
image resource Pascal 补到 2 字节；mask length 0 仍占 4 字节；channel data length 含
2 字节 compression；不按 LayerCount 之外的关系校验语义。

---

## 12. Ogg

**规范**：RFC 3533；Vorbis I；RFC 7845（Opus）；Theora；Speex `speex_header.h`；
FLAC-to-Ogg mapping；Ogg Skeleton draft。

**命名**：单一 parser `ogg`，根 `Ogg Encapsulation Format`；`.ogg/.oga/.ogv/.opus/.spx/.ogx`
不拆 parser。树只有物理 `Page` 节点，不按 codec/扩展名造伪节点。字段用各自规范标识符
（RFC 3533 snake_case、Opus 标签、Theora `VMAJ/PICW`、Speex 成员名、FLAC 全大写）。
完整 packet header 字段加 `Packet[n].` 前缀，跨页续传 packet 不在当前页解析。

**Page/lacing**：`capture_pattern = OggS`；header = 27 + `number_page_segments`；
字段 `stream_structure_version/header_type_flag/granule_position/bitstream_serial_number/
page_sequence_number/CRC_checksum/number_page_segments/segment_table[n]`。
`header_type_flag`：0x01 continued、0x02 BOS、0x04 EOS。多字节整数 LE。
`granule_position == -1` 表示本页无完成 packet。lacing：255 继续，<255 结束，0 是合法空 packet。
根摘要 `FirstPage`/`LastPage` 是物理序号，不是 `page_sequence_number`。
CRC：多项式 `0x04C11DB7`、初值 0、不反射、无最终 XOR；校验时 CRC 字段置 0；
选中页面计算 `<CRC check>`，失败报 `CRC_MISMATCH`。

**Codec header**（完整 packet 且 magic 匹配）：

| Magic | 结构 | 字段要点 |
|---|---|---|
| `0x01 vorbis` | Vorbis identification | version/channels/rate/bitrates/blocksize/framing |
| `0x03 vorbis` | Vorbis comment | vendor、user comments、framing |
| `0x05 vorbis` | Vorbis setup | 只标注 codebooks 未解释 |
| `OpusHead` | Opus ident | Version/Channels/Pre-skip/Input Rate/Output Gain/Mapping |
| `OpusTags` | Opus comment | Vendor/User Comment 长度与字符串 |
| `0x80 theora` | Theora ident | VMAJ/VMIN/VREV/FMBW/FMBH/PICW/PICH/... |
| `0x81 theora` | Theora comment | 同 Vorbis comment，无 framing |
| `0x82 theora` | Theora setup | 只标注量化/Huffman 表未解释 |
| `Speex   ` | Speex header | version/rate/mode/channels/bitrate/frame_size/... |
| `0x7F FLAC` | Ogg FLAC mapping | 版本、header packet 数、`fLaC`、STREAMINFO |
| `fishead\0` | Skeleton ident | 版本、时间分子/分母、UTC、v4 segment 字段 |
| `fisbone\0` | Skeleton secondary | serial/granulerate/basegranule/preroll/... |

Theora 最后 16 bit 按 MSB-first 拆 `QUAL`(6)/`KFGSHIFT`(5)/`PF`(2)/`Res`(3)；
comment 4 字节长度统一 LE；`PICW`/`PICH` 固定读 24 bit。

**限制**：跨页 header packet 不解析；Speex comment 无固定 magic 不解析；
FLAC 只解析首包 mapping + STREAMINFO；不把 granule 换算成秒；不做 packet 重组/连续性校验/
音视频解码；未知 codec BOS 只显示 `Unknown`。

---

## 13. FLAC

**规范**：RFC 9639（2024-12）；Xiph FLAC 索引；Ogg FLAC 映射见 RFC 9639 §10.1。

**命名**：parser `flac`，根 `Free Lossless Audio Codec`；结构名与字段**全大写**：
`STREAMINFO/PADDING/APPLICATION/SEEKTABLE/VORBIS_COMMENT/CUESHEET/PICTURE/RESERVED`，
`SEEKPOINT/CUESHEET_TRACK/CUESHEET_TRACK_INDEX`；字段如 `MINIMUM_BLOCK_SIZE/
SAMPLE_RATE/TRACK_OFFSET`。签名 `fLaC` 不单独建节点，根 payload 从偏移 4 开始。

**结构**：4 字节签名 + metadata block 序列（至少 STREAMINFO）+ 音频帧。
块头：1 bit LAST + 7 bit TYPE + 24 bit LENGTH；已知 TYPE 不重复输出，RESERVED/127 输出数值。

- `STREAMINFO`（0，34 字节）：min/max block、min/max frame、20 bit `SAMPLE_RATE`、
  3 bit `(CHANNELS-1)`、5 bit `(BITS_PER_SAMPLE-1)`、36 bit `TOTAL_SAMPLES`、
  128 bit `MD5_SIGNATURE`；channels/bits 显示还原值。
- `PADDING`（1）：长度 + Note。
- `APPLICATION`（2）：`APPLICATION_ID`（hex + 可打印 ASCII）+ `<DATA_LENGTH>`。
- `SEEKTABLE`（3）：每 `SEEKPOINT` 18 字节（`SAMPLE_NUMBER/OFFSET/FRAME_SAMPLES`），
  每个点建子节点；长度非 18 倍数时剩余 `<unparsed data>` + 诊断。
- `VORBIS_COMMENT`（4）：与 Ogg 共用 `VorbisComment`，但大写键、无 framing bit、长度 LE。
- `CUESHEET`（5）：396 字节固定头（catalog 128、lead-in 64、CD flag、reserved、track count）；
  `CUESHEET_TRACK` 36 字节 + N 个 `CUESHEET_TRACK_INDEX`（12 字节），均建子节点。
- `PICTURE`（6）：type/media type/description/width/height/depth/colors/data length；
  图像不解码。
- 音频帧：最后 metadata block 之后全部字节为一个 `<audio frames>` `Unparsed`，
  只显示 `<AudioDataLength>`；不解析 frame header/subframe/CRC。

**已知坑**：除 Vorbis comment 的 4 字节长度外，多字节整数都是 **big-endian**；
block size 必须 16–65535 且 min ≤ max；channels/bits 存储值减 1；
`TOTAL_SAMPLES = 0` 与全 0 MD5 表示未知；不校验 frame CRC/MD5；
不要把音频里的 `0xFFF8` 当 metadata。Ogg FLAC 的 STREAMINFO 同构、同为大写。

---

## 14. Blender（.blend）

**规范**：Blender `BLO_core_blend_header.hh`/`BLO_core_bhead.hh`；`DNA1` SDNA；
归档的 Blender File Format 文档。

**命名**：parser `blend`，根 `Blender File`；树节点用 4 字节 block code（NUL 截断），
如 `REND/TEST/GLOB/WM/DATA/DNA1/ENDB/OB/ME/SCE`；块头字段用源码名
`len/old/SDNAnr/nr`；根字段 `endian/file_version`，v0 的 `pointer_size` 是原始头字节，
v1 指针固定 8（`<PointerSize>`），`<HeaderSize>`/`<FileFormatVersion>` 是派生值。

**头/块头**：

- 旧 12 字节：`BLENDER` + `_`/`-`（4/8 字节指针）+ `v`/`V` + 3 位版本。
- 当前 17 字节：`BLENDER` + `17` + `-` + `01` + `v` + 4 位版本。
- `BHead4`：code/len/old(4)/SDNAnr/nr，20 字节。
- `SmallBHead8`：code/len/old(8)/SDNAnr/nr，24 字节。
- `LargeBHead8`：code/SDNAnr/old(8)/len(64)/nr(64)，32 字节。
- 旧文件可能以 8 字节 `ENDB` + 0 终止。

**DNA1**：`SDNA` + `NAME` + `TYPE` + `TLEN`（紧跟 TYPE，无 count）+ `STRC`，各段 4 字节对齐。
结构定义 = type_index + field_count + (field_type, field_name) 对。
显示 `NAME_COUNT/TYPE_COUNT/TLEN_COUNT/STRUCT_COUNT` 与 `NAME[n]/TYPE[n]/TLEN[n]/STRUCT[n]`，
不展开完整字段表；结构名用于块 `<SDNA_STRUCT>`；`SDNAnr` 是结构索引。

**已知块**：`GLOB` 按 DNA `FileGlobal` 偏移解析顶层字段，指针显示原文件地址（`0x...`），
不解析目标，`_pad*` 不展开；`TEST` 读 width/height 与 `<ThumbnailDataLength>`，
像素不解码；`ENDB` 结束块；其余 `DATA/WM/USER`/ID 块保持 `Unparsed`，只显示块头与
`<SDNA_STRUCT>` + Note。

**限制**：仅无压缩；ZSTD/Gzip 以压缩魔数开头，探测不匹配，按 Raw；不解析指针/链表/
数组内容/嵌套结构体/对象语义；`TLEN` 没有 count，长度等于 `TYPE_COUNT`；
SDNA 段有 4 字节对齐填充。

---

## 15. Protocol Buffers（无 .proto）

**规范**：Protocol Buffers Encoding（key/varint/wire type/group）；不加载 descriptor/schema。

**wire format**：

- key = `(field_number << 3) | wire_type`，varint。
- 0 varint：最多 10 字节；第 10 字节只能 `0x00`/`0x01`。
- 1 fixed64：8 字节 LE；5 fixed32：4 字节 LE；2 length-delimited：varint 长度 + payload。
- 3/4 start/end-group：嵌套字段直到同字段号 end-group；end-group key 属于 group 范围，
  不属于 group payload。
- field number 0 与 wire type 6/7 非法；停止该层，剩余成 `<unparsed data>`。

**命名**：parser `protobuf`，根 `Protocol Buffers`；树只显示 wire type 名
（`varint/fixed64/length-delimited/start-group/fixed32`）；标题
`VarintField '1'`/`LengthDelimitedField '2'`/`GroupField '6'` 等；原始键
`field_number/wire_type/key/value/length/end_group_field_number`；派生
`<PayloadKind>/<Text>/<HexPreview>/<FieldCount>/<PayloadLength>/<UnsignedValue>/<SignedValue>`。

**length-delimited 分类**：空 → `empty`；完整合法可打印 UTF-8（允许 tab/CR/LF，不允许 NUL/
其他控制）→ `string`；完整 payload 能严格按 wire format 解析 → `message`（递归建树）；
否则 `bytes`（前 32 字节 Hex）。分类只影响派生字段与是否建子节点，不改变范围/字节；
可打印但实际是二进制的数据会被显示为 string，这是无描述符的已知歧义。

**范围**：字段节点 = key + value（length-delimited 含长度 varint + payload；group 含
start/end key）；嵌套 message 为 `Container`，payload 必须被子节点覆盖；group 的 payload
排除 end key，无匹配 end-group 时延伸到父末尾并报 `TRUNCATED_PAYLOAD`；重复字段号不合并；
截断 length-delimited 为 `Unparsed` + 诊断；顶层每个 byte 必须落在字段或显式未解析节点内。

**探测**：无 magic；固定 512 KiB 窗口内完整合法 wire 结构：≥2 字段且有文本/嵌套证据 →
`Strong`；完整但证据弱，或窗口切断合法字段尾部 → `Weak`；否则 `NoMatch`。
`ProbePriority = 90`，低于全部 magic 格式；非法结构落 Raw。

**诊断**：`INVALID_FIELD_NUMBER`（0 或 >29 位）、`INVALID_WIRE_TYPE`（6/7）、
`INVALID_VINT`、`TRUNCATED_HEADER/PAYLOAD`、`SCHEMA_MISMATCH`（无 start 的 end-group）、
`LIMIT_STRING`/`INVALID_ENCODING`。

**已知坑**：不要为“树更漂亮”把任意 bytes 猜成 message；嵌套 message 必须整个 payload
严格解析；group 的 end key 不重复计入子节点；packed repeated/map/oneof/enum/默认值都需要
`.proto`；不要提高探测优先级去抢 magic 格式。

---

## 16. NBT（Named Binary Tag，Minecraft）

**规范**：minecraft.wiki NBT format；只支持**无压缩** NBT。

**命名**：parser `nbt`，根 `Named Binary Tag`；树只显示规范 tag 类型名
`End/Byte/Short/Int/Long/Float/Double/ByteArray/String/List/Compound/IntArray/LongArray`，
不显示名称/引号/索引/括号；右侧标题 `Compound 'root'`、`Int 'health'`、`Int '[0]'`；
原始字段 `type`（`1 (TAG_Byte)`）、`name`、`value`、`length`、`element_type`；
派生 `<Index>/<ElementType>/<HexPreview>/<PreviewValues>/<ChildCount>/<EndTagPresent>/<RawBits>`。

**结构**：全部多字节整数/浮点 big-endian；命名 tag = u8 type + u16 名称长度 + UTF-8 名称 +
payload；Byte/Short/Int/Long 为 1/2/4/8 字节有符号；Float/Double 为 IEEE-754；
ByteArray = i32 长度 + 字节；IntArray = i32 + i32 元素；LongArray = i32 + i64；
String = u16 长度 + UTF-8；List = u8 元素类型 + i32 元素数 + 无名称元素
（type 0 + count 0 为空列表）；Compound = 命名 tag 序列 + `TAG_End`
（End 属于范围但不是子节点）；根通常是 Compound，名称可为空。

**范围/树**：根是合成节点；其下是实际根 tag，之后字节为 `<trailing data>`；
命名 tag 范围含 type/名称/payload；compound 范围含最后的 End，但 payload 排除 End；
列表元素只覆盖 payload；大数组不逐元素建节点，只显示长度与有界预览；
截断/负长度 → `Unparsed` + 诊断；compound 缺 End → 保留子节点、范围到父末尾 +
`TRUNCATED_CHILD`。

**探测**：无 magic；要求根 tag 为 Compound；512 KiB 窗口内完整覆盖文件 → `Strong`；
窗口在 tag 中间结束或根后有 trailing → `Weak`；gzip/zlib 首字节、MCA 区域表、
非 compound 根或非法结构 → `NoMatch`；`ProbePriority = 95`，压缩 NBT/MCA 落 Raw。

**诊断**：`INVALID_TAG_TYPE`、`INVALID_LENGTH`（负长度）、`TRUNCATED_HEADER/PAYLOAD`、
`TRUNCATED_CHILD`、`INVALID_ENCODING`/`LIMIT_STRING`、`TRAILING_DATA`。

**已知坑**：历史上可能用 Java modified UTF-8，本实现按标准 UTF-8；`TAG_End` 不是独立节点；
列表元素没有名称；数组长度是 i32，负值必须诊断；不要把 gzip/zlib NBT 当无压缩 NBT；
不要把 MCA 的 location/timestamp 表当 NBT。

---

## 17. RawData

- 无规范；未知格式/损坏文件兜底，不抛异常。
- 根节点下单个 `Data` 节点，覆盖整个文件；仍遵守惰性读取，不整块进内存。
- 报告标明“未识别格式”。

---

## 18. 内嵌 ID3 / EXIF / ICC

不是顶层探测目标，由容器在对应位置调用。

**ID3v2**：入口 RIFF `id3 `（WAVE）；支持 v2.2/2.3/2.4、tag header/extended header/footer/
frames（`frame[n].`）；synchsafe 尺寸、unsynchronisation、帧编码；压缩/加密帧只保留 payload；
v2.4 优先按 synchsafe，合法帧边界时才接受普通 BE；CHAP/CTOC 子帧复用帧循环；
APIC 只标注类型与长度；文本保留声明编码。

**EXIF**：入口 JPEG APP1（跳过 `Exif\0\0`）、PNG `eXIf`（直接 TIFF header）、
PSD 1058/1059（数据以 TIFF header 开始时）；结构 TIFF header → IFD0 → IFD1 链、
Exif/GPS/Interop 子 IFD；offset 相对 TIFF header；越界/循环 IFD/大字符串有保护；
字段名用官方标签名（`IFD0.Make`、`ByteOrder`、`TIFFMagic`、`IFDOffset`）；
MakerNote 只识别签名并标注未解析；缩略图只做范围检查；GPS 生成 `Computed.GPSPosition`。

**ICC**：入口 PNG `iCCP`（zlib 解压后）、JPEG APP2 单段、PSD 1039；128 字节 header →
tag count(u32) → tag table（12 字节项：signature/offset/size）→ tag payload；
字段用规范原名（`Profile size`、`Preferred CMM type`、`Profile version`、`Profile/device class`、
`Data colour space`、`Profile connection space (PCS)`、`Rendering intent`、`Profile ID` 等）；
tag 数据用 `Tag[n].`；常见 tag type `XYZ /curv/para/text/desc/mluc/sig /sf32/ui32/ui16/
ui08/dtim/meas/view/chrm` 解析为字段；LUT 类只解析通道数、矩阵、网格声明与 CLUT precision；
未知 tag type 只给 Note；不做颜色变换/跨 tag 一致性；JPEG 多段不拼接。

**限制**：IFD 深度 6、总数 32、链长 8 防循环；ICC tag 条目/数组/LUT 声明受
`MaxDataLines`；tag 范围受 profile size 约束。

---

## 19. 格式状态

| 格式 | 状态 | 备注 |
|---|---|---|
| Raw | 稳定 | 兜底 |
| RIFF | 稳定 | `LIST:movi` 整体不展开 |
| EBML | 稳定 | 重点测试 unknown size |
| FLV | 稳定 | Enhanced audio 未展开；Multitrack/AMF3 已支持 |
| ASF | 稳定 | Data Packets 不切分；Index Blocks 未展开 |
| FBX | 稳定 | 32/64 位记录；deflate 数组只记长度；footer framing 可选展开 |
| PNG | 稳定 | CRC 按需（含 IDAT/fdAT）；`eXIf`、`iCCP`、`cICP`、`mDCV`/`cLLI` |
| JPEG | 稳定 | APPn 按 Identifier；Exif/XMP/8BIM/单段 ICC |
| ISOBMFF | 稳定 | 最大；ASC/SPS/PPS/FLAC/fdel/alis 已补全 |
| MIDI | 稳定 | `MThd`/`MTrk`、逐事件、running status/SysEx/meta |
| PSD/PSB | **不稳定** | 私有 descriptor/adjustment/layer effect 可能只显示原始范围 |
| Ogg | 稳定 | Page 级树；codec header；setup header 与音视频 packet 不解码 |
| FLAC | 稳定 | metadata block 树；音频帧整体 `Unparsed` |
| Blender | 稳定（无压缩） | 块序列 + SDNA；其余内存块 `Unparsed`；ZSTD/Gzip 按 Raw |
| Protobuf | 稳定（启发式） | 无 `.proto`；文本/嵌套消息/字节分类是显示启发式 |
| NBT | 稳定（无压缩） | 12 种 tag；压缩 NBT/MCA 按 Raw |
| ID3/EXIF/ICC | 稳定 | 内嵌解析器，不做顶层探测 |
