# assets — 随包资源

会被编译进 exe 或随包分发的二进制与矢量资源。

## 放什么

- `GameClock.ico`：应用与托盘图标，编译时通过 `/win32icon` 嵌入。
- `icons/*.svg`：界面使用的矢量图标（`pencil`、`plus`、`power`、`search`、`trash`）。

## 不放什么

- 文档截图 → `../docs/images/`
- 构建生成物 → `../dist/`
- 运行时代码 → `../src/`

## 边界

- 文件名被 `build.ps1` 或代码路径引用，重命名需同步修改并重新构建。
- 新增图标需同时更新 `README.md` 的源码结构说明。
