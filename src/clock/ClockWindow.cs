// 桌面悬浮时钟窗：无边框、透明、置顶的时钟显示层。
// 由 AppController 创建并持有，设置变更统一走 ApplySettings 下发；窗口只负责渲染与交互，
// 不落盘、不碰托盘、不直接关开关，保证“显示位置”的开关只有控制器一处决定。
// 约束：分层窗口按像素命中，_root 用 alpha 极低的透明填充让整块区域可拖、可右键；
// WM_MOUSEACTIVATE 一律返回 MA_NOACTIVATE，点击只作用于本窗口，不抢前台焦点。
// 已知坑：全屏隐藏只 Hide()/Show()，计时器不停，否则提醒与日程到点会被漏掉。

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace GameClock
{

    /// <summary>
    /// 桌面悬浮时钟窗口。
    /// 生命周期：由 <see cref="AppController"/> 在启用「桌面悬浮窗」时创建并 Show()，
    /// 关闭开关或退出程序时 Close()；进入全屏时 Hide()、退出全屏时 Show()，实例始终复用。
    /// 控件树与计时器全在构造期建好，运行期只改属性，不重建视觉树。
    /// </summary>
    internal sealed class ClockWindow : Window
    {
        // ===== 布局常量 =====

        /// <summary>默认停靠（右上角）时距屏幕边缘的留白，单位为设备无关像素。</summary>
        private const double DefaultMargin = 26.0;

        // ===== 构造期依赖 =====

        /// <summary>当前生效的设置对象；与控制器共享同一引用，窗口只读大部分字段，仅回写位置与字号。</summary>
        private AppSettings _settings;

        /// <summary>按 <see cref="AppSettings.TimeZoneId"/> 解析出的时区，所有“现在”都由它换算。</summary>
        private TimeZoneInfo _timeZone;

        // ===== 窗口与视觉元素 =====

        /// <summary>最外层容器：提供透明命中背景、内边距，并承载拖动/右键/滚轮三个输入事件。</summary>
        private readonly Border _root;

        /// <summary>纵向排列时钟、天气、提醒气泡的栈，尺寸由内容决定（SizeToContent）。</summary>
        private readonly StackPanel _stack;

        /// <summary>主时钟文字，使用分层渲染以支持阴影与 3D 挤出。</summary>
        private readonly LayeredText _clockText;

        /// <summary>天气附加行；默认折叠，只有开关打开且拿到文本时才显示。</summary>
        private readonly TextBlock _weatherText;

        /// <summary>提醒气泡卡片的外框（圆角、深色背景、投影），整块显示/隐藏。</summary>
        private readonly Border _reminderPanel;

        /// <summary>气泡首行：触发时刻 + 来源标签（如「提醒」「生日日程」「测试提醒」）。</summary>
        private readonly TextBlock _reminderTimeText;

        /// <summary>气泡正文：提醒内容，自动换行，限制最大宽度避免撑爆屏幕宽度。</summary>
        private readonly TextBlock _reminderMessageText;

        // ===== 计时器 =====

        /// <summary>时钟刷新计时器，100 ms 一跳；同时承担提醒与日程的到点检查。</summary>
        private readonly DispatcherTimer _clockTimer;

        /// <summary>置顶守护计时器，3 s 一次，把窗口重新抬回 TOPMOST 组。</summary>
        private readonly DispatcherTimer _topmostTimer;

        /// <summary>提醒停留计时器；每次显示气泡时按设置重设间隔，到点自动收起。</summary>
        private readonly DispatcherTimer _reminderTimer;

        /// <summary>鼠标悬停气泡的轮询计时器，150 ms 采样一次光标位置。</summary>
        private readonly DispatcherTimer _reminderHoverTimer;

        /// <summary>全屏检测计时器，1 s 一次；只做前台窗口判定，不影响业务计时。</summary>
        private readonly DispatcherTimer _fullscreenTimer;

        // ===== 提醒队列 =====

        /// <summary>待播提醒队列；气泡可见期间新到的提醒先排队，收起后逐条播放，避免互相覆盖。</summary>
        private readonly Queue<ReminderNotice> _reminderQueue;

        // ===== 窗口过程与状态标志 =====

        /// <summary>窗口消息钩子的持有者；关闭时必须解挂，否则窗口释放后仍会被回调。</summary>
        private HwndSource _source;

        /// <summary>是否已完成首次 Loaded；未加载前句柄与尺寸都不可用，多数应用设置的操作要跳过。</summary>
        private bool _loaded;

        /// <summary>位置是否已恢复并可用于回写；复位期间用它屏蔽 LocationChanged 的重复上报。</summary>
        private bool _positionReady;

        /// <summary>是否正在程序内改 Left/Top；为 true 时忽略 LocationChanged，避免自触发保存与递归。</summary>
        private bool _applyingPosition;

        /// <summary>是否临时暂停置顶守护；其它窗口自己管理 TOPMOST 时由控制器打开，避免互相抢层。</summary>
        private bool _topmostWatchSuspended;

        /// <summary>当前是否因全屏而隐藏；只有它记录的状态才允许恢复显示，防止误 Show 打断全屏。</summary>
        private bool _hiddenForFullscreen;

        /// <summary>提醒气泡是否正在显示；决定新提醒是立即播放还是入队。</summary>
        private bool _reminderVisible;

        /// <summary>鼠标开始悬停气泡的时刻（UTC）；离开或收起时清空，用于累计 3 秒。</summary>
        private DateTime? _reminderHoverStartedUtc;

        /// <summary>上一次到点检查的分钟键；同一分钟内不再重复检查提醒与日程，避免每 100 ms 生成字符串。</summary>
        private long _lastDueCheckKey = -1;

        /// <summary>上一次写入的时钟文本；内容没变就跳过赋值，减少布局与重绘抖动。</summary>
        private string _lastDisplayText;

        /// <summary>最近一次天气数据；为 null 表示还没取到或已清空，格式里的天气变量会退化为空。</summary>
        private WeatherInfo _weatherInfo;

        // ===== 对外事件（由控制器订阅） =====

        /// <summary>请求弹出共用右键菜单；控制器负责菜单内容与执行。</summary>
        public event EventHandler MenuRequested;

        /// <summary>窗口位置被用户拖动或程序纠正后触发，供控制器防抖保存。</summary>
        public event EventHandler PositionChanged;

        /// <summary>滚轮缩放字号后触发，供控制器防抖保存字号。</summary>
        public event EventHandler ScaleChanged;

        /// <summary>某条提醒到点触发；携带原始对象，控制器据此做日志或联动。</summary>
        public event EventHandler<ReminderEventArgs> ReminderTriggered;

        /// <summary>某个日程到达发生时刻并已展示气泡后触发。</summary>
        public event EventHandler<ScheduleTriggeredEventArgs> ScheduleTriggered;

        /// <summary>
        /// 构造窗口，按设置搭好外观、控件树与五个计时器。
        /// 此处只创建对象、不显示；首次 Show() 后才由 <see cref="OnLoaded"/> 恢复位置并启动计时。
        /// </summary>
        /// <param name="settings">控制器持有的设置；为 null 时回退默认值，避免早调用或设计期构造抛空引用。</param>
        public ClockWindow(AppSettings settings)
        {
            // 旧配置可能缺字段，先归一化再读属性，避免下面取到越界字号或非法时区。
            _settings = settings ?? AppSettings.CreateDefault();
            _settings.Normalize();
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            // 窗口外观：无边框 + 透明背景 + 不进任务栏 + 不激活，保证 Show() 不抢当前焦点。
            Title = "GameClockOverlayWindow";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = _settings.Topmost;
            // 尺寸随内容变化，拖动与缩放后无需手工算宽高。
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            // 先完全透明，等 OnLoaded 落位后再给目标不透明度，避免首帧闪现在左上角。
            Opacity = 0.0;

            // 有保存位置就先落到该坐标；可用性由 RestorePosition 在 Loaded 后复核。
            if (_settings.Left.HasValue && _settings.Top.HasValue)
            {
                Left = _settings.Left.Value;
                Top = _settings.Top.Value;
            }

            _root = new Border();
            // 分层窗口按像素做命中测试，alpha=1 的近乎全透明填充让整块时钟区域都可拖、可右键。
            _root.Background = UiTheme.BrushFrom(Color.FromArgb(1, 0, 0, 0));
            // 抓手光标提示可拖动；真正的拖动在 _root.MouseLeftButtonDown 里调 DragMove。
            _root.Cursor = Cursors.SizeAll;
            _root.Padding = new Thickness(18, 10, 18, 14);

            // 内容纵向居中：时钟 → 天气 → 提醒气泡，后加者叠在下方。
            _stack = new StackPanel();
            _stack.HorizontalAlignment = HorizontalAlignment.Center;
            _stack.VerticalAlignment = VerticalAlignment.Center;

            // 时钟文字不参与命中测试，鼠标事件统一落到 _root，文字不会挡住拖动。
            _clockText = new LayeredText();
            _clockText.IsHitTestVisible = false;
            _stack.Children.Add(_clockText);

            // 天气行：默认折叠，拿到数据且开关打开后才由 UpdateWeatherVisibility 显示。
            _weatherText = new TextBlock();
            _weatherText.FontFamily = new FontFamily("Microsoft YaHei UI");
            _weatherText.FontSize = 15;
            _weatherText.HorizontalAlignment = HorizontalAlignment.Center;
            _weatherText.TextAlignment = TextAlignment.Center;
            _weatherText.TextWrapping = TextWrapping.NoWrap;
            _weatherText.IsHitTestVisible = false;
            _weatherText.Visibility = Visibility.Collapsed;
            _weatherText.Margin = new Thickness(0, 4, 0, 0);
            _stack.Children.Add(_weatherText);

            // 气泡首行：字号小于正文、半粗，作为“时刻 · 来源”的次要信息。
            _reminderTimeText = new TextBlock();
            _reminderTimeText.FontFamily = new FontFamily("Microsoft YaHei UI");
            _reminderTimeText.FontSize = 11;
            _reminderTimeText.FontWeight = FontWeights.SemiBold;
            _reminderTimeText.HorizontalAlignment = HorizontalAlignment.Center;
            _reminderTimeText.TextAlignment = TextAlignment.Center;

            // 气泡正文：自动换行并限制最大宽度，长提醒不会把窗口横向拉出屏幕。
            _reminderMessageText = new TextBlock();
            _reminderMessageText.FontFamily = new FontFamily("Microsoft YaHei UI");
            _reminderMessageText.FontSize = 14;
            _reminderMessageText.FontWeight = FontWeights.Normal;
            _reminderMessageText.Foreground = UiTheme.TextPrimary;
            _reminderMessageText.TextWrapping = TextWrapping.Wrap;
            _reminderMessageText.TextAlignment = TextAlignment.Center;
            _reminderMessageText.HorizontalAlignment = HorizontalAlignment.Center;
            _reminderMessageText.MaxWidth = 430;
            _reminderMessageText.Margin = new Thickness(0, 3, 0, 0);

            // 两行文字先装进内层栈，再整体作为圆角卡片的唯一子元素。
            StackPanel reminderContent = new StackPanel();
            reminderContent.Children.Add(_reminderTimeText);
            reminderContent.Children.Add(_reminderMessageText);

            // 气泡卡片：深色不透明底 + 细边框 + 投影，避免与透明窗口叠加时文字发灰。
            _reminderPanel = new Border();
            _reminderPanel.Background = UiTheme.BrushFrom(Color.FromArgb(222, 18, 18, 24));
            _reminderPanel.BorderThickness = new Thickness(1);
            _reminderPanel.CornerRadius = new CornerRadius(9);
            _reminderPanel.Padding = new Thickness(14, 9, 14, 10);
            _reminderPanel.Margin = new Thickness(0, 10, 0, 0);
            _reminderPanel.MaxWidth = 460;
            _reminderPanel.HorizontalAlignment = HorizontalAlignment.Center;
            _reminderPanel.Visibility = Visibility.Collapsed;
            // 投影方向 315°、短深度，模拟左上光源，和时钟文字的阴影方向保持一致。
            _reminderPanel.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 315,
                ShadowDepth = 3,
                BlurRadius = 8,
                Opacity = 0.55
            };
            _reminderPanel.Child = reminderContent;
            _stack.Children.Add(_reminderPanel);

            _root.Child = _stack;
            Content = _root;

            // 提醒串行播放：同一时刻只显示一条，其余排队。
            _reminderQueue = new Queue<ReminderNotice>();

            // 时钟 100 ms 一跳：秒与毫秒刷新要跟手，同时顺带检查提醒、日程是否到点。
            _clockTimer = new DispatcherTimer(DispatcherPriority.Background);
            _clockTimer.Interval = TimeSpan.FromMilliseconds(100);
            _clockTimer.Tick += OnClockTick;

            // 置顶守护 3 s 一次：别的置顶窗口会把悬浮窗压下去，需要周期性重新抬起。
            _topmostTimer = new DispatcherTimer(DispatcherPriority.Background);
            _topmostTimer.Interval = TimeSpan.FromSeconds(3.0);
            _topmostTimer.Tick += OnTopmostTick;

            // 提醒停留计时器：12 s 只是占位间隔，真正时长在 ShowReminder 里按设置重设。
            _reminderTimer = new DispatcherTimer(DispatcherPriority.Background);
            _reminderTimer.Interval = TimeSpan.FromSeconds(12.0);
            _reminderTimer.Tick += OnReminderTimerTick;

            // 悬停关闭轮询 150 ms：足够灵敏，又不至于每帧都取一次光标坐标。
            _reminderHoverTimer = new DispatcherTimer(DispatcherPriority.Background);
            _reminderHoverTimer.Interval = TimeSpan.FromMilliseconds(150);
            _reminderHoverTimer.Tick += OnReminderHoverTick;

            // 全屏检测 1 s 一次：兼顾响应速度与查询开销，只在 Tick 里做前台窗口判定。
            _fullscreenTimer = new DispatcherTimer(DispatcherPriority.Background);
            _fullscreenTimer.Interval = TimeSpan.FromSeconds(1.0);
            _fullscreenTimer.Tick += OnFullscreenTick;

            // 窗口与输入事件挂在 _root 而不是窗口本身：透明窗口只在有内容的像素上命中。
            Loaded += OnLoaded;
            Closed += OnClosed;
            LocationChanged += OnLocationChanged;
            SizeChanged += OnSizeChanged;
            _root.MouseLeftButtonDown += OnMouseLeftButtonDown;
            _root.MouseRightButtonUp += OnMouseRightButtonUp;
            _root.MouseWheel += OnMouseWheel;

            // 先套视觉再刷新时间，保证首帧就是最终字号与颜色（此时还没 Show，不会闪旧样式）。
            ApplyVisuals();
            UpdateClock(true);
        }

        /// <summary>
        /// 应用一整套新设置：换引用、重算时区、重刷视觉与提醒样式，并同步窗口样式与置顶。
        /// 未 Loaded 时只做与句柄无关的部分，其余留给首次显示后的初始化。
        /// </summary>
        /// <param name="settings">控制器已归一化的设置；为 null 直接忽略，避免清空当前状态。</param>
        public void ApplySettings(AppSettings settings)
        {
            // 空引用说明控制器尚未准备好设置，保持现状比退回默认值更安全。
            if (settings == null)
            {
                return;
            }

            _settings = settings;
            _settings.Normalize();
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);
            // 时区或提醒列表可能整体换了，清掉分钟键让下一次计时器回调立即补查一遍。
            _lastDueCheckKey = -1;

            // 未加载时没有有效句柄，只能更新纯 WPF 视觉；位置与 Win32 样式等 Loaded 后再补。
            if (!_loaded)
            {
                ApplyVisuals();
                RefreshWeatherText();
                return;
            }

            ApplyVisuals();
            RefreshWeatherText();
            Opacity = _settings.Opacity;
            Topmost = _settings.Topmost;
            // 气泡正在显示时应用新设置，需要按新时长重排收起计时，不能沿用旧间隔。
            ApplyReminderDisplaySettings();

            // 关掉“全屏隐藏”后要立刻恢复显示，否则窗口会一直停在上一次 Hide() 的状态。
            if (!_settings.HideOnFullscreen)
            {
                RestoreFromFullscreen();
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // 鼠标穿透与置顶都靠扩展样式实现，改完 WPF 属性后必须再通知一次 Win32。
            NativeMethods.ApplyOverlayStyles(hwnd, _settings.ClickThrough);
            NativeMethods.SetTopmost(hwnd, _settings.Topmost);

            // 字号或内容变了会改变窗口尺寸，排到布局完成后再纠正位置，避免用到旧的 ActualWidth。
            UpdateLayout();
            Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    // 布局完成后再纠偏并回写，此时 ActualWidth/ActualHeight 才是最终值。
                    EnsureVisible();
                    CapturePosition();
                }),
                DispatcherPriority.Background);
        }

        /// <summary>
        /// 单独切换鼠标穿透（托盘菜单或设置页可直接调用，无需整体重应用）。
        /// 未加载时只记到设置里，等 OnLoaded 统一下发。
        /// </summary>
        /// <param name="enabled">true = 点击落到下层窗口，悬浮窗只显示不响应。</param>
        public void ApplyClickThrough(bool enabled)
        {
            _settings.ClickThrough = enabled;
            // 没有句柄时 SetClickThrough 无从下手，值已存下，Loaded 后 ApplyOverlayStyles 会带上。
            if (!_loaded)
            {
                return;
            }
            NativeMethods.SetClickThrough(new WindowInteropHelper(this).Handle, enabled);
        }

        /// <summary>
        /// 接收后台天气回调；null 表示取数失败或已关闭，此时格式里的天气变量退化为空。
        /// </summary>
        /// <param name="info">天气结果，可为 null。</param>
        public void SetWeather(WeatherInfo info)
        {
            _weatherInfo = info;
            RefreshWeatherText();
        }

        /// <summary>
        /// 单独切换置顶（托盘菜单用）；与 ApplySettings 的差异是只动置顶，不重刷视觉。
        /// </summary>
        /// <param name="enabled">true = 保持 TOPMOST。</param>
        public void ApplyTopmost(bool enabled)
        {
            _settings.Topmost = enabled;
            // 未加载时句柄无效，等 OnLoaded 后再统一设置。
            if (!_loaded)
            {
                return;
            }

            Topmost = enabled;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetTopmost(hwnd, enabled);
        }

        /// <summary>
        /// 重新声明一次置顶，供控制器在其它窗口关闭后调用，避免悬浮窗被压在下面。
        /// 只在已加载且确实开启置顶时执行，避免把用户主动取消的置顶又打开。
        /// </summary>
        public void ReassertTopmost()
        {
            // 关闭置顶或尚未加载时无事可做：前者是用户选择，后者没有句柄。
            if (!_loaded || !_settings.Topmost)
            {
                return;
            }
            NativeMethods.SetTopmost(new WindowInteropHelper(this).Handle, true);
        }

        /// <summary>
        /// 临时暂停或恢复置顶守护；其它窗口（如任务栏覆盖窗）需要自己控制层级时使用。
        /// </summary>
        /// <param name="suspended">true = 3 秒定时器不再抢 TOPMOST。</param>
        public void SetTopmostWatchSuspended(bool suspended)
        {
            _topmostWatchSuspended = suspended;
        }

        /// <summary>
        /// 立即补一次提醒与日程检查；设置页“测试”或系统休眠唤醒后由控制器调用。
        /// 以当前时区时间判断，避免跨时区时用错“今天”。
        /// </summary>
        public void CheckRemindersNow()
        {
            // 未加载时计时器还没跑，此时检查可能重复播报，等 Loaded 后再补。
            if (_loaded)
            {
                DateTime now = TimeZoneCatalog.GetNow(_timeZone);
                // 记下当前分钟键：紧接着的计时器回调不再把同一分钟再查一遍。
                _lastDueCheckKey = GetDueCheckKey(now);
                CheckDueReminders(now);
                CheckDueSchedules(now);
            }
        }

        /// <summary>
        /// 在悬浮窗上试播一条提醒（设置页预览），走与真实提醒相同的队列与样式。
        /// 时间文本非法时退化为「--:--」，保证预览不因格式问题静默失败。
        /// </summary>
        /// <param name="time">预览时刻文本，可空。</param>
        /// <param name="message">预览正文；为空则直接忽略，避免弹出空气泡。</param>
        public void ShowReminderPreview(string time, string message)
        {
            // 没有正文的预览没有意义，直接跳过。
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string normalizedTime;
            // 先按项目时间格式规范化；失败时保留原始输入（去空白），仍为空则显示占位符。
            if (!TimeFormatter.TryNormalizeTime(time, out normalizedTime))
            {
                normalizedTime = string.IsNullOrWhiteSpace(time) ? "--:--" : time.Trim();
            }
            EnqueueReminder(new ReminderNotice(normalizedTime, message.Trim(), true));
        }

        /// <summary>
        /// 把窗口位置复位到默认右上角，并通知控制器保存。
        /// 托盘菜单与设置页“恢复默认位置”共用此入口。
        /// </summary>
        public void ResetPosition()
        {
            ResetPositionToDefault();
            CapturePosition();
            // 复位同样是一次位置变更，必须通知控制器，否则重启后又会回到旧坐标。
            if (PositionChanged != null)
            {
                PositionChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 首次显示后的初始化：恢复位置、补下发 Win32 样式、启动计时器。
        /// 位置必须等布局算出真实宽高后才能校验可见区域，因此放在 UpdateLayout 之后。
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;
            // 先刷视觉再布局，用的才是最终字号与字体，量出的宽高才准。
            ApplyVisuals();
            UpdateLayout();
            RestorePosition();
            UpdateLayout();
            // 位置定稿后才允许回写，避免恢复过程中的 LocationChanged 把中间值存进设置。
            _positionReady = true;
            CapturePositionQuietly();
            Opacity = _settings.Opacity;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // 构造函数里句柄尚未创建，扩展样式必须在这里补一次。
            NativeMethods.ApplyOverlayStyles(hwnd, _settings.ClickThrough);
            NativeMethods.SetTopmost(hwnd, _settings.Topmost);

            UpdateClock(true);
            RefreshWeatherText();
            // 只在此处启动计时器：全屏隐藏/恢复只调 Hide()/Show()，不停表，提醒才不会漏。
            _clockTimer.Start();
            _topmostTimer.Start();
            _fullscreenTimer.Start();
        }

        /// <summary>
        /// 窗口关闭时收尾：停掉全部计时器并解挂窗口消息钩子。
        /// 钩子不解挂会持有窗口引用，进程内反复开关悬浮窗时会泄漏。
        /// </summary>
        private void OnClosed(object sender, EventArgs e)
        {
            _clockTimer.Stop();
            _topmostTimer.Stop();
            _reminderTimer.Stop();
            _reminderHoverTimer.Stop();
            _fullscreenTimer.Stop();
            // 消息钩子持有 this 引用，不解挂会让窗口无法回收。
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source = null;
            }
        }

        /// <summary>
        /// 句柄创建时挂上窗口消息钩子，并先应用一次扩展样式。
        /// 早于 Loaded 触发，是唯一能拿到 HWND 的可靠时机。
        /// </summary>
        /// <param name="e">WPF 事件参数，透传给基类。</param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // 这里先设一次，兜住“显示前就被其它代码查询样式”的情况。
            NativeMethods.ApplyOverlayStyles(hwnd, _settings.ClickThrough);

            // 只在首次挂接，重复调用会重复收消息。
            if (_source == null)
            {
                _source = HwndSource.FromHwnd(hwnd);
                // 极端情况下取不到消息源，此时跳过挂接，窗口仍能正常显示。
                if (_source != null)
                {
                    _source.AddHook(WndProc);
                }
            }
        }

        /// <summary>
        /// 窗口消息钩子：拦截鼠标激活消息，一律回 MA_NOACTIVATE。
        /// 这样点击悬浮窗只触发拖动或右键，不会把前台应用切走。
        /// </summary>
        /// <returns>已处理返回 0 并置 <paramref name="handled"/>；其余消息交回默认处理。</returns>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 只关心激活消息，其它消息保持原样，避免影响 WPF 自身的输入处理。
            if (msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// 左键按下开始拖动窗口。
        /// 拖动由 WPF 的 DragMove 接管，松手后立刻纠正位置并回写，保证不会拖到屏幕外仍被保存。
        /// </summary>
        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 鼠标穿透模式下窗口不该响应任何输入；这里再拦一次，防止样式设置滞后时误拖。
            if (_settings.ClickThrough)
            {
                return;
            }

            // 只处理“左键按下且仍按住”的完整手势，避免右键或已释放的残事件触发拖动。
            if (e.ChangedButton == MouseButton.Left && e.LeftButton == MouseButtonState.Pressed)
            {
                e.Handled = true;
                // 交给 WPF 处理拖动，拖动结束后再纠偏并回写位置。
                try
                {
                    DragMove();
                }
                // 拖动被打断属于正常交互，没有需要清理的状态（原因见下方）。
                catch (InvalidOperationException)
                {
                    // 拖动期间鼠标被提前释放、或窗口正在关闭时 DragMove 会抛；没有可恢复动作，
                    // 且位置仍在下方纠正，因此可以安全吞掉。
                }
                EnsureVisible();
                CapturePosition();
            }
        }

        /// <summary>
        /// 右键抬起时请求弹出共用菜单；菜单内容与执行由控制器负责。
        /// </summary>
        private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 穿透模式不弹菜单，保持“看得见但点不到”的语义。
            if (_settings.ClickThrough)
            {
                return;
            }

            e.Handled = true;
            // 没有订阅者时静默忽略，窗口仍可正常拖动。
            if (MenuRequested != null)
            {
                MenuRequested(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 滚轮缩放字号：上滚 +4、下滚 -4，并夹在设置允许的范围内。
        /// 到边界时不改值也不发事件，避免控制器收到无效变更去写盘。
        /// </summary>
        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 穿透模式不处理；Delta 为 0 的合成事件也没有方向可判断。
            if (_settings.ClickThrough || e.Delta == 0)
            {
                return;
            }

            double step = e.Delta > 0 ? 4.0 : -4.0;
            double next = _settings.FontSize + step;
            // 夹取到统一上下限，防止设置里出现超大字号把窗口撑出屏幕。
            next = Math.Max(AppSettings.MinFontSize, Math.Min(AppSettings.MaxFontSize, next));
            // 已到边界：差值小于 0.01 视为没变，直接返回，不触发重绘与保存。
            if (Math.Abs(next - _settings.FontSize) < 0.01)
            {
                return;
            }

            e.Handled = true;
            _settings.FontSize = next;
            ApplyVisuals();
            // 只通知控制器；是否落盘、何时落盘由控制器防抖决定。
            if (ScaleChanged != null)
            {
                ScaleChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 全屏检测定时回调：按前台窗口是否全屏决定隐藏或恢复。
        /// 开关关闭时只负责恢复，不做任何判定，避免残留的隐藏状态卡住窗口。
        /// </summary>
        private void OnFullscreenTick(object sender, EventArgs e)
        {
            // 未加载时窗口还没显示，Hide/Show 都没有意义。
            if (!_loaded)
            {
                return;
            }

            // 用户关掉“全屏隐藏”后，若之前正因全屏而隐藏，必须立刻恢复。
            if (!_settings.HideOnFullscreen)
            {
                RestoreFromFullscreen();
                return;
            }

            bool fullscreen = NativeMethods.IsFullscreenForeground();

            // 命中全屏只隐藏窗口，计时器与提醒检查都继续运行。
            if (fullscreen)
            {
                // 只在“可见且尚未标记”时 Hide 一次，避免每秒重复调用。
                if (IsVisible && !_hiddenForFullscreen)
                {
                    _hiddenForFullscreen = true;
                    Hide();
                }
            }
            // 退出全屏且之前确实隐藏过，才恢复显示。
            else if (_hiddenForFullscreen)
            {
                RestoreFromFullscreen();
            }
        }

        /// <summary>
        /// 从全屏隐藏状态恢复：显示窗口、恢复透明度，并重新下发穿透/置顶样式。
        /// 多次调用是幂等的，未处于隐藏状态时直接返回。
        /// </summary>
        private void RestoreFromFullscreen()
        {
            // 没有隐藏过就不要 Show()，否则会把用户主动隐藏的窗口重新弹出来。
            if (!_hiddenForFullscreen)
            {
                return;
            }

            _hiddenForFullscreen = false;
            Show();
            // Show() 后 WPF 可能重置部分视觉属性，这里补回不透明度。
            Opacity = _settings.Opacity;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // Hide/Show 会重建部分窗口状态，扩展样式与置顶需要重新声明。
            NativeMethods.ApplyOverlayStyles(hwnd, _settings.ClickThrough);
            NativeMethods.SetTopmost(hwnd, _settings.Topmost);
            ReassertTopmost();
        }

        /// <summary>
        /// 窗口位置变化回调：把新位置回写设置并通知控制器保存。
        /// 初始化阶段与程序内主动改坐标时都要跳过，否则会把中间值写进配置。
        /// </summary>
        private void OnLocationChanged(object sender, EventArgs e)
        {
            // _positionReady 之前是恢复位置的过程；_applyingPosition 期间是本类自己改坐标。
            if (!_positionReady || _applyingPosition)
            {
                return;
            }

            CapturePosition();
            // 位置有变，交给控制器决定何时落盘。
            if (PositionChanged != null)
            {
                PositionChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 尺寸变化回调：缩放字号或天气行显隐都会走到这里。
        /// 用后台优先级延后纠正位置，等布局稳定后再读 ActualWidth/ActualHeight。
        /// </summary>
        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 位置尚未就绪时窗口还在初始化，此时纠正会与 RestorePosition 打架。
            if (!_positionReady)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                new Action(KeepInsideScreenAfterResize),
                DispatcherPriority.Background);
        }

        /// <summary>
        /// 尺寸变化后把窗口压回虚拟屏幕内，尽量保持左上角不动、只在越界边纠正。
        /// 与 <see cref="EnsureVisible"/> 的区别：这里保证整块可见，而非“至少露出一条边”。
        /// </summary>
        private void KeepInsideScreenAfterResize()
        {
            // 延迟执行期间窗口可能已关闭或又进入初始化，重新确认一次。
            if (!_positionReady)
            {
                return;
            }

            Rect virtualScreen = GetVirtualScreen();
            double width = Math.Max(ActualWidth, 1.0);
            double height = Math.Max(ActualHeight, 1.0);
            double left = Left;
            double top = Top;

            // 横向：能整块放下才做右/左贴边纠正，放不下就靠左对齐，避免来回跳。
            if (width <= virtualScreen.Width - 8.0)
            {
                // 右边越界就贴右，保留 4 像素边距。
                if (left + width > virtualScreen.Right - 4.0)
                {
                    left = virtualScreen.Right - width - 4.0;
                }
                // 左边越界同理贴左，防止整窗滑出屏幕。
                if (left < virtualScreen.Left + 4.0)
                {
                    left = virtualScreen.Left + 4.0;
                }
            }
            // 窗口比整个虚拟屏幕还宽时无法左右纠偏，直接靠左对齐。
            else
            {
                left = virtualScreen.Left + 4.0;
            }

            // 纵向同理：留 4 像素边距，防止贴边后被系统判定为完全不可见。
            if (height <= virtualScreen.Height - 8.0)
            {
                // 下边越界就上移，保留 4 像素底边。
                if (top + height > virtualScreen.Bottom - 4.0)
                {
                    top = virtualScreen.Bottom - height - 4.0;
                }
                // 上边越界则下移，保证顶部内容可见。
                if (top < virtualScreen.Top + 4.0)
                {
                    top = virtualScreen.Top + 4.0;
                }
            }
            // 窗口比屏幕还高时只能靠上对齐。
            else
            {
                top = virtualScreen.Top + 4.0;
            }

            // 只有真正需要移动时才写坐标，避免每帧都触发 LocationChanged 与保存。
            if (Math.Abs(left - Left) > 0.1 || Math.Abs(top - Top) > 0.1)
            {
                SetWindowPosition(left, top);
                CapturePosition();
                // 位置有变，交给控制器决定何时落盘。
                if (PositionChanged != null)
                {
                    PositionChanged(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 把时刻折算成「分钟键」：同一天同一分钟得到同一个值，用于到点检查去重。
        /// 只做整数运算，不生成任何字符串，可安全地每 100 ms 调用一次。
        /// </summary>
        private static long GetDueCheckKey(DateTime now)
        {
            return (now.Date.Ticks / TimeSpan.TicksPerMinute) + (now.Hour * 60) + now.Minute;
        }

        /// <summary>
        /// 时钟计时器回调（100 ms）：刷新显示，并在进入新的一分钟时检查提醒与日程。
        /// 用同一份“当前时区时间”做三件事，避免跨秒时各分支取到不同时刻。
        /// </summary>
        private void OnClockTick(object sender, EventArgs e)
        {
            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            UpdateClock(now, false);

            // 提醒与日程的最小粒度是分钟：同一分钟内重复检查只会白跑日期解析。
            long dueKey = GetDueCheckKey(now);
            if (dueKey == _lastDueCheckKey)
            {
                return;
            }

            _lastDueCheckKey = dueKey;
            CheckDueReminders(now);
            CheckDueSchedules(now);
        }

        /// <summary>
        /// 置顶守护回调：把窗口重新抬回 TOPMOST 组。
        /// 只在可见、已加载、确实开启置顶且未被暂停时执行，避免与全屏隐藏互相拉扯。
        /// </summary>
        private void OnTopmostTick(object sender, EventArgs e)
        {
            // 不可见时抬顶没有意义，还会把刚 Hide() 的窗口重新带回前台。
            if (!_loaded || _topmostWatchSuspended || !_settings.Topmost || !IsVisible)
            {
                return;
            }
            NativeMethods.SetTopmost(new WindowInteropHelper(this).Handle, true);
        }

        /// <summary>
        /// 把设置里的字体、颜色、阴影、3D 开关套到时钟文字与提醒气泡上。
        /// 每次字号/配色变更都会调用，末尾强制刷新一次时钟文本以适配新度量。
        /// </summary>
        private void ApplyVisuals()
        {
            Color color = _settings.TimeColor;
            FontFamily family = FontCatalog.GetFontFamily(_settings.FontFamilyName);

            // 时钟主文字：颜色、字号、半粗、阴影与 3D 全部由设置决定，3D 关闭时分层文字自动隐藏挤出层。
            _clockText.ApplyStyle(
                family,
                _settings.FontSize,
                FontWeights.SemiBold,
                color,
                _settings.ShowShadow,
                _settings.Text3D);

            // 气泡首行跟时钟同色；边框取同时钟的半透明色，保证主题一致又不喧宾夺主。
            _reminderTimeText.Foreground = UiTheme.BrushFrom(color);
            _reminderPanel.BorderBrush = UiTheme.BrushFrom(
                Color.FromArgb(130, color.R, color.G, color.B));

            // 天气行：字号/字体/粗细/斜体独立配置；行距随字号微调，避免大写字母贴住时钟。
            double weatherSize = _settings.WeatherFontSize;
            Color weatherColor = _settings.WeatherColor;
            _weatherText.FontFamily = FontCatalog.GetFontFamily(_settings.WeatherFontFamilyName);
            _weatherText.FontSize = weatherSize;
            _weatherText.FontWeight = _settings.WeatherFontBold ? FontWeights.Bold : FontWeights.Normal;
            _weatherText.FontStyle = _settings.WeatherFontItalic ? FontStyles.Italic : FontStyles.Normal;
            _weatherText.Margin = new Thickness(0, Math.Max(2.0, weatherSize * 0.20), 0, 0);
            // 天气文字略带透明（232/255），视觉上弱于时钟主行。
            _weatherText.Foreground = UiTheme.BrushFrom(
                Color.FromArgb(232, weatherColor.R, weatherColor.G, weatherColor.B));
            // 阴影像素尺寸也随字号缩放；深色文字配白影、浅色文字配黑影，保证任何背景都读得清。
            if (_settings.WeatherShowShadow)
            {
                _weatherText.Effect = new DropShadowEffect
                {
                    Color = ColorUtil.Luminance(weatherColor) < 0.22 ? Colors.White : Colors.Black,
                    Direction = 315,
                    ShadowDepth = Math.Max(1.0, weatherSize * 0.10),
                    BlurRadius = Math.Max(2.0, weatherSize * 0.20),
                    Opacity = 0.78
                };
            }
            // 关闭阴影时显式置空，旧的 DropShadowEffect 不会自动消失。
            else
            {
                _weatherText.Effect = null;
            }

            UpdateWeatherVisibility();

            // 清空缓存文本，强制下一次 UpdateClock 重新赋值，让新字体度量立即生效。
            _lastDisplayText = null;
            UpdateClock(true);
        }

        /// <summary>
        /// 根据开关与当前文本决定天气行显隐；变化时重排布局并把窗口拉回屏幕内。
        /// 天气行显隐会改变窗口高度，所以必须重新纠正位置。
        /// </summary>
        private void UpdateWeatherVisibility()
        {
            // 同时要求开关打开且确实有文本：取数失败时保持折叠，不留空白行。
            bool show = _settings.WeatherEnabled && !string.IsNullOrWhiteSpace(_weatherText.Text);
            Visibility target = show ? Visibility.Visible : Visibility.Collapsed;
            // 只有显隐真正变化才重排，避免每次刷新都触发布局。
            if (_weatherText.Visibility != target)
            {
                _weatherText.Visibility = target;
                // 未加载时布局还没建立，等 OnLoaded 统一处理即可。
                if (_loaded)
                {
                    UpdateLayout();
                    // 高度变了可能越界，延后到布局完成后纠正。
                    Dispatcher.BeginInvoke(
                        new Action(EnsureVisible),
                        DispatcherPriority.Background);
                }
            }
        }

        /// <summary>
        /// 按当前时区取“现在”并刷新时钟文本；计时器与设置变更都走这个重载。
        /// </summary>
        /// <param name="force">true = 即使文本相同也重新赋值（字体/字号变更后需要）。</param>
        private void UpdateClock(bool force)
        {
            UpdateClock(TimeZoneCatalog.GetNow(_timeZone), force);
        }

        /// <summary>
        /// 用指定时刻格式化并写入时钟文字。
        /// 简洁变量模式与 .NET 格式模式二选一，取决于 <see cref="AppSettings.FormatMode"/>。
        /// </summary>
        /// <param name="now">当前时区时间，由调用方统一取，保证与提醒检查同一时刻。</param>
        /// <param name="force">true = 跳过文本比较，强制刷新。</param>
        private void UpdateClock(DateTime now, bool force)
        {
            // 天气只作为格式变量的数据源；开关关闭时传 null，让变量渲染为空。
            WeatherInfo weather = _settings.WeatherEnabled ? _weatherInfo : null;
            string display = FormatEngine.IsTokenMode(_settings.FormatMode)
                ? FormatEngine.Format(now, weather, _settings.TokenTimeFormat)
                : TimeFormatter.Format(now, _settings.TimeFormat);
            // 文本没变就不赋值：LayeredText 赋值会同步四个 TextBlock 并触发布局，100 ms 一次太浪费。
            if (force || !string.Equals(display, _lastDisplayText, StringComparison.Ordinal))
            {
                _clockText.Text = display;
                _lastDisplayText = display;
            }
        }

        /// <summary>
        /// 判断当前时间格式是否已经包含天气变量。
        /// 若已包含，就不再单独显示天气行，避免同一份信息出现两次。
        /// </summary>
        private bool TimeFormatUsesWeather()
        {
            return FormatEngine.IsTokenMode(_settings.FormatMode)
                && FormatEngine.ContainsWeatherToken(_settings.TokenTimeFormat);
        }

        /// <summary>
        /// 拼出天气行文本：优先用用户自定义的天气格式，未配置则退回天气数据自带的摘要。
        /// 天气关闭、无数据或时间格式已含天气变量时返回空串，由显隐逻辑折叠整行。
        /// </summary>
        /// <param name="now">当前时区时间，供天气格式里的时间变量使用。</param>
        private string BuildWeatherText(DateTime now)
        {
            // 没有开关或没有数据都没有可显示的内容。
            if (!_settings.WeatherEnabled || _weatherInfo == null)
            {
                return string.Empty;
            }
            // 时间行里已经带了天气，重复显示会显得冗余。
            if (TimeFormatUsesWeather())
            {
                return string.Empty;
            }
            // 自定义天气格式优先，允许用户只取温度或只取天气现象。
            if (!string.IsNullOrWhiteSpace(_settings.WeatherFormat))
            {
                return FormatEngine.Format(now, _weatherInfo, _settings.WeatherFormat);
            }
            return _weatherInfo.DisplayText;
        }

        /// <summary>
        /// 重新计算并写入天气文本；天气异步回调与设置变更都从这里进入。
        /// 文本变化会改变窗口高度，因此需要重排并把窗口拉回可见区域。
        /// </summary>
        private void RefreshWeatherText()
        {
            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            string text = BuildWeatherText(now);
            // 先比较再赋值，避免无关刷新引发布局抖动。
            bool changed = !string.Equals(_weatherText.Text, text, StringComparison.Ordinal);
            _weatherText.Text = text;
            UpdateWeatherVisibility();
            // 文本确实变化且窗口已加载，才需要重排并纠偏。
            if (changed && _loaded)
            {
                UpdateLayout();
                Dispatcher.BeginInvoke(
                    new Action(EnsureVisible),
                    DispatcherPriority.Background);
            }
            // 天气变量可能参与时钟格式，文本变化后时钟也要按同一时刻重算。
            UpdateClock(now, true);
        }

        /// <summary>
        /// 检查是否有提醒到点：整点匹配 HH:mm、模式匹配当前场景、且今天未触发过。
        /// 触发后立刻回写 LastTriggeredDate，防止 100 ms 轮询在同一分钟内重复播报。
        /// </summary>
        /// <param name="now">当前时区时间，由调用方统一提供。</param>
        private void CheckDueReminders(DateTime now)
        {
            // 没有提醒配置时直接返回，省掉每分钟 600 次的字符串格式化。
            if (_settings.Reminders == null || _settings.Reminders.Count == 0)
            {
                return;
            }

            string currentTime = now.ToString("HH:mm");
            string today = now.ToString("yyyy-MM-dd");

            // 数量通常只有个位数，线性扫描足够，还能直接回写触发标记。
            for (int i = 0; i < _settings.Reminders.Count; i++)
            {
                Reminder reminder = _settings.Reminders[i];
                // 列表里可能有 null 或已停用的项，跳过即可，不影响其它提醒。
                if (reminder == null || !reminder.Enabled)
                {
                    continue;
                }
                // 只在分钟完全相等时触发：精度到分，与界面上只能填 HH:mm 一致。
                if (!string.Equals(reminder.Time, currentTime, StringComparison.Ordinal))
                {
                    continue;
                }
                // 场景模式不匹配（如“仅专注模式”）时今天不提醒。
                if (!ReminderModes.AppliesToState(reminder.Mode, _settings.CurrentMode))
                {
                    continue;
                }
                // 今天已经响过就不再重复，避免同一分钟内多次入队。
                if (string.Equals(reminder.LastTriggeredDate, today, StringComparison.Ordinal))
                {
                    continue;
                }

                reminder.LastTriggeredDate = today;
                EnqueueReminder(new ReminderNotice(reminder.Time, reminder.Message));

                // 先入队保证界面一定显示，再通知控制器做额外处理（如托盘气泡）。
                if (ReminderTriggered != null)
                {
                    ReminderTriggered(this, new ReminderEventArgs(reminder));
                }
            }
        }

        /// <summary>
        /// 检查日程是否到达发生时刻：由 <see cref="ScheduleService"/> 算出本次 occurrence，
        /// 再用 occurrence key 去重，保证同一场日程只播报一次。
        /// </summary>
        /// <param name="now">当前时区时间。</param>
        private void CheckDueSchedules(DateTime now)
        {
            // 没有日程配置时直接返回，避免无谓的循环。
            if (_settings.Schedules == null || _settings.Schedules.Count == 0)
            {
                return;
            }

            // 同样是线性扫描；触发规则全部交给 ScheduleService 计算。
            for (int i = 0; i < _settings.Schedules.Count; i++)
            {
                ScheduleItem schedule = _settings.Schedules[i];
                // 空项或停用项直接跳过，不影响其它日程。
                if (schedule == null || !schedule.Enabled)
                {
                    continue;
                }

                DateTime occurrence;
                // 触发规则（每年/每月/每周等）全部封装在服务里，窗口只拿结果。
                if (!ScheduleService.TryFindDueOccurrence(schedule, now, out occurrence))
                {
                    continue;
                }

                // occurrence key 精确到具体发生实例，比“今天是否响过”更适合周期日程。
                string occurrenceKey = ScheduleService.GetOccurrenceKey(occurrence);
                // 同一个 occurrence 已经播报过就不再重复。
                if (string.Equals(schedule.LastTriggeredKey, occurrenceKey, StringComparison.Ordinal))
                {
                    continue;
                }

                schedule.LastTriggeredKey = occurrenceKey;
                EnqueueScheduleNotice(schedule, occurrence);

                // 先入队保证界面可见，再通知控制器做附加处理。
                if (ScheduleTriggered != null)
                {
                    ScheduleTriggered(this, new ScheduleTriggeredEventArgs(schedule, occurrence));
                }
            }
        }

        /// <summary>
        /// 把一条日程转成提醒气泡：首行时间 + 类别标签，正文是标题与可选备注。
        /// 备注为空时不追加换行，避免气泡里出现空白行。
        /// </summary>
        private void EnqueueScheduleNotice(ScheduleItem schedule, DateTime occurrence)
        {
            string label = ScheduleCategories.DisplayName(schedule.Type) + "日程";
            System.Text.StringBuilder message = new System.Text.StringBuilder();
            message.Append(schedule.Title);
            // 有备注才追加第二行，避免气泡里出现空白行。
            if (!string.IsNullOrWhiteSpace(schedule.Note))
            {
                message.Append("\n");
                message.Append(schedule.Note.Trim());
            }
            // 时刻用不随区域设置变化的格式，保证气泡首行永远是 HH:mm。
            EnqueueReminder(new ReminderNotice(
                occurrence.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                message.ToString(),
                label));
        }

        /// <summary>
        /// 提醒入队：当前没有气泡就直接显示，否则排队等上一条收起。
        /// 所有提醒（真实、日程、预览）都走这一个入口，保证串行与样式一致。
        /// </summary>
        private void EnqueueReminder(ReminderNotice notice)
        {
            // 有气泡在显示就排队，等它收起后按顺序播。
            if (_reminderVisible)
            {
                _reminderQueue.Enqueue(notice);
                return;
            }
            ShowReminder(notice);
        }

        /// <summary>
        /// 显示一条提醒气泡：填文字、按设置闪烁、可选提示音，并启动停留计时与悬停监听。
        /// 调用前提是没有其它气泡正在显示，串行由 <see cref="EnqueueReminder"/> 保证。
        /// </summary>
        private void ShowReminder(ReminderNotice notice)
        {
            _reminderTimeText.Text = notice.Time + " · " + notice.Label;
            _reminderMessageText.Text = notice.Message;
            _reminderPanel.Visibility = Visibility.Visible;
            // 先清掉可能残留的闪烁动画并归位不透明度，否则新气泡会从上次的动画状态开始。
            _reminderPanel.BeginAnimation(UIElement.OpacityProperty, null);
            _reminderPanel.Opacity = 1.0;
            int blinkCount = _settings.ReminderBlinkCount;
            // 闪烁次数为 0 表示关掉动效（对光敏感或省电场景）。
            if (blinkCount > 0)
            {
                // 1.0 ↔ 0.18 往复，260 ms 单程；FillBehavior.Stop 让动画结束后回到属性值 1.0。
                DoubleAnimation blink = new DoubleAnimation();
                blink.From = 1.0;
                blink.To = 0.18;
                blink.Duration = TimeSpan.FromMilliseconds(260);
                blink.AutoReverse = true;
                blink.RepeatBehavior = new RepeatBehavior(blinkCount);
                blink.FillBehavior = FillBehavior.Stop;
                _reminderPanel.BeginAnimation(UIElement.OpacityProperty, blink);
            }

            // 提示音由独立模块播放，失败也不影响气泡显示，因此这里不做异常处理。
            if (_settings.ReminderSoundEnabled)
            {
                AlarmSound.Play();
            }

            _reminderVisible = true;
            // 每次都重设间隔：用户可能在设置里改过停留秒数，复用旧值会让气泡提前或过晚消失。
            _reminderTimer.Stop();
            _reminderTimer.Interval = TimeSpan.FromSeconds(_settings.ReminderDisplaySeconds);
            _reminderTimer.Start();
            StartReminderHoverWatch();

            // 气泡改变了窗口高度，先布局再把窗口拉回可见区域。
            UpdateLayout();
            Dispatcher.BeginInvoke(
                new Action(EnsureVisible),
                DispatcherPriority.Background);
        }

        /// <summary>
        /// 应用提醒显示设置：气泡可见时按新时长重启收起计时，不可见时清掉悬停状态。
        /// 由 <see cref="ApplySettings"/> 调用，避免正在显示的气泡沿用旧设置。
        /// </summary>
        private void ApplyReminderDisplaySettings()
        {
            // 没有气泡时只需清空悬停状态，停掉轮询避免空转。
            if (!_reminderVisible)
            {
                _reminderHoverTimer.Stop();
                _reminderHoverStartedUtc = null;
                return;
            }

            _reminderTimer.Stop();
            _reminderTimer.Interval = TimeSpan.FromSeconds(_settings.ReminderDisplaySeconds);
            _reminderTimer.Start();
            StartReminderHoverWatch();
        }

        /// <summary>
        /// 按开关启动或停止悬停关闭监听；每次显示新气泡都要重置累计时间。
        /// </summary>
        private void StartReminderHoverWatch()
        {
            // 新气泡从零开始累计，不能继承上一条的悬停时刻。
            _reminderHoverStartedUtc = null;
            // 开关打开才启动轮询，否则停掉，省一次 150 ms 唤醒。
            if (_settings.ReminderHoverCloseEnabled)
            {
                _reminderHoverTimer.Start();
            }
            // 关闭悬停收起时不需要轮询，停表即可。
            else
            {
                _reminderHoverTimer.Stop();
            }
        }

        /// <summary>
        /// 停留计时到点：收起当前气泡，若有排队则接着显示下一条。
        /// </summary>
        private void OnReminderTimerTick(object sender, EventArgs e)
        {
            DismissCurrentReminder();
        }

        /// <summary>
        /// 悬停轮询：鼠标在气泡矩形内连续停留满 3 秒就提前收起。
        /// 用屏幕坐标比较，避免窗口自身位移时把“还在气泡上”误判为离开。
        /// </summary>
        private void OnReminderHoverTick(object sender, EventArgs e)
        {
            // 任一前提不成立（无气泡、开关关闭、尚未布局出尺寸）都视为未悬停，清零累计。
            if (!_reminderVisible
                || !_settings.ReminderHoverCloseEnabled
                || !_reminderPanel.IsVisible
                || _reminderPanel.ActualWidth <= 0
                || _reminderPanel.ActualHeight <= 0)
            {
                _reminderHoverStartedUtc = null;
                return;
            }

            NativeMethods.POINT cursor;
            // 取光标失败（如会话切换、UAC 界面）时按“不在气泡上”处理，不清空也无从判断。
            if (!NativeMethods.GetCursorPos(out cursor))
            {
                _reminderHoverStartedUtc = null;
                return;
            }

            // 把气泡的左上/右下角换算到屏幕坐标，与光标做矩形包含判断。
            Point topLeft = _reminderPanel.PointToScreen(new Point(0, 0));
            Point bottomRight = _reminderPanel.PointToScreen(
                new Point(_reminderPanel.ActualWidth, _reminderPanel.ActualHeight));
            bool inside = cursor.X >= topLeft.X
                && cursor.X <= bottomRight.X
                && cursor.Y >= topLeft.Y
                && cursor.Y <= bottomRight.Y;

            // 移出气泡就重新计时，避免“进出多次”累计到 3 秒误关。
            if (!inside)
            {
                _reminderHoverStartedUtc = null;
                return;
            }

            // 首次进入只记录起点，下次轮询再判断时长。
            if (!_reminderHoverStartedUtc.HasValue)
            {
                _reminderHoverStartedUtc = DateTime.UtcNow;
                return;
            }

            // 用 UTC 差值计时，不受系统时区或夏令时切换影响。
            if ((DateTime.UtcNow - _reminderHoverStartedUtc.Value).TotalSeconds >= 3.0)
            {
                _reminderHoverStartedUtc = null;
                DismissCurrentReminder();
            }
        }

        /// <summary>
        /// 收起当前气泡：停计时器、清动画、隐藏面板，再把队列里的下一条排到后台显示。
        /// 队列为空时窗口保持现状，等下一次提醒入队。
        /// </summary>
        private void DismissCurrentReminder()
        {
            _reminderTimer.Stop();
            _reminderHoverTimer.Stop();
            _reminderHoverStartedUtc = null;
            // 先摘掉闪烁动画再隐藏，避免动画在隐藏状态下继续跑而占用渲染资源。
            _reminderPanel.BeginAnimation(UIElement.OpacityProperty, null);
            _reminderPanel.Visibility = Visibility.Collapsed;
            _reminderVisible = false;

            // 队列里还有提醒就立刻接上下一条。
            if (_reminderQueue.Count > 0)
            {
                ReminderNotice next = _reminderQueue.Dequeue();
                // 排到后台而不是同步调用：避免在计时器回调里递归显示，也让隐藏先完成渲染；
                // 下一条延后显示，避免在收起回调里同步递归。
                Dispatcher.BeginInvoke(
                    new Action(delegate { ShowReminder(next); }),
                    DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// 恢复上次保存的位置；若保存点已不在任何屏幕内（拔掉外接屏、分辨率变化）则退回默认位置。
        /// </summary>
        private void RestorePosition()
        {
            // 两个坐标都有值才算有效保存位置。
            if (_settings.Left.HasValue && _settings.Top.HasValue)
            {
                SetWindowPosition(_settings.Left.Value, _settings.Top.Value);
                // 至少露出一块可点的区域才算可用；否则继续走默认停靠。
                if (HasUsableVisibleArea())
                {
                    return;
                }
            }
            ResetPositionToDefault();
        }

        /// <summary>
        /// 把窗口放到主屏工作区右上角，并留出默认边距。
        /// 尺寸尚未算出时用 300×90 的保守估计，保证位置不会离谱到屏幕外。
        /// </summary>
        private void ResetPositionToDefault()
        {
            Rect workArea = SystemParameters.WorkArea;
            double width = ActualWidth > 0 ? ActualWidth : 300;
            double height = ActualHeight > 0 ? ActualHeight : 90;
            double left = workArea.Right - width - DefaultMargin;
            double top = workArea.Top + DefaultMargin;
            SetWindowPosition(left, top);
        }

        /// <summary>
        /// 程序内改坐标的统一入口；置位 <see cref="_applyingPosition"/> 让 LocationChanged 跳过回写，
        /// finally 保证即使赋值抛异常也会复位标志。
        /// </summary>
        private void SetWindowPosition(double left, double top)
        {
            _applyingPosition = true;
            // 用 try/finally 保证标志一定复位，即使赋值过程抛异常。
            try
            {
                Left = left;
                Top = top;
            }
            finally
            {
                _applyingPosition = false;
            }
        }

        /// <summary>
        /// 判断窗口在虚拟屏幕里是否还有足够大的可见区域（宽 80、高 36 与窗口尺寸取小）。
        /// 用于启动时决定“沿用保存位置”还是“回到默认位置”。
        /// </summary>
        private bool HasUsableVisibleArea()
        {
            Rect virtualScreen = GetVirtualScreen();
            double width = Math.Max(ActualWidth, 1.0);
            double height = Math.Max(ActualHeight, 1.0);
            // 分别求与虚拟屏幕矩形的交集宽高；负数说明完全在屏幕外。
            double visibleWidth = Math.Min(Left + width, virtualScreen.Right) - Math.Max(Left, virtualScreen.Left);
            double visibleHeight = Math.Min(Top + height, virtualScreen.Bottom) - Math.Max(Top, virtualScreen.Top);
            return visibleWidth >= Math.Min(80.0, width) && visibleHeight >= Math.Min(36.0, height);
        }

        /// <summary>
        /// 保证窗口至少露出一条可拖动/可右键的边：越界时平移回屏幕内。
        /// 与 <see cref="KeepInsideScreenAfterResize"/> 不同，这里允许大部分在屏幕外，只保底 90×40。
        /// </summary>
        private void EnsureVisible()
        {
            // 位置未就绪时窗口尺寸与坐标都不可信，直接跳过。
            if (!_positionReady)
            {
                return;
            }

            Rect virtualScreen = GetVirtualScreen();
            double width = Math.Max(ActualWidth, 1.0);
            double height = Math.Max(ActualHeight, 1.0);
            double left = Left;
            double top = Top;

            // 保底可见宽高：窗口比这还小时按窗口实际尺寸，避免算出负偏移。
            double minVisibleWidth = Math.Min(90.0, width);
            double minVisibleHeight = Math.Min(40.0, height);

            // 左侧越界：把窗口左边缘推到“屏幕左 + 保底宽度 - 窗口宽”的位置。
            if (left + width < virtualScreen.Left + minVisibleWidth)
            {
                left = virtualScreen.Left + minVisibleWidth - width;
            }
            // 右侧越界：保留右边缘的保底宽度。
            if (left > virtualScreen.Right - minVisibleWidth)
            {
                left = virtualScreen.Right - minVisibleWidth;
            }
            // 上方与下方同理，纵向保底 40 像素，够抓到标题区域即可。
            if (top + height < virtualScreen.Top + minVisibleHeight)
            {
                top = virtualScreen.Top + minVisibleHeight - height;
            }
            // 下边界同理：底边保留保底高度。
            if (top > virtualScreen.Bottom - minVisibleHeight)
            {
                top = virtualScreen.Bottom - minVisibleHeight;
            }

            // 只有实际需要移动才写坐标，避免每次刷新都触发保存。
            if (Math.Abs(left - Left) > 0.1 || Math.Abs(top - Top) > 0.1)
            {
                SetWindowPosition(left, top);
                CapturePosition();
                // 位置有变，交给控制器决定何时落盘。
                if (PositionChanged != null)
                {
                    PositionChanged(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 取整个虚拟桌面矩形（含所有显示器），多屏与负坐标场景都以此为准。
        /// </summary>
        private Rect GetVirtualScreen()
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }

        /// <summary>
        /// 把当前窗口坐标写回设置；未就绪时跳过，防止把初始化中的中间值存下来。
        /// 只改内存，不落盘；落盘由控制器收到 <see cref="PositionChanged"/> 后防抖完成。
        /// </summary>
        private void CapturePosition()
        {
            // 未就绪时 Left/Top 还是初始化中间值，不能写进设置。
            if (!_positionReady)
            {
                return;
            }
            _settings.Left = Left;
            _settings.Top = Top;
        }

        /// <summary>
        /// 不检查状态的强制回写，只在 OnLoaded 里“位置已定稿、标志刚置位”时使用。
        /// </summary>
        private void CapturePositionQuietly()
        {
            _settings.Left = Left;
            _settings.Top = Top;
        }

        /// <summary>
        /// 一条待显示提醒的不可变快照：时刻、正文、来源标签。
        /// 用快照而不是直接引用 Reminder，是因为入队后用户可能马上编辑或删除原对象。
        /// </summary>
        private sealed class ReminderNotice
        {

            /// <summary>普通提醒：来源标签固定为「提醒」。</summary>
            public ReminderNotice(string time, string message)
                : this(time, message, "提醒")
            {
            }

            /// <summary>带预览标记的构造：预览用「测试提醒」，避免和真实提醒混淆。</summary>
            public ReminderNotice(string time, string message, bool isPreview)
                : this(time, message, isPreview ? "测试提醒" : "提醒")
            {
            }

            /// <summary>完整构造：由日程等调用方直接给出标签（如「生日日程」）。</summary>
            public ReminderNotice(string time, string message, string label)
            {
                Time = time;
                Message = message;
                Label = label;
            }

            /// <summary>首行显示的时刻文本，通常为 HH:mm。</summary>
            public string Time { get; private set; }

            /// <summary>气泡正文，可含换行（日程备注会拼在标题后）。</summary>
            public string Message { get; private set; }

            /// <summary>来源标签，与时刻用「 · 」连接显示。</summary>
            public string Label { get; private set; }
        }
    }
}
