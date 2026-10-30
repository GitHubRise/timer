# theme — 共享视觉层

暗色 UI 的画笔、控件样式、字体目录与矢量图标。被设置窗口、关于窗口和悬浮窗共同引用，不承载业务状态。

## 放什么

- `UiTheme.cs`：颜色、画笔、按钮/下拉框/页签/列表等控件样式。
- `ModernScrollBar.cs`：自定义滚动条样式与轨道。
- `FontCatalog.cs`：系统字体列表与字体项模板。
- `SvgIcons.cs`：内嵌 SVG 路径数据与图标工厂。

## 不放什么

- 具体窗口布局与事件 → `../settings/`、`../clock/`
- 设置模型与持久化 → `../settings/AppSettings.cs`
- 位图资源文件 → `../../assets/`

## 边界

- 不引用 `AppSettings`，样式以静态资源或静态工厂方法提供。
- 颜色变化统一走 `UiTheme`，避免窗口内散落硬编码色值。
