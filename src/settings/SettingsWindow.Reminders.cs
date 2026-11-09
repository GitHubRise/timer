// 设置窗口「定时提醒」页签：维护工作/娱乐状态提示、提醒显示时长、悬停关闭、
// 闪烁次数与提示音，并提供内置提醒快捷添加和自定义提醒的增删改测。
// 对应 AppSettings.ReminderDisplaySeconds / ReminderHoverCloseEnabled /
// ReminderBlinkCount / ReminderSoundEnabled / Reminders 字段；提醒按
// ReminderModes 过滤当前状态。本页签只改编辑副本，点「应用」后才落盘。

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

    /// <summary>
    /// 「定时提醒」页签：构建提醒设置界面，并把用户编辑写回
    /// <see cref="_working"/> 的提醒配置；改动只标记待应用，不直接落盘。
    /// </summary>
    internal sealed partial class SettingsWindow
    {

        // ===== 布局构建 =====

        /// <summary>
        /// 构建「定时提醒」页签：从上到下依次为当前状态、提醒显示、
        /// 内置提醒快捷按钮、自定义提醒表单和已添加提醒列表。
        /// </summary>
        /// <returns>可直接加入页签容器的根面板。</returns>
        private UIElement BuildRemindersTab()
        {
            StackPanel panel = new StackPanel();

            // 状态卡片只负责摆放单选按钮，真正的状态值由主窗口字段保存
            Border stateCard = UiTheme.MakeCard();
            StackPanel stateContent = new StackPanel();
            stateContent.Children.Add(UiTheme.MakeSectionTitle("当前状态"));
            // 灰色引导语统一由 Hint 创建，关闭「显示提示」时会整块隐藏
            TextBlock stateHint = SettingsUiHints.Hint(
                "标记你现在的状态。提醒会自动过滤：全部状态的提醒始终生效，仅工作和仅娱乐只会在对应状态触发。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            stateHint.Margin = new Thickness(0, 0, 0, 8);
            stateHint.TextWrapping = TextWrapping.Wrap;
            stateContent.Children.Add(stateHint);
            // 两个状态单选按钮横向排列，切换逻辑由主窗口负责
            StackPanel stateRow = new StackPanel();
            stateRow.Orientation = Orientation.Horizontal;
            stateRow.Children.Add(_workModeRadio);
            stateRow.Children.Add(_leisureModeRadio);
            stateContent.Children.Add(stateRow);
            stateCard.Child = stateContent;
            panel.Children.Add(stateCard);

            // 显示参数卡片：时长、闪烁、悬停关闭与提示音
            Border displayCard = UiTheme.MakeCard();
            StackPanel displayContent = new StackPanel();
            displayContent.Children.Add(UiTheme.MakeSectionTitle("提醒显示"));
            // 时长下拉的选项在窗口初始化时填充，这里只负责排版
            TextBlock durationLabel = UiTheme.MakeText("显示时长", 13, UiTheme.TextPrimary, FontWeights.Normal);
            durationLabel.Margin = new Thickness(0, 0, 0, 6);
            displayContent.Children.Add(durationLabel);
            // 下拉项来自 _reminderDisplayOptions，这里只挂到卡片上
            displayContent.Children.Add(_reminderDurationCombo);
            // 闪烁次数用滑块加右侧数值文本，0 表示不闪烁
            displayContent.Children.Add(BuildSliderBlock(
                "闪烁次数（0 = 不闪烁）",
                _reminderBlinkSlider,
                _reminderBlinkValueText,
                "提醒卡片进入时闪烁指定次数，便于在游戏画面中注意到；0 表示不闪烁",
                new Thickness(0, 10, 0, 2)));
            // 悬停关闭和提示音两个勾选项紧随闪烁滑块
            _reminderHoverCheck.Margin = new Thickness(0, 8, 0, 4);
            displayContent.Children.Add(_reminderHoverCheck);
            // 提示音勾选项与悬停关闭保持同一间距
            _reminderSoundCheck.Margin = new Thickness(0, 0, 0, 4);
            displayContent.Children.Add(_reminderSoundCheck);
            // 同一条灰色提示说明「到点关闭」和「悬停 3 秒关闭」两种行为
            TextBlock displayHint = SettingsUiHints.Hint(
                "时长到点自动关闭；开启悬停检测后，鼠标停留在提醒卡片上满 3 秒也会关闭。闪烁和铃声用于提醒，可在上方调整。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            displayHint.Margin = new Thickness(0, 2, 0, 0);
            displayHint.TextWrapping = TextWrapping.Wrap;
            displayContent.Children.Add(displayHint);
            displayCard.Child = displayContent;
            panel.Children.Add(displayCard);

            // 内置提醒按预设生成按钮，点一下即可添加一条可继续编辑的提醒
            Border presetCard = UiTheme.MakeCard();
            StackPanel presetPanel = new StackPanel();
            presetPanel.Children.Add(UiTheme.MakeSectionTitle("内置提醒（点击直接添加）"));
            // 预设按钮自动换行，窄窗口下不会挤出卡片
            WrapPanel presetWrap = new WrapPanel();
            presetWrap.Margin = new Thickness(0, 0, 0, 2);
            List<ReminderPreset> presets = ReminderPresets.Create();
            // 每个预设建一个按钮，用 Tag 带回预设数据，事件处理器只写一份
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
                // 自动化 ID 用时间去掉冒号，保证 UI 测试能稳定定位
                AutomationProperties.SetAutomationId(button, "PresetReminder_" + preset.Time.Replace(":", ""));
                presetWrap.Children.Add(button);
            }
            presetPanel.Children.Add(presetWrap);
            // 说明添加后仍可改时间/状态/内容，降低「内置即固定」的误解
            TextBlock presetHint = SettingsUiHints.Hint(
                "添加后可以在下方修改时间、适用状态或内容，也可以停用或删除。提醒每天触发一次。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            presetHint.Margin = new Thickness(0, 2, 0, 0);
            presetHint.TextWrapping = TextWrapping.Wrap;
            presetPanel.Children.Add(presetHint);
            presetCard.Child = presetPanel;
            panel.Children.Add(presetCard);

            // 自定义提醒表单：时间、适用状态、内容三个输入加测试/添加按钮
            Border customCard = UiTheme.MakeCard();
            customCard.Name = "CustomReminderCard";
            StackPanel customPanel = new StackPanel();
            customPanel.Name = "CustomReminderPanel";
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义提醒"));
            // 四列依次是时间、状态、内容、操作，内容列吃掉剩余宽度
            Grid form = new Grid();
            form.Name = "CustomReminderForm";
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 时间、状态、内容各自成列，标签在上、输入框在下
            StackPanel timePanel = new StackPanel();
            TextBlock timeLabel = UiTheme.MakeText("时间 HH:mm", 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            timeLabel.Margin = new Thickness(0, 0, 0, 4);
            timePanel.Children.Add(timeLabel);
            timePanel.Children.Add(_reminderTimeBox);
            // 三列分别加入网格，列索引在下面显式指定
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

            // 操作按钮靠右并底部对齐，与输入框基线保持一致
            StackPanel formActions = new StackPanel();
            formActions.Orientation = Orientation.Horizontal;
            formActions.VerticalAlignment = VerticalAlignment.Bottom;
            formActions.Margin = new Thickness(8, 0, 0, 0);

            // 测试只做预览，不写入提醒列表，所以放在「添加」左侧
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

            // 添加是主操作，使用主按钮样式；回车提交也走同一入口
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

            // 列表卡片：标题行右侧显示条数，空列表时用占位文本代替滚动区
            Border listCard = UiTheme.MakeCard();
            StackPanel listPanel = new StackPanel();
            Grid listHeader = new Grid();
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock listLabel = UiTheme.MakeText("已添加提醒", 12, UiTheme.TextSecondary, FontWeights.SemiBold);
            listLabel.Margin = new Thickness(0, 0, 0, 8);
            // 标题占满左侧，条数放右侧
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

        // ===== 显示参数事件 =====

        /// <summary>
        /// 显示时长下拉变化：把选中的秒数写回编辑副本。
        /// </summary>
        private void OnReminderDurationChanged(object sender, SelectionChangedEventArgs e)
        {
            // 回填期间控件也会触发事件，直接忽略，避免把旧值当成用户修改
            if (_updating)
            {
                return;
            }
            // 下拉项是选项对象，取出秒数写入配置
            ReminderDisplayOption option = _reminderDurationCombo.SelectedItem as ReminderDisplayOption;
            // 选中项为空说明界面状态异常，不做修改，避免把配置清零
            if (option != null)
            {
                _working.ReminderDisplaySeconds = option.Seconds;
                MarkDirty();
            }
        }

        /// <summary>
        /// 「悬停关闭」勾选框变化：把开关状态写回编辑副本。
        /// </summary>
        private void OnReminderHoverChanged(object sender, RoutedEventArgs e)
        {
            // 回填时同样跳过，防止程序设置勾选状态被当成用户操作
            if (_updating)
            {
                return;
            }
            _working.ReminderHoverCloseEnabled = _reminderHoverCheck.IsChecked == true;
            MarkDirty();
        }

        /// <summary>
        /// 闪烁次数滑块变化：取整后写回配置，并同步右侧数值文本。
        /// </summary>
        private void OnReminderBlinkChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 回填阶段不响应，避免重建界面时把值改回去
            if (_updating)
            {
                return;
            }
            // 滑块给的是连续值，四舍五入成整数次数
            int count = (int)Math.Round(e.NewValue);
            _working.ReminderBlinkCount = count;
            _reminderBlinkValueText.Text = count + " 次";
            MarkDirty();
        }

        /// <summary>
        /// 「提示音」勾选框变化：把开关状态写回编辑副本。
        /// </summary>
        private void OnReminderSoundChanged(object sender, RoutedEventArgs e)
        {
            // 回填期间跳过，逻辑同其他显示参数
            if (_updating)
            {
                return;
            }
            _working.ReminderSoundEnabled = _reminderSoundCheck.IsChecked == true;
            MarkDirty();
        }

        /// <summary>
        /// 按秒数选中显示时长下拉项；配置值不在选项表内时回退到默认项。
        /// </summary>
        /// <param name="seconds">配置中保存的显示秒数。</param>
        private void SelectReminderDurationOption(int seconds)
        {
            ReminderDisplayOption match = null;
            // 选项数量很少，线性查找即可
            for (int i = 0; i < _reminderDisplayOptions.Count; i++)
            {
                if (_reminderDisplayOptions[i].Seconds == seconds)
                {
                    match = _reminderDisplayOptions[i];
                    break;
                }
            }
            // 索引 3 是默认的 12 秒；旧配置可能存了已删除的选项
            if (match == null)
            {
                match = _reminderDisplayOptions[3];
            }

            // 程序设置选中项仍会触发 SelectionChanged，用 _updating 屏蔽
            bool wasUpdating = _updating;
            _updating = true;
            try
            {
                // 回填期间设置选中项，事件处理器会因 _updating 直接返回
                _reminderDurationCombo.SelectedItem = match;
            }
            // 无论设置是否成功都恢复原状态，避免界面永久处于回填模式
            finally
            {
                _updating = wasUpdating;
            }
        }

        // ===== 提醒添加与校验 =====

        /// <summary>
        /// 内置提醒按钮点击：把预设转成一条新提醒加入列表，重复时只提示不添加。
        /// </summary>
        private void OnAddPresetReminder(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            ReminderPreset preset = button != null ? button.Tag as ReminderPreset : null;
            // 按钮的 Tag 在构建时写入，取不到说明界面状态异常，直接忽略
            if (preset == null)
            {
                return;
            }

            // 先把列表里正在编辑的值同步回模型，重复判断才看得到最新数据
            SyncReminderRows();
            // 时间、内容、状态都相同才算重复，避免同一提醒加两遍
            if (ContainsReminder(preset.Time, preset.Message, preset.Mode))
            {
                // 重复添加只提示，不改动现有提醒
                SetReminderStatus("这个内置提醒已经在列表里了。", false);
                return;
            }

            // 内置提醒默认启用，用户可在列表里再停用或改状态
            Reminder reminder = new Reminder();
            reminder.Time = preset.Time;
            reminder.Message = preset.Message;
            reminder.Mode = preset.Mode;
            reminder.Enabled = true;
            _working.Reminders.Add(reminder);
            // 整表重建让新行立刻出现，条数与空状态也一起刷新
            RebuildReminderRows();
            SetReminderStatus("已添加：" + preset.Time + "  " + preset.Message, true);
            // 只标记待应用，由「应用」按钮统一落盘
            MarkDirty();
        }

        /// <summary>
        /// 自定义提醒「添加」按钮的点击入口，实际逻辑在 <see cref="AddCustomReminder"/>。
        /// </summary>
        private void OnAddCustomReminder(object sender, RoutedEventArgs e)
        {
            AddCustomReminder();
        }

        /// <summary>
        /// 测试按钮：校验时间和内容后请求悬浮窗预览一次，不写入提醒列表。
        /// </summary>
        private void OnTestCustomReminder(object sender, RoutedEventArgs e)
        {
            string time;
            // 时间必须先规范成 HH:mm，否则预览没有意义
            if (!TimeFormatter.TryNormalizeTime(_reminderTimeBox.Text, out time))
            {
                SetReminderStatus("时间格式应为 HH:mm，例如 23:00。", false);
                _reminderTimeBox.Focus();
                return;
            }

            string message = (_reminderMessageBox.Text ?? string.Empty).Trim();
            // 空内容无法形成预览，聚焦输入框让用户补全
            if (message.Length == 0)
            {
                SetReminderStatus("请填写提醒内容后再测试。", false);
                _reminderMessageBox.Focus();
                return;
            }

            // 事件同步派发，返回后即可提示测试请求已发出
            RaiseReminderTest(time, message);
            SetReminderStatus("已发送测试提醒：" + time + "  " + message, true);
        }

        /// <summary>
        /// 触发 <see cref="ReminderTestRequested"/>，由控制器把测试提醒送到悬浮窗。
        /// </summary>
        /// <param name="time">已规范为 HH:mm 的时间。</param>
        /// <param name="message">提醒内容。</param>
        private void RaiseReminderTest(string time, string message)
        {
            // 没有订阅者时静默返回，设置窗口仍可独立打开
            if (ReminderTestRequested != null)
            {
                ReminderTestRequested(this, new ReminderPreviewEventArgs(time, message));
            }
        }

        /// <summary>
        /// 内容框回车提交：普通回车添加提醒，Ctrl+Enter 保留给默认行为。
        /// </summary>
        private void OnReminderMessageKeyDown(object sender, KeyEventArgs e)
        {
            // 不拦截 Ctrl+Enter，避免影响输入法或换行等系统行为
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                // 标记已处理，防止回车继续触发默认按钮或换行
                e.Handled = true;
                AddCustomReminder();
            }
        }

        /// <summary>
        /// 校验自定义提醒的时间与内容，去重后写入编辑副本并刷新列表。
        /// 成功后清空内容框、保留时间，方便连续录入。
        /// </summary>
        private void AddCustomReminder()
        {
            string time;
            // 时间格式非法时给出示例并聚焦，后续校验不再进行
            if (!TimeFormatter.TryNormalizeTime(_reminderTimeBox.Text, out time))
            {
                SetReminderStatus("时间格式应为 HH:mm，例如 23:00。", false);
                _reminderTimeBox.Focus();
                return;
            }

            string message = (_reminderMessageBox.Text ?? string.Empty).Trim();
            // 内容为空没有可显示的提醒，聚焦让用户补全
            if (message.Length == 0)
            {
                SetReminderStatus("请填写提醒内容。", false);
                _reminderMessageBox.Focus();
                return;
            }
            // 与输入框 MaxLength 保持一致，防止粘贴或旧配置带来超长内容
            if (message.Length > 120)
            {
                message = message.Substring(0, 120);
            }

            // 先同步列表中未提交的编辑，去重判断才完整
            SyncReminderRows();
            // 状态下拉异常时回退到「全部状态」，提醒不会因此丢失
            ReminderModeOption modeOption = _newReminderModeCombo.SelectedItem as ReminderModeOption;
            string mode = modeOption != null ? modeOption.Value : ReminderModes.Both;
            // 完全相同的时间、内容、状态只保留一条
            if (ContainsReminder(time, message, mode))
            {
                SetReminderStatus("相同时间、内容和状态的提醒已存在。", false);
                return;
            }

            // 新提醒默认启用，立即参与触发计算
            Reminder reminder = new Reminder();
            reminder.Time = time;
            reminder.Message = message;
            reminder.Mode = mode;
            reminder.Enabled = true;
            _working.Reminders.Add(reminder);
            // 重建列表并清空内容框；时间保留，便于连续添加
            RebuildReminderRows();
            _reminderMessageBox.Text = "";
            SetReminderStatus("已添加：" + time + "  " + message, true);
            MarkDirty();
        }

        /// <summary>
        /// 判断列表中是否已有时间、内容、适用状态完全相同的提醒。
        /// </summary>
        /// <param name="time">规范为 HH:mm 的时间。</param>
        /// <param name="message">提醒内容。</param>
        /// <param name="mode">提醒适用状态。</param>
        /// <returns>存在相同提醒时为 <c>true</c>。</returns>
        private bool ContainsReminder(string time, string message, string mode)
        {
            // 列表通常只有几条，逐条比较足够，也避免引入额外索引
            for (int i = 0; i < _working.Reminders.Count; i++)
            {
                Reminder item = _working.Reminders[i];
                // 用 Ordinal 精确比较；状态先 Normalize，兼容旧配置里的别名
                if (string.Equals(item.Time, time, StringComparison.Ordinal)
                    && string.Equals(item.Message, message, StringComparison.Ordinal)
                    && string.Equals(ReminderModes.Normalize(item.Mode), ReminderModes.Normalize(mode), StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 在表单下方显示操作结果：成功用次要文字色，失败用强调色。
        /// </summary>
        /// <param name="text">要显示的结果文案。</param>
        /// <param name="success">成功为 <c>true</c>，失败为 <c>false</c>。</param>
        private void SetReminderStatus(string text, bool success)
        {
            _reminderStatus.Text = text;
            // 这里直接改 Foreground，不走 SetStatusColor：成功和失败都必须
            // 始终可见，不受「显示灰色提示」开关影响
            _reminderStatus.Foreground = success ? UiTheme.TextSecondary : UiTheme.Accent;
        }

        // ===== 列表构建与回填 =====

        /// <summary>
        /// 按编辑副本中的提醒重建整个列表，并刷新条数、空列表占位与滚动区可见性。
        /// </summary>
        private void RebuildReminderRows()
        {
            // 行数不多，整表重建比逐行增删更不容易留下过期控件
            _reminderRowsPanel.Children.Clear();
            // 行对象列表与可视树一起清空，避免残留已删除的引用
            _reminderRows.Clear();

            // 每行控件持有自己的输入框，重建时同步登记到 _reminderRows
            for (int i = 0; i < _working.Reminders.Count; i++)
            {
                Reminder reminder = _working.Reminders[i];
                ReminderRow row = CreateReminderRow(reminder);
                _reminderRows.Add(row);
                _reminderRowsPanel.Children.Add(row.Container);
            }

            // 条数与空状态一起更新，避免列表和标题文案不同步
            _reminderCountText.Text = _working.Reminders.Count.ToString(CultureInfo.InvariantCulture) + " 条";
            // 空列表占位同样受「显示提示」开关控制，关闭时不占位
            _reminderEmptyText.Visibility = SettingsUiHints.ShowHints && _working.Reminders.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            // 没有提醒时收起滚动区，改由占位文本提示
            _reminderScroll.Visibility = _working.Reminders.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>
        /// 为一条提醒创建可编辑的行：启用勾选、时间、适用状态、内容与操作按钮。
        /// 控件值只在 <see cref="SyncReminderRows"/> 中写回模型。
        /// </summary>
        /// <param name="reminder">行对应的提醒实例。</param>
        /// <returns>含控件引用与初始值快照的行对象。</returns>
        private ReminderRow CreateReminderRow(Reminder reminder)
        {
            ReminderRow row = new ReminderRow(reminder);

            // 行卡片沿用深色圆角样式，和页签里的其他卡片保持同一层级
            Border container = new Border();
            // 深色底加 1 像素描边，让每行从卡片背景中分离出来
            container.Background = UiTheme.Frozen(0x1B, 0x1B, 0x21);
            container.BorderBrush = UiTheme.Frozen(0x32, 0x32, 0x3C);
            container.BorderThickness = new Thickness(1);
            container.CornerRadius = new CornerRadius(7);
            container.Padding = new Thickness(8, 6, 8, 6);
            container.Margin = new Thickness(0, 0, 0, 6);

            // 五列：启用、时间、状态、内容、操作；内容列占满剩余宽度
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 勾选框只控制启用状态，不触发整行重建
            CheckBox enabled = new CheckBox();
            enabled.IsChecked = reminder.Enabled;
            enabled.VerticalAlignment = VerticalAlignment.Center;
            enabled.ToolTip = "启用或停用这条提醒";
            enabled.Margin = new Thickness(0, 0, 4, 0);
            grid.Children.Add(enabled);
            Grid.SetColumn(enabled, 0);

            // 时间框限长 5 位，配合等宽字体让 HH:mm 对齐
            TextBox timeBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(timeBox);
            timeBox.Text = reminder.Time;
            // HH:mm 固定 5 个字符，限制长度可挡住明显非法输入
            timeBox.MaxLength = 5;
            timeBox.Width = 56;
            timeBox.Height = 28;
            timeBox.Padding = new Thickness(5, 3, 5, 3);
            timeBox.FontFamily = new FontFamily("Consolas");
            timeBox.FontSize = 12.5;
            timeBox.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(timeBox);
            Grid.SetColumn(timeBox, 1);

            // 状态下拉复用共享选项表，选中值统一走 ReminderModes.Normalize
            ComboBox modeCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(modeCombo);
            modeCombo.ItemsSource = _reminderModeOptions;
            modeCombo.DisplayMemberPath = "Label";
            modeCombo.SelectedValuePath = "Value";
            // 旧配置可能存状态别名，先规范化再匹配选项
            modeCombo.SelectedValue = ReminderModes.Normalize(reminder.Mode);
            modeCombo.Width = 98;
            modeCombo.Height = 28;
            modeCombo.FontSize = 11.5;
            modeCombo.Margin = new Thickness(6, 0, 7, 0);
            modeCombo.VerticalAlignment = VerticalAlignment.Center;
            modeCombo.ToolTip = "这条提醒只在所选状态下触发";
            grid.Children.Add(modeCombo);
            Grid.SetColumn(modeCombo, 2);

            // 内容框最多 120 字，与添加提醒时的截断规则一致
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

            // 行内「测试」只预览当前行，不会改动列表
            Button test = new Button();
            test.Content = "测试";
            test.Style = UiTheme.GhostButtonStyle;
            test.Padding = new Thickness(7, 4, 7, 4);
            test.FontSize = 11.5;
            test.Height = 28;
            test.VerticalAlignment = VerticalAlignment.Center;
            test.ToolTip = "在悬浮窗上预览这条提醒，不会触发真实提醒";
            // 行内测试与表单测试共用 RaiseReminderTest，行为保持一致
            test.Click += delegate
            {
                // 先把本行输入框的值同步到模型，预览的才是界面上的当前内容
                SyncReminderRows();
                string normalizedTime;
                // 行内时间也必须先规范化，错误提示统一落在表单下方
                if (!TimeFormatter.TryNormalizeTime(row.Reminder.Time, out normalizedTime))
                {
                    SetReminderStatus("这条提醒的时间格式不正确，请使用 HH:mm。", false);
                    row.TimeBox.Focus();
                    return;
                }
                string normalizedMessage = (row.Reminder.Message ?? string.Empty).Trim();
                // 空内容无法预览，聚焦本行内容框
                if (normalizedMessage.Length == 0)
                {
                    SetReminderStatus("这条提醒没有填写内容。", false);
                    row.MessageBox.Focus();
                    return;
                }
                RaiseReminderTest(normalizedTime, normalizedMessage);
                SetReminderStatus("已发送测试提醒：" + normalizedTime + "  " + normalizedMessage, true);
            };

            // 删除是破坏性操作，用幽灵按钮弱化，但仍保留在行尾
            Button delete = new Button();
            delete.Content = "删除";
            delete.Style = UiTheme.GhostButtonStyle;
            delete.Padding = new Thickness(7, 4, 7, 4);
            delete.FontSize = 11.5;
            delete.Height = 28;
            delete.VerticalAlignment = VerticalAlignment.Center;
            delete.ToolTip = "删除这条提醒";
            // 删除只改编辑副本，点「应用」后才真正生效
            delete.Click += delegate
            {
                // 删除前先同步其他行的编辑，避免重建时丢掉未提交的输入
                SyncReminderRows();
                _working.Reminders.Remove(reminder);
                // 重建后条数、空状态和滚动区一起刷新
                RebuildReminderRows();
                SetReminderStatus("已删除一条提醒。", true);
                MarkDirty();
            };

            // 测试与删除横排放在最后一列
            StackPanel rowActions = new StackPanel();
            rowActions.Orientation = Orientation.Horizontal;
            rowActions.VerticalAlignment = VerticalAlignment.Center;
            rowActions.Children.Add(test);
            rowActions.Children.Add(delete);
            grid.Children.Add(rowActions);
            Grid.SetColumn(rowActions, 4);

            // 组装完成后把控件引用交给行对象，供同步与测试使用
            container.Child = grid;
            row.Container = container;
            row.EnabledCheck = enabled;
            row.TimeBox = timeBox;
            row.ModeCombo = modeCombo;
            row.MessageBox = messageBox;
            row.DeleteButton = delete;
            // 记录初始值，供 SyncReminderRows 判断用户是否真的改过
            row.OriginalTime = reminder.Time;
            row.OriginalMessage = reminder.Message;
            row.OriginalMode = ReminderModes.Normalize(reminder.Mode);
            return row;
        }

        /// <summary>
        /// 把每行的界面值写回对应提醒；时间、内容或状态变化时清空上次触发日期。
        /// </summary>
        private void SyncReminderRows()
        {
            // 逐行读取界面值，未变化时只同步启用状态，保留触发记录
            for (int i = 0; i < _reminderRows.Count; i++)
            {
                ReminderRow row = _reminderRows[i];
                // 去空白后再比较，避免用户只补了空格就重置触发记录
                string newTime = (row.TimeBox.Text ?? string.Empty).Trim();
                string newMessage = (row.MessageBox.Text ?? string.Empty).Trim();
                ReminderModeOption selectedMode = row.ModeCombo.SelectedItem as ReminderModeOption;
                // 下拉未选中时按「全部状态」处理，避免状态变成空串
                string newMode = selectedMode != null ? ReminderModes.Normalize(selectedMode.Value) : ReminderModes.Both;

                // 只有时间/内容/状态真的变了才清空上次触发日期，
                // 否则改一下启用勾选就会让当天提醒重新触发
                if (!string.Equals(row.OriginalTime, newTime, StringComparison.Ordinal)
                    || !string.Equals(row.OriginalMessage, newMessage, StringComparison.Ordinal)
                    || !string.Equals(row.OriginalMode, newMode, StringComparison.Ordinal))
                {
                    // 清空触发记录并更新快照，后续比较以新值为准
                    row.Reminder.LastTriggeredDate = "";
                    row.OriginalTime = newTime;
                    row.OriginalMessage = newMessage;
                    row.OriginalMode = newMode;
                }
                // 启用状态每行都同步，它不影响触发日期
                row.Reminder.Enabled = row.EnabledCheck.IsChecked == true;
                row.Reminder.Time = newTime;
                row.Reminder.Message = newMessage;
                row.Reminder.Mode = newMode;
            }
        }

        /// <summary>
        /// 列表行的控件引用与初始值快照；行对象只服务本页签，不参与持久化。
        /// </summary>
        private sealed class ReminderRow
        {

            /// <summary>
            /// 创建行对象并绑定对应的提醒实例。
            /// </summary>
            /// <param name="reminder">该行编辑的提醒。</param>
            public ReminderRow(Reminder reminder)
            {
                // 行对象只持有引用，不复制提醒字段
                Reminder = reminder;
            }

            /// <summary>
            /// 行对应的提醒实例，界面值通过 <see cref="SyncReminderRows"/> 写回。
            /// </summary>
            public Reminder Reminder { get; private set; }

            /// <summary>
            /// 行外层容器，重建列表时整体挂到面板上。
            /// </summary>
            public Border Container { get; set; }

            /// <summary>
            /// 启用勾选框，值在同步时写回提醒。
            /// </summary>
            public CheckBox EnabledCheck { get; set; }

            /// <summary>
            /// 时间输入框，内容为 HH:mm。
            /// </summary>
            public TextBox TimeBox { get; set; }

            /// <summary>
            /// 适用状态下拉框，选项来自共享的 _reminderModeOptions。
            /// </summary>
            public ComboBox ModeCombo { get; set; }

            /// <summary>
            /// 提醒内容输入框，最多 120 字。
            /// </summary>
            public TextBox MessageBox { get; set; }

            /// <summary>
            /// 删除按钮；测试按钮不在行对象上暴露。
            /// </summary>
            public Button DeleteButton { get; set; }

            /// <summary>
            /// 同步时的初始时间，用于判断用户是否改过。
            /// </summary>
            public string OriginalTime { get; set; }

            /// <summary>
            /// 同步时的初始内容。
            /// </summary>
            public string OriginalMessage { get; set; }

            /// <summary>
            /// 同步时的初始状态（已规范化）。
            /// </summary>
            public string OriginalMode { get; set; }
        }
    }
}
