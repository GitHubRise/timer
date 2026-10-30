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
    internal sealed class ClockWindow : Window
    {
        private const double DefaultMargin = 26.0;

        private AppSettings _settings;
        private TimeZoneInfo _timeZone;
        private readonly Border _root;
        private readonly StackPanel _stack;
        private readonly LayeredText _clockText;
        private readonly TextBlock _weatherText;
        private readonly Border _reminderPanel;
        private readonly TextBlock _reminderTimeText;
        private readonly TextBlock _reminderMessageText;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _topmostTimer;
        private readonly DispatcherTimer _reminderTimer;
        private readonly DispatcherTimer _reminderHoverTimer;
        private readonly DispatcherTimer _fullscreenTimer;
        private readonly Queue<ReminderNotice> _reminderQueue;

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

        public event EventHandler MenuRequested;
        public event EventHandler PositionChanged;
        public event EventHandler ScaleChanged;
        public event EventHandler<ReminderEventArgs> ReminderTriggered;
        public event EventHandler<ScheduleTriggeredEventArgs> ScheduleTriggered;

        public ClockWindow(AppSettings settings)
        {
            _settings = settings ?? AppSettings.CreateDefault();
            _settings.Normalize();
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

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

            if (_settings.Left.HasValue && _settings.Top.HasValue)
            {
                Left = _settings.Left.Value;
                Top = _settings.Top.Value;
            }

            _root = new Border();
            // Layered windows are hit-tested per pixel. This almost invisible fill
            // makes the full clock area draggable and right-clickable.
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

            StackPanel reminderContent = new StackPanel();
            reminderContent.Children.Add(_reminderTimeText);
            reminderContent.Children.Add(_reminderMessageText);

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

            _reminderQueue = new Queue<ReminderNotice>();

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

            Loaded += OnLoaded;
            Closed += OnClosed;
            LocationChanged += OnLocationChanged;
            SizeChanged += OnSizeChanged;
            _root.MouseLeftButtonDown += OnMouseLeftButtonDown;
            _root.MouseRightButtonUp += OnMouseRightButtonUp;
            _root.MouseWheel += OnMouseWheel;

            ApplyVisuals();
            UpdateClock(true);
        }

        public void ApplySettings(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            _settings = settings;
            _settings.Normalize();
            _timeZone = TimeZoneCatalog.GetZone(_settings.TimeZoneId);

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
            ApplyReminderDisplaySettings();

            if (!_settings.HideOnFullscreen)
            {
                RestoreFromFullscreen();
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ApplyOverlayStyles(hwnd, _settings.ClickThrough);
            NativeMethods.SetTopmost(hwnd, _settings.Topmost);

            UpdateLayout();
            Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    EnsureVisible();
                    CapturePosition();
                }),
                DispatcherPriority.Background);
        }

        public void ApplyClickThrough(bool enabled)
        {
            _settings.ClickThrough = enabled;
            if (!_loaded)
            {
                return;
            }
            NativeMethods.SetClickThrough(new WindowInteropHelper(this).Handle, enabled);
        }

        public void SetWeather(WeatherInfo info)
        {
            _weatherInfo = info;
            RefreshWeatherText();
        }

        public void ApplyTopmost(bool enabled)
        {
            _settings.Topmost = enabled;
            if (!_loaded)
            {
                return;
            }

            Topmost = enabled;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetTopmost(hwnd, enabled);
        }

        public void ReassertTopmost()
        {
            if (!_loaded || !_settings.Topmost)
            {
                return;
            }
            NativeMethods.SetTopmost(new WindowInteropHelper(this).Handle, true);
        }

        public void SetTopmostWatchSuspended(bool suspended)
        {
            _topmostWatchSuspended = suspended;
        }

        public void CheckRemindersNow()
        {
            if (_loaded)
            {
                DateTime now = TimeZoneCatalog.GetNow(_timeZone);
                CheckDueReminders(now);
                CheckDueSchedules(now);
            }
        }

        public void ShowReminderPreview(string time, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string normalizedTime;
            if (!TimeFormatter.TryNormalizeTime(time, out normalizedTime))
            {
                normalizedTime = string.IsNullOrWhiteSpace(time) ? "--:--" : time.Trim();
            }
            EnqueueReminder(new ReminderNotice(normalizedTime, message.Trim(), true));
        }

        public void ResetPosition()
        {
            ResetPositionToDefault();
            CapturePosition();
            if (PositionChanged != null)
            {
                PositionChanged(this, EventArgs.Empty);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;
            ApplyVisuals();
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
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _clockTimer.Stop();
            _topmostTimer.Stop();
            _reminderTimer.Stop();
            _reminderHoverTimer.Stop();
            _fullscreenTimer.Stop();
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source = null;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.ApplyOverlayStyles(hwnd, _settings.ClickThrough);

            if (_source == null)
            {
                _source = HwndSource.FromHwnd(hwnd);
                if (_source != null)
                {
                    _source.AddHook(WndProc);
                }
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);
            }

            return IntPtr.Zero;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_settings.ClickThrough)
            {
                return;
            }

            if (e.ChangedButton == MouseButton.Left && e.LeftButton == MouseButtonState.Pressed)
            {
                e.Handled = true;
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
                EnsureVisible();
                CapturePosition();
            }
        }

        private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_settings.ClickThrough)
            {
                return;
            }

            e.Handled = true;
            if (MenuRequested != null)
            {
                MenuRequested(this, EventArgs.Empty);
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_settings.ClickThrough || e.Delta == 0)
            {
                return;
            }

            double step = e.Delta > 0 ? 4.0 : -4.0;
            double next = _settings.FontSize + step;
            next = Math.Max(AppSettings.MinFontSize, Math.Min(AppSettings.MaxFontSize, next));
            if (Math.Abs(next - _settings.FontSize) < 0.01)
            {
                return;
            }

            e.Handled = true;
            _settings.FontSize = next;
            ApplyVisuals();
            if (ScaleChanged != null)
            {
                ScaleChanged(this, EventArgs.Empty);
            }
        }

        private void OnFullscreenTick(object sender, EventArgs e)
        {
            if (!_loaded)
            {
                return;
            }

            if (!_settings.HideOnFullscreen)
            {
                RestoreFromFullscreen();
                return;
            }

            bool fullscreen = NativeMethods.IsFullscreenForeground();

            if (fullscreen)
            {
                if (IsVisible && !_hiddenForFullscreen)
                {
                    _hiddenForFullscreen = true;
                    Hide();
                }
            }
            else if (_hiddenForFullscreen)
            {
                RestoreFromFullscreen();
            }
        }

        private void RestoreFromFullscreen()
        {
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
        }

        private void OnLocationChanged(object sender, EventArgs e)
        {
            if (!_positionReady || _applyingPosition)
            {
                return;
            }

            CapturePosition();
            if (PositionChanged != null)
            {
                PositionChanged(this, EventArgs.Empty);
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_positionReady)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                new Action(KeepInsideScreenAfterResize),
                DispatcherPriority.Background);
        }

        private void KeepInsideScreenAfterResize()
        {
            if (!_positionReady)
            {
                return;
            }

            Rect virtualScreen = GetVirtualScreen();
            double width = Math.Max(ActualWidth, 1.0);
            double height = Math.Max(ActualHeight, 1.0);
            double left = Left;
            double top = Top;

            if (width <= virtualScreen.Width - 8.0)
            {
                if (left + width > virtualScreen.Right - 4.0)
                {
                    left = virtualScreen.Right - width - 4.0;
                }
                if (left < virtualScreen.Left + 4.0)
                {
                    left = virtualScreen.Left + 4.0;
                }
            }
            else
            {
                left = virtualScreen.Left + 4.0;
            }

            if (height <= virtualScreen.Height - 8.0)
            {
                if (top + height > virtualScreen.Bottom - 4.0)
                {
                    top = virtualScreen.Bottom - height - 4.0;
                }
                if (top < virtualScreen.Top + 4.0)
                {
                    top = virtualScreen.Top + 4.0;
                }
            }
            else
            {
                top = virtualScreen.Top + 4.0;
            }

            if (Math.Abs(left - Left) > 0.1 || Math.Abs(top - Top) > 0.1)
            {
                SetWindowPosition(left, top);
                CapturePosition();
                if (PositionChanged != null)
                {
                    PositionChanged(this, EventArgs.Empty);
                }
            }
        }

        private void OnClockTick(object sender, EventArgs e)
        {
            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            UpdateClock(now, false);
            CheckDueReminders(now);
            CheckDueSchedules(now);
        }

        private void OnTopmostTick(object sender, EventArgs e)
        {
            if (!_loaded || _topmostWatchSuspended || !_settings.Topmost || !IsVisible)
            {
                return;
            }
            NativeMethods.SetTopmost(new WindowInteropHelper(this).Handle, true);
        }

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
            _reminderPanel.BorderBrush = UiTheme.BrushFrom(
                Color.FromArgb(130, color.R, color.G, color.B));

            double weatherSize = _settings.WeatherFontSize;
            Color weatherColor = _settings.WeatherColor;
            _weatherText.FontFamily = FontCatalog.GetFontFamily(_settings.WeatherFontFamilyName);
            _weatherText.FontSize = weatherSize;
            _weatherText.FontWeight = _settings.WeatherFontBold ? FontWeights.Bold : FontWeights.Normal;
            _weatherText.FontStyle = _settings.WeatherFontItalic ? FontStyles.Italic : FontStyles.Normal;
            _weatherText.Margin = new Thickness(0, Math.Max(2.0, weatherSize * 0.20), 0, 0);
            _weatherText.Foreground = UiTheme.BrushFrom(
                Color.FromArgb(232, weatherColor.R, weatherColor.G, weatherColor.B));
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
            else
            {
                _weatherText.Effect = null;
            }

            UpdateWeatherVisibility();

            _lastDisplayText = null;
            UpdateClock(true);
        }

        private void UpdateWeatherVisibility()
        {
            bool show = _settings.WeatherEnabled && !string.IsNullOrWhiteSpace(_weatherText.Text);
            Visibility target = show ? Visibility.Visible : Visibility.Collapsed;
            if (_weatherText.Visibility != target)
            {
                _weatherText.Visibility = target;
                if (_loaded)
                {
                    UpdateLayout();
                    Dispatcher.BeginInvoke(
                        new Action(EnsureVisible),
                        DispatcherPriority.Background);
                }
            }
        }

        private void UpdateClock(bool force)
        {
            UpdateClock(TimeZoneCatalog.GetNow(_timeZone), force);
        }

        private void UpdateClock(DateTime now, bool force)
        {
            WeatherInfo weather = _settings.WeatherEnabled ? _weatherInfo : null;
            string display = FormatEngine.IsTokenMode(_settings.FormatMode)
                ? FormatEngine.Format(now, weather, _settings.TokenTimeFormat)
                : TimeFormatter.Format(now, _settings.TimeFormat);
            if (force || !string.Equals(display, _lastDisplayText, StringComparison.Ordinal))
            {
                _clockText.Text = display;
                _lastDisplayText = display;
            }
        }

        private bool TimeFormatUsesWeather()
        {
            return FormatEngine.IsTokenMode(_settings.FormatMode)
                && FormatEngine.ContainsWeatherToken(_settings.TokenTimeFormat);
        }

        private string BuildWeatherText(DateTime now)
        {
            if (!_settings.WeatherEnabled || _weatherInfo == null)
            {
                return string.Empty;
            }
            if (TimeFormatUsesWeather())
            {
                return string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(_settings.WeatherFormat))
            {
                return FormatEngine.Format(now, _weatherInfo, _settings.WeatherFormat);
            }
            return _weatherInfo.DisplayText;
        }

        private void RefreshWeatherText()
        {
            DateTime now = TimeZoneCatalog.GetNow(_timeZone);
            string text = BuildWeatherText(now);
            bool changed = !string.Equals(_weatherText.Text, text, StringComparison.Ordinal);
            _weatherText.Text = text;
            UpdateWeatherVisibility();
            if (changed && _loaded)
            {
                UpdateLayout();
                Dispatcher.BeginInvoke(
                    new Action(EnsureVisible),
                    DispatcherPriority.Background);
            }
            UpdateClock(now, true);
        }

        private void CheckDueReminders(DateTime now)
        {
            if (_settings.Reminders == null || _settings.Reminders.Count == 0)
            {
                return;
            }

            string currentTime = now.ToString("HH:mm");
            string today = now.ToString("yyyy-MM-dd");

            for (int i = 0; i < _settings.Reminders.Count; i++)
            {
                Reminder reminder = _settings.Reminders[i];
                if (reminder == null || !reminder.Enabled)
                {
                    continue;
                }
                if (!string.Equals(reminder.Time, currentTime, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!ReminderModes.AppliesToState(reminder.Mode, _settings.CurrentMode))
                {
                    continue;
                }
                if (string.Equals(reminder.LastTriggeredDate, today, StringComparison.Ordinal))
                {
                    continue;
                }

                reminder.LastTriggeredDate = today;
                EnqueueReminder(new ReminderNotice(reminder.Time, reminder.Message));

                if (ReminderTriggered != null)
                {
                    ReminderTriggered(this, new ReminderEventArgs(reminder));
                }
            }
        }

        private void CheckDueSchedules(DateTime now)
        {
            if (_settings.Schedules == null || _settings.Schedules.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _settings.Schedules.Count; i++)
            {
                ScheduleItem schedule = _settings.Schedules[i];
                if (schedule == null || !schedule.Enabled)
                {
                    continue;
                }

                DateTime occurrence;
                if (!ScheduleService.TryFindDueOccurrence(schedule, now, out occurrence))
                {
                    continue;
                }

                string occurrenceKey = ScheduleService.GetOccurrenceKey(occurrence);
                if (string.Equals(schedule.LastTriggeredKey, occurrenceKey, StringComparison.Ordinal))
                {
                    continue;
                }

                schedule.LastTriggeredKey = occurrenceKey;
                EnqueueScheduleNotice(schedule, occurrence);

                if (ScheduleTriggered != null)
                {
                    ScheduleTriggered(this, new ScheduleTriggeredEventArgs(schedule, occurrence));
                }
            }
        }

        private void EnqueueScheduleNotice(ScheduleItem schedule, DateTime occurrence)
        {
            string label = ScheduleCategories.DisplayName(schedule.Type) + "日程";
            System.Text.StringBuilder message = new System.Text.StringBuilder();
            message.Append(schedule.Title);
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

        private void EnqueueReminder(ReminderNotice notice)
        {
            if (_reminderVisible)
            {
                _reminderQueue.Enqueue(notice);
                return;
            }
            ShowReminder(notice);
        }

        private void ShowReminder(ReminderNotice notice)
        {
            _reminderTimeText.Text = notice.Time + " · " + notice.Label;
            _reminderMessageText.Text = notice.Message;
            _reminderPanel.Visibility = Visibility.Visible;
            _reminderPanel.BeginAnimation(UIElement.OpacityProperty, null);
            _reminderPanel.Opacity = 1.0;
            int blinkCount = _settings.ReminderBlinkCount;
            if (blinkCount > 0)
            {
                DoubleAnimation blink = new DoubleAnimation();
                blink.From = 1.0;
                blink.To = 0.18;
                blink.Duration = TimeSpan.FromMilliseconds(260);
                blink.AutoReverse = true;
                blink.RepeatBehavior = new RepeatBehavior(blinkCount);
                blink.FillBehavior = FillBehavior.Stop;
                _reminderPanel.BeginAnimation(UIElement.OpacityProperty, blink);
            }

            if (_settings.ReminderSoundEnabled)
            {
                AlarmSound.Play();
            }

            _reminderVisible = true;
            _reminderTimer.Stop();
            _reminderTimer.Interval = TimeSpan.FromSeconds(_settings.ReminderDisplaySeconds);
            _reminderTimer.Start();
            StartReminderHoverWatch();

            UpdateLayout();
            Dispatcher.BeginInvoke(
                new Action(EnsureVisible),
                DispatcherPriority.Background);
        }

        private void ApplyReminderDisplaySettings()
        {
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

        private void StartReminderHoverWatch()
        {
            _reminderHoverStartedUtc = null;
            if (_settings.ReminderHoverCloseEnabled)
            {
                _reminderHoverTimer.Start();
            }
            else
            {
                _reminderHoverTimer.Stop();
            }
        }

        private void OnReminderTimerTick(object sender, EventArgs e)
        {
            DismissCurrentReminder();
        }

        private void OnReminderHoverTick(object sender, EventArgs e)
        {
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
            if (!NativeMethods.GetCursorPos(out cursor))
            {
                _reminderHoverStartedUtc = null;
                return;
            }

            Point topLeft = _reminderPanel.PointToScreen(new Point(0, 0));
            Point bottomRight = _reminderPanel.PointToScreen(
                new Point(_reminderPanel.ActualWidth, _reminderPanel.ActualHeight));
            bool inside = cursor.X >= topLeft.X
                && cursor.X <= bottomRight.X
                && cursor.Y >= topLeft.Y
                && cursor.Y <= bottomRight.Y;

            if (!inside)
            {
                _reminderHoverStartedUtc = null;
                return;
            }

            if (!_reminderHoverStartedUtc.HasValue)
            {
                _reminderHoverStartedUtc = DateTime.UtcNow;
                return;
            }

            if ((DateTime.UtcNow - _reminderHoverStartedUtc.Value).TotalSeconds >= 3.0)
            {
                _reminderHoverStartedUtc = null;
                DismissCurrentReminder();
            }
        }

        private void DismissCurrentReminder()
        {
            _reminderTimer.Stop();
            _reminderHoverTimer.Stop();
            _reminderHoverStartedUtc = null;
            _reminderPanel.BeginAnimation(UIElement.OpacityProperty, null);
            _reminderPanel.Visibility = Visibility.Collapsed;
            _reminderVisible = false;

            if (_reminderQueue.Count > 0)
            {
                ReminderNotice next = _reminderQueue.Dequeue();
                Dispatcher.BeginInvoke(
                    new Action(delegate { ShowReminder(next); }),
                    DispatcherPriority.Background);
            }
        }

        private void RestorePosition()
        {
            if (_settings.Left.HasValue && _settings.Top.HasValue)
            {
                SetWindowPosition(_settings.Left.Value, _settings.Top.Value);
                if (HasUsableVisibleArea())
                {
                    return;
                }
            }
            ResetPositionToDefault();
        }

        private void ResetPositionToDefault()
        {
            Rect workArea = SystemParameters.WorkArea;
            double width = ActualWidth > 0 ? ActualWidth : 300;
            double height = ActualHeight > 0 ? ActualHeight : 90;
            double left = workArea.Right - width - DefaultMargin;
            double top = workArea.Top + DefaultMargin;
            SetWindowPosition(left, top);
        }

        private void SetWindowPosition(double left, double top)
        {
            _applyingPosition = true;
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

        private bool HasUsableVisibleArea()
        {
            Rect virtualScreen = GetVirtualScreen();
            double width = Math.Max(ActualWidth, 1.0);
            double height = Math.Max(ActualHeight, 1.0);
            double visibleWidth = Math.Min(Left + width, virtualScreen.Right) - Math.Max(Left, virtualScreen.Left);
            double visibleHeight = Math.Min(Top + height, virtualScreen.Bottom) - Math.Max(Top, virtualScreen.Top);
            return visibleWidth >= Math.Min(80.0, width) && visibleHeight >= Math.Min(36.0, height);
        }

        private void EnsureVisible()
        {
            if (!_positionReady)
            {
                return;
            }

            Rect virtualScreen = GetVirtualScreen();
            double width = Math.Max(ActualWidth, 1.0);
            double height = Math.Max(ActualHeight, 1.0);
            double left = Left;
            double top = Top;

            double minVisibleWidth = Math.Min(90.0, width);
            double minVisibleHeight = Math.Min(40.0, height);

            if (left + width < virtualScreen.Left + minVisibleWidth)
            {
                left = virtualScreen.Left + minVisibleWidth - width;
            }
            if (left > virtualScreen.Right - minVisibleWidth)
            {
                left = virtualScreen.Right - minVisibleWidth;
            }
            if (top + height < virtualScreen.Top + minVisibleHeight)
            {
                top = virtualScreen.Top + minVisibleHeight - height;
            }
            if (top > virtualScreen.Bottom - minVisibleHeight)
            {
                top = virtualScreen.Bottom - minVisibleHeight;
            }

            if (Math.Abs(left - Left) > 0.1 || Math.Abs(top - Top) > 0.1)
            {
                SetWindowPosition(left, top);
                CapturePosition();
                if (PositionChanged != null)
                {
                    PositionChanged(this, EventArgs.Empty);
                }
            }
        }

        private Rect GetVirtualScreen()
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }

        private void CapturePosition()
        {
            if (!_positionReady)
            {
                return;
            }
            _settings.Left = Left;
            _settings.Top = Top;
        }

        private void CapturePositionQuietly()
        {
            _settings.Left = Left;
            _settings.Top = Top;
        }

        private sealed class ReminderNotice
        {
            public ReminderNotice(string time, string message)
                : this(time, message, "提醒")
            {
            }

            public ReminderNotice(string time, string message, bool isPreview)
                : this(time, message, isPreview ? "测试提醒" : "提醒")
            {
            }

            public ReminderNotice(string time, string message, string label)
            {
                Time = time;
                Message = message;
                Label = label;
            }

            public string Time { get; private set; }
            public string Message { get; private set; }
            public string Label { get; private set; }
        }
    }
}
