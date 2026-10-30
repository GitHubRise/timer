using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Media;

namespace GameClock
{
    [DataContract]
    internal sealed class AppSettings
    {
        public const double MinFontSize = 16.0;
        public const double MaxFontSize = 160.0;
        public const double MinOpacity = 0.20;
        public const double MaxOpacity = 1.00;

        private const string DefaultColor = "#FF3B30";
        private const string DefaultFont = "Consolas";
        private const double DefaultFontSize = 52.0;
        private const double DefaultOpacity = 0.92;

        [DataMember(Name = "color")]
        public string ColorHex { get; set; }

        [DataMember(Name = "fontFamily")]
        public string FontFamilyName { get; set; }

        [DataMember(Name = "fontSize")]
        public double FontSize { get; set; }

        [DataMember(Name = "opacity")]
        public double Opacity { get; set; }

        [DataMember(Name = "showShadow")]
        public bool ShowShadow { get; set; }

        [DataMember(Name = "text3d")]
        public bool Text3D { get; set; }

        [DataMember(Name = "timeFormat")]
        public string TimeFormat { get; set; }

        [DataMember(Name = "formatMode")]
        public string FormatMode { get; set; }

        [DataMember(Name = "tokenTimeFormat")]
        public string TokenTimeFormat { get; set; }

        [DataMember(Name = "weatherFormat")]
        public string WeatherFormat { get; set; }

        [DataMember(Name = "timeZoneId")]
        public string TimeZoneId { get; set; }

        [DataMember(Name = "clickThrough")]
        public bool ClickThrough { get; set; }

        [DataMember(Name = "topmost")]
        public bool Topmost { get; set; }

        [DataMember(Name = "startWithWindows")]
        public bool StartWithWindows { get; set; }

        [DataMember(Name = "currentMode")]
        public string CurrentMode { get; set; }

        [DataMember(Name = "weatherEnabled")]
        public bool WeatherEnabled { get; set; }

        [DataMember(Name = "weatherCity")]
        public string WeatherCity { get; set; }

        [DataMember(Name = "weatherFontFamily")]
        public string WeatherFontFamilyName { get; set; }

        [DataMember(Name = "weatherFontSize")]
        public double WeatherFontSize { get; set; }

        [DataMember(Name = "weatherColor")]
        public string WeatherColorHex { get; set; }

        [DataMember(Name = "weatherShadow")]
        public bool WeatherShowShadow { get; set; }

        [DataMember(Name = "weatherRefreshMinutes")]
        public int WeatherRefreshMinutes { get; set; }

        [DataMember(Name = "weatherBold")]
        public bool WeatherFontBold { get; set; }

        [DataMember(Name = "weatherItalic")]
        public bool WeatherFontItalic { get; set; }

        [DataMember(Name = "reminders")]
        public List<Reminder> Reminders { get; set; }

        [DataMember(Name = "schedules")]
        public List<ScheduleItem> Schedules { get; set; }

        [DataMember(Name = "reminderDisplaySeconds")]
        public int ReminderDisplaySeconds { get; set; }

        [DataMember(Name = "reminderHoverClose")]
        public bool ReminderHoverCloseEnabled { get; set; }

        [DataMember(Name = "reminderSoundEnabled")]
        public bool ReminderSoundEnabled { get; set; }

        [DataMember(Name = "reminderBlinkCount")]
        public int ReminderBlinkCount { get; set; }

        [DataMember(Name = "left")]
        public double? Left { get; set; }

        [DataMember(Name = "top")]
        public double? Top { get; set; }

