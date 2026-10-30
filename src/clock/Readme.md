# clock — 悬浮窗表现层

无边框、透明、置顶的时间悬浮窗：负责把时间/天气文本画出来，接收拖动与右键，并按提醒队列显示气泡。不做格式解析，也不持久化设置。

## 放什么

- `ClockWindow.cs`：窗口样式、层级文字、拖动与位置捕获、滚轮缩放字号、全屏自动隐藏、点击穿透、置顶维持、天气行、提醒检查与展示、位置/缩放/提醒/日程事件。
- `TaskbarClockWindow.cs`：任务栏时钟——查找 `Shell_TrayWnd`/`TrayClockWClass`、把系统时钟移出任务栏、按矩形覆盖、2 秒重同步、退出时移回系统时钟。
- `LayeredText.cs`：文字阴影与 3D 立体字的分层绘制。

## 不放什么

- 时间格式与农历/干支计算 → `../format/`
- 设置读写与设置界面 → `../settings/`
- Win32 声明与开机自启 → `../platform/`
- 提醒与日程的数据结构、触发窗口计算 → `../reminders/`

## 边界

- 用户操作通过 `MenuRequested`、`PositionChanged`、`ScaleChanged`、`ReminderTriggered`、`ScheduleTriggered` 事件交给 `../app/AppController.cs`。
- 滚轮缩放只改 `FontSize`（步长 4，16–160），不改变位置与透明度；全屏隐藏依据 `SHQueryUserNotificationState` 的 3/4 状态，仅隐藏/恢复窗口，计时器保持运行。
- 任务栏时钟只做覆盖：**隐藏系统时钟会让任务栏重排、托盘图标左移**，因此改为把 `TrayClockWClass` 移出任务栏保留占位，再把本窗口移到其矩形上，不注入 explorer；恢复时按父窗口坐标移回并发送 `WM_SETTINGCHANGE` 让任务栏重排。覆盖窗宽度限制在原生时钟矩形内，文字放不下时自动缩小字号。
- 窗口只读 `AppSettings` 的副本式引用，修改后由控制器统一保存。
