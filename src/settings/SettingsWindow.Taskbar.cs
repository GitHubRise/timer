using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
                "开启后接管系统任务栏的时间区域并重绘两行文本；关闭后立即恢复系统样式。下方的字体、颜色与格式只作用于任务栏时钟。",
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

            panel.Children.Add(BuildTaskbarFormatSection());

            return panel;
        }

        private UIElement BuildTaskbarFormatSection()
        {
            StackPanel section = new StackPanel();

            Border modeCard = UiTheme.MakeCard();
            StackPanel modePanel = new StackPanel();
            modePanel.Children.Add(UiTheme.MakeSectionTitle("格式语言"));
            TextBlock modeHint = UiTheme.MakeText(
                "“兼容 .NET”使用传统日期格式；“简洁变量”支持农历、干支、星期和天气变量。两种格式分别保存，切换不会丢失。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            modeHint.Margin = new Thickness(0, 0, 0, 8);
            modeHint.TextWrapping = TextWrapping.Wrap;
            modePanel.Children.Add(modeHint);
            StackPanel modeRow = new StackPanel();
            modeRow.Orientation = Orientation.Horizontal;
            modeRow.Children.Add(_taskbarDotNetFormatRadio);
            modeRow.Children.Add(_taskbarTokenFormatRadio);
            modePanel.Children.Add(modeRow);
            modeCard.Child = modePanel;
            section.Children.Add(modeCard);

            Border presetsCard = UiTheme.MakeCard();
            StackPanel presetsPanel = new StackPanel();
            presetsPanel.Children.Add(UiTheme.MakeSectionTitle("常用格式（内置）"));
            TextBlock presetHint = UiTheme.MakeText(
                "直接选择即可，不需要修改格式。示例使用当前日期时间生成。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            presetHint.Margin = new Thickness(0, 0, 0, 8);
            presetsPanel.Children.Add(presetHint);
            presetsPanel.Children.Add(_taskbarFormatCombo);
            presetsCard.Child = presetsPanel;
            section.Children.Add(presetsCard);

            Border customCard = UiTheme.MakeCard();
            StackPanel customPanel = new StackPanel();
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义格式"));
            customPanel.Children.Add(_taskbarFormatHintText);
            customPanel.Children.Add(_taskbarFormatBox);

            Grid actions = new Grid();
            actions.Margin = new Thickness(0, 8, 0, 0);
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button newlineButton = new Button();
            newlineButton.Content = "插入换行";
            newlineButton.Style = UiTheme.SecondaryButtonStyle;
            newlineButton.Padding = new Thickness(12, 5, 12, 5);
            newlineButton.Height = 30;
            newlineButton.HorizontalAlignment = HorizontalAlignment.Left;
            newlineButton.Click += OnTaskbarInsertNewline;
            newlineButton.ToolTip = "格式最多支持一个换行（两行文字）";
            AutomationProperties.SetAutomationId(newlineButton, "TaskbarInsertNewlineButton");
            actions.Children.Add(newlineButton);
            Grid.SetColumn(newlineButton, 0);

            TextBlock hintRight = UiTheme.MakeText("输入即更新，也可以直接保存。", 11, UiTheme.TextMuted, FontWeights.Normal);
            hintRight.VerticalAlignment = VerticalAlignment.Center;
            hintRight.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Children.Add(hintRight);
            Grid.SetColumn(hintRight, 1);

            customPanel.Children.Add(actions);
            customPanel.Children.Add(_taskbarFormatStatus);
            customCard.Child = customPanel;
            section.Children.Add(customCard);

            return section;
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

            HandleTaskbarFormatTextChanged();
        }

        private void OnTaskbarFormatModeChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            bool tokenMode = _taskbarTokenFormatRadio.IsChecked == true;
            string newMode = tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet;
            if (string.Equals(_working.TaskbarFormatMode, newMode, StringComparison.Ordinal))
            {
                UpdateTaskbarFormatHint();
                return;
            }

            if (FormatEngine.IsTokenMode(_working.TaskbarFormatMode))
            {
                _working.TaskbarFormat = _taskbarFormatBox.Text ?? string.Empty;
            }
            else
            {
                _working.TaskbarDotNetFormat = _taskbarFormatBox.Text ?? string.Empty;
            }

            _working.TaskbarFormatMode = newMode;
            _taskbarActivePresets = tokenMode ? _taskbarTokenPresets : _taskbarDotNetPresets;

            _updating = true;
            try
            {
                _taskbarFormatCombo.ItemsSource = _taskbarActivePresets;
                _taskbarFormatBox.Text = tokenMode ? _working.TaskbarFormat : _working.TaskbarDotNetFormat;
            }
            finally
            {
                _updating = false;
            }

            UpdateTaskbarFormatHint();
            HandleTaskbarFormatTextChanged();
            MarkDirty();
        }

        private void UpdateTaskbarFormatHint()
        {
            bool tokenMode = _taskbarTokenFormatRadio.IsChecked == true;
            _taskbarFormatHintText.Text = tokenMode
                ? "支持简洁变量（区分大小写；mm 是月份、ff 是分钟）。输入时实时更新，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。"
                : "支持 .NET 日期格式（MM 是月份、mm 是分钟）。输入时实时更新，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
        }

        private void OnTaskbarFormatPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            TimeFormatPreset preset = _taskbarFormatCombo.SelectedItem as TimeFormatPreset;
            if (preset == null || string.IsNullOrEmpty(preset.Format))
            {
                return;
            }

            _updating = true;
            try
            {
                _taskbarFormatBox.Text = preset.Format;
            }
            finally
            {
                _updating = false;
            }
            HandleTaskbarFormatTextChanged();
        }

        private void HandleTaskbarFormatTextChanged()
        {
            bool tokenMode = _taskbarTokenFormatRadio.IsChecked == true;
            string normalized;
            string error;
            bool valid = tokenMode
                ? FormatEngine.TryNormalizeTokenFormat(_taskbarFormatBox.Text, true, out normalized, out error)
                : TimeFormatter.TryNormalizeFormat(_taskbarFormatBox.Text, out normalized, out error);

            if (valid)
            {
                if (tokenMode)
                {
                    _working.TaskbarFormat = normalized;
                }
                else
                {
                    _working.TaskbarDotNetFormat = normalized;
                }
                _taskbarFormatStatus.Text = "格式有效，保存后应用到任务栏时钟。";
                _taskbarFormatStatus.Foreground = UiTheme.TextMuted;
            }
            else
            {
                _taskbarFormatStatus.Text = error;
                _taskbarFormatStatus.Foreground = UiTheme.Accent;
            }

            UpdateTaskbarFormatComboSelection();
            MarkDirty();
        }

        private void UpdateTaskbarFormatComboSelection()
        {
            TimeFormatPreset match = null;
            for (int i = 0; i < _taskbarActivePresets.Count; i++)
            {
                if (string.Equals(_taskbarActivePresets[i].Format, _taskbarFormatBox.Text, StringComparison.Ordinal))
                {
                    match = _taskbarActivePresets[i];
                    break;
                }
            }

            _updating = true;
            try
            {
                _taskbarFormatCombo.SelectedItem = match != null ? (object)match : _taskbarCustomPreset;
            }
            finally
            {
                _updating = false;
            }
        }

        private void OnTaskbarFormatKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                InsertTaskbarFormatNewline();
            }
            else
            {
                HandleTaskbarFormatTextChanged();
            }
            e.Handled = true;
        }

        private void OnTaskbarInsertNewline(object sender, RoutedEventArgs e)
        {
            InsertTaskbarFormatNewline();
        }

        private void InsertTaskbarFormatNewline()
        {
            if (TimeFormatter.CountNewlines(_taskbarFormatBox.Text) >= 1)
            {
                _taskbarFormatStatus.Text = "最多只能插入一个换行。";
                _taskbarFormatStatus.Foreground = UiTheme.Accent;
                return;
            }

            int caret = _taskbarFormatBox.CaretIndex;
            if (caret < 0)
            {
                caret = _taskbarFormatBox.Text.Length;
            }
            _taskbarFormatBox.Text = _taskbarFormatBox.Text.Insert(caret, "\n");
            _taskbarFormatBox.CaretIndex = caret + 1;
            HandleTaskbarFormatTextChanged();
        }

        private void UpdateTaskbarColorUi()
        {
            _taskbarColorSwatch.Background = new SolidColorBrush(_working.TaskbarColor);
        }
    }
}
