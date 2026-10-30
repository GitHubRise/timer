using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace GameClock
{
    internal sealed partial class SettingsWindow
    {
        public event EventHandler ApplyRequested;
        public event EventHandler<ReminderPreviewEventArgs> ReminderTestRequested;

        public AppSettings Result
        {
            get { return _working; }
        }

        private UIElement BuildWindowContent()
        {
            Border shell = new Border();
            shell.Background = UiTheme.WindowBackground;
            shell.BorderBrush = UiTheme.CardBorder;
            shell.BorderThickness = new Thickness(1);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid();
            header.Margin = new Thickness(26, 18, 18, 0);
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleStack = new StackPanel();
            titleStack.Background = Brushes.Transparent;
            titleStack.MouseLeftButtonDown += OnHeaderMouseDown;
            TextBlock title = UiTheme.MakeText("时间悬浮窗设置", 22, UiTheme.TextPrimary, FontWeights.SemiBold);
            TextBlock subtitle = UiTheme.MakeText(
                "颜色、字体、格式和提醒；点击“应用”只保存不关闭。",
                11.5,
                UiTheme.TextSecondary,
                FontWeights.Normal);
            subtitle.Margin = new Thickness(0, 4, 0, 0);
            titleStack.Children.Add(title);
            titleStack.Children.Add(subtitle);
            header.Children.Add(titleStack);
            Grid.SetColumn(titleStack, 0);

            Button closeButton = new Button();
            closeButton.Content = "×";
            closeButton.Style = UiTheme.GhostButtonStyle;
            closeButton.FontFamily = new FontFamily("Segoe UI");
            closeButton.Width = 36;
            closeButton.Height = 32;
            closeButton.FontSize = 20;
            closeButton.Padding = new Thickness(0);
            closeButton.HorizontalAlignment = HorizontalAlignment.Right;
            closeButton.VerticalAlignment = VerticalAlignment.Top;
            closeButton.ToolTip = "关闭（不应用尚未保存的修改）";
            closeButton.Click += OnClose;
            AutomationProperties.SetAutomationId(closeButton, "CloseButton");
            header.Children.Add(closeButton);
            Grid.SetColumn(closeButton, 1);

            root.Children.Add(header);
            Grid.SetRow(header, 0);

            Border previewCard = BuildPreviewCard();
            previewCard.Margin = new Thickness(26, 14, 26, 2);
            root.Children.Add(previewCard);
            Grid.SetRow(previewCard, 1);

            _tabs.Items.Add(CreateTab("外观", BuildAppearanceTab()));
            _tabs.Items.Add(CreateTab("时间格式", BuildFormatTab()));
            _tabs.Items.Add(CreateTab("定时提醒", BuildRemindersTab()));
            _tabs.Items.Add(CreateTab("日程", BuildScheduleTab()));
            _tabs.Items.Add(CreateTab("天气", BuildWeatherTab()));
            _tabs.SelectedIndex = TabAppearance;
            root.Children.Add(_tabs);
            Grid.SetRow(_tabs, 2);

            Border footer = BuildFooter();
            root.Children.Add(footer);
            Grid.SetRow(footer, 3);

            shell.Child = root;
            return shell;
        }

        private void AttachEvents()
        {
            _fontCombo.SelectionChanged += OnFontChanged;
            _sizeSlider.ValueChanged += OnSizeChanged;
            _opacitySlider.ValueChanged += OnOpacityChanged;
            _shadowCheck.Checked += OnEffectChanged;
            _shadowCheck.Unchecked += OnEffectChanged;
            _threeDCheck.Checked += OnEffectChanged;
            _threeDCheck.Unchecked += OnEffectChanged;
            _clickThroughCheck.Checked += OnBehaviorChanged;
            _clickThroughCheck.Unchecked += OnBehaviorChanged;
            _topmostCheck.Checked += OnBehaviorChanged;
            _topmostCheck.Unchecked += OnBehaviorChanged;
            _startWithWindowsCheck.Checked += OnBehaviorChanged;
            _startWithWindowsCheck.Unchecked += OnBehaviorChanged;
            _formatCombo.SelectionChanged += OnFormatPresetChanged;
            _formatBox.TextChanged += OnFormatTextChanged;
            _formatBox.PreviewKeyDown += OnFormatKeyDown;
            _dotNetFormatRadio.Checked += OnFormatModeChanged;
            _tokenFormatRadio.Checked += OnFormatModeChanged;
            _formatHelpSearchBox.TextChanged += OnFormatHelpSearchChanged;
            _weatherFormatBox.TextChanged += OnWeatherFormatTextChanged;
            _timeZoneCombo.SelectionChanged += OnTimeZoneChanged;
            _reminderMessageBox.KeyDown += OnReminderMessageKeyDown;
            _workModeRadio.Checked += OnCurrentStateChanged;
            _leisureModeRadio.Checked += OnCurrentStateChanged;
            _reminderDurationCombo.SelectionChanged += OnReminderDurationChanged;
            _reminderHoverCheck.Checked += OnReminderHoverChanged;
            _reminderHoverCheck.Unchecked += OnReminderHoverChanged;
            _reminderBlinkSlider.ValueChanged += OnReminderBlinkChanged;
            _reminderSoundCheck.Checked += OnReminderSoundChanged;
            _reminderSoundCheck.Unchecked += OnReminderSoundChanged;
            _weatherEnabledCheck.Checked += OnWeatherChanged;
            _weatherEnabledCheck.Unchecked += OnWeatherChanged;
            _weatherCityBox.TextChanged += OnWeatherCityChanged;
            _weatherFontCombo.SelectionChanged += OnWeatherFontChanged;
            _weatherSizeSlider.ValueChanged += OnWeatherSizeChanged;
            _weatherShadowCheck.Checked += OnWeatherShadowChanged;
            _weatherShadowCheck.Unchecked += OnWeatherShadowChanged;
            _weatherBoldCheck.Checked += OnWeatherFontStyleChanged;
            _weatherBoldCheck.Unchecked += OnWeatherFontStyleChanged;
            _weatherItalicCheck.Checked += OnWeatherFontStyleChanged;
            _weatherItalicCheck.Unchecked += OnWeatherFontStyleChanged;
            _weatherRefreshCombo.SelectionChanged += OnWeatherRefreshChanged;
            PreviewKeyDown += OnWindowPreviewKeyDown;
        }

        private void LoadControlsFromWorking()
        {
            FontChoice choice = FontCatalog.FindChoice(_fontChoices, _working.FontFamilyName);
            if (choice == null)
            {
                choice = FontCatalog.FindChoice(_fontChoices, FontCatalog.DefaultFont);
            }
            _fontCombo.SelectedItem = choice;

            _sizeSlider.Value = _working.FontSize;
            _opacitySlider.Value = Math.Round(_working.Opacity * 100.0);
            _sizeValueText.Text = ((int)Math.Round(_working.FontSize)).ToString(CultureInfo.InvariantCulture) + " px";
            _opacityValueText.Text = ((int)Math.Round(_working.Opacity * 100.0)).ToString(CultureInfo.InvariantCulture) + "%";

            _shadowCheck.IsChecked = _working.ShowShadow;
            _threeDCheck.IsChecked = _working.Text3D;
            _clickThroughCheck.IsChecked = _working.ClickThrough;
            _topmostCheck.IsChecked = _working.Topmost;
            _startWithWindowsCheck.IsChecked = _working.StartWithWindows;

            string mode = ReminderModes.Normalize(_working.CurrentMode);
            _workModeRadio.IsChecked = mode == ReminderModes.Work;
            _leisureModeRadio.IsChecked = mode == ReminderModes.Leisure;
            SelectReminderDurationOption(_working.ReminderDisplaySeconds);
            _reminderHoverCheck.IsChecked = _working.ReminderHoverCloseEnabled;
            _reminderBlinkSlider.Value = _working.ReminderBlinkCount;
            _reminderBlinkValueText.Text = _working.ReminderBlinkCount + " 次";
            _reminderSoundCheck.IsChecked = _working.ReminderSoundEnabled;

            _weatherEnabledCheck.IsChecked = _working.WeatherEnabled;
            _weatherCityBox.Text = _working.WeatherCity;
            _weatherCityBox.IsEnabled = _working.WeatherEnabled;

            FontChoice weatherFont = FontCatalog.FindChoice(_fontChoices, _working.WeatherFontFamilyName);
            if (weatherFont == null)
            {
                weatherFont = FontCatalog.FindChoice(_fontChoices, "Microsoft YaHei UI");
            }
            if (weatherFont == null)
            {
                weatherFont = FontCatalog.FindChoice(_fontChoices, FontCatalog.DefaultFont);
            }
            _weatherFontCombo.SelectedItem = weatherFont;
            _weatherSizeSlider.Value = _working.WeatherFontSize;
            _weatherSizeValueText.Text = ((int)Math.Round(_working.WeatherFontSize)).ToString(CultureInfo.InvariantCulture) + " px";
            _weatherShadowCheck.IsChecked = _working.WeatherShowShadow;
            _weatherBoldCheck.IsChecked = _working.WeatherFontBold;
            _weatherItalicCheck.IsChecked = _working.WeatherFontItalic;
            UpdateWeatherColorUi();
            SelectWeatherRefreshOption(_working.WeatherRefreshMinutes);

            bool tokenMode = FormatEngine.IsTokenMode(_working.FormatMode);
            _dotNetFormatRadio.IsChecked = !tokenMode;
            _tokenFormatRadio.IsChecked = tokenMode;
            _formatPresets = tokenMode ? _tokenFormatPresets : _dotNetFormatPresets;
            _formatCombo.ItemsSource = _formatPresets;
            _formatBox.Text = tokenMode ? _working.TokenTimeFormat : _working.TimeFormat;
            _weatherFormatBox.Text = _working.WeatherFormat;
            UpdateFormatHelp();
            UpdateWeatherFormatStatus();
            SelectTimeZoneChoice(_working.TimeZoneId);
            UpdateFormatComboSelection();
            _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            _formatStatus.Foreground = UiTheme.TextMuted;

            UpdateColorUi();
            RebuildReminderRows();
            RebuildScheduleRows();
            UpdatePreview();
        }

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void UpdatePreview()
        {
            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            WeatherInfo sample = CreateSampleWeather();
            string display = FormatEngine.IsTokenMode(_working.FormatMode)
                ? FormatEngine.Format(now, sample, _working.TokenTimeFormat)
                : TimeFormatter.Format(now, _working.TimeFormat);
            _previewText.Text = display;
            _previewText.ApplyStyle(
                FontCatalog.GetFontFamily(_working.FontFamilyName),
                _working.FontSize,
                FontWeights.SemiBold,
                _working.TimeColor,
                _working.ShowShadow,
                _working.Text3D);
            _previewText.Opacity = _working.Opacity;

            bool inlineWeather = FormatEngine.IsTokenMode(_working.FormatMode)
                && FormatEngine.ContainsWeatherToken(_working.TokenTimeFormat);
            bool showWeather = _working.WeatherEnabled && !inlineWeather;
            _previewWeatherText.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
            if (!showWeather)
            {
                return;
            }

            string city = string.IsNullOrWhiteSpace(_working.WeatherCity) ? "北京" : _working.WeatherCity;
            Color weatherColor = _working.WeatherColor;
            _previewWeatherText.Text = string.IsNullOrWhiteSpace(_working.WeatherFormat)
                ? city + " · 晴 · 12°C · 今日 9°~17°"
                : FormatEngine.Format(now, sample, _working.WeatherFormat);
            _previewWeatherText.FontFamily = FontCatalog.GetFontFamily(_working.WeatherFontFamilyName);
            _previewWeatherText.FontSize = _working.WeatherFontSize;
            _previewWeatherText.FontWeight = _working.WeatherFontBold ? FontWeights.Bold : FontWeights.Normal;
            _previewWeatherText.FontStyle = _working.WeatherFontItalic ? FontStyles.Italic : FontStyles.Normal;
            _previewWeatherText.Foreground = UiTheme.BrushFrom(
                Color.FromArgb(232, weatherColor.R, weatherColor.G, weatherColor.B));
            _previewWeatherText.Margin = new Thickness(0, Math.Max(2, _working.WeatherFontSize * 0.20), 0, 0);
            _previewWeatherText.Opacity = _working.Opacity;
            if (_working.WeatherShowShadow)
            {
                _previewWeatherText.Effect = new DropShadowEffect
                {
                    Color = ColorUtil.Luminance(weatherColor) < 0.22 ? Colors.White : Colors.Black,
                    Direction = 315,
                    ShadowDepth = Math.Max(1.0, _working.WeatherFontSize * 0.10),
                    BlurRadius = Math.Max(2.0, _working.WeatherFontSize * 0.20),
                    Opacity = 0.78
                };
            }
            else
            {
                _previewWeatherText.Effect = null;
            }
        }

        private WeatherInfo CreateSampleWeather()
        {
            WeatherInfo sample = new WeatherInfo();
            sample.City = string.IsNullOrWhiteSpace(_working.WeatherCity) ? "北京" : _working.WeatherCity.Trim();
            sample.Condition = "晴";
            sample.WeatherCode = 0;
            sample.Temperature = 12;
            sample.Min = 9;
            sample.Max = 17;
            sample.Humidity = 48;
            sample.WindDirection = "北风";
            sample.AqiValue = 49;
            sample.AqiLevel = "优";
            sample.PublicIp = "221.222.190.200";

            string[] conditions = new string[] { "晴", "晴", "多云", "阴", "小雨", "晴" };
            int[] codes = new int[] { 0, 0, 2, 3, 61, 0 };
            double[] mins = new double[] { 9, 6, 8, 10, 7, 9 };
            double[] maxs = new double[] { 17, 19, 20, 18, 15, 21 };
            for (int i = 0; i < conditions.Length; i++)
            {
                DailyForecast day = new DailyForecast();
                day.Code = codes[i];
                day.Condition = conditions[i];
                day.Min = mins[i];
                day.Max = maxs[i];
                sample.Days.Add(day);
            }
            return sample;
        }

        private bool CommitAll(out string error)
        {
            error = null;

            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string normalizedFormat;
            string formatError;
            bool formatValid = tokenMode
                ? FormatEngine.TryNormalizeTokenFormat(_formatBox.Text, true, out normalizedFormat, out formatError)
                : TimeFormatter.TryNormalizeFormat(_formatBox.Text, out normalizedFormat, out formatError);
            if (!formatValid)
            {
                _formatStatus.Text = formatError;
                _formatStatus.Foreground = UiTheme.Accent;
                _tabs.SelectedIndex = TabFormat;
                _formatStatus.BringIntoView();
                _formatBox.Focus();
                error = formatError;
                return false;
            }

            if (tokenMode)
            {
                _working.TokenTimeFormat = normalizedFormat;
                _working.FormatMode = FormatEngine.ModeToken;
            }
            else
            {
                _working.TimeFormat = normalizedFormat;
                _working.FormatMode = FormatEngine.ModeDotNet;
            }

            string weatherFormatText = (_weatherFormatBox.Text ?? string.Empty).Trim();
            if (weatherFormatText.Length > 0)
            {
                string normalizedWeatherFormat;
                string weatherFormatError;
                if (!FormatEngine.TryNormalizeTokenFormat(
                    weatherFormatText,
                    false,
                    out normalizedWeatherFormat,
                    out weatherFormatError))
                {
                    _weatherFormatStatus.Text = weatherFormatError;
                    _weatherFormatStatus.Foreground = UiTheme.Accent;
                    _weatherFormatStatus.BringIntoView();
                    _tabs.SelectedIndex = TabWeather;
                    _weatherFormatBox.Focus();
                    error = weatherFormatError;
                    return false;
                }
                _working.WeatherFormat = normalizedWeatherFormat;
            }
            else
            {
                _working.WeatherFormat = string.Empty;
            }

            TimeZoneChoice timeZoneChoice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            if (timeZoneChoice != null)
            {
                _working.TimeZoneId = timeZoneChoice.Id;
            }
            _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            _formatStatus.Foreground = UiTheme.TextMuted;

            FontChoice font = _fontCombo.SelectedItem as FontChoice;
            if (font != null)
            {
                _working.FontFamilyName = font.Name;
            }
            _working.FontSize = _sizeSlider.Value;
            _working.Opacity = _opacitySlider.Value / 100.0;
            _working.ShowShadow = _shadowCheck.IsChecked == true;
            _working.Text3D = _threeDCheck.IsChecked == true;
            _working.ClickThrough = _clickThroughCheck.IsChecked == true;
            _working.Topmost = _topmostCheck.IsChecked == true;
            _working.StartWithWindows = _startWithWindowsCheck.IsChecked == true;
            _working.CurrentMode = _workModeRadio.IsChecked == true
                ? ReminderModes.Work
                : ReminderModes.Leisure;
            ReminderDisplayOption displayOption = _reminderDurationCombo.SelectedItem as ReminderDisplayOption;
            if (displayOption != null)
            {
                _working.ReminderDisplaySeconds = displayOption.Seconds;
            }
            _working.ReminderHoverCloseEnabled = _reminderHoverCheck.IsChecked == true;
            _working.ReminderBlinkCount = (int)Math.Round(_reminderBlinkSlider.Value);
            _working.ReminderSoundEnabled = _reminderSoundCheck.IsChecked == true;
            _working.WeatherEnabled = _weatherEnabledCheck.IsChecked == true;
            _working.WeatherCity = (_weatherCityBox.Text ?? string.Empty).Trim();
            FontChoice weatherFont = _weatherFontCombo.SelectedItem as FontChoice;
            if (weatherFont != null)
            {
                _working.WeatherFontFamilyName = weatherFont.Name;
            }
            _working.WeatherFontSize = _weatherSizeSlider.Value;
            _working.WeatherShowShadow = _weatherShadowCheck.IsChecked == true;
            _working.WeatherFontBold = _weatherBoldCheck.IsChecked == true;
            _working.WeatherFontItalic = _weatherItalicCheck.IsChecked == true;
            WeatherRefreshOption refreshOption = _weatherRefreshCombo.SelectedItem as WeatherRefreshOption;
            if (refreshOption != null)
            {
                _working.WeatherRefreshMinutes = refreshOption.Minutes;
            }

            SyncReminderRows();
            for (int i = 0; i < _reminderRows.Count; i++)
            {
                ReminderRow row = _reminderRows[i];
                row.TimeBox.BorderBrush = UiTheme.FieldBorder;
                row.MessageBox.BorderBrush = UiTheme.FieldBorder;

                string normalizedTime;
                if (!TimeFormatter.TryNormalizeTime(row.Reminder.Time, out normalizedTime))
                {
                    row.TimeBox.BorderBrush = UiTheme.Accent;
                    SetReminderStatus("有一条提醒的时间格式不正确，请使用 HH:mm。", false);
                    _tabs.SelectedIndex = TabReminders;
                    row.TimeBox.Focus();
                    error = "提醒时间格式不正确。";
                    return false;
                }

                string message = (row.Reminder.Message ?? string.Empty).Trim();
                if (message.Length == 0)
                {
                    row.MessageBox.BorderBrush = UiTheme.Accent;
                    SetReminderStatus("有一条提醒没有填写内容。", false);
                    _tabs.SelectedIndex = TabReminders;
                    row.MessageBox.Focus();
                    error = "提醒内容不能为空。";
                    return false;
                }
                if (message.Length > 120)
                {
                    message = message.Substring(0, 120);
                }

                row.Reminder.Time = normalizedTime;
                row.Reminder.Message = message;
                row.TimeBox.Text = normalizedTime;
                row.MessageBox.Text = message;
            }

            _working.Normalize();
            return true;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            string error;
            if (!CommitAll(out error))
            {
                return;
            }

            if (ApplyRequested != null)
            {
                ApplyRequested(this, EventArgs.Empty);
            }
            _footerStatus.Text = "已应用，未关闭";
            _footerStatus.Foreground = UiTheme.Success;
        }

        private void OnSaveAndExit(object sender, RoutedEventArgs e)
        {
            string error;
            if (!CommitAll(out error))
            {
                return;
            }
            DialogResult = true;
        }

        private void OnAbout(object sender, RoutedEventArgs e)
        {
            AboutWindow about = new AboutWindow();
            about.Owner = this;
            about.ShowDialog();
        }

        private void OnResetDefaults(object sender, RoutedEventArgs e)
        {
            AppSettings defaults = AppSettings.CreateDefault();
            _working.ColorHex = defaults.ColorHex;
            _working.FontFamilyName = defaults.FontFamilyName;
            _working.FontSize = defaults.FontSize;
            _working.Opacity = defaults.Opacity;
            _working.ShowShadow = defaults.ShowShadow;
            _working.Text3D = defaults.Text3D;
            _working.TimeFormat = defaults.TimeFormat;
            _working.TimeZoneId = defaults.TimeZoneId;
            _working.ClickThrough = defaults.ClickThrough;
            _working.Topmost = defaults.Topmost;
            _working.StartWithWindows = defaults.StartWithWindows;
            _working.WeatherEnabled = defaults.WeatherEnabled;
            _working.WeatherCity = defaults.WeatherCity;
            _working.WeatherFontFamilyName = defaults.WeatherFontFamilyName;
            _working.WeatherFontSize = defaults.WeatherFontSize;
            _working.WeatherColorHex = defaults.WeatherColorHex;
            _working.WeatherShowShadow = defaults.WeatherShowShadow;
            _working.WeatherRefreshMinutes = defaults.WeatherRefreshMinutes;
            _working.WeatherFontBold = defaults.WeatherFontBold;
            _working.WeatherFontItalic = defaults.WeatherFontItalic;
            _working.ReminderDisplaySeconds = defaults.ReminderDisplaySeconds;
            _working.ReminderHoverCloseEnabled = defaults.ReminderHoverCloseEnabled;
            _working.ReminderSoundEnabled = defaults.ReminderSoundEnabled;
            _working.ReminderBlinkCount = defaults.ReminderBlinkCount;

            _updating = true;
            try
            {
                LoadControlsFromWorking();
            }
            finally
            {
                _updating = false;
            }
            _footerStatus.Text = "已恢复默认外观（提醒和状态保留）";
            _footerStatus.Foreground = UiTheme.TextMuted;
        }

        private void MarkDirty()
        {
            _footerStatus.Text = "有未应用的修改";
            _footerStatus.Foreground = UiTheme.TextMuted;
        }

        private sealed class ColorPreset
        {
            public ColorPreset(Color color, Border swatch)
            {
                Color = color;
                Swatch = swatch;
            }

            public Color Color { get; private set; }
            public Border Swatch { get; private set; }
        }
    }
}
