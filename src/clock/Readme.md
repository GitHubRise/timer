# clock — 悬浮窗与任务栏表现层

无边框、透明、置顶的时间悬浮窗，以及覆盖系统任务栏时间区域的时钟与“今日”面板。负责把时间/天气文本画出来、接收拖动与右键、按提醒队列显示气泡。不做格式解析，也不持久化设置。

## 放什么

- `ClockWindow.cs`：窗口样式、层级文字、拖动与位置捕获、滚轮缩放字号、全屏自动隐藏、点击穿透、置顶维持、天气行、提醒检查与展示、位置/缩放/提醒/日程事件。
- `TaskbarClockWindow.cs`：任务栏时钟——查找 `Shell_TrayWnd`/`TrayClockWClass`、按矩形覆盖、采样任务栏背景、读取原生字体、每 250 ms 抬到 TOPMOST 组最前、1 秒几何同步；处理悬停/单击面板与右键菜单。
- `TaskbarPanelWindow.cs`：任务栏时钟展开的“今日”面板（大号时间、日期、农历、天气、当天提醒/日程），不抢焦点。
- `LayeredText.cs`：文字阴影与 3D 立体字的分层绘制。

## 不放什么

- 时间格式与农历/干支计算 → `../format/`
- 设置读写与设置界面 → `../settings/`
- Win32 声明与开机自启 → `../platform/`
- 提醒与日程的数据结构、触发窗口计算 → `../reminders/`

## 边界

- 用户操作通过 `MenuRequested`、`PositionChanged`、`ScaleChanged`、`ReminderTriggered`、`ScheduleTriggered` 事件交给 `../app/AppController.cs`；任务栏面板的“打开设置”走 `SettingsRequested`。
- 滚轮缩放只改 `FontSize`（步长 4，16–160），不改变位置与透明度；全屏隐藏依据 `SHQueryUserNotificationState` 的 3/4 状态，仅隐藏/恢复窗口，计时器保持运行。
- 任务栏时钟只做覆盖：**不移动、不隐藏系统时钟**，因此覆盖窗消失（关闭、退出、崩溃）后系统时钟自然恢复。外观跟随系统：字体取 `WM_GETFONT`，背景采样原生时钟上下边缘像素，文字颜色按背景亮度选择。
- 任务栏覆盖窗必须能收到鼠标：使用不透明的普通置顶窗口（`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`，不启用 `WS_EX_TRANSPARENT`），并每 250 ms `RaiseTopmost`，否则任务栏会把它压到下面。`RaiseTopmost` 只在 `IsVisible` 时调用且不带 `SWP_SHOWWINDOW`，否则会把全屏隐藏后的窗口重新显示；恢复显示在 `SetVisible(true)` 里显式 `Show()`。
- 面板不抢焦点：悬停约 0.42 秒自动展开；单击固定，点击面板外或再次单击任务栏时钟收起；面板只读 `AppSettings` 的副本。
- 窗口只读 `AppSettings` 的副本式引用，修改后由控制器统一保存。
