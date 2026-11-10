// 任务栏时钟覆盖窗：用一个不透明窗口盖住原生 TrayClockWClass，自己绘制两行时间。
// 契约：只“覆盖”，绝不移动/隐藏/注入系统时钟——覆盖窗消失后原时钟立即恢复，进程崩溃也不丢。
// 外观：字体取原生时钟 WM_GETFONT，字号 = 原生字号 × TaskbarFontScale/100；背景与文字色按实际像素采样推算。
// 节奏：250 ms 重绘并抬到最前（任务栏展开/重绘后会抢 TOPMOST），1 s 重同步几何与全屏状态。
// 已知坑：RaiseTopmost 不得带 SWP_SHOWWINDOW，否则会把刚 Hide 的覆盖窗重新显示、顶掉全屏隐藏；
// 全屏恢复必须在 SetVisible(true) 里显式 Show()。面板只由悬停开合，左键单击转给系统日历面板。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GameClock
{

    /// <summary>
    /// 任务栏时钟：用一个不透明的覆盖窗盖住原生 TrayClockWClass，自己绘制两行时间。
    /// 不移动、不隐藏系统时钟——覆盖窗消失后系统时钟立即恢复，进程异常退出也不会留下空白。
    /// 外观跟随系统任务栏：字体取原生时钟字体，背景取原生时钟上下边缘颜色，文字颜色按背景亮度选择。
    /// </summary>
    internal sealed class TaskbarClockWindow : Window
    {

        // ===== 原生窗口类名 =====

        /// <summary>任务栏根窗口类名，FindWindow 的入口。</summary>
        private const string ShellTrayClassName = "Shell_TrayWnd";

        /// <summary>托盘/通知区容器类名，原生时钟通常挂在它下面。</summary>
        private const string TrayNotifyClassName = "TrayNotifyWnd";

        /// <summary>系统时钟窗口类名；覆盖目标，只读不改。</summary>
        private const string TrayClockClassName = "TrayClockWClass";

        /// <summary>任务栏「操作中心」按钮类名；覆盖区向右扩到它，换取排版宽度。</summary>
        private const string ActionCenterClassName = "TrayButton";

        /// <summary>与「操作中心」按钮允许的水平间隙（物理像素）；正常是 0，留 2 px 容差。</summary>
        private const int ActionCenterMaxGap = 2;

        // ===== 节奏常量（毫秒） =====

        /// <summary>重绘并抬到最前的周期：任务栏随时会把自己提到 TOPMOST 前面。</summary>
        private const int TickIntervalMs = 250;

        /// <summary>重新同步几何与全屏状态的周期；比 tick 慢，避免频繁 FindWindow。</summary>
        private const int SyncIntervalMs = 1000;

        /// <summary>悬停展开面板的延迟，避免鼠标划过就弹。</summary>
        private const int HoverDelayMs = 420;

        /// <summary>鼠标移开后收起面板的延迟，给用户移进面板的缓冲。</summary>
        private const int PanelCloseDelayMs = 360;

        /// <summary>主题/尺寸变化后延迟重采样，合并连续的多条广播消息。</summary>
        private const int ResampleDelayMs = 500;

        /// <summary>
        /// 左键交给系统时钟后，覆盖窗保持隐藏的最长时间：够让这次合成点击的按下/松开消息
        /// 全部落地（否则松开消息会落回自己身上再触发一次单击，表现为连闪），
        /// 又不至于让系统原生格式停留到肉眼可辨。
        /// </summary>
        private const int CalendarRestoreDelayMs = 260;

        /// <summary>
        /// 两次左键单击之间的最小间隔：合成点击的松开消息可能回落到自己身上，
        /// 冷却期内重复触发直接忽略，作为防连闪的第二道闸。
        /// </summary>
        private const int CalendarClickCooldownMs = 600;

        // ===== 控件与计时器 =====

        /// <summary>设置副本：构造时克隆并 Normalize，运行期由 ApplySettings 局部更新。</summary>
        private readonly AppSettings _settings;

        /// <summary>唯一的文本块；两行内容靠格式里的换行，居中显示。</summary>
        private readonly TextBlock _text;

        /// <summary>250 ms 重绘 + 置顶。</summary>
        private readonly DispatcherTimer _tickTimer;

        /// <summary>1 s 同步几何、全屏状态与原生时钟矩形。</summary>
        private readonly DispatcherTimer _syncTimer;

        /// <summary>悬停 420 ms 后展开面板。</summary>
        private readonly DispatcherTimer _hoverTimer;

        /// <summary>鼠标移开 360 ms 后收起面板。</summary>
        private readonly DispatcherTimer _panelCloseTimer;

        /// <summary>主题变化后延迟重采样背景与字体。</summary>
        private readonly DispatcherTimer _resampleTimer;

        // ===== 运行期数据：天气、时区与面板 =====

        /// <summary>最近一次天气；null 表示尚未取到或取数失败。</summary>
        private WeatherInfo _weather;

        /// <summary>当前时区缓存，由 TimeZoneId 解析；所有取时间都走它。</summary>
        private TimeZoneInfo _timeZone;

        /// <summary>今日面板窗口；懒创建，关闭后置空。</summary>
        private TaskbarPanelWindow _panel;

        // ===== 原生窗口句柄与矩形 =====

        /// <summary>原生 TrayClockWClass 的句柄；只用于读字体与矩形，绝不修改它。</summary>
        private IntPtr _clockWindow;

        /// <summary>原生时钟矩形（屏幕物理像素），覆盖窗与面板都按它定位。</summary>
        private NativeMethods.RECT _clockRect;

        /// <summary>是否已成功取到过矩形；未定位前不排版、不做命中测试。</summary>
        private bool _hasClockRect;

        /// <summary>是否已 Start 过；Stop 之后计时器回调靠它短路。</summary>
        private bool _started;

        /// <summary>
        /// 正在为“打开系统日历”临时隐藏覆盖窗：true 期间 OnTick / OnSyncTick / SetVisible 全部让路，
        /// 避免任何一条路径把这个刻意隐藏的窗口又显示回来，导致合成点击落空。
        /// </summary>
        private bool _calendarPending;

        /// <summary>隐藏覆盖窗交给系统时钟的时刻（<see cref="Environment.TickCount"/>）。</summary>
        private int _calendarHiddenAt;

        /// <summary>上一次左键单击转交系统日历的时刻，用于 <see cref="CalendarClickCooldownMs"/> 冷却判断。</summary>
        private int _lastCalendarClickAt;

        /// <summary>
        /// 悬停展开是否已“武装”。点过时钟后置 false：光标还停在时钟上，若继续按 420 ms 展开
        /// 今日面板，会盖住刚弹出的系统日历；等光标先移出时钟（OnMouseLeave）再重新武装。
        /// </summary>
        private bool _hoverArmed = true;

        // ===== 外观与字体 =====

        /// <summary>当前显示器 DPI 缩放；原生像素与 DIP 之间的换算因子。</summary>
        private double _dpiScale = 1.0;

        /// <summary>原生时钟字体；读取失败时退到设置里的旧字段。</summary>
        private FontFamily _fontFamily;

        /// <summary>原生字号（DIP），TaskbarFontScale 以它为基准放大。</summary>
        private double _baseFontSize = 12.0;

        /// <summary>文字颜色，按采样背景亮度在近黑/白之间选。</summary>
        private Color _textColor = Color.FromRgb(0x1A, 0x1A, 0x1A);

        /// <summary>是否采到过可信背景；无效时先不显示，避免把时钟盖成黑块。</summary>
        private bool _hasValidBackground;

        // ===== 消息钩子与面板状态 =====

        /// <summary>WndProc 钩子来源；OnClosed 时摘钩子，避免回调触碰已销毁的窗口。</summary>
        private HwndSource _source;

        /// <summary>
        /// 构造覆盖窗：克隆设置、建立无边框不透明窗口、单一文本块与外层 Viewbox，并创建全部计时器。
        /// 构造阶段不显示、不定位、也不找原生时钟；这些推迟到 Start 与 OnSourceInitialized。
        /// </summary>
        /// <param name="settings">控制器持有的设置；内部会克隆，避免双向影响。</param>
        public TaskbarClockWindow(AppSettings settings)
        {
            // 保存克隆副本并立即 Normalize：调用方之后改设置对象不会影响已经画出来的外观。
            _settings = settings != null ? settings.Clone() : AppSettings.CreateDefault();
            _settings.Normalize();
            // 时区在构造时就解析好，之后取时间都走缓存，不重复查系统时区。
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            // 窗口样式：无边框、不透明、顶置、不占任务栏、不抢焦点；
            // 必须不透明：半透明窗口会让下面原生时钟的文字透出来形成重影。
            Title = "GameClockTaskbarClock";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            // 先用浅灰不透明底占位，真实颜色在 CaptureBackground 采样成功后替换为渐变。
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
            ShowInTaskbar = false;
            ShowActivated = false;
            // Topmost 只是初始值，任务栏会反复抢最前，实际靠 250 ms RaiseTopmost 维持。
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            // 允许获得焦点：部分系统配置下不可聚焦窗口收不到鼠标进入/离开消息；配合 ShowActivated=false 不会真抢焦点。
            Focusable = true;
            // 初始 1×1 放在屏幕外，等 SyncGeometry 找到原生时钟再定位，避免首帧闪在左上角。
            Width = 1;
            Height = 1;
            Left = -10000;
            Top = -10000;

            // 文本块只建一次，之后 UpdateText/ApplyVisuals 只改属性，不重建可视树。
            _text = new TextBlock();
            // 居中 + 不换行：两行内容由格式里的换行决定；宽度不够时先降字号，残留溢出交给 Viewbox 等比缩小。
            _text.TextAlignment = TextAlignment.Center;
            _text.TextWrapping = TextWrapping.NoWrap;
            _text.Padding = new Thickness(2, 0, 2, 0);
            _text.Foreground = new SolidColorBrush(_textColor);
            _text.FontWeight = FontWeights.Normal;
            // BlockLineHeight 让 LineHeight 精确等于“行高 = 时钟高度 / 行数”，不被字体自带行距挤出去。
            _text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            _text.VerticalAlignment = VerticalAlignment.Center;
            _text.HorizontalAlignment = HorizontalAlignment.Center;
            _text.SnapsToDevicePixels = true;
            // Display 模式 + ClearType：小字号要贴近系统渲染，边缘才不发虚。
            TextOptions.SetTextFormattingMode(_text, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(_text, TextRenderingMode.ClearType);
            // 覆盖窗本身要接收点击，但文字不参与命中测试，鼠标事件统一由窗口处理。
            _text.IsHitTestVisible = false;

            // Viewbox 只等比缩小、不放大：Fill 会把字形横向压扁，看起来「被挤瘦」，且 ClearType 会因非等比缩放发虚。
            Viewbox viewbox = new Viewbox();
            viewbox.Stretch = Stretch.Uniform;
            viewbox.StretchDirection = StretchDirection.DownOnly;
            viewbox.HorizontalAlignment = HorizontalAlignment.Stretch;
            viewbox.VerticalAlignment = VerticalAlignment.Stretch;
            viewbox.Child = _text;
            Content = viewbox;

            // 悬停展开、移开延迟收起；左键/右键由 WndProc 钩子处理。
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;

            // 计时器分工：tick 重绘+置顶，sync 同步几何，hover/close 管面板开合，resample 处理主题变化。
            _tickTimer = new DispatcherTimer(DispatcherPriority.Normal);
            _tickTimer.Interval = TimeSpan.FromMilliseconds(TickIntervalMs);
            _tickTimer.Tick += OnTick;

            _syncTimer = new DispatcherTimer(DispatcherPriority.Background);
            _syncTimer.Interval = TimeSpan.FromMilliseconds(SyncIntervalMs);
            _syncTimer.Tick += OnSyncTick;

            // 交互类计时器用 Background 优先级：界面忙时让位，避免和布局/绘制抢时间片。
            _hoverTimer = new DispatcherTimer(DispatcherPriority.Background);
            _hoverTimer.Interval = TimeSpan.FromMilliseconds(HoverDelayMs);
            _hoverTimer.Tick += OnHoverTick;

            _panelCloseTimer = new DispatcherTimer(DispatcherPriority.Background);
            _panelCloseTimer.Interval = TimeSpan.FromMilliseconds(PanelCloseDelayMs);
            _panelCloseTimer.Tick += OnPanelCloseTick;

            _resampleTimer = new DispatcherTimer(DispatcherPriority.Background);
            _resampleTimer.Interval = TimeSpan.FromMilliseconds(ResampleDelayMs);
            _resampleTimer.Tick += OnResampleTick;
        }

        /// <summary>右键菜单请求；由控制器弹出与托盘共用的菜单。</summary>
        public event EventHandler MenuRequested;

        /// <summary>面板中点击“打开设置”时触发。</summary>
        public event EventHandler SettingsRequested;

        /// <summary>
        /// 启动：取 DPI、显示窗口，立刻同步一次几何与文字，然后开两个常驻计时器。
        /// 幂等（_started 短路）；由 AppController.ApplyDisplaySurfaces 在开关变化时调用。
        /// </summary>
        public void Start()
        {
            // 幂等：控制器在设置变更时可能重复调用 Start。
            if (_started)
            {
                return;
            }

            _started = true;
            // 取一次句柄：OnSourceInitialized 已经套好覆盖窗样式，这里只是确认窗口已创建。
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // DPI 在显示前后可能不同（跨屏拖动/缩放变更），Start 时先取一次作为基线。
            _dpiScale = GetDpiScale();
            // 先 Show() 再同步：隐藏窗口的 DPI 与尺寸都不可靠。
            Show();
            // force=true 强制重采样字体与背景，忽略缓存。
            SyncGeometry(true);
            // 强制重排版：刚启动时文本为空，需要走完整路径。
            UpdateText(true);
            // tick 负责重绘与置顶，sync 负责跟随任务栏，周期不同不能合并。
            _tickTimer.Start();
            _syncTimer.Start();
        }

        /// <summary>
        /// 停止：落标志位、停全部计时器、收起面板并隐藏覆盖窗；由控制器在关闭显示位置时调用。
        /// 不销毁窗口对象，再次 Start 可复用同一实例。
        /// </summary>
        public void Stop()
        {
            // 先落标志位：计时器回调靠 _started 判断是否已停止，避免停一半又被唤醒。
            _started = false;
            _tickTimer.Stop();
            _syncTimer.Stop();
            _hoverTimer.Stop();
            _panelCloseTimer.Stop();
            _resampleTimer.Stop();
            // 面板先收起来，否则主窗隐藏后它还会浮在桌面上。
            HidePanel();
            // 只在可见时 Hide，减少无谓的窗口消息。
            if (IsVisible)
            {
                Hide();
            }
        }

        /// <summary>
        /// 应用新设置：只挑外观/数据相关字段写入内部副本，时区重新解析；已启动时立即重绘并重同步。
        /// 传 null 视为“不改”，设置窗口取消编辑时会走到这个分支。
        /// </summary>
        /// <param name="settings">控制器的设置对象；内部先克隆再取字段。</param>
        public void ApplySettings(AppSettings settings)
        {
            // 空值按“不改”处理：设置窗口取消时控制器可能传 null。
            if (settings == null)
            {
                return;
            }

            // 与构造一致：克隆 + Normalize，保证旧配置缺字段时仍能画出合理外观。
            AppSettings copy = settings.Clone();
            copy.Normalize();
            // 逐字段挑需要的外观/数据项，不整体替换 _settings，避免冲掉本窗口的运行时状态。
            _settings.TaskbarFormat = copy.TaskbarFormat;
            _settings.TaskbarFormatMode = copy.TaskbarFormatMode;
            _settings.TaskbarDotNetFormat = copy.TaskbarDotNetFormat;
            // 字体/字号/颜色旧字段只为兼容保留：真实外观取自系统任务栏，仅在读取失败时兜底。
            _settings.TaskbarFontFamilyName = copy.TaskbarFontFamilyName;
            _settings.TaskbarFontSize = copy.TaskbarFontSize;
            // 缩放与加粗是用户真正可调的两项外观开关。
            _settings.TaskbarFontScale = copy.TaskbarFontScale;
            _settings.TaskbarBold = copy.TaskbarBold;
            _settings.TaskbarColorHex = copy.TaskbarColorHex;
            _settings.CurrentMode = copy.CurrentMode;
            _settings.TimeZoneId = copy.TimeZoneId;
            _settings.Reminders = copy.Reminders;
            _settings.Schedules = copy.Schedules;
            _settings.WeatherEnabled = copy.WeatherEnabled;
            _settings.WeatherCity = copy.WeatherCity;
            // 时区可能被改过，重新解析缓存。
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            // 未启动时只更新副本，等 Start 统一应用；已启动则立即重绘并重同步。
            if (_started)
            {
                UpdateText(true);
                SyncGeometry(false);
            }
        }

        /// <summary>
        /// 接收控制器异步取回的天气（null 表示失败），刷新覆盖窗文本与已展开的面板。
        /// 天气是唯一来自网络的异步数据，可能晚于窗口创建到达。
        /// </summary>
        /// <param name="weather">最新天气；null 时格式引擎退化为不带天气的文本。</param>
        public void SetWeather(WeatherInfo weather)
        {
            // 直接覆盖旧值：null 也是有效状态（取数失败），不需要保留上一次天气。
            _weather = weather;
            // 强制重绘：天气变化可能不改变时间文本，但会改变格式化结果。
            UpdateText(true);
            // 面板已展开时同步刷新，避免用户盯着旧天气。
            RefreshPanel();
        }

        /// <summary>
        /// 窗口句柄建立后的初始化：套覆盖窗扩展样式、先抬一次顶、挂 WndProc 钩子。
        /// 只调用一次；样式必须在第一次显示前套上，否则会闪一下普通窗口边框。
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            // 先取句柄：ApplyTaskbarOverlayStyles 需要有效 HWND。
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE：不进 Alt+Tab、不抢焦点，由 NativeMethods 统一封装。
            NativeMethods.ApplyTaskbarOverlayStyles(hwnd);
            // 刚创建时任务栏可能已把自己提到最前，先抬一次再进入 250 ms 周期。
            NativeMethods.RaiseTopmost(hwnd);
            // 挂 WndProc 钩子：点击、右键、主题变化都从这里进来，WPF 没有对应的高层事件。
            _source = HwndSource.FromHwnd(hwnd);
            // 极端情况下没有 HwndSource（无渲染源），判空避免空引用。
            if (_source != null)
            {
                _source.AddHook(OnWindowMessage);
            }
        }

        /// <summary>
        /// 关窗清理：停计时器、摘 WndProc 钩子、关闭面板，最后交给基类释放句柄。
        /// 顺序不能反——清理步骤都还需要有效句柄与已初始化的字段。
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            // 关窗兜底：逐个停表，否则回调会在窗口销毁后继续跑并触碰已释放句柄。
            _tickTimer.Stop();
            _syncTimer.Stop();
            _hoverTimer.Stop();
            _panelCloseTimer.Stop();
            _resampleTimer.Stop();
            // 摘钩子并把 _source 置空，防止重入 OnClosed 时重复 RemoveHook。
            if (_source != null)
            {
                _source.RemoveHook(OnWindowMessage);
                _source = null;
            }
            // 面板是独立窗口，主窗关闭时必须一起关，否则会留下一个无主的浮层。
            if (_panel != null)
            {
                _panel.Close();
                _panel = null;
            }
            // 基类会释放窗口句柄，所以放在最后调用。
            base.OnClosed(e);
        }

        /// <summary>
        /// 250 ms 心跳：刷新文本，并在窗口可见时把覆盖窗重新抬到 TOPMOST 组首位。
        /// 全屏隐藏期间不抬顶，否则会把刚 Hide() 的窗口重新显示出来。
        /// </summary>
        private void OnTick(object sender, EventArgs e)
        {
            // 左键刚把时钟交给系统：覆盖窗保持隐藏，直到光标离开时钟区域再取回来。
            // 如果这里立刻显示，合成点击的松开消息可能落回自己身上，形成“点击→隐藏→显示→再点击”的连续闪烁。
            if (_calendarPending)
            {
                if (ShouldRestoreAfterCalendarClick())
                {
                    RestoreAfterCalendarClick();
                }
                return;
            }
            // force=false：文本没变就跳过重排，省下布局开销。
            UpdateText(false);
            // 任务栏会在面板展开、任务栏重绘时把自己提到最顶层窗口组前面，
            // 覆盖窗必须重新抬到最前，否则会露出系统的原生时钟。
            // 但只在真的被压下去时才抬：无条件每 250 ms 抬顶会把任务栏的提示框、
            // 右键菜单这些后出现的顶层窗口盖到底下（用户反馈的“置顶优先级太高”）。
            // 全屏隐藏期间不提升，避免把已隐藏的窗口重新显示出来。
            if (IsVisible)
            {
                // 每次重新取句柄而不是缓存值，避免窗口重建后拿到旧句柄。
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                IntPtr shell = NativeMethods.FindWindow(ShellTrayClassName, null);
                if (!NativeMethods.IsAbove(hwnd, shell))
                {
                    NativeMethods.RaiseTopmost(hwnd);
                }
            }
        }

        /// <summary>
        /// 1 s 同步：跟随任务栏位置/尺寸、自动隐藏、全屏状态与原生时钟矩形变化。
        /// 不主动重采样（除矩形变化），重采样由 Resample 计时器负责。
        /// </summary>
        private void OnSyncTick(object sender, EventArgs e)
        {
            // 日历点击进行中：窗口刻意隐藏着，任何同步都可能带 SWP_SHOWWINDOW 把它显示回来。
            if (_calendarPending)
            {
                return;
            }
            // 每秒一次足够跟上任务栏移动；失败分支内部会自行隐藏或保持现状。
            SyncGeometry(false);
        }

        /// <summary>
        /// 主题/尺寸变化后的延迟重采样：单次触发，先停表再判断，避免连续消息反复截屏。
        /// </summary>
        private void OnResampleTick(object sender, EventArgs e)
        {
            // 单次计时器，先停：下一次变化由 WndProc 重新启动。
            _resampleTimer.Stop();
            // 只在仍启动、已定位且可见时重采样；全屏隐藏期间不折腾。
            if (_started && _hasClockRect && IsVisible)
            {
                SyncGeometry(true);
            }
        }

        /// <summary>
        /// WndProc 钩子：处理左键/右键松开与三类外观变化消息。
        /// 返回值恒为 IntPtr.Zero 且不置 handled——这些消息仍交给 WPF 正常处理。
        /// </summary>
        /// <returns>恒为 <see cref="IntPtr.Zero"/>。</returns>
        private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 局部常量：只在本钩子使用，数值与 Win32 定义一致（不引 WinForms 常量以免命名冲突）。
            const int WmLButtonUp = 0x0202;
            const int WmRButtonUp = 0x0205;
            const int WmSettingChange = 0x001A;
            const int WmThemeChanged = 0x031A;
            const int WmDwmColorizationColorChanged = 0x0320;

            // 左键松开：单击语义（展开/固定/收起）；用 Up 而非 Down，拖动任务栏时不会误触发。
            if (message == WmLButtonUp)
            {
                HandleClockLeftClick();
                return IntPtr.Zero;
            }
            // 右键松开：收起面板并请求共用菜单，菜单由控制器负责弹出。
            if (message == WmRButtonUp)
            {
                HandleClockRightClick();
                return IntPtr.Zero;
            }

            // 主题/尺寸变化只置“待重采样”标记，延迟 500 ms 再采样，避免连发消息反复截屏。
            bool resample = false;
            // WM_SETTINGCHANGE 的 lParam 可能为空，表示“所有设置都变了”。
            if (message == WmSettingChange)
            {
                // 主题切换（浅色/深色）、任务栏尺寸变化都会广播 WM_SETTINGCHANGE。
                string area = lParam != IntPtr.Zero
                    ? System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam)
                    : null;
                // 只认与任务栏外观相关的三个 area：颜色主题、窗口度量（任务栏高度/DPI）。
                resample = string.IsNullOrEmpty(area)
                    || string.Equals(area, "ImmersiveColorSet", StringComparison.Ordinal)
                    || string.Equals(area, "WindowMetrics", StringComparison.Ordinal);
            }
            // 主题消息与 DWM 强调色变化没有 area 参数，直接重采样。
            else if (message == WmThemeChanged || message == WmDwmColorizationColorChanged)
            {
                resample = true;
            }

            // 未启动时不要启动计时器，否则 Stop 之后还会被唤醒一次。
            if (resample && _started)
            {
                // 停后再启动等于重置计时：连续多条消息合并成一次采样。
                _resampleTimer.Stop();
                _resampleTimer.Start();
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 左键单击：收起今日面板，然后打开系统原生的「日历」飞出面板（等价于点系统时钟）。
        /// 今日面板只由悬停展开、移开收起，不再靠点击固定；「打开设置」走面板底部按钮或右键菜单。
        /// </summary>
        private void HandleClockLeftClick()
        {
            // 冷却期内直接忽略：合成点击的松开消息可能回落到自己身上，不挡住就会连闪。
            // 隐藏期间的重复点击同样忽略——那说明上一次移交还没走完。
            if (_calendarPending
                || unchecked(Environment.TickCount - _lastCalendarClickAt) < CalendarClickCooldownMs)
            {
                return;
            }

            // 点击前先收面板：系统日历面板和今日面板叠在一起会互相遮挡。
            HidePanel();
            // 点击后光标还停在时钟上，先解除悬停武装，免得 420 ms 后今日面板又弹出来盖住日历。
            _hoverArmed = false;

            // 覆盖窗正好压在原生时钟上，必须先把窗口隐藏（不可见的窗口不参与命中测试），
            // 点击才会落到下面的系统时钟上。合成点击发出去后先不回显：立刻显示会让这次点击的
            // 松开消息落回自己身上，又触发一次本方法，表现为“点了以后一直闪”。
            // 回显交给 OnTick：等光标离开时钟区域、或超过 CalendarRestoreDelayMs 再取回来。
            _calendarPending = true;
            _calendarHiddenAt = Environment.TickCount;
            _lastCalendarClickAt = _calendarHiddenAt;
            try
            {
                Visibility = Visibility.Hidden;
                // 窗口不可见后命中测试才会落到下面的原生时钟上，这里补一次真实点击。
                NativeMethods.ClickNativeClock();
            }
            catch (Exception)
            {
                // 合成点击失败（找不到时钟、被系统拒绝）不能把窗口留在隐藏状态，
                // 否则时钟位置会一直是系统原生格式；这里直接恢复。
                RestoreAfterCalendarClick();
            }
        }

        /// <summary>
        /// 判断是否该把覆盖窗取回来：光标已离开时钟区域，或隐藏时长已超过
        /// <see cref="CalendarRestoreDelayMs"/>（后者是主路径：点完鼠标通常还停在时钟上）。
        /// </summary>
        private bool ShouldRestoreAfterCalendarClick()
        {
            // 已 Stop 或窗口本来就没在显示：恢复也无意义。
            if (!_started)
            {
                return true;
            }

            NativeMethods.POINT point;
            if (NativeMethods.GetCursorPos(out point))
            {
                // 直接比物理像素矩形（窗口坐标要经 DPI 换算，隐藏时也未必可靠）。
                NativeMethods.RECT rect;
                if (NativeMethods.GetWindowRect(new WindowInteropHelper(this).Handle, out rect))
                {
                    if (point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top || point.Y >= rect.Bottom)
                    {
                        return true;
                    }
                }
            }

            // 光标还停在时钟上：等够一拍就恢复，别把系统原生格式亮太久。
            return unchecked(Environment.TickCount - _calendarHiddenAt) >= CalendarRestoreDelayMs;
        }

        /// <summary>
        /// 左键交给系统后把覆盖窗取回来：清标志、重新显示，并插回任务栏正上方。
        /// 重复调用是安全的（已恢复时只会再抬一次顶）。
        /// </summary>
        private void RestoreAfterCalendarClick()
        {
            _calendarPending = false;
            if (!_started)
            {
                return;
            }

            Visibility = Visibility.Visible;
            NativeMethods.RaiseTopmost(new WindowInteropHelper(this).Handle);
        }

        /// <summary>收起面板并抛出 <see cref="MenuRequested"/>，由控制器弹出与托盘共用的菜单。</summary>
        private void HandleClockRightClick()
        {
            // 右键一律先收面板，菜单与面板不同时出现。
            HidePanel();
            EventHandler handler = MenuRequested;
            // 事件可能未订阅，判空后再抛。
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>鼠标进入时钟：取消收起倒计时；面板未开时启动 420 ms 悬停展开。</summary>
        private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 鼠标回到时钟上：取消“移开即收起”的倒计时。
            _panelCloseTimer.Stop();
            // 刚点过“打开日历”、光标还没离开过：不再弹今日面板，否则会盖住系统日历面板。
            if (!_hoverArmed)
            {
                return;
            }
            // 面板已展开就不再排悬停展开，避免重复 Show。
            if (_panel == null || !_panel.IsVisible)
            {
                // 停后再启动＝重置 420 ms 计时，快速划过不会误弹面板。
                _hoverTimer.Stop();
                _hoverTimer.Start();
            }
        }

        /// <summary>鼠标离开时钟：取消悬停展开；面板开着则启动 360 ms 收起倒计时。</summary>
        private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 光标已经离开：下次真正“移回来”时才允许悬停展开，重新武装悬停。
            _hoverArmed = true;
            // 离开即取消悬停展开：还没到 420 ms 就移走，不应弹面板。
            _hoverTimer.Stop();
            // 只有面板确实开着才排收起倒计时；没开就什么都不做。
            if (_panel != null && _panel.IsVisible)
            {
                // 延时收起给用户“从时钟移进面板”的缓冲，否则鼠标一离开面板就消失。
                _panelCloseTimer.Stop();
                _panelCloseTimer.Start();
            }
        }

        /// <summary>
        /// 悬停计时到点：再次确认光标仍在时钟上且面板未开，才展开面板。
        /// </summary>
        private void OnHoverTick(object sender, EventArgs e)
        {
            // 一次性计时器，先停再判断。
            _hoverTimer.Stop();
            // 光标已经移走或面板已打开：本次悬停作废。
            if (!IsCursorOverClock() || (_panel != null && _panel.IsVisible))
            {
                return;
            }
            ShowPanel();
        }

        /// <summary>
        /// 收起倒计时到点：光标仍在时钟上或已移到面板上就再等下一次判断，否则收起。
        /// </summary>
        private void OnPanelCloseTick(object sender, EventArgs e)
        {
            // 单次计时器，先停。
            _panelCloseTimer.Stop();
            // 光标还在时钟或面板上就继续等：用户只是从时钟移向面板的途中。
            if (IsCursorOverClock() || IsCursorOverPanel())
            {
                return;
            }
            // 真正收起。
            HidePanel();
        }

        /// <summary>光标是否落在原生时钟矩形内（屏幕物理像素比较，不做 DIP 换算）。</summary>
        private bool IsCursorOverClock()
        {
            // 还没定位到原生时钟：无法判定，按“不在上面”处理。
            if (!_hasClockRect)
            {
                return false;
            }

            // GetCursorPos 返回物理像素，与 _clockRect 同坐标系。
            NativeMethods.POINT point;
            if (!NativeMethods.GetCursorPos(out point))
            {
                return false;
            }
            // 右/下边界取开区间：相邻窗口共享一条边时不会同时命中两个区域。
            return point.X >= _clockRect.Left
                && point.X < _clockRect.Right
                && point.Y >= _clockRect.Top
                && point.Y < _clockRect.Bottom;
        }

        /// <summary>光标是否落在已展开的面板窗口内。</summary>
        private bool IsCursorOverPanel()
        {
            // 面板没开就等于不在面板上。
            if (_panel == null || !_panel.IsVisible)
            {
                return false;
            }

            NativeMethods.POINT point;
            if (!NativeMethods.GetCursorPos(out point))
            {
                return false;
            }

            // 面板是独立 HWND，直接问它的窗口矩形，比在 WPF 里做命中测试更可靠。
            NativeMethods.RECT rect;
            IntPtr hwnd = new WindowInteropHelper(_panel).Handle;
            if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out rect))
            {
                return false;
            }
            return point.X >= rect.Left
                && point.X < rect.Right
                && point.Y >= rect.Top
                && point.Y < rect.Bottom;
        }

        /// <summary>
        /// 展开面板：必要前提不满足时直接放弃；否则懒创建、刷新模型、贴边显示。
        /// 面板只由悬停展开，不再支持点击固定（左键留给系统日历面板）。
        /// </summary>
        private void ShowPanel()
        {
            // 未启动、未定位到原生时钟或句柄无效时展开没有意义。
            if (!_started || !_hasClockRect || _clockWindow == IntPtr.Zero)
            {
                return;
            }

            // 懒创建：面板只在第一次展开时构造，减少启动开销。
            EnsurePanel();
            // 每次展开都重建模型：面板可能已关了许久，数据必须是最新的。
            _panel.SetModel(BuildPanelModel(TimeZoneCatalog.GetNow(_timeZone)));
            _panel.ShowNear(_clockRect, _dpiScale);
            // 面板是 TOPMOST 窗口，可能把覆盖窗压下去，重新抬一次。
            NativeMethods.RaiseTopmost(new WindowInteropHelper(this).Handle);
        }

        /// <summary>收起面板：停掉两个相关计时器并隐藏窗口。</summary>
        private void HidePanel()
        {
            // 两个与面板相关的计时器全部停掉，避免收起后又被回调拉起。
            _hoverTimer.Stop();
            _panelCloseTimer.Stop();
            // 面板可能已被销毁（Closed 里置空），判空后再 Hide。
            if (_panel != null && _panel.IsVisible)
            {
                _panel.Hide();
            }
        }

        /// <summary>懒创建面板窗口并接线：设置请求、鼠标进入/离开、关闭后置空引用。</summary>
        private void EnsurePanel()
        {
            // 已经建过就复用；Show/Hide 不销毁 HWND。
            if (_panel != null)
            {
                return;
            }

            // 面板自己不知道时钟窗，通过事件把“打开设置”转出去。
            _panel = new TaskbarPanelWindow();
            _panel.SettingsRequested += OnPanelSettingsRequested;
            // 鼠标移进面板即取消收起倒计时，否则移过去的过程中面板就没了。
            _panel.MouseEnter += delegate
            {
                _panelCloseTimer.Stop();
            };
            // 移出面板重新开始倒计时；是否真收起由 OnPanelCloseTick 再确认。
            _panel.MouseLeave += delegate
            {
                _panelCloseTimer.Stop();
                _panelCloseTimer.Start();
            };
            // 面板被关闭（例如进程退出）时清空引用，下次展开重新创建。
            _panel.Closed += delegate { _panel = null; };
        }

        /// <summary>把面板的「打开设置」请求转发给控制器；时钟窗只做转发，不直接开设置。</summary>
        private void OnPanelSettingsRequested(object sender, EventArgs e)
        {
            // 事件可能未订阅，判空后转发。
            EventHandler handler = SettingsRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>刷新已展开的面板数据；面板未显示时什么都不做（下次展开会用最新模型）。</summary>
        private void RefreshPanel()
        {
            // 没显示就不刷新，省掉一次模型构建与布局。
            if (_panel != null && _panel.IsVisible)
            {
                _panel.SetModel(BuildPanelModel(TimeZoneCatalog.GetNow(_timeZone)));
            }
        }

        /// <summary>
        /// 重新计算两行文本并写入文本块；文本未变时跳过重排，但仍刷新已展开的面板。
        /// </summary>
        /// <param name="force">true = 忽略“文本未变”的短路，强制走完整路径。</param>
        private void UpdateText(bool force)
        {
            // 统一走 TimeZoneCatalog：设置里可能选了任意时区，不能用 DateTime.Now。
            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            // 两种格式模式互斥：简洁变量走 FormatEngine，.NET 格式串走 TimeFormatter。
            string text = FormatEngine.IsTokenMode(_settings.TaskbarFormatMode)
                ? FormatEngine.Format(now, _weather, _settings.TaskbarFormat)
                : TimeFormatter.Format(now, _settings.TaskbarDotNetFormat);

            // 文本没变就跳过重排；但面板里的秒数仍在走，需要单独刷新。
            if (!force && string.Equals(text, _text.Text, StringComparison.Ordinal))
            {
                if (_panel != null && _panel.IsVisible)
                {
                    _panel.SetModel(BuildPanelModel(now));
                }
                return;
            }

            // 先赋文本再应用视觉：ApplyVisuals 要按新文本的行数与最宽行算字号。
            _text.Text = text;
            // 还没定位到原生时钟时没有可用尺寸，等 SyncGeometry 后再排版。
            if (_hasClockRect)
            {
                ApplyVisuals();
            }
            // 面板可见时跟着刷新，保证大号时间与任务栏一致。
            if (_panel != null && _panel.IsVisible)
            {
                _panel.SetModel(BuildPanelModel(now));
            }
        }

        /// <summary>
        /// 按原生时钟矩形排版文本：算行高、取字体与字号、必要时降字号，再设置文本块属性。
        /// 只改属性不重建控件；由 SyncGeometry 与 UpdateText 在尺寸/文本变化时调用。
        /// </summary>
        private void ApplyVisuals()
        {
            // 没有矩形或文本为空时无法排版，直接返回。
            if (!_hasClockRect || string.IsNullOrEmpty(_text.Text))
            {
                return;
            }

            // 原生矩形是物理像素，先换算成 DIP 才能与 WPF 字号/行高统一。
            double widthDip = (_clockRect.Right - _clockRect.Left) / _dpiScale;
            double heightDip = (_clockRect.Bottom - _clockRect.Top) / _dpiScale;
            // 只有格式里的换行会产生多行；最多按两行算，更多行会挤爆任务栏高度。
            string[] lines = _text.Text.Split('\n');
            int lineCount = Math.Max(1, Math.Min(2, lines.Length));
            // 行高等分时钟高度：两行整体刚好填满原生时钟区域。
            double lineHeight = heightDip / lineCount;
            // 左右各留 8 DIP：文本块内衬 2×2，再加一点余量，避免 ClearType 末笔贴边被裁。
            double maxWidth = Math.Max(10.0, widthDip - 8.0);

            // 字体优先用原生时钟的 WM_GETFONT 结果；取不到才退到设置里的旧字段。
            FontFamily family = _fontFamily;
            if (family == null)
            {
                family = new FontFamily(string.IsNullOrWhiteSpace(_settings.TaskbarFontFamilyName)
                    ? "Microsoft YaHei UI"
                    : _settings.TaskbarFontFamilyName);
            }

            // 基准字号同样来自原生字体，缺省 12 DIP 保证极端情况下也能画出字。
            double nativeSize = _baseFontSize > 0 ? _baseFontSize : 12.0;
            // 缩放 100 = 标准；小于 100 会小于原生字号，这里钳到 1.0 不再缩小。
            double fontScale = _settings.TaskbarFontScale > 0 ? _settings.TaskbarFontScale / 100.0 : 1.0;
            if (fontScale < 1.0)
            {
                fontScale = 1.0;
            }
            // 先把字高用满行高：行高决定字号上限，超过就会被行距挤出去。
            double size = Math.Min(lineHeight / 1.24, nativeSize * fontScale);
            if (size < nativeSize)
            {
                size = nativeSize;
            }
            if (size > lineHeight * 0.92)
            {
                size = lineHeight * 0.92;
            }

            // 加粗只由 TaskbarBold 控制，与原生字体权重无关。
            FontWeight weight = _settings.TaskbarBold ? FontWeights.Bold : FontWeights.Normal;
            Typeface typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            // 先量最宽行：横向压缩能力有限，需要据此决定是否降字号。
            double widest = MeasureWidestLine(lines, lineCount, typeface, size);

            // 再按宽度等比降字号：横向压扁字形会让字「变瘦」，宁可整行小一点也要保持字形比例。
            // 只降到原生字号的 85%，更长的格式不再压字号，交给 Viewbox 做等比缩放。
            double minSize = Math.Max(6.0, nativeSize * 0.85);
            for (int i = 0; i < 40 && widest > maxWidth && size > minSize; i++)
            {
                size -= 0.25;
                widest = MeasureWidestLine(lines, lineCount, typeface, size);
            }

            // 宽度取“最宽行 + 内衬”，不硬撑到窗口宽度：只有真的放不下时才由 Viewbox 等比缩小。
            _text.Width = Math.Max(widest + 6.0, widthDip - 4.0);
            _text.FontFamily = family;
            _text.FontSize = size;
            _text.FontWeight = weight;
            // LineHeight 与行数配合，使两行恰好铺满高度。
            _text.LineHeight = lineHeight;
            _text.Foreground = new SolidColorBrush(_textColor);
            // 清掉上次可能残留的变换，避免重复应用时叠加。
            _text.RenderTransform = Transform.Identity;
        }

        /// <summary>逐行量宽度并取最大值；用与显示一致的 DPI 测量。</summary>
        /// <returns>最宽行的宽度（DIP）；所有行为空时返回 0。</returns>
        private double MeasureWidestLine(string[] lines, int lineCount, Typeface typeface, double size)
        {
            // 逐行量宽度取最大值：Viewbox 的横向压缩量由最宽行决定。
            double widest = 0.0;
            for (int line = 0; line < lineCount; line++)
            {
                // 行数可能多于实际行（格式里只有一个换行时用空串补齐）。
                string value = line < lines.Length ? lines[line] : string.Empty;
                // 空行不参与测量，否则 FormattedText 会得到 0 宽。
                if (value.Length == 0)
                {
                    continue;
                }
                // 用与显示一致的 DPI 测量，否则宽度会按 96 DPI 算偏小。
                FormattedText formatted = new FormattedText(
                    value,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    size,
                    Brushes.Black,
                    _dpiScale);
                // 含尾随空白：宁可多留一点余量，也不要最后一个字符被裁。
                if (formatted.WidthIncludingTrailingWhitespace > widest)
                {
                    widest = formatted.WidthIncludingTrailingWhitespace;
                }
            }
            return widest;
        }

        /// <summary>
        /// 组装今日面板的一帧数据：时间/日期/农历/天气/状态 + 当天提醒与日程，并生成去重签名。
        /// 只读设置副本，不改任何状态；结果交给 TaskbarPanelWindow.SetModel。
        /// </summary>
        /// <returns>可直接传给面板的模型；日程已排序并截断到 6 条。</returns>
        private TaskbarPanelModel BuildPanelModel(DateTime now)
        {
            // 每次都新建模型：SetModel 自己按签名判等，避免共享可变对象。
            TaskbarPanelModel model = new TaskbarPanelModel();
            // 大号时间固定 HH:mm:ss，用不变文化，避免 12/24 小时的本地化差异。
            model.TimeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            // 日期走格式引擎以支持中文星期；解析失败时下面有兜底。
            model.DateText = FormatEngine.Format(now, _weather, "yyyy年mm月dd日 dddd");
            // 格式串写错或引擎返回空串时退到最短日期写法，面板不能没有日期。
            if (string.IsNullOrEmpty(model.DateText))
            {
                model.DateText = now.ToString("yyyy年M月d日", CultureInfo.InvariantCulture);
            }
            // 农历用内置令牌（NN = 农历月日，MD = 干支等），与任务栏格式解耦。
            model.LunarText = FormatEngine.Format(now, _weather, "NN MD");
            // 天气可能尚未取到（null），面板按空串折叠这一行。
            model.WeatherText = _weather != null ? _weather.DisplayText : string.Empty;
            // 当前状态决定哪些提醒会出现在列表里，必须让用户看见。
            model.ModeText = "当前状态：" + ReminderModes.DisplayName(_settings.CurrentMode);

            // signature 收集与日程相关的全部字段，用于判断面板是否需要重建控件树。
            List<TaskbarAgendaLine> agenda = new List<TaskbarAgendaLine>();
            System.Text.StringBuilder signature = new System.Text.StringBuilder();

            // 提醒：只收启用的、且适用于当前状态的；过期条目仍显示，只是置灰。
            if (_settings.Reminders != null)
            {
                for (int i = 0; i < _settings.Reminders.Count; i++)
                {
                    Reminder reminder = _settings.Reminders[i];
                    // 配置里可能残留 null 项（手工编辑过 json），跳过而不是抛异常。
                    if (reminder == null || !reminder.Enabled)
                    {
                        continue;
                    }
                    // 状态过滤：例如「工作」状态下不显示只在「休息」时有效的提醒。
                    if (!ReminderModes.AppliesToState(reminder.Mode, _settings.CurrentMode))
                    {
                        continue;
                    }
                    // IsPast 只比时分（当天列表），是否属于今天由上面两个来源各自保证。
                    agenda.Add(new TaskbarAgendaLine(reminder.Time, reminder.Message, IsPast(reminder.Time, now)));
                    // 签名只追加显示相关字段，顺序稳定，便于字符串判等。
                    signature.Append("r").Append(reminder.Time).Append(reminder.Message).Append(';');
                }
            }

            // 日程：只收启用且“今天会发生”的；年度日程按月日匹配。
            if (_settings.Schedules != null)
            {
                for (int i = 0; i < _settings.Schedules.Count; i++)
                {
                    ScheduleItem schedule = _settings.Schedules[i];
                    // 年度日程的 Date 字段可能只是占位年份，必须用 OccursToday 判断。
                    if (schedule == null || !schedule.Enabled || !OccursToday(schedule, now))
                    {
                        continue;
                    }
                    // 正文带类别前缀（生日/会议…），让用户不点开也能分辨来源。
                    string text = ScheduleCategories.DisplayName(schedule.Type) + " · " + schedule.Title;
                    // 日程与提醒共用同一行模型，过期同样置灰。
                    agenda.Add(new TaskbarAgendaLine(schedule.Time, text, IsPast(schedule.Time, now)));
                    signature.Append("s").Append(schedule.Time).Append(text).Append(';');
                }
            }

            // 按 HH:mm 字符串排序：零填充时分，序数比较即时间序。
            agenda.Sort(CompareAgenda);
            // 只显示前 6 条：面板高度要压在任务栏之上，过多会把卡片顶出屏幕。
            if (agenda.Count > 6)
            {
                agenda.RemoveRange(6, agenda.Count - 6);
            }

            // 把排好序、截断后的列表交给面板。
            model.Agenda = agenda;
            // 签名带上分钟级的 now：跨分钟后置灰状态会变，需要允许重建。
            model.Signature = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)
                + "|"
                + _settings.CurrentMode
                + "|"
                + signature.ToString();
            return model;
        }

        /// <summary>日程行排序：按 HH:mm 文本序数比较，等宽零填充即时间序。</summary>
        private static int CompareAgenda(TaskbarAgendaLine left, TaskbarAgendaLine right)
        {
            // 时间字符串可能为空（手工配置），用空串兜底，保持排序稳定。
            return string.CompareOrdinal(left.Time ?? string.Empty, right.Time ?? string.Empty);
        }

        /// <summary>判断 HH:mm 文本是否早于当前时刻（只比时分，不跨日期）。</summary>
        private static bool IsPast(string time, DateTime now)
        {
            // 解析失败一律当作“未过期”：宁可少置灰，也不要把坏数据标成已完成。
            DateTime parsed;
            if (!DateTime.TryParseExact(
                time,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed))
            {
                return false;
            }
            // 只比时分：当天列表里出现昨天的时间没有意义。
            return parsed.Hour < now.Hour || (parsed.Hour == now.Hour && parsed.Minute < now.Minute);
        }

        /// <summary>判断日程是否发生在 now 当天；年度日程只比月日。</summary>
        private static bool OccursToday(ScheduleItem schedule, DateTime now)
        {
            // 日期解析失败（旧格式/空串）时该日程不进入今日列表。
            DateTime date;
            if (!ScheduleService.TryParseDate(schedule.Date, out date))
            {
                return false;
            }
            // 年度日程（生日等）：只比月日，忽略年份。
            if (schedule.Annual)
            {
                return date.Month == now.Month && date.Day == now.Day;
            }
            // 一次性日程：必须与今天同一天。
            return date.Date == now.Date;
        }

        /// <summary>
        /// 同步几何与可见性：确认任务栏可见、未全屏、原生时钟矩形有效，然后把覆盖窗摆上去。
        /// 任一前提不成立都调用 SetVisible(false)，绝不在不合适的时机硬盖。
        /// 矩形变化（或 force、或尚无有效背景）时才重取字体、重采样背景、重排版。
        /// </summary>
        /// <param name="force">true = 忽略矩形未变的短路，强制重采样与重排版。</param>
        private void SyncGeometry(bool force)
        {
            // 未启动时不同步：Start 会显式调用一次，避免构造期访问未建立的句柄。
            if (!_started)
            {
                return;
            }

            // 句柄无效时直接返回，等 OnSourceInitialized 之后的调用再说。
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            // 任务栏找不到或不可见（自动隐藏、explorer 重启中）：覆盖窗跟着藏起来。
            IntPtr shell = NativeMethods.FindWindow(ShellTrayClassName, null);
            if (shell == IntPtr.Zero || !NativeMethods.IsWindowVisible(shell))
            {
                SetVisible(false);
                return;
            }

            // 任务栏矩形退化（宽/高接近 0）说明正处在重建过程，先不显示。
            NativeMethods.RECT shellRect = new NativeMethods.RECT();
            if (!NativeMethods.GetWindowRect(shell, out shellRect)
                || shellRect.Bottom - shellRect.Top < 8
                || shellRect.Right - shellRect.Left < 8)
            {
                SetVisible(false);
                return;
            }

            // 任务栏被全屏窗口盖住或自动隐藏时，覆盖窗不应该浮在游戏/桌面上。
            // 用主屏高度判断任务栏是否被移出可视区（自动隐藏时会被推到屏幕外或高度为 0）。
            int screenHeight = NativeMethods.GetSystemMetrics(1);
            // 贴边判定：任务栏被移到顶部/底部之外的位置时不覆盖。
            if (shellRect.Bottom <= 2 || shellRect.Top >= screenHeight - 2)
            {
                SetVisible(false);
                return;
            }

            // 全屏应用（游戏、浏览器 F11、演示模式）盖住任务栏时，覆盖窗不应该浮在上面。
            if (NativeMethods.IsFullscreenForeground())
            {
                SetVisible(false);
                return;
            }

            // 找原生时钟：三段式查找见 LocateNativeClock；微软改过任务栏层级，不能只认一种路径。
            IntPtr clock = LocateNativeClock(shell);
            if (clock == IntPtr.Zero)
            {
                SetVisible(false);
                return;
            }

            // 原生时钟本身可能被系统隐藏（关掉时间显示），拿不到矩形就不覆盖。
            NativeMethods.RECT rect = new NativeMethods.RECT();
            if (!NativeMethods.GetWindowRect(clock, out rect))
            {
                SetVisible(false);
                return;
            }

            // 宽高过小说明抓到的是过渡态或隐藏窗口，覆盖上去会变成一条黑边。
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 24 || height < 12)
            {
                SetVisible(false);
                return;
            }

            // 命中测试确认任务栏真的露在最上面：全屏无边框窗口会盖住它但矩形仍然有效。
            if (!IsTaskbarVisibleAt(shell, rect))
            {
                SetVisible(false);
                return;
            }

            // 原生时钟只有 93 px，长格式放不下；向右扩到「操作中心」按钮换取宽度，字形就不用被压扁。
            rect = ExtendOverActionCenter(shell, rect);

            // 只在矩形变化（或强制、或还没采到有效背景）时才做昂贵操作：取字体、采样像素、重排版。
            bool changed = !_hasClockRect
                || rect.Left != _clockRect.Left
                || rect.Top != _clockRect.Top
                || rect.Right != _clockRect.Right
                || rect.Bottom != _clockRect.Bottom;

            // 命中后才写状态：上面的失败分支都保持旧值，避免半更新。
            _clockWindow = clock;
            _clockRect = rect;
            _hasClockRect = true;

            // 换位置/换主题/首帧：必须重取 DPI 与字体，并重新采样背景。
            if (changed || force || !_hasValidBackground)
            {
                _dpiScale = GetDpiScale();
                RefreshNativeFont(clock);
                CaptureBackground();
                if (!_hasValidBackground)
                {
                    // 采样到黑屏/过渡帧时先不显示，避免把任务栏时钟盖成一块黑色。
                    SetVisible(false);
                    return;
                }
                // 先排版再定位：字号与行高依赖当前矩形与字体。
                ApplyVisuals();
                PositionWindow();
                // 面板若开着（用户正看着），跟随新矩形重新贴边。
                if (_panel != null && _panel.IsVisible)
                {
                    _panel.ShowNear(_clockRect, _dpiScale);
                }
            }
            // 矩形没变：只需确认还在任务栏之上，不做采样与重排。
            else
            {
                // 同样只在被压下去时才抬顶，否则会盖住随后弹出的提示框与右键菜单。
                if (!NativeMethods.IsAbove(hwnd, shell))
                {
                    NativeMethods.RaiseTopmost(hwnd);
                }
            }

            // 所有检查通过，确保窗口可见（从全屏隐藏/任务栏重建中恢复）。
            SetVisible(true);
        }

        /// <summary>
        /// 把覆盖矩形向右扩到任务栏的「操作中心」按钮。
        /// 原生时钟固定 93 px，长格式（如「2026-11-09 11:18:13」）需要 135.5 px，
        /// 不扩宽就只能缩字号或压扁字形，两种都不好看；按钮与时钟紧邻（间隙 0），
        /// 扩过去正好取得富余宽度。代价是通知按钮与其未读徽标被盖住，
        /// 由右键菜单的「操作中心」入口补偿（见 <see cref="NativeMethods.OpenActionCenter"/>）。
        /// 找不到按钮（旧系统、系统隐藏）时原样返回，退回等比缩放。
        /// </summary>
        private NativeMethods.RECT ExtendOverActionCenter(IntPtr shell, NativeMethods.RECT rect)
        {
            IntPtr actionCenter = NativeMethods.FindChildToRight(shell, ActionCenterClassName, rect, ActionCenterMaxGap);
            // 没有可扩的按钮就不动矩形：扩宽会盖住别的东西，宁可按原宽度排版。
            if (actionCenter == IntPtr.Zero)
            {
                return rect;
            }

            NativeMethods.RECT buttonRect;
            if (!NativeMethods.GetWindowRect(actionCenter, out buttonRect))
            {
                return rect;
            }

            // 只改右边界：左边界必须与原生时钟对齐，否则会露出系统自己的时间。
            NativeMethods.RECT extended = rect;
            extended.Right = buttonRect.Right;
            return extended;
        }

        /// <summary>
        /// 读取原生时钟字体：成功则记住字体名与字号（像素高 ÷ DPI 换成 DIP），失败退回设置旧字段。
        /// WM_GETFONT 取不到时（句柄无效/时钟未创建完）不抛异常，只走兜底。
        /// </summary>
        private void RefreshNativeFont(IntPtr clock)
        {
            // faceName/pixelHeight 都是 Win32 口径：字号是像素高，需要除以 DPI 换成 DIP。
            string faceName;
            int pixelHeight;
            // 成功路径：原生字体像素高 ÷ 当前 DPI = WPF 的 FontSize（DIP）。
            if (NativeMethods.TryGetWindowFont(clock, out faceName, out pixelHeight)
                && !string.IsNullOrEmpty(faceName)
                && pixelHeight > 0)
            {
                _fontFamily = new FontFamily(faceName);
                _baseFontSize = pixelHeight / _dpiScale;
                return;
            }

            // 取字体失败：退回设置里的旧字段，再不行用雅黑 12。
            _fontFamily = new FontFamily(string.IsNullOrWhiteSpace(_settings.TaskbarFontFamilyName)
                ? "Microsoft YaHei UI"
                : _settings.TaskbarFontFamilyName);
            _baseFontSize = _settings.TaskbarFontSize > 0 ? _settings.TaskbarFontSize : 12.0;
        }

        /// <summary>
        /// 采样原生时钟上下边缘的背景色：覆盖之前先把自己移出屏幕，采样后再由 PositionWindow 放回。
        /// 任务栏可能是纯色、强调色着色或半透明亚克力，取实际像素比写死颜色更稳；
        /// 采样到纯黑/剧烈跳变（任务栏动画、全屏窗口、过渡帧）时判定无效，保留上一次有效颜色。
        /// </summary>
        private bool CaptureBackground()
        {
            int width = _clockRect.Right - _clockRect.Left;
            int height = _clockRect.Bottom - _clockRect.Top;
            // 过小的矩形没有采样价值（会采到边框或相邻图标），直接判失败。
            if (width < 8 || height < 8)
            {
                return false;
            }

            // 采样前把自己挪出屏幕：否则截图里会拍到覆盖窗自己。
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // 只有当前可见时才需要挪开并等待合成；未显示时窗口本来就不在画面上。
            bool wasVisible = IsVisible;
            if (wasVisible)
            {
                // 不改尺寸、不激活、不调整 Z 序，纯粹挪到屏幕外。
                NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_TOPMOST,
                    -20000,
                    -20000,
                    0,
                    0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                // 等一帧合成完成，否则可能截到挪动前的画面（覆盖窗自己的颜色）。
                System.Threading.Thread.Sleep(80);
            }

            // 截图与像素计算都可能因驱动/远程桌面失败，失败时保留上一次有效颜色。
            try
            {
                // 直接在屏幕坐标上取这块矩形，尺寸与 RECT 一致（物理像素）。
                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(width, height))
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        _clockRect.Left,
                        _clockRect.Top,
                        0,
                        0,
                        new System.Drawing.Size(width, height),
                        System.Drawing.CopyPixelOperation.SourceCopy);

                    // 取上下各约 10% 处的一行求均值：这两行通常没有文字，最接近任务栏底色。
                    int topRow = Math.Max(2, Math.Min(height - 4, height / 10));
                    int bottomRow = Math.Max(2, Math.Min(height - 4, height - height / 10 - 1));
                    System.Drawing.Color top = AverageRow(bitmap, topRow);
                    System.Drawing.Color bottom = AverageRow(bitmap, bottomRow);
                    Color topColor = Color.FromRgb(top.R, top.G, top.B);
                    Color bottomColor = Color.FromRgb(bottom.R, bottom.G, bottom.B);

                    // 颜色不可信时放弃本次结果，保留旧背景与旧文字色。
                    if (!IsPlausibleTaskbarColor(topColor, bottomColor))
                    {
                        return false;
                    }

                    // 亮度决定文字颜色：浅底用近黑，深底用白色，避免跟着主题变瞎。
                    _textColor = Luminance(topColor) + Luminance(bottomColor) >= 300.0
                        ? Color.FromRgb(0x1A, 0x1A, 0x1A)
                        : Colors.White;

                    // 用上下两色的垂直渐变复刻任务栏的细微明暗，纯色会显得比周围亮/暗。
                    LinearGradientBrush brush = new LinearGradientBrush(topColor, bottomColor, 90.0);
                    brush.Freeze();
                    // Freeze：背景刷子被渲染线程复用时不再产生额外拷贝。
                    Background = brush;
                    _hasValidBackground = true;
                    return true;
                }
            }
            catch (Exception)
            {
                // 采样失败时保留上一次颜色，不影响时钟显示。
                return false;
            }
        }

        /// <summary>判断采样到的上下边缘颜色是否像真实任务栏（亮度、主题、色差三重校验）。</summary>
        private static bool IsPlausibleTaskbarColor(Color top, Color bottom)
        {
            // 亮度均值作为主判据，下面再结合系统主题与上下色差。
            double average = (Luminance(top) + Luminance(bottom)) / 2.0;

            // 纯黑多半是覆盖窗移开后的过渡帧或全屏游戏背景，不能拿来当任务栏底色。
            if (average < 8.0)
            {
                return false;
            }

            // 采样结果要与系统浅色/深色任务栏一致：浅色主题下黑块、深色主题下白块都判为无效。
            if (NativeMethods.IsSystemUsesLightTheme())
            {
                if (average < 110.0)
                {
                    return false;
                }
            }
            else if (average > 150.0)
            {
                return false;
            }

            // 正常任务栏上下边缘颜色接近；跳变超过阈值通常是采到了文字/图标/边框。
            // 色差阈值 90 是经验值：任务栏渐变远小于它，文字/图标/边框远大于它。
            return Math.Abs(top.R - bottom.R) <= 90
                && Math.Abs(top.G - bottom.G) <= 90
                && Math.Abs(top.B - bottom.B) <= 90;
        }

        /// <summary>求位图某一行的平均颜色；行号越界时自动夹取。</summary>
        private static System.Drawing.Color AverageRow(System.Drawing.Bitmap bitmap, int y)
        {
            // 行号越界时夹到有效范围，调用方不必重复判边界。
            if (y < 0)
            {
                y = 0;
            }
            if (y >= bitmap.Height)
            {
                y = bitmap.Height - 1;
            }

            // 用 long 累加：宽度乘以 255 在 int 内够用，但留足余量避免溢出。
            long r = 0;
            long g = 0;
            long b = 0;
            int count = 0;
            // 左右各跳 2px：避开通栏边缘的 1px 高光/暗线。
            for (int x = 2; x < bitmap.Width - 2; x++)
            {
                System.Drawing.Color color = bitmap.GetPixel(x, y);
                r += color.R;
                g += color.G;
                b += color.B;
                count++;
            }

            // 宽度不足 5px 时循环一次都不跑，返回空色由调用方判为无效。
            if (count == 0)
            {
                return System.Drawing.Color.Empty;
            }
            return System.Drawing.Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
        }

        /// <summary>按 ITU-R BT.601 权重算感知亮度（0–255）。</summary>
        private static double Luminance(Color color)
        {
            // 601 权重与 Windows 判断“浅色/深色”的观感接近，够用且便宜。
            return color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
        }

        /// <summary>把覆盖窗移动到原生时钟矩形上（物理像素），并重新抬到最前。</summary>
        private void PositionWindow()
        {
            // 没有句柄或还没定位到原生时钟时不动窗口，避免把它挪到 (0,0)。
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !_hasClockRect)
            {
                return;
            }

            // MoveWindowPhysical 按物理像素直接改窗口位置与大小，绕开 WPF 的 DIP 缩放。
            NativeMethods.MoveWindowPhysical(
                hwnd,
                _clockRect.Left,
                _clockRect.Top,
                _clockRect.Right - _clockRect.Left,
                _clockRect.Bottom - _clockRect.Top);
            // 每次移动后都抬一次：任务栏重绘会把自己提到前面。
            NativeMethods.RaiseTopmost(hwnd);
        }

        /// <summary>
        /// 显示/隐藏覆盖窗。显示时先定位再 Show（只靠 MoveWindow 不会让隐藏窗口出现）。
        /// 隐藏时先收面板；全屏隐藏与任务栏不可见都走这里。
        /// </summary>
        private void SetVisible(bool visible)
        {
            // 日历点击进行中：窗口是刻意隐藏的，这条路径的 Show() 会把点击吃掉，直接跳过。
            if (_calendarPending)
            {
                return;
            }

            // 显示路径：先定位再 Show，顺序反了会先闪在旧位置。
            if (visible)
            {
                PositionWindow();
                // 已可见就只做定位，不重复 Show，免得多余的激活/重绘。
                if (!IsVisible)
                {
                    // 恢复时显式 Show()：只靠 MoveWindow 不会让隐藏窗口重新出现。
                    Show();
                    // 刚 Show 出来的窗口在 Z 序底部，立即抬到最前。
                    NativeMethods.RaiseTopmost(new WindowInteropHelper(this).Handle);
                }
                return;
            }

            // 隐藏前先收面板：面板是独立窗口，主窗隐藏不会连带它。
            HidePanel();
            // 只在可见时 Hide，减少无谓消息。
            if (IsVisible)
            {
                Hide();
            }
        }

        /// <summary>取当前显示器 DPI 缩放；取不到时按 1.0（DIP 即像素）处理。</summary>
        private double GetDpiScale()
        {
            // 从当前可视树拿合成目标：窗口未显示时可能为 null。
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformToDevice.M11;
            }
            // 还没连上渲染源：按 1.0 处理，后续 SyncGeometry 会再取一次。
            return 1.0;
        }

        /// <summary>
        /// 命中测试判断任务栏是否真的露在最上层：取时钟左侧 2px 处一点，向上找根窗口与任务栏比较。
        /// 被全屏无边框窗口盖住时矩形仍然有效，但根祖先不是 Shell_TrayWnd。
        /// </summary>
        private static bool IsTaskbarVisibleAt(IntPtr shell, NativeMethods.RECT clockRect)
        {
            // 探测点取时钟左侧 2px、上侧 2px：落在任务栏空白处，不会命中时钟自己的文字。
            NativeMethods.POINT point;
            point.X = clockRect.Left - 2;
            point.Y = clockRect.Top + 2;
            // GA_ROOT=2：命中窗口的根祖先必须就是任务栏，才说明任务栏在最上层。
            IntPtr hit = NativeMethods.WindowFromPoint(point);
            if (hit == IntPtr.Zero)
            {
                return false;
            }

            // 命中任务栏的子窗口（托盘/IME/通知区）才算任务栏真的可见；
            // 全屏游戏的无边框窗口盖住它时，根窗口不是 Shell_TrayWnd。
            return NativeMethods.GetAncestor(hit, 2) == shell;
        }

        /// <summary>
        /// 定位原生时钟窗口：优先 TrayNotifyWnd 下的 TrayClockWClass，其次直接挂在任务栏根下，
        /// 最后递归按类名搜。这样能扛住 Win10/11 之间任务栏层级的差异。
        /// </summary>
        /// <returns>原生时钟句柄；找不到返回 <see cref="IntPtr.Zero"/>，由调用方判定不可覆盖。</returns>
        private static IntPtr LocateNativeClock(IntPtr shell)
        {
            // 首选：TrayNotifyWnd 下的 TrayClockWClass，Win10/11 的常规层级。
            IntPtr tray = NativeMethods.FindWindowEx(shell, IntPtr.Zero, TrayNotifyClassName, null);
            IntPtr clock = IntPtr.Zero;
            if (tray != IntPtr.Zero)
            {
                clock = NativeMethods.FindWindowEx(tray, IntPtr.Zero, TrayClockClassName, null);
            }
            // 部分版本把时钟直接挂在任务栏根窗口下。
            if (clock == IntPtr.Zero)
            {
                clock = NativeMethods.FindWindowEx(shell, IntPtr.Zero, TrayClockClassName, null);
            }
            // 兜底：递归按类名找，应对微软调整层级或第三方任务栏。
            if (clock == IntPtr.Zero)
            {
                clock = NativeMethods.FindDescendantByClass(shell, TrayClockClassName);
            }
            // 三段都失败返回 IntPtr.Zero，由调用方判定不可覆盖。
            return clock;
        }
    }
}
