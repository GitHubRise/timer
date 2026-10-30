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
    // ClockWindow：桌面悬浮窗本体（无边框、透明、置顶，可选点击穿透）。
    // 职责：绘制已格式化的时间/天气文本，处理拖动、滚轮缩放与右键菜单请求，
    //       在提醒/日程到点时展示气泡；位置与字号变更通过事件交给控制器落盘。
    // 调用方：AppController 创建/销毁本窗口并转发设置；本窗口不反向调用控制器。
    // 边界：不解析时间格式、不读写配置、不直接调用 SettingsStore；Win32 细节
    //       一律走 NativeMethods。100 ms 时钟刷新等高频路径禁止写日志。
    // 已知坑：SizeToContent 使 Loaded 前 ActualWidth/Height 不可信，位置恢复必须
    //       延迟到 Loaded；透明背景需最小可见 alpha，否则 WPF 不给根元素命中测试。
    /// <summary>
    /// 桌面悬浮窗：无边框透明置顶窗口，负责时间/天气展示、拖动、滚轮缩放、
    /// 全屏自动隐藏与提醒气泡排队展示。
    /// </summary>
    internal sealed class ClockWindow : Window
    {
        /// <summary>默认距工作区边缘的留白（DIP）；首次显示或重置位置时使用。</summary>
        private const double DefaultMargin = 26.0;

        /// <summary>设置副本；位置/字号改动只写回这里，落盘由 AppController 防抖完成。</summary>
        private AppSettings _settings;

        /// <summary>当前时区；100 ms tick 用它换算“现在”，切换时区由设置应用驱动。</summary>
        private TimeZoneInfo _timeZone;
        // UI 元素组：根 Border 提供整块命中区域（拖动/右键），StackPanel 纵向排
        // 时间、天气、提醒气泡；全部只在构造期创建一次，之后只改属性。
        private readonly Border _root;
        private readonly StackPanel _stack;
        private readonly LayeredText _clockText;
        private readonly TextBlock _weatherText;
        private readonly Border _reminderPanel;
        private readonly TextBlock _reminderTimeText;
        private readonly TextBlock _reminderMessageText;
        // 计时器组：时钟 100 ms、置顶 3 s、提醒 12 s、悬停 150 ms、全屏 1 s。
        // 全部在构造期创建，OnClosed 统一停止；Tick 内属于高频路径，不写日志。
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _topmostTimer;
        private readonly DispatcherTimer _reminderTimer;
        private readonly DispatcherTimer _reminderHoverTimer;
        private readonly DispatcherTimer _fullscreenTimer;
        private readonly Queue<ReminderNotice> _reminderQueue;

        // 运行状态组：句柄与加载标志决定 Win32 调用是否安全；_positionReady 之前的
        // Left/Top 是临时值，不能落盘；其余标志记录置顶暂停、全屏隐藏与提醒状态。
        private HwndSource _source;
        private bool _loaded;
        private bool _positionReady;
        private bool _applyingPosition;
        private bool _topmostWatchSuspended;
        private bool _hiddenForFullscreen;
        private bool _reminderVisible;
        private DateTime? _reminderHoverStartedUtc;
        private string _lastDisplayText;
        private WeatherInfo _weatherInfo;

        /// <summary>右键请求打开悬浮菜单；窗口本身不创建菜单。</summary>
        public event EventHandler MenuRequested;

        /// <summary>位置变化（拖动、重置、夹回屏幕）后触发，控制器据此防抖保存。</summary>
        public event EventHandler PositionChanged;

        /// <summary>滚轮缩放字号后触发，控制器据此防抖保存。</summary>
        public event EventHandler ScaleChanged;

        /// <summary>提醒到点触发；控制器负责写 LastTriggeredDate 之外的外部动作。</summary>
        public event EventHandler<ReminderEventArgs> ReminderTriggered;

        /// <summary>日程到点触发；携带本次实际发生的时间点。</summary>
        public event EventHandler<ScheduleTriggeredEventArgs> ScheduleTriggered;

        /// <summary>
        /// 搭 UI 与计时器，但不显示窗口；位置恢复放到 OnLoaded，因为 SizeToContent
        /// 需要先完成一次布局才能得到真实宽高。
        /// </summary>
        public ClockWindow(AppSettings settings)
        {
            _settings = settings ?? AppSettings.CreateDefault();
            // 控制器与被窗口共享同一份设置对象；这里 Normalize 只兜底修正本窗口
            // 会用到的数值范围（字号 16–160、透明度 0.2–1.0 等）。
            _settings.Normalize();
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            // 无边框 + 透明 + 不占任务栏：定位完全由设置中的 Left/Top 决定；
            // 初始透明度 0，等 OnLoaded 完成布局后再显示，避免首帧在错误位置闪一下。
            Title = "GameClockOverlayWindow";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = _settings.Topmost;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            Opacity = 0.0;

            // 上次位置只作初值；OnLoaded 里还会按虚拟屏幕可见性校验并夹回。
            if (_settings.Left.HasValue && _settings.Top.HasValue)
            {
                Left = _settings.Left.Value;
                Top = _settings.Top.Value;
            }

            _root = new Border();
            // 分层窗口按像素命中测试：给整块区域一个 alpha=1/255 的近透明底色，
            // 让整个时钟区域都能拖动/右键，而不是只能点中文字笔画。
            _root.Background = UiTheme.BrushFrom(Color.FromArgb(1, 0, 0, 0));
            _root.Cursor = Cursors.SizeAll;
            _root.Padding = new Thickness(18, 10, 18, 14);

            _stack = new StackPanel();
            _stack.HorizontalAlignment = HorizontalAlignment.Center;
            _stack.VerticalAlignment = VerticalAlignment.Center;

            _clockText = new LayeredText();
            _clockText.IsHitTestVisible = false;
            _stack.Children.Add(_clockText);

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

            _reminderTimeText = new TextBlock();
            _reminderTimeText.FontFamily = new FontFamily("Microsoft YaHei UI");
            _reminderTimeText.FontSize = 11;
            _reminderTimeText.FontWeight = FontWeights.SemiBold;
            _reminderTimeText.HorizontalAlignment = HorizontalAlignment.Center;
            _reminderTimeText.TextAlignment = TextAlignment.Center;

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

            // 提醒气泡内容：第一行「时间 · 标签」，第二行正文（自动换行，最大 460 DIP）。
            StackPanel reminderContent = new StackPanel();
            reminderContent.Children.Add(_reminderTimeText);
            reminderContent.Children.Add(_reminderMessageText);

            // 深色半透明底（alpha=222/255）保证浅色与深色桌面上文字都可读；
            // 圆角 9、1 px 边框与阴影只影响观感，不参与窗口尺寸计算。
            _reminderPanel = new Border();
            _reminderPanel.Background = UiTheme.BrushFrom(Color.FromArgb(222, 18, 18, 24));
            _reminderPanel.BorderThickness = new Thickness(1);
            _reminderPanel.CornerRadius = new CornerRadius(9);
            _reminderPanel.Padding = new Thickness(14, 9, 14, 10);
            _reminderPanel.Margin = new Thickness(0, 10, 0, 0);
            _reminderPanel.MaxWidth = 460;
            _reminderPanel.HorizontalAlignment = HorizontalAlignment.Center;
            _reminderPanel.Visibility = Visibility.Collapsed;
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

            // 提醒串行展示：当前气泡可见时，后来的提醒先入队，关闭后再依次弹出。
            _reminderQueue = new Queue<ReminderNotice>();

            // 计时器分工（Tick 均为高频路径，函数体内禁止写日志）：
            // 100 ms 刷新时间文本并检查提醒/日程；3 s 重新压置顶；
            // 12 s 提醒自动关闭；150 ms 检查鼠标是否悬停在气泡上；1 s 轮询全屏状态。
            _clockTimer = new DispatcherTimer(DispatcherPriority.Background);
            _clockTimer.Interval = TimeSpan.FromMilliseconds(100);
            _clockTimer.Tick += OnClockTick;

            _topmostTimer = new DispatcherTimer(DispatcherPriority.Background);
            _topmostTimer.Interval = TimeSpan.FromSeconds(3.0);
            _topmostTimer.Tick += OnTopmostTick;

            _reminderTimer = new DispatcherTimer(DispatcherPriority.Background);
            _reminderTimer.Interval = TimeSpan.FromSeconds(12.0);
            _reminderTimer.Tick += OnReminderTimerTick;

            _reminderHoverTimer = new DispatcherTimer(DispatcherPriority.Background);
            _reminderHoverTimer.Interval = TimeSpan.FromMilliseconds(150);
            _reminderHoverTimer.Tick += OnReminderHoverTick;

            _fullscreenTimer = new DispatcherTimer(DispatcherPriority.Background);
            _fullscreenTimer.Interval = TimeSpan.FromSeconds(1.0);
            _fullscreenTimer.Tick += OnFullscreenTick;

            // 事件只上报意图，落盘、弹菜单、同步天气等副作用统一由 AppController 处理。
            Loaded += OnLoaded;
            Closed += OnClosed;
            LocationChanged += OnLocationChanged;
            SizeChanged += OnSizeChanged;
            _root.MouseLeftButtonDown += OnMouseLeftButtonDown;
            _root.MouseRightButtonUp += OnMouseRightButtonUp;
            _root.MouseWheel += OnMouseWheel;

            // 构造期先按默认设置刷一遍文字与样式，避免显示首帧出现空白。
            ApplyVisuals();
            UpdateClock(true);
        }

        /// <summary>
        /// 应用控制器提交的新设置：刷新字体/颜色/天气行/提醒样式与窗口层级。
        /// 只在设置提交后调用；本方法不落盘，也不重建窗口。
        /// </summary>
        public void ApplySettings(AppSettings settings)
        {
            // 控制器可能传 null（设置尚未加载完成）：保持当前显示，不抛异常。
            if (settings == null)
            {
                return;
            }

            // Normalize 修正数值范围；时区解析一次后缓存，后续 tick 不再查表。
            _settings = settings;
            _settings.Normalize();
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            // 句柄未就绪时只刷视觉；位置恢复与 Win32 样式留给 OnLoaded/首帧回调。
            if (!_loaded)
            {
                ApplyVisuals();
                RefreshWeatherText();
                return;
            }

            // 刷新次序：先样式与文字，再透明度/层级，最后重排布局。
            ApplyVisuals();
            RefreshWeatherText();
            Opacity = _settings.Opacity;
            Topmost = _settings.Topmost;
            ApplyReminderDisplaySettings();

            // 关闭「全屏隐藏」后要立刻恢复显示，不能等下一次 1 s 轮询。
            if (!_settings.HideOnFullscreen)
            {
                RestoreFromFullscreen();
            }

            // 点击穿透与置顶是 Win32 扩展样式，WPF 属性覆盖不到，必须显式重设。
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ApplyOverlayStyles(hwnd, _settings.ClickThrough);
            NativeMethods.SetTopmost(hwnd, _settings.Topmost);

            // 字号/天气行变化会改变窗口尺寸：等布局完成后再夹回可见区并捕获位置。
            UpdateLayout();
            Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    EnsureVisible();
                    CapturePosition();
                }),
                DispatcherPriority.Background);

            // 设置应用是一次用户可见跃迁：记下关键外观参数，便于对照“设置没生效”的反馈。
            AppLog.Info(
                "Clock",
                "悬浮窗设置已应用：字号=" + _settings.FontSize
                    + "，透明度=" + _settings.Opacity
                    + "，格式=" + _settings.TimeFormat
                    + "，天气行=" + _settings.WeatherEnabled
                    + "，点击穿透=" + _settings.ClickThrough
                    + "，置顶=" + _settings.Topmost);
        }

        /// <summary>
        /// 切换鼠标穿透：开启后点击落到桌面，悬浮窗不再响应拖动/右键。
        /// 未 Loaded 时没有句柄，等 OnSourceInitialized 统一应用。
        /// </summary>
        public void ApplyClickThrough(bool enabled)
        {
            _settings.ClickThrough = enabled;
            // 句柄未创建时无法改扩展样式，OnSourceInitialized 会按当前设置补上。
            if (!_loaded)
            {
                return;
            }
            NativeMethods.SetClickThrough(new WindowInteropHelper(this).Handle, enabled);
        }

        /// <summary>
        /// 更新天气数据并刷新天气行；info 为 null 表示取数失败，天气行会隐藏，
        /// 时间格式里若含天气 token 则由后续 tick 自然显示为空。
        /// </summary>
        public void SetWeather(WeatherInfo info)
        {
            // 直接覆盖旧数据：取数失败传 null，比保留过期的“昨天天气”更诚实。
            _weatherInfo = info;
            RefreshWeatherText();
        }

        /// <summary>
        /// 立即应用置顶开关：同时设置 WPF Topmost 与 Win32 HWND_TOPMOST，
        /// 只改一边会出现“设置里关了仍压在最前”的不一致。
        /// </summary>
        public void ApplyTopmost(bool enabled)
        {
            _settings.Topmost = enabled;
            // 未 Loaded 时只记设置值，句柄就绪后由 OnLoaded 统一应用。
            if (!_loaded)
            {
                return;
            }

            Topmost = enabled;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetTopmost(hwnd, enabled);
        }

        /// <summary>把窗口重新压到最前（启动、菜单关闭、全屏恢复后调用），仅在开启置顶时生效。</summary>
        public void ReassertTopmost()
        {
            // 关闭置顶时重压会把窗口错误地提回最前，因此这里必须尊重设置。
            if (!_loaded || !_settings.Topmost)
            {
                return;
            }
            NativeMethods.SetTopmost(new WindowInteropHelper(this).Handle, true);
        }

        /// <summary>
        /// 菜单或设置窗口打开期间挂起 3 s 置顶轮询，避免悬浮窗把刚打开的窗口压到下面。
        /// 挂起只影响轮询，不影响用户显式切换置顶。
        /// </summary>
        public void SetTopmostWatchSuspended(bool suspended)
        {
            _topmostWatchSuspended = suspended;
        }

        /// <summary>提醒/日程保存后立即补查一次到期项，不等下一次 100 ms tick。</summary>
        public void CheckRemindersNow()
        {
            // 窗口未显示时也允许补查：提醒由队列与计时器驱动，不依赖可见性。
            if (_loaded)
            {
                DateTime now = TimeZoneCatalog.GetNow(_timeZone);
                CheckDueReminders(now);
                CheckDueSchedules(now);
            }
        }

        /// <summary>
        /// 弹出测试提醒（设置页“预览”按钮）：时间不合法时回退为 --:-- 或用户原文，
        /// 保证预览操作总有可见反馈。
        /// </summary>
        public void ShowReminderPreview(string time, string message)
        {
            // 空正文没有可展示的内容，直接忽略而不是弹一个空白气泡。
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string normalizedTime;
            // 预览允许用户只写正文：时间无法规范化时用占位符，不让整次预览失败。
            if (!TimeFormatter.TryNormalizeTime(time, out normalizedTime))
            {
                normalizedTime = string.IsNullOrWhiteSpace(time) ? "--:--" : time.Trim();
            }
            EnqueueReminder(new ReminderNotice(normalizedTime, message.Trim(), true));
        }

        /// <summary>把窗口移回工作区右上角的默认位置，并通知控制器保存。</summary>
        public void ResetPosition()
        {
            ResetPositionToDefault();
            // 重置是用户可见动作：记录最终位置，后续防抖保存由控制器完成。
            AppLog.Info("Clock", "悬浮窗位置重置为默认：Left=" + Left + ", Top=" + Top);
            CapturePosition();
            // 位置事件由控制器订阅并触发防抖落盘；这里不直接写 SettingsStore。
            if (PositionChanged != null)
            {
                PositionChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 窗口首次显示：恢复上次位置、应用扩展样式并启动计时器。
        /// 全屏轮询与置顶轮询也在这里启动，保证隐藏/显示期间不影响提醒计时。
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;
            ApplyVisuals();
            // 顺序有依赖：先 UpdateLayout 让 SizeToContent 算出实际宽高，再恢复位置，
            // 否则首次定位会按 0×0 把窗口放到屏幕外。
            UpdateLayout();
            RestorePosition();
            UpdateLayout();
            _positionReady = true;
            CapturePositionQuietly();
            Opacity = _settings.Opacity;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ApplyOverlayStyles(hwnd, _settings.ClickThrough);
            NativeMethods.SetTopmost(hwnd, _settings.Topmost);

            UpdateClock(true);
            RefreshWeatherText();
            _clockTimer.Start();
            _topmostTimer.Start();
            _fullscreenTimer.Start();

            // 窗口创建属于状态跃迁：记录一次最终位置/字号/格式，便于对照用户反馈。
            AppLog.Info(
                "Clock",
                "悬浮窗加载完成：位置=(" + Left + ", " + Top + ")，字号=" + _settings.FontSize
                    + "，格式=" + _settings.TimeFormat + "，点击穿透=" + _settings.ClickThrough
                    + "，置顶=" + _settings.Topmost);
        }

        /// <summary>关闭前停掉全部计时器并摘掉窗口过程钩子，避免已销毁窗口继续被 Tick 访问。</summary>
        private void OnClosed(object sender, EventArgs e)
        {
            _clockTimer.Stop();
            _topmostTimer.Stop();
            _reminderTimer.Stop();
            _reminderHoverTimer.Stop();
            _fullscreenTimer.Stop();
            // 钩子必须解绑：否则已关闭窗口的 WndProc 仍可能被消息泵回调。
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source = null;
            }

            AppLog.Info("Clock", "悬浮窗已关闭：时钟/置顶/提醒/全屏计时器均已停止");
        }

        /// <summary>句柄创建完成：此时才能设置 WS_EX_TOOLWINDOW/WS_EX_TRANSPARENT 等扩展样式。</summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ApplyOverlayStyles(hwnd, _settings.ClickThrough);

            // 重复初始化时不要重复挂同一个钩子；HwndSource 偶尔取不到（句柄尚未完全就绪）。
            if (_source == null)
            {
                _source = HwndSource.FromHwnd(hwnd);
                // 取不到 HwndSource 只失去“不抢焦点”这一层保护，窗口仍可正常显示。
                if (_source != null)
                {
                    _source.AddHook(WndProc);
                }
            }
        }

        /// <summary>
        /// 窗口过程钩子：拦截 WM_MOUSEACTIVATE 并返回 MA_NOACTIVATE，
        /// 点击悬浮窗时不抢前台焦点（游戏/全屏场景下尤其重要）。
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 只处理鼠标激活；其余消息原样交回 WPF，避免拦截掉输入/绘制消息。
            if (msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// 左键拖动：点击穿透开启时完全不响应；DragMove 会一直阻塞到鼠标松开。
        /// 拖动结束后才夹回可见区并上报位置，避免拖动途中反复落盘。
        /// </summary>
        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 穿透模式下 WPF 收不到鼠标事件；这里再判一次，防样式切换瞬间的漏网事件。
            if (_settings.ClickThrough)
            {
                return;
            }

            // 只处理“按下左键且当前仍按住”，滚轮/右键不进入拖动流程。
            if (e.ChangedButton == MouseButton.Left && e.LeftButton == MouseButtonState.Pressed)
            {
                e.Handled = true;
                // DragMove 会阻塞到鼠标松开；竞态下 WPF 抛 InvalidOperationException，需要兜住。
                try
                {
                    DragMove();
                }
                // 只兜 DragMove 的已知竞态异常；其他异常不吞，继续向上抛。
                catch (InvalidOperationException)
                {
                    // 鼠标在 DragMove 真正开始前已松开时 WPF 会抛此异常：属于正常竞态，
                    // 不是缺陷，但记一条 Warn 便于排查用户“拖不动”的反馈。
                    AppLog.Warn("Clock", "拖动未开始：鼠标状态已变化（InvalidOperationException），本次拖动忽略");
                }
                EnsureVisible();
                CapturePosition();
                AppLog.Info("Clock", "拖动/定位收尾，位置已捕获：Left=" + Left + ", Top=" + Top + "（等待防抖保存）");
            }
        }

        /// <summary>右键请求菜单：菜单的创建与定位由控制器负责，窗口只转发事件。</summary>
        private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 穿透模式下手感应像桌面本体：不弹菜单、不改选中状态。
            if (_settings.ClickThrough)
            {
                return;
            }

            e.Handled = true;
            // 没有订阅者（例如脱离控制器单独实例化）时静默忽略，不抛空引用。
            if (MenuRequested != null)
            {
                MenuRequested(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 滚轮只缩放字号：步长 4，范围 16–160；不改变位置、颜色或透明度。
        /// 变更通过 ScaleChanged 交给控制器防抖落盘，本方法内不做保存。
        /// </summary>
        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 穿透模式下滚轮应透给下层应用；delta 为 0 的合成事件也没有缩放意义。
            if (_settings.ClickThrough || e.Delta == 0)
            {
                return;
            }

            double step = e.Delta > 0 ? 4.0 : -4.0;
            double next = _settings.FontSize + step;
            // 夹到设置允许范围；触边后再滚会因数值不变而直接返回，不会产生日志噪音。
            next = Math.Max(AppSettings.MinFontSize, Math.Min(AppSettings.MaxFontSize, next));
            // 夹取后与当前值相同（已在边界）：不重绘、不通知，滚轮事件到此为止。
            if (Math.Abs(next - _settings.FontSize) < 0.01)
            {
                return;
            }

            e.Handled = true;
            _settings.FontSize = next;
            bool atLimit = next <= AppSettings.MinFontSize || next >= AppSettings.MaxFontSize;
            ApplyVisuals();
            // 缩放只通知控制器，位置/透明度保持不变是滚轮交互的硬约束。
            if (ScaleChanged != null)
            {
                ScaleChanged(this, EventArgs.Empty);
            }
            // 触边判定只用于日志：普通滚动格不记录，避免一轮缩放刷出几十条。
            if (atLimit)
            {
                // 只在触边时记一条：普通滚轮格不写日志，避免一格一条淹没 app.log。
                AppLog.Info(
                    "Clock",
                    "滚轮缩放到达字号边界：" + next + "（允许范围 "
                        + AppSettings.MinFontSize + "–" + AppSettings.MaxFontSize + "）");
            }
        }

        /// <summary>
        /// 每秒轮询全屏状态：QUNS_RUNNING_D3D_FULL_SCREEN(3) 或 QUNS_PRESENTATION_MODE(4)
        /// 时隐藏窗口；查询失败按“非全屏”处理，避免异常把时钟永久藏起来。
        /// 只切换显示状态，不停止时钟、提醒与日程计时器。
        /// </summary>
        private void OnFullscreenTick(object sender, EventArgs e)
        {
            // 窗口尚未 Loaded 时没有句柄，Hide/Show 没有意义，也拿不到有效状态。
            if (!_loaded)
            {
                return;
            }

            // 设置里关闭「全屏隐藏」后要立即恢复；复用同一入口保证样式与层级一致。
            if (!_settings.HideOnFullscreen)
            {
                RestoreFromFullscreen();
                return;
            }

            int state;
            // 查询失败（返回值非 0）按“非全屏”处理：宁可多显示，也不要把时钟永久藏掉。
            bool fullscreen = NativeMethods.SHQueryUserNotificationState(out state) == 0
                && (state == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN
                    || state == NativeMethods.QUNS_PRESENTATION_MODE);

            // 只有“确认全屏”才隐藏；1 s 轮询每次都会走到这里，绝不能记日志。
            if (fullscreen)
            {
                // 只在“可见 → 隐藏”这一次跃迁记日志；1 s 轮询本身绝不能写日志。
                if (IsVisible && !_hiddenForFullscreen)
                {
                    _hiddenForFullscreen = true;
                    AppLog.Info("Clock", "检测到全屏/演示模式（QUNS=" + state + "），隐藏悬浮窗");
                    Hide();
                }
            }
            // 仅在确实因全屏隐藏过时恢复，避免误 Show 用户手动关闭的窗口。
            else if (_hiddenForFullscreen)
            {
                RestoreFromFullscreen();
            }
        }

        /// <summary>
        /// 退出全屏/关闭“全屏隐藏”后恢复显示。重新应用分层与置顶样式，
        /// 否则窗口可能仍被全屏应用遗留的层级压住。
        /// </summary>
        private void RestoreFromFullscreen()
        {
            // 未处于全屏隐藏状态时重复 Show 会重新激活窗口，直接返回更安全。
            if (!_hiddenForFullscreen)
            {
                return;
            }

            _hiddenForFullscreen = false;
            Show();
            Opacity = _settings.Opacity;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ApplyOverlayStyles(hwnd, _settings.ClickThrough);
            NativeMethods.SetTopmost(hwnd, _settings.Topmost);
            ReassertTopmost();
            AppLog.Info("Clock", "退出全屏，恢复悬浮窗显示与置顶");
        }

        /// <summary>
        /// 窗口位置变化（拖动、程序设置、尺寸兜底修正）后缓存位置并通知控制器落盘。
        /// 位置尚未恢复或正在程序化设置位置时必须跳过，否则会和恢复流程互相覆盖。
        /// </summary>
        private void OnLocationChanged(object sender, EventArgs e)
        {
            // 初始化恢复与 SetWindowPosition 期间不记位置，避免把中间态写进配置。
            if (!_positionReady || _applyingPosition)
            {
                return;
            }

            CapturePosition();
            // 订阅方由控制器挂载；未挂载时（设计期/单测）静默跳过。
            if (PositionChanged != null)
            {
                PositionChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 尺寸变化（字号/天气行/提醒气泡）后延迟夹回屏幕可见区。
        /// 必须排到 Background：SizeChanged 触发时 ActualWidth/Height 还没稳定。
        /// </summary>
        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 位置尚未恢复时尺寸变化来自初始化布局，不应把窗口夹到别处。
            if (!_positionReady)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                new Action(KeepInsideScreenAfterResize),
                DispatcherPriority.Background);
        }

        /// <summary>
        /// 尺寸变化后的兜底定位：窗口大于屏幕时贴左上角，否则保证四周至少露出 4 DIP。
        /// 这里不直接落盘，位置变更仍通过 PositionChanged 交给控制器防抖保存。
        /// </summary>
        private void KeepInsideScreenAfterResize()
        {
            // 延迟回调可能在窗口关闭后才执行，必须再确认一次状态。
            if (!_positionReady)
            {
                return;
            }

            Rect virtualScreen = GetVirtualScreen();
            double width = Math.Max(ActualWidth, 1.0);
            double height = Math.Max(ActualHeight, 1.0);
            double left = Left;
            double top = Top;

            // 宽度能放进屏幕时左右各留 4 DIP；放不下就贴左边，保证左侧内容可见。
            if (width <= virtualScreen.Width - 8.0)
            {
                // 右边界越界：向左推回，4 DIP 是视觉安全边距。
                if (left + width > virtualScreen.Right - 4.0)
                {
                    left = virtualScreen.Right - width - 4.0;
                }
                // 左边界越界：向右推回。
                if (left < virtualScreen.Left + 4.0)
                {
                    left = virtualScreen.Left + 4.0;
                }
            }
            // 宽度超过整屏可用宽度：无法两侧同时留白，直接贴左边界显示。
            else
            {
                left = virtualScreen.Left + 4.0;
            }

            // 高度方向同理：能放下就上下夹住，放不下贴顶。
            if (height <= virtualScreen.Height - 8.0)
            {
                // 下边界越界：向上推回。
                if (top + height > virtualScreen.Bottom - 4.0)
                {
                    top = virtualScreen.Bottom - height - 4.0;
                }
                // 上边界越界：向下推回。
                if (top < virtualScreen.Top + 4.0)
                {
                    top = virtualScreen.Top + 4.0;
                }
            }
            // 高度超过整屏可用高度：只能贴顶，保证至少第一行文字可见。
            else
            {
                top = virtualScreen.Top + 4.0;
            }

            // 0.1 DIP 容差避免浮点误差导致每次 SizeChanged 都重复设置位置。
            if (Math.Abs(left - Left) > 0.1 || Math.Abs(top - Top) > 0.1)
            {
                SetWindowPosition(left, top);
                CapturePosition();
                // 通知控制器：尺寸变化引起的位置修正也要落盘。
                if (PositionChanged != null)
                {
                    PositionChanged(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>100 ms 时钟心跳：刷新文本并检查提醒/日程；高频路径，禁止写日志。</summary>
        private void OnClockTick(object sender, EventArgs e)
        {
            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            UpdateClock(now, false);
            CheckDueReminders(now);
            CheckDueSchedules(now);
        }

        /// <summary>
        /// 3 s 置顶轮询：其他应用抢焦点后把悬浮窗重新压回最前。
        /// 设置窗口/菜单打开、关闭置顶或窗口不可见时跳过。
        /// </summary>
        private void OnTopmostTick(object sender, EventArgs e)
        {
            // 任一条件不满足都不该重压，否则会压住设置窗口或把隐藏窗口顶出来。
            if (!_loaded || _topmostWatchSuspended || !_settings.Topmost || !IsVisible)
            {
                return;
            }
            NativeMethods.SetTopmost(new WindowInteropHelper(this).Handle, true);
        }

        /// <summary>
        /// 按当前设置刷新全部视觉：时间字体/颜色/阴影/3D、提醒标题与边框、天气行。
        /// 只改渲染属性，不触发布局落盘；最后强制刷新一次时间文本。
        /// </summary>
        private void ApplyVisuals()
        {
            Color color = _settings.TimeColor;
            FontFamily family = FontCatalog.GetFontFamily(_settings.FontFamilyName);

            _clockText.ApplyStyle(
                family,
                _settings.FontSize,
                FontWeights.SemiBold,
                color,
                _settings.ShowShadow,
                _settings.Text3D);

            _reminderTimeText.Foreground = UiTheme.BrushFrom(color);
            // 提醒边框用主色的 130/255 透明版本：呼应主题但比正文弱一档。
            _reminderPanel.BorderBrush = UiTheme.BrushFrom(
                Color.FromArgb(130, color.R, color.G, color.B));

            double weatherSize = _settings.WeatherFontSize;
            Color weatherColor = _settings.WeatherColor;
            _weatherText.FontFamily = FontCatalog.GetFontFamily(_settings.WeatherFontFamilyName);
            _weatherText.FontSize = weatherSize;
            _weatherText.FontWeight = _settings.WeatherFontBold ? FontWeights.Bold : FontWeights.Normal;
            _weatherText.FontStyle = _settings.WeatherFontItalic ? FontStyles.Italic : FontStyles.Normal;
            // 天气行上边距随字号放大，最小 2 DIP，避免小字号时贴住时间。
            _weatherText.Margin = new Thickness(0, Math.Max(2.0, weatherSize * 0.20), 0, 0);
            // 天气文字固定 232/255 透明度：比主时间略淡，形成层次又保持可读。
            _weatherText.Foreground = UiTheme.BrushFrom(
                Color.FromArgb(232, weatherColor.R, weatherColor.G, weatherColor.B));
            // 阴影颜色按亮度反转：深色字配白影、浅色字配黑影，保证任何桌面上都有轮廓。
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
            // 关闭阴影时显式置 null：残留的 Effect 会继续走离屏渲染，白耗性能。
            else
            {
                _weatherText.Effect = null;
            }

            UpdateWeatherVisibility();

            // 清掉缓存并强制刷新：设置变化后即使文本没变也要重绘（字体/颜色可能变了）。
            _lastDisplayText = null;
            UpdateClock(true);
        }

        /// <summary>
        /// 天气行的显隐决策：只有开启天气且有非空文本才显示。
        /// 可见性变化会改变窗口高度，布局完成后要重新夹回屏幕内。
        /// </summary>
        private void UpdateWeatherVisibility()
        {
            bool show = _settings.WeatherEnabled && !string.IsNullOrWhiteSpace(_weatherText.Text);
            Visibility target = show ? Visibility.Visible : Visibility.Collapsed;
            // 只在可见性真正变化时记日志并重排，避免每次刷新都触发 UpdateLayout。
            if (_weatherText.Visibility != target)
            {
                _weatherText.Visibility = target;
                AppLog.Info("Clock", "天气行" + (show ? "显示：" + _weatherText.Text : "隐藏（无数据或已关闭）"));
                // 未 Loaded 时布局由 OnLoaded 统一处理，这里只改属性。
                if (_loaded)
                {
                    UpdateLayout();
                    Dispatcher.BeginInvoke(
                        new Action(EnsureVisible),
                        DispatcherPriority.Background);
                }
            }
        }

        /// <summary>按当前时区取“现在”并刷新文本；force=true 时即使文本相同也重绘。</summary>
        private void UpdateClock(bool force)
        {
            UpdateClock(TimeZoneCatalog.GetNow(_timeZone), force);
        }

        /// <summary>
        /// 计算并写入时间文本。只在文本变化（或强制）时赋值，避免 100 ms tick 触发无谓重绘；
        /// 天气 token 仅在开启天气时传入，关闭时统一传 null。
        /// </summary>
        private void UpdateClock(DateTime now, bool force)
        {
            WeatherInfo weather = _settings.WeatherEnabled ? _weatherInfo : null;
            string display = FormatEngine.IsTokenMode(_settings.FormatMode)
                ? FormatEngine.Format(now, weather, _settings.TokenTimeFormat)
                : TimeFormatter.Format(now, _settings.TimeFormat);
            // Ordinal 比较：时间文本只含 ASCII 与固定符号，比区域感知比较更快也更确定。
            if (force || !string.Equals(display, _lastDisplayText, StringComparison.Ordinal))
            {
                _clockText.Text = display;
                _lastDisplayText = display;
            }
        }

        /// <summary>时间格式串是否已包含天气 token；包含时不再单独显示天气行，避免重复。</summary>
        private bool TimeFormatUsesWeather()
        {
            return FormatEngine.IsTokenMode(_settings.FormatMode)
                && FormatEngine.ContainsWeatherToken(_settings.TokenTimeFormat);
        }

        /// <summary>
        /// 计算天气行文本：未开启/无数据/时间格式已含天气 token 时返回空串。
        /// 自定义 WeatherFormat 优先，否则回落到天气服务拼好的 DisplayText。
        /// </summary>
        private string BuildWeatherText(DateTime now)
        {
            // 天气关闭或取数失败：返回空串，调用方据此隐藏整行。
            if (!_settings.WeatherEnabled || _weatherInfo == null)
            {
                return string.Empty;
            }
            // 时间格式里已有天气 token：单独天气行会重复展示同一信息。
            if (TimeFormatUsesWeather())
            {
                return string.Empty;
            }
            // 用户自定义天气格式优先；空白格式回落到服务端拼好的默认文本。
            if (!string.IsNullOrWhiteSpace(_settings.WeatherFormat))
            {
                return FormatEngine.Format(now, _weatherInfo, _settings.WeatherFormat);
            }
            return _weatherInfo.DisplayText;
        }

        /// <summary>天气数据或设置变化后重算天气行；文本变化时重排布局并夹回屏幕。</summary>
        private void RefreshWeatherText()
        {
            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            string text = BuildWeatherText(now);
            bool changed = !string.Equals(_weatherText.Text, text, StringComparison.Ordinal);
            _weatherText.Text = text;
            UpdateWeatherVisibility();
            // 文本变化会改变窗口宽高；延迟到布局完成后再做可见区修正。
            if (changed && _loaded)
            {
                UpdateLayout();
                Dispatcher.BeginInvoke(
                    new Action(EnsureVisible),
                    DispatcherPriority.Background);
            }
            UpdateClock(now, true);
        }

        /// <summary>
        /// 检查到点的定时提醒：同一分钟同一提醒每天只触发一次（LastTriggeredDate 去重）。
        /// 100 ms 心跳调用，只入队气泡并抛事件，不在这里写日志。
        /// </summary>
        private void CheckDueReminders(DateTime now)
        {
            // 没有提醒配置时直接返回，避免高频心跳里空转。
            if (_settings.Reminders == null || _settings.Reminders.Count == 0)
            {
                return;
            }

            string currentTime = now.ToString("HH:mm");
            string today = now.ToString("yyyy-MM-dd");

            // 逐条比对：匹配失败的分支都 continue，命中后立刻写回 LastTriggeredDate 去重。
            for (int i = 0; i < _settings.Reminders.Count; i++)
            {
                Reminder reminder = _settings.Reminders[i];
                // 容错：旧配置里可能出现 null 项；已禁用的提醒也不参与。
                if (reminder == null || !reminder.Enabled)
                {
                    continue;
                }
                // 时间必须精确到分钟相等（HH:mm），不支持秒级提醒。
                if (!string.Equals(reminder.Time, currentTime, StringComparison.Ordinal))
                {
                    continue;
                }
                // 提醒模式（如仅游戏模式）与当前模式不匹配时跳过。
                if (!ReminderModes.AppliesToState(reminder.Mode, _settings.CurrentMode))
                {
                    continue;
                }
                // 今天已经触发过：即使心跳在同一分钟内多次命中也不再重复弹。
                if (string.Equals(reminder.LastTriggeredDate, today, StringComparison.Ordinal))
                {
                    continue;
                }

                reminder.LastTriggeredDate = today;
                EnqueueReminder(new ReminderNotice(reminder.Time, reminder.Message));

                // 事件由控制器订阅，用于播放声音或外部联动；未订阅时只显示气泡。
                if (ReminderTriggered != null)
                {
                    ReminderTriggered(this, new ReminderEventArgs(reminder));
                }
            }
        }

        /// <summary>
        /// 检查到点的日程：按 ScheduleService 计算最近一次应触发时刻，
        /// 用 occurrenceKey 去重，避免同一日程在多次心跳里重复入队。
        /// </summary>
        private void CheckDueSchedules(DateTime now)
        {
            // 没有日程配置时直接返回，避免高频心跳里空转。
            if (_settings.Schedules == null || _settings.Schedules.Count == 0)
            {
                return;
            }

            // 逐条求“当前应触发”的发生时刻；命中后用 occurrenceKey 去重并清空待触发状态。
            for (int i = 0; i < _settings.Schedules.Count; i++)
            {
                ScheduleItem schedule = _settings.Schedules[i];
                // 容错：跳过 null 项与已禁用日程。
                if (schedule == null || !schedule.Enabled)
                {
                    continue;
                }

                DateTime occurrence;
                // 没有“当前应该触发”的发生时刻（未到点/已过期）就跳过。
                if (!ScheduleService.TryFindDueOccurrence(schedule, now, out occurrence))
                {
                    continue;
                }

                string occurrenceKey = ScheduleService.GetOccurrenceKey(occurrence);
                // 同一发生时刻已经处理过：直接跳过，防止重复提醒。
                if (string.Equals(schedule.LastTriggeredKey, occurrenceKey, StringComparison.Ordinal))
                {
                    continue;
                }

                schedule.LastTriggeredKey = occurrenceKey;
                EnqueueScheduleNotice(schedule, occurrence);

                // 事件供控制器做外部联动；未订阅时仅显示气泡。
                if (ScheduleTriggered != null)
                {
                    ScheduleTriggered(this, new ScheduleTriggeredEventArgs(schedule, occurrence));
                }
            }
        }

        /// <summary>
        /// 把日程转成提醒气泡内容：标题 + 可选备注，标签用“分类日程”。
        /// 发生时刻用 InvariantCulture 格式化，避免区域设置改变小时制。
        /// </summary>
        private void EnqueueScheduleNotice(ScheduleItem schedule, DateTime occurrence)
        {
            string label = ScheduleCategories.DisplayName(schedule.Type) + "日程";
            System.Text.StringBuilder message = new System.Text.StringBuilder();
            message.Append(schedule.Title);
            // 备注为空或全空白时不追加空行，保持气泡紧凑。
            if (!string.IsNullOrWhiteSpace(schedule.Note))
            {
                message.Append("\n");
                message.Append(schedule.Note.Trim());
            }
            EnqueueReminder(new ReminderNotice(
                occurrence.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                message.ToString(),
                label));
        }

        /// <summary>
        /// 提醒入队策略：当前没有气泡时立即显示，否则排队等上一条消失后再显示。
        /// 队列顺序即触发顺序，避免同时到点时互相覆盖。
        /// </summary>
        private void EnqueueReminder(ReminderNotice notice)
        {
            // 已有气泡可见：排队，由 DismissCurrentReminder 负责出队。
            if (_reminderVisible)
            {
                _reminderQueue.Enqueue(notice);
                return;
            }
            ShowReminder(notice);
        }

        /// <summary>
        /// 显示一条提醒气泡：写入文案、闪烁提示、可选声音，并启动自动关闭与悬停关闭计时。
        /// 这是用户可见的状态跃迁，记录一条日志（不是每次 tick）。
        /// </summary>
        private void ShowReminder(ReminderNotice notice)
        {
            _reminderTimeText.Text = notice.Time + " · " + notice.Label;
            _reminderMessageText.Text = notice.Message;
            _reminderPanel.Visibility = Visibility.Visible;
            // 先清掉旧的透明度动画，否则重新显示时会从上一次闪烁的中间值开始。
            _reminderPanel.BeginAnimation(UIElement.OpacityProperty, null);
            _reminderPanel.Opacity = 1.0;
            int blinkCount = _settings.ReminderBlinkCount;
            // 闪烁次数为 0 时不加动画，避免 RepeatBehavior(0) 直接停在不透明状态。
            if (blinkCount > 0)
            {
                DoubleAnimation blink = new DoubleAnimation();
                blink.From = 1.0;
                blink.To = 0.18;
                blink.Duration = TimeSpan.FromMilliseconds(260);
                blink.AutoReverse = true;
                blink.RepeatBehavior = new RepeatBehavior(blinkCount);
                // Stop：动画结束后回到代码设置的 Opacity=1，而不是停在 0.18。
                blink.FillBehavior = FillBehavior.Stop;
                _reminderPanel.BeginAnimation(UIElement.OpacityProperty, blink);
            }

            // 声音开关独立于显示时长，关闭后静默但气泡照常显示。
            if (_settings.ReminderSoundEnabled)
            {
                AlarmSound.Play();
            }

            _reminderVisible = true;
            _reminderTimer.Stop();
            _reminderTimer.Interval = TimeSpan.FromSeconds(_settings.ReminderDisplaySeconds);
            _reminderTimer.Start();
            StartReminderHoverWatch();

            AppLog.Info("Clock", "显示提醒气泡：" + notice.Time + " · " + notice.Label
                + "（" + _settings.ReminderDisplaySeconds + " 秒后自动关闭，悬停关闭="
                + _settings.ReminderHoverCloseEnabled + "）");

            UpdateLayout();
            Dispatcher.BeginInvoke(
                new Action(EnsureVisible),
                DispatcherPriority.Background);
        }

        /// <summary>设置变更后重排提醒计时器：气泡未显示时只清理悬停计时，不重启显示计时。</summary>
        private void ApplyReminderDisplaySettings()
        {
            // 没有可见气泡：停掉悬停计时并清空起始时间，等下次 ShowReminder 再启动。
            if (!_reminderVisible)
            {
                _reminderHoverTimer.Stop();
                _reminderHoverStartedUtc = null;
                return;
            }

            // 已在显示：按新时长重新计时，并用新的悬停开关重置悬停监视。
            _reminderTimer.Stop();
            _reminderTimer.Interval = TimeSpan.FromSeconds(_settings.ReminderDisplaySeconds);
            _reminderTimer.Start();
            StartReminderHoverWatch();
        }

        /// <summary>启动/停止“鼠标悬停 3 秒关闭”计时器；关闭该功能时同时清空起始时间。</summary>
        private void StartReminderHoverWatch()
        {
            _reminderHoverStartedUtc = null;
            // 开启悬停关闭才轮询，关闭时停表避免 150 ms 空转。
            if (_settings.ReminderHoverCloseEnabled)
            {
                _reminderHoverTimer.Start();
            }
            // 关闭悬停关闭：停表，鼠标停在气泡上也不会被 150 ms 轮询误判。
            else
            {
                _reminderHoverTimer.Stop();
            }
        }

        /// <summary>提醒显示计时到期：关闭当前气泡，若队列非空则继续显示下一条。</summary>
        private void OnReminderTimerTick(object sender, EventArgs e)
        {
            DismissCurrentReminder();
        }

        /// <summary>
        /// 150 ms 悬停轮询：指针停在提醒气泡内满 3 秒就关闭气泡。
        /// 高频路径（鼠标移动级别），不写任何日志；只在真正关闭时改状态。
        /// </summary>
        private void OnReminderHoverTick(object sender, EventArgs e)
        {
            // 气泡不可见/功能关闭/尚未完成布局时，没有可命中的矩形，清空计时即可。
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
            // 取光标失败（桌面切换/会话断开等）时放弃本轮，避免误判为“在气泡内”。
            if (!NativeMethods.GetCursorPos(out cursor))
            {
                _reminderHoverStartedUtc = null;
                return;
            }

            Point topLeft = _reminderPanel.PointToScreen(new Point(0, 0));
            Point bottomRight = _reminderPanel.PointToScreen(
                new Point(_reminderPanel.ActualWidth, _reminderPanel.ActualHeight));
            // 用屏幕坐标做矩形命中：PointToScreen 已含 DPI 缩放，不能再混用 DIP。
            bool inside = cursor.X >= topLeft.X
                && cursor.X <= bottomRight.X
                && cursor.Y >= topLeft.Y
                && cursor.Y <= bottomRight.Y;

            // 指针移出：清空悬停起点，下次进入重新计 3 秒。
            if (!inside)
            {
                _reminderHoverStartedUtc = null;
                return;
            }

            // 首次命中：只记起点，本轮不关闭。
            if (!_reminderHoverStartedUtc.HasValue)
            {
                _reminderHoverStartedUtc = DateTime.UtcNow;
                return;
            }

            // 连续悬停满 3 秒才关闭，避免鼠标扫过就误关。
            if ((DateTime.UtcNow - _reminderHoverStartedUtc.Value).TotalSeconds >= 3.0)
            {
                _reminderHoverStartedUtc = null;
                DismissCurrentReminder();
            }
        }

        /// <summary>
        /// 关闭当前提醒气泡；队列里还有提醒时异步显示下一条。
        /// 清动画再隐藏，防止动画回调在隐藏后继续改 Opacity。
        /// </summary>
        private void DismissCurrentReminder()
        {
            _reminderTimer.Stop();
            _reminderHoverTimer.Stop();
            _reminderHoverStartedUtc = null;
            _reminderPanel.BeginAnimation(UIElement.OpacityProperty, null);
            _reminderPanel.Visibility = Visibility.Collapsed;
            _reminderVisible = false;

            // 队列非空：排到 Background 再显示，避免在动画/计时回调里重入布局。
            if (_reminderQueue.Count > 0)
            {
                ReminderNotice next = _reminderQueue.Dequeue();
                Dispatcher.BeginInvoke(
                    new Action(delegate { ShowReminder(next); }),
                    DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// 启动时恢复上次保存的位置；保存位置已不在可见区域（换显示器/分辨率变化）时回默认位。
        /// </summary>
        private void RestorePosition()
        {
            // 只有左右坐标都保存过才值得恢复；缺一不可，否则默认位更可靠。
            if (_settings.Left.HasValue && _settings.Top.HasValue)
            {
                SetWindowPosition(_settings.Left.Value, _settings.Top.Value);
                // 恢复后仍有足够可见面积就沿用；否则继续走默认位。
                if (HasUsableVisibleArea())
                {
                    return;
                }
                // 保存位置已不可用（换显示器/分辨率）：回退默认位并留一条 Warn 便于排查。
                AppLog.Warn(
                    "Clock",
                    "保存的悬浮窗位置不可见（Left=" + _settings.Left.Value
                        + ", Top=" + _settings.Top.Value + "），回退到默认位置");
            }
            ResetPositionToDefault();
        }

        /// <summary>把窗口放到主屏工作区右上角（右边距/上边距各 DefaultMargin）。</summary>
        private void ResetPositionToDefault()
        {
            Rect workArea = SystemParameters.WorkArea;
            // 首次显示前 ActualWidth/Height 可能为 0，用 300×90 的经验尺寸兜底定位。
            double width = ActualWidth > 0 ? ActualWidth : 300;
            double height = ActualHeight > 0 ? ActualHeight : 90;
            // 以工作区（不含任务栏）为准，避免默认位置被任务栏遮住。
            double left = workArea.Right - width - DefaultMargin;
            double top = workArea.Top + DefaultMargin;
            SetWindowPosition(left, top);
        }

        /// <summary>
        /// 程序化设置位置：期间置 _applyingPosition，抑制 OnLocationChanged 把中间态写回配置。
        /// finally 保证异常时标志也能复位，否则后续拖动将永远不保存位置。
        /// </summary>
        private void SetWindowPosition(double left, double top)
        {
            _applyingPosition = true;
            // try/finally 而不是 try/catch：位置赋值不会抛业务异常，只需保证标志复位。
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
        /// 当前保存位置是否还有“可用”的可见面积：宽至少 80 DIP、高至少 36 DIP（或窗口本身更小）。
        /// 阈值小于窗口尺寸，允许用户把窗口部分移出屏幕，但保证还能拖回来。
        /// </summary>
        private bool HasUsableVisibleArea()
        {
            Rect virtualScreen = GetVirtualScreen();
            double width = Math.Max(ActualWidth, 1.0);
            double height = Math.Max(ActualHeight, 1.0);
            // 与整个虚拟桌面求交，多显示器拼接处也按并集处理。
            double visibleWidth = Math.Min(Left + width, virtualScreen.Right) - Math.Max(Left, virtualScreen.Left);
            double visibleHeight = Math.Min(Top + height, virtualScreen.Bottom) - Math.Max(Top, virtualScreen.Top);
            return visibleWidth >= Math.Min(80.0, width) && visibleHeight >= Math.Min(36.0, height);
        }

        /// <summary>
        /// 把窗口拉回可见区：保证至少露出 90×40 DIP（或窗口本身更小）。
        /// 与 KeepInsideScreenAfterResize 的“整窗夹紧”不同，这里只保证可抓取。
        /// </summary>
        private void EnsureVisible()
        {
            // 位置尚未恢复时不做修正，交由 RestorePosition 决定初始位置。
            if (!_positionReady)
            {
                return;
            }

            Rect virtualScreen = GetVirtualScreen();
            double width = Math.Max(ActualWidth, 1.0);
            double height = Math.Max(ActualHeight, 1.0);
            double left = Left;
            double top = Top;

            // 90×40 DIP 是“还能看到时间并拖回来”的最小可见尺寸。
            double minVisibleWidth = Math.Min(90.0, width);
            double minVisibleHeight = Math.Min(40.0, height);

            // 左/右越界：分别推回到刚好露出最小可见宽度。
            if (left + width < virtualScreen.Left + minVisibleWidth)
            {
                left = virtualScreen.Left + minVisibleWidth - width;
            }
            // 右侧越出：向左拉回到只露最小可见宽度，仍保留可抓取区域。
            if (left > virtualScreen.Right - minVisibleWidth)
            {
                left = virtualScreen.Right - minVisibleWidth;
            }
            // 上/下越界同理，按最小可见高度推回。
            if (top + height < virtualScreen.Top + minVisibleHeight)
            {
                top = virtualScreen.Top + minVisibleHeight - height;
            }
            // 下侧越出：向上拉回，保证标题行/时间仍可见。
            if (top > virtualScreen.Bottom - minVisibleHeight)
            {
                top = virtualScreen.Bottom - minVisibleHeight;
            }

            // 0.1 DIP 容差避免浮点误差导致重复 SetWindowPosition/重复落盘。
            if (Math.Abs(left - Left) > 0.1 || Math.Abs(top - Top) > 0.1)
            {
                SetWindowPosition(left, top);
                CapturePosition();
                // 位置被修正也属于用户可见变化，通知控制器防抖保存。
                if (PositionChanged != null)
                {
                    PositionChanged(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 整个虚拟桌面的 DIP 矩形（多显示器时是各屏并集，主屏不一定在 0,0）。
        /// 必须用 VirtualScreen* 而不是 WorkArea，否则副屏上的窗口会被误判越界。
        /// </summary>
        private Rect GetVirtualScreen()
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }

        /// <summary>把当前 WPF 位置写回设置副本；位置尚未恢复时不写，避免覆盖用户保存值。</summary>
        private void CapturePosition()
        {
            // 初始化阶段 Left/Top 还是临时值（-10000 等），写回会毁掉已保存位置。
            if (!_positionReady)
            {
                return;
            }
            _settings.Left = Left;
            _settings.Top = Top;
        }

        /// <summary>
        /// 无条件写回位置，供“先记录再延迟恢复”的初始化路径使用。
        /// 调用方必须自行保证此时 Left/Top 已是最终值。
        /// </summary>
        private void CapturePositionQuietly()
        {
            _settings.Left = Left;
            _settings.Top = Top;
        }

        /// <summary>
        /// 提醒气泡的内容载体：时间、正文、标签三要素，构造时只做字符串赋值。
        /// 作为队列元素不可变，避免显示过程中被外部修改。
        /// </summary>
        private sealed class ReminderNotice
        {
            /// <summary>普通提醒：标签固定为“提醒”。</summary>
            public ReminderNotice(string time, string message)
                : this(time, message, "提醒")
            {
            }

            /// <summary>预览/测试提醒：isPreview 决定标签文案，便于用户区分真实提醒。</summary>
            public ReminderNotice(string time, string message, bool isPreview)
                : this(time, message, isPreview ? "测试提醒" : "提醒")
            {
            }

            /// <summary>完整构造：日程提醒用“分类日程”作为标签。</summary>
            public ReminderNotice(string time, string message, string label)
            {
                Time = time;
                Message = message;
                Label = label;
            }

            /// <summary>气泡标题左侧的时间文本（HH:mm）。</summary>
            public string Time { get; private set; }
            /// <summary>气泡正文，可能含换行（日程备注）。</summary>
            public string Message { get; private set; }
            /// <summary>气泡标题右侧的标签：提醒/测试提醒/分类日程。</summary>
            public string Label { get; private set; }
        }
    }
}
