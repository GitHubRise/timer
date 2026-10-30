// 进程编排层：加载设置、创建悬浮窗/任务栏时钟/托盘与右键菜单、同步天气、防抖保存、统一退出。
// 调用方：Program（启动/单实例唤醒）、SettingsWindow（应用与测试提醒）、OverlayMenu（菜单事件）。
// 边界：只做编排，不画窗口、不直接写注册表（交给 platform/），也不实现格式与提醒算法。
// 边界：窗口的创建/销毁/显示位置统一走 ApplyDisplaySurfaces，设置窗口不得直接开关窗口。
// 已知坑：全屏隐藏由 ClockWindow 每秒轮询驱动，这里不能重复 Hide/Show，否则会与轮询互相打架。
// 已知坑：天气回调是后台线程，必须 BeginInvoke 回 UI 线程，且用 generation 丢弃过期结果。
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace GameClock
{
    /// <summary>
    /// 单实例应用的控制器：持有全部窗口/托盘/定时器，负责它们的生命周期与状态同步。
    /// </summary>
    internal sealed class AppController : IDisposable
    {
        // 应用与设置：_settings 是当前生效的配置对象，保存时直接序列化它。
        private readonly Application _app;
        private AppSettings _settings;
        // 显示面：桌面悬浮窗与任务栏时钟各自独立，可单独关闭；_taskbarClock 为 null 表示尚未创建。
        private ClockWindow _clock;
        private TaskbarClockWindow _taskbarClock;
        // 天气缓存：_weatherInfo 为 null 表示暂无数据；_weatherGeneration 用于丢弃过期回调。
        private WeatherInfo _weatherInfo;
        // 托盘与菜单：_tray 可能不可用（图标加载失败时仍有回退图标），_menu 与悬浮窗共用右键菜单。
        private OverlayMenu _menu;
        private WinForms.NotifyIcon _tray;
        private System.Drawing.Icon _trayIcon;
        // 设置窗口与保存/天气定时器：_saveTimer 做 550ms 防抖，_weatherTimer 按设置周期刷新。
        private SettingsWindow _settingsWindow;
        private DispatcherTimer _saveTimer;
        private DispatcherTimer _weatherTimer;
        private int _weatherGeneration;
        // 交互/退出标志：菜单或设置窗口打开时暂停置顶复位；_exiting 一旦置位不再接受新操作。
        private bool _menuOpen;
        private bool _settingsOpen;
        private bool _exiting;
        private bool _disposed;

        /// <summary>只保存 WPF Application 引用；真正的初始化在 <see cref="Start"/> 中完成。</summary>
        public AppController(Application app)
        {
            _app = app;
        }

        /// <summary>
        /// 处理第二实例的唤醒请求：主窗口已就绪则直接打开设置窗口；退出过程中忽略。
        /// </summary>
        public void ShowSettingsFromExternal()
        {
            // 尚未完成启动（_clock 为 null）或正在退出时，没有可激活的 UI，静默忽略并记录原因。
            if (_exiting || _clock == null)
            {
                AppLog.Warn("App", "收到外部唤醒请求，但应用未就绪或正在退出，忽略本次请求");
                return;
            }
            ShowSettings();
        }

        /// <summary>
        /// 启动流程：读设置 → 应用开机自启 → 建菜单/悬浮窗/托盘 → 应用显示面 → 启动天气刷新。
        /// 只调用一次，由 Program 在主窗口创建后执行。
        /// </summary>
        public void Start()
        {
            // 加载失败时 SettingsStore 返回默认配置，这里无法区分，后续保存会覆盖损坏文件。
            _settings = SettingsStore.Load();
            // 开机自启以设置为准重新同步一次，修正注册表被外部改动的情况。
            AutoStartManager.SetEnabled(_settings.StartWithWindows);
            // 保存防抖：鼠标拖动/滚轮缩放会连续触发，550ms 内的多次请求合并为一次落盘。
            _saveTimer = new DispatcherTimer(DispatcherPriority.Background);
            _saveTimer.Interval = TimeSpan.FromMilliseconds(550);
            _saveTimer.Tick += OnSaveTimerTick;

            // 右键菜单：所有点击都转成事件回到控制器，菜单本身不碰窗口与设置。
            _menu = new OverlayMenu();
            _menu.SettingsClicked += OnMenuSettings;
            _menu.ClickThroughClicked += OnMenuClickThrough;
            _menu.TopmostClicked += OnMenuTopmost;
            _menu.WorkModeClicked += OnMenuWorkMode;
            _menu.LeisureModeClicked += OnMenuLeisureMode;
            _menu.ResetPositionClicked += OnMenuResetPosition;
            _menu.AboutClicked += OnMenuAbout;
            _menu.ExitClicked += OnMenuExit;
            _menu.MenuOpening += OnMenuOpening;
            _menu.MenuOpened += OnMenuOpened;
            _menu.MenuClosed += OnMenuClosed;

            // 桌面悬浮窗：先创建并立即 Show，再由 ApplyDisplaySurfaces 按设置决定显示还是隐藏。
            _clock = new ClockWindow(_settings);
            _clock.MenuRequested += OnClockMenuRequested;
            _clock.PositionChanged += OnClockPositionChanged;
            _clock.ScaleChanged += OnClockScaleChanged;
            _clock.ReminderTriggered += OnClockReminderTriggered;
            _clock.ScheduleTriggered += OnClockScheduleTriggered;
            _clock.Show();
            ApplyDisplaySurfaces();

            // 托盘图标与菜单文字：图标失败时 LoadApplicationIcon 会回退到系统默认图标。
            CreateTrayIcon();
            SyncMenu();
            UpdateTrayText();
            _clock.ReassertTopmost();

            // 天气刷新：先按设置配置周期，再立即拉取一次；关闭天气时 ConfigureWeatherTimer 只停表。
            _weatherTimer = new DispatcherTimer(DispatcherPriority.Background);
            _weatherTimer.Interval = TimeSpan.FromMinutes(30.0);
            _weatherTimer.Tick += OnWeatherTimerTick;
            ConfigureWeatherTimer();
            RefreshWeather();

            // 启动完成汇总：一条日志给出四个开关的最终状态，便于对照配置文件排查。
            AppLog.Info(
                "App",
                "启动完成：桌面悬浮窗=" + (_settings.ShowDesktopOverlay ? "开" : "关")
                    + "，任务栏时钟=" + (_settings.ShowTaskbarClock ? "开" : "关")
                    + "，天气=" + (_settings.WeatherEnabled ? "开" : "关")
                    + "，开机自启=" + (_settings.StartWithWindows ? "开" : "关"));
        }

        /// <summary>
        /// 按当前设置应用显示面：桌面悬浮窗显示/隐藏、任务栏时钟创建/更新/停止。
        /// 设置窗口点「应用」、启动与旧配置迁移后都会调用。
        /// </summary>
        private void ApplyDisplaySurfaces()
        {
            // 设置未加载、正在退出或悬浮窗已销毁时，没有可应用的显示面，直接跳过。
            if (_settings == null || _exiting || _clock == null)
            {
                return;
            }

            // 桌面悬浮窗：开启且当前不可见时显示；关闭且当前可见时隐藏。
            if (_settings.ShowDesktopOverlay)
            {
                // 只在不可见时 Show，避免重复 Show 把全屏隐藏状态打断。
                if (!_clock.IsVisible)
                {
                    _clock.Show();
                    AppLog.Info("App", "显示桌面悬浮窗（设置已开启该显示位置）");
                }
                _clock.ReassertTopmost();
            }
            else if (_clock.IsVisible)
            {
                // 关闭显示位置时只隐藏窗口，不销毁，重新开启时无需重建。
                _clock.Hide();
                AppLog.Info("App", "隐藏桌面悬浮窗（设置已关闭该显示位置）");
            }

            // 任务栏时钟：首次需要时创建，之后复用窗口对象并热更新设置。
            if (_settings.ShowTaskbarClock)
            {
                // 首次启用：创建覆盖窗并完成一次完整初始化。
                if (_taskbarClock == null)
                {
                    _taskbarClock = new TaskbarClockWindow(_settings);
                    AppLog.Info("App", "创建任务栏时钟窗口");
                }
                else
                {
                    // 已存在：只热更新字体/格式/位置，避免任务栏窗口闪烁重建。
                    _taskbarClock.ApplySettings(_settings);
                }
                _taskbarClock.SetWeather(_settings.WeatherEnabled ? _weatherInfo : null);
                _taskbarClock.Start();
            }
            else if (_taskbarClock != null)
            {
                // 关闭任务栏时钟：停计时器并把系统时钟移回原位，窗口对象保留以便再次启用。
                _taskbarClock.Stop();
                AppLog.Info("App", "停止任务栏时钟并恢复系统时钟");
            }
        }

        /// <summary>
        /// 按设置调整天气刷新周期；天气关闭时只停表，不清空已显示的数据（由 RefreshWeather 负责）。
        /// </summary>
        private void ConfigureWeatherTimer()
        {
            // 定时器尚未创建（Start 早期）或已释放时没有可配置的对象。
            if (_weatherTimer == null)
            {
                return;
            }

            // 先停表再决定是否重启，避免改周期后旧定时器继续按原间隔触发。
            _weatherTimer.Stop();
            // 天气关闭或设置未加载：停表即可，不重启刷新周期。
            if (_settings == null || !_settings.WeatherEnabled)
            {
                return;
            }

            // 周期收敛到白名单值（分钟），防止配置被手改成 0 或超大值导致疯狂请求。
            int minutes = WeatherRefreshOptions.Normalize(_settings.WeatherRefreshMinutes);
            _weatherTimer.Interval = TimeSpan.FromMinutes(minutes);
            _weatherTimer.Start();
        }

        /// <summary>
        /// 创建托盘图标并绑定共用右键菜单；图标从 exe 关联图标提取，失败回退系统默认图标。
        /// </summary>
        private void CreateTrayIcon()
        {
            _trayIcon = LoadApplicationIcon();

            _tray = new WinForms.NotifyIcon();
            _tray.Icon = _trayIcon;
            _tray.Text = "游戏时间悬浮窗";
            _tray.Visible = true;
            // 托盘与悬浮窗共用同一个菜单对象，状态由 SyncMenu 统一回填。
            _tray.ContextMenuStrip = _menu.Menu;
            _tray.DoubleClick += OnTrayDoubleClick;
        }

        /// <summary>
        /// 提取 exe 关联图标；任何失败都回退到系统默认应用图标，保证托盘一定有图标。
        /// </summary>
        private System.Drawing.Icon LoadApplicationIcon()
        {
            // 从 exe 内嵌资源提取图标；单文件发布/受限目录/资源损坏都可能失败，整体兜底。
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                // 提取成功才用 exe 自带图标，否则继续走系统默认图标。
                if (icon != null)
                {
                    return icon;
                }
            }
            catch (Exception ex)
            {
                // 单文件/受限目录/图标资源损坏都可能抛异常；托盘图标缺失不影响核心功能，
                // 但会降低可发现性，因此记录后回退到系统默认图标。
                AppLog.Warn("App", "提取程序图标失败，回退到系统默认图标：" + ex.Message);
            }
            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        }

        /// <summary>托盘图标双击：打开设置窗口（单实例唤醒也走这里）。</summary>
        private void OnTrayDoubleClick(object sender, EventArgs e)
        {
            ShowSettings();
        }

        /// <summary>悬浮窗请求右键菜单：菜单已创建且未退出时在鼠标位置弹出。</summary>
        private void OnClockMenuRequested(object sender, EventArgs e)
        {
            // 退出过程中不再弹出菜单，避免菜单项作用在已销毁的窗口上。
            if (_menu != null && !_exiting)
            {
                _menu.ShowAtCursor();
            }
        }

        /// <summary>悬浮窗被拖动后触发：只安排防抖保存，不立即写盘。</summary>
        private void OnClockPositionChanged(object sender, EventArgs e)
        {
            ScheduleSave();
        }

        /// <summary>悬浮窗滚轮缩放字号后触发：同样走防抖保存，避免每格滚轮都写盘。</summary>
        private void OnClockScaleChanged(object sender, EventArgs e)
        {
            ScheduleSave();
        }

        /// <summary>天气定时器到点：重新发起一次异步拉取。</summary>
        private void OnWeatherTimerTick(object sender, EventArgs e)
        {
            RefreshWeather();
        }

        /// <summary>
        /// 发起天气刷新：关闭天气时清空显示；开启时按格式需求决定是否附带空气质量/公网 IP。
        /// 回调可能在后台线程，必须切回 UI 线程并用 generation 丢弃过期结果。
        /// </summary>
        private void RefreshWeather()
        {
            // 悬浮窗尚未创建时没有地方显示天气，等 Start 完成后再刷。
            if (_clock == null)
            {
                return;
            }
            // 天气关闭或正在退出：递增 generation 让在途请求作废，并清空两处显示。
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

            // 每次请求分配新代号；回调回来时代号不一致说明已被更新的请求取代，直接丢弃。
            int generation = ++_weatherGeneration;
            string city = _settings.WeatherCity;
            // 只拼接当前格式实际需要的部分（token 模式取 TokenTimeFormat，否则为空），
            // 再用 FormatEngine 判断是否需要空气质量与公网 IP，避免无谓的网络请求。
            string tokenFormat = FormatEngine.IsTokenMode(_settings.FormatMode)
                ? _settings.TokenTimeFormat
                : string.Empty;
            string combinedFormat = tokenFormat + "\n" + (_settings.WeatherFormat ?? string.Empty);
            WeatherFetchOptions options = new WeatherFetchOptions();
            options.IncludeAirQuality = FormatEngine.RequiresAirQuality(combinedFormat);
            options.IncludePublicIp = FormatEngine.RequiresPublicIp(combinedFormat);
            // 异步回调：不在此处碰 UI 控件，只把结果通过 Dispatcher 投递回 UI 线程。
            WeatherService.FetchAsync(city, options, delegate(WeatherInfo info)
            {
                // 应用已退出或已释放：不再排队任何 UI 操作，避免访问已销毁对象。
                if (_exiting || _disposed)
                {
                    return;
                }

                // Dispatcher 可能已随应用关闭，BeginInvoke 抛异常在此兜底，避免后台线程崩溃。
                try
                {
                    _app.Dispatcher.BeginInvoke(
                        new Action(delegate
                        {
                            // 第二次检查：排队期间可能已退出、释放或被新请求取代。
                            if (_exiting || _disposed || generation != _weatherGeneration)
                            {
                                return;
                            }
                            // 设置已关闭天气或窗口已销毁：本次结果不再应用。
                            if (_settings == null || !_settings.WeatherEnabled || _clock == null)
                            {
                                return;
                            }
                            // 成功：缓存并同时更新悬浮窗与任务栏时钟（后者可能尚未创建）。
                            if (info != null)
                            {
                                _weatherInfo = info;
                                _clock.SetWeather(info);
                                if (_taskbarClock != null)
                                {
                                    _taskbarClock.SetWeather(info);
                                }
                            }
                            else
                            {
                                // 失败回调约定用 null 表示；保留上一次天气，只记录一次失败原因。
                                AppLog.Warn("App", "天气获取失败（城市=" + city + "），本次不更新天气显示");
                            }
                        }),
                        DispatcherPriority.Background);
                }
                catch (InvalidOperationException ex)
                {
                    // Dispatcher 已关闭（应用正在退出）时 BeginInvoke 会抛异常；
                    // 此时已无 UI 可更新，记录后直接放弃本次结果。
                    AppLog.Warn("App", "刷新天气后切回 UI 线程失败（应用可能正在退出）：" + ex.Message);
                }
            });
        }

        /// <summary>
        /// 悬浮窗提醒到点：先落盘最近触发时间（防重启后重复提醒），再用托盘气泡提示。
        /// 气泡只是补充，悬浮窗自身也会显示提醒。
        /// </summary>
        private void OnClockReminderTriggered(object sender, ReminderEventArgs e)
        {
            // 触发信息里带 LastTriggeredTime，必须尽快写盘，避免退出时丢失去重依据。
            SaveNow();
            // 托盘不可用（极端环境）时只保留窗口提醒，不因气泡失败影响主流程。
            if (_tray != null && _tray.Visible && e != null && e.Reminder != null)
            {
                _tray.BalloonTipTitle = e.Reminder.Time + " · 时间提醒";
                _tray.BalloonTipText = e.Reminder.Message;
                _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _tray.ShowBalloonTip(5200);
                AppLog.Info("Reminder", "触发提醒 " + e.Reminder.Time + "：" + e.Reminder.Message);
            }
        }

        /// <summary>
        /// 日程到点：保存 LastTriggeredKey 后用托盘气泡提示；宽限窗口内的重复 tick 由 ClockWindow 去重。
        /// </summary>
        private void OnClockScheduleTriggered(object sender, ScheduleTriggeredEventArgs e)
        {
            // 与提醒一致，先去重信息落盘，再展示气泡。
            SaveNow();
            // 托盘不可用时只保留悬浮窗提醒，不因气泡失败影响主流程。
            if (_tray != null && _tray.Visible && e != null && e.Schedule != null)
            {
                _tray.BalloonTipTitle = ScheduleCategories.DisplayName(e.Schedule.Type)
                    + "日程 · "
                    + e.Occurrence.ToString("MM-dd HH:mm");
                _tray.BalloonTipText = e.Schedule.Title;
                _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _tray.ShowBalloonTip(5200);
                AppLog.Info("Schedule", "触发日程提醒（" + ScheduleCategories.DisplayName(e.Schedule.Type) + "）：" + e.Schedule.Title + "，发生时间 " + e.Occurrence.ToString("yyyy-MM-dd HH:mm"));
            }
        }

        /// <summary>菜单即将展开：重新同步勾选状态，保证界面与当前设置一致。</summary>
        private void OnMenuOpening(object sender, EventArgs e)
        {
            SyncMenu();
        }

        /// <summary>菜单展开后暂停悬浮窗置顶复位，避免定时器把菜单压到悬浮窗下面。</summary>
        private void OnMenuOpened(object sender, EventArgs e)
        {
            _menuOpen = true;
            UpdateTopmostSuspension();
        }

        /// <summary>菜单关闭：恢复置顶复位，并立即把悬浮窗拉回最前（若设置窗口未打开）。</summary>
        private void OnMenuClosed(object sender, EventArgs e)
        {
            _menuOpen = false;
            UpdateTopmostSuspension();
            // 设置窗口打开时由设置窗口占据焦点，此时不抢置顶；其余情况立即复位。
            if (_clock != null && !_exiting && !_settingsOpen)
            {
                _clock.ReassertTopmost();
            }
        }

        /// <summary>把置顶、穿透、工作/娱乐状态回填到菜单勾选与文字。</summary>
        private void SyncMenu()
        {
            // 菜单或设置任一未就绪（启动早期/退出后）时跳过，避免空引用。
            if (_menu != null && _settings != null)
            {
                _menu.Sync(_settings.Topmost, _settings.ClickThrough, _settings.CurrentMode);
            }
        }

        /// <summary>菜单「设置…」：打开或激活设置窗口。</summary>
        private void OnMenuSettings(object sender, EventArgs e)
        {
            ShowSettings();
        }

        /// <summary>
        /// 菜单「鼠标穿透」：翻转开关、立即应用到窗口、同步菜单/托盘文字并保存；
        /// 开启时额外弹一次托盘提示，告知如何恢复交互。
        /// </summary>
        private void OnMenuClickThrough(object sender, EventArgs e)
        {
            // 设置未加载或正在退出时不接受新的交互操作。
            if (_settings == null || _exiting)
            {
                return;
            }

            _settings.ClickThrough = !_settings.ClickThrough;
            _clock.ApplyClickThrough(_settings.ClickThrough);
            SyncMenu();
            UpdateTrayText();
            SaveNow();

            // 开启穿透后悬浮窗不再接收鼠标，必须提示用户可从托盘关闭。
            if (_settings.ClickThrough)
            {
                ShowClickThroughBalloon();
            }
            AppLog.Info("App", "鼠标穿透已" + (_settings.ClickThrough ? "开启" : "关闭"));
        }

        /// <summary>菜单「始终置顶」：翻转开关并立即应用，保存后立即生效。</summary>
        private void OnMenuTopmost(object sender, EventArgs e)
        {
            // 与穿透一致：退出/未初始化时忽略点击。
            if (_settings == null || _exiting)
            {
                return;
            }

            _settings.Topmost = !_settings.Topmost;
            _clock.ApplyTopmost(_settings.Topmost);
            SyncMenu();
            UpdateTrayText();
            SaveNow();
            AppLog.Info("App", "始终置顶已" + (_settings.Topmost ? "开启" : "关闭"));
        }

        /// <summary>菜单「工作」：切换到工作状态，提醒按状态过滤。</summary>
        private void OnMenuWorkMode(object sender, EventArgs e)
        {
            SetCurrentMode(ReminderModes.Work);
        }

        /// <summary>菜单「娱乐」：切换到娱乐状态，提醒按状态过滤。</summary>
        private void OnMenuLeisureMode(object sender, EventArgs e)
        {
            SetCurrentMode(ReminderModes.Leisure);
        }

        /// <summary>
        /// 切换工作/娱乐状态：相同状态直接忽略；切换后刷新菜单/托盘、落盘并立即重查提醒。
        /// </summary>
        private void SetCurrentMode(string mode)
        {
            // 未初始化或退出中不切换状态，避免保存到已作废的对象上。
            if (_settings == null || _exiting)
            {
                return;
            }

            string normalized = ReminderModes.Normalize(mode);
            // 已是目标状态时不做任何事，防止重复写盘和重复弹提醒。
            if (string.Equals(_settings.CurrentMode, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _settings.CurrentMode = normalized;
            SyncMenu();
            UpdateTrayText();
            SaveNow();
            // 状态变化会改变提醒过滤条件，立即重查一次让新状态下该弹的提醒马上弹。
            if (_clock != null)
            {
                _clock.CheckRemindersNow();
            }
            AppLog.Info("App", "当前状态切换为 " + ReminderModes.DisplayName(normalized));
        }

        /// <summary>菜单「重置位置」：把悬浮窗放回默认角落并安排一次防抖保存。</summary>
        private void OnMenuResetPosition(object sender, EventArgs e)
        {
            // 悬浮窗不存在或正在退出时没有可重置的位置。
            if (_clock == null || _exiting)
            {
                return;
            }
            _clock.ResetPosition();
            ScheduleSave();
        }

        /// <summary>菜单「关于」：以设置窗口/悬浮窗为 owner 弹出模态关于框。</summary>
        private void OnMenuAbout(object sender, EventArgs e)
        {
            // 退出过程中不再创建新窗口，避免关闭流程与模态循环互相等待。
            if (_exiting)
            {
                return;
            }

            AboutWindow about = new AboutWindow();
            // 优先挂到设置窗口，保证关于框显示在设置之上；否则挂悬浮窗。
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

        /// <summary>菜单「退出」：走统一退出流程（保存、销毁托盘与窗口、关闭应用）。</summary>
        private void OnMenuExit(object sender, EventArgs e)
        {
            ExitApp();
        }

        /// <summary>
        /// 打开设置窗口；已打开时从最小化恢复并激活，避免出现多个设置实例。
        /// 模态期间暂停置顶复位，关闭后恢复。
        /// </summary>
        private void ShowSettings()
        {
            // 退出过程中不再打开新窗口，否则关闭流程会与模态循环卡住。
            if (_exiting)
            {
                return;
            }

            // 已有设置窗口：只恢复/激活，不重复创建，保护未提交的编辑内容。
            if (_settingsWindow != null)
            {
                // 最小化时先恢复正常尺寸，否则 Activate 不会把窗口带到前台。
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
                _settingsWindow.Activate();
                return;
            }

            // 设置窗口编辑的是副本：构造函数内部克隆 _settings，只有「应用/确定」才回传。
            SettingsWindow dialog = new SettingsWindow(_settings);
            _settingsWindow = dialog;
            _settingsOpen = true;
            UpdateTopmostSuspension();
            dialog.ApplyRequested += OnSettingsApplyRequested;
            dialog.ReminderTestRequested += OnSettingsReminderTestRequested;

            try
            {
                // 模态显示：阻塞到用户确定/取消；确定时 Result 为编辑后的副本。
                bool? result = dialog.ShowDialog();
                // 用户点「确定」且未在模态期间退出时提交；取消则丢弃编辑副本。
                if (result == true && !_exiting)
                {
                    CommitDialogSettings(dialog.Result, false);
                }
            }
            finally
            {
                // 无论正常关闭还是异常退出模态，都必须解绑事件并清空引用。
                dialog.ApplyRequested -= OnSettingsApplyRequested;
                dialog.ReminderTestRequested -= OnSettingsReminderTestRequested;
                _settingsWindow = null;
                _settingsOpen = false;
                UpdateTopmostSuspension();
                // 设置窗口已关闭，悬浮窗重新抢回置顶（退出中则跳过）。
                if (_clock != null && !_exiting)
                {
                    _clock.ReassertTopmost();
                }
            }
        }

        /// <summary>设置窗口点「应用」：立即提交当前编辑副本，但保持窗口打开。</summary>
        private void OnSettingsApplyRequested(object sender, EventArgs e)
        {
            SettingsWindow dialog = _settingsWindow;
            // 窗口已关闭/正在退出时忽略迟到的应用请求，避免把过期副本写回。
            if (dialog == null || _exiting)
            {
                return;
            }
            CommitDialogSettings(dialog.Result, true);
        }

        /// <summary>
        /// 设置窗口点「测试提醒」：桌面悬浮窗可见时在窗口内预览，否则退回托盘气泡。
        /// </summary>
        private void OnSettingsReminderTestRequested(object sender, ReminderPreviewEventArgs e)
        {
            // 悬浮窗不存在、退出中或参数为空时没有可预览的内容。
            if (_clock == null || _exiting || e == null)
            {
                return;
            }

            // 优先在真实显示位置预览，用户能看到最终字号/颜色效果。
            if (_settings != null && _settings.ShowDesktopOverlay)
            {
                _clock.ShowReminderPreview(e.Time, e.Message);
                return;
            }

            // 悬浮窗关闭时退回托盘气泡，至少能确认提醒文案与声音链路。
            if (_tray != null && _tray.Visible)
            {
                _tray.BalloonTipTitle = e.Time + " · 时间提醒";
                _tray.BalloonTipText = e.Message;
                _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _tray.ShowBalloonTip(4200);
            }
        }

        /// <summary>
        /// 提交设置窗口的编辑副本：记录改动前的关键字段 → 应用窗口/显示面/天气/自启 → 保存。
        /// 先记旧值是为了判断天气城市或格式是否变化，从而决定是否重新拉取天气。
        /// </summary>
        /// <param name="updated">设置窗口的编辑副本（会再 Clone 一次，避免后续界面修改污染已生效配置）。</param>
        /// <param name="reactivateDialog">为 true 表示「应用」后窗口仍打开，需要重新激活它。</param>
        private void CommitDialogSettings(AppSettings updated, bool reactivateDialog)
        {
            // 任何一项缺失都说明调用时机不对（弹窗已关/退出中），直接放弃提交。
            if (updated == null || _settings == null || _clock == null || _exiting)
            {
                return;
            }

            // 记录提交前的天气/格式相关字段，用于判断是否需要刷新天气数据。
            bool previousClickThrough = _settings.ClickThrough;
            bool previousWeatherEnabled = _settings.WeatherEnabled;
            string previousWeatherCity = _settings.WeatherCity;
            bool previousTokenMode = FormatEngine.IsTokenMode(_settings.FormatMode);
            string previousTokenFormat = _settings.TokenTimeFormat;
            string previousWeatherFormat = _settings.WeatherFormat;
            AppSettings next = updated.Clone();
            // 位置以窗口当前实际位置为准：设置窗口打开期间用户可能拖动过悬浮窗。
            next.Left = _clock.Left;
            next.Top = _clock.Top;
            next.Normalize();

            _settings = next;
            // 开机自启写入失败时保存仍继续，但会记日志并在下方弹一次错误气泡。
            bool autoStartApplied = AutoStartManager.SetEnabled(_settings.StartWithWindows);
            _clock.ApplySettings(_settings);
            ApplyDisplaySurfaces();
            _clock.CheckRemindersNow();
            ConfigureWeatherTimer();
            SyncMenu();
            UpdateTrayText();
            SaveNow();

            // 天气相关变化：开关或城市变了要清空缓存并重拉；只改格式则复用旧数据重绘。
            bool weatherChanged = previousWeatherEnabled != _settings.WeatherEnabled
                || !string.Equals(previousWeatherCity, _settings.WeatherCity, StringComparison.Ordinal);
            bool formatChanged = previousTokenMode != FormatEngine.IsTokenMode(_settings.FormatMode)
                || !string.Equals(previousTokenFormat, _settings.TokenTimeFormat, StringComparison.Ordinal)
                || !string.Equals(previousWeatherFormat, _settings.WeatherFormat, StringComparison.Ordinal);
            if (!_settings.WeatherEnabled)
            {
                // 关闭天气：作废在途请求并清空两处显示，避免残留旧城市数据。
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
                // 开关/城市变化：旧数据已失效，先清空再立即按新参数拉取。
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
                // 仅格式变化：缓存仍有效，但需要重新计算空气质量/IP 需求，重新拉取最稳妥。
                RefreshWeather();
            }

            // 自启失败：AutoStartManager 已记 Warn，这里只补一个用户可见的气泡提示。
            if (!autoStartApplied && _settings.StartWithWindows)
            {
                ShowAutoStartErrorBalloon();
            }
            // 本次提交把穿透从关改成开：提示一次如何恢复。
            if (!previousClickThrough && _settings.ClickThrough)
            {
                ShowClickThroughBalloon();
            }

            // 「应用」后窗口仍要留在前台；悬浮窗也是置顶窗口，不激活会被压在下面。
            if (reactivateDialog && _settingsWindow != null)
            {
                // The overlay is topmost as well; make sure the settings window
                // stays above it immediately after live-applying a change.
                _settingsWindow.Activate();
            }
            AppLog.Info(
                "App",
                "设置已应用并保存：桌面悬浮窗=" + (_settings.ShowDesktopOverlay ? "开" : "关")
                    + "，任务栏时钟=" + (_settings.ShowTaskbarClock ? "开" : "关")
                    + "，天气=" + (_settings.WeatherEnabled ? "开" : "关")
                    + "，穿透=" + (_settings.ClickThrough ? "开" : "关")
                    + "，置顶=" + (_settings.Topmost ? "开" : "关"));
        }

        /// <summary>
        /// 菜单或设置窗口打开时暂停悬浮窗置顶复位；两者都关闭后恢复。
        /// 否则每 3 秒的置顶会把设置窗口/菜单压到悬浮窗下面。
        /// </summary>
        private void UpdateTopmostSuspension()
        {
            // 悬浮窗尚未创建时无需设置；创建后 Start 末尾会立即复位一次置顶。
            if (_clock != null)
            {
                _clock.SetTopmostWatchSuspended(_menuOpen || _settingsOpen);
            }
        }

        /// <summary>托盘提示：穿透已开启以及如何恢复鼠标交互。</summary>
        private void ShowClickThroughBalloon()
        {
            // 托盘不可用时静默跳过，穿透仍可通过右键菜单关闭（如果鼠标还能到窗口）。
            if (_tray == null || !_tray.Visible)
            {
                return;
            }

            _tray.BalloonTipTitle = "鼠标穿透已开启";
            _tray.BalloonTipText = "悬浮窗现在不会挡住鼠标。右键系统托盘图标即可关闭穿透。";
            _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
            _tray.ShowBalloonTip(3200);
        }

        /// <summary>托盘提示：开机自启写入注册表失败时的可见回退说明。</summary>
        private void ShowAutoStartErrorBalloon()
        {
            // 没有托盘图标就没有提示渠道；失败详情已在 AutoStartManager 的 Warn 日志中。
            if (_tray == null || !_tray.Visible)
            {
                return;
            }

            _tray.BalloonTipTitle = "开机自启设置失败";
            _tray.BalloonTipText = "无法写入当前用户的启动项，请检查系统权限后重试。";
            _tray.BalloonTipIcon = WinForms.ToolTipIcon.Warning;
            _tray.ShowBalloonTip(4200);
        }

        /// <summary>刷新托盘图标提示文字：状态 + 当前恢复交互的方式（穿透/置顶/普通）。</summary>
        private void UpdateTrayText()
        {
            // 托盘或设置未就绪时无法拼出状态文字，等 Start 完成后再更新。
            if (_tray == null || _settings == null)
            {
                return;
            }

            string state;
            string mode = ReminderModes.DisplayName(_settings.CurrentMode);
            // 穿透优先展示（此时窗口不接收鼠标，恢复方式最重要），其次置顶，最后普通模式。
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
        /// 安排一次防抖保存：550ms 内的连续拖动/缩放只触发最后一次写盘。
        /// </summary>
        private void ScheduleSave()
        {
            // 定时器未创建（启动早期）或正在退出时忽略，退出路径会显式 SaveNow。
            if (_saveTimer == null || _exiting)
            {
                return;
            }
            // 重置计时：每次新变动都把落盘时间往后推 550ms。
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>防抖定时器到点：停表并立即保存一次。</summary>
        private void OnSaveTimerTick(object sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveNow();
        }

        /// <summary>
        /// 立即保存当前设置；保存失败只记录日志，不抛出异常打断用户操作。
        /// </summary>
        private void SaveNow()
        {
            // 设置尚未加载（构造后、Start 前）时没有可保存的内容。
            if (_settings == null)
            {
                return;
            }
            // 先停防抖定时器：本次已经落盘，不能让 550ms 后再写一遍。
            if (_saveTimer != null)
            {
                _saveTimer.Stop();
            }
            bool saved = SettingsStore.Save(_settings);
            // 写盘失败通常意味着 %APPDATA% 不可写或磁盘满；配置仍留在内存中，但重启会丢失。
            if (!saved)
            {
                AppLog.Warn("App", "保存设置失败，本次改动未落盘（%APPDATA%\\GameClock\\settings.json 可能不可写）");
            }
        }

        /// <summary>
        /// 退出流程：先停所有后台刷新并保存，再关闭设置窗口、托盘、任务栏时钟与悬浮窗，最后关闭应用。
        /// 幂等：_exiting 置位后重复调用直接返回。
        /// </summary>
        private void ExitApp()
        {
            // 重复退出（菜单点击 + 窗口关闭）只执行一次，避免重复 Dispose 抛异常。
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            AppLog.Info("App", "开始退出：保存设置并销毁托盘与窗口");
            SaveNow();
            // 作废在途天气回调；停表后再关窗口，防止回调访问已销毁的 ClockWindow。
            _weatherGeneration++;
            if (_weatherTimer != null)
            {
                _weatherTimer.Stop();
            }

            // 先关设置窗口：模态窗口不关掉，应用无法正常退出。
            if (_settingsWindow != null)
            {
                // 模态窗口可能已被系统关闭，Close 抛异常时继续退出，不能让它阻断保存与托盘释放。
                try
                {
                    _settingsWindow.Close();
                }
                catch (InvalidOperationException ex)
                {
                    // 窗口可能已被用户关闭（句柄已销毁），此时 Close 会抛异常；
                    // 目标（让窗口消失）已达成，记录后继续退出流程。
                    AppLog.Warn("App", "退出时关闭设置窗口失败（窗口可能已被关闭）：" + ex.Message);
                }
                _settingsWindow = null;
            }

            // 先隐藏再释放托盘图标，避免图标残留在通知区域直到鼠标划过。
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }

            // 先停任务栏覆盖窗（恢复系统时钟）再关悬浮窗，最后才关闭 WPF 应用。
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

            // 投递到 Dispatcher 再 Shutdown：避免在菜单/定时器回调栈内直接关闭应用。
            _app.Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    _app.Shutdown();
                }),
                DispatcherPriority.Background);
        }

        /// <summary>
        /// 释放控制器全部资源；与 ExitApp 的区别是不关闭 WPF Application，供应用退出事件调用。
        /// 幂等，且在没有托盘图标（启动早期失败）时也能安全执行。
        /// </summary>
        public void Dispose()
        {
            // 幂等保护：App 退出与控制器手动释放可能重复调用。
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            // 先作废天气回调并停表，再保存与关窗口，保证释放期间没有后台线程回写 UI。
            _weatherGeneration++;
            if (_weatherTimer != null)
            {
                _weatherTimer.Stop();
                _weatherTimer = null;
            }

            // 防抖保存定时器一并停掉，避免释放后定时器再触发一次保存。
            if (_saveTimer != null)
            {
                _saveTimer.Stop();
            }
            SaveNow();

            // 关闭仍打开的设置窗口；异常原因同 ExitApp（窗口可能已被销毁）。
            if (_settingsWindow != null)
            {
                // 模态窗口可能已被系统关闭，Close 抛异常时继续退出，不能让它阻断保存与托盘释放。
                try
                {
                    _settingsWindow.Close();
                }
                catch (InvalidOperationException ex)
                {
                    // 窗口在退出事件触发前已被用户关闭时，Close 会抛异常；
                    // 释放流程继续执行，仅记录异常信息用于排查。
                    AppLog.Warn("App", "释放时关闭设置窗口失败（窗口可能已被关闭）：" + ex.Message);
                }
                _settingsWindow = null;
            }

            // 释放托盘：先断开菜单引用再 Dispose，防止 NotifyIcon 释放时反向操作已释放的菜单。
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.ContextMenuStrip = null;
                _tray.Dispose();
                _tray = null;
            }

            // 图标对象独立于 NotifyIcon，必须单独释放，否则进程退出前会泄漏 GDI 句柄。
            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            // 菜单之后再释放窗口：窗口的右键菜单可能仍引用该对象。
            if (_menu != null)
            {
                _menu.Dispose();
                _menu = null;
            }

            // 关闭显示面：先恢复任务栏系统时钟，再关悬浮窗。
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

            // 提醒声音播放器是进程级单例，最后统一释放。
            AlarmSound.DisposePlayer();
        }
    }
}
