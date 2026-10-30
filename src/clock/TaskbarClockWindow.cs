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
        private bool _nativeClockHidden;
        private bool _started;
        private NativeMethods.RECT _clockRect;
        private bool _hasClockRect;
        private IntPtr _rectClock;

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
            _settings.ColorHex = copy.ColorHex;
            _settings.ShowShadow = copy.ShowShadow;
            _settings.FontFamilyName = copy.FontFamilyName;
            _settings.FontSize = copy.FontSize;
            _settings.FormatMode = copy.FormatMode;
            _settings.TimeFormat = copy.TimeFormat;
            _settings.TokenTimeFormat = copy.TokenTimeFormat;
            _settings.TimeZoneId = copy.TimeZoneId;
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

            UpdateText(true);
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
            string text = FormatEngine.IsTokenMode(_settings.FormatMode)
                ? FormatEngine.Format(now, _weather, _settings.TokenTimeFormat)
                : TimeFormatter.Format(now, _settings.TimeFormat);
            text = FirstLine(text);

            if (!force && string.Equals(text, _clockText.Text, StringComparison.Ordinal))
            {
                return;
            }

            _clockText.Text = text;
            ApplyVisuals(GetWidthDip(), GetHeightDip(), text);
        }

        private void ApplyVisuals(double widthDip, double heightDip, string text)
        {
            double fontSize = FitFontSize(text, widthDip, heightDip);
            FontFamily family = new FontFamily(string.IsNullOrWhiteSpace(_settings.FontFamilyName)
                ? "Consolas"
                : _settings.FontFamilyName);
            _clockText.ApplyStyle(
                family,
                fontSize,
                FontWeights.SemiBold,
                _settings.TimeColor,
                _settings.ShowShadow,
                false);
        }

        private double FitFontSize(string text, double widthDip, double heightDip)
        {
            double size = Math.Max(9.0, Math.Min(26.0, heightDip * 0.66));
            if (string.IsNullOrEmpty(text))
            {
                return size;
            }

            double scale = GetDpiScale();
            double limit = Math.Max(8.0, widthDip - 4.0);
            FontFamily family = new FontFamily(string.IsNullOrWhiteSpace(_settings.FontFamilyName)
                ? "Consolas"
                : _settings.FontFamilyName);
            Typeface typeface = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

            for (int i = 0; i < 36 && size > 8.0; i++)
            {
                FormattedText formatted = new FormattedText(
                    text,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    size,
                    Brushes.Black,
                    scale);
                if (formatted.WidthIncludingTrailingWhitespace <= limit)
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

            if (width >= 40 && height >= 20)
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

            HideNativeClock(clock);
            NativeMethods.MoveWindowPhysical(hwnd, rect.Left, rect.Top, width, height);
            SetVisible(true);

            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            string text = FormatEngine.IsTokenMode(_settings.FormatMode)
                ? FormatEngine.Format(now, _weather, _settings.TokenTimeFormat)
                : TimeFormatter.Format(now, _settings.TimeFormat);
            text = FirstLine(text);
            _clockText.Text = text;
            ApplyVisuals(width / GetDpiScale(), height / GetDpiScale(), text);
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

        private void HideNativeClock(IntPtr clock)
        {
            if (clock == IntPtr.Zero)
            {
                return;
            }

            if (_nativeClock != clock)
            {
                _nativeClock = clock;
                _nativeClockHidden = false;
            }

            if (!_nativeClockHidden)
            {
                NativeMethods.ShowWindow(clock, NativeMethods.SW_HIDE);
                _nativeClockHidden = true;
            }
        }

        private void RestoreNativeClock()
        {
            if (!_nativeClockHidden || _nativeClock == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.ShowWindow(_nativeClock, NativeMethods.SW_SHOWNOACTIVATE);
            _nativeClockHidden = false;

            // Hiding the clock collapses its taskbar layout; ask the shell to relayout it.
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

        private double GetWidthDip()
        {
            return ActualWidth > 1.0 ? ActualWidth : 1.0;
        }

        private double GetHeightDip()
        {
            return ActualHeight > 1.0 ? ActualHeight : 1.0;
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

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length > 0)
                {
                    return line;
                }
            }
            return string.Empty;
        }
    }
}
