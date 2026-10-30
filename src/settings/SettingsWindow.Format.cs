// 设置窗口「格式」页签分部（partial SettingsWindow）：全局显示时区、变量说明检索、悬浮窗的格式语言/常用/自定义格式。
// 调用方：SettingsWindow.Shell 构建页签与接线；本页的 On* 处理器只改 _working，不直接保存。
// 边界：任务栏格式在 SettingsWindow.Taskbar.cs；本页只处理悬浮窗 FormatMode/TimeFormat/TokenTimeFormat 与全局 TimeZoneId。
// 不变量：切格式语言时两种格式串分别保存、互不覆盖；时区一律经 TimeZoneCatalog，不直接读 TimeZoneInfo.Local。
// 已知坑：_updating 期间回填控件会再次触发事件，所有处理器入口都要先返回。

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
    // 本分部文件：格式页签的控件构造、时区选择与悬浮窗格式校验；应用/保存链路在 SettingsWindow.Shell.cs。
    internal sealed partial class SettingsWindow
    {
        /// <summary>
        /// 构造「格式」页签根面板：全局显示时区 → 各时钟格式说明 → 变量帮助（可检索）。
        /// </summary>
        private UIElement BuildFormatTab()
        {
            // 本页故意不放具体格式串：悬浮窗与任务栏各自保存格式，统一在各自页签编辑，避免一处改动影响两个位置。
            StackPanel panel = new StackPanel();

            // 时区卡片：标题 + 说明 + 下拉/同步按钮 + 状态行，是全局唯一影响时间计算的控件组。
            Border timeZoneCard = UiTheme.MakeCard();
            StackPanel timeZonePanel = new StackPanel();
            timeZonePanel.Children.Add(UiTheme.MakeSectionTitle("显示时区"));
            // 时区是全局唯一值：时间显示、周数计算与提醒触发时刻都读它，所以只在这一页设置。
            TextBlock timeZoneHint = UiTheme.MakeText(
                "默认使用 UTC+08:00（中国标准时间）。切换后，时间、日期、周数和提醒触发都按所选时区计算。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            timeZoneHint.Margin = new Thickness(0, 0, 0, 8);
            timeZoneHint.TextWrapping = TextWrapping.Wrap;
            timeZonePanel.Children.Add(timeZoneHint);

            Grid timeZoneRow = new Grid();
            // 时区行：下拉占满剩余宽度，右侧固定放「同步系统时区」按钮。
            timeZoneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timeZoneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timeZoneRow.Children.Add(_timeZoneCombo);
            Grid.SetColumn(_timeZoneCombo, 0);

            Button syncButton = new Button();
            // 同步按钮只把系统时区写进编辑副本；落盘与生效仍由「应用」后的控制器完成。
            syncButton.Content = "同步系统时区";
            syncButton.Style = UiTheme.SecondaryButtonStyle;
            syncButton.Padding = new Thickness(12, 5, 12, 5);
            syncButton.Height = 32;
            syncButton.Margin = new Thickness(8, 0, 0, 0);
            syncButton.VerticalAlignment = VerticalAlignment.Center;
            syncButton.ToolTip = "读取 Windows 当前时区并立即切换；点「应用」后保存。";
            syncButton.Click += OnSyncSystemTimeZone;
            AutomationProperties.SetAutomationId(syncButton, "SyncTimeZoneButton");
            timeZoneRow.Children.Add(syncButton);
            Grid.SetColumn(syncButton, 1);

            timeZonePanel.Children.Add(timeZoneRow);
            timeZonePanel.Children.Add(_timeZoneStatus);
            timeZoneCard.Child = timeZonePanel;
            panel.Children.Add(timeZoneCard);

            panel.Children.Add(BuildFormatScopeHint());

            Border helpCard = UiTheme.MakeCard();
            // 帮助卡片：检索框 + 计数 + 可滚动变量表；条目由 FormatHelpCatalog 按当前语言生成。
            StackPanel helpPanel = new StackPanel();
            helpPanel.Children.Add(UiTheme.MakeSectionTitle("格式说明"));
            // 说明检索范围：变量名、说明、示例都能命中，用户不必记住精确写法。
            TextBlock helpHint = UiTheme.MakeText(
                "支持检索：输入变量名、说明或示例关键词。一行一个变量：格式：描述。示例预览：预览。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            helpHint.Margin = new Thickness(0, 0, 0, 8);
            helpHint.TextWrapping = TextWrapping.Wrap;
            helpPanel.Children.Add(helpHint);
            // 顺序固定：检索框 → 计数 → 列表；重建时只替换列表内容与计数。
            helpPanel.Children.Add(_formatHelpSearchBox);
            helpPanel.Children.Add(_formatHelpCount);
            helpPanel.Children.Add(_formatHelpScroll);

            helpCard.Child = helpPanel;
            panel.Children.Add(helpCard);

            return panel;
        }

        /// <summary>
        /// 纯说明卡片：告诉用户格式语言与常用/自定义格式按显示位置各存一份，本页不承载格式编辑。
        /// </summary>
        private UIElement BuildFormatScopeHint()
        {
            // 该卡片只做提示，不含任何可编辑控件：格式串的编辑入口在悬浮窗/任务栏各自页签。
            // 保留它是为了解释「这里为什么没有格式输入框」，减少“找不到设置项”的困惑。
            // 只放文字不放控件：真正的格式编辑在 Taskbar 页与悬浮窗面板，这里是防误改的指引。
            Border card = UiTheme.MakeCard();
            StackPanel panel = new StackPanel();
            panel.Children.Add(UiTheme.MakeSectionTitle("各时钟的显示格式"));
            TextBlock hint = UiTheme.MakeText(
                "桌面悬浮窗与任务栏时钟各自保存格式语言、常用格式与自定义格式，请到对应页签修改。本页只设置全局时区与变量说明。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            hint.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(hint);
            card.Child = panel;
            return card;
        }

        /// <summary>
        /// 悬浮窗格式区（格式语言单选、内置常用格式下拉、自定义格式输入框与状态行）。
        /// 当前主界面未调用它——格式区已由悬浮窗自己的页面承载，保留但不要在此再挂事件。
        /// </summary>
        private UIElement BuildOverlayFormatSection()
        {
            // 三张卡片自上而下：语言 → 常用格式 → 自定义输入；状态行跟随输入框，便于就地看报错。
            StackPanel section = new StackPanel();

            Border presetCard = UiTheme.MakeCard();
            // 卡片一：格式语言单选；两种语言的格式串分别保存在 TokenTimeFormat / TimeFormat。
            StackPanel presetPanel = new StackPanel();
            presetPanel.Children.Add(UiTheme.MakeSectionTitle("格式语言"));
            TextBlock modeHint = UiTheme.MakeText(
                "“兼容 .NET”沿用原有格式；“简洁变量”支持农历、干支、星期和天气变量。两种格式分别保存，切换不会丢失。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            modeHint.Margin = new Thickness(0, 0, 0, 8);
            modeHint.TextWrapping = TextWrapping.Wrap;
            presetPanel.Children.Add(modeHint);
            StackPanel modeRow = new StackPanel();
            // 两个单选同属一组，由 Shell 接线；切换逻辑集中在 OnFormatModeChanged。
            modeRow.Orientation = Orientation.Horizontal;
            modeRow.Children.Add(_dotNetFormatRadio);
            modeRow.Children.Add(_tokenFormatRadio);
            presetPanel.Children.Add(modeRow);
            presetCard.Child = presetPanel;
            section.Children.Add(presetCard);

            Border presetsCard = UiTheme.MakeCard();
            // 卡片二：内置常用格式；选中即填入下方输入框，再走统一校验。
            StackPanel presetsPanel = new StackPanel();
            presetsPanel.Children.Add(UiTheme.MakeSectionTitle("常用格式（内置）"));
            TextBlock presetHint = UiTheme.MakeText(
                "直接选择即可，不需要修改格式。示例使用当前日期时间生成。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            presetHint.Margin = new Thickness(0, 0, 0, 8);
            presetsPanel.Children.Add(presetHint);
            presetsPanel.Children.Add(_formatCombo);
            presetsCard.Child = presetsPanel;
            section.Children.Add(presetsCard);

            Border customCard = UiTheme.MakeCard();
            // 卡片三：自定义格式；提示文本 → 输入框 → 操作行 → 状态行自上而下。
            StackPanel customPanel = new StackPanel();
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义格式"));
            customPanel.Children.Add(_formatHintText);
            customPanel.Children.Add(_formatBox);

            Grid actions = new Grid();
            // 操作行：左侧「插入换行」、右侧弱提示，避免用户以为必须按回车才生效。
            actions.Margin = new Thickness(0, 8, 0, 0);
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button newlineButton = new Button();
            // 与 Ctrl+Enter 等价的第二个入口；「最多一个换行」的规则由 InsertFormatNewline 统一保证。
            newlineButton.Content = "插入换行";
            newlineButton.Style = UiTheme.SecondaryButtonStyle;
            newlineButton.Padding = new Thickness(12, 5, 12, 5);
            newlineButton.Height = 30;
            newlineButton.HorizontalAlignment = HorizontalAlignment.Left;
            newlineButton.Click += OnInsertNewline;
            newlineButton.ToolTip = "格式最多支持一个换行（两行文字）";
            AutomationProperties.SetAutomationId(newlineButton, "InsertNewlineButton");
            actions.Children.Add(newlineButton);
            Grid.SetColumn(newlineButton, 0);

            // 右对齐的弱提示：说明输入即预览，消除「要不要回车」的疑问。
            TextBlock noNeedEnter = UiTheme.MakeText("输入即预览，也可以直接保存。", 11, UiTheme.TextMuted, FontWeights.Normal);
            noNeedEnter.VerticalAlignment = VerticalAlignment.Center;
            noNeedEnter.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Children.Add(noNeedEnter);
            Grid.SetColumn(noNeedEnter, 1);

            customPanel.Children.Add(actions);
            // 状态行紧跟操作行，非法格式的报错就地可见。
            customPanel.Children.Add(_formatStatus);
            customCard.Child = customPanel;
            section.Children.Add(customCard);

            return section;
        }

        /// <summary>
        /// 切换格式语言（.NET / 简洁变量）：两种格式串分别保存，切换时先把当前输入存回旧模式，再载入新模式的格式串。
        /// </summary>
        private void OnFormatModeChanged(object sender, RoutedEventArgs e)
        {
            // 设置 IsChecked 会再次触发 Checked，回填期间必须直接返回。
            if (_updating)
            {
                return;
            }

            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string newMode = tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet;
            // 单选按钮组在设置 IsChecked 时也会触发本处理器，模式没变就直接返回，避免反复标脏。
            if (string.Equals(_working.FormatMode, newMode, StringComparison.Ordinal))
            {
                return;
            }

            // 先把用户在上一个模式里输入的内容存回对应字段，切回来时才不会丢。
            if (FormatEngine.IsTokenMode(_working.FormatMode))
            {
                _working.TokenTimeFormat = _formatBox.Text;
            }
            else
            {
                _working.TimeFormat = _formatBox.Text;
            }
            _working.FormatMode = newMode;
            _formatPresets = tokenMode ? _tokenFormatPresets : _dotNetFormatPresets;

            // 换数据源与文本框内容都会触发 SelectionChanged/TextChanged，必须用 _updating 屏蔽。
            _updating = true;
            try
            {
                _formatCombo.ItemsSource = _formatPresets;
                _formatBox.Text = tokenMode ? _working.TokenTimeFormat : _working.TimeFormat;
            }
            finally
            {
                _updating = false;
            }

            UpdateFormatHelp();
            HandleFormatTextChanged();
            MarkDirty();
        }

        /// <summary>
        /// 按当前格式语言重建变量说明：提示文案、帮助条目与示例统一使用当前时区的时间生成。
        /// </summary>
        private void UpdateFormatHelp()
        {
            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            // 两种语言的「大小写含义」相反（简洁变量 mm 是月份，.NET 的 mm 是分钟），提示必须分别写。
            if (tokenMode)
            {
                _formatHintText.Text =
                    "支持简洁变量（区分大小写；mm 是月份、ff 是分钟）。输入时实时预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            }
            else
            {
                _formatHintText.Text =
                    "支持 .NET 日期格式（MM 是月份、mm 是分钟）。输入时实时预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            }

            // 示例必须用「此刻 + 当前时区」生成，否则用户会在预览里看到与实际显示不一致的日期。
            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            _formatHelpItems = FormatHelpCatalog.Create(
                tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet,
                now,
                CreateSampleWeather());
            // 重建后保留当前检索词：输入搜索时列表不会跳回全量。
            RebuildFormatHelp();
        }

        /// <summary>
        /// 帮助检索框输入：只在已生成的条目列表里过滤，不重新生成条目。
        /// </summary>
        private void OnFormatHelpSearchChanged(object sender, TextChangedEventArgs e)
        {
            // 页签还没构建完时 _formatHelpItems 为 null，此时直接返回，等 UpdateFormatHelp 首次填充。
            if (_formatHelpItems == null)
            {
                return;
            }
            RebuildFormatHelp();
        }

        /// <summary>
        /// 以当前检索词重建帮助行列表，并更新「显示 N / M 个变量」计数。
        /// </summary>
        private void RebuildFormatHelp()
        {
            // 页签未构建完成时面板与条目都可能为空；两者缺一就跳过，避免 NullReferenceException。
            if (_formatHelpPanel == null || _formatHelpItems == null)
            {
                return;
            }

            string query = (_formatHelpSearchBox.Text ?? string.Empty).Trim();
            _formatHelpPanel.Children.Clear();
            _formatHelpScroll.ScrollToTop();

            int shown = 0;
            // 帮助条目只有几十条，线性过滤足够，不必额外建索引。
            for (int i = 0; i < _formatHelpItems.Count; i++)
            {
                FormatHelpItem item = _formatHelpItems[i];
                if (!MatchesFormatHelp(item, query))
                {
                    continue;
                }
                _formatHelpPanel.Children.Add(CreateFormatHelpRow(item));
                shown++;
            }

            // 空结果给出可直接照着输入的检索示例，比只显示「无匹配」更有用。
            if (shown == 0)
            {
                TextBlock empty = UiTheme.MakeText(
                    "没有匹配的变量，试试 yyyy、mm、农历、星期。",
                    11.5,
                    UiTheme.TextMuted,
                    FontWeights.Normal);
                empty.Margin = new Thickness(2, 6, 2, 6);
                _formatHelpPanel.Children.Add(empty);
            }

            _formatHelpCount.Text = "显示 " + shown + " / " + _formatHelpItems.Count + " 个变量";
        }

        /// <summary>
        /// 检索匹配：变量名、说明、示例三者任一命中即显示，全部忽略大小写。
        /// </summary>
        private static bool MatchesFormatHelp(FormatHelpItem item, string query)
        {
            // 空检索词视为全匹配：首次打开就展示全部变量。
            if (string.IsNullOrEmpty(query))
            {
                return true;
            }
            return item.Token.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Example.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 生成一行变量说明：变量名用等宽字体强调，说明与示例弱化，整行 ToolTip 显示完整文本。
        /// </summary>
        private static UIElement CreateFormatHelpRow(FormatHelpItem item)
        {
            TextBlock line = new TextBlock();
            line.FontFamily = new FontFamily("Microsoft YaHei UI");
            line.FontSize = 11.5;
            line.TextWrapping = TextWrapping.NoWrap;
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            line.Margin = new Thickness(2, 3, 2, 3);

            Run token = new Run(item.Token);
            // 变量名用 Consolas + 强调色：等宽字体让 yyyy／MM 这类符号更易逐字符核对。
            token.FontFamily = new FontFamily("Consolas");
            token.FontWeight = FontWeights.SemiBold;
            token.Foreground = UiTheme.Accent;
            line.Inlines.Add(token);
            line.Inlines.Add(new Run("：" + item.Description + "。")
            {
                Foreground = UiTheme.TextSecondary
            });
            line.Inlines.Add(new Run("示例预览：" + item.Example)
            {
                Foreground = UiTheme.TextMuted
            });
            // 单行省略后可能看不全说明，ToolTip 给出完整文本兜底。
            line.ToolTip = item.DisplayText;
            return line;
        }

        /// <summary>
        /// 常用格式下拉选中：把预设格式串填进输入框，再由 HandleFormatTextChanged 校验与预览。
        /// </summary>
        private void OnFormatPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            // 回填下拉选中项也会触发 SelectionChanged，_updating 期间忽略。
            if (_updating)
            {
                return;
            }

            // 「自定义」预设的 Format 为空，选中它只表示当前格式不在内置列表里，不能覆盖用户已输入的格式。
            TimeFormatPreset preset = _formatCombo.SelectedItem as TimeFormatPreset;
            if (preset == null || string.IsNullOrEmpty(preset.Format))
            {
                return;
            }

            _updating = true;
            try
            {
                // 写 Text 会触发 TextChanged；屏蔽后由下面的 HandleFormatTextChanged 统一校验一次。
                _formatBox.Text = preset.Format;
            }
            finally
            {
                _updating = false;
            }
            HandleFormatTextChanged();
        }

        private void OnTimeZoneChanged(object sender, SelectionChangedEventArgs e)
        {
            // 回填时 ItemsSource/SelectedItem 变动也会触发本事件，必须过滤。
            if (_updating)
            {
                return;
            }

            TimeZoneChoice choice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            // 下拉被 ItemsSource 重置时会短暂落到 null，此时保持原时区不动。
            if (choice == null)
            {
                return;
            }

            // 只写编辑副本，落盘与生效由「应用」后的控制器负责。
            _working.TimeZoneId = choice.Id;
            AppLog.Info("Settings", "显示时区改为 " + choice.Id + "（" + TimeZoneCatalog.GetOffsetLabel(choice.Id) + "）。");
            UpdateTimeZoneStatus();
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 「同步系统时区」：把 Windows 当前时区写进编辑副本并回填下拉框；不落盘，等「应用」。
        /// </summary>
        private void OnSyncSystemTimeZone(object sender, RoutedEventArgs e)
        {
            // 只改编辑副本：点「应用」后才真正生效，中途关窗即视为放弃。
            // 统一走 TimeZoneCatalog.GetSystemId()，设置窗口不得直接读 TimeZoneInfo.Local。
            string systemId = TimeZoneCatalog.GetSystemId();
            AppLog.Info("Settings", "同步系统时区：" + systemId + "（" + TimeZoneCatalog.GetOffsetLabel(systemId) + "）。");
            _working.TimeZoneId = systemId;
            SelectTimeZoneChoice(systemId);
            UpdateTimeZoneStatus();
            UpdatePreview();
            MarkDirty();

            // 底部状态栏提示「已同步但未生效」，避免用户误以为已经保存。
            if (_footerStatus != null)
            {
                _footerStatus.Text = "已同步系统时区：" + TimeZoneCatalog.GetOffsetLabel(systemId)
                    + " · " + systemId + "，点「应用」生效。";
            }
        }

        /// <summary>
        /// 刷新时区状态行：与系统时区一致时用弱化色，不一致时用强调色并提示可点「同步系统时区」。
        /// </summary>
        private void UpdateTimeZoneStatus()
        {
            // 状态行只在两处刷新：选中时区变化、以及「同步系统时区」之后。
            // 比较用 OrdinalIgnoreCase：配置里的时区 id 大小写可能与系统返回值不同。
            // 文案分两行：当前偏移 + 时区 id，第二行说明与系统是否一致。
            TimeZoneChoice choice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            if (choice == null)
            {
                return;
            }

            string systemId = TimeZoneCatalog.GetSystemId();
            string current = TimeZoneCatalog.GetOffsetLabel(choice.Id) + " · " + choice.Id;
            // 一致用弱化色、不一致用强调色：颜色本身就是「是否需要处理」的信号。
            if (string.Equals(choice.Id, systemId, StringComparison.OrdinalIgnoreCase))
            {
                _timeZoneStatus.Text = "当前：" + current + "\n已与系统时区一致。";
                _timeZoneStatus.Foreground = UiTheme.TextMuted;
            }
            // 不一致时把系统时区写全，用户才知道点「同步」会切到什么。
            else
            {
                _timeZoneStatus.Text = "当前：" + current
                    + "\n系统时区：" + TimeZoneCatalog.GetOffsetLabel(systemId) + " · " + systemId
                    + "，可点「同步系统时区」。";
                _timeZoneStatus.Foreground = UiTheme.Accent;
            }
        }

        /// <summary>
        /// 按 id 选中下拉项；id 不在目录里（旧配置、被删时区）时退回列表第一项，避免下拉空白。
        /// </summary>
        private void SelectTimeZoneChoice(string id)
        {
            // 先归一化再匹配：持久化里可能存的是别名或旧 ID。
            string normalized = TimeZoneCatalog.Normalize(id);
            TimeZoneChoice match = null;
            for (int i = 0; i < _timeZoneChoices.Count; i++)
            {
                if (string.Equals(_timeZoneChoices[i].Id, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    match = _timeZoneChoices[i];
                    break;
                }
            }
            // 旧配置或系统已移除的时区会匹配不到，退回第一项以保证下拉始终有选中值。
            if (match == null && _timeZoneChoices.Count > 0)
            {
                match = _timeZoneChoices[0];
            }

            bool wasUpdating = _updating;
            _updating = true;
            try
            {
                _timeZoneCombo.SelectedItem = match;
            }
            finally
            {
                _updating = wasUpdating;
            }
            UpdateTimeZoneStatus();
        }

        /// <summary>
        /// 自定义格式输入框的 TextChanged 入口：回填控件期间直接返回，用户输入时交给统一的校验流程。
        /// </summary>
        private void OnFormatTextChanged(object sender, TextChangedEventArgs e)
        {
            // TextChanged 只是入口：选择常用格式、插入换行、手动输入都汇到 HandleFormatTextChanged。
            // 这里不做任何写字段动作，保证「格式串只在 HandleFormatTextChanged 里被写」这一处落点。
            // _updating 期间写 Text 也会触发本事件，必须过滤，否则回填会被当成用户修改并标脏。
            if (_updating)
            {
                return;
            }
            HandleFormatTextChanged();
        }

        /// <summary>
        /// 校验当前格式串：合法则写回编辑副本并刷新预览，非法只更新状态行（不标脏、不改预览）。
        /// 逐字触发，因此这里不写日志——保存链路的校验结果由 Shell.CommitAll 记录。
        /// </summary>
        private void HandleFormatTextChanged()
        {
            // 校验顺序固定：先按当前语言规范化 → 合法才写回并刷新下拉/预览 → 非法只提示。
            // 语言从单选控件实时读取而不是 _working：切语言与改文本可能发生在同一帧。
            // 写回的是规范化结果（统一大小写与空白），保证 _working 里的格式串随时可被引擎解析。
            // 以单选按钮为准而不是 _working.FormatMode：输入过程中两者可能短暂不一致。
            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string normalized;
            string error;
            bool valid = tokenMode
                ? FormatEngine.TryNormalizeTokenFormat(_formatBox.Text, true, out normalized, out error)
                : TimeFormatter.TryNormalizeFormat(_formatBox.Text, out normalized, out error);

            if (valid)
            {
                // 写回的是规范化后的格式串（统一大小写与空白），预览与保存必须用同一份。
                if (tokenMode)
                {
                    _working.TokenTimeFormat = normalized;
                }
                else
                {
                    _working.TimeFormat = normalized;
                }
                _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
                _formatStatus.Foreground = UiTheme.TextMuted;
                UpdatePreview();
            }
            // 非法只提示不改值：不能让人打到一半就被清空，预览也保持上一次合法结果。
            else
            {
                _formatStatus.Text = error;
                _formatStatus.Foreground = UiTheme.Accent;
                _formatStatus.BringIntoView();
            }

            // 每次校验后让下拉跟随文本：命中内置预设就选中，否则落到「自定义」占位项。
            UpdateFormatComboSelection();
            MarkDirty();
        }

        /// <summary>
        /// 让常用格式下拉跟随输入框：命中内置预设就选中它，否则选「自定义」占位项。
        /// </summary>
        private void UpdateFormatComboSelection()
        {
            // 用 Ordinal 全等匹配：格式串区分大小写，忽略大小写会把 mm/MM 混为一谈。
            TimeFormatPreset match = null;
            for (int i = 0; i < _formatPresets.Count; i++)
            {
                if (string.Equals(_formatPresets[i].Format, _formatBox.Text, StringComparison.Ordinal))
                {
                    match = _formatPresets[i];
                    break;
                }
            }

            _updating = true;
            try
            {
                // 改 SelectedItem 会再触发 SelectionChanged；不屏蔽就会与 OnFormatPresetChanged 互相打转。
                _formatCombo.SelectedItem = match != null ? (object)match : _customFormatPreset;
            }
            finally
            {
                _updating = false;
            }
        }

        /// <summary>
        /// 输入框按键：Enter 立即校验预览，Ctrl+Enter 插入一个换行；其余按键不拦截。
        /// </summary>
        private void OnFormatKeyDown(object sender, KeyEventArgs e)
        {
            // 只接管 Enter，其余按键（方向键、退格等）交给 TextBox 正常处理。
            if (e.Key != Key.Enter)
            {
                return;
            }

            // Ctrl+Enter 插入换行，单独 Enter 立即校验预览；两条分支都要吃掉回车，否则会触发默认按钮。
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                InsertFormatNewline();
            }
            else
            {
                HandleFormatTextChanged();
            }
            e.Handled = true;
        }

        /// <summary>
        /// 「插入换行」按钮：与 Ctrl+Enter 走同一条插入逻辑，保证两条入口的规则一致。
        /// </summary>
        private void OnInsertNewline(object sender, RoutedEventArgs e)
        {
            InsertFormatNewline();
        }

        /// <summary>
        /// 在光标处插入一个换行（最多一个）：两层文字是悬浮窗排版的硬上限，两个换行会被渲染层丢弃。
        /// </summary>
        private void InsertFormatNewline()
        {
            // 拒绝分支要给出用户可见原因，否则点了没反应会被当成 bug。
            if (TimeFormatter.CountNewlines(_formatBox.Text) >= 1)
            {
                AppLog.Warn("Settings", "悬浮窗格式插入换行被拒绝：已存在一个换行，当前格式=" + _formatBox.Text + "。");
                _formatStatus.Text = "最多只能插入一个换行。";
                _formatStatus.Foreground = UiTheme.Accent;
                return;
            }

            // 未聚焦时 CaretIndex 可能为 -1，此时退化为追加到末尾。
            int caret = _formatBox.CaretIndex;
            if (caret < 0)
            {
                caret = _formatBox.Text.Length;
            }
            _formatBox.Text = _formatBox.Text.Insert(caret, "\n");
            // 光标停在换行之后，便于继续输入第二行文字。
            _formatBox.CaretIndex = caret + 1;
            HandleFormatTextChanged();
        }
    }
}
