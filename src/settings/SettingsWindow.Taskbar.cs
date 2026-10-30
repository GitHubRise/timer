using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

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
            TextBlock switchHint = SettingsUiHints.Hint(
                "开启后在系统任务栏的时间区域内显示两行时钟。字体与颜色跟随系统任务栏，不移动、不隐藏系统时钟；关闭或退出后系统时钟立即恢复。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            switchHint.TextWrapping = TextWrapping.Wrap;
            switchHint.Margin = new Thickness(24, 3, 0, 0);
            switchPanel.Children.Add(switchHint);
            switchCard.Child = switchPanel;
            panel.Children.Add(switchCard);

            Border lookCard = UiTheme.MakeCard();
            StackPanel lookPanel = new StackPanel();
            lookPanel.Children.Add(UiTheme.MakeSectionTitle("文字大小"));
            TextBlock lookHint = SettingsUiHints.Hint(
                "字号默认使用系统标准大小，也可在此放大；宽度不足时自动收紧字距，加粗会占用更多宽度。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            lookHint.TextWrapping = TextWrapping.Wrap;
            lookHint.Margin = new Thickness(0, 0, 0, 8);
            lookPanel.Children.Add(lookHint);
            lookPanel.Children.Add(_taskbarFontScaleCombo);
            _taskbarBoldCheck.Margin = new Thickness(0, 8, 0, 0);
            lookPanel.Children.Add(_taskbarBoldCheck);
            lookCard.Child = lookPanel;
            panel.Children.Add(lookCard);

            if (SettingsUiHints.ShowHints)
            {
                Border behaviorCard = UiTheme.MakeCard();
                StackPanel behaviorPanel = new StackPanel();
                behaviorPanel.Children.Add(UiTheme.MakeSectionTitle("交互"));
                TextBlock behaviorHint = SettingsUiHints.Hint(
                    "鼠标在任务栏时钟上停留约 0.4 秒，或左键单击，会展开“今日”面板：大号时间、农历、天气、当天提醒与日程。右键打开与托盘相同的菜单。",
                    11,
                    UiTheme.TextMuted,
                    FontWeights.Normal);
                behaviorHint.TextWrapping = TextWrapping.Wrap;
                behaviorPanel.Children.Add(behaviorHint);
                behaviorCard.Child = behaviorPanel;
                panel.Children.Add(behaviorCard);
            }

            panel.Children.Add(BuildTaskbarFormatSection());

            return panel;
        }

        private UIElement BuildTaskbarFormatSection()
        {
            StackPanel section = new StackPanel();

            Border modeCard = UiTheme.MakeCard();
            StackPanel modePanel = new StackPanel();
            modePanel.Children.Add(UiTheme.MakeSectionTitle("格式语言"));
            TextBlock modeHint = SettingsUiHints.Hint(
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
            TextBlock presetHint = SettingsUiHints.Hint(
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

            TextBlock hintRight = SettingsUiHints.Hint("输入即更新，也可以直接保存。", 11, UiTheme.TextMuted, FontWeights.Normal);
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
                SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.TextMuted);
            }
            else
            {
                _taskbarFormatStatus.Text = error;
                SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.Accent);
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
                SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.Accent);
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

        private static ComboBoxItem CreateTaskbarScaleItem(int scale, string label)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Content = label;
            item.Tag = scale;
            return item;
        }
    }
}
