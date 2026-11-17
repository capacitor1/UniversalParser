# 03 — 全局风格指南

> 定义解析结果、字段、节点、诊断、Hex、导出与 GUI 的统一风格。
> 与 `02-product-decisions.md` 冲突时以 `02` 为准。

---

## 1. 总原则

```text
原始优先
官方命名优先
解释不覆盖原始值
诊断不伪装成数据
显示层不篡改解析层
```

读者必须能区分：文件里的原始字段、解析器算出的派生值、解析器的 Note/Warning/Error。

---

## 2. 字段命名

### 2.1 原始字段

字段名来自官方规范并保持官方写法：

| 规范写法 | 使用 |
|---|---|
| `major_brand` | `major_brand` |
| `DataOffset` | `DataOffset` |
| `sample_description_index` | `sample_description_index` |
| `Bit depth` | `Bit depth`（prose label 也保留） |

禁止：翻译、统一大小写、把 prose label 改成 `BitDepth`、给字段名加单位
（`DurationMs` 不是官方名）、用 UI 友好名替代官方名。

规范同时给出代码标识符和 prose label 时，键用代码标识符；只有 prose label 时保留原写法。
规范没有稳定标识符时用描述性英文键，并在 `08-format-notes.md` 标注“本工具命名”。

### 2.2 官方未命名的原始字段

使用描述性英文键，**不加尖括号**，例如 `unknown_flag_0 = 01`。

### 2.3 派生字段

payload length、枚举含义、计算出的时间/尺寸/校验结果等属于派生值。
派生值可以用尖括号，但优先行内解释。

---

## 3. 值的显示

### 3.1 默认：原始值

```text
official_name = raw_value
```

保留规范进制/格式；不擅自加单位、换算、四舍五入或补前导零（规范要求固定宽度除外）。

### 3.2 短解释：行内括号

```text
data_flag_example = 0F (C1|C2|H1)
```

原始值在前，空格 + `(` + 英文解释 + `)`；括号内不重复原始值、不塞多行文本。

### 3.3 长解释：独立尖括号条目

```text
data_flag_example = 0F
<DecodedMeaning> = data_flag_example: C1 | C2 | H1 | ...
```

原始字段保留；尖括号条目紧随其后，并写明解释的是哪个原始字段。

### 3.4 尖括号允许范围

只允许：

1. 非原始派生值：`<PayloadLength>`、`<DecodedMeaning>`、`<ComputedDuration>`
2. 关键短 Note：`<Note>`
3. 解析器错误：`<Error>`、`<Message>`、`<ErrorCode>`

禁止：原始字段加尖括号、官方未命名原始字段加尖括号、把普通解释都塞进 `<Note>`、
用 `<Warning>` 代替结构化诊断。

### 3.5 payload length

规范已有 `size`/`length` 时先展示它，不要用 `<PayloadLength>` 取代：

```text
size = 1234
<PayloadLength> = 1234
<Note> = Payload is not parsed.
```

### 3.6 小体量压缩数据解码（D-011）

只有 PNG `iCCP`、`zTXt`、压缩 `iTXt` 允许有界解码：

- 原始压缩长度保留（`<CompressedTextLength>`、`<CompressedProfileLength>`）。
- 解压结果是非原始值，用尖括号（`<Text>`、`<TextByteLength>`、`<DecompressedProfileLength>`）。
- 解压失败只报 `INVALID_ENCODING`，不显示猜测内容。
- 超过 `MaxStringBytes`/`MaxBlobBytes` 只显示前缀并报 `LIMIT_STRING`/`LIMIT_BLOB`。
- `IDAT`/`fdAT`/FBX 数组/音视频帧仍只显示长度与 Note。

---

## 4. 字段顺序

```text
1. 原始字段（按规范/结构顺序）
2. 行内解释（属于原始值）
3. 派生字段
4. Note
5. Warning
6. Error
```

带尖括号的解释值必须紧跟对应原始值。无依赖的派生值不插在原始字段中间；
错误只有在导致后续字段不可读时才能提前；Note 不得混入普通字段而不加标记。

---

## 5. 节点命名

每个节点有两个名字：

- **标题（Title）**：右侧详情面板，`(OptionalMarker)NoSpaceTitle 'RawID'`。
- **树节点名（Name）**：左侧树，使用 `ProcessedTagId`（最接近的已处理语义名）。

### 5.1 标题

```text
FileType 'ftyp'
(QuickTime)Title '©nam'
ImageHeader 'IHDR'
StartOfImage '\xFF\xD8'
Segment '0x18538067'
FilePropertiesObject '{75B22630-668E-11CF-A6D9-00AA0062CE6C}'
```

- `OptionalMarker` 只用于私有子项（如 `(QuickTime)`）。
- `NoSpaceTitle` 首字母大写、无空格。
- `RawID` 与文件字节一致：可打印单字节原样（含 4CC 尾空格 `'fmt '`）、
  不可打印用 `\xNN`、GUID 用大括号、部分 varint 用 `0x...`。
