# clock — 悬浮窗表现层

无边框、透明、置顶的时间悬浮窗：负责把时间/天气文本画出来，接收拖动与右键，并按提醒队列显示气泡。不做格式解析，也不持久化设置。

## 放什么

- `ClockWindow.cs`：窗口样式、层级文字、拖动与位置捕获、点击穿透、置顶维持、天气行、提醒检查与展示、位置/提醒/日程事件。
- `LayeredText.cs`：文字阴影与 3D 立体字的分层绘制。

## 不放什么

- 时间格式与农历/干支计算 → `../format/`
- 设置读写与设置界面 → `../settings/`
- Win32 声明与开机自启 → `../platform/`
- 提醒与日程的数据结构、触发窗口计算 → `../reminders/`

## 边界

- 用户操作通过 `MenuRequested`、`PositionChanged`、`ReminderTriggered`、`ScheduleTriggered` 事件交给 `../app/AppController.cs`。
- 窗口只读 `AppSettings` 的副本式引用，修改后由控制器统一保存。
