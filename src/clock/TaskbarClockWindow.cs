using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GameClock
{
    /// <summary>
    /// Draws the clock over the Windows taskbar clock area.
    /// The native clock is hidden while this window is active and restored on exit.
    /// </summary>
    internal sealed class TaskbarClockWindow : Window
    {
        private const string ShellTrayClassName = "Shell_TrayWnd";
        private const string TrayNotifyClassName = "TrayNotifyWnd";
        private const string TrayClockClassName = "TrayClockWClass";
        private const int DefaultClockWidth = 74;
        private const int DefaultClockHeight = 40;
        private const int SyncIntervalMs = 2000;

        private readonly AppSettings _settings;
        private readonly LayeredText _clockText;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _syncTimer;
        private WeatherInfo _weather;
        private TimeZoneInfo _timeZone;
        private IntPtr _nativeClock;
        private bool _nativeClockMoved;
        private bool _started;
        private NativeMethods.RECT _clockRect;
        private bool _hasClockRect;
        private IntPtr _rectClock;
        private NativeMethods.RECT _lastOverlayRect;
        private bool _hasLastOverlay;

        public TaskbarClockWindow(AppSettings settings)
        {
            _settings = settings != null ? settings.Clone() : AppSettings.CreateDefault();
            _settings.Normalize();
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

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

            _clockText = new LayeredText();
            _clockText.IsHitTestVisible = false;

            Border root = new Border();
            root.Background = Brushes.Transparent;
            root.IsHitTestVisible = false;
            root.Child = _clockText;
            Content = root;

            _clockTimer = new DispatcherTimer(DispatcherPriority.Normal);
            _clockTimer.Interval = TimeSpan.FromMilliseconds(200);
            _clockTimer.Tick += OnClockTick;

            _syncTimer = new DispatcherTimer(DispatcherPriority.Background);
            _syncTimer.Interval = TimeSpan.FromMilliseconds(SyncIntervalMs);
            _syncTimer.Tick += OnSyncTick;
        }

        public void Start()
        {
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

        public void Stop()
        {
            _started = false;
            _clockTimer.Stop();
            _syncTimer.Stop();
            RestoreNativeClock();
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
            _settings.TaskbarColorHex = copy.TaskbarColorHex;
            _settings.TaskbarFontFamilyName = copy.TaskbarFontFamilyName;
            _settings.TaskbarFontSize = copy.TaskbarFontSize;
            _settings.TaskbarFormat = copy.TaskbarFormat;
            _settings.TimeZoneId = copy.TimeZoneId;
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            if (_started)
            {
                UpdateText(true);
                SyncGeometry();
            }
        }

        public void SetWeather(WeatherInfo weather)
        {
            _weather = weather;
            UpdateText(true);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ApplyTaskbarStyles(hwnd);
        }

        protected override void OnClosed(EventArgs e)
        {
            _clockTimer.Stop();
            _syncTimer.Stop();
            RestoreNativeClock();
            base.OnClosed(e);
        }

        private void OnClockTick(object sender, EventArgs e)
        {
            UpdateText(false);
        }

        private void OnSyncTick(object sender, EventArgs e)
        {
            SyncGeometry();
        }

        private void UpdateText(bool force)
        {
            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            string text = FormatEngine.Format(now, _weather, _settings.TaskbarFormat);

            if (!force && string.Equals(text, _clockText.Text, StringComparison.Ordinal))
            {
                return;
            }

            _clockText.Text = text;
            if (_hasClockRect)
            {
                ApplyLayout();
            }
        }

        private void ApplyLayout()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !_hasClockRect)
            {
                return;
            }

            NativeMethods.RECT overlay = BuildOverlayRect(_clockRect);
            int width = overlay.Right - overlay.Left;
            int height = overlay.Bottom - overlay.Top;
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

        private NativeMethods.RECT BuildOverlayRect(NativeMethods.RECT clockRect)
        {
            // The notification button abuts the clock slot on the right, so the overlay
            // must stay inside the native clock rectangle and shrink the text instead.
            NativeMethods.RECT result = new NativeMethods.RECT();
            result.Left = clockRect.Left;
            result.Top = clockRect.Top;
            result.Right = clockRect.Right;
            result.Bottom = clockRect.Bottom;
            return result;
        }

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

        private FontFamily GetTaskbarFontFamily()
        {
            return new FontFamily(string.IsNullOrWhiteSpace(_settings.TaskbarFontFamilyName)
                ? "Microsoft YaHei UI"
                : _settings.TaskbarFontFamilyName);
        }

        private double FitFontSize(string text, double widthDip, double heightDip)
        {
            double size = Math.Min(
                Math.Max(7.0, Math.Min(40.0, _settings.TaskbarFontSize)),
                Math.Max(6.0, heightDip / 2.6));
            if (string.IsNullOrEmpty(text))
            {
                return size;
            }

            double scale = GetDpiScale();
            double limitWidth = Math.Max(6.0, widthDip - 4.0);
            double limitHeight = Math.Max(4.0, heightDip - 2.0);
            Typeface typeface = new Typeface(GetTaskbarFontFamily(), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

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
                if (formatted.WidthIncludingTrailingWhitespace <= limitWidth
                    && formatted.Height <= limitHeight)
                {
                    break;
                }
                size -= 0.5;
            }

            return size;
        }

        private void SyncGeometry()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            IntPtr shell = NativeMethods.FindWindow(ShellTrayClassName, null);
            if (shell == IntPtr.Zero || !NativeMethods.IsWindowVisible(shell))
            {
                // Taskbar missing (explorer restarting) or auto-hidden: give the native clock back.
                RestoreNativeClock();
                SetVisible(false);
                return;
            }

            IntPtr clock = LocateNativeClock(shell);
            if (clock == IntPtr.Zero)
            {
                RestoreNativeClock();
                SetVisible(false);
                return;
            }

            NativeMethods.RECT rect = new NativeMethods.RECT();
            int width = 0;
            int height = 0;
            if (NativeMethods.GetWindowRect(clock, out rect))
            {
                width = rect.Right - rect.Left;
                height = rect.Bottom - rect.Top;
            }

            NativeMethods.RECT shellRect = new NativeMethods.RECT();
            bool hasShell = NativeMethods.GetWindowRect(shell, out shellRect);
            bool insideShell = hasShell
                && rect.Top >= shellRect.Top - 2
                && rect.Bottom <= shellRect.Bottom + 8;

            if (width >= 40 && height >= 20 && insideShell)
            {
                _clockRect = rect;
                _hasClockRect = true;
                _rectClock = clock;
            }
            else if (_hasClockRect && _rectClock == clock)
            {
                // Hiding the native clock collapses its layout; keep the last good rect.
                rect = _clockRect;
                width = rect.Right - rect.Left;
                height = rect.Bottom - rect.Top;
            }
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

        private static NativeMethods.RECT GetFallbackRect(IntPtr shell)
        {
            NativeMethods.RECT baseRect = new NativeMethods.RECT();
            IntPtr tray = NativeMethods.FindWindowEx(shell, IntPtr.Zero, TrayNotifyClassName, null);
            bool found = tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out baseRect);
            if (!found)
            {
                found = NativeMethods.GetWindowRect(shell, out baseRect);
            }
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
            if (result.Bottom - result.Top < 16)
            {
                result.Bottom = result.Top + DefaultClockHeight;
            }
            return result;
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
            return clock;
        }

        private void MoveNativeClockAway(IntPtr clock)
        {
            if (clock == IntPtr.Zero)
            {
                return;
            }

            if (_nativeClock != clock)
            {
                _nativeClock = clock;
                _nativeClockMoved = false;
            }

            if (_nativeClockMoved)
            {
                // A taskbar relayout can pull the clock back into its slot; move it out again.
                NativeMethods.RECT probe;
                if (NativeMethods.GetWindowRect(clock, out probe) && probe.Left < -1000)
                {
                    return;
                }
                _nativeClockMoved = false;
            }

            if (!_nativeClockMoved)
            {
                // Hiding the clock removes its taskbar slot and shifts the tray icons.
                // Moving it off the tray keeps the slot reserved while the overlay covers it.
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

        private void RestoreNativeClock()
        {
            if (!_nativeClockMoved || _nativeClock == IntPtr.Zero)
            {
                return;
            }

            if (_hasClockRect)
            {
                // TrayClockWClass is a taskbar child: SetWindowPos expects coordinates
                // relative to the parent's client area, not screen coordinates.
                int left = 0;
                int top = 0;
                IntPtr parent = NativeMethods.GetParent(_nativeClock);
                NativeMethods.RECT parentRect = new NativeMethods.RECT();
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

            // Ask the shell to relayout the tray so the clock returns to its slot.
            IntPtr shell = NativeMethods.FindWindow(ShellTrayClassName, null);
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

        private void SetVisible(bool visible)
        {
            if (visible)
            {
                if (!IsVisible)
                {
                    Show();
                }
                Opacity = 1.0;
            }
            else
            {
                Opacity = 0.0;
                if (IsVisible)
                {
                    Hide();
                }
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
    }
}
