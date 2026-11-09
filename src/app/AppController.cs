// 应用总控制器：把设置、悬浮窗、任务栏时钟、托盘与共用菜单串成一条生命周期。
// 启动线：Program 的 Startup 事件 → Start 载入设置、建窗口、建托盘 → 计时器与天气开始工作。
// 退出线：菜单“退出”或宿主 Dispose → 先让 _exiting 生效并停计时器，再逐项关窗口、卸托盘。
// 边界：所有 UI 入口先判 _exiting；天气回调可能晚到，靠 _weatherGeneration 代次号丢弃过期结果。
// 本类只做编排，不画窗口、不读写注册表；窗口外观与设置持久化分别交给 clock/、settings/ 模块。

using System;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace GameClock
{

    /// <summary>
    /// 应用总控制器：设置载入与落盘、悬浮窗/任务栏时钟的创建与显隐、托盘图标与共用右键菜单的分发中枢。
    /// 生命周期主线为 <see cref="Start"/> → 菜单或设置变更 → <see cref="ExitApp"/>（宿主也可直接调用 <see cref="Dispose"/>）。
    /// 内部靠 <see cref="_exiting"/> 与 <see cref="_disposed"/> 守住退出期：窗口字段退出后会置空，调用前一律判空。
    /// </summary>
    internal sealed class AppController : IDisposable
    {

        // ===== 宿主与配置 =====

        /// <summary>WPF 应用对象；只用来取 Dispatcher 与最终 Shutdown，不参与界面绘制。</summary>
        private readonly Application _app;

        /// <summary>当前生效的设置；设置窗口提交的是克隆副本，这里整体替换引用，不原地改字段。</summary>
        private AppSettings _settings;

        // ===== 窗口与界面元素 =====

        /// <summary>桌面悬浮窗；退出流程会置空，任何回调里都必须先判空再调用。</summary>
        private ClockWindow _clock;

        /// <summary>任务栏时钟覆盖窗；首次开启任务栏显示时才创建，关闭开关只停不销毁。</summary>
        private TaskbarClockWindow _taskbarClock;

        /// <summary>最近一次成功取回的天气；关闭天气或城市变更时清空，避免旧城市数据残留。</summary>
        private WeatherInfo _weatherInfo;

        /// <summary>悬浮窗右键与托盘右键共用的菜单，本类持有并负责释放。</summary>
        private OverlayMenu _menu;

        /// <summary>系统托盘图标；退出时先隐藏再释放，避免任务栏留下残影。</summary>
        private WinForms.NotifyIcon _tray;

        /// <summary>托盘图标句柄；单独保存以便 Dispose 时释放，不能指望 NotifyIcon 代为回收。</summary>
        private System.Drawing.Icon _trayIcon;

        /// <summary>设置窗口实例；非 null 表示设置正开着，用于重复打开时激活与暂停置顶守护。</summary>
        private SettingsWindow _settingsWindow;

        // ===== 计时器 =====

        /// <summary>防抖保存计时器：拖动或滚轮缩放后 550ms 内无新操作才落盘。</summary>
        private DispatcherTimer _saveTimer;

        /// <summary>天气刷新计时器；间隔取自设置项，关闭天气时停表。</summary>
        private DispatcherTimer _weatherTimer;

        // ===== 状态标志 =====

        /// <summary>天气请求代次号；自增即作废所有在途回调，防止慢请求覆盖新设置。</summary>
        private int _weatherGeneration;

        /// <summary>菜单是否展开；与 <see cref="_settingsOpen"/> 一起决定是否暂停置顶守护。</summary>
        private bool _menuOpen;

        /// <summary>设置窗口是否开着；开启期间暂停置顶守护，关闭后立即恢复。</summary>
        private bool _settingsOpen;

        /// <summary>退出中标志；置 true 后菜单与回调入口全部直接返回。</summary>
        private bool _exiting;

        /// <summary>Dispose 幂等标志；重复释放直接返回。</summary>
        private bool _disposed;

        /// <summary>只保存宿主引用；真正的初始化在 <see cref="Start"/> 里做，构造期不建任何窗口。</summary>
        public AppController(Application app)
        {
            _app = app;
        }

        /// <summary>
        /// 处理第二实例发来的“显示设置”请求，由 Program 的监听线程切回 UI 线程后调用。
        /// 尚未启动完成（悬浮窗为空）或正在退出时直接忽略，避免弹窗打断退出流程。
        /// </summary>
        public void ShowSettingsFromExternal()
        {
            if (_exiting || _clock == null)
            {
                return;
            }
            ShowSettings();
        }

        /// <summary>
        /// 启动入口：载入设置 → 建共用菜单 → 建悬浮窗并订阅事件 → 建托盘 → 启动天气。
        /// 由 Program 在 <c>Application.Startup</c> 中调用一次；抛出的异常交给调用方弹致命错误并关机。
        /// 顺序敏感：菜单必须先于托盘创建（托盘要挂它的 ContextMenuStrip），天气放最后以免阻塞首屏。
        /// </summary>
        public void Start()
        {
            _settings = SettingsStore.Load();
            // 启动时按配置对齐一次注册表，处理用户手工删掉启动项、配置却仍开着的情况。
            AutoStartManager.SetEnabled(_settings.StartWithWindows);
            // 防抖保存计时器：拖动与滚轮缩放期间只不断重启，松手 550ms 后才真正写盘。
            _saveTimer = new DispatcherTimer(DispatcherPriority.Background);
            _saveTimer.Interval = TimeSpan.FromMilliseconds(550);
            _saveTimer.Tick += OnSaveTimerTick;

            // 共用菜单先建：托盘右键与悬浮窗右键都复用它，必须先于托盘存在。
            _menu = new OverlayMenu();
            _menu.SettingsClicked += OnMenuSettings;
            _menu.ClickThroughClicked += OnMenuClickThrough;
            _menu.TopmostClicked += OnMenuTopmost;
            _menu.WorkModeClicked += OnMenuWorkMode;
            _menu.LeisureModeClicked += OnMenuLeisureMode;
            _menu.ResetPositionClicked += OnMenuResetPosition;
            _menu.ActionCenterClicked += OnMenuActionCenter;
            _menu.AboutClicked += OnMenuAbout;
            _menu.ExitClicked += OnMenuExit;
            _menu.MenuOpening += OnMenuOpening;
            _menu.MenuOpened += OnMenuOpened;
            _menu.MenuClosed += OnMenuClosed;

            // 悬浮窗：先无条件 Show 一次让句柄就绪，再由 ApplyDisplaySurfaces 按开关决定真实显隐。
            _clock = new ClockWindow(_settings);
            _clock.MenuRequested += OnClockMenuRequested;
            _clock.PositionChanged += OnClockPositionChanged;
            _clock.ScaleChanged += OnClockScaleChanged;
            _clock.ReminderTriggered += OnClockReminderTriggered;
            _clock.ScheduleTriggered += OnClockScheduleTriggered;
            _clock.Show();
            ApplyDisplaySurfaces();

            CreateTrayIcon();
            SyncMenu();
            UpdateTrayText();
            // 首屏补一次置顶，避免启动瞬间被任务栏或其他置顶窗口压住。
            _clock.ReassertTopmost();

            // 天气计时器：先按配置定时长与启停，再立刻取一次，避免要等一个周期才出数据。
            _weatherTimer = new DispatcherTimer(DispatcherPriority.Background);
            _weatherTimer.Interval = TimeSpan.FromMinutes(30.0);
            _weatherTimer.Tick += OnWeatherTimerTick;
            ConfigureWeatherTimer();
            RefreshWeather();
        }

        /// <summary>
        /// 按设置同步两种显示位置：桌面悬浮窗按 <c>ShowDesktopOverlay</c> 显隐，任务栏时钟按 <c>ShowTaskbarClock</c> 启停。
        /// 启动时与设置提交后都会调用；任务栏窗口首次需要时才创建，关闭开关只停不销毁，便于快速重开。
        /// 依赖设置与悬浮窗已就绪，任一为空或正在退出就直接返回。
        /// </summary>
        private void ApplyDisplaySurfaces()
        {
            if (_settings == null || _exiting || _clock == null)
            {
                return;
            }

            // 桌面悬浮窗：恢复显示时补一次置顶，避免刚 Show 出来就被其他置顶窗盖住。
            if (_settings.ShowDesktopOverlay)
            {
                if (!_clock.IsVisible)
                {
                    _clock.Show();
                }
                _clock.ReassertTopmost();
            }
            else if (_clock.IsVisible)
            {
                _clock.Hide();
            }

            // 任务栏时钟：首次开启才建窗并订阅菜单/设置请求，之后只把新设置重放给它。
            if (_settings.ShowTaskbarClock)
            {
                if (_taskbarClock == null)
                {
                    _taskbarClock = new TaskbarClockWindow(_settings);
                    _taskbarClock.MenuRequested += OnTaskbarMenuRequested;
                    _taskbarClock.SettingsRequested += OnTaskbarSettingsRequested;
                }
                else
                {
                    _taskbarClock.ApplySettings(_settings);
                }
                // 天气开关关闭时传 null，让任务栏窗自行折叠天气行，不在窗口内部判断开关。
                _taskbarClock.SetWeather(_settings.WeatherEnabled ? _weatherInfo : null);
                _taskbarClock.Start();
            }
            else if (_taskbarClock != null)
            {
                _taskbarClock.Stop();
            }
        }

        /// <summary>
        /// 按设置重配天气计时器的间隔与启停。
        /// 先停表再判断：关闭天气后必须保证不再有周期请求，否则会在后台一直外呼。
        /// 间隔经 <c>WeatherRefreshOptions.Normalize</c> 收敛，手改配置写出 0 或负数也不会变成忙轮询。
        /// </summary>
        private void ConfigureWeatherTimer()
        {
            if (_weatherTimer == null)
            {
                return;
            }

            _weatherTimer.Stop();
            // 未启用天气时保持停表，等下次设置在 CommitDialogSettings 里重新调用本方法。
            if (_settings == null || !_settings.WeatherEnabled)
            {
                return;
            }

            int minutes = WeatherRefreshOptions.Normalize(_settings.WeatherRefreshMinutes);
            _weatherTimer.Interval = TimeSpan.FromMinutes(minutes);
            _weatherTimer.Start();
        }

        /// <summary>
        /// 创建托盘图标并挂上共用菜单，双击托盘打开设置（见 <see cref="OnTrayDoubleClick"/>）。
        /// 只在 <see cref="Start"/> 中调用一次；图标句柄由字段持有，最终在 <see cref="Dispose"/> 里释放。
        /// </summary>
        private void CreateTrayIcon()
        {
            _trayIcon = LoadApplicationIcon();

            // 托盘只承担入口职责，不保存业务状态；菜单本体仍归 _menu 所有。
            _tray = new WinForms.NotifyIcon();
            _tray.Icon = _trayIcon;
            _tray.Text = "游戏时间悬浮窗";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu.Menu;
            _tray.DoubleClick += OnTrayDoubleClick;
        }

        /// <summary>
        /// 取可执行文件自带的图标；提取不到（路径异常、资源被裁剪）时回退系统默认应用图标。
        /// 返回值交给 <see cref="CreateTrayIcon"/> 持有，调用方不要另行释放。
        /// </summary>
        private System.Drawing.Icon LoadApplicationIcon()
        {
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch (Exception)
            {
                // 图标缺失不应阻止程序启动，吞掉后统一走下面的系统默认图标。
            }
            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        }

        /// <summary>托盘双击：等同菜单里的“设置…”，已开着时由 <see cref="ShowSettings"/> 负责前置激活。</summary>
        private void OnTrayDoubleClick(object sender, EventArgs e)
        {
            ShowSettings();
        }

        /// <summary>悬浮窗右键：把共用菜单弹到鼠标位置；菜单为空或退出中则忽略。</summary>
        private void OnClockMenuRequested(object sender, EventArgs e)
        {
            if (_menu != null && !_exiting)
            {
                _menu.ShowAtCursor();
            }
        }

        /// <summary>
        /// 任务栏时钟右键：先同步勾选状态再弹菜单。
        /// 任务栏窗的设置可能落后于控制器（例如刚在设置里改过），所以这里强制刷新一次。
        /// </summary>
        private void OnTaskbarMenuRequested(object sender, EventArgs e)
        {
            if (_menu != null && !_exiting)
            {
                SyncMenu();
                _menu.ShowAtCursor();
            }
        }

        /// <summary>任务栏面板里的“打开设置”入口，直接复用统一的设置弹窗流程。</summary>
        private void OnTaskbarSettingsRequested(object sender, EventArgs e)
        {
            ShowSettings();
        }

        /// <summary>悬浮窗被拖动后进入防抖保存，避免拖动过程中每一帧都写磁盘。</summary>
        private void OnClockPositionChanged(object sender, EventArgs e)
        {
            ScheduleSave();
        }

        /// <summary>悬浮窗滚轮改字号后同样防抖保存；位置与字号共用一条保存路径。</summary>
        private void OnClockScaleChanged(object sender, EventArgs e)
        {
            ScheduleSave();
        }

        /// <summary>天气计时器到点：重新取一次数据，失败时由 <see cref="RefreshWeather"/> 保留旧值。</summary>
        private void OnWeatherTimerTick(object sender, EventArgs e)
        {
            RefreshWeather();
        }

        /// <summary>
        /// 取一次天气并按结果刷新悬浮窗与任务栏时钟。
        /// 关闭天气或正在退出时走“清空”分支并自增代次号，让在途请求回来也作废；
        /// 请求参数由当前格式串推导（空气质量、公网 IP 只在格式真正用到时才请求），减少无谓外呼。
        /// 回调切回 UI 线程后才碰控件，并用代次号比对丢弃过期结果。
        /// </summary>
        private void RefreshWeather()
        {
            if (_clock == null)
            {
                return;
            }
            // 未启用或已退出：清空展示并作废在途请求，不让旧数据继续留在界面上。
            if (_settings == null || !_settings.WeatherEnabled || _exiting)
            {
                _weatherGeneration++;
                _weatherInfo = null;
                _clock.SetWeather(null);
                if (_taskbarClock != null)
                {
                    _taskbarClock.SetWeather(null);
                }
                return;
            }

            // 发起前先领代次号；此后任何新请求都会让本次回调的结果失效。
            int generation = ++_weatherGeneration;
            string city = _settings.WeatherCity;
            // 只有简洁变量模式才把变量格式拼进来，.NET 格式里不会出现天气变量占位。
            string tokenFormat = FormatEngine.IsTokenMode(_settings.FormatMode)
                ? _settings.TokenTimeFormat
                : string.Empty;
            string combinedFormat = tokenFormat + "\n" + (_settings.WeatherFormat ?? string.Empty);
            WeatherFetchOptions options = new WeatherFetchOptions();
            options.IncludeAirQuality = FormatEngine.RequiresAirQuality(combinedFormat);
            options.IncludePublicIp = FormatEngine.RequiresPublicIp(combinedFormat);
            WeatherService.FetchAsync(city, options, delegate(WeatherInfo info)
            {
                // 回调可能在退出后才到，先在后台线程挡一次，避免访问已关闭的 Dispatcher。
                if (_exiting || _disposed)
                {
                    return;
                }

                try
                {
                    // 窗口与设置只允许在 UI 线程访问，统一切回 Dispatcher 再改界面。
                    _app.Dispatcher.BeginInvoke(
                        new Action(delegate
                        {
                            // 入队到执行之间状态可能已变：退出、已释放、或已有更新的请求。
                            if (_exiting || _disposed || generation != _weatherGeneration)
                            {
                                return;
                            }
                            // 用户可能在等待期间关掉天气或关掉悬浮窗，此时丢弃结果即可。
                            if (_settings == null || !_settings.WeatherEnabled || _clock == null)
                            {
                                return;
                            }
                            // 失败时 info 为 null，保留上一次的天气而不是把界面清空。
                            if (info != null)
                            {
                                _weatherInfo = info;
                                _clock.SetWeather(info);
                                if (_taskbarClock != null)
                                {
                                    _taskbarClock.SetWeather(info);
                                }
                            }
                        }),
                        DispatcherPriority.Background);
                }
                catch (InvalidOperationException)
                {
                    // Dispatcher 已随宿主关闭，属于正常退出时序，可以安全忽略。
                }
            });
        }

        /// <summary>
        /// 悬浮窗触发到点提醒：先落盘再发托盘气泡。
        /// 先保存是因为一次性提醒触发后即被移除，异常退出时不落盘会导致重启后重复提醒。
        /// </summary>
        private void OnClockReminderTriggered(object sender, ReminderEventArgs e)
        {
            SaveNow();
            if (_tray != null && _tray.Visible && e != null && e.Reminder != null)
            {
                _tray.BalloonTipTitle = e.Reminder.Time + " · 时间提醒";
                _tray.BalloonTipText = e.Reminder.Message;
                _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _tray.ShowBalloonTip(5200);
            }
        }

        /// <summary>日程到点：同样先落盘再弹气泡；标题里带上分类名与本次发生时间，便于区分重复日程。</summary>
        private void OnClockScheduleTriggered(object sender, ScheduleTriggeredEventArgs e)
        {
            SaveNow();
            if (_tray != null && _tray.Visible && e != null && e.Schedule != null)
            {
                _tray.BalloonTipTitle = ScheduleCategories.DisplayName(e.Schedule.Type)
                    + "日程 · "
                    + e.Occurrence.ToString("MM-dd HH:mm");
                _tray.BalloonTipText = e.Schedule.Title;
                _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _tray.ShowBalloonTip(5200);
            }
        }

        /// <summary>菜单即将展开：菜单项可能被设置窗口或托盘改过，展开前先同步一次勾选状态。</summary>
        private void OnMenuOpening(object sender, EventArgs e)
        {
            SyncMenu();
        }

        /// <summary>菜单已展开：暂停悬浮窗的置顶守护，否则守护会把菜单压到悬浮窗下面。</summary>
        private void OnMenuOpened(object sender, EventArgs e)
        {
            _menuOpen = true;
            UpdateTopmostSuspension();
        }

        /// <summary>
        /// 菜单关闭：恢复置顶守护。
        /// 若设置窗口还开着就不急着抢置顶，等设置关闭时统一恢复，避免两个窗口互相踩。
        /// </summary>
        private void OnMenuClosed(object sender, EventArgs e)
        {
            _menuOpen = false;
            UpdateTopmostSuspension();
            if (_clock != null && !_exiting && !_settingsOpen)
            {
                _clock.ReassertTopmost();
            }
        }

        /// <summary>把当前设置的置顶、穿透与工作/娱乐状态同步到菜单勾选项与文案。</summary>
        private void SyncMenu()
        {
            if (_menu != null && _settings != null)
            {
                _menu.Sync(_settings.Topmost, _settings.ClickThrough, _settings.CurrentMode);
            }
        }

        /// <summary>菜单“设置…”：走统一的设置弹窗入口，不另开实例。</summary>
        private void OnMenuSettings(object sender, EventArgs e)
        {
            ShowSettings();
        }

        /// <summary>
        /// 菜单切换鼠标穿透：改设置 → 应用到悬浮窗 → 同步菜单与托盘文案 → 立即保存。
        /// 从关到开时补一条托盘气泡，提醒用户仍可通过托盘菜单恢复操作。
        /// </summary>
        private void OnMenuClickThrough(object sender, EventArgs e)
        {
            if (_settings == null || _exiting)
            {
                return;
            }

            _settings.ClickThrough = !_settings.ClickThrough;
            _clock.ApplyClickThrough(_settings.ClickThrough);
            SyncMenu();
            UpdateTrayText();
            SaveNow();

            // 穿透开启后悬浮窗不再接收鼠标，必须明确告诉用户从哪里关掉。
            if (_settings.ClickThrough)
            {
                ShowClickThroughBalloon();
            }
        }

        /// <summary>菜单切换始终置顶：改设置 → 应用到悬浮窗 → 同步菜单与托盘文案 → 立即保存。</summary>
        private void OnMenuTopmost(object sender, EventArgs e)
        {
            if (_settings == null || _exiting)
            {
                return;
            }

            _settings.Topmost = !_settings.Topmost;
            _clock.ApplyTopmost(_settings.Topmost);
            SyncMenu();
            UpdateTrayText();
            SaveNow();
        }

        /// <summary>菜单切到“工作”状态，具体流程见 <see cref="SetCurrentMode"/>。</summary>
        private void OnMenuWorkMode(object sender, EventArgs e)
        {
            SetCurrentMode(ReminderModes.Work);
        }

        /// <summary>菜单切到“娱乐”状态，具体流程见 <see cref="SetCurrentMode"/>。</summary>
        private void OnMenuLeisureMode(object sender, EventArgs e)
        {
            SetCurrentMode(ReminderModes.Leisure);
        }

        /// <summary>
        /// 切换当前工作/娱乐状态：归一化后与现值比较，相同则不改也不保存，避免无意义的写盘。
        /// 变更后立即重查提醒（<c>CheckRemindersNow</c>），让新状态下的过滤规则马上生效。
        /// </summary>
        private void SetCurrentMode(string mode)
        {
            if (_settings == null || _exiting)
            {
                return;
            }

            string normalized = ReminderModes.Normalize(mode);
            // 状态未变时直接返回，连托盘文案与磁盘都不碰。
            if (string.Equals(_settings.CurrentMode, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _settings.CurrentMode = normalized;
            SyncMenu();
            UpdateTrayText();
            SaveNow();
            // 立即按新状态重算一次，否则要等下一个提醒计时器周期才按新规则过滤。
            if (_clock != null)
            {
                _clock.CheckRemindersNow();
            }
        }

        /// <summary>菜单“重置位置”：把悬浮窗放回默认角落，并防抖保存新坐标。</summary>
        private void OnMenuResetPosition(object sender, EventArgs e)
        {
            if (_clock == null || _exiting)
            {
                return;
            }
            _clock.ResetPosition();
            ScheduleSave();
        }

        /// <summary>
        /// 菜单“操作中心”：模拟 Win+N 打开 Windows 通知中心。
        /// 任务栏时钟的覆盖窗把通知按钮一起盖住了，这里给一条替代入口；
        /// 通知中心由系统自己弹出，本项目只负责转发这一次按键。
        /// </summary>
        private void OnMenuActionCenter(object sender, EventArgs e)
        {
            // 退出流程中不再制造新的系统交互。
            if (_exiting)
            {
                return;
            }
            NativeMethods.OpenActionCenter();
        }

        /// <summary>
        /// 菜单“关于”：以模态窗口显示版本号与编译号。
        /// 有设置窗口就以它为 Owner，否则挂悬浮窗，保证关于窗不会跑到主界面后面。
        /// </summary>
        private void OnMenuAbout(object sender, EventArgs e)
        {
            if (_exiting)
            {
                return;
            }

            AboutWindow about = new AboutWindow();
            if (_settingsWindow != null)
            {
                about.Owner = _settingsWindow;
            }
            else if (_clock != null)
            {
                about.Owner = _clock;
            }
            about.ShowDialog();
        }

        /// <summary>菜单“退出”：转交 <see cref="ExitApp"/>，保证退出步骤只有一处实现。</summary>
        private void OnMenuExit(object sender, EventArgs e)
        {
            ExitApp();
        }

        /// <summary>
        /// 打开设置窗口；已存在时只做恢复与激活，不重复创建。
        /// 以模态方式运行，关闭后按返回值决定是否提交；无论正常关闭还是抛异常，finally 都会清空引用并恢复置顶。
        /// 设置关闭后必须立刻让悬浮窗重新置顶，并解除置顶守护的暂停状态。
        /// </summary>
        private void ShowSettings()
        {
            if (_exiting)
            {
                return;
            }

            if (_settingsWindow != null)
            {
                // 已打开：最小化时先还原，再激活，避免只闪烁任务栏而不出窗口。
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
                _settingsWindow.Activate();
                return;
            }

            SettingsWindow dialog = new SettingsWindow(_settings);
            _settingsWindow = dialog;
            _settingsOpen = true;
            UpdateTopmostSuspension();
            dialog.ApplyRequested += OnSettingsApplyRequested;
            dialog.ReminderTestRequested += OnSettingsReminderTestRequested;

            try
            {
                bool? result = dialog.ShowDialog();
                // 只有点了“确定/应用并关闭”才提交，取消与直接关窗都保留原设置。
                if (result == true && !_exiting)
                {
                    CommitDialogSettings(dialog.Result, false);
                }
            }
            finally
            {
                // 事件必须先解绑再清引用，防止窗口销毁过程中回调打到已释放的控制器。
                dialog.ApplyRequested -= OnSettingsApplyRequested;
                dialog.ReminderTestRequested -= OnSettingsReminderTestRequested;
                _settingsWindow = null;
                _settingsOpen = false;
                UpdateTopmostSuspension();
                if (_clock != null && !_exiting)
                {
                    _clock.ReassertTopmost();
                }
            }
        }

        /// <summary>
        /// 设置窗口里点“应用”（不关闭窗口）：立即提交编辑副本。
        /// 用窗口的 Result 而不是重读磁盘，保证用户当前编辑内容生效。
        /// </summary>
        private void OnSettingsApplyRequested(object sender, EventArgs e)
        {
            SettingsWindow dialog = _settingsWindow;
            if (dialog == null || _exiting)
            {
                return;
            }
            CommitDialogSettings(dialog.Result, true);
        }

        /// <summary>
        /// 设置里点“测试提醒”：优先在用悬浮窗的提醒面板演示，悬浮窗被关掉时退回托盘气泡。
        /// 两条路径都不落盘、不进入真实提醒队列，纯粹是预览。
        /// </summary>
        private void OnSettingsReminderTestRequested(object sender, ReminderPreviewEventArgs e)
        {
            if (_clock == null || _exiting || e == null)
            {
                return;
            }

            // 悬浮窗可见时用它演示，效果与真实提醒一致，且不打扰系统通知中心。
            if (_settings != null && _settings.ShowDesktopOverlay)
            {
                _clock.ShowReminderPreview(e.Time, e.Message);
                return;
            }

            if (_tray != null && _tray.Visible)
            {
                _tray.BalloonTipTitle = e.Time + " · 时间提醒";
                _tray.BalloonTipText = e.Message;
                _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _tray.ShowBalloonTip(4200);
            }
        }

        /// <summary>
        /// 提交设置窗口的编辑副本：补齐位置 → 归一化 → 整体替换 → 逐项应用到各窗口。
        /// 提交前记录穿透、天气开关/城市、格式三项旧值，用于判断提交后是否需要清空或重取天气。
        /// <paramref name="reactivateDialog"/> 为 true 表示来自“应用”按钮，窗口还开着，需要重新提到前面。
        /// 自启写入失败不中断提交，只弹气泡提示，设置本身仍然生效。
        /// </summary>
        private void CommitDialogSettings(AppSettings updated, bool reactivateDialog)
        {
            if (updated == null || _settings == null || _clock == null || _exiting)
            {
                return;
            }

            // 先记下会影响天气展示的旧值，提交后再对比，避免无条件重新请求。
            bool previousClickThrough = _settings.ClickThrough;
            bool previousWeatherEnabled = _settings.WeatherEnabled;
            string previousWeatherCity = _settings.WeatherCity;
            bool previousTokenMode = FormatEngine.IsTokenMode(_settings.FormatMode);
            string previousTokenFormat = _settings.TokenTimeFormat;
            string previousWeatherFormat = _settings.WeatherFormat;
            AppSettings next = updated.Clone();
            // 位置以窗口实际坐标为准：设置窗口不编辑位置，拖动结果不能被旧副本覆盖。
            next.Left = _clock.Left;
            next.Top = _clock.Top;
            next.Normalize();

            _settings = next;
            // 自启写注册表可能失败，返回 false 时只提示、不回滚其他设置。
            bool autoStartApplied = AutoStartManager.SetEnabled(_settings.StartWithWindows);
            _clock.ApplySettings(_settings);
            ApplyDisplaySurfaces();
            _clock.CheckRemindersNow();
            ConfigureWeatherTimer();
            SyncMenu();
            UpdateTrayText();
            SaveNow();

            // 天气开关或城市变化要让旧数据立即失效；仅格式变化时保留数据、只重取一次。
            bool weatherChanged = previousWeatherEnabled != _settings.WeatherEnabled
                || !string.Equals(previousWeatherCity, _settings.WeatherCity, StringComparison.Ordinal);
            bool formatChanged = previousTokenMode != FormatEngine.IsTokenMode(_settings.FormatMode)
                || !string.Equals(previousTokenFormat, _settings.TokenTimeFormat, StringComparison.Ordinal)
                || !string.Equals(previousWeatherFormat, _settings.WeatherFormat, StringComparison.Ordinal);
            if (!_settings.WeatherEnabled)
            {
                // 关闭天气：清空两处展示并作废在途请求。
                _weatherGeneration++;
                _weatherInfo = null;
                _clock.SetWeather(null);
                if (_taskbarClock != null)
                {
                    _taskbarClock.SetWeather(null);
                }
            }
            else if (weatherChanged)
            {
                // 城市或开关变了：旧城市的数据必须清掉，再按新配置重取。
                _weatherInfo = null;
                _clock.SetWeather(null);
                if (_taskbarClock != null)
                {
                    _taskbarClock.SetWeather(null);
                }
                RefreshWeather();
            }
            else if (formatChanged)
            {
                // 仅格式变化：数据可复用，但要按新格式重新判断是否需要空气质量/公网 IP。
                RefreshWeather();
            }

            if (!autoStartApplied && _settings.StartWithWindows)
            {
                ShowAutoStartErrorBalloon();
            }
            // 仅在“关 → 开”时提示，重复打开设置不会反复弹同一句话。
            if (!previousClickThrough && _settings.ClickThrough)
            {
                ShowClickThroughBalloon();
            }

            if (reactivateDialog && _settingsWindow != null)
            {
                // 悬浮窗同样置顶，应用后要把设置窗口重新提到前面，否则会被悬浮窗压住。
                _settingsWindow.Activate();
            }
        }

        /// <summary>菜单或设置窗口任一打开时暂停悬浮窗的置顶守护，关闭后恢复，避免守护与弹窗抢置顶。</summary>
        private void UpdateTopmostSuspension()
        {
            if (_clock != null)
            {
                _clock.SetTopmostWatchSuspended(_menuOpen || _settingsOpen);
            }
        }

        /// <summary>提示鼠标穿透已开启以及恢复方式；托盘不可见时静默跳过。</summary>
        private void ShowClickThroughBalloon()
        {
            if (_tray == null || !_tray.Visible)
            {
                return;
            }

            _tray.BalloonTipTitle = "鼠标穿透已开启";
            _tray.BalloonTipText = "悬浮窗现在不会挡住鼠标。右键系统托盘图标即可关闭穿透。";
            _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
            _tray.ShowBalloonTip(3200);
        }

        /// <summary>开机自启写入失败时的警告气泡；只提示，不阻断设置提交。</summary>
        private void ShowAutoStartErrorBalloon()
        {
            if (_tray == null || !_tray.Visible)
            {
                return;
            }

            _tray.BalloonTipTitle = "开机自启设置失败";
            _tray.BalloonTipText = "无法写入当前用户的启动项，请检查系统权限后重试。";
            _tray.BalloonTipIcon = WinForms.ToolTipIcon.Warning;
            _tray.ShowBalloonTip(4200);
        }

        /// <summary>
        /// 刷新托盘悬停文字：状态 + 当前交互模式。
        /// Windows 托盘提示有长度上限，这里用短词拼装，穿透时给出恢复方式而不是“置顶”。
        /// </summary>
        private void UpdateTrayText()
        {
            if (_tray == null || _settings == null)
            {
                return;
            }

            string state;
            string mode = ReminderModes.DisplayName(_settings.CurrentMode);
            // 穿透优先级高于置顶：穿透时鼠标点不到悬浮窗，提示必须指向托盘恢复。
            if (_settings.ClickThrough)
            {
                state = mode + " · 穿透 · 托盘恢复";
            }
            else if (_settings.Topmost)
            {
                state = mode + " · 置顶 · 右键菜单";
            }
            else
            {
                state = mode + " · 普通 · 右键菜单";
            }
            _tray.Text = "游戏时间悬浮窗 · " + state;
        }

        /// <summary>
        /// 启动防抖保存：先停再启，等于把计时器往后续 550ms；滑动期间连续调用只会在最后一次后落盘。
        /// 计时器未建（Start 之前）或退出中直接忽略，避免退出后又写一次磁盘。
        /// </summary>
        private void ScheduleSave()
        {
            if (_saveTimer == null || _exiting)
            {
                return;
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>防抖计时器到点：先停表再保存，保证一次操作只落盘一次。</summary>
        private void OnSaveTimerTick(object sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveNow();
        }

        /// <summary>
        /// 立即保存当前设置；会顺带停掉防抖计时器，避免随后又写一遍相同内容。
        /// 设置为空（尚未载入）时直接返回。
        /// </summary>
        private void SaveNow()
        {
            if (_settings == null)
            {
                return;
            }
            if (_saveTimer != null)
            {
                _saveTimer.Stop();
            }
            SettingsStore.Save(_settings);
        }

        /// <summary>
        /// 用户主动退出：先置退出标志并落盘，再依次停天气、关设置窗、卸托盘、关两种时钟窗，最后异步 Shutdown。
        /// 关闭顺序按“依赖方先关”安排：设置窗依赖控制器，悬浮窗依赖任务栏窗，托盘依赖菜单。
        /// 用 BeginInvoke 而不是直接 Shutdown，是为了让当前事件处理先返回，避免在消息循环里自我销毁。
        /// </summary>
        private void ExitApp()
        {
            // 幂等：重复点“退出”或菜单事件重复投递时只执行一次。
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            SaveNow();
            // 作废在途天气回调，避免退出过程中还有结果回来碰已关闭的窗口。
            _weatherGeneration++;
            if (_weatherTimer != null)
            {
                _weatherTimer.Stop();
            }

            if (_settingsWindow != null)
            {
                try
                {
                    _settingsWindow.Close();
                }
                catch (InvalidOperationException)
                {
                    // 窗口可能已被用户关掉，句柄失效属于正常时序，忽略即可。
                }
                _settingsWindow = null;
            }

            if (_tray != null)
            {
                // 先隐藏再释放，否则部分系统会残留一个看不见但仍占位的图标。
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }

            if (_clock != null)
            {
                if (_taskbarClock != null)
                {
                    _taskbarClock.Stop();
                    _taskbarClock.Close();
                    _taskbarClock = null;
                }
                _clock.Close();
                _clock = null;
            }

            _app.Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    _app.Shutdown();
                }),
                DispatcherPriority.Background);
        }

        /// <summary>
        /// 宿主释放入口（Program 的 finally 调用）：停计时器、保存、关闭窗口并释放托盘与菜单。
        /// 与 <see cref="ExitApp"/> 的区别是不主动 Shutdown 应用，只做资源回收；用 <see cref="_disposed"/> 保证幂等。
        /// 每一步都判空，允许在 Startup 中途失败、只初始化了一部分的情况下安全退出。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            // 先作废天气回调，再停表，避免回调在释放过程中排队进 Dispatcher。
            _weatherGeneration++;
            if (_weatherTimer != null)
            {
                _weatherTimer.Stop();
                _weatherTimer = null;
            }

            if (_saveTimer != null)
            {
                _saveTimer.Stop();
            }
            SaveNow();

            if (_settingsWindow != null)
            {
                try
                {
                    _settingsWindow.Close();
                }
                catch (InvalidOperationException)
                {
                    // 窗口已随应用关闭，重复 Close 会抛此异常，可以直接忽略。
                }
                _settingsWindow = null;
            }

            if (_tray != null)
            {
                // 先摘掉菜单引用再释放，防止托盘销毁过程中回访已释放的菜单。
                _tray.Visible = false;
                _tray.ContextMenuStrip = null;
                _tray.Dispose();
                _tray = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            if (_menu != null)
            {
                _menu.Dispose();
                _menu = null;
            }

            if (_clock != null)
            {
                if (_taskbarClock != null)
                {
                    _taskbarClock.Stop();
                    _taskbarClock.Close();
                    _taskbarClock = null;
                }
                _clock.Close();
                _clock = null;
            }

            AlarmSound.DisposePlayer();
        }
    }
}
