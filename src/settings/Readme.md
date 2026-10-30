# settings — 设置模型、持久化与设置界面

应用的配置中心：一份 `AppSettings` 数据模型、JSON 持久化、颜色工具，以及五个页签的设置窗口与日程编辑弹框。

## 放什么

- `AppSettings.cs`：`AppSettings`（`[DataContract]`）、`SettingsStore`（`%APPDATA%\GameClock\settings.json`）、`ColorUtil`。
- `SettingsWindow.cs`：字段、构造、窗口骨架、公共控件工厂、预览、提交与默认值。
- `SettingsWindow.Shell.cs`：页签容器、标题栏、关闭与键盘行为。
- `SettingsWindow.Chrome.cs`：窗口外观、行为、透明度、颜色与预览刷新。
- `SettingsWindow.Appearance.cs`、`.Format.cs`、`.Reminders.cs`、`.Schedule.cs`、`.Weather.cs`：各页签的构建与事件。
- `ScheduleEditorWindow.cs`：日程添加/编辑弹框。
- `AboutWindow.cs`：版本、编译号、运行环境。

## 不放什么

- 悬浮窗本体 → `../clock/`
- 提醒/日程触发判定 → `../reminders/`
- 主题画笔与控件样式 → `../theme/`

## 边界

- 设置窗口编辑 `AppSettings` 的克隆副本，用户提交后通过 `ApplyRequested` 事件交给 `../app/AppController.cs` 应用并保存。
- 「外观 → 显示位置」的「桌面悬浮窗」「任务栏时钟」只写 `ShowDesktopOverlay`/`ShowTaskbarClock`；至少保留一个由设置窗口与 `Normalize` 双重保证，窗口开关由控制器执行。
- `SettingsWindow` 以 `partial` 按页签拆分，新增成员放进对应分部文件；主文件只保留字段、构造与公共骨架。
- 分区文件必须复用主文件的 using 集，避免同类型在不同文件里解析到不同别名。