        public AppSettings()
        {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            ColorHex = DefaultColor;
            FontFamilyName = DefaultFont;
            FontSize = DefaultFontSize;
            Opacity = DefaultOpacity;
            ShowShadow = true;
            Text3D = false;
            TimeFormat = TimeFormatter.DefaultFormat;
            FormatMode = FormatEngine.ModeDotNet;
            TokenTimeFormat = FormatEngine.DefaultTokenFormat;
            WeatherFormat = string.Empty;
            TimeZoneId = TimeZoneCatalog.DefaultId;
            ClickThrough = false;
            Topmost = true;
            StartWithWindows = false;
            CurrentMode = ReminderModes.Leisure;
            WeatherEnabled = true;
            WeatherCity = "北京";
            WeatherFontFamilyName = "Microsoft YaHei UI";
            WeatherFontSize = 15.0;
            WeatherColorHex = "#FF3B30";
            WeatherShowShadow = true;
            WeatherRefreshMinutes = 30;
            WeatherFontBold = false;
            WeatherFontItalic = false;
            Reminders = ReminderDefaults.Create();
            Schedules = new List<ScheduleItem>();
            ReminderDisplaySeconds = 12;
            ReminderHoverCloseEnabled = true;
            ReminderSoundEnabled = true;
            ReminderBlinkCount = 3;
            Left = null;
            Top = null;
        }

        public Color TimeColor
        {
            get { return ColorUtil.FromHex(ColorHex); }
        }

        public Color WeatherColor
        {
            get { return ColorUtil.FromHex(WeatherColorHex); }
        }

        public AppSettings Clone()
        {
            AppSettings copy = (AppSettings)MemberwiseClone();
            copy.Reminders = new List<Reminder>();
            if (Reminders != null)
            {
                for (int i = 0; i < Reminders.Count; i++)
                {
                    copy.Reminders.Add(Reminders[i].Clone());
                }
            }
            copy.Schedules = new List<ScheduleItem>();
            if (Schedules != null)
            {
                for (int i = 0; i < Schedules.Count; i++)
                {
                    copy.Schedules.Add(Schedules[i].Clone());
                }
            }
            return copy;
        }

        public void Normalize()
        {
            Color color = ColorUtil.FromHex(ColorHex);
            ColorHex = ColorUtil.ToHex(color);

            if (string.IsNullOrWhiteSpace(FontFamilyName))
            {
                FontFamilyName = DefaultFont;
            }
            FontFamilyName = FontFamilyName.Trim();

            if (double.IsNaN(FontSize) || double.IsInfinity(FontSize))
            {
                FontSize = DefaultFontSize;
            }
            FontSize = Clamp(FontSize, MinFontSize, MaxFontSize);

            if (double.IsNaN(Opacity) || double.IsInfinity(Opacity))
            {
                Opacity = DefaultOpacity;
            }
            Opacity = Clamp(Opacity, MinOpacity, MaxOpacity);

            TimeFormat = TimeFormatter.NormalizeFormat(TimeFormat);
            FormatMode = FormatEngine.NormalizeMode(FormatMode);
            TokenTimeFormat = FormatEngine.NormalizeTokenFormat(TokenTimeFormat);
            WeatherFormat = FormatEngine.NormalizeWeatherFormat(WeatherFormat);
            TimeZoneId = TimeZoneCatalog.Normalize(TimeZoneId);
            CurrentMode = ReminderModes.Normalize(CurrentMode);

            WeatherCity = (WeatherCity ?? string.Empty).Trim();
            if (WeatherCity.Length == 0)
            {
                WeatherCity = "北京";
            }
            if (WeatherCity.Length > 40)
            {
                WeatherCity = WeatherCity.Substring(0, 40);
            }

            if (string.IsNullOrWhiteSpace(WeatherFontFamilyName))
            {
                WeatherFontFamilyName = "Microsoft YaHei UI";
            }
            WeatherFontFamilyName = WeatherFontFamilyName.Trim();

            if (double.IsNaN(WeatherFontSize) || double.IsInfinity(WeatherFontSize))
            {
                WeatherFontSize = 15.0;
            }
            WeatherFontSize = Clamp(WeatherFontSize, 9.0, 60.0);

            Color weatherColor = ColorUtil.FromHex(WeatherColorHex);
            WeatherColorHex = ColorUtil.ToHex(weatherColor);
            WeatherRefreshMinutes = WeatherRefreshOptions.Normalize(WeatherRefreshMinutes);

            if (Reminders == null)
            {
                Reminders = new List<Reminder>();
            }
            for (int i = Reminders.Count - 1; i >= 0; i--)
            {
                Reminder reminder = Reminders[i];
                if (reminder == null || !reminder.Normalize())
                {
                    Reminders.RemoveAt(i);
                }
            }
            while (Reminders.Count > 50)
            {
                Reminders.RemoveAt(Reminders.Count - 1);
            }

            if (Schedules == null)
            {
                Schedules = new List<ScheduleItem>();
            }
            for (int i = Schedules.Count - 1; i >= 0; i--)
            {
                ScheduleItem schedule = Schedules[i];
                if (schedule == null || !schedule.Normalize())
                {
                    Schedules.RemoveAt(i);
                }
            }
            while (Schedules.Count > 100)
            {
                Schedules.RemoveAt(Schedules.Count - 1);
            }

            ReminderDisplaySeconds = ReminderDisplayOptions.Normalize(ReminderDisplaySeconds);
            if (ReminderBlinkCount < 0)
            {
                ReminderBlinkCount = 0;
            }
            if (ReminderBlinkCount > 20)
            {
                ReminderBlinkCount = 20;
            }

            if (Left.HasValue && (double.IsNaN(Left.Value) || double.IsInfinity(Left.Value)))
            {
                Left = null;
            }
            if (Top.HasValue && (double.IsNaN(Top.Value) || double.IsInfinity(Top.Value)))
            {
                Top = null;
            }
            if (!Left.HasValue || !Top.HasValue)
            {
                Left = null;
                Top = null;
            }
        }

