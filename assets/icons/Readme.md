# icons — 界面 SVG 图标

设置窗口与关于窗口使用的矢量图标，以 SVG 路径或内联数据加载，不随 exe 单独分发。

## 放什么

- `pencil.svg`、`plus.svg`、`power.svg`、`search.svg`、`trash.svg`：按钮与列表操作图标。

## 不放什么

- 应用/托盘图标 → `../GameClock.ico`
- 文档截图 → `../../docs/images/`
- 位图与生成图

## 边界

- 新增图标需在 `src/theme/SvgIcons.cs` 注册路径，并在 `assets/Readme.md` 与根 `README.md` 的源码结构中同步。
- 保持单色、透明背景与统一画布尺寸，颜色由 `src/theme/UiTheme.cs` 决定。