- 单引号内不转换大小写、不翻译、不补空格。
- 不追加 `[truncated]` 等状态；状态由 Kind/诊断表达。

### 5.2 树节点名

- 只显示 `ProcessedTagId`，不显示 raw ID。
- 根节点显示格式全称（D-014）。
- 4CC 格式显示 4CC：`IDAT`、`RIFF:WAVE`、`LIST:INFO`。
- 有语义名的格式显示语义名：`EBMLVersion`、`HeaderObject`、`VideoTag`。
- JPEG 显示 mnemonic：`SOI`、`APP0`；FBX 显示节点名。
- Protobuf 只显示 wire type 名（`varint`、`fixed64`、`length-delimited`、`start-group`、
  `fixed32`）；NBT 只显示 tag 类型名（`Compound`、`Byte`、`List` 等）。
  字段号、tag 名称、索引、payload 类型放在右侧解析视图。

### 5.3 合成节点

```text
<unparsed data>
<trailing data>
<padding>
<zero fill>
```

### 5.4 未知节点

- 已知是未知块：`UnknownBox 'xxxx'` / `UnknownChunk 'xxxx'`
- 完全未解析：`<unparsed data>`
- 不用 `[Parser Error]` 当节点名；错误用诊断表达。

### 5.5 不重复身份字段

块类型/ID/GUID/marker 已在标题中出现，字段区不再重复同一个身份值：

- PNG 不输出 `Chunk Type`；FLV 不输出 `Signature`；ASF 不输出 `Object GUID`；
- JPEG 不输出 `Marker`/`<MarkerName>`；FBX 不输出 `Name`；EBML 不输出 `<ElementID>`。

原始 `size`/`length`/version 等结构字段不受影响，仍需保留。

### 5.6 JPEG APPn

- APP0–APP15 的内容由 payload 开头的 NUL 终止 `Identifier` 决定，不由 APP 序号决定。
- 顺序：`Identifier`、`<ApplicationFormat>`、内容字段或 `<UnparsedLength>`。
- `Exif`、标准 XMP、`Photoshop 3.0`、单段 `ICC_PROFILE` 注册 handler；其余为 `Unparsed`。
- XMP 返回完整原文，不展开 XML/RDF、不修剪 BOM/空白、不验证 XML；只有导出报告转义。
- 8BIM 资源列表用 `Resource[n].`；未知资源只显示 framing + 未解释长度。
- 未注册 Identifier 不产生 Note/Warning；无 NUL 终止符只给一条结构性 Note。
- `Adobe` 是固定 5 字节签名，后续两字节 version 不得被当作 NUL 终止符。

### 5.7 百分比与统计

- 百分比只经 `ValueFormat.Percentage`，小数位来自进程内设置（默认 3）。
- 同一份数据在字段、饼图、导出中使用同一小数位。
- 统计视图只保留功能性说明，不写装饰性文案。

### 5.8 Protocol Buffers（无描述符）

- 字段身份是原始 `field_number` + `wire_type`；不存在官方字段名或消息类型名。
- TreeView 只显示 wire type 规范名。
- 标题：`VarintField '1'`、`LengthDelimitedField '2'`、`GroupField '6'` 等；
  单引号内是原始字段号。
- 原始键：`field_number`、`wire_type`（`2 (length-delimited)`）、`key`（`0x08`）、`value`、
  `length`、`end_group_field_number`。
- 派生：`<PayloadKind>`（string/message/bytes/empty）、`<Text>`、`<HexPreview>`、
  `<FieldCount>`、`<PayloadLength>`、`<UnsignedValue>`、`<SignedValue>`、`<Note>`。
- `<PayloadKind>` 是显示启发式，不是 schema；禁止输出猜测的字段名、枚举名或业务类型。

### 5.9 NBT（Named Binary Tag）

- TreeView 只显示规范类型名：`Compound`、`Byte`、`Short`、`Int`、`Long`、`Float`、
  `Double`、`ByteArray`、`String`、`List`、`IntArray`、`LongArray`。
- 名称/索引在右侧标题：`Compound 'root'`、`Int 'health'`、`Int '[0]'`、`Compound '[1]'`。
- 命名 tag 原始字段：`type`（`1 (TAG_Byte)`）、`name`、`value`；列表用 `element_type`、
  `length`；数组用 `length`。
- 派生：`<Index>`、`<ElementType>`、`<HexPreview>`、`<PreviewValues>`、`<ChildCount>`、
  `<EndTagPresent>`、`<RawBits>`。
- 大数组不逐元素建节点；列表元素逐个建节点。

---

## 6. 诊断

### 6.1 模型

- severity：`Note` / `Warning` / `Error`
- code：稳定英文码，如 `TRUNCATED_PAYLOAD`
- message：英文短句
- range：相关 source range
- detail：可选长文本

