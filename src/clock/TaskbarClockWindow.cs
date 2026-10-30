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
        private const string ShellTrayClassName = "Shell_TrayWnd";
        private const string TrayNotifyClassName = "TrayNotifyWnd";
        private const string TrayClockClassName = "TrayClockWClass";
        private const int TickIntervalMs = 250;
        private const int SyncIntervalMs = 1000;
        private const int HoverDelayMs = 420;
        private const int PanelCloseDelayMs = 360;
        private const int ResampleDelayMs = 500;

        private readonly AppSettings _settings;
        private readonly TextBlock _text;
        private readonly DispatcherTimer _tickTimer;
        private readonly DispatcherTimer _syncTimer;
        private readonly DispatcherTimer _hoverTimer;
        private readonly DispatcherTimer _panelCloseTimer;
        private readonly DispatcherTimer _resampleTimer;
        private readonly DispatcherTimer _pinClickTimer;

        private WeatherInfo _weather;
        private TimeZoneInfo _timeZone;
        private TaskbarPanelWindow _panel;
        private IntPtr _clockWindow;
        private NativeMethods.RECT _clockRect;
        private bool _hasClockRect;
        private bool _started;
        private double _dpiScale = 1.0;
        private FontFamily _fontFamily;
        private double _baseFontSize = 12.0;
        private Color _textColor = Color.FromRgb(0x1A, 0x1A, 0x1A);
        private bool _hasValidBackground;
        private HwndSource _source;
        private bool _panelPinned;

        public TaskbarClockWindow(AppSettings settings)
        {
            _settings = settings != null ? settings.Clone() : AppSettings.CreateDefault();
            _settings.Normalize();
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            Title = "GameClockTaskbarClock";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Focusable = true;
            Width = 1;
            Height = 1;
            Left = -10000;
            Top = -10000;

            _text = new TextBlock();
            _text.TextAlignment = TextAlignment.Center;
            _text.TextWrapping = TextWrapping.NoWrap;
            _text.Padding = new Thickness(2, 0, 2, 0);
            _text.Foreground = new SolidColorBrush(_textColor);
            _text.FontWeight = FontWeights.Normal;
            _text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            _text.VerticalAlignment = VerticalAlignment.Center;
            _text.HorizontalAlignment = HorizontalAlignment.Center;
            _text.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(_text, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(_text, TextRenderingMode.ClearType);
            _text.IsHitTestVisible = false;

            // Viewbox 只做横向收紧：行高固定，高度方向不缩放，宽度方向按实际宽度压回任务栏时钟矩形。
            Viewbox viewbox = new Viewbox();
            viewbox.Stretch = Stretch.Fill;
            viewbox.HorizontalAlignment = HorizontalAlignment.Stretch;
            viewbox.VerticalAlignment = VerticalAlignment.Stretch;
            viewbox.Child = _text;
            Content = viewbox;

            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;

            _tickTimer = new DispatcherTimer(DispatcherPriority.Normal);
            _tickTimer.Interval = TimeSpan.FromMilliseconds(TickIntervalMs);
            _tickTimer.Tick += OnTick;

            _syncTimer = new DispatcherTimer(DispatcherPriority.Background);
            _syncTimer.Interval = TimeSpan.FromMilliseconds(SyncIntervalMs);
            _syncTimer.Tick += OnSyncTick;

            _hoverTimer = new DispatcherTimer(DispatcherPriority.Background);
            _hoverTimer.Interval = TimeSpan.FromMilliseconds(HoverDelayMs);
            _hoverTimer.Tick += OnHoverTick;

            _panelCloseTimer = new DispatcherTimer(DispatcherPriority.Background);
            _panelCloseTimer.Interval = TimeSpan.FromMilliseconds(PanelCloseDelayMs);
            _panelCloseTimer.Tick += OnPanelCloseTick;

            _resampleTimer = new DispatcherTimer(DispatcherPriority.Background);
            _resampleTimer.Interval = TimeSpan.FromMilliseconds(ResampleDelayMs);
            _resampleTimer.Tick += OnResampleTick;

            _pinClickTimer = new DispatcherTimer(DispatcherPriority.Background);
            _pinClickTimer.Interval = TimeSpan.FromMilliseconds(60);
            _pinClickTimer.Tick += OnPinClickTick;
        }

        /// <summary>右键菜单请求；由控制器弹出与托盘共用的菜单。</summary>
        public event EventHandler MenuRequested;

        /// <summary>面板中点击“打开设置”时触发。</summary>
        public event EventHandler SettingsRequested;

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            _dpiScale = GetDpiScale();
            Show();
            SyncGeometry(true);
            UpdateText(true);
            _tickTimer.Start();
            _syncTimer.Start();
        }

        public void Stop()
        {
            _started = false;
            _tickTimer.Stop();
            _syncTimer.Stop();
            _hoverTimer.Stop();
            _panelCloseTimer.Stop();
            _resampleTimer.Stop();
            _pinClickTimer.Stop();
            HidePanel();
            if (IsVisible)
            {
                Hide();
            }
        }

        public void ApplySettings(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            AppSettings copy = settings.Clone();
            copy.Normalize();
            _settings.TaskbarFormat = copy.TaskbarFormat;
            _settings.TaskbarFormatMode = copy.TaskbarFormatMode;
            _settings.TaskbarDotNetFormat = copy.TaskbarDotNetFormat;
            _settings.TaskbarFontFamilyName = copy.TaskbarFontFamilyName;
            _settings.TaskbarFontSize = copy.TaskbarFontSize;
            _settings.TaskbarFontScale = copy.TaskbarFontScale;
            _settings.TaskbarBold = copy.TaskbarBold;
            _settings.TaskbarColorHex = copy.TaskbarColorHex;
            _settings.CurrentMode = copy.CurrentMode;
            _settings.TimeZoneId = copy.TimeZoneId;
            _settings.Reminders = copy.Reminders;
            _settings.Schedules = copy.Schedules;
            _settings.WeatherEnabled = copy.WeatherEnabled;
            _settings.WeatherCity = copy.WeatherCity;
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            if (_started)
            {
                UpdateText(true);
                SyncGeometry(false);
            }
        }

        public void SetWeather(WeatherInfo weather)
        {
            _weather = weather;
            UpdateText(true);
            RefreshPanel();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ApplyTaskbarOverlayStyles(hwnd);
            NativeMethods.RaiseTopmost(hwnd);
            _source = HwndSource.FromHwnd(hwnd);
            if (_source != null)
            {
                _source.AddHook(OnWindowMessage);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _tickTimer.Stop();
            _syncTimer.Stop();
            _hoverTimer.Stop();
            _panelCloseTimer.Stop();
            _resampleTimer.Stop();
            _pinClickTimer.Stop();
            if (_source != null)
            {
                _source.RemoveHook(OnWindowMessage);
                _source = null;
            }
            if (_panel != null)
            {
                _panel.Close();
                _panel = null;
            }
            base.OnClosed(e);
        }

        private void OnTick(object sender, EventArgs e)
        {
            UpdateText(false);
            // 任务栏会在面板展开、任务栏重绘时把自己提到最顶层窗口组前面，
            // 覆盖窗必须反复抬到最前，否则会露出系统的原生时钟。
            NativeMethods.RaiseTopmost(new WindowInteropHelper(this).Handle);
        }

        private void OnSyncTick(object sender, EventArgs e)
        {
            SyncGeometry(false);
        }

        private void OnResampleTick(object sender, EventArgs e)
        {
            _resampleTimer.Stop();
            if (_started && _hasClockRect && IsVisible)
            {
                SyncGeometry(true);
            }
        }

        private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmLButtonUp = 0x0202;
            const int WmRButtonUp = 0x0205;
            const int WmSettingChange = 0x001A;
            const int WmThemeChanged = 0x031A;
            const int WmDwmColorizationColorChanged = 0x0320;

            if (message == WmLButtonUp)
            {
                HandleClockLeftClick();
                return IntPtr.Zero;
            }
            if (message == WmRButtonUp)
            {
                HandleClockRightClick();
                return IntPtr.Zero;
            }

            bool resample = false;
            if (message == WmSettingChange)
            {
                // 主题切换（浅色/深色）、任务栏尺寸变化都会广播 WM_SETTINGCHANGE。
                string area = lParam != IntPtr.Zero
                    ? System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam)
                    : null;
                resample = string.IsNullOrEmpty(area)
                    || string.Equals(area, "ImmersiveColorSet", StringComparison.Ordinal)
                    || string.Equals(area, "WindowMetrics", StringComparison.Ordinal);
            }
            else if (message == WmThemeChanged || message == WmDwmColorizationColorChanged)
            {
                resample = true;
            }

            if (resample && _started)
            {
                _resampleTimer.Stop();
                _resampleTimer.Start();
            }
            return IntPtr.Zero;
        }

        private void HandleClockLeftClick()
        {
            if (_panel != null && _panel.IsVisible)
            {
                if (_panelPinned)
                {
                    HidePanel();
                    return;
                }
                // 悬浮展开的面板，单击后固定住，方便用鼠标去点“打开设置”。
                _panelPinned = true;
                _pinClickTimer.Start();
                return;
            }
            ShowPanel(true);
        }

        private void HandleClockRightClick()
        {
            HidePanel();
            EventHandler handler = MenuRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _panelCloseTimer.Stop();
            if (_panel == null || !_panel.IsVisible)
            {
                _hoverTimer.Stop();
                _hoverTimer.Start();
            }
        }

        private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _hoverTimer.Stop();
            if (_panel != null && _panel.IsVisible)
            {
                _panelCloseTimer.Stop();
                _panelCloseTimer.Start();
            }
        }

        private void OnHoverTick(object sender, EventArgs e)
        {
            _hoverTimer.Stop();
            if (!IsCursorOverClock() || (_panel != null && _panel.IsVisible))
            {
                return;
            }
            ShowPanel(false);
        }

        private void OnPanelCloseTick(object sender, EventArgs e)
        {
            _panelCloseTimer.Stop();
            if (_panelPinned || IsCursorOverClock() || IsCursorOverPanel())
            {
                return;
            }
            HidePanel();
        }

        private void OnPinClickTick(object sender, EventArgs e)
        {
            if (!_panelPinned || _panel == null || !_panel.IsVisible)
            {
                _pinClickTimer.Stop();
                return;
            }

            if (!NativeMethods.IsLeftMouseButtonDown())
            {
                return;
            }

            if (!IsCursorOverClock() && !IsCursorOverPanel())
            {
                HidePanel();
            }
        }

        private bool IsCursorOverClock()
        {
            if (!_hasClockRect)
            {
                return false;
            }

            NativeMethods.POINT point;
            if (!NativeMethods.GetCursorPos(out point))
            {
                return false;
            }
            return point.X >= _clockRect.Left
                && point.X < _clockRect.Right
                && point.Y >= _clockRect.Top
                && point.Y < _clockRect.Bottom;
        }

        private bool IsCursorOverPanel()
        {
            if (_panel == null || !_panel.IsVisible)
            {
                return false;
            }

            NativeMethods.POINT point;
            if (!NativeMethods.GetCursorPos(out point))
            {
                return false;
            }

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

        private void ShowPanel(bool pin)
        {
            if (!_started || !_hasClockRect || _clockWindow == IntPtr.Zero)
            {
                return;
            }

            EnsurePanel();
            _panelPinned = pin;
            _panel.SetModel(BuildPanelModel(TimeZoneCatalog.GetNow(_timeZone)));
            _panel.ShowNear(_clockRect, _dpiScale);
            if (pin)
            {
                _pinClickTimer.Start();
            }
            NativeMethods.RaiseTopmost(new WindowInteropHelper(this).Handle);
        }

        private void HidePanel()
        {
            _hoverTimer.Stop();
            _panelCloseTimer.Stop();
            _pinClickTimer.Stop();
            _panelPinned = false;
            if (_panel != null && _panel.IsVisible)
            {
                _panel.Hide();
            }
        }

        private void EnsurePanel()
        {
            if (_panel != null)
            {
                return;
            }

            _panel = new TaskbarPanelWindow();
            _panel.SettingsRequested += OnPanelSettingsRequested;
            _panel.MouseEnter += delegate
            {
                _panelCloseTimer.Stop();
            };
            _panel.MouseLeave += delegate
            {
                _panelCloseTimer.Stop();
                _panelCloseTimer.Start();
            };
            _panel.Closed += delegate { _panel = null; };
        }

        private void OnPanelSettingsRequested(object sender, EventArgs e)
        {
            EventHandler handler = SettingsRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RefreshPanel()
        {
            if (_panel != null && _panel.IsVisible)
            {
                _panel.SetModel(BuildPanelModel(TimeZoneCatalog.GetNow(_timeZone)));
            }
        }

        private void UpdateText(bool force)
        {
            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            string text = FormatEngine.IsTokenMode(_settings.TaskbarFormatMode)
                ? FormatEngine.Format(now, _weather, _settings.TaskbarFormat)
                : TimeFormatter.Format(now, _settings.TaskbarDotNetFormat);

            if (!force && string.Equals(text, _text.Text, StringComparison.Ordinal))
            {
                if (_panel != null && _panel.IsVisible)
                {
                    _panel.SetModel(BuildPanelModel(now));
                }
                return;
            }

            _text.Text = text;
            if (_hasClockRect)
            {
                ApplyVisuals();
            }
            if (_panel != null && _panel.IsVisible)
            {
                _panel.SetModel(BuildPanelModel(now));
            }
        }

        private void ApplyVisuals()
        {
            if (!_hasClockRect || string.IsNullOrEmpty(_text.Text))
            {
                return;
            }

            double widthDip = (_clockRect.Right - _clockRect.Left) / _dpiScale;
            double heightDip = (_clockRect.Bottom - _clockRect.Top) / _dpiScale;
            string[] lines = _text.Text.Split('\n');
            int lineCount = Math.Max(1, Math.Min(2, lines.Length));
            double lineHeight = heightDip / lineCount;
            double maxWidth = Math.Max(10.0, widthDip - 2.0);

            FontFamily family = _fontFamily;
            if (family == null)
            {
                family = new FontFamily(string.IsNullOrWhiteSpace(_settings.TaskbarFontFamilyName)
                    ? "Microsoft YaHei UI"
                    : _settings.TaskbarFontFamilyName);
            }

            double nativeSize = _baseFontSize > 0 ? _baseFontSize : 12.0;
            double fontScale = _settings.TaskbarFontScale > 0 ? _settings.TaskbarFontScale / 100.0 : 1.0;
            if (fontScale < 1.0)
            {
                fontScale = 1.0;
            }
            // 不按宽度一路缩小字号：先把字高用满行高（mytime 的字更高），
            // 再用横向缩放收紧字距，避免字变小、发虚。
            double size = Math.Min(lineHeight / 1.24, nativeSize * fontScale);
            if (size < nativeSize)
            {
                size = nativeSize;
            }
            if (size > lineHeight * 0.92)
            {
                size = lineHeight * 0.92;
            }

            FontWeight weight = _settings.TaskbarBold ? FontWeights.Bold : FontWeights.Normal;
            Typeface typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            double widest = MeasureWidestLine(lines, lineCount, typeface, size);

            // 横向压缩过多会显得字形发扁；先略降字号，但不低于系统原生字号。
            for (int i = 0; i < 40 && widest > maxWidth / 0.68 && size > nativeSize + 0.01; i++)
            {
                size -= 0.25;
                widest = MeasureWidestLine(lines, lineCount, typeface, size);
            }

            _text.Width = Math.Max(widest + 12.0, widthDip);
            _text.FontFamily = family;
            _text.FontSize = size;
            _text.FontWeight = weight;
            _text.LineHeight = lineHeight;
            _text.Foreground = new SolidColorBrush(_textColor);
            _text.RenderTransform = Transform.Identity;
        }

        private double MeasureWidestLine(string[] lines, int lineCount, Typeface typeface, double size)
        {
            double widest = 0.0;
            for (int line = 0; line < lineCount; line++)
            {
                string value = line < lines.Length ? lines[line] : string.Empty;
                if (value.Length == 0)
                {
                    continue;
                }
                FormattedText formatted = new FormattedText(
                    value,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    size,
                    Brushes.Black,
                    _dpiScale);
                if (formatted.WidthIncludingTrailingWhitespace > widest)
                {
                    widest = formatted.WidthIncludingTrailingWhitespace;
                }
            }
            return widest;
        }

        private TaskbarPanelModel BuildPanelModel(DateTime now)
        {
            TaskbarPanelModel model = new TaskbarPanelModel();
            model.TimeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            model.DateText = FormatEngine.Format(now, _weather, "yyyy年mm月dd日 dddd");
            if (string.IsNullOrEmpty(model.DateText))
            {
                model.DateText = now.ToString("yyyy年M月d日", CultureInfo.InvariantCulture);
            }
            model.LunarText = FormatEngine.Format(now, _weather, "NN MD");
            model.WeatherText = _weather != null ? _weather.DisplayText : string.Empty;
            model.ModeText = "当前状态：" + ReminderModes.DisplayName(_settings.CurrentMode);

            List<TaskbarAgendaLine> agenda = new List<TaskbarAgendaLine>();
            System.Text.StringBuilder signature = new System.Text.StringBuilder();

            if (_settings.Reminders != null)
            {
                for (int i = 0; i < _settings.Reminders.Count; i++)
                {
                    Reminder reminder = _settings.Reminders[i];
                    if (reminder == null || !reminder.Enabled)
                    {
                        continue;
                    }
                    if (!ReminderModes.AppliesToState(reminder.Mode, _settings.CurrentMode))
                    {
                        continue;
                    }
                    agenda.Add(new TaskbarAgendaLine(reminder.Time, reminder.Message, IsPast(reminder.Time, now)));
                    signature.Append("r").Append(reminder.Time).Append(reminder.Message).Append(';');
                }
            }

            if (_settings.Schedules != null)
            {
                for (int i = 0; i < _settings.Schedules.Count; i++)
                {
                    ScheduleItem schedule = _settings.Schedules[i];
                    if (schedule == null || !schedule.Enabled || !OccursToday(schedule, now))
                    {
                        continue;
                    }
                    string text = ScheduleCategories.DisplayName(schedule.Type) + " · " + schedule.Title;
                    agenda.Add(new TaskbarAgendaLine(schedule.Time, text, IsPast(schedule.Time, now)));
                    signature.Append("s").Append(schedule.Time).Append(text).Append(';');
                }
            }

            agenda.Sort(CompareAgenda);
            if (agenda.Count > 6)
            {
                agenda.RemoveRange(6, agenda.Count - 6);
            }

            model.Agenda = agenda;
            model.Signature = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)
                + "|"
                + _settings.CurrentMode
                + "|"
                + signature.ToString();
            return model;
        }

        private static int CompareAgenda(TaskbarAgendaLine left, TaskbarAgendaLine right)
        {
            return string.CompareOrdinal(left.Time ?? string.Empty, right.Time ?? string.Empty);
        }

        private static bool IsPast(string time, DateTime now)
        {
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
            return parsed.Hour < now.Hour || (parsed.Hour == now.Hour && parsed.Minute < now.Minute);
        }

        private static bool OccursToday(ScheduleItem schedule, DateTime now)
        {
            DateTime date;
            if (!ScheduleService.TryParseDate(schedule.Date, out date))
            {
                return false;
            }
            if (schedule.Annual)
            {
                return date.Month == now.Month && date.Day == now.Day;
            }
            return date.Date == now.Date;
        }

        private void SyncGeometry(bool force)
        {
            if (!_started)
            {
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            IntPtr shell = NativeMethods.FindWindow(ShellTrayClassName, null);
            if (shell == IntPtr.Zero || !NativeMethods.IsWindowVisible(shell))
            {
                SetVisible(false);
                return;
            }

            NativeMethods.RECT shellRect = new NativeMethods.RECT();
            if (!NativeMethods.GetWindowRect(shell, out shellRect)
                || shellRect.Bottom - shellRect.Top < 8
                || shellRect.Right - shellRect.Left < 8)
            {
                SetVisible(false);
                return;
            }

            // 任务栏被全屏窗口盖住或自动隐藏时，覆盖窗不应该浮在游戏/桌面上。
            int screenHeight = NativeMethods.GetSystemMetrics(1);
            if (shellRect.Bottom <= 2 || shellRect.Top >= screenHeight - 2)
            {
                SetVisible(false);
                return;
            }

            if (NativeMethods.IsFullscreenForeground())
            {
                SetVisible(false);
                return;
            }

            IntPtr clock = LocateNativeClock(shell);
            if (clock == IntPtr.Zero)
            {
                SetVisible(false);
                return;
            }

            NativeMethods.RECT rect = new NativeMethods.RECT();
            if (!NativeMethods.GetWindowRect(clock, out rect))
            {
                SetVisible(false);
                return;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 24 || height < 12)
            {
                SetVisible(false);
                return;
            }

            if (!IsTaskbarVisibleAt(shell, rect))
            {
                SetVisible(false);
                return;
            }

            bool changed = !_hasClockRect
                || rect.Left != _clockRect.Left
                || rect.Top != _clockRect.Top
                || rect.Right != _clockRect.Right
                || rect.Bottom != _clockRect.Bottom;

            _clockWindow = clock;
            _clockRect = rect;
            _hasClockRect = true;

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
                ApplyVisuals();
                PositionWindow();
                if (_panel != null && _panel.IsVisible)
                {
                    _panel.ShowNear(_clockRect, _dpiScale);
                }
            }
            else
            {
                NativeMethods.RaiseTopmost(hwnd);
            }

            SetVisible(true);
        }

        private void RefreshNativeFont(IntPtr clock)
        {
            string faceName;
            int pixelHeight;
            if (NativeMethods.TryGetWindowFont(clock, out faceName, out pixelHeight)
                && !string.IsNullOrEmpty(faceName)
                && pixelHeight > 0)
            {
                _fontFamily = new FontFamily(faceName);
                _baseFontSize = pixelHeight / _dpiScale;
                return;
            }

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
            if (width < 8 || height < 8)
            {
                return false;
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            bool wasVisible = IsVisible;
            if (wasVisible)
            {
                NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_TOPMOST,
                    -20000,
                    -20000,
                    0,
                    0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                System.Threading.Thread.Sleep(80);
            }

            try
            {
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

                    int topRow = Math.Max(2, Math.Min(height - 4, height / 10));
                    int bottomRow = Math.Max(2, Math.Min(height - 4, height - height / 10 - 1));
                    System.Drawing.Color top = AverageRow(bitmap, topRow);
                    System.Drawing.Color bottom = AverageRow(bitmap, bottomRow);
                    Color topColor = Color.FromRgb(top.R, top.G, top.B);
                    Color bottomColor = Color.FromRgb(bottom.R, bottom.G, bottom.B);

                    if (!IsPlausibleTaskbarColor(topColor, bottomColor))
                    {
                        return false;
                    }

                    _textColor = Luminance(topColor) + Luminance(bottomColor) >= 300.0
                        ? Color.FromRgb(0x1A, 0x1A, 0x1A)
                        : Colors.White;

                    LinearGradientBrush brush = new LinearGradientBrush(topColor, bottomColor, 90.0);
                    brush.Freeze();
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

        private static bool IsPlausibleTaskbarColor(Color top, Color bottom)
        {
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
            return Math.Abs(top.R - bottom.R) <= 90
                && Math.Abs(top.G - bottom.G) <= 90
                && Math.Abs(top.B - bottom.B) <= 90;
        }

        private static System.Drawing.Color AverageRow(System.Drawing.Bitmap bitmap, int y)
        {
            if (y < 0)
            {
                y = 0;
            }
            if (y >= bitmap.Height)
            {
                y = bitmap.Height - 1;
            }

            long r = 0;
            long g = 0;
            long b = 0;
            int count = 0;
            for (int x = 2; x < bitmap.Width - 2; x++)
            {
                System.Drawing.Color color = bitmap.GetPixel(x, y);
                r += color.R;
                g += color.G;
                b += color.B;
                count++;
            }

            if (count == 0)
            {
                return System.Drawing.Color.Empty;
            }
            return System.Drawing.Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
        }

        private static double Luminance(Color color)
        {
            return color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
        }

        private void PositionWindow()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !_hasClockRect)
            {
                return;
            }

            NativeMethods.MoveWindowPhysical(
                hwnd,
                _clockRect.Left,
                _clockRect.Top,
                _clockRect.Right - _clockRect.Left,
                _clockRect.Bottom - _clockRect.Top);
            NativeMethods.RaiseTopmost(hwnd);
        }

        private void SetVisible(bool visible)
        {
            if (visible)
            {
                if (!IsVisible)
                {
                    PositionWindow();
                }
                return;
            }

            HidePanel();
            if (IsVisible)
            {
                Hide();
            }
        }

        private double GetDpiScale()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformToDevice.M11;
            }
            return 1.0;
        }

        private static bool IsTaskbarVisibleAt(IntPtr shell, NativeMethods.RECT clockRect)
        {
            NativeMethods.POINT point;
            point.X = clockRect.Left - 2;
            point.Y = clockRect.Top + 2;
            IntPtr hit = NativeMethods.WindowFromPoint(point);
            if (hit == IntPtr.Zero)
            {
                return false;
            }

            // 命中任务栏的子窗口（托盘/IME/通知区）才算任务栏真的可见；
            // 全屏游戏的无边框窗口盖住它时，根窗口不是 Shell_TrayWnd。
            return NativeMethods.GetAncestor(hit, 2) == shell;
        }

        private static IntPtr LocateNativeClock(IntPtr shell)
        {
            IntPtr tray = NativeMethods.FindWindowEx(shell, IntPtr.Zero, TrayNotifyClassName, null);
            IntPtr clock = IntPtr.Zero;
            if (tray != IntPtr.Zero)
            {
                clock = NativeMethods.FindWindowEx(tray, IntPtr.Zero, TrayClockClassName, null);
            }
            if (clock == IntPtr.Zero)
            {
                clock = NativeMethods.FindWindowEx(shell, IntPtr.Zero, TrayClockClassName, null);
            }
            if (clock == IntPtr.Zero)
            {
                clock = NativeMethods.FindDescendantByClass(shell, TrayClockClassName);
            }
            return clock;
        }
    }
}
