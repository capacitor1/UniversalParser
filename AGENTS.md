# AGENTS.md — UniversalParser

本文件是给后续 AI/开发者看的操作约束。**先读文档，再动代码。**

---

## 1. 当前阶段

**v1.0.1 已发布（Phase 8 + issue#1 维护）。** Phase 0–7 已完成，发布物在 `dist/`：
`UniversalParser_1.0.1.exe`（win-x64、单文件、framework-dependent，依赖 .NET 10 Desktop Runtime）+
无注释的 limits 示例。

当前允许：

- 缺陷修复、字段/命名打磨、真实样本暴露的边界修正。
- 文档维护与压缩；把已完成内容归档到 `docs/archive/`。
- 明确任务驱动的新格式/新能力；必须先更新 `02-product-decisions.md` 与 `01-open-issues.md`。
- 重新构建、测试与发布。

当前仍禁止：

- 在 GUI 中使用 `Application.DoEvents()` 或关闭跨线程检查。
- 为追求“完整规范”无限展开冷门 box/codec；未知块保持显式 `Unparsed` + raw 前缀。
- 恢复已删除的旧代码、旧格式测试或临时样本。
- 基于旧 `Node` / `ParseResult` / `IParser` 打补丁。
- 未经任务确认自动解禁 `01-open-issues.md` 中的暂缓项。

---

## 2. 每次开始工作前必须读

按顺序：

1. `docs/00-context-recovery.md`
2. `docs/01-open-issues.md`
3. `docs/02-product-decisions.md`
4. `docs/03-style-guide.md`
5. `docs/04-architecture.md`
6. `docs/05-implementation-history.md`
7. `docs/07-quality-checklist.md`
8. 与当前任务相关的 `docs/08-format-notes.md`

需要完整历史理由、验收细节或被压缩掉的实现记录时，再查 `docs/archive/`。
如果上下文被压缩，不要凭记忆继续；先读上述文档。

---

## 3. 不可违背的规则

### 产品

- 这是**原始解析器**，不是播放器、转码器、校验器或 payload 查看器。
- 只按格式规范切分容器/块，提取字段，展示原始值和必要的人类可读解释。
- 诊断只针对格式本身；语义错误与跨块/跨字段一致性不检查（D-028）。
- 默认不实现 payload 解码、转封装、播放、渲染、完整校验；D-011 仅允许小体量、
  简单压缩/变换的元数据/文本有界解码（PNG `iCCP`/`zTXt`/压缩 `iTXt`）。
- 导出报告服务于**故障分析**，不要求兼容旧版 TXT。
- GUI 树图标只用 `src/UniversalParser.App/Assets` 的 PNG（嵌入资源，D-027），
  不在代码里画图标；每个菜单命令必须有快捷键并在菜单中显示（D-027）。

### 字段与显示

- 原始字段名必须遵循官方规范：
  - 官方是 snake_case 就 snake_case
  - 官方是 camelCase 就 camelCase
  - 官方是 PascalCase 就 PascalCase
  - 官方是 prose label（如 `Bit depth`）就保留原写法
  - 不翻译、不擅自统一大小写
- 官方没有明确命名的原始字段：使用描述性英文键，**不加尖括号**。
- 原始值优先：`official_name = raw_value`；短解释用 `raw_value (human)`；
  长解释另起尖括号条目。
- 尖括号只允许用于：非原始派生值、极度重要的短 Note、解析器内部错误。
- 原始字段本身绝不允许加尖括号。
- 规范已有原始 `size`/`length` 时先展示它；不要用 `<PayloadLength>` 取代。

### 语言

- GUI 标签、菜单、状态、错误摘要：**全英文**。
- 文件内容中的中文/其他语言：按文件声明的编码原样保留，不翻译、不强制 ASCII。

### 大文件

- 所有 offset/length 使用 64 位；禁止未经检查的 `(int)`/`(uint)` 转换。
- 禁止把整个节点或整个文件读入内存；Hex、列表、导出必须流式/分页/惰性。
- 任何循环读取都必须有长度上限或取消检查。
- 所有读取内存限制必须来自 `ParseLimits` / `ParseContext.Limits`，不得在格式内另建
  不可配置的私有上限；默认值见 D-008。报告导出无总预算；Hex 固定首尾各 128 KiB（D-034）。