### 6.2 显示

```text
<Note> = ...
<Warning> = ...
<Error> = Truncated payload
<Message> = ...
<ErrorCode> = TRUNCATED_PAYLOAD
```

异常堆栈只进日志/调试输出；错误不改变 RawData 范围。

### 6.3 截断

- 节点范围是实际可读范围；
- 有 `Warning`/`Error` 与 `TRUNCATED_*` 码；
- 不把节点名改成 `xxx [truncated]`。

---

## 7. 数值与文本

- 原始值按规范进制；派生值用括号或独立条目；格式化使用 `InvariantCulture`。
- 不加千分位（报告可读性区域除外）；浮点保留规范精度；64 位值不用 `int` 格式化。
- 文本使用格式声明的编码，保留原始语言，不强制 ASCII，不把中文替换成 `?`。
- 控制字符：字段值用 `\0`、`\n`、`\t`、`\xNN`；Hex ASCII 区用 `.`。
- 文本受 `MaxStringBytes` 限制；超限截断并诊断。
- 时间：原始字段优先；派生人类可读时间放括号/独立条目；时区/纪元明确；不擅自本地化。

---

## 8. RawData 与 Hex

### 8.1 RawData

```text
RawData = 当前节点的完整编码范围，包含 header
```

例外：JPEG `SCAN` 的 RawData 是熵编码数据，不含 marker，必须在格式笔记记录。
不得把异常文本、padding 或其他节点数据当 RawData。

### 8.2 Hex

- 16 字节/行；offset 8 位（≤4 GB）或 16 位（>4 GB）；hex 大写；ASCII 32..126，其余 `.`。
- 只读；支持复制行/选区；按 source range 读取，不持有 `Stream`。
- 第 `n` 行最多读取 `length - n*16` 字节，不越界读下一个节点。
- 表头、文字、选区、鼠标命中共用 `HexDumpFormatter` 的列计算；不使用平均字符宽度估算。
- 字节跨度 → 列跨度统一走 `GetColumnSpan`。
- 字体/DPI 变化后重新测量列坐标。

### 8.3 大节点

不预加载整个节点；只渲染可见行；行号/偏移 64 位；滚动条用分页/映射，不绑定 `int.MaxValue`。

---

## 9. 导出报告

### 9.1 目标

报告是**故障分析文档**，不是 UI 截图，也不是旧版兼容文本。

### 9.2 结构

```text
UniversalParser Forensic Report
================================
1. Summary        File / Size / Detected format / Parser / Parse status / Node path + range
2. Diagnostics    Severity / Code / Message / Range / Context
3. Structure      Node tree with ranges; truncated/unparsed/padding markers
4. Fields         Raw fields → inline explanations → derived → Note/Warning/Error
5. Hex            Range / Offset / Hex + ASCII
6. Read Limits    Limit values and whether any limit was hit
```

### 9.3 完整性

- Fields 始终完整写入，不按行数/字节数截断。
- Structure 完整写入。
- Hex 是唯一二进制窗口：`<= 256 KiB` 完整；`> 256 KiB` 只写首 128 KiB + 尾 128 KiB，
  中间以独立说明行分隔，并提示 `Export Raw`。
- 没有 `MaxExportBytes`，也没有对应 JSON 配置项。
- 流式写入、可取消、有进度；文件名 sanitize；控制字符转义。

### 9.4 格式

UTF-8 纯文本，缩进/键值/段落，不用 tab 作为唯一分隔符；换行 → `\n`、tab → `\t`、
控制字符 → `\xNN`。不要求旧版逐字节兼容、固定列宽或包含整文件 Hex。

---

## 10. GUI 文案

- 全英文；文件内容语言保留；错误摘要简短，堆栈只进日志。
- 不显示调试信息（加载耗时、控件高度等）。
- 根节点固定字段见 D-025；创建时间用 UTC；属性显示名称与原始 flags。
- 字段列表区分原始/派生/Note/Warning/Error；长值提供 tooltip/复制。
- 统计视图只写功能性说明；百分比精度由 `Settings` 决定。
- 菜单快捷键与树图标固定映射见 D-027；字段搜索/过滤明确不做（D-021）。

---

## 11. 反例清单

```text
<DataOffset> = 0x10              // 原始字段加尖括号
data_offset = 0x10               // 官方是 DataOffset 却改成 snake_case
DurationMs = 1000                // 原始字段名被加单位
Width = 1920 px                  // 原始值被加单位
<Note> = every field explanation // Note 滥用
[Parser Error]                   // 用节点名表达错误
RawData = exception text         // 伪造 RawData
DataLength = 1234                // 取代官方 size，且不保留原始字段
```

正确方向：

```text
DataOffset = 0x10
size = 1234
<PayloadLength> = 1234
data_flag_example = 0F (C1|C2|H1)
<Error> = Truncated payload
<ErrorCode> = TRUNCATED_PAYLOAD
```
