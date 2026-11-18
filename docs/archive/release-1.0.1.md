# UniversalParser v1.0.1 发布记录

> 日期：2026-11-19 · 类型：本地发布（未做 git 提交/推送）

## 产物

```text
dist/
├─ UniversalParser_1.0.1.exe             1,423,007 bytes
└─ universalparser.limits.example.json    无注释示例配置
```

- Windows x64、framework-dependent、单文件发布；目标机器需要 **.NET 10 Desktop Runtime (x64)**。
- App 启用 `RollForward=LatestMajor`，允许在未来主版本运行时上启动。
- 未做代码签名、压缩打包或安装器；直接运行 exe。
- SHA-256：`21F6EB190FF8216FCB732D2F3538E053882802E19000A8A3242C2F36AC58D8AF`
- 文件版本：`1.0.1.0`；产品版本：`1.0.1+248460b1a02f32f8259b5a5e49daa5f9ccd841de`

## 构建

```powershell
Remove-Item dist\* -Force
dotnet publish src/UniversalParser.App/UniversalParser.App.csproj `
  -c Release -r win-x64 -p:SelfContained=false `
  -p:PublishSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false -o dist
Remove-Item dist\*.xml -Force
Rename-Item dist\UniversalParser.exe UniversalParser_1.0.1.exe
Copy-Item universalparser.limits.example.json dist\
```

## 本版变化

- 完成 issue#1 的代码质量维护：文本显示/编码策略统一，共享编码、转义、有界读取与格式化工具，
  静态查找表改为 `FrozenDictionary`，Protobuf 增加 ZigZag/float/double 预览，
  修正 Windows 保留设备名与 RIFF 的 Windows-1252 回退。
- 启用 `AnalysisLevel=latest-all`；仅对异常边界、Token 顺序和纯性能建议做有理由抑制。
- 未提取字符串资源，未启用 AOT；未处理明确排除的全局配置与性能项。

## 验证

- `dotnet build UniversalParser.sln`：0 warning / 0 error（`latest-all`）
- `dotnet test UniversalParser.sln`：**170 passed**
- `dotnet format UniversalParser.sln --verify-no-changes`：通过
- `dist/` 只保留版本化 exe 与 limits 示例

## 已知限制

见 `docs/01-open-issues.md`；产品行为与 v1.0.0 一致，本版为质量维护与重新发布。
