// 设置窗口「提醒」页签分部（partial SettingsWindow）：当前状态、提醒显示参数、内置/自定义提醒增删与列表行渲染。
// 调用方：SettingsWindow.Shell 构建页签与接线；提醒测试通过 ReminderTestRequested 事件交给控制器预览。
// 边界：本文件只改 _working.Reminders 与显示参数；触发、去重与气泡展示由 reminders/ 模块和控制器负责。
// 不变量：列表行是编辑副本的视图，行内改动先经 SyncReminderRows 回写模型，任何增删前也要先 Sync。
// 已知坑：保存时的逐行时间/内容校验在 Shell.CommitAll，本页只记录列表增删与即时校验拒绝。

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
    // 本分部文件：提醒页签的控件构造、列表行与增删逻辑；应用/保存链路在 SettingsWindow.Shell.cs。
    internal sealed partial class SettingsWindow
    {
        /// <summary>
        /// 构造「提醒」页签根面板：当前状态 → 提醒显示参数 → 内置提醒快捷添加 → 自定义提醒表单 → 已添加列表。
        /// </summary>
        private UIElement BuildRemindersTab()
        {
            // 页面顺序刻意让「添加」在「列表」之上：用户先填表再看结果，列表增长时不会把表单顶出可视区。
            StackPanel panel = new StackPanel();

            Border stateCard = UiTheme.MakeCard();
            // 卡片一：当前工作/娱乐状态。它是提醒过滤条件，不参与日程触发。
            StackPanel stateContent = new StackPanel();
            stateContent.Children.Add(UiTheme.MakeSectionTitle("当前状态"));
            // 工作/娱乐状态只做提醒过滤，不影响日程；两者互斥，由同一组 RadioButton 保证。
            TextBlock stateHint = SettingsUiHints.Hint(
                // 说明「全部状态」始终生效：用户常误以为两个单选互斥会屏蔽第三种情况。
                "标记你现在的状态。提醒会自动过滤：全部状态的提醒始终生效，仅工作和仅娱乐只会在对应状态触发。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            stateHint.Margin = new Thickness(0, 0, 0, 8);
            stateHint.TextWrapping = TextWrapping.Wrap;
            stateContent.Children.Add(stateHint);
            StackPanel stateRow = new StackPanel();
            // 两个单选由 Shell 的同一组事件处理，勾选后只改 _working 的工作/娱乐标记。
            stateRow.Orientation = Orientation.Horizontal;
            stateRow.Children.Add(_workModeRadio);
            stateRow.Children.Add(_leisureModeRadio);
            stateContent.Children.Add(stateRow);
            stateCard.Child = stateContent;
            panel.Children.Add(stateCard);

            Border displayCard = UiTheme.MakeCard();
            // 卡片二：提醒气泡的表现参数（时长、闪烁次数、悬停关闭、铃声），不涉及触发时刻。
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
            // 悬停关闭依赖气泡内的鼠标检测，3 秒阈值固定写在提醒气泡侧，这里只暴露开关。
            displayContent.Children.Add(_reminderHoverCheck);
            _reminderSoundCheck.Margin = new Thickness(0, 0, 0, 4);
            // 铃声走系统默认提示音，不打包音频资源，避免额外文件与授权问题。
            displayContent.Children.Add(_reminderSoundCheck);
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

            Border presetCard = UiTheme.MakeCard();
            // 卡片三：内置提醒一键添加，省掉手打常用内容；条目本身由 reminders 模块提供。
            StackPanel presetPanel = new StackPanel();
            presetPanel.Children.Add(UiTheme.MakeSectionTitle("内置提醒（点击直接添加）"));
            WrapPanel presetWrap = new WrapPanel();
            // 用 WrapPanel 让内置条目随窗口宽度自动换行，条目数量变化时不需要改布局。
            presetWrap.Margin = new Thickness(0, 0, 0, 2);
            List<ReminderPreset> presets = ReminderPresets.Create();
            // 内置条目数量少，直接逐个建按钮而不做数据模板；顺序与 Create() 返回顺序一致。
            // 内置条目由 reminders 模块提供，这里只负责渲染：增删常用提醒不需要改本文件。
            for (int i = 0; i < presets.Count; i++)
            {
                ReminderPreset preset = presets[i];
                Button button = new Button();
                // 次要按钮 + 8px 右/下外边距，靠 WrapPanel 自动换行，条数变化时不需要改布局代码。
                button.Content = preset.ButtonText;
                button.Style = UiTheme.SecondaryButtonStyle;
                button.Padding = new Thickness(11, 6, 11, 6);
                button.Margin = new Thickness(0, 0, 8, 8);
                button.FontSize = 12;
                button.ToolTip = "[" + ReminderModes.DisplayName(preset.Mode) + "] " + preset.Message;
                button.Tag = preset;
                button.Click += OnAddPresetReminder;
                // 每个预置按钮点一次复制一条；是否重复由 OnAddPresetReminder 统一判断。
                AutomationProperties.SetAutomationId(button, "PresetReminder_" + preset.Time.Replace(":", ""));
                presetWrap.Children.Add(button);
            }
            presetPanel.Children.Add(presetWrap);
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

            Border customCard = UiTheme.MakeCard();
            // 卡片四：自定义表单（时间 / 适用状态 / 内容 / 测试 / 添加）。
            customCard.Name = "CustomReminderCard";
            // Name 供 UI 自动化脚本定位卡片，与 x:Name 字段生成无关（这里没有代码生成）。
            StackPanel customPanel = new StackPanel();
            customPanel.Name = "CustomReminderPanel";
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义提醒"));
            Grid form = new Grid();
            form.Name = "CustomReminderForm";
            // 四列固定栅格：时间 84、状态 122、内容吃掉剩余宽度、按钮列自适应，避免窄窗口下按钮被挤掉。
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel timePanel = new StackPanel();
            // 时间列固定 84px：刚好放下「时间 HH:mm」标签与 5 字宽输入框，再窄标签会换行。
            TextBlock timeLabel = UiTheme.MakeText("时间 HH:mm", 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            // 标签直接写出格式样例，减少用户猜测输入法的失败率。
            timeLabel.Margin = new Thickness(0, 0, 0, 4);
            timePanel.Children.Add(timeLabel);
            timePanel.Children.Add(_reminderTimeBox);
            form.Children.Add(timePanel);
            Grid.SetColumn(timePanel, 0);

            StackPanel modePanel = new StackPanel();
            // 状态列 122px：按最长的「全部状态/仅工作/仅娱乐」文案加下拉箭头估算。
            TextBlock modeLabel = UiTheme.MakeText("适用状态", 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            modeLabel.Margin = new Thickness(0, 0, 0, 4);
            modePanel.Children.Add(modeLabel);
            modePanel.Children.Add(_newReminderModeCombo);
            modePanel.Margin = new Thickness(8, 0, 0, 0);
            form.Children.Add(modePanel);
            Grid.SetColumn(modePanel, 1);

            StackPanel messagePanel = new StackPanel();
            // 内容列用星号宽度吃满剩余空间：提醒内容最长，最需要横向空间。
            TextBlock messageLabel = UiTheme.MakeText("提醒内容", 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            messageLabel.Margin = new Thickness(0, 0, 0, 4);
            messagePanel.Children.Add(messageLabel);
            messagePanel.Children.Add(_reminderMessageBox);
            messagePanel.Margin = new Thickness(8, 0, 0, 0);
            form.Children.Add(messagePanel);
            Grid.SetColumn(messagePanel, 2);

            StackPanel formActions = new StackPanel();
            // 按钮列用 Auto：窗口再窄也保证「测试」「添加」完整可见，不会被内容列挤掉。
            // 操作区贴底对齐：左侧「测试」只预览，右侧「添加」才写入 _working.Reminders。
            formActions.Orientation = Orientation.Horizontal;
            formActions.VerticalAlignment = VerticalAlignment.Bottom;
            formActions.Margin = new Thickness(8, 0, 0, 0);

            Button testButton = new Button();
            // 测试按钮不写配置：只为确认「这条内容在该时间弹出」是否可读。
            testButton.Content = "测试";
            testButton.Style = UiTheme.SecondaryButtonStyle;
            testButton.Height = 32;
            testButton.MinWidth = 54;
            testButton.Padding = new Thickness(8, 5, 8, 5);
            testButton.Margin = new Thickness(0, 0, 6, 0);
            testButton.ToolTip = "按当前时间和内容在悬浮窗上预览一次提醒，不会添加";
            testButton.Click += OnTestCustomReminder;
            // 测试只发事件、不落盘：处理函数里的拒绝分支会自己写状态行。
            AutomationProperties.SetAutomationId(testButton, "TestReminderButton");
            formActions.Children.Add(testButton);

            Button addButton = new Button();
            // 主按钮：回车（非 Ctrl）也会走到同一个 AddCustomReminder，保持键盘与鼠标一致。
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
            // 状态行夹在表单与列表之间：添加结果离操作点最近，不用滚动去找。
            customCard.Child = customPanel;
            panel.Children.Add(customCard);

            Border listCard = UiTheme.MakeCard();
            // 卡片五：已添加提醒列表；行内可直接改时间/状态/内容，也可以测试或删除。
            StackPanel listPanel = new StackPanel();
            Grid listHeader = new Grid();
            // 表头左侧标题、右侧计数：计数由 RebuildReminderRows 统一刷新。
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock listLabel = UiTheme.MakeText("已添加提醒", 12, UiTheme.TextSecondary, FontWeights.SemiBold);
            listLabel.Margin = new Thickness(0, 0, 0, 8);
            listHeader.Children.Add(listLabel);
            listHeader.Children.Add(_reminderCountText);
            Grid.SetColumn(_reminderCountText, 1);
            listPanel.Children.Add(listHeader);
            listPanel.Children.Add(_reminderScroll);
            // 空态文案与滚动区互斥显示，避免空列表时留一个无内容的滚动框。
            listPanel.Children.Add(_reminderEmptyText);
            listCard.Child = listPanel;
            panel.Children.Add(listCard);

            return panel;
        }

        /// <summary>
        /// 提醒显示时长下拉变化：写回秒数。
        /// </summary>
        private void OnReminderDurationChanged(object sender, SelectionChangedEventArgs e)
        {
            // 回填下拉项也会触发 SelectionChanged，_updating 期间直接忽略，避免把程序赋值当成用户修改。
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

        /// <summary>
        /// 悬停关闭开关变化：开启后鼠标在提醒卡片上停留满 3 秒即关闭。
        /// </summary>
        private void OnReminderHoverChanged(object sender, RoutedEventArgs e)
        {
            // 勾选状态在回填时也会触发，_updating 期间不能标记为脏。
            if (_updating)
            {
                return;
            }
            _working.ReminderHoverCloseEnabled = _reminderHoverCheck.IsChecked == true;
            // 3 秒停留阈值写在提醒气泡一侧（ReminderPopup），这里只保存开关值，避免两处各存一份数字。
            MarkDirty();
        }

        /// <summary>
        /// 闪烁次数滑块变化：0 表示不闪烁，直接写入整数次数。
        /// </summary>
        private void OnReminderBlinkChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 滑动条在回填时同样会触发 ValueChanged；拖动过程中的连续值只更新显示，不写日志。
            if (_updating)
            {
                return;
            }
            int count = (int)Math.Round(e.NewValue);
            // 滑块是 double，写回模型前取整；0 表示不闪烁，与标签文案的含义一一对应。
            _working.ReminderBlinkCount = count;
            _reminderBlinkValueText.Text = count + " 次";
            MarkDirty();
        }

        /// <summary>
        /// 提示音开关变化。
        /// </summary>
        private void OnReminderSoundChanged(object sender, RoutedEventArgs e)
        {
            // 铃声开关只是偏好，回填期忽略。
            if (_updating)
            {
                return;
            }
            _working.ReminderSoundEnabled = _reminderSoundCheck.IsChecked == true;
            // 铃声走系统默认提示音，不打包音频资源，避免额外文件与授权问题。
            MarkDirty();
        }

        /// <summary>
        /// 把时长下拉框定位到指定秒数；配置里的值不在候选列表时回退到默认项（索引 3）。
        /// </summary>
        private void SelectReminderDurationOption(int seconds)
        {
            ReminderDisplayOption match = null;
            // 线性查找即可：候选只有几项，不值得为它维护字典。
            for (int i = 0; i < _reminderDisplayOptions.Count; i++)
            {
                if (_reminderDisplayOptions[i].Seconds == seconds)
                {
                    match = _reminderDisplayOptions[i];
                    break;
                }
            }
            // 旧配置或手改 JSON 可能存了非候选值；索引 3 是默认时长，必须保证 SelectedItem 不为 null。
            if (match == null)
            {
                match = _reminderDisplayOptions[3];
            }

            // 回填会触发 SelectionChanged，先备份当前 _updating 再恢复，
            // 因为这里可能被 LoadControlsFromWorking 之外的路径调用。
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

        /// <summary>
        /// 点击内置提醒按钮：复制一条启用的提醒追加到列表，已存在相同「时间+内容+状态」则拒绝。
        /// </summary>
        private void OnAddPresetReminder(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            // Tag 承载内置条目对象；类型不符（比如 Shell 误接线）直接返回，不做任何改动。
            ReminderPreset preset = button != null ? button.Tag as ReminderPreset : null;
            if (preset == null)
            {
                return;
            }

            // 列表行里的编辑可能还没同步到模型，先 Sync 再做去重判断，否则会漏判刚改过的行。
            SyncReminderRows();
            if (ContainsReminder(preset.Time, preset.Message, preset.Mode))
            {
                SetReminderStatus("这个内置提醒已经在列表里了。", false);
                AppLog.Warn("Reminder", "拒绝添加内置提醒：已存在相同条目 " + preset.Time + " " + preset.Message);
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
            AppLog.Info("Reminder", "添加内置提醒：" + preset.Time + " " + preset.Message + "（" + ReminderModes.DisplayName(preset.Mode) + "）");
            MarkDirty();
        }

        /// <summary>
        /// 「添加」按钮入口：与内容框回车走同一条路径（AddCustomReminder），只在副本上加一条。
        /// </summary>
        private void OnAddCustomReminder(object sender, RoutedEventArgs e)
        {
            // 按钮与回车共用 AddCustomReminder，避免出现两套校验逻辑。
            AddCustomReminder();
        }

        /// <summary>
        /// 「测试」按钮入口：只预览提醒，不写入列表。
        /// </summary>
        private void OnTestCustomReminder(object sender, RoutedEventArgs e)
        {
            // 只走事件通知（由控制器在悬浮窗上预览），因此这里不产生列表变更。
            string time;
            if (!TimeFormatter.TryNormalizeTime(_reminderTimeBox.Text, out time))
            {
                SetReminderStatus("时间格式应为 HH:mm，例如 23:00。", false);
                _reminderTimeBox.Focus();
                AppLog.Warn("Reminder", "测试提醒被拒绝：时间格式非法，输入=" + (_reminderTimeBox.Text ?? string.Empty));
                return;
            }

            string message = (_reminderMessageBox.Text ?? string.Empty).Trim();
            if (message.Length == 0)
            {
                SetReminderStatus("请填写提醒内容后再测试。", false);
                _reminderMessageBox.Focus();
                AppLog.Warn("Reminder", "测试提醒被拒绝：内容为空，时间=" + time);
                return;
            }

            RaiseReminderTest(time, message);
            SetReminderStatus("已发送测试提醒：" + time + "  " + message, true);
            AppLog.Info("Reminder", "发送测试提醒：" + time + " " + message);
        }

        /// <summary>
        /// 把测试预览请求抛给控制器；没有订阅者时静默跳过（例如窗口以独立方式打开）。
        /// </summary>
        private void RaiseReminderTest(string time, string message)
        {
            if (ReminderTestRequested != null)
            {
                ReminderTestRequested(this, new ReminderPreviewEventArgs(time, message));
            }
        }

        /// <summary>
        /// 内容框快捷键：Enter 直接添加（Ctrl+Enter 不拦截，留给输入多行或其它处理）。
        /// </summary>
        private void OnReminderMessageKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                e.Handled = true;
                // 置 Handled 阻止后续默认文本输入，避免回车被当成内容插进单行框。
                AddCustomReminder();
            }
        }

        /// <summary>
        /// 从「自定义提醒」表单取时间/内容/适用状态并追加到列表：
        /// 时间必须能归一化为 HH:mm，内容非空且截断到 120 字，重复（时间+内容+状态全同）直接拒绝。
        /// </summary>
        private void AddCustomReminder()
        {
            string time;
            // 校验失败只写状态行并把焦点放回出错字段，不弹对话框。
            if (!TimeFormatter.TryNormalizeTime(_reminderTimeBox.Text, out time))
            {
                AppLog.Warn("Reminder", "添加提醒被拒绝：时间格式非法，输入=" + (_reminderTimeBox.Text ?? string.Empty) + "。");
                SetReminderStatus("时间格式应为 HH:mm，例如 23:00。", false);
                _reminderTimeBox.Focus();
                return;
            }

            string message = (_reminderMessageBox.Text ?? string.Empty).Trim();
            // 空内容没有提示意义，直接拒绝。
            if (message.Length == 0)
            {
                AppLog.Warn("Reminder", "添加提醒被拒绝：内容为空，时间=" + time + "。");
                SetReminderStatus("请填写提醒内容。", false);
                _reminderMessageBox.Focus();
                return;
            }
            // 120 字是提醒卡片两行能容纳的上限（与 Shell.CommitAll 的截断长度保持一致），超出直接截断而不报错。
            if (message.Length > 120)
            {
                message = message.Substring(0, 120);
            }

            // 增删前先同步行内编辑，否则列表与 _working 会不一致（例如刚在行里改过时间）。
            SyncReminderRows();
            ReminderModeOption modeOption = _newReminderModeCombo.SelectedItem as ReminderModeOption;
            // 未选状态时按「全部状态」处理，避免新增的提醒永远不触发。
            string mode = modeOption != null ? modeOption.Value : ReminderModes.Both;
            // 重复判定在归一化之后做，避免 "9:00" 与 "09:00" 被当成两条。
            if (ContainsReminder(time, message, mode))
            {
                AppLog.Warn("Reminder", "添加提醒被拒绝：已存在相同时间/内容/状态的提醒，时间=" + time + "，内容=" + message + "。");
                SetReminderStatus("相同时间、内容和状态的提醒已存在。", false);
                return;
            }

            // 复制出一份新对象而不是复用预置或字符串：列表项后续允许就地改时间/内容/状态。
            Reminder reminder = new Reminder();
            reminder.Time = time;
            reminder.Message = message;
            reminder.Mode = mode;
            reminder.Enabled = true;
            _working.Reminders.Add(reminder);
            // 追加成功才记日志：上面的三条拒绝分支各自已记，避免一次操作出现两条结果日志。
            AppLog.Info("Reminder", "添加提醒：" + time + " " + message + "（" + ReminderModes.DisplayName(mode) + "）。");
            RebuildReminderRows();
            _reminderMessageBox.Text = "";
            // 成功后清空内容框但保留时间：连续添加同一时段的提醒更省事。
            SetReminderStatus("已添加：" + time + "  " + message, true);
            MarkDirty();
        }

        /// <summary>
        /// 判断列表里是否已有「时间 + 内容 + 适用状态」完全相同的提醒；三项都用 Ordinal 全等比较。
        /// </summary>
        private bool ContainsReminder(string time, string message, string mode)
        {
            // 逐项比较而不是拼字符串：拼接会引入分隔符冲突。适用状态先归一化再比，
            // 是因为历史配置里可能存的是空串或别名。
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

        /// <summary>
        /// 写提醒页的状态行：成功用次要色，失败用强调色（Accent 在暗色主题里就是红色系警示色）。
        /// </summary>
        private void SetReminderStatus(string text, bool success)
        {
            _reminderStatus.Text = text;
            _reminderStatus.Foreground = success ? UiTheme.TextSecondary : UiTheme.Accent;
            // Accent 在暗色主题里就是红色系警示色，与其它页签的失败提示保持同一套语义。
        }

        /// <summary>
        /// 按 _working.Reminders 全量重建提醒行；空列表时显示占位文案并隐藏滚动区。
        /// </summary>
        private void RebuildReminderRows()
        {
            // 全量重建：行内控件与模型是临时绑定，增量更新容易漏掉状态，数据量很小，重建更可靠。
            _reminderRowsPanel.Children.Clear();
            // 先清控件再清列表：两个集合必须同步，否则 SyncReminderRows 会读到已被丢弃的行。
            _reminderRows.Clear();

            for (int i = 0; i < _working.Reminders.Count; i++)
            {
                Reminder reminder = _working.Reminders[i];
                ReminderRow row = CreateReminderRow(reminder);
                _reminderRows.Add(row);
                _reminderRowsPanel.Children.Add(row.Container);
            }

            _reminderCountText.Text = _working.Reminders.Count.ToString(CultureInfo.InvariantCulture) + " 条";

            // 空列表时隐藏滚动区并显示占位文案；提示总开关关闭时空态文案同样不显示。
            _reminderEmptyText.Visibility = SettingsUiHints.ShowHints && _working.Reminders.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            _reminderScroll.Visibility = _working.Reminders.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>
        /// 为一条提醒创建行控件：启用勾选、时间、适用状态、内容、测试与删除按钮。
        /// 行内控件只做展示与就地编辑，真正写回模型要靠 SyncReminderRows。
        /// </summary>
        private ReminderRow CreateReminderRow(Reminder reminder)
        {
            // 行与模型一一对应；模型对象由调用方持有，行只保存引用。
            ReminderRow row = new ReminderRow(reminder);

            Border container = new Border();
            // 行容器：1px 描边 + 7px 圆角，与设置窗口其它卡片保持同一套暗色层次（0x1B1B21 / 0x32323C）。
            container.Background = UiTheme.Frozen(0x1B, 0x1B, 0x21);
            container.BorderBrush = UiTheme.Frozen(0x32, 0x32, 0x3C);
            container.BorderThickness = new Thickness(1);
            container.CornerRadius = new CornerRadius(7);
            container.Padding = new Thickness(8, 6, 8, 6);
            container.Margin = new Thickness(0, 0, 0, 6);

            Grid grid = new Grid();
            // 五列：勾选 24 / 时间 62 / 适用状态 104 / 内容自适应 / 操作自动，宽度按最长的内置文案估算。
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            CheckBox enabled = new CheckBox();
            // 勾选框宽度 24px 与列宽一致，行内元素才不会错位。
            // 启用勾选只改模型状态，不立即触发提醒；停用的提醒仍留在列表里便于再次启用。
            enabled.IsChecked = reminder.Enabled;
            enabled.VerticalAlignment = VerticalAlignment.Center;
            enabled.ToolTip = "启用或停用这条提醒";
            enabled.Margin = new Thickness(0, 0, 4, 0);
            grid.Children.Add(enabled);
            Grid.SetColumn(enabled, 0);

            TextBox timeBox = new TextBox();
            // 56×28：高度与行内按钮一致，宽度够放 "HH:mm" 加左右内边距。
            // 时间框限长 5（HH:mm），等宽字体保证冒号与数字对齐，方便一眼比对。
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
            // 98×28：比 104 的列宽窄一点，给左右各留 6–7px 间隙，行内不贴边。
            // 用 SelectedValue/SelectedValuePath 绑定状态值，取到的是规范化后的字符串而不是显示文案。
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
            // 不设固定宽度：靠 Grid 的星号列自动拉伸，窗口变宽时内容框跟着变长。
            // 内容框限长 120，与 Shell.CommitAll 的截断长度一致，避免保存时被二次裁剪。
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
            // 行内按钮统一用 Ghost 样式 + 11.5px：视觉权重必须低于内容框，否则列表会被按钮抢占注意力。
            // 测试按钮只发 ReminderTestRequested，不写 _working、不落盘，点多少次都不会产生新提醒。
            test.Content = "测试";
            test.Style = UiTheme.GhostButtonStyle;
            test.Padding = new Thickness(7, 4, 7, 4);
            test.FontSize = 11.5;
            test.Height = 28;
            test.VerticalAlignment = VerticalAlignment.Center;
            test.ToolTip = "在悬浮窗上预览这条提醒，不会触发真实提醒";
            test.Click += delegate
            {
                // 先把行内编辑写回模型，再用归一化后的值发测试，避免测到旧值。
                SyncReminderRows();
                string normalizedTime;
                if (!TimeFormatter.TryNormalizeTime(row.Reminder.Time, out normalizedTime))
                {
                    // 行内时间被改成非法值时不发测试，只就地提示并聚焦。
                    SetReminderStatus("这条提醒的时间格式不正确，请使用 HH:mm。", false);
                    row.TimeBox.Focus();
                    return;
                }
                string normalizedMessage = (row.Reminder.Message ?? string.Empty).Trim();
                // 空内容没有展示意义，测试请求直接拒绝。
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
            // 与「测试」同款样式，只靠文案区分，避免行内出现两种按钮风格。
            // 删除不做二次确认：本页改动只作用于编辑副本，点「应用」前不落盘，误删可关窗放弃。
            delete.Content = "删除";
            delete.Style = UiTheme.GhostButtonStyle;
            delete.Padding = new Thickness(7, 4, 7, 4);
            delete.FontSize = 11.5;
            delete.Height = 28;
            delete.VerticalAlignment = VerticalAlignment.Center;
            delete.ToolTip = "删除这条提醒";
            delete.Click += delegate
            {
                // 删除前同步一次，保证日志与状态行里的时间/内容是用户当前看到的值。
                SyncReminderRows();
                AppLog.Info("Reminder", "删除提醒：" + reminder.Time + " " + reminder.Message + "。");
                _working.Reminders.Remove(reminder);
                RebuildReminderRows();
                SetReminderStatus("已删除一条提醒。", true);
                MarkDirty();
            };

            StackPanel rowActions = new StackPanel();
            // 操作区：测试与删除横排、垂直居中，和内容框基线对齐。
            rowActions.Orientation = Orientation.Horizontal;
            rowActions.VerticalAlignment = VerticalAlignment.Center;
            rowActions.Children.Add(test);
            rowActions.Children.Add(delete);
            grid.Children.Add(rowActions);
            Grid.SetColumn(rowActions, 4);

            container.Child = grid;
            // 先把网格挂进圆角容器，再回填控件引用：行对象持有的必须是最终会被渲染的那个实例。
            // 把控件引用回填到行对象：SyncReminderRows 靠它读值，删除按钮靠它定位焦点。
            row.Container = container;
            row.EnabledCheck = enabled;
            row.TimeBox = timeBox;
            row.ModeCombo = modeCombo;
            row.MessageBox = messageBox;
            row.DeleteButton = delete;
            // Row.Original* 保存「上次同步时的值」，用于判断用户是否真的改过内容。
            row.OriginalTime = reminder.Time;
            row.OriginalMessage = reminder.Message;
            row.OriginalMode = ReminderModes.Normalize(reminder.Mode);
            return row;
        }

        /// <summary>
        /// 把每一行的编辑结果写回对应的 Reminder 对象：时间/内容/状态任一变化就清空 LastTriggeredDate，
        /// 让改动后的提醒当天可以再次触发；启用开关每次同步都覆盖。
        /// </summary>
        private void SyncReminderRows()
        {
            // 逐行回写而非只写变更行：未变更行的赋值是幂等的，逻辑更简单。
            for (int i = 0; i < _reminderRows.Count; i++)
            {
                ReminderRow row = _reminderRows[i];
                string newTime = (row.TimeBox.Text ?? string.Empty).Trim();
                string newMessage = (row.MessageBox.Text ?? string.Empty).Trim();
                ReminderModeOption selectedMode = row.ModeCombo.SelectedItem as ReminderModeOption;
                // 下拉项可能为 null（列表刚重建、还没选中），此时按「全部状态」兜底，不让状态变成空串。
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
                // 启用开关每次同步都无条件覆盖：勾选状态不参与「内容是否改过」的判定。
                row.Reminder.Time = newTime;
                row.Reminder.Message = newMessage;
                row.Reminder.Mode = newMode;
            }
        }

        /// <summary>
        /// 一条提醒对应的行控件组：模型引用、容器、各输入控件，以及上次同步时的原始值快照。
        /// 只在本分部类内使用，字段用自动属性便于 CreateReminderRow 逐项赋值。
        /// </summary>
        private sealed class ReminderRow
        {
            public ReminderRow(Reminder reminder)
            {
                Reminder = reminder;
            }

            public Reminder Reminder { get; private set; }
            // 模型引用只读：换模型意味着换行，必须整行重建，不能被就地改写。
            // 容器与控件由 CreateReminderRow 回填；重建列表时整行丢弃，不保留旧控件。
            public Border Container { get; set; }
            public CheckBox EnabledCheck { get; set; }
            public TextBox TimeBox { get; set; }
            public ComboBox ModeCombo { get; set; }
            public TextBox MessageBox { get; set; }
            public Button DeleteButton { get; set; }
            // 删除按钮引用只在重建前定位焦点时用得到，不参与数据回写。
            public string OriginalTime { get; set; }
            public string OriginalMessage { get; set; }
            public string OriginalMode { get; set; }
        }
    }
}
