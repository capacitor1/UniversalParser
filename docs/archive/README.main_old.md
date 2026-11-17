# UniversalParser

只读的二进制结构解析工作台（WinForms + .NET 10）：把容器/分块型文件切成结构树，
展示字段、Hex 与解析报告。**v1.0.0 已发布。**

> 当前状态：Phase 0–7 完成，Phase 8 发布完成；已知限制见 `docs/01-open-issues.md`。

---

## 1. 运行

```text
dist/
├─ UniversalParser.exe                    Windows x64，单文件，framework-dependent
└─ universalparser.limits.example.json    可选限制配置示例（无注释 JSON）
```

需要安装 **.NET 10 Desktop Runtime (x64)** 后运行 `UniversalParser.exe`。
该发布为 framework-dependent 单文件，不包含运行时。

可选配置：把示例复制为 exe 旁的 `universalparser.limits.json`，只保留要覆盖的键：

```json
{
  "maxStringBytes": 4194304,
  "maxBlobBytes": 268435456,
  "maxDataLines": 10000
}
```

缺失键使用内置默认值；损坏的 JSON 会回退默认值并在状态栏提示。
探测窗口与长时间读取的 512 KiB 阈值不可配置。

---

## 2. 构建与测试

```powershell
dotnet build UniversalParser.sln
dotnet test UniversalParser.sln
dotnet format UniversalParser.sln --verify-no-changes
```

当前：`dotnet build` 0 warning / 0 error；`dotnet test` **117 passed**。

发布：

```powershell
dotnet publish src/UniversalParser.App/UniversalParser.App.csproj `
  -c Release -r win-x64 -p:SelfContained=false `
  -p:PublishSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false -o dist
Remove-Item dist/*.xml -Force
Copy-Item universalparser.limits.example.json dist/
```

---

## 3. 仓库内容

- `src/UniversalParser.Core`：只读 source、64 位 range、不可变节点、字段/诊断、
  `ParseLimits` + JSON 覆盖、registry/probe、`ParseSession`、Hex/报告导出、树校验、统计。
  格式：ISOBMFF、RIFF、EBML、FLV、ASF、FBX、PNG/APNG、JPEG、MIDI、PSD/PSB、Ogg、
  FLAC、Blender（无压缩）、Protocol Buffers、Minecraft NBT（无压缩）、Raw；
  内嵌 EXIF/ID3v2/ICC/8BIM。
- `src/UniversalParser.App`：旧版布局/字体、树懒加载、虚拟字段列表、64 位 Hex、
  Statistics、`Settings`、菜单快捷键、`Assets` PNG 树图标。
- `src/UniversalParser.Tests`：框架层与部分格式回归；只使用内存构造样本。
- `fixtures/`：仅临时真实样本，验证后删除；自动化测试不依赖。
- `docs/`：当前规范、状态与格式笔记；`docs/archive/` 保存完整历史。

---

## 4. 文档

| 文件 | 作用 |
|---|---|
| `docs/00-context-recovery.md` | 上下文丢失后的快速恢复 |
| `docs/01-open-issues.md` | 剩余限制、已接受风险、明确不做 |
| `docs/02-product-decisions.md` | 决策登记表与当前生效规则（最高优先级） |
| `docs/03-style-guide.md` | 字段、命名、显示、导出风格 |
| `docs/04-architecture.md` | 分层、契约、硬性不变量 |
| `docs/05-implementation-history.md` | 阶段历史、格式覆盖矩阵 |
| `docs/07-quality-checklist.md` | 发布与验收检查表 |
| `docs/08-format-notes.md` | 各格式规范来源、范围语义、已知坑 |
| `docs/archive/` | 完整决策、格式笔记、Phase 6 收尾与 v1.0.0 发布记录 |

**冲突处理优先级：**

```text
用户最新明确指令
  > docs/02-product-decisions.md
  > docs/03-style-guide.md
  > docs/04-architecture.md
  > 现有实现
```

文档之间冲突时先改文档，再改代码；不要用旧实现“事实上”覆盖新决策。

---

## 5. 八条不可违背的决策

1. **导出不兼容旧版文本。** 导出以故障分析为目标。
2. **节点长度计入 padding。** 不应存在看不见的空洞；例外必须记录。
3. **RawData 惰性读取。** 不整块加载；Hex 按范围读取。
4. **支持超大文件。** 64 位 offset/length；Hex、列表、导出流式/分页。
5. **原始数据优先，官方命名优先。** 原始值在前，解释在后；尖括号只用于派生值、
   关键 Note、解析器错误。
6. **只做原始解析。** 不解码/校验/转封装 payload；不做播放器/查看器。
   诊断只管格式本身（D-028）。
7. **GUI 全英文；文件内容语言按原编码保留。**
8. **读取内存限制。** `ParseLimits` 是唯一默认来源；可执行文件旁的
   `universalparser.limits.json` 可部分覆盖；树深度/节点数/时长不设限（D-032）。
   报告无总预算，Hex 固定首尾各 128 KiB（D-034）。

---

## 6. 当前不做

- 字段搜索/过滤、JSON 报告、SHA-256、fuzz、旧版导出兼容（D-021/D-022）。
- payload 解码、播放、转封装、文件修复、完整语义校验（D-006/D-028）。
- 无 schema 的 protobuf/NBT 业务字段猜测。
- 已归档的旧代码、旧测试与临时样本不再恢复。

---

## 7. 版本

| 版本 | 日期 | 内容 |
|---|---|---|
| 1.0.0 | 2026-11-18 | Phase 8 收尾；文档压缩/归档；单文件 framework-dependent 发布到 `dist/` |
