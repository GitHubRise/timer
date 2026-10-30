using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameClock
{
    internal sealed partial class SettingsWindow
    {
        private UIElement BuildTaskbarTab()
        {
            StackPanel panel = new StackPanel();

            Border switchCard = UiTheme.MakeCard();
            StackPanel switchPanel = new StackPanel();
            switchPanel.Children.Add(UiTheme.MakeSectionTitle("任务栏时钟"));
            switchPanel.Children.Add(_taskbarClockCheck);
            TextBlock switchHint = UiTheme.MakeText(
                "开启后覆盖系统任务栏的时间区域，并把系统时钟暂移出任务栏；关闭后立即恢复。下方的字体、颜色与格式只作用于任务栏时钟。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            switchHint.TextWrapping = TextWrapping.Wrap;
            switchHint.Margin = new Thickness(24, 3, 0, 0);
            switchPanel.Children.Add(switchHint);
            switchCard.Child = switchPanel;
            panel.Children.Add(switchCard);

            Border fontCard = UiTheme.MakeCard();
            StackPanel fontPanel = new StackPanel();
            fontPanel.Children.Add(UiTheme.MakeSectionTitle("字体与颜色"));
            TextBlock fontHint = UiTheme.MakeText(
                "任务栏时钟与桌面悬浮窗各自保存字体、字号和颜色，互不影响。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            fontHint.TextWrapping = TextWrapping.Wrap;
            fontHint.Margin = new Thickness(0, 0, 0, 8);
            fontPanel.Children.Add(fontHint);

            TextBlock fontLabel = UiTheme.MakeText("字体", 13, UiTheme.TextPrimary, FontWeights.Normal);
            fontLabel.Margin = new Thickness(0, 0, 0, 6);
            fontPanel.Children.Add(fontLabel);
            fontPanel.Children.Add(_taskbarFontCombo);
            fontPanel.Children.Add(BuildSliderBlock(
                "字号",
                _taskbarSizeSlider,
                _taskbarSizeValueText,
                "7 到 40 像素；任务栏空间不足时会按实际宽度自动缩小",
                new Thickness(0, 14, 0, 2)));
            fontPanel.Children.Add(BuildTaskbarColorRow());
            fontCard.Child = fontPanel;
            panel.Children.Add(fontCard);

            Border formatCard = UiTheme.MakeCard();
            StackPanel formatPanel = new StackPanel();
            formatPanel.Children.Add(UiTheme.MakeSectionTitle("显示格式"));
            TextBlock formatHint = UiTheme.MakeText(
                "简洁变量格式，支持两行。默认第一行日期时间，第二行农历、星期与温度；变量说明见「时间格式」页签。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            formatHint.TextWrapping = TextWrapping.Wrap;
            formatHint.Margin = new Thickness(0, 0, 0, 8);
            formatPanel.Children.Add(formatHint);
            formatPanel.Children.Add(_taskbarFormatBox);
            formatPanel.Children.Add(_taskbarFormatStatus);
            formatCard.Child = formatPanel;
            panel.Children.Add(formatCard);

            return panel;
        }

        private UIElement BuildTaskbarColorRow()
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 14, 0, 2);
            row.VerticalAlignment = VerticalAlignment.Center;

            TextBlock label = UiTheme.MakeText("颜色", 13, UiTheme.TextPrimary, FontWeights.Normal);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(label);
            row.Children.Add(_taskbarColorSwatch);
            row.Children.Add(_taskbarColorBox);

            TextBlock hint = UiTheme.MakeText("#RRGGBB，例如 #FFFFFF 适合深色任务栏。", 11, UiTheme.TextMuted, FontWeights.Normal);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(hint);
            return row;
        }

        private void OnTaskbarFontChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            FontChoice font = _taskbarFontCombo.SelectedItem as FontChoice;
            if (font != null)
            {
                _working.TaskbarFontFamilyName = font.Name;
            }
            MarkDirty();
        }

        private void OnTaskbarSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }

            _taskbarSizeValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + " px";
            _working.TaskbarFontSize = e.NewValue;
            MarkDirty();
        }

        private void OnTaskbarColorChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            Color color = ColorUtil.FromHex(_taskbarColorBox.Text);
            _taskbarColorSwatch.Background = new SolidColorBrush(color);
            MarkDirty();
        }

        private void OnTaskbarFormatChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            string text = _taskbarFormatBox.Text ?? string.Empty;
            string normalized;
            string error;
            if (FormatEngine.TryNormalizeTokenFormat(text, true, out normalized, out error))
            {
                _working.TaskbarFormat = normalized;
                _taskbarFormatStatus.Text = "格式有效，保存后应用到任务栏时钟。";
                _taskbarFormatStatus.Foreground = UiTheme.TextMuted;
            }
            else
            {
                _taskbarFormatStatus.Text = error;
                _taskbarFormatStatus.Foreground = UiTheme.Accent;
            }
            MarkDirty();
        }

        private void UpdateTaskbarColorUi()
        {
            _taskbarColorSwatch.Background = new SolidColorBrush(_working.TaskbarColor);
        }
    }
}