        public static AppSettings CreateDefault()
        {
            return new AppSettings();
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }

    internal static class ColorUtil
    {
        private static readonly Color Fallback = Color.FromRgb(255, 59, 48);

        public static Color FromHex(string hex)
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    object value = ColorConverter.ConvertFromString(hex.Trim());
                    if (value is Color)
                    {
                        Color color = (Color)value;
                        return Color.FromRgb(color.R, color.G, color.B);
                    }
                }
                catch (FormatException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
            return Fallback;
        }

        public static string ToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        public static double Luminance(Color color)
        {
            return (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        }
    }

    internal static class SettingsStore
    {
        public static string FolderPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GameClock");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(FolderPath, "settings.json"); }
        }

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return AppSettings.CreateDefault();
                }

                byte[] data = File.ReadAllBytes(FilePath);
                if (data.Length == 0)
                {
                    return AppSettings.CreateDefault();
                }

                using (MemoryStream stream = new MemoryStream(data))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    AppSettings settings = serializer.ReadObject(stream) as AppSettings;
                    if (settings == null)
                    {
                        return AppSettings.CreateDefault();
                    }
                    settings.Normalize();
                    return settings;
                }
            }
            catch (IOException)
            {
                return AppSettings.CreateDefault();
            }
            catch (UnauthorizedAccessException)
            {
                return AppSettings.CreateDefault();
            }
            catch (SerializationException)
            {
                return AppSettings.CreateDefault();
            }
            catch (ArgumentException)
            {
                return AppSettings.CreateDefault();
            }
        }

        public static bool Save(AppSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            settings.Normalize();
            string tempPath = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(FolderPath);
                byte[] data;
                using (MemoryStream stream = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    serializer.WriteObject(stream, settings);
                    data = stream.ToArray();
                }

                File.WriteAllBytes(tempPath, data);

                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(tempPath, FilePath, null);
                    }
                    catch (Exception)
                    {
                        File.Delete(FilePath);
                        File.Move(tempPath, FilePath);
                    }
                }
                else
                {
                    File.Move(tempPath, FilePath);
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (SerializationException)
            {
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
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
}
