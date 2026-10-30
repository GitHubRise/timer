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
        private UIElement BuildRemindersTab()
        {
            StackPanel panel = new StackPanel();

            Border stateCard = UiTheme.MakeCard();
            StackPanel stateContent = new StackPanel();
            stateContent.Children.Add(UiTheme.MakeSectionTitle("当前状态"));
            TextBlock stateHint = UiTheme.MakeText(
                "标记你现在的状态。提醒会自动过滤：全部状态的提醒始终生效，仅工作和仅娱乐只会在对应状态触发。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            stateHint.Margin = new Thickness(0, 0, 0, 8);
            stateHint.TextWrapping = TextWrapping.Wrap;
            stateContent.Children.Add(stateHint);
            StackPanel stateRow = new StackPanel();
            stateRow.Orientation = Orientation.Horizontal;
            stateRow.Children.Add(_workModeRadio);
            stateRow.Children.Add(_leisureModeRadio);
            stateContent.Children.Add(stateRow);
            stateCard.Child = stateContent;
            panel.Children.Add(stateCard);

            Border displayCard = UiTheme.MakeCard();
            StackPanel displayContent = new StackPanel();
            displayContent.Children.Add(UiTheme.MakeSectionTitle("提醒显示"));
            TextBlock durationLabel = UiTheme.MakeText("显示时长", 13, UiTheme.TextPrimary, FontWeights.Normal);
            durationLabel.Margin = new Thickness(0, 0, 0, 6);
            displayContent.Children.Add(durationLabel);
            displayContent.Children.Add(_reminderDurationCombo);
            displayContent.Children.Add(BuildSliderBlock(
                "闪烁次数（0 = 不闪烁）",
                _reminderBlinkSlider,
                _reminderBlinkValueText,
                "提醒卡片进入时闪烁指定次数，便于在游戏画面中注意到；0 表示不闪烁",
                new Thickness(0, 10, 0, 2)));
            _reminderHoverCheck.Margin = new Thickness(0, 8, 0, 4);
            displayContent.Children.Add(_reminderHoverCheck);
            _reminderSoundCheck.Margin = new Thickness(0, 0, 0, 4);
            displayContent.Children.Add(_reminderSoundCheck);
            TextBlock displayHint = UiTheme.MakeText(
                "时长到点自动关闭；开启悬停检测后，鼠标停留在提醒卡片上满 3 秒也会关闭。闪烁和铃声用于提醒，可在上方调整。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            displayHint.Margin = new Thickness(0, 2, 0, 0);
            displayHint.TextWrapping = TextWrapping.Wrap;
            displayContent.Children.Add(displayHint);
            displayCard.Child = displayContent;
            panel.Children.Add(displayCard);

            Border presetCard = UiTheme.MakeCard();
            StackPanel presetPanel = new StackPanel();
            presetPanel.Children.Add(UiTheme.MakeSectionTitle("内置提醒（点击直接添加）"));
            WrapPanel presetWrap = new WrapPanel();
            presetWrap.Margin = new Thickness(0, 0, 0, 2);
            List<ReminderPreset> presets = ReminderPresets.Create();
            for (int i = 0; i < presets.Count; i++)
            {
                ReminderPreset preset = presets[i];
                Button button = new Button();
                button.Content = preset.ButtonText;
                button.Style = UiTheme.SecondaryButtonStyle;
                button.Padding = new Thickness(11, 6, 11, 6);
                button.Margin = new Thickness(0, 0, 8, 8);
                button.FontSize = 12;
                button.ToolTip = "[" + ReminderModes.DisplayName(preset.Mode) + "] " + preset.Message;
                button.Tag = preset;
                button.Click += OnAddPresetReminder;
                AutomationProperties.SetAutomationId(button, "PresetReminder_" + preset.Time.Replace(":", ""));
                presetWrap.Children.Add(button);
            }
            presetPanel.Children.Add(presetWrap);
            TextBlock presetHint = UiTheme.MakeText(
                "添加后可以在下方修改时间、适用状态或内容，也可以停用或删除。提醒每天触发一次。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            presetHint.Margin = new Thickness(0, 2, 0, 0);
            presetHint.TextWrapping = TextWrapping.Wrap;
            presetPanel.Children.Add(presetHint);
            presetCard.Child = presetPanel;
            panel.Children.Add(presetCard);

            Border customCard = UiTheme.MakeCard();
            customCard.Name = "CustomReminderCard";
            StackPanel customPanel = new StackPanel();
            customPanel.Name = "CustomReminderPanel";
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义提醒"));
            Grid form = new Grid();
            form.Name = "CustomReminderForm";
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel timePanel = new StackPanel();
            TextBlock timeLabel = UiTheme.MakeText("时间 HH:mm", 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            timeLabel.Margin = new Thickness(0, 0, 0, 4);
            timePanel.Children.Add(timeLabel);
            timePanel.Children.Add(_reminderTimeBox);
            form.Children.Add(timePanel);
            Grid.SetColumn(timePanel, 0);

            StackPanel modePanel = new StackPanel();
            TextBlock modeLabel = UiTheme.MakeText("适用状态", 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            modeLabel.Margin = new Thickness(0, 0, 0, 4);
            modePanel.Children.Add(modeLabel);
            modePanel.Children.Add(_newReminderModeCombo);
            modePanel.Margin = new Thickness(8, 0, 0, 0);
            form.Children.Add(modePanel);
            Grid.SetColumn(modePanel, 1);

            StackPanel messagePanel = new StackPanel();
            TextBlock messageLabel = UiTheme.MakeText("提醒内容", 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            messageLabel.Margin = new Thickness(0, 0, 0, 4);
            messagePanel.Children.Add(messageLabel);
            messagePanel.Children.Add(_reminderMessageBox);
            messagePanel.Margin = new Thickness(8, 0, 0, 0);
            form.Children.Add(messagePanel);
            Grid.SetColumn(messagePanel, 2);

            StackPanel formActions = new StackPanel();
            formActions.Orientation = Orientation.Horizontal;
            formActions.VerticalAlignment = VerticalAlignment.Bottom;
            formActions.Margin = new Thickness(8, 0, 0, 0);

            Button testButton = new Button();
            testButton.Content = "测试";
            testButton.Style = UiTheme.SecondaryButtonStyle;
            testButton.Height = 32;
            testButton.MinWidth = 54;
            testButton.Padding = new Thickness(8, 5, 8, 5);
            testButton.Margin = new Thickness(0, 0, 6, 0);
            testButton.ToolTip = "按当前时间和内容在悬浮窗上预览一次提醒，不会添加";
            testButton.Click += OnTestCustomReminder;
            AutomationProperties.SetAutomationId(testButton, "TestReminderButton");
            formActions.Children.Add(testButton);

            Button addButton = new Button();
            addButton.Name = "AddReminderButtonControl";
            addButton.Content = "添加";
            addButton.Style = UiTheme.PrimaryButtonStyle;
            addButton.Height = 32;
            addButton.MinWidth = 58;
            addButton.Padding = new Thickness(8, 5, 8, 5);
            addButton.Click += OnAddCustomReminder;
            AutomationProperties.SetAutomationId(addButton, "AddReminderButton");
            formActions.Children.Add(addButton);

            form.Children.Add(formActions);
            Grid.SetColumn(formActions, 3);

            customPanel.Children.Add(form);
            customPanel.Children.Add(_reminderStatus);
            customCard.Child = customPanel;
            panel.Children.Add(customCard);

            Border listCard = UiTheme.MakeCard();
            StackPanel listPanel = new StackPanel();
            Grid listHeader = new Grid();
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock listLabel = UiTheme.MakeText("已添加提醒", 12, UiTheme.TextSecondary, FontWeights.SemiBold);
            listLabel.Margin = new Thickness(0, 0, 0, 8);
            listHeader.Children.Add(listLabel);
            listHeader.Children.Add(_reminderCountText);
            Grid.SetColumn(_reminderCountText, 1);
            listPanel.Children.Add(listHeader);
            listPanel.Children.Add(_reminderScroll);
            listPanel.Children.Add(_reminderEmptyText);
            listCard.Child = listPanel;
            panel.Children.Add(listCard);

            return panel;
        }

        private void OnReminderDurationChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            ReminderDisplayOption option = _reminderDurationCombo.SelectedItem as ReminderDisplayOption;
            if (option != null)
            {
                _working.ReminderDisplaySeconds = option.Seconds;
                MarkDirty();
            }
        }

        private void OnReminderHoverChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.ReminderHoverCloseEnabled = _reminderHoverCheck.IsChecked == true;
            MarkDirty();
        }

        private void OnReminderBlinkChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            int count = (int)Math.Round(e.NewValue);
            _working.ReminderBlinkCount = count;
            _reminderBlinkValueText.Text = count + " 次";
            MarkDirty();
        }

        private void OnReminderSoundChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.ReminderSoundEnabled = _reminderSoundCheck.IsChecked == true;
            MarkDirty();
        }

        private void SelectReminderDurationOption(int seconds)
        {
            ReminderDisplayOption match = null;
            for (int i = 0; i < _reminderDisplayOptions.Count; i++)
            {
                if (_reminderDisplayOptions[i].Seconds == seconds)
                {
                    match = _reminderDisplayOptions[i];
                    break;
                }
            }
            if (match == null)
            {
                match = _reminderDisplayOptions[3];
            }

            bool wasUpdating = _updating;
            _updating = true;
            try
            {
                _reminderDurationCombo.SelectedItem = match;
            }
            finally
            {
                _updating = wasUpdating;
            }
        }

        private void OnAddPresetReminder(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            ReminderPreset preset = button != null ? button.Tag as ReminderPreset : null;
            if (preset == null)
            {
                return;
            }

            SyncReminderRows();
            if (ContainsReminder(preset.Time, preset.Message, preset.Mode))
            {
                SetReminderStatus("这个内置提醒已经在列表里了。", false);
                return;
            }

            Reminder reminder = new Reminder();
            reminder.Time = preset.Time;
            reminder.Message = preset.Message;
            reminder.Mode = preset.Mode;
            reminder.Enabled = true;
            _working.Reminders.Add(reminder);
            RebuildReminderRows();
            SetReminderStatus("已添加：" + preset.Time + "  " + preset.Message, true);
            MarkDirty();
        }

        private void OnAddCustomReminder(object sender, RoutedEventArgs e)
        {
            AddCustomReminder();
        }

        private void OnTestCustomReminder(object sender, RoutedEventArgs e)
        {
            string time;
            if (!TimeFormatter.TryNormalizeTime(_reminderTimeBox.Text, out time))
            {
                SetReminderStatus("时间格式应为 HH:mm，例如 23:00。", false);
                _reminderTimeBox.Focus();
                return;
            }

            string message = (_reminderMessageBox.Text ?? string.Empty).Trim();
            if (message.Length == 0)
            {
                SetReminderStatus("请填写提醒内容后再测试。", false);
                _reminderMessageBox.Focus();
                return;
            }

            RaiseReminderTest(time, message);
            SetReminderStatus("已发送测试提醒：" + time + "  " + message, true);
        }

        private void RaiseReminderTest(string time, string message)
        {
            if (ReminderTestRequested != null)
            {
                ReminderTestRequested(this, new ReminderPreviewEventArgs(time, message));
            }
        }

        private void OnReminderMessageKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                e.Handled = true;
                AddCustomReminder();
            }
        }

        private void AddCustomReminder()
        {
            string time;
            if (!TimeFormatter.TryNormalizeTime(_reminderTimeBox.Text, out time))
            {
                SetReminderStatus("时间格式应为 HH:mm，例如 23:00。", false);
                _reminderTimeBox.Focus();
                return;
            }

            string message = (_reminderMessageBox.Text ?? string.Empty).Trim();
            if (message.Length == 0)
            {
                SetReminderStatus("请填写提醒内容。", false);
                _reminderMessageBox.Focus();
                return;
            }
            if (message.Length > 120)
            {
                message = message.Substring(0, 120);
            }

            SyncReminderRows();
            ReminderModeOption modeOption = _newReminderModeCombo.SelectedItem as ReminderModeOption;
            string mode = modeOption != null ? modeOption.Value : ReminderModes.Both;
            if (ContainsReminder(time, message, mode))
            {
                SetReminderStatus("相同时间、内容和状态的提醒已存在。", false);
                return;
            }

            Reminder reminder = new Reminder();
            reminder.Time = time;
            reminder.Message = message;
            reminder.Mode = mode;
            reminder.Enabled = true;
            _working.Reminders.Add(reminder);
            RebuildReminderRows();
            _reminderMessageBox.Text = "";
            SetReminderStatus("已添加：" + time + "  " + message, true);
            MarkDirty();
        }

        private bool ContainsReminder(string time, string message, string mode)
        {
            for (int i = 0; i < _working.Reminders.Count; i++)
            {
                Reminder item = _working.Reminders[i];
                if (string.Equals(item.Time, time, StringComparison.Ordinal)
                    && string.Equals(item.Message, message, StringComparison.Ordinal)
                    && string.Equals(ReminderModes.Normalize(item.Mode), ReminderModes.Normalize(mode), StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private void SetReminderStatus(string text, bool success)
        {
            _reminderStatus.Text = text;
            _reminderStatus.Foreground = success ? UiTheme.TextSecondary : UiTheme.Accent;
        }

        private void RebuildReminderRows()
        {
            _reminderRowsPanel.Children.Clear();
            _reminderRows.Clear();

            for (int i = 0; i < _working.Reminders.Count; i++)
            {
                Reminder reminder = _working.Reminders[i];
                ReminderRow row = CreateReminderRow(reminder);
                _reminderRows.Add(row);
                _reminderRowsPanel.Children.Add(row.Container);
            }

            _reminderCountText.Text = _working.Reminders.Count.ToString(CultureInfo.InvariantCulture) + " 条";
            _reminderEmptyText.Visibility = _working.Reminders.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            _reminderScroll.Visibility = _working.Reminders.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private ReminderRow CreateReminderRow(Reminder reminder)
        {
            ReminderRow row = new ReminderRow(reminder);

            Border container = new Border();
            container.Background = UiTheme.Frozen(0x1B, 0x1B, 0x21);
            container.BorderBrush = UiTheme.Frozen(0x32, 0x32, 0x3C);
            container.BorderThickness = new Thickness(1);
            container.CornerRadius = new CornerRadius(7);
            container.Padding = new Thickness(8, 6, 8, 6);
            container.Margin = new Thickness(0, 0, 0, 6);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            CheckBox enabled = new CheckBox();
            enabled.IsChecked = reminder.Enabled;
            enabled.VerticalAlignment = VerticalAlignment.Center;
            enabled.ToolTip = "启用或停用这条提醒";
            enabled.Margin = new Thickness(0, 0, 4, 0);
            grid.Children.Add(enabled);
            Grid.SetColumn(enabled, 0);

            TextBox timeBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(timeBox);
            timeBox.Text = reminder.Time;
            timeBox.MaxLength = 5;
            timeBox.Width = 56;
            timeBox.Height = 28;
            timeBox.Padding = new Thickness(5, 3, 5, 3);
            timeBox.FontFamily = new FontFamily("Consolas");
            timeBox.FontSize = 12.5;
            timeBox.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(timeBox);
            Grid.SetColumn(timeBox, 1);

            ComboBox modeCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(modeCombo);
            modeCombo.ItemsSource = _reminderModeOptions;
            modeCombo.DisplayMemberPath = "Label";
            modeCombo.SelectedValuePath = "Value";
            modeCombo.SelectedValue = ReminderModes.Normalize(reminder.Mode);
            modeCombo.Width = 98;
            modeCombo.Height = 28;
            modeCombo.FontSize = 11.5;
            modeCombo.Margin = new Thickness(6, 0, 7, 0);
            modeCombo.VerticalAlignment = VerticalAlignment.Center;
            modeCombo.ToolTip = "这条提醒只在所选状态下触发";
            grid.Children.Add(modeCombo);
            Grid.SetColumn(modeCombo, 2);

            TextBox messageBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(messageBox);
            messageBox.Text = reminder.Message;
            messageBox.MaxLength = 120;
            messageBox.Height = 28;
            messageBox.Padding = new Thickness(7, 3, 7, 3);
            messageBox.FontSize = 12.5;
            messageBox.Margin = new Thickness(0, 0, 8, 0);
            messageBox.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(messageBox);
            Grid.SetColumn(messageBox, 3);

            Button test = new Button();
            test.Content = "测试";
            test.Style = UiTheme.GhostButtonStyle;
            test.Padding = new Thickness(7, 4, 7, 4);
            test.FontSize = 11.5;
            test.Height = 28;
            test.VerticalAlignment = VerticalAlignment.Center;
            test.ToolTip = "在悬浮窗上预览这条提醒，不会触发真实提醒";
            test.Click += delegate
            {
                SyncReminderRows();
                string normalizedTime;
                if (!TimeFormatter.TryNormalizeTime(row.Reminder.Time, out normalizedTime))
                {
                    SetReminderStatus("这条提醒的时间格式不正确，请使用 HH:mm。", false);
                    row.TimeBox.Focus();
                    return;
                }
                string normalizedMessage = (row.Reminder.Message ?? string.Empty).Trim();
                if (normalizedMessage.Length == 0)
                {
                    SetReminderStatus("这条提醒没有填写内容。", false);
                    row.MessageBox.Focus();
                    return;
                }
                RaiseReminderTest(normalizedTime, normalizedMessage);
                SetReminderStatus("已发送测试提醒：" + normalizedTime + "  " + normalizedMessage, true);
            };

            Button delete = new Button();
            delete.Content = "删除";
            delete.Style = UiTheme.GhostButtonStyle;
            delete.Padding = new Thickness(7, 4, 7, 4);
            delete.FontSize = 11.5;
            delete.Height = 28;
            delete.VerticalAlignment = VerticalAlignment.Center;
            delete.ToolTip = "删除这条提醒";
            delete.Click += delegate
            {
                SyncReminderRows();
                _working.Reminders.Remove(reminder);
                RebuildReminderRows();
                SetReminderStatus("已删除一条提醒。", true);
                MarkDirty();
            };

            StackPanel rowActions = new StackPanel();
            rowActions.Orientation = Orientation.Horizontal;
            rowActions.VerticalAlignment = VerticalAlignment.Center;
            rowActions.Children.Add(test);
            rowActions.Children.Add(delete);
            grid.Children.Add(rowActions);
            Grid.SetColumn(rowActions, 4);

            container.Child = grid;
            row.Container = container;
            row.EnabledCheck = enabled;
            row.TimeBox = timeBox;
            row.ModeCombo = modeCombo;
            row.MessageBox = messageBox;
            row.DeleteButton = delete;
            row.OriginalTime = reminder.Time;
            row.OriginalMessage = reminder.Message;
            row.OriginalMode = ReminderModes.Normalize(reminder.Mode);
            return row;
        }

        private void SyncReminderRows()
        {
            for (int i = 0; i < _reminderRows.Count; i++)
            {
                ReminderRow row = _reminderRows[i];
                string newTime = (row.TimeBox.Text ?? string.Empty).Trim();
                string newMessage = (row.MessageBox.Text ?? string.Empty).Trim();
                ReminderModeOption selectedMode = row.ModeCombo.SelectedItem as ReminderModeOption;
                string newMode = selectedMode != null ? ReminderModes.Normalize(selectedMode.Value) : ReminderModes.Both;

                if (!string.Equals(row.OriginalTime, newTime, StringComparison.Ordinal)
                    || !string.Equals(row.OriginalMessage, newMessage, StringComparison.Ordinal)
                    || !string.Equals(row.OriginalMode, newMode, StringComparison.Ordinal))
                {
                    row.Reminder.LastTriggeredDate = "";
                    row.OriginalTime = newTime;
                    row.OriginalMessage = newMessage;
                    row.OriginalMode = newMode;
                }
                row.Reminder.Enabled = row.EnabledCheck.IsChecked == true;
                row.Reminder.Time = newTime;
                row.Reminder.Message = newMessage;
                row.Reminder.Mode = newMode;
            }
        }

        private sealed class ReminderRow
        {
            public ReminderRow(Reminder reminder)
            {
                Reminder = reminder;
            }

            public Reminder Reminder { get; private set; }
            public Border Container { get; set; }
            public CheckBox EnabledCheck { get; set; }
            public TextBox TimeBox { get; set; }
            public ComboBox ModeCombo { get; set; }
            public TextBox MessageBox { get; set; }
            public Button DeleteButton { get; set; }
            public string OriginalTime { get; set; }
            public string OriginalMessage { get; set; }
            public string OriginalMode { get; set; }
        }
    }
}
