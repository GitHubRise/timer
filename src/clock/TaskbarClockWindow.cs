using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GameClock
{
    // TaskbarClockWindow：用无边框透明置顶窗覆盖系统任务栏时钟区域，绘制自己的时间文本。
    // 调用方：AppController.ApplyDisplaySurfaces 创建/Start/Stop，设置提交后调 ApplySettings。
    // 边界：只“覆盖”不“注入”explorer，绝不隐藏 TrayClockWClass（隐藏会让任务栏重排、
    //       托盘图标移位）；改为把系统时钟移出可视区但保留占位，退出时换算父窗口坐标移回。
    // 已知坑：TrayClockWClass 是任务栏子窗口，SetWindowPos 用相对父窗口的客户区坐标；
    //       任务栏重排可能把时钟拉回原位，所以每 2 s 要探测一次并重新移走。
    /// <summary>
    /// 任务栏时钟覆盖窗：定位系统时钟矩形、移走原生时钟占位、按可用宽高自适应字号，
    /// 窗口消失/退出时把原生时钟恢复到原槽位。
    /// </summary>
    internal sealed class TaskbarClockWindow : Window
    {
        // 任务栏窗口类名：Shell_TrayWnd 是任务栏本体，TrayNotifyWnd 是通知区，
        // TrayClockWClass 是系统时钟；三者都是 explorer 的内部窗口，只按类名查找。
        private const string ShellTrayClassName = "Shell_TrayWnd";
        private const string TrayNotifyClassName = "TrayNotifyWnd";
        private const string TrayClockClassName = "TrayClockWClass";
        // 找不到真实时钟矩形时的兜底尺寸（物理像素）：通知区右侧 74×40 是 Win10/11 常见槽位。
        private const int DefaultClockWidth = 74;
        private const int DefaultClockHeight = 40;
        // 任务栏几何轮询间隔：2 s 足以跟上任务栏重排/DPI 变化，又不会造成明显开销。
        private const int SyncIntervalMs = 2000;

        // 设置副本：构造时 Clone 一份，只取 Taskbar* 与 TimeZone* 字段，避免与悬浮窗互相覆盖。
        private readonly AppSettings _settings;
        // 文字渲染控件（分层文字），不参与命中测试，鼠标事件全部透给下层。
        private readonly LayeredText _clockText;
        // 两个计时器：200 ms 刷文本（高频、禁止日志），2 s 同步任务栏几何。
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _syncTimer;
        // 天气数据由控制器推送；格式化时作为 token 的取值来源。
        private WeatherInfo _weather;
        private TimeZoneInfo _timeZone;
        // 原生时钟句柄与“是否已移走”标志；句柄变化（explorer 重启）时标志要复位。
        private IntPtr _nativeClock;
        private bool _nativeClockMoved;
        // 是否已 Start；Start 幂等，重复调用不会重复 Show/启动计时器。
        private bool _started;
        // 系统时钟矩形（屏幕坐标）及来源句柄；_hasClockRect 为 false 时不能覆盖。
        private NativeMethods.RECT _clockRect;
        private bool _hasClockRect;
        private IntPtr _rectClock;
        // 上一次覆盖窗的物理矩形，用于跳过无变化的 SetWindowPos，减少外部副作用。
        private NativeMethods.RECT _lastOverlayRect;
        private bool _hasLastOverlay;

        /// <summary>
        /// 构造窗口但不显示：只搭 UI、计时器与初始状态，实际定位在 Start→SyncGeometry 完成。
        /// 初始 1×1、-10000、Opacity=0，避免首帧在错误位置闪一下。
        /// </summary>
        public TaskbarClockWindow(AppSettings settings)
        {
            // 必须 Clone：任务栏窗只消费 Taskbar* 字段，不能把悬浮窗的设置对象改掉。
            _settings = settings != null ? settings.Clone() : AppSettings.CreateDefault();
            _settings.Normalize();
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            // 无边框透明、不占任务栏、不抢焦点；Topmost 保证盖在任务栏之上。
            Title = "GameClockTaskbarClock";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            Width = 1;
            Height = 1;
            Left = -10000;
            Top = -10000;
            Opacity = 0.0;

            // 文字控件不命中鼠标：任务栏区域的点击应落到任务栏本身（开始菜单/托盘）。
            _clockText = new LayeredText();
            _clockText.IsHitTestVisible = false;

            // 根 Border 也全透明且不命中，保证覆盖窗只是“画上去”的一层。
            Border root = new Border();
            root.Background = Brushes.Transparent;
            root.IsHitTestVisible = false;
            root.Child = _clockText;
            Content = root;

            // 200 ms 刷文本用 Normal 优先级，保证秒针跳动跟手；几何同步用 Background。
            _clockTimer = new DispatcherTimer(DispatcherPriority.Normal);
            _clockTimer.Interval = TimeSpan.FromMilliseconds(200);
            _clockTimer.Tick += OnClockTick;

            _syncTimer = new DispatcherTimer(DispatcherPriority.Background);
            _syncTimer.Interval = TimeSpan.FromMilliseconds(SyncIntervalMs);
            _syncTimer.Tick += OnSyncTick;
        }

        /// <summary>
        /// 启动任务栏显示：Show 后立刻刷文本、定位覆盖区，再启动两个计时器。
        /// 幂等：重复调用直接返回，避免重复 Show/重复计时。
        /// </summary>
        public void Start()
        {
            // 已启动：保持现有窗口与计时器，不重复初始化。
            if (_started)
            {
                return;
            }

            _started = true;
            Show();
            UpdateText(true);
            SyncGeometry();
            _clockTimer.Start();
            _syncTimer.Start();
        }

        /// <summary>
        /// 停止任务栏显示：停计时器、把系统时钟移回原槽位并隐藏覆盖窗。
        /// 恢复必须发生在隐藏之前，否则任务栏会出现短暂空槽。
        /// </summary>
        public void Stop()
        {
            _started = false;
            _clockTimer.Stop();
            _syncTimer.Stop();

            // 先恢复再 Hide：RestoreNativeClock 内部会把 _nativeClockMoved 复位。
            RestoreNativeClock();
            // 窗口可能已被全屏/任务栏消失逻辑隐藏，只在可见时 Hide，避免多余调用。
            if (IsVisible)
            {
                Hide();
            }
        }

        /// <summary>
        /// 应用新设置：只拷贝任务栏专用字段（字体/字号/颜色/格式/时区），
        /// 不触碰悬浮窗字段；已启动时立即重绘并按新字号重新定位。
        /// </summary>
        public void ApplySettings(AppSettings settings)
        {
            // 控制器可能传 null（设置尚未加载）：保持当前显示，不抛异常。
            if (settings == null)
            {
                return;
            }

            // 先 Clone 再取值：调用方的设置对象可能还会被继续编辑，直接引用会读到中间态。
            AppSettings copy = settings.Clone();
            copy.Normalize();
            _settings.TaskbarColorHex = copy.TaskbarColorHex;
            _settings.TaskbarFontFamilyName = copy.TaskbarFontFamilyName;
            _settings.TaskbarFontSize = copy.TaskbarFontSize;
            _settings.TaskbarFormat = copy.TaskbarFormat;
            _settings.TaskbarFormatMode = copy.TaskbarFormatMode;
            _settings.TaskbarDotNetFormat = copy.TaskbarDotNetFormat;
            _settings.TimeZoneId = copy.TimeZoneId;
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            // 未启动时只更新字段，Start 时统一生效，避免对隐藏窗口做无谓的 SetWindowPos。
            if (_started)
            {
                UpdateText(true);
                SyncGeometry();
            }
        }

        /// <summary>更新天气数据并强制重算文本；null 表示取数失败，token 会渲染为空。</summary>
        public void SetWeather(WeatherInfo weather)
        {
            _weather = weather;
            UpdateText(true);
        }

        /// <summary>
        /// 句柄创建完成：应用任务栏专用扩展样式（TOOLWINDOW/LAYERED/TRANSPARENT/NOACTIVATE），
        /// 保证不抢焦点、不占 Alt+Tab、鼠标点击穿透到任务栏。
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ApplyTaskbarStyles(hwnd);
        }

        /// <summary>窗口销毁：停计时器并把系统时钟还回去；日志已在 Stop 记录，这里不重复。</summary>
        protected override void OnClosed(EventArgs e)
        {
            _clockTimer.Stop();
            _syncTimer.Stop();
            RestoreNativeClock();
            base.OnClosed(e);
        }

        /// <summary>200 ms 文本心跳：只在文本变化时重绘；高频路径，禁止写日志。</summary>
        private void OnClockTick(object sender, EventArgs e)
        {
            UpdateText(false);
        }

        /// <summary>2 s 几何同步：任务栏重排、DPI 变化、explorer 重启后重新对齐覆盖区。</summary>
        private void OnSyncTick(object sender, EventArgs e)
        {
            SyncGeometry();
        }

        /// <summary>
        /// 计算当前时间文本并写入；文本未变化时直接返回，避免 200 ms 一次的无谓重绘。
        /// 文本变化会改变宽度需求，因此需要重新 ApplyLayout 自适应字号。
        /// </summary>
        private void UpdateText(bool force)
        {
            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            string text = FormatEngine.IsTokenMode(_settings.TaskbarFormatMode)
                ? FormatEngine.Format(now, _weather, _settings.TaskbarFormat)
                : TimeFormatter.Format(now, _settings.TaskbarDotNetFormat);

            // 非强制且文本相同：秒数没变，跳过重绘（任务栏同样受“每次重绘不记日志”约束）。
            if (!force && string.Equals(text, _clockText.Text, StringComparison.Ordinal))
            {
                return;
            }

            _clockText.Text = text;
            // 还没拿到任务栏时钟矩形时只更新文本，等 SyncGeometry 成功后再定位。
            if (_hasClockRect)
            {
                ApplyLayout();
            }
        }

        /// <summary>
        /// 把覆盖窗移动到系统时钟矩形上并重排文字。
        /// 只有矩形真正变化才 SetWindowPos：该调用是外部副作用，2 s 同步不应反复触发。
        /// </summary>
        private void ApplyLayout()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // 句柄未就绪或还没定位到系统时钟：没有可覆盖的目标。
            if (hwnd == IntPtr.Zero || !_hasClockRect)
            {
                return;
            }

            NativeMethods.RECT overlay = BuildOverlayRect(_clockRect);
            int width = overlay.Right - overlay.Left;
            int height = overlay.Bottom - overlay.Top;
            // 四个边任一变化才移动；相等时跳过，避免每 2 s 一次无意义的 SetWindowPos。
            if (!_hasLastOverlay
                || overlay.Left != _lastOverlayRect.Left
                || overlay.Top != _lastOverlayRect.Top
                || overlay.Right != _lastOverlayRect.Right
                || overlay.Bottom != _lastOverlayRect.Bottom)
            {
                NativeMethods.MoveWindowPhysical(hwnd, overlay.Left, overlay.Top, width, height);
                _lastOverlayRect = overlay;
                _hasLastOverlay = true;
            }
            ApplyVisuals(width / GetDpiScale(), height / GetDpiScale());
        }

        /// <summary>
        /// 计算覆盖窗矩形。通知按钮紧贴时钟槽右侧，覆盖窗必须完全落在原生时钟矩形内，
        /// 超出的部分会被系统绘制覆盖；宁可缩小字号也不外扩。
        /// </summary>
        private NativeMethods.RECT BuildOverlayRect(NativeMethods.RECT clockRect)
        {
            NativeMethods.RECT result = new NativeMethods.RECT();
            result.Left = clockRect.Left;
            result.Top = clockRect.Top;
            result.Right = clockRect.Right;
            result.Bottom = clockRect.Bottom;
            return result;
        }

        /// <summary>
        /// 按可用 DIP 宽高自适应字号并应用任务栏样式：无阴影、无 3D，
        /// 保持与系统时钟一致的扁平观感，也避免放大后超出槽位。
        /// </summary>
        private void ApplyVisuals(double widthDip, double heightDip)
        {
            double fontSize = FitFontSize(_clockText.Text, widthDip, heightDip);
            _clockText.ApplyStyle(
                GetTaskbarFontFamily(),
                fontSize,
                FontWeights.Normal,
                _settings.TaskbarColor,
                false,
                false);
        }

        /// <summary>取任务栏字体；名称为空时回落到系统 UI 常用的“Microsoft YaHei UI”。</summary>
        private FontFamily GetTaskbarFontFamily()
        {
            return new FontFamily(string.IsNullOrWhiteSpace(_settings.TaskbarFontFamilyName)
                ? "Microsoft YaHei UI"
                : _settings.TaskbarFontFamilyName);
        }

        /// <summary>
        /// 在给定宽高内选择最大可用字号：先把设置字号夹到 7–40，且不超过槽高的 1/2.6
        /// （两行文本的粗略行高约束），再按 0.5 步长向下试探直到不溢出。
        /// </summary>
        private double FitFontSize(string text, double widthDip, double heightDip)
        {
            // 1/2.6 是两行文本（日期+时间）能塞进槽高的经验比例；7/40 为绝对上下限。
            double size = Math.Min(
                Math.Max(7.0, Math.Min(40.0, _settings.TaskbarFontSize)),
                Math.Max(6.0, heightDip / 2.6));
            // 空文本没有测量意义，直接用当前字号，等待下一次 SetWeather/UpdateText。
            if (string.IsNullOrEmpty(text))
            {
                return size;
            }

            double scale = GetDpiScale();
            // 左右各留 2 DIP、上下各留 1 DIP：FormattedText 与实际渲染的度量差，
            // 加上槽位边缘会被任务栏裁切，不放安全边距会缺笔画。
            double limitWidth = Math.Max(6.0, widthDip - 4.0);
            double limitHeight = Math.Max(4.0, heightDip - 2.0);
            Typeface typeface = new Typeface(GetTaskbarFontFamily(), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            // 最多 40 次、每次减 0.5：从设置字号降到 5 的极端情况下也能收敛，避免死循环。
            for (int i = 0; i < 40 && size > 5.0; i++)
            {
                FormattedText formatted = new FormattedText(
                    text,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    size,
                    Brushes.Black,
                    scale);
                // 宽度包含尾随空白，防止以空格结尾的格式串把最后一行撑出槽位。
                if (formatted.WidthIncludingTrailingWhitespace <= limitWidth
                    && formatted.Height <= limitHeight)
                {
                    break;
                }
                size -= 0.5;
            }

            return size;
        }

        /// <summary>
        /// 同步任务栏几何：找任务栏 → 找系统时钟 → 校验矩形是否可信 → 移走原生时钟 → 覆盖。
        /// 任务栏不存在（被隐藏/explorer 重启）时把系统时钟还回去并隐藏覆盖窗。
        /// 这是 2 s 周期路径，只有状态跃迁才记日志。
        /// </summary>
        private void SyncGeometry()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // 窗口句柄还没创建：等 OnSourceInitialized 之后的下一次同步。
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            IntPtr shell = NativeMethods.FindWindow(ShellTrayClassName, null);
            // 任务栏不存在或被自动隐藏：无法覆盖，先把系统时钟还回去再隐藏自己。
            if (shell == IntPtr.Zero || !NativeMethods.IsWindowVisible(shell))
            {
                RestoreNativeClock();
                SetVisible(false);
                return;
            }

            IntPtr clock = LocateNativeClock(shell);
            // 任务栏在但时钟不在：explorer 可能正在重启；此前移走过的话需要把句柄状态复位。
            if (clock == IntPtr.Zero)
            {
                RestoreNativeClock();
                SetVisible(false);
                return;
            }

            // 探测原生时钟矩形：GetWindowRect 失败时 width/height 保持 0，后续会走兜底/上次矩形。
            NativeMethods.RECT rect = new NativeMethods.RECT();
            int width = 0;
            int height = 0;
            // 取物理像素矩形；失败不抛异常，宽高留在 0 由后续分支兜底。
            if (NativeMethods.GetWindowRect(clock, out rect))
            {
                width = rect.Right - rect.Left;
                height = rect.Bottom - rect.Top;
            }

            NativeMethods.RECT shellRect = new NativeMethods.RECT();
            bool hasShell = NativeMethods.GetWindowRect(shell, out shellRect);
            // 允许上下各溢出 2/8 px：任务栏缩放动画与高 DPI 取整会让时钟略超任务栏边界。
            bool insideShell = hasShell
                && rect.Top >= shellRect.Top - 2
                && rect.Bottom <= shellRect.Bottom + 8;

            // 矩形可信：宽高达到最小 40×20 且确实落在任务栏内，才记录为新基准。
            if (width >= 40 && height >= 20 && insideShell)
            {
                _clockRect = rect;
                _hasClockRect = true;
                _rectClock = clock;
            }
            // 同一时钟且之前有可信矩形：原生时钟被移走后布局会塌缩成 0×0，
            // 此时保留上一次的好矩形，避免覆盖窗跟着缩没。
            else if (_hasClockRect && _rectClock == clock)
            {
                rect = _clockRect;
                width = rect.Right - rect.Left;
                height = rect.Bottom - rect.Top;
            }
            // 首次定位或时钟句柄变化：用通知区右端推算兜底矩形。
            else
            {
                rect = GetFallbackRect(shell);
                width = rect.Right - rect.Left;
                height = rect.Bottom - rect.Top;
            }

            MoveNativeClockAway(clock);
            _clockRect = rect;
            _hasClockRect = true;
            _rectClock = clock;
            ApplyLayout();
            SetVisible(true);
        }

        /// <summary>
        /// 兜底矩形：优先用通知区 TrayNotifyWnd 的右端，其次任务栏本体，都取不到时用 74×40。
        /// 槽位右对齐——系统时钟永远贴在通知区右侧；高度不足 16 px 时强制用默认高度。
        /// </summary>
        private static NativeMethods.RECT GetFallbackRect(IntPtr shell)
        {
            NativeMethods.RECT baseRect = new NativeMethods.RECT();
            IntPtr tray = NativeMethods.FindWindowEx(shell, IntPtr.Zero, TrayNotifyClassName, null);
            bool found = tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out baseRect);
            // 通知区取不到时退一步用整个任务栏矩形，至少能拿到 top/bottom。
            if (!found)
            {
                found = NativeMethods.GetWindowRect(shell, out baseRect);
            }
            // 任务栏也取不到（极早期启动）：构造一个 74×40 的虚拟矩形。
            if (!found)
            {
                baseRect.Right = DefaultClockWidth;
                baseRect.Bottom = DefaultClockHeight;
            }

            NativeMethods.RECT result = new NativeMethods.RECT();
            result.Right = baseRect.Right;
            result.Left = baseRect.Right - DefaultClockWidth;
            result.Top = baseRect.Top;
            result.Bottom = baseRect.Bottom;
            // 任务栏高度异常（<16 px）时用默认高度兜底，保证文字还能显示两行。
            if (result.Bottom - result.Top < 16)
            {
                result.Bottom = result.Top + DefaultClockHeight;
            }
            return result;
        }

        /// <summary>
        /// 查找系统时钟窗口：优先在通知区 TrayNotifyWnd 下找 TrayClockWClass，
        /// 找不到再退到任务栏本体下找（不同 Windows 版本层级不同）。
        /// </summary>
        private static IntPtr LocateNativeClock(IntPtr shell)
        {
            IntPtr tray = NativeMethods.FindWindowEx(shell, IntPtr.Zero, TrayNotifyClassName, null);
            IntPtr clock = IntPtr.Zero;
            // 先在通知区下找：Win10/11 的常规层级。
            if (tray != IntPtr.Zero)
            {
                clock = NativeMethods.FindWindowEx(tray, IntPtr.Zero, TrayClockClassName, null);
            }
            // 通知区里没有：部分版本把时钟直接挂在任务栏下，再兜底找一次。
            if (clock == IntPtr.Zero)
            {
                clock = NativeMethods.FindWindowEx(shell, IntPtr.Zero, TrayClockClassName, null);
            }
            return clock;
        }

        /// <summary>
        /// 把系统时钟移到 (-10000,-10000)：保留任务栏槽位（不隐藏，避免托盘重排），
        /// 覆盖窗才能稳定盖在原位置；任务栏重排把它拉回来后要重新移走。
        /// </summary>
        private void MoveNativeClockAway(IntPtr clock)
        {
            // 没有时钟句柄时无从移动。
            if (clock == IntPtr.Zero)
            {
                return;
            }

            // 句柄变化（explorer 重启后重建）：状态归零，重新移一次。
            if (_nativeClock != clock)
            {
                _nativeClock = clock;
                _nativeClockMoved = false;
            }

            // 已移走：探测是否还在屏幕外（Left < -1000）；被任务栏重排拉回则继续往下重新移动。
            if (_nativeClockMoved)
            {
                NativeMethods.RECT probe;
                // 仍在屏幕外（Left<-1000）说明占位保持有效，直接返回避免重复 SetWindowPos。
                if (NativeMethods.GetWindowRect(clock, out probe) && probe.Left < -1000)
                {
                    return;
                }
                _nativeClockMoved = false;
            }

            // 未移走（或刚被拉回）：移到屏幕外，保留尺寸/层级，只改位置。
            if (!_nativeClockMoved)
            {
                // 失败通常意味着句柄已失效（explorer 退出）；这里不重试，等下一轮同步按新句柄重来。
                NativeMethods.SetWindowPos(
                    clock,
                    IntPtr.Zero,
                    -10000,
                    -10000,
                    0,
                    0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                _nativeClockMoved = true;
            }
        }

        /// <summary>
        /// 恢复系统时钟：子窗口坐标要由屏幕坐标换算成父窗口客户区坐标，
        /// 再 SetWindowPos 回原槽位并 ShowWindow，最后广播 WM_SETTINGCHANGE 让任务栏重排。
        /// </summary>
        private void RestoreNativeClock()
        {
            // 没移走过（或句柄已空）：无需恢复，避免对无关窗口做 SetWindowPos。
            if (!_nativeClockMoved || _nativeClock == IntPtr.Zero)
            {
                return;
            }

            // 有可信矩形才按原槽位恢复；没有时只能保持当前位置再 Show。
            if (_hasClockRect)
            {
                int left = 0;
                int top = 0;
                IntPtr parent = NativeMethods.GetParent(_nativeClock);
                NativeMethods.RECT parentRect = new NativeMethods.RECT();
                // 屏幕坐标 → 父客户区坐标；取不到父窗口时退回 (0,0)，至少把窗口显示出来。
                if (parent != IntPtr.Zero && NativeMethods.GetWindowRect(parent, out parentRect))
                {
                    left = _clockRect.Left - parentRect.Left;
                    top = _clockRect.Top - parentRect.Top;
                }

                NativeMethods.SetWindowPos(
                    _nativeClock,
                    IntPtr.Zero,
                    left,
                    top,
                    _clockRect.Right - _clockRect.Left,
                    _clockRect.Bottom - _clockRect.Top,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            }

            NativeMethods.ShowWindow(_nativeClock, NativeMethods.SW_SHOWNOACTIVATE);
            _nativeClockMoved = false;

            // 广播 WM_SETTINGCHANGE（2000 ms 超时）：通知 explorer 重新布局通知区，
            // 不广播时钟可能留在屏幕外，直到下次任务栏重排。
            IntPtr shell = NativeMethods.FindWindow(ShellTrayClassName, null);
            // 任务栏不在（正在重启）：跳过广播，恢复动作已尽力完成。
            if (shell != IntPtr.Zero)
            {
                IntPtr result;
                NativeMethods.SendMessageTimeout(
                    shell,
                    NativeMethods.WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0x0002,
                    2000,
                    out result);
            }
        }

        /// <summary>
        /// 显示/隐藏覆盖窗：显示时才把不透明度拉到 1，隐藏时先透明再 Hide，
        /// 避免任务栏区域出现一帧不透明的黑块。
        /// </summary>
        private void SetVisible(bool visible)
        {
            // 需要显示：窗口未 Show 过就 Show，然后拉到完全不透明。
            if (visible)
            {
                // 只在尚未显示时 Show：重复 Show 会让窗口抢焦点/重排 Z 序。
                if (!IsVisible)
                {
                    Show();
                }
                Opacity = 1.0;
            }
            // 需要隐藏：先透明再 Hide，防止布局/合成延迟造成闪黑。
            else
            {
                Opacity = 0.0;
                // 已隐藏时不再调用 Hide，避免无意义的窗口状态切换。
                if (IsVisible)
                {
                    Hide();
                }
            }
        }

        /// <summary>
        /// 取当前窗口的 DPI 缩放（物理像素 / DIP）；取不到 PresentationSource 时按 1.0 处理。
        /// 覆盖窗用物理像素定位，字号测量用 DIP，两者换算全靠这个比例。
        /// </summary>
        private double GetDpiScale()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            // 窗口尚未挂到呈现源（构造早期/已关闭）：按 96 DPI 处理。
            if (source != null && source.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformToDevice.M11;
            }
            return 1.0;
        }
    }
}