- 不得重新引入解析深度、子节点数、总节点数或解析时长限制（D-032）。
- 探测/长时间读取的 512 KiB 阈值固定，不可配置（D-037）。

### 资源与错误

- 解析失败必须返回**部分结果 + 诊断**，不能把异常文本塞进 RawData。
- 单节点解析失败不能导致 GUI 显示旧结果。
- 关闭文件必须取消解析、释放 source、清理结果、清空缓存。
- 不允许使用 `Application.DoEvents()`；不允许关闭 `CheckForIllegalCrossThreadCalls`。
- 所有后台操作必须支持取消。

### 范围

- 默认：子节点必须完全位于父节点范围内，兄弟节点不重叠。
- 默认：padding 计入节点长度；未覆盖区间显式表示，不能有“看不见的空洞”。
- 特殊格式允许忽略部分框架数据（如 FBX footer），但必须在 `08-format-notes.md`
  记录，并尽量用显式合成节点表示。

---

## 4. 代码风格

- 现代 C# / .NET 稳定语法；可空引用类型开启；不要用 `!` 掩盖契约缺失。
- 一个文件一个主要类型；命名空间与目录一致。
- 解析核心不依赖 WinForms。
- 优先使用 `ReadOnlySpan<byte>`、`Memory<byte>`、`System.Buffers`。
- 避免重复基础设施：I/O、进度、字节格式化、字符串清洗各只有一份。
- 注释解释“为什么”，不解释显而易见的“是什么”。
- 不写超长单行；不滥用空行；保持可读性。

---

## 5. 测试要求

当前保留：source/range/reader、node/field/diagnostic、engine/registry/session、树校验、
导出/limits，以及 PSD/PSB、Ogg、FLAC、Blender、Protobuf、NBT 与 PNG CRC 回归。

- 当前 `dotnet test`：**170 passed**。
- 不恢复旧的按格式解析测试或 GUI 测试；需要时针对当前代码重写。
- 新测试必须覆盖真实行为，不能为通过而放宽断言。
- 框架层改动至少运行 `dotnet build` 与 `dotnet test`；GUI/大文件行为用人工验证补充。
- 发布前额外运行 `dotnet format --verify-no-changes` 并验证 exe 启动。

---

## 6. 文档维护

- 当前决策/规则：更新 `docs/02-product-decisions.md`（只保留生效规则）。
- 当前风格：`docs/03-style-guide.md`；架构：`docs/04-architecture.md`。
- 当前限制/暂缓项：`docs/01-open-issues.md`；格式坑：`docs/08-format-notes.md`。
- 阶段与发布历史：`docs/05-implementation-history.md`。
- 已完成决策的完整理由、验收、格式笔记全文、历史关闭记录：放 `docs/archive/`，只追加。
- 每次重要变更，在 `docs/00-context-recovery.md` 的“最近变更”追加一行。
- 文档只写当前有效信息；旧样本路径、旧测试名、旧实现文件列表不留在主文档。

**不要把重要信息只留在聊天里。**

---

## 7. 测试与样本规则

- **写完代码后必须主动进行基础测试。** 至少运行 `dotnet build` 和 `dotnet test`。
- 优先构造最小样本；也可以请用户提供真实样本。构造/转换可用 Python、`.cmd`/`.ps1`
  或 `ffmpeg`/`ffprobe`。
- 冷门块/box 名称、字段名或规范细节不要猜；先查官方规范，并把结论记录到 `08`。
- 同一个块/box 连续解析失败两次以上，必须查规范或对照真实样本，不能继续盲改。
- 测试样本放入 `fixtures/`，记录来源、生成命令、预期结果；验证后按用户要求删除。
- 自动化测试不得依赖 `fixtures/`；不提交来源不明的大文件。

---

## 8. 发布

```powershell
dotnet publish src/UniversalParser.App/UniversalParser.App.csproj `
  -c Release -r win-x64 -p:SelfContained=false `
  -p:PublishSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false -o dist
Remove-Item dist/*.xml -Force
Rename-Item dist/UniversalParser.exe UniversalParser_1.0.1.exe
Copy-Item universalparser.limits.example.json dist/
```

- `dist/` 只保留 `UniversalParser_<version>.exe` 与 `universalparser.limits.example.json`。
- 示例 JSON 不带注释；不打包、不做安装器、不签名。
- 发布记录写入 `docs/archive/release-<version>.md`，更新 `00` 的状态与最近变更。
