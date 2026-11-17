# 00 — 上下文恢复摘要

> 上下文丢失后先读这一页。最后更新：2026-11-18（v1.0.0）。

---

## 1. 项目是什么

UniversalParser 是一个 Windows WinForms 工具：把容器/分块型文件按二进制结构切成树，展示结构树、
节点字段与当前节点 Hex，并可导出解析报告或原始范围。

支持的格式：

```text
ISOBMFF (mp4/mov/3gp/m4a/heic/avif)  RIFF (wav/avi/webp/ani/dls/wwise)  EBML (mkv/webm)
FLV  ASF  FBX (binary)  PNG/APNG  JPEG  MIDI  PSD/PSB  Ogg (Vorbis/Opus/Theora/Speex/FLAC)
FLAC  Blender .blend (uncompressed)  Protocol Buffers  Minecraft NBT (uncompressed)  Raw
```

**产品边界：逐块原始解析；诊断只管格式本身，不检查语义内容或跨块关系（D-028）。**
不解码/校验/转封装实际 payload；只有小体量、简单压缩的元数据/文本可按 D-011 有界解码。

---

## 2. 八条不可违背的决策

1. 导出不兼容旧版文本；报告用于故障分析。
2. padding 计入节点长度；未覆盖区间必须显式表示。
3. RawData 惰性读取；Hex 按范围读取。
4. 支持超大文件：64 位 offset/length，流式/分页。
5. 原始数据优先、官方命名优先；尖括号只用于派生值/关键 Note/错误。
6. 只做原始解析，不做播放器/查看器/payload 解码。
7. GUI 全英文；文件内容语言按原编码保留。
8. 读取内存限制：`ParseLimits` 是唯一默认来源，可用可执行文件旁的 JSON 覆盖；
   树深度/节点数/时长不设限（D-032）。报告无总字节预算，Hex 固定首尾 128 KiB（D-034）。

完整规则见 `02-product-decisions.md`，详细理由与验收记录在
`archive/02-product-decisions-full.md`。

---

## 3. 六个硬性不变量

```text
1. 所有读取都通过只读随机访问源，不共享可变 Position。
2. 子节点必须在父节点范围内；兄弟节点不重叠；未覆盖区间显式表示。
3. 解析失败返回部分结果 + 结构化诊断，不伪造 RawData。
4. 所有长度/偏移/行号使用 64 位，禁止无检查的 int 截断。
5. 解析不设深度/节点数/时间限制；字符串/Blob/表格/导出受内存上限保护；支持取消。
6. GUI 只消费结果与 source range，不持有 parser 的流。
```

---

## 4. 当前状态（v1.0.0）

- Phase 0–7 完成；Phase 8 文档收尾、归档与发布已完成。
- 发布物：`dist/UniversalParser.exe`（win-x64，单文件，framework-dependent）+
  `universalparser.limits.example.json`（无注释示例）。需要安装 .NET 10 Desktop Runtime (x64)。
- `dotnet build` 0 warning / 0 error；`dotnet test` **117 passed**（仅框架层与部分格式回归）；
  `dotnet format --verify-no-changes` 通过。
- 测试只用内存构造样本；`fixtures/` 只放临时真实样本，验证后删除。
- 格式状态：除 PSD/PSB 为**不稳定**外，其余格式均为稳定原始解析。
- 已知限制与暂缓项见 `01-open-issues.md`；不再自动扩展范围。

---

## 5. 文档地图

| 顺序 | 文件 | 作用 |
|---:|---|---|
| 1 | `00-context-recovery.md` | 本页：快速恢复 |
| 2 | `01-open-issues.md` | 剩余限制、已接受风险、明确不做 |
| 3 | `02-product-decisions.md` | 决策登记表与当前生效规则（最高优先级） |
| 4 | `03-style-guide.md` | 字段、命名、显示、导出风格 |
| 5 | `04-architecture.md` | 分层、契约、硬性不变量 |
| 6 | `05-implementation-history.md` | 阶段历史、格式覆盖矩阵 |
| 7 | `07-quality-checklist.md` | 发布与验收检查表 |
| 8 | `08-format-notes.md` | 各格式规范来源、范围语义、已知坑 |
| — | `archive/` | 已完成决策全文、格式笔记全文、Phase 6 收尾记录、v1.0.0 发布记录 |

**冲突处理优先级：**

```text
用户最新明确指令
  > docs/02-product-decisions.md
  > docs/03-style-guide.md
  > docs/04-architecture.md
  > 现有实现
```

---

## 6. 阶段计划

```text
Phase 0~4  文档/决策、Core、RIFF、EBML/FLV/ASF/FBX、PNG/JPEG/ISOBMFF（已完成）
Phase 5    GUI 重写（已完成）
Phase 6    BUG 修复、性能优化、细节微调（已完成）
Phase 7    细节提升与文件支持性完善（已完成：D-034 ~ D-043）
Phase 8    文档收尾、归档与 v1.0.0 发布（已完成；旧代码不在本仓库）
```

后续工作必须有明确任务；不因“版本发布”自动解禁 `01` 中的暂缓项。

---

## 7. 最近变更

| 日期 | 变更 |
|---|---|
| 2026-11-18 | Phase 8：文档压缩并建立 `docs/archive/`；无注释 limits 示例；v1.0.0 单文件（framework-dependent）发布到 `dist/` |
