# 07 — 质量检查与发布清单

> 适用于 v1.0.0。剩余限制见 `01-open-issues.md`；格式细节见 `08-format-notes.md`。

---

## 1. Core 契约

- [x] 只读随机访问 source；无共享可变 `Position`
- [x] 64 位 `SourceRange`；`checked` 溢出检查
- [x] 不可变 `FormatNode`；子节点在父范围内；兄弟不重叠
- [x] 未覆盖区间显式节点（`<trailing data>`/`<unparsed data>`/`<padding>`）
- [x] 解析失败返回部分结果 + 诊断，不伪造 RawData
- [x] 所有 parser 只读 `ParseContext.Limits`
- [x] `MaxStringBytes`/`MaxBlobBytes`/`MaxDataLines` 只约束单次读取
- [x] 探测与长时间读取使用固定 512 KiB 阈值，不阻断执行（D-037）
- [x] 无深度/子节点数/总节点数/解析时长限制（D-032）
- [x] 报告无总预算；Fields/Structure 完整；Hex 固定首尾各 128 KiB（D-034）
- [x] Raw 完整范围流式导出，不因大小拒绝；短读/提前 EOF/取消有回归
- [x] 取消令牌贯穿探测、解析、节点解析、导出
- [x] `NodeTreeValidator` 校验范围、排序、重叠、payload 覆盖

## 2. 字段与命名

- [x] 原始字段名遵循官方写法（snake_case/camelCase/PascalCase/prose label）
- [x] 原始值优先；解释用 `raw (human)` 或尖括号派生条目
- [x] 尖括号只用于派生值、关键 Note、解析器错误
- [x] 标题 `(Marker)NoSpaceTitle 'RawID'`；树显示 `ProcessedTagId`
- [x] 不可打印字节 `\xNN`；GUID 大括号；varint `0x...`
- [x] 身份字段不在字段区重复
- [x] 截断不修改节点名；状态由 Kind/诊断表达

## 3. GUI

- [x] 旧版窗口尺寸/布局/字体/菜单
- [x] 树懒加载；展开时一次性创建全部子节点；`Expand All` 分批可取消
- [x] 字段列表虚拟模式 + OwnerDraw
- [x] Hex 只渲染可见行；64 位行号；按节点范围截断
- [x] 加载进度持续反馈；无进度时 Marquee
- [x] 后台节点解析 + 取消 + 代次校验
- [x] 关闭文件取消任务、释放 source、清空结果/缓存
- [x] 无 `Application.DoEvents()`；无关闭跨线程检查
- [x] 导出可取消；文件名 sanitize；报告转义
- [x] 部分实现/未展开区域以 `<Note>` 标注
- [x] 字段右键复制；Hex 拖选/复制；滚动条可到最后一行
- [x] Hex 表头/文字/选区/命中共用列坐标；字体/DPI 变化后重算
- [x] 菜单快捷键与树图标符合 D-027
- [x] Statistics：后台计算、饼图/图例/悬停、`Node type` 过滤、节点定位
- [x] `Settings` 进程内生效：小数位 1–6、Top N 3/5/8/10
- [x] 根节点只显示 D-025 的六个字段
- [x] 字段搜索/过滤未实现（明确不做，D-021）

## 4. 格式覆盖

| 格式 | 状态 | 必须保持 |
|---|---|---|
| Raw | 稳定 | 未知文件整块 `Raw`，不猜字段 |
| RIFF | 稳定 | pad 计入范围；`LIST:movi` 整体 `Unparsed`；OpenDML `dmlh`/`indx` |
| EBML | 稳定 | VINT、unknown size；block 元素高熵 `Unparsed`（D-033） |
| FLV | 稳定 | `PreviousTagSize`、AMF0/AMF3、Enhanced FLV、CodecID 12、Multitrack |
| ASF | 稳定 | GUID object 树、GUID `{}`、Data Packets 单一 `Unparsed` |
| FBX | 稳定 | 32/64 位 record、全部类型、deflate 不解压、footer framing |
| PNG | 稳定 | chunk/CRC、文本、APNG、`cICP`/`mDCV`/`cLLI`、颜色语义、`iCCP` |
| JPEG | 稳定 | Identifier 分派；Exif/XMP/8BIM/单段 ICC；其它 APP `Unparsed` |
| ISOBMFF | 稳定 | 32/64 位 size、`size == 0`、`uuid`、sample entry、分片、HEIF |
| MIDI | 稳定 | MThd/MTrk、逐事件、running status、SysEx、meta |
| PSD/PSB | **不稳定** | 5 个顶层 section；共享 8BIM；layer/channel；像素不解码 |
| Ogg | 稳定 | page/lacing/CRC；各 codec header；packet 不解码 |
| FLAC | 稳定 | metadata block 树；全大写；音频帧 `Unparsed` |
| Blender | 稳定（无压缩） | 头/块头、DNA1/GLOB/TEST/ENDB；其余 `Unparsed` |
| Protobuf | 稳定（启发式） | 无 `.proto`；wire 字段；文本/消息/字节分类 |
| NBT | 稳定（无压缩） | 12 种 tag；名称/索引在右侧视图；压缩/MCA 按 Raw |

## 5. 性能与安全

- [x] 树不在打开时创建全部节点；字段列表不创建全部行；Hex 不预生成全部行
- [x] 导出流式；Fields/Structure 完整；Hex 固定首尾窗口
- [x] EBML/JPEG/FBX/FLV 16 MiB 读取窗口；JPEG 单次扫描；尖峰内存上限 16 GiB（D-031）
- [x] 零填充/截断文件不会产生节点爆炸/死循环
- [x] 所有长度/字符串读取有上限；所有循环有取消检查
- [x] 不执行文件内容；不把内容当路径；导出文件名 sanitize
- [x] 诊断只针对格式本身（D-028）
- [ ] >4 GB Hex / >2 GB 单节点 / 超大报告实测（明确不做，D-022）
- [ ] 各格式随机 fuzz（明确不做，D-022）

## 6. 发布

- [x] `dotnet build UniversalParser.sln`：0 warning / 0 error
- [x] `dotnet test UniversalParser.sln`：117 passed
- [x] `dotnet format --verify-no-changes`
- [x] `docs/01-open-issues.md` 无阻塞项；P1 全部关闭/接受
- [x] 支持矩阵与 `05-implementation-history.md` 一致
- [x] 文档无过期链接、无过期测试数；历史内容已归档
- [x] `universalparser.limits.example.json` 为无注释 JSON，并随 `dist/` 发布
- [x] `dist/UniversalParser.exe` 单文件 framework-dependent（win-x64），启动验证通过
- [x] 发布目录不含 PDB/XML 文档等额外文件

发布命令：

```powershell
dotnet publish src/UniversalParser.App/UniversalParser.App.csproj `
  -c Release -r win-x64 -p:SelfContained=false `
  -p:PublishSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false -o dist
```

发布后删除 `dist/*.xml`，再复制 limits 示例。
