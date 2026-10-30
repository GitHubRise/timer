# clock — 悬浮窗表现层

无边框、透明、置顶的时间悬浮窗：负责把时间/天气文本画出来，接收拖动与右键，并按提醒队列显示气泡。不做格式解析，也不持久化设置。

## 放什么

- `ClockWindow.cs`：窗口样式、层级文字、拖动与位置捕获、滚轮缩放字号、全屏自动隐藏、点击穿透、置顶维持、天气行、提醒检查与展示、位置/缩放/提醒/日程事件。
- `TaskbarClockWindow.cs`：任务栏时钟控制器——定位 `TrayClockWClass`、调用 `TrayClockInjector` 注入原生组件，每秒编排两行文本与样式并推送；不再持有 WPF 窗口。
- `LayeredText.cs`：文字阴影与 3D 立体字的分层绘制。

## 不放什么

- 时间格式与农历/干支计算 → `../format/`
- 设置读写与设置界面 → `../settings/`
- Win32 声明与开机自启 → `../platform/`
- 提醒与日程的数据结构、触发窗口计算 → `../reminders/`

## 边界

- 用户操作通过 `MenuRequested`、`PositionChanged`、`ScaleChanged`、`ReminderTriggered`、`ScheduleTriggered` 事件交给 `../app/AppController.cs`。
- 滚轮缩放只改 `FontSize`（步长 4，16–160），不改变位置与透明度；全屏隐藏依据 `SHQueryUserNotificationState` 的 3/4 状态，仅隐藏/恢复窗口，计时器保持运行。
- 任务栏时钟走注入：`TrayClockInjector` 把 `GameClockTray.dll` 加载进当前用户 `explorer.exe`，DLL 子类化 `TrayClockWClass` 后自绘两行，**不隐藏、不移动系统时钟**（隐藏或移出会触发任务栏重排、托盘图标移位）。文本与样式通过 `WM_COPYDATA` 每秒推送；DLL 15 秒收不到配置会自动恢复原生绘制，`explorer` 重启后由控制器按新进程重新注入，退出或关闭开关时发卸载消息恢复原 WndProc。
- 窗口只读 `AppSettings` 的副本式引用，修改后由控制器统一保存。
