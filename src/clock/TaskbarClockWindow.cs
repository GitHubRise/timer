using System;
using System.IO;
using System.Windows.Threading;

namespace GameClock
{
    /// <summary>
    /// Keeps the task bar clock in sync with the settings by pushing a text
    /// configuration to GameClockTray.dll, which paints the clock inside explorer.
    /// </summary>
    internal sealed class TaskbarClockWindow : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _timer;
        private WeatherInfo _weather;
        private IntPtr _clock;
        private bool _started;
        private bool _disposed;
        private bool _failed;
        private int _pushing;
        private DateTime _lastAttempt;

        public TaskbarClockWindow(AppSettings settings)
        {
            _settings = settings != null ? settings.Clone() : AppSettings.CreateDefault();
            _settings.Normalize();

            _timer = new DispatcherTimer(DispatcherPriority.Normal);
            _timer.Interval = TimeSpan.FromMilliseconds(1000);
            _timer.Tick += OnTimerTick;
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _failed = false;
            Push();
            _timer.Start();
        }

        public void Stop()
        {
            _started = false;
            _timer.Stop();

            if (_clock != IntPtr.Zero)
            {
                TrayClockInjector.Uninstall(_clock);
                TrayClockInjector.ResetProcessCache();
                _clock = IntPtr.Zero;
            }
        }

        public void ApplySettings(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            _settings.TaskbarFontFamilyName = settings.TaskbarFontFamilyName;
            _settings.TaskbarFontSize = settings.TaskbarFontSize;
            _settings.TaskbarColorHex = settings.TaskbarColorHex;
            _settings.TaskbarFormat = settings.TaskbarFormat;
            _settings.TaskbarFormatMode = settings.TaskbarFormatMode;
            _settings.TaskbarDotNetFormat = settings.TaskbarDotNetFormat;
            _settings.TimeZoneId = settings.TimeZoneId;
            _settings.Normalize();
            if (_started)
            {
                Push();
            }
        }

        public void SetWeather(WeatherInfo weather)
        {
            _weather = weather;
            if (_started)
            {
                Push();
            }
        }

        public void Close()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            Push();
        }

        private void Push()
        {
            if (!_started || System.Threading.Interlocked.CompareExchange(ref _pushing, 1, 0) != 0)
            {
                return;
            }

            string text = BuildText();
            string first;
            string second;
            SplitLines(text, out first, out second);

            string fontFace = _settings.TaskbarFontFamilyName;
            int fontHeight = (int)Math.Round(_settings.TaskbarFontSize);
            System.Windows.Media.Color color = _settings.TaskbarColor;

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    PushCore(fontFace, fontHeight, color, first, second);
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _pushing, 0);
                }
            });
        }

        private void PushCore(string fontFace, int fontHeight, System.Windows.Media.Color color, string first, string second)
        {
            IntPtr clock = TrayClockInjector.FindClock();
            if (clock == IntPtr.Zero)
            {
                _clock = IntPtr.Zero;
                return;
            }

            bool newClock = clock != _clock;
            _clock = clock;

            if (!TrayClockInjector.IsInstalled(clock))
            {
                if (_failed && (DateTime.UtcNow - _lastAttempt).TotalSeconds < 10)
                {
                    return;
                }

                string error;
                _lastAttempt = DateTime.UtcNow;
                if (!TrayClockInjector.EnsureInstalled(clock, out error))
                {
                    _failed = true;
                    LogError(error);
                    return;
                }
                _failed = false;
                newClock = true;
            }

            if (newClock)
            {
                TrayClockInjector.ResetProcessCache();
            }

            TrayClockInjector.SendConfig(clock, fontFace, fontHeight, false, false, color, first, second);
        }
        private string BuildText()
        {
            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_settings.TimeZoneId));
            if (FormatEngine.IsTokenMode(_settings.TaskbarFormatMode))
            {
                return FormatEngine.Format(now, _weather, _settings.TaskbarFormat);
            }
            return TimeFormatter.Format(now, _settings.TaskbarDotNetFormat);
        }

        private static void SplitLines(string text, out string first, out string second)
        {
            first = string.Empty;
            second = string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            first = lines.Length > 0 ? lines[0] : string.Empty;
            second = lines.Length > 1 ? lines[1] : string.Empty;
        }

        private static void LogError(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                string folder = SettingsStore.FolderPath;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                string path = Path.Combine(folder, "taskbar.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + "\r\n");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
