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
        private UIElement BuildWeatherTab()
        {
            StackPanel panel = new StackPanel();

            Border weatherCard = UiTheme.MakeCard();
            StackPanel content = new StackPanel();
            content.Children.Add(UiTheme.MakeSectionTitle("天气预报"));
            content.Children.Add(_weatherEnabledCheck);

            TextBlock fontLabel = UiTheme.MakeText("天气字体", 13, UiTheme.TextPrimary, FontWeights.Normal);
            fontLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(fontLabel);
            content.Children.Add(_weatherFontCombo);

            StackPanel styleRow = new StackPanel();
            styleRow.Orientation = Orientation.Horizontal;
            styleRow.Margin = new Thickness(0, 4, 0, 0);
            styleRow.Children.Add(_weatherBoldCheck);
            styleRow.Children.Add(_weatherItalicCheck);
            content.Children.Add(styleRow);

            content.Children.Add(BuildSliderBlock(
                "天气字号",
                _weatherSizeSlider,
                _weatherSizeValueText,
                "天气行单独使用的字号，9 到 60 像素",
                new Thickness(0, 12, 0, 2)));
            content.Children.Add(BuildWeatherColorRow());
            content.Children.Add(BuildWeatherPalette());
            content.Children.Add(_weatherShadowCheck);

            TextBlock refreshLabel = UiTheme.MakeText("自动更新频率", 13, UiTheme.TextPrimary, FontWeights.Normal);
            refreshLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(refreshLabel);
            content.Children.Add(_weatherRefreshCombo);

            TextBlock cityLabel = UiTheme.MakeText("城市", 13, UiTheme.TextPrimary, FontWeights.Normal);
            cityLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(cityLabel);

            Grid cityRow = new Grid();
            cityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cityRow.Children.Add(_weatherCityBox);
            cityRow.Children.Add(_weatherTestButton);
            Grid.SetColumn(_weatherTestButton, 1);
            content.Children.Add(cityRow);

            content.Children.Add(_weatherTestStatus);

            TextBlock cityHint = UiTheme.MakeText(
                "支持中文或英文城市名，例如：北京、上海、Tokyo。保存或应用后会立即刷新。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            cityHint.Margin = new Thickness(0, 6, 0, 0);
            cityHint.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(cityHint);

            TextBlock formatLabel = UiTheme.MakeText("天气格式（可选）", 13, UiTheme.TextPrimary, FontWeights.Normal);
            formatLabel.Margin = new Thickness(0, 14, 0, 6);
            content.Children.Add(formatLabel);
            content.Children.Add(_weatherFormatBox);
            content.Children.Add(_weatherFormatStatus);

            TextBlock formatHelp = UiTheme.MakeText(
                "可用变量：pos 城市  we1~we6 6日天气  tl1~tl6 低温  th1~th6 高温\n" +
                "rtm 实时温度  rhu 湿度  rwd 风向  icn 天气图标  aqi 空气质量  ip 公网 IP\n\n" +
                "示例：pos · we1 · rtm\n" +
                "示例：icn rtm · rhu · rwd\n" +
                "示例：pos · we1 · rtm · 今日 tl1~th1\n" +
                "留空时使用默认格式：城市 · 天气 · 温度 · 今日高低温。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            formatHelp.Margin = new Thickness(0, 8, 0, 0);
            formatHelp.TextWrapping = TextWrapping.Wrap;
            formatHelp.LineHeight = 18;
            content.Children.Add(formatHelp);

            TextBlock apiHint = UiTheme.MakeText(
                "天气数据来自 Open-Meteo 公共 API，按设置的频率自动更新（默认 30 分钟）；无需 API Key。网络不可用时会自动隐藏天气行。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            apiHint.Margin = new Thickness(0, 10, 0, 0);
            apiHint.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(apiHint);

            weatherCard.Child = content;
            panel.Children.Add(weatherCard);

            Border noteCard = UiTheme.MakeCard();
            StackPanel note = new StackPanel();
            note.Children.Add(UiTheme.MakeSectionTitle("显示说明"));
            TextBlock noteText = UiTheme.MakeText(
                "天气会显示在时间下方，例如：\n北京 · 晴 · 11°C · 今日 5°~16°\n\n" +
                "天气的字体、字号、颜色和阴影都可以独立于时间设置。\n" +
                "只发送城市名称和坐标到公共天气服务，不包含任何个人账号信息。",
                11.5,
                UiTheme.TextSecondary,
                FontWeights.Normal);
            noteText.LineHeight = 19;
            note.Children.Add(noteText);
            noteCard.Child = note;
            panel.Children.Add(noteCard);

            return panel;
        }

        private UIElement BuildWeatherColorRow()
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 12, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            TextBlock label = UiTheme.MakeText("天气颜色", 13, UiTheme.TextPrimary, FontWeights.Normal);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(label);
            left.Children.Add(_weatherColorSwatch);
            left.Children.Add(_weatherColorHexText);
            row.Children.Add(left);
            Grid.SetColumn(left, 0);

            Button pick = new Button();
            pick.Content = "选择…";
            pick.Style = UiTheme.SecondaryButtonStyle;
            pick.Padding = new Thickness(13, 6, 13, 6);
            pick.Height = 30;
            pick.Click += OnPickWeatherColor;
            AutomationProperties.SetAutomationId(pick, "PickWeatherColorButton");
            row.Children.Add(pick);
            Grid.SetColumn(pick, 1);

            return row;
        }

        private UIElement BuildWeatherPalette()
        {
            WrapPanel palette = new WrapPanel();
            palette.Margin = new Thickness(0, 10, 0, 2);
            Color[] presetColors = new Color[]
            {
                Color.FromRgb(0xFF, 0x3B, 0x30),
                Color.FromRgb(0xFF, 0x95, 0x00),
                Color.FromRgb(0xFF, 0xD6, 0x0A),
                Color.FromRgb(0x34, 0xC7, 0x59),
                Color.FromRgb(0x00, 0xC7, 0xBE),
                Color.FromRgb(0x0A, 0x84, 0xFF),
                Color.FromRgb(0xBF, 0x5A, 0xF2),
                Color.FromRgb(0x00, 0x00, 0x00),
                Color.FromRgb(0xF2, 0xF2, 0xF7)
            };
            string[] names = new string[]
            {
                "红色（默认）", "橙色", "黄色", "绿色", "青色", "蓝色", "紫色", "黑色", "白色"
            };

            for (int i = 0; i < presetColors.Length; i++)
            {
                Color presetColor = presetColors[i];
                Border swatch = new Border();
                swatch.Width = 24;
                swatch.Height = 24;
                swatch.CornerRadius = new CornerRadius(12);
                swatch.Background = UiTheme.BrushFrom(presetColor);
                swatch.BorderThickness = new Thickness(1);
                swatch.BorderBrush = UiTheme.Frozen(0x55, 0x55, 0x60);
                swatch.Margin = new Thickness(0, 0, 9, 0);
                swatch.Cursor = Cursors.Hand;
                swatch.ToolTip = names[i] + "  " + ColorUtil.ToHex(presetColor);
                swatch.MouseLeftButtonDown += delegate
                {
                    SelectWeatherColor(presetColor);
                };
                palette.Children.Add(swatch);
                _weatherColorPresets.Add(new ColorPreset(presetColor, swatch));
            }
            return palette;
        }

        private void OnWeatherChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherEnabled = _weatherEnabledCheck.IsChecked == true;
            _weatherCityBox.IsEnabled = _working.WeatherEnabled;
            UpdatePreview();
            MarkDirty();
        }

        private void SelectWeatherRefreshOption(int minutes)
        {
            WeatherRefreshOption match = null;
            for (int i = 0; i < _weatherRefreshOptions.Count; i++)
            {
                if (_weatherRefreshOptions[i].Minutes == minutes)
                {
                    match = _weatherRefreshOptions[i];
                    break;
                }
            }
            if (match == null)
            {
                match = _weatherRefreshOptions[2];
            }

            bool wasUpdating = _updating;
            _updating = true;
            try
            {
                _weatherRefreshCombo.SelectedItem = match;
            }
            finally
            {
                _updating = wasUpdating;
            }
        }

        private void OnWeatherCityChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherCity = (_weatherCityBox.Text ?? string.Empty).Trim();
            UpdatePreview();
            MarkDirty();
        }

        private void OnWeatherFontChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            FontChoice choice = _weatherFontCombo.SelectedItem as FontChoice;
            if (choice != null)
            {
                _working.WeatherFontFamilyName = choice.Name;
                UpdatePreview();
                MarkDirty();
            }
        }

        private void OnWeatherSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherFontSize = e.NewValue;
            _weatherSizeValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + " px";
            UpdatePreview();
            MarkDirty();
        }

        private void OnWeatherShadowChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherShowShadow = _weatherShadowCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        private void OnWeatherFontStyleChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherFontBold = _weatherBoldCheck.IsChecked == true;
            _working.WeatherFontItalic = _weatherItalicCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        private void OnWeatherRefreshChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            WeatherRefreshOption option = _weatherRefreshCombo.SelectedItem as WeatherRefreshOption;
            if (option != null)
            {
                _working.WeatherRefreshMinutes = option.Minutes;
                MarkDirty();
            }
        }

        private void OnTestWeatherCity(object sender, RoutedEventArgs e)
        {
            string city = (_weatherCityBox.Text ?? string.Empty).Trim();
            if (city.Length == 0)
            {
                _weatherTestStatus.Text = "请先填写城市名。";
                _weatherTestStatus.Foreground = UiTheme.Accent;
                return;
            }

            int version = ++_weatherTestVersion;
            _weatherTestButton.IsEnabled = false;
            _weatherTestButton.Content = "查询中…";
            _weatherTestStatus.Text = "正在查询 " + city + " 的天气，请稍候…";
            _weatherTestStatus.Foreground = UiTheme.TextMuted;

            WeatherService.FetchAsync(city, delegate(WeatherInfo info)
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (version != _weatherTestVersion)
                        {
                            return;
                        }
                        _weatherTestButton.IsEnabled = true;
                        _weatherTestButton.Content = "测试地名";
                        if (info != null)
                        {
                            _weatherTestStatus.Text = "✓ 查询成功：" + info.DisplayText;
                            _weatherTestStatus.Foreground = UiTheme.Success;
                        }
                        else
                        {
                            _weatherTestStatus.Text = "✕ 查询失败：未找到该地名，或网络暂时不可用。";
                            _weatherTestStatus.Foreground = UiTheme.Accent;
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void OnPickWeatherColor(object sender, RoutedEventArgs e)
        {
            using (WinForms.ColorDialog dialog = new WinForms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                Color current = _working.WeatherColor;
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WinForms.IWin32Window owner = new SimpleWin32Window(handle);
                if (dialog.ShowDialog(owner) == WinForms.DialogResult.OK)
                {
                    SelectWeatherColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
                }
            }
        }

        private void SelectWeatherColor(Color color)
        {
            _working.WeatherColorHex = ColorUtil.ToHex(color);
            UpdateWeatherColorUi();
            UpdatePreview();
            MarkDirty();
        }

        private void UpdateWeatherColorUi()
        {
            Color color = _working.WeatherColor;
            _weatherColorSwatch.Background = UiTheme.BrushFrom(color);
            _weatherColorHexText.Text = ColorUtil.ToHex(color);

            for (int i = 0; i < _weatherColorPresets.Count; i++)
            {
                ColorPreset preset = _weatherColorPresets[i];
                bool selected = string.Equals(
                    ColorUtil.ToHex(preset.Color),
                    _working.WeatherColorHex,
                    StringComparison.OrdinalIgnoreCase);
                preset.Swatch.BorderThickness = new Thickness(selected ? 3 : 1);
                preset.Swatch.BorderBrush = selected
                    ? UiTheme.TextPrimary
                    : UiTheme.Frozen(0x55, 0x55, 0x60);
            }
        }

        private void OnWeatherFormatTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            string text = (_weatherFormatBox.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                _working.WeatherFormat = string.Empty;
                UpdateWeatherFormatStatus();
                UpdatePreview();
                MarkDirty();
                return;
            }

            string normalized;
            string error;
            if (FormatEngine.TryNormalizeTokenFormat(text, false, out normalized, out error))
            {
                _working.WeatherFormat = normalized;
                UpdateWeatherFormatStatus();
                UpdatePreview();
            }
            else
            {
                _weatherFormatStatus.Text = error;
                _weatherFormatStatus.Foreground = UiTheme.Accent;
            }
            MarkDirty();
        }

        private void UpdateWeatherFormatStatus()
        {
            if (string.IsNullOrWhiteSpace(_working.WeatherFormat))
            {
                _weatherFormatStatus.Text = "留空使用默认格式：城市 · 天气 · 温度 · 今日高低温。";
                _weatherFormatStatus.Foreground = UiTheme.TextMuted;
                return;
            }

            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            string example = FormatEngine.Format(now, CreateSampleWeather(), _working.WeatherFormat);
            _weatherFormatStatus.Text = "预览：" + example;
            _weatherFormatStatus.Foreground = UiTheme.TextMuted;
        }

        private sealed class SimpleWin32Window : WinForms.IWin32Window
        {
            private readonly IntPtr _handle;

            public SimpleWin32Window(IntPtr handle)
            {
                _handle = handle;
            }

            public IntPtr Handle
            {
                get { return _handle; }
            }
        }
    }
}
