// 设置窗口「任务栏」页签分部（partial SettingsWindow）：任务栏时钟开关、独立字体/字号/颜色与格式语言。
// 调用方：SettingsWindow.Shell 构建页签与接线；本文件只写 _working 的 Taskbar* 字段。
// 边界：TaskbarFontFamilyName/TaskbarFontSize/TaskbarColorHex/TaskbarFormatMode/TaskbarFormat/TaskbarDotNetFormat
// 与悬浮窗的字体和格式字段完全隔离，不得复用；覆盖/恢复系统任务栏时钟由 platform/ 与 clock/ 负责，此处不碰任务栏。
// 不变量：TaskbarFormat 始终存简洁变量、TaskbarDotNetFormat 始终存 .NET 格式；切换模式前先把当前输入存回旧模式。

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GameClock
{
    // 本分部文件：任务栏页签的控件构造与格式校验；应用/保存链路在 SettingsWindow.Shell.cs。
    internal sealed partial class SettingsWindow
    {
        /// <summary>
        /// 构造「任务栏」页签根面板：任务栏时钟开关 → 独立字体与颜色 → 格式（语言/常用/自定义）。
        /// </summary>
        private UIElement BuildTaskbarTab()
        {
            // 任务栏与悬浮窗是两套完全独立的显示参数；同名字段带 Taskbar 前缀，切勿在这里读写悬浮窗字段。
            StackPanel panel = new StackPanel();

            // 卡片一：任务栏时钟总开关；真正显示/隐藏由控制器在「应用」后执行。
            // 提示文案把「会占用系统时钟位置」写清楚，避免用户以为系统时钟被弄坏了。
            Border switchCard = UiTheme.MakeCard();
            StackPanel switchPanel = new StackPanel();
            switchPanel.Children.Add(UiTheme.MakeSectionTitle("任务栏时钟"));
            // 真正的覆盖/恢复动作发生在应用设置后由控制器执行，这里只改开关值。
            switchPanel.Children.Add(_taskbarClockCheck);
            TextBlock switchHint = SettingsUiHints.Hint(
                "开启后覆盖系统任务栏的时间区域，并把系统时钟暂移出任务栏；关闭后立即恢复。下方的字体、颜色与格式只作用于任务栏时钟。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            switchHint.TextWrapping = TextWrapping.Wrap;
            switchHint.Margin = new Thickness(24, 3, 0, 0);
            switchPanel.Children.Add(switchHint);
            switchCard.Child = switchPanel;
            panel.Children.Add(switchCard);

            // 卡片二：任务栏专用字体与字号；与悬浮窗字段隔离，改一处不会影响另一处。
            // 任务栏可用高度有限，字号实际上限由 AppSettings.Normalize 钳制，这里只是 UI 提示范围。
            // 字体下拉的选项来自 FontCatalog，与外观页共用同一份候选列表。
            Border fontCard = UiTheme.MakeCard();
            StackPanel fontPanel = new StackPanel();
            fontPanel.Children.Add(UiTheme.MakeSectionTitle("字体与颜色"));
            TextBlock fontHint = SettingsUiHints.Hint(
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

        /// <summary>
        /// 构建「格式语言 / 常用格式 / 自定义格式」三段卡片，只作用于任务栏时钟。
        /// </summary>
        private UIElement BuildTaskbarFormatSection()
        {
            StackPanel section = new StackPanel();

            // 第一段：格式语言单选。切换时由 OnTaskbarFormatModeChanged 把当前输入存回旧语言再换源，两种格式串互不覆盖。
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
            // 两个 RadioButton 由 Shell 的 AttachEvents 接到同一个 OnTaskbarFormatModeChanged，靠 IsChecked 区分方向。
            StackPanel modeRow = new StackPanel();
            modeRow.Orientation = Orientation.Horizontal;
            modeRow.Children.Add(_taskbarDotNetFormatRadio);
            modeRow.Children.Add(_taskbarTokenFormatRadio);
            modePanel.Children.Add(modeRow);
            modeCard.Child = modePanel;
            section.Children.Add(modeCard);

            // 第二段：常用格式下拉。选中项只是把预设串填进下方输入框，真正的校验与落值仍走输入框那条链路。
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

            // 第三段：自定义格式。输入即校验（HandleTaskbarFormatTextChanged），不需要回车确认。
            Border customCard = UiTheme.MakeCard();
            StackPanel customPanel = new StackPanel();
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义格式"));
            customPanel.Children.Add(_taskbarFormatHintText);
            customPanel.Children.Add(_taskbarFormatBox);

            // 操作行：左侧按钮插入换行，右侧一句「输入即更新」的提示，中间留空避免元素贴边。
            Grid actions = new Grid();
            actions.Margin = new Thickness(0, 8, 0, 0);
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 换行按钮：任务栏只能显示两行，因此格式串最多允许一个 \n，第二次插入会被拒绝并提示。
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

        /// <summary>
        /// 构建「颜色」行：色块与 #RRGGBB 输入框。输入逐字生效，不合法时 ColorUtil 退回默认色，界面上不报错。
        /// </summary>
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

            // 提示文案里的 #FFFFFF 是深色任务栏上的常用值，写出来比让用户自己猜十六进制更省事。
            TextBlock hint = SettingsUiHints.Hint("#RRGGBB，例如 #FFFFFF 适合深色任务栏。", 11, UiTheme.TextMuted, FontWeights.Normal);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(hint);
            return row;
        }

        /// <summary>
        /// 任务栏字体选择：只写 TaskbarFontFamilyName，不触碰悬浮窗的 FontFamilyName。
        /// </summary>
        private void OnTaskbarFontChanged(object sender, SelectionChangedEventArgs e)
        {
            // 回填控件时也会触发 SelectionChanged，_updating 期间直接返回，否则加载设置就会被标脏。
            if (_updating)
            {
                return;
            }

            FontChoice font = _taskbarFontCombo.SelectedItem as FontChoice;
            // 下拉被清空或选中占位项时 SelectedItem 不是 FontChoice，保留旧值比写入空串安全。
            if (font != null)
            {
                _working.TaskbarFontFamilyName = font.Name;
            }
            MarkDirty();
        }

        /// <summary>
        /// 任务栏字号滑块：范围 7–40 像素（见 BuildTaskbarTab 的滑块说明），实际显示超宽时由任务栏窗再缩小。
        /// </summary>
        private void OnTaskbarSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 拖动滑块连续触发，只更新副本与预览；不记日志，否则 app.log 会被滑块刷满。
            // 拖动滑块期间会连续触发，这里不记日志，否则一次拖动就能刷满 app.log。
            if (_updating)
            {
                return;
            }

            // 只把取整后的值写进提示文本，字段本身保留 double，避免反复取整造成精度漂移。
            _taskbarSizeValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + " px";
            _working.TaskbarFontSize = e.NewValue;
            MarkDirty();
        }

        /// <summary>
        /// 颜色输入框：逐字解析 #RRGGBB，解析失败时 ColorUtil 回退到默认色，因此这里不需要错误提示。
        /// </summary>
        private void OnTaskbarColorChanged(object sender, TextChangedEventArgs e)
        {
            // 十六进制输入逐字触发；只有解析成功才写回，非法中间态（如 "#FF"）保留旧色不动。
            // 每次按键都会进来，属于高频输入，不记日志。
            if (_updating)
            {
                return;
            }

            Color color = ColorUtil.FromHex(_taskbarColorBox.Text);
            // 用新 Brush 而不是改现有 Brush.Color：冻结画刷（Frozen）不可变，改颜色会抛异常。
            _taskbarColorSwatch.Background = new SolidColorBrush(color);
            MarkDirty();
        }

        /// <summary>
        /// 自定义格式输入框的 TextChanged 入口，实际校验在 <see cref="HandleTaskbarFormatTextChanged"/>。
        /// </summary>
        private void OnTaskbarFormatChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            HandleTaskbarFormatTextChanged();
        }

        /// <summary>
        /// 切换任务栏格式语言（.NET ↔ 简洁变量）：先把当前输入存回旧语言对应的字段，再换数据源与输入框内容。
        /// </summary>
        private void OnTaskbarFormatModeChanged(object sender, RoutedEventArgs e)
        {
            // 切语言会重建常用格式列表（_taskbarActivePresets），再把当前格式填回输入框。
            // 两种语言的格式串分别存在 TaskbarFormat / TaskbarDotNetFormat，切换不会丢掉另一份。
            if (_updating)
            {
                return;
            }

            bool tokenMode = _taskbarTokenFormatRadio.IsChecked == true;
            string newMode = tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet;
            // 同一模式重复触发（两个 RadioButton 的 Checked 都会冒泡到这一个处理器）时只刷新提示，避免误覆盖格式串。
            if (string.Equals(_working.TaskbarFormatMode, newMode, StringComparison.Ordinal))
            {
                UpdateTaskbarFormatHint();
                return;
            }

            // 按「旧」模式决定输入框里的内容归谁：TaskbarFormat 只存简洁变量，TaskbarDotNetFormat 只存 .NET 格式。
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

            // 换 ItemsSource / Text 会触发 SelectionChanged 与 TextChanged，必须用 _updating 挡住，finally 保证异常时也复位。
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

            // 一次模式切换是一条用户可见决策，记一条即可；随后的输入校验不再逐次记录。
            AppLog.Info("Taskbar", "任务栏格式语言切换为 " + (tokenMode ? "简洁变量" : "兼容 .NET") + "。");
            UpdateTaskbarFormatHint();
            HandleTaskbarFormatTextChanged();
            MarkDirty();
        }

        /// <summary>
        /// 按当前格式语言刷新输入框上方的语法提示（mm/MM 的语义在两种语言里正好相反，必须分开展示）。
        /// </summary>
        private void UpdateTaskbarFormatHint()
        {
            bool tokenMode = _taskbarTokenFormatRadio.IsChecked == true;
            _taskbarFormatHintText.Text = tokenMode
                ? "支持简洁变量（区分大小写；mm 是月份、ff 是分钟）。输入时实时更新，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。"
                : "支持 .NET 日期格式（MM 是月份、mm 是分钟）。输入时实时更新，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
        }

        /// <summary>
        /// 从常用格式下拉选中预设：把预设串填进输入框，再交给统一的校验链路。
        /// </summary>
        private void OnTaskbarFormatPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            // 换 ItemsSource 时会先触发一次 SelectedItem=null，这里是必须挡住的噪声。
            if (_updating)
            {
                return;
            }

            TimeFormatPreset preset = _taskbarFormatCombo.SelectedItem as TimeFormatPreset;
            // 「自定义」占位项的 Format 为空，选中它不应该清空输入框，因此直接返回。
            if (preset == null || string.IsNullOrEmpty(preset.Format))
            {
                return;
            }

            // 写 Text 会再触发 TextChanged，用 _updating 挡一次，随后手动走一遍校验，保证只校验一次。
            _updating = true;
            try
            {
                _taskbarFormatBox.Text = preset.Format;
            }
            finally
            {
                _updating = false;
            }
            AppLog.Info("Taskbar", "任务栏选择常用格式：" + preset.Format + "。");
            HandleTaskbarFormatTextChanged();
        }

        /// <summary>
        /// 校验当前输入并写回对应的任务栏格式字段；失败时只更新页内状态文本，保存链路由 Shell.CommitAll 兜底。
        /// </summary>
        private void HandleTaskbarFormatTextChanged()
        {
            bool tokenMode = _taskbarTokenFormatRadio.IsChecked == true;
            string normalized;
            string error;
            // 两种语言用各自的规范化入口：简洁变量额外允许天气/农历变量，.NET 只认传统日期占位符。
            bool valid = tokenMode
                ? FormatEngine.TryNormalizeTokenFormat(_taskbarFormatBox.Text, true, out normalized, out error)
                : TimeFormatter.TryNormalizeFormat(_taskbarFormatBox.Text, out normalized, out error);

            if (valid)
            {
                // 合法才能落值，非法输入保持在编辑副本之外，避免把半截格式串带进预览与持久化。
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
                // 逐字输入都会走到这里，只回写状态文本、不记日志，否则打字过程会把日志刷满。
                _taskbarFormatStatus.Text = error;
                SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.Accent);
            }

            UpdateTaskbarFormatComboSelection();
            MarkDirty();
        }

        /// <summary>
        /// 让常用格式下拉跟随输入框内容：命中预设就选中它，否则选「自定义」占位项。
        /// </summary>
        private void UpdateTaskbarFormatComboSelection()
        {
            TimeFormatPreset match = null;
            // 顺序查找足够：预设数量是个位数，且必须取第一个完全匹配项，避免重复预设互相抢选中态。
            for (int i = 0; i < _taskbarActivePresets.Count; i++)
            {
                if (string.Equals(_taskbarActivePresets[i].Format, _taskbarFormatBox.Text, StringComparison.Ordinal))
                {
                    match = _taskbarActivePresets[i];
                    break;
                }
            }

            // 设置 SelectedItem 会反向触发 OnTaskbarFormatPresetChanged，必须挡住。
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

        /// <summary>
        /// 输入框按键：Enter 立即校验一次，Ctrl+Enter 插入换行（与提示文案保持一致）。
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
            // 必须把 Enter 标记为已处理，否则无边框窗口的默认按钮/光标移动会跟着触发。
            e.Handled = true;
        }

        /// <summary>「插入换行」按钮的点击入口，逻辑集中在 <see cref="InsertTaskbarFormatNewline"/>。</summary>
        private void OnTaskbarInsertNewline(object sender, RoutedEventArgs e)
        {
            InsertTaskbarFormatNewline();
        }

        /// <summary>
        /// 在光标处插入一个换行；任务栏最多两行，已有换行时拒绝并提示（用户可见拒绝，记一条 Warn）。
        /// </summary>
        private void InsertTaskbarFormatNewline()
        {
            // 换行只允许一个：任务栏时钟固定两行高度，多余换行会被裁掉且用户看不见。
            // 上限 1 个 \n：任务栏高度只放得下两行，多出的换行会被任务栏窗裁掉，不如在这里直接拒绝。
            if (TimeFormatter.CountNewlines(_taskbarFormatBox.Text) >= 1)
            {
                _taskbarFormatStatus.Text = "最多只能插入一个换行。";
                SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.Accent);
                AppLog.Warn("Taskbar", "任务栏格式插入换行被拒绝：已存在一个换行，当前格式=" + _taskbarFormatBox.Text + "。");
                return;
            }

            // CaretIndex 在控件未获得焦点时可能是 0 或旧值，负数才回退到末尾，避免把光标位置凭空改掉。
            int caret = _taskbarFormatBox.CaretIndex;
            if (caret < 0)
            {
                caret = _taskbarFormatBox.Text.Length;
            }
            _taskbarFormatBox.Text = _taskbarFormatBox.Text.Insert(caret, "\n");
            _taskbarFormatBox.CaretIndex = caret + 1;
            HandleTaskbarFormatTextChanged();
        }

        /// <summary>
        /// 同步任务栏颜色色块。注意这里读的是 _working.TaskbarColor（由 ColorHex 解析），不是颜色输入框的原文。
        /// </summary>
        private void UpdateTaskbarColorUi()
        {
            // 色块与十六进制文本都从 TaskbarColor 现算，避免两处显示不一致。
            _taskbarColorSwatch.Background = new SolidColorBrush(_working.TaskbarColor);
        }
    }
}
