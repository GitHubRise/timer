# settings — 设置模型、持久化与设置界面

应用的配置中心：一份 `AppSettings` 数据模型、JSON 持久化、颜色工具，以及六个页签的设置窗口与日程编辑弹框。

## 放什么

- `AppSettings.cs`：`AppSettings`（`[DataContract]`）、`SettingsStore`（`%APPDATA%\GameClock\settings.json`）、`ColorUtil`；含悬浮窗与任务栏两套独立的字体/颜色/格式字段。
- `SettingsWindow.cs`：字段、构造、窗口骨架、公共控件工厂、预览、提交与默认值。
- `SettingsWindow.Shell.cs`：页签容器、标题栏、关闭与键盘行为。
- `SettingsWindow.Chrome.cs`：窗口外观、行为、透明度、颜色与预览刷新。
- `SettingsWindow.Appearance.cs`、`.Taskbar.cs`、`.Format.cs`、`.Reminders.cs`、`.Schedule.cs`、`.Weather.cs`：各页签的构建与事件；`.Taskbar.cs` 管任务栏独立字体、颜色、格式语言与两行格式；`.Format.cs` 的“全局时间格式”页只放时区（含「同步系统时区」按钮）与变量说明，并提供悬浮窗格式区 `BuildOverlayFormatSection()`。
- `ScheduleEditorWindow.cs`：日程添加/编辑弹框。
- `AboutWindow.cs`：版本、编译号、运行环境。

## 不放什么

- 悬浮窗本体 → `../clock/`
- 提醒/日程触发判定 → `../reminders/`
- 主题画笔与控件样式 → `../theme/`

## 边界

- 设置窗口编辑 `AppSettings` 的克隆副本，用户提交后通过 `ApplyRequested` 事件交给 `../app/AppController.cs` 应用并保存。
- 「桌面悬浮窗」页签与「任务栏时钟」页签各管自己的开关：分别只写 `ShowDesktopOverlay` / `ShowTaskbarClock`，两者互相独立、可同时关闭；窗口开关由控制器执行。
- 任务栏字体、字号、颜色、格式只写 `TaskbarFontFamilyName`/`TaskbarFontSize`/`TaskbarColorHex`/`TaskbarFormatMode`/`TaskbarFormat`/`TaskbarDotNetFormat`，不复用悬浮窗字段；默认值见 `AppSettings.SetDefaults` 与 `FormatEngine.DefaultTaskbarTokenFormat`。
- 格式语言与常用/自定义格式按显示位置各存一份：悬浮窗写 `FormatMode`/`TimeFormat`/`TokenTimeFormat`，任务栏写 `TaskbarFormatMode`/`TaskbarDotNetFormat`/`TaskbarFormat`；`TaskbarFormat` 只存简洁变量、`TaskbarDotNetFormat` 只存 .NET 格式，两者与 `TaskbarFormatMode` 同步切换。
- `SettingsWindow` 以 `partial` 按页签拆分，新增成员放进对应分部文件；主文件只保留字段、构造与公共骨架。
- 分区文件必须复用主文件的 using 集，避免同类型在不同文件里解析到不同别名。
