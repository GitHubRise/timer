# clock — 悬浮窗与任务栏表现层

无边框、透明、置顶的时间悬浮窗，以及覆盖系统任务栏时间区域的时钟与“今日”面板。负责把时间/天气文本画出来、接收拖动与右键、按提醒队列显示气泡。不做格式解析，也不持久化设置。

## 放什么

- `ClockWindow.cs`：窗口样式、层级文字、拖动与位置捕获、滚轮缩放字号、全屏自动隐藏、点击穿透、置顶维持、天气行、提醒检查与展示、位置/缩放/提醒/日程事件。
- `TaskbarClockWindow.cs`：任务栏时钟——查找 `Shell_TrayWnd`/`TrayClockWClass`、按矩形覆盖、采样任务栏背景、读取原生字体、每 250 ms 抬到 TOPMOST 组最前、1 秒几何同步；处理悬停面板、左键系统日历与右键菜单。
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
- 任务栏覆盖窗必须能收到鼠标：使用不透明的普通置顶窗口（`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`，不启用 `WS_EX_TRANSPARENT`），并每 250 ms 检查 Z 序，**只在被任务栏压下去时**调用 `RaiseTopmost`。`RaiseTopmost` 把窗口插到任务栏正上方（`GetWindow(shell, GW_HWNDPREV)`），不能顶到 TOPMOST 最前，否则会盖住任务栏提示框与右键菜单。调用只在 `IsVisible` 时进行且不带 `SWP_SHOWWINDOW`，否则会把全屏隐藏后的窗口重新显示；恢复显示在 `SetVisible(true)` 里显式 `Show()`。
- 覆盖区宽度：原生时钟约 93 px 放不下长格式，`SyncGeometry` 用 `NativeMethods.FindChildToRight` 把矩形向右扩到紧邻的「操作中心」按钮；字号保持原生，`Viewbox` 用 `Stretch.Uniform` + `DownOnly`（不能用 `Fill`，会横向压扁字形）。覆盖通知按钮后由共用菜单的「操作中心」项补偿入口。
- 面板不抢焦点：悬停约 0.42 秒自动展开、光标移开 0.36 秒收起，开合只由悬停计时器决定（点击不再固定）；面板只读 `AppSettings` 的副本。
- 左键单击转给系统日历：先 `NativeMethods.PositionCursorOnNativeClock()`（覆盖窗还可见时把光标摆到原生时钟中心、预热任务栏 hover）→ 隐藏覆盖窗 → `Dispatcher.BeginInvoke` 延后调 `NativeMethods.ClickNativeClock()` 合成真实点击（不能同步发：当前输入消息里窗口还握着捕获）→ **保持隐藏约 90 ms**（`CalendarRestoreDelayMs`，或光标已离开时钟矩形）再由 250 ms 的 `OnTick` 调 `RestoreAfterCalendarClick()` 显示回来；另有 `CalendarClickCooldownMs = 600` 冷却与 `CalendarHoverGraceMs = 3000` 悬停宽限（宽限内不弹今日面板，免得盖住日历）。不能点完立刻回显（松开消息会再触发一次单击，表现为连续闪烁）。
- 窗口只读 `AppSettings` 的副本式引用，修改后由控制器统一保存。
