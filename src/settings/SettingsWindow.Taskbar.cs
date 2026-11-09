// 设置窗口「任务栏时钟」页签：显示开关、字号缩放、加粗与两种格式语言。
// 字段对应 AppSettings.ShowTaskbarClock / TaskbarFontScale / TaskbarBold /
// TaskbarFormatMode / TaskbarFormat / TaskbarDotNetFormat。
// 契约：覆盖窗只按原生 TrayClockWClass 矩形绘制，不移动、不隐藏系统时钟；字体取系统
// WM_GETFONT、颜色按任务栏背景亮度选，本页不提供字体与颜色设置（旧字段仅作兜底）。
// 字号 = 系统字号 × TaskbarFontScale / 100，宽度不足时由 Viewbox 收紧字距，不缩字号。

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace GameClock
{
    internal sealed partial class SettingsWindow
    {
        // ===== 页签布局构建 =====

        /// <summary>
        /// 构建任务栏时钟页签：开关卡片、文字大小卡片、可选交互说明卡片，最后接格式区。
        /// 开关与字号控件由主文件创建，事件在 AttachEvents 或应用时统一收集。
        /// </summary>
        /// <returns>可直接加入页签容器的根面板。</returns>
        private UIElement BuildTaskbarTab()
        {
            StackPanel panel = new StackPanel();

            // 开关卡片：说明覆盖窗与系统时钟的边界，避免用户误以为会替换原生时钟。
            Border switchCard = UiTheme.MakeCard();
            StackPanel switchPanel = new StackPanel();
            switchPanel.Children.Add(UiTheme.MakeSectionTitle("任务栏时钟"));
            switchPanel.Children.Add(_taskbarClockCheck);
            // 提示强调「字体颜色跟随系统」「不移动不隐藏」「关闭即恢复」三个契约。
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

            // 文字大小卡片：字号档位与加粗放在一起，它们共同决定文字占用的宽度。
            Border lookCard = UiTheme.MakeCard();
            StackPanel lookPanel = new StackPanel();
            lookPanel.Children.Add(UiTheme.MakeSectionTitle("文字大小"));
            // 提示解释「放大字号后靠收紧字距而不是缩字号」，这是覆盖窗的排版策略。
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

            // 交互说明卡片只在灰提示打开时创建，关闭提示时整张卡片不进入布局。
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

            // 格式区独立成段：模式、常用格式、自定义格式三张卡片。
            panel.Children.Add(BuildTaskbarFormatSection());

            return panel;
        }

        /// <summary>
        /// 构建任务栏格式区：格式语言单选、常用格式下拉、自定义格式输入与状态行。
        /// 两套格式分别保存，切换模式时靠输入框在 .NET 与简洁变量之间倒手。
        /// </summary>
        /// <returns>可直接加入页签根面板的纵向容器。</returns>
        private UIElement BuildTaskbarFormatSection()
        {
            StackPanel section = new StackPanel();

            // 模式卡片：单选按钮决定使用 .NET 还是简洁变量，两套格式互不覆盖。
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

            // 常用格式卡片：下拉数据源随模式整体替换，首项是「自定义格式」占位。
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

            // 自定义格式卡片：模式说明、输入框、插入换行按钮与校验状态行。
            Border customCard = UiTheme.MakeCard();
            StackPanel customPanel = new StackPanel();
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义格式"));
            customPanel.Children.Add(_taskbarFormatHintText);
            customPanel.Children.Add(_taskbarFormatBox);

            // 按钮行：左侧「插入换行」固定宽度，右侧提示输入即更新。
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
            // 任务栏只有两行高度，这里提前告诉用户换行数量上限。
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

        // ===== 格式输入与模式切换 =====

        /// <summary>格式输入框文本变化：装载阶段跳过，其余交给统一的校验与写回流程。</summary>
        private void OnTaskbarFormatChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            HandleTaskbarFormatTextChanged();
        }

        /// <summary>
        /// 切换格式语言：先把输入框内容存回旧模式字段，再换预设列表并回填新模式已存格式。
        /// 两种格式分别保存在 <c>TaskbarFormat</c> 与 <c>TaskbarDotNetFormat</c>，切换不丢内容。
        /// </summary>
        private void OnTaskbarFormatModeChanged(object sender, RoutedEventArgs e)
        {
            // 回填控件阶段会触发 Checked，那不是用户操作，直接跳过。
            if (_updating)
            {
                return;
            }

            bool tokenMode = _taskbarTokenFormatRadio.IsChecked == true;
            string newMode = tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet;
            // 模式没变时只刷新说明，不重绑数据源，避免打断正在输入的用户。
            if (string.Equals(_working.TaskbarFormatMode, newMode, StringComparison.Ordinal))
            {
                UpdateTaskbarFormatHint();
                return;
            }

            // 切走前把输入框内容写回旧模式对应的字段，否则这次编辑会丢失。
            if (FormatEngine.IsTokenMode(_working.TaskbarFormatMode))
            {
                _working.TaskbarFormat = _taskbarFormatBox.Text ?? string.Empty;
            }
            else
            {
                _working.TaskbarDotNetFormat = _taskbarFormatBox.Text ?? string.Empty;
            }

            _working.TaskbarFormatMode = newMode;
            // 换用新模式对应的预设列表，并把该模式上次保存的格式回填到输入框。
            _taskbarActivePresets = tokenMode ? _taskbarTokenPresets : _taskbarDotNetPresets;

            _updating = true;
            try
            {
                _taskbarFormatCombo.ItemsSource = _taskbarActivePresets;
                _taskbarFormatBox.Text = tokenMode ? _working.TaskbarFormat : _working.TaskbarDotNetFormat;
            }
            finally
            {
                // 重绑结束必须恢复事件开关，否则后续输入不再触发校验。
                _updating = false;
            }

            // 最后统一刷新说明与校验结果，保证状态行和输入框描述同一份内容。
            UpdateTaskbarFormatHint();
            HandleTaskbarFormatTextChanged();
            MarkDirty();
        }

        /// <summary>按当前模式刷新输入框上方的语法说明，重点提示 mm/MM 的大小写差异。</summary>
        private void UpdateTaskbarFormatHint()
        {
            bool tokenMode = _taskbarTokenFormatRadio.IsChecked == true;
            _taskbarFormatHintText.Text = tokenMode
                ? "支持简洁变量（区分大小写；mm 是月份、ff 是分钟）。输入时实时更新，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。"
                : "支持 .NET 日期格式（MM 是月份、mm 是分钟）。输入时实时更新，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
        }

        /// <summary>
        /// 常用格式下拉选择变化：把预设里的格式串填入输入框，再手动走一次校验。
        /// 「自定义格式」占位项没有格式串，选中它时保持输入框原样。
        /// </summary>
        private void OnTaskbarFormatPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            TimeFormatPreset preset = _taskbarFormatCombo.SelectedItem as TimeFormatPreset;
            // 占位项（或空格式）不代表要替换内容，直接返回。
            if (preset == null || string.IsNullOrEmpty(preset.Format))
            {
                return;
            }

            // 回填输入框会触发 TextChanged，用 _updating 屏蔽后再手动执行一次校验。
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

        /// <summary>
        /// 任务栏格式的统一校验入口：两种模式各用各的校验器，校验通过才写回编辑副本。
        /// 失败时保留上一个有效格式，只把错误写到状态行，并同步下拉选中项、标记脏。
        /// </summary>
        private void HandleTaskbarFormatTextChanged()
        {
            bool tokenMode = _taskbarTokenFormatRadio.IsChecked == true;
            string normalized;
            string error;
            // 简洁变量允许最多一个换行（true），.NET 格式不允许换行。
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
                // 合法状态用灰色引导语，说明下一步是保存后应用。
                _taskbarFormatStatus.Text = "格式有效，保存后应用到任务栏时钟。";
                SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.TextMuted);
            }
            else
            {
                // 非法格式不覆盖已保存值，状态行换成强调色，错误始终可见。
                _taskbarFormatStatus.Text = error;
                SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.Accent);
            }

            UpdateTaskbarFormatComboSelection();
            // 无论校验是否通过都算用户编辑过，交给「应用」决定何时落盘。
            MarkDirty();
        }

        /// <summary>
        /// 让常用格式下拉的选中项跟输入框内容保持一致：命中内置预设就选中它，
        /// 否则选中「自定义格式」占位项，避免下拉框停留在上一条预设上造成误解。
        /// </summary>
        private void UpdateTaskbarFormatComboSelection()
        {
            TimeFormatPreset match = null;
            for (int i = 0; i < _taskbarActivePresets.Count; i++)
            {
                // 按格式串精确比较；占位项格式为 null，自然不会命中空输入以外的内容。
                if (string.Equals(_taskbarActivePresets[i].Format, _taskbarFormatBox.Text, StringComparison.Ordinal))
                {
                    match = _taskbarActivePresets[i];
                    break;
                }
            }

            _updating = true;
            try
            {
                // 设置选中项会触发 SelectionChanged，必须屏蔽回写，否则会反向覆盖输入框。
                _taskbarFormatCombo.SelectedItem = match != null ? (object)match : _taskbarCustomPreset;
            }
            finally
            {
                _updating = false;
            }
        }

        // ===== 换行与键盘 =====

        /// <summary>
        /// 格式输入框按键处理：只接管 Enter。Ctrl+Enter 插入换行，单独 Enter 立即校验并应用预览；
        /// 标记已处理后，TextBox 不会再执行默认换行或蜂鸣。
        /// </summary>
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

        /// <summary>「插入换行」按钮：转调统一的换行插入逻辑。</summary>
        private void OnTaskbarInsertNewline(object sender, RoutedEventArgs e)
        {
            InsertTaskbarFormatNewline();
        }

        /// <summary>
        /// 在光标处插入一个换行：任务栏最多两行，因此已有换行时直接拒绝并提示。
        /// 光标位置无效（尚未获得焦点）时插到末尾，插入后把光标移到换行之后。
        /// </summary>
        private void InsertTaskbarFormatNewline()
        {
            // 任务栏覆盖窗只有两行高度，超过一个换行的格式无法完整显示。
            if (TimeFormatter.CountNewlines(_taskbarFormatBox.Text) >= 1)
            {
                _taskbarFormatStatus.Text = "最多只能插入一个换行。";
                SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.Accent);
                return;
            }

            int caret = _taskbarFormatBox.CaretIndex;
            // 控件尚未获得焦点时 CaretIndex 可能为 -1，回退到末尾插入。
            if (caret < 0)
            {
                caret = _taskbarFormatBox.Text.Length;
            }
            _taskbarFormatBox.Text = _taskbarFormatBox.Text.Insert(caret, "\n");
            // 光标移到换行之后，方便紧接着输入第二行。
            _taskbarFormatBox.CaretIndex = caret + 1;
            HandleTaskbarFormatTextChanged();
        }

        // ===== 辅助方法 =====

        /// <summary>
        /// 创建一个字号档位下拉项：显示文案与倍数原值分离，改文案不影响保存值。
        /// </summary>
        /// <param name="scale">字号倍数原值，取值 100/112/124/136。</param>
        /// <param name="label">下拉里显示的中文档位名。</param>
        /// <returns>带 <see cref="FrameworkElement.Tag"/> 的下拉项。</returns>
        private static ComboBoxItem CreateTaskbarScaleItem(int scale, string label)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Content = label;
            // Tag 存整数原值，取值时按 Tag 判断，避免解析界面文案。
            item.Tag = scale;
            return item;
        }
    }
}
