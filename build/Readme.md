# build — 构建输入

构建脚本读取的清单、运行配置与编译号计数。与根目录 `build.ps1` 配合，不存放脚本本身。

## 放什么

- `app.manifest`：应用清单，编译时以 `/win32manifest` 传入。
- `GameClock.exe.config`：运行时配置，构建后复制到 `dist/`。
- `build.number`：编译号计数器，每次构建自增；属于本机状态，被 `.gitignore` 忽略。

## 不放什么

- 构建脚本 → 根 `build.ps1`
- 编译输出（exe、zip、SHA256SUMS）→ `../dist/`
- 源码 → `../src/`

## 边界

- `GameClock.exe.config` 与 `app.manifest` 随仓库分发，修改后需重新构建验证。
- 不要手工编辑 `build.number`；需要重置编译号时直接改文件并在提交说明中注明。
