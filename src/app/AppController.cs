using System;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace GameClock
{
    internal sealed class AppController : IDisposable
    {
        private readonly Application _app;
        private AppSettings _settings;
        private ClockWindow _clock;
        private TaskbarClockWindow _taskbarClock;
        private WeatherInfo _weatherInfo;
        private OverlayMenu _menu;
        private WinForms.NotifyIcon _tray;
        private System.Drawing.Icon _trayIcon;
        private SettingsWindow _settingsWindow;
        private DispatcherTimer _saveTimer;
        private DispatcherTimer _weatherTimer;
        private int _weatherGeneration;
        private bool _menuOpen;
        private bool _settingsOpen;
        private bool _exiting;
        private bool _disposed;

        public AppController(Application app)
        {
            _app = app;
        }

        public void ShowSettingsFromExternal()
        {
            if (_exiting || _clock == null)
            {
                return;
            }
            ShowSettings();
        }

        public void Start()
        {
            _settings = SettingsStore.Load();
            AutoStartManager.SetEnabled(_settings.StartWithWindows);
            _saveTimer = new DispatcherTimer(DispatcherPriority.Background);
            _saveTimer.Interval = TimeSpan.FromMilliseconds(550);
            _saveTimer.Tick += OnSaveTimerTick;

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
            _clock.ReassertTopmost();

            _weatherTimer = new DispatcherTimer(DispatcherPriority.Background);
            _weatherTimer.Interval = TimeSpan.FromMinutes(30.0);
            _weatherTimer.Tick += OnWeatherTimerTick;
            ConfigureWeatherTimer();
            RefreshWeather();
        }

        private void ApplyDisplaySurfaces()
        {
            if (_settings == null || _exiting || _clock == null)
            {
                return;
            }

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

            if (_settings.ShowTaskbarClock)
            {
                if (_taskbarClock == null)
                {
                    _taskbarClock = new TaskbarClockWindow(_settings);
                }
                else
                {
                    _taskbarClock.ApplySettings(_settings);
                }
                _taskbarClock.SetWeather(_settings.WeatherEnabled ? _weatherInfo : null);
                _taskbarClock.Start();
            }
            else if (_taskbarClock != null)
            {
                _taskbarClock.Stop();
            }
        }

        private void ConfigureWeatherTimer()
        {
            if (_weatherTimer == null)
            {
                return;
            }

            _weatherTimer.Stop();
            if (_settings == null || !_settings.WeatherEnabled)
            {
                return;
            }

            int minutes = WeatherRefreshOptions.Normalize(_settings.WeatherRefreshMinutes);
            _weatherTimer.Interval = TimeSpan.FromMinutes(minutes);
            _weatherTimer.Start();
        }

        private void CreateTrayIcon()
        {
            _trayIcon = LoadApplicationIcon();

            _tray = new WinForms.NotifyIcon();
            _tray.Icon = _trayIcon;
            _tray.Text = "游戏时间悬浮窗";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu.Menu;
            _tray.DoubleClick += OnTrayDoubleClick;
        }

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
            }
            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        }

        private void OnTrayDoubleClick(object sender, EventArgs e)
        {
            ShowSettings();
        }

        private void OnClockMenuRequested(object sender, EventArgs e)
        {
            if (_menu != null && !_exiting)
            {
                _menu.ShowAtCursor();
            }
        }

        private void OnClockPositionChanged(object sender, EventArgs e)
        {
            ScheduleSave();
        }

        private void OnClockScaleChanged(object sender, EventArgs e)
        {
            ScheduleSave();
        }

        private void OnWeatherTimerTick(object sender, EventArgs e)
        {
            RefreshWeather();
        }

        private void RefreshWeather()
        {
            if (_clock == null)
            {
                return;
            }
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

            int generation = ++_weatherGeneration;
            string city = _settings.WeatherCity;
            string tokenFormat = FormatEngine.IsTokenMode(_settings.FormatMode)
                ? _settings.TokenTimeFormat
                : string.Empty;
            string combinedFormat = tokenFormat + "\n" + (_settings.WeatherFormat ?? string.Empty);
            WeatherFetchOptions options = new WeatherFetchOptions();
            options.IncludeAirQuality = FormatEngine.RequiresAirQuality(combinedFormat);
            options.IncludePublicIp = FormatEngine.RequiresPublicIp(combinedFormat);
            WeatherService.FetchAsync(city, options, delegate(WeatherInfo info)
            {
                if (_exiting || _disposed)
                {
                    return;
                }

                try
                {
                    _app.Dispatcher.BeginInvoke(
                        new Action(delegate
                        {
                            if (_exiting || _disposed || generation != _weatherGeneration)
                            {
                                return;
                            }
                            if (_settings == null || !_settings.WeatherEnabled || _clock == null)
                            {
                                return;
                            }
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
                }
            });
        }

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

        private void OnMenuOpening(object sender, EventArgs e)
        {
            SyncMenu();
        }

        private void OnMenuOpened(object sender, EventArgs e)
        {
            _menuOpen = true;
            UpdateTopmostSuspension();
        }

        private void OnMenuClosed(object sender, EventArgs e)
        {
            _menuOpen = false;
            UpdateTopmostSuspension();
            if (_clock != null && !_exiting && !_settingsOpen)
            {
                _clock.ReassertTopmost();
            }
        }

        private void SyncMenu()
        {
            if (_menu != null && _settings != null)
            {
                _menu.Sync(_settings.Topmost, _settings.ClickThrough, _settings.CurrentMode);
            }
        }

        private void OnMenuSettings(object sender, EventArgs e)
        {
            ShowSettings();
        }

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

            if (_settings.ClickThrough)
            {
                ShowClickThroughBalloon();
            }
        }

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

        private void OnMenuWorkMode(object sender, EventArgs e)
        {
            SetCurrentMode(ReminderModes.Work);
        }

        private void OnMenuLeisureMode(object sender, EventArgs e)
        {
            SetCurrentMode(ReminderModes.Leisure);
        }

        private void SetCurrentMode(string mode)
        {
            if (_settings == null || _exiting)
            {
                return;
            }

            string normalized = ReminderModes.Normalize(mode);
            if (string.Equals(_settings.CurrentMode, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _settings.CurrentMode = normalized;
            SyncMenu();
            UpdateTrayText();
            SaveNow();
            if (_clock != null)
            {
                _clock.CheckRemindersNow();
            }
        }

        private void OnMenuResetPosition(object sender, EventArgs e)
        {
            if (_clock == null || _exiting)
            {
                return;
            }
            _clock.ResetPosition();
            ScheduleSave();
        }

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

        private void OnMenuExit(object sender, EventArgs e)
        {
            ExitApp();
        }

        private void ShowSettings()
        {
            if (_exiting)
            {
                return;
            }

            if (_settingsWindow != null)
            {
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
                if (result == true && !_exiting)
                {
                    CommitDialogSettings(dialog.Result, false);
                }
            }
            finally
            {
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

        private void OnSettingsApplyRequested(object sender, EventArgs e)
        {
            SettingsWindow dialog = _settingsWindow;
            if (dialog == null || _exiting)
            {
                return;
            }
            CommitDialogSettings(dialog.Result, true);
        }

        private void OnSettingsReminderTestRequested(object sender, ReminderPreviewEventArgs e)
        {
            if (_clock == null || _exiting || e == null)
            {
                return;
            }

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

        private void CommitDialogSettings(AppSettings updated, bool reactivateDialog)
        {
            if (updated == null || _settings == null || _clock == null || _exiting)
            {
                return;
            }

            bool previousClickThrough = _settings.ClickThrough;
            bool previousWeatherEnabled = _settings.WeatherEnabled;
            string previousWeatherCity = _settings.WeatherCity;
            bool previousTokenMode = FormatEngine.IsTokenMode(_settings.FormatMode);
            string previousTokenFormat = _settings.TokenTimeFormat;
            string previousWeatherFormat = _settings.WeatherFormat;
            AppSettings next = updated.Clone();
            next.Left = _clock.Left;
            next.Top = _clock.Top;
            next.Normalize();

            _settings = next;
            bool autoStartApplied = AutoStartManager.SetEnabled(_settings.StartWithWindows);
            _clock.ApplySettings(_settings);
            ApplyDisplaySurfaces();
            _clock.CheckRemindersNow();
            ConfigureWeatherTimer();
            SyncMenu();
            UpdateTrayText();
            SaveNow();

            bool weatherChanged = previousWeatherEnabled != _settings.WeatherEnabled
                || !string.Equals(previousWeatherCity, _settings.WeatherCity, StringComparison.Ordinal);
            bool formatChanged = previousTokenMode != FormatEngine.IsTokenMode(_settings.FormatMode)
                || !string.Equals(previousTokenFormat, _settings.TokenTimeFormat, StringComparison.Ordinal)
                || !string.Equals(previousWeatherFormat, _settings.WeatherFormat, StringComparison.Ordinal);
            if (!_settings.WeatherEnabled)
            {
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
                RefreshWeather();
            }

            if (!autoStartApplied && _settings.StartWithWindows)
            {
                ShowAutoStartErrorBalloon();
            }
            if (!previousClickThrough && _settings.ClickThrough)
            {
                ShowClickThroughBalloon();
            }

            if (reactivateDialog && _settingsWindow != null)
            {
                // The overlay is topmost as well; make sure the settings window
                // stays above it immediately after live-applying a change.
                _settingsWindow.Activate();
            }
        }

        private void UpdateTopmostSuspension()
        {
            if (_clock != null)
            {
                _clock.SetTopmostWatchSuspended(_menuOpen || _settingsOpen);
            }
        }

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

        private void UpdateTrayText()
        {
            if (_tray == null || _settings == null)
            {
                return;
            }

            string state;
            string mode = ReminderModes.DisplayName(_settings.CurrentMode);
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

        private void ScheduleSave()
        {
            if (_saveTimer == null || _exiting)
            {
                return;
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void OnSaveTimerTick(object sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveNow();
        }

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

        private void ExitApp()
        {
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            SaveNow();
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
                }
                _settingsWindow = null;
            }

            if (_tray != null)
            {
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
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
                }
                _settingsWindow = null;
            }

            if (_tray != null)
            {
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
