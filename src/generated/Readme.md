# generated — 构建时生成

`build.ps1` 在编译前生成版本信息源码的目录，内容随每次构建变化，因此不纳入版本库。

## 放什么

- `BuildInfo.g.cs`：`BuildInfo.Version`、`BuildInfo.BuildNumber`、`BuildInfo.BuildTime`（格式由 `build.ps1` 决定）。

## 不放什么

- 任何手写源码或需要评审的代码
- 构建产物（exe、zip、校验文件 → `../../dist/`）

## 边界

- 目录本身靠 `.gitkeep` 与 `.gitignore` 中的 `BuildInfo.g.cs` 规则保留；文件缺失时构建脚本会自动重建。
- 生成文件不得手工编辑；需要新增字段时改 `build.ps1` 的模板。
