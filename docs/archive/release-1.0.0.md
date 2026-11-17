# UniversalParser v1.0.0 发布记录

> 日期：2026-11-18 · 阶段：Phase 8 完成

## 产物

```text
dist/
├─ UniversalParser.exe                    1,405,087 bytes (1.34 MiB)
└─ universalparser.limits.example.json    无注释示例配置
```

- `UniversalParser.exe` 为 Windows x64、framework-dependent、单文件发布；
  目标机器需要安装 **.NET 10 Desktop Runtime (x64)**，不包含运行时。
- 未做代码签名、压缩打包或安装器；直接运行 exe。
- SHA-256：`5D8C71C6D0FC2089C97A34A70B7B0E7551A5D065101A8A2A869E8CDD81783355`

## 构建

```powershell
dotnet publish src/UniversalParser.App/UniversalParser.App.csproj `
  -c Release -r win-x64 -p:SelfContained=false `
  -p:PublishSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false -o dist
```

发布后删除 `dist/*.xml`，再复制 `universalparser.limits.example.json`。

## 配置

把示例复制为可执行文件旁的 `universalparser.limits.json`，只保留需要覆盖的键：

```json
{
  "maxStringBytes": 4194304,
  "maxBlobBytes": 268435456,
  "maxDataLines": 10000
}
```

缺失键使用内置默认值；文件损坏时回退默认值，并在状态栏显示
`Limits configuration ignored: ...`。探测与长时间读取的 512 KiB 阈值不可配置。

## 格式范围

ISOBMFF、RIFF、EBML、FLV、ASF、FBX（二进制）、PNG/APNG、JPEG、MIDI、PSD/PSB、
Ogg、FLAC、Blender（无压缩）、Protocol Buffers、Minecraft NBT（无压缩）与 Raw 兜底；
内嵌 EXIF、ID3v2、ICC 与 Photoshop 8BIM。

PSD/PSB 状态为**不稳定**：基础结构可用，但 Adobe 私有/复杂结构可能只显示原始范围。
其余格式为稳定的原始解析，不承诺完整规范覆盖。

## 已知限制

见 `docs/01-open-issues.md`。摘要：

- 不解码 payload；仅 PNG `iCCP`/`zTXt`/压缩 `iTXt` 有界解码。
- 不做跨块/跨字段语义校验。
- 无字段搜索/过滤、JSON 报告、SHA-256。
- 报告 Hex 固定首尾各 128 KiB；完整二进制用 `Export Raw`。
- 大文件只做流式/惰性读取，不提供 >4 GB Hex 的自动化实测。

## 验证

- `dotnet build UniversalParser.sln`：0 warning / 0 error
- `dotnet test UniversalParser.sln`：117 passed
- `dotnet format --verify-no-changes`：通过
- `dist/UniversalParser.exe` 启动验证通过
