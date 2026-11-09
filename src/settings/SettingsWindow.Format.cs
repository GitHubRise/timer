// 设置窗口 · 全局时间格式页签。
// 负责显示时区选择与同步、格式变量帮助检索，以及桌面悬浮窗的格式语言、内置常用格式与自定义格式。
// 时区写回 AppSettings.TimeZoneId；悬浮窗格式写回 FormatMode、TimeFormat、TokenTimeFormat。
// 任务栏时钟的格式在任务栏页签单独维护，本页只提供变量说明与预览入口。
// 约定：灰色说明文字统一由 SettingsUiHints.Hint 创建；状态行改色走 SettingsUiHints.SetStatusColor。

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

    /// <summary>设置窗口的全局时间格式页签：时区、变量帮助，以及悬浮窗格式编辑。</summary>
    internal sealed partial class SettingsWindow
    {

        // ===== 布局构建 =====

        /// <summary>
        /// 构建「时间格式」页签：顶部显示时区卡片，中间格式范围提示，下面是可检索的变量帮助。
        /// 悬浮窗的格式编辑区由 <see cref="BuildOverlayFormatSection"/> 单独构建，插在外观页签中。
        /// </summary>
        private UIElement BuildFormatTab()
        {
            // 整页纵向排列，卡片之间由外边距分隔。
            StackPanel panel = new StackPanel();

            // 第一张卡片：显示时区。默认 UTC+08:00，切换后时间、日期、周数与提醒触发都跟着变。
            Border timeZoneCard = UiTheme.MakeCard();
            StackPanel timeZonePanel = new StackPanel();
            timeZonePanel.Children.Add(UiTheme.MakeSectionTitle("显示时区"));
            // 时区说明用灰色提示，总开关关闭时整块折叠，但下拉框仍然可用。
            TextBlock timeZoneHint = SettingsUiHints.Hint(
                "默认使用 UTC+08:00（中国标准时间）。切换后，时间、日期、周数和提醒触发都按所选时区计算。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            timeZoneHint.Margin = new Thickness(0, 0, 0, 8);
            timeZoneHint.TextWrapping = TextWrapping.Wrap;
            timeZonePanel.Children.Add(timeZoneHint);

            // 一行两列：左侧时区下拉自适应宽度，右侧「同步系统时区」按钮按内容定宽。
            Grid timeZoneRow = new Grid();
            timeZoneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timeZoneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timeZoneRow.Children.Add(_timeZoneCombo);
            Grid.SetColumn(_timeZoneCombo, 0);

            Button syncButton = new Button();
            syncButton.Content = "同步系统时区";
            // 同步按钮只改编辑副本，点「应用」后才真正落盘并生效。
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
            // 状态行根据当前时区与系统时区是否一致切换文案和颜色。
            timeZonePanel.Children.Add(_timeZoneStatus);
            timeZoneCard.Child = timeZonePanel;
            panel.Children.Add(timeZoneCard);

            // 说明各时钟格式分开保存，避免用户在本页找不到格式输入框。
            panel.Children.Add(BuildFormatScopeHint());

            // 第三张卡片：变量帮助，支持按变量名、说明或示例关键词检索。
            Border helpCard = UiTheme.MakeCard();
            StackPanel helpPanel = new StackPanel();
            helpPanel.Children.Add(UiTheme.MakeSectionTitle("格式说明"));
            // 提示里直接给出检索方式与变量的三段结构，减少用户试错。
            TextBlock helpHint = SettingsUiHints.Hint(
                "支持检索：输入变量名、说明或示例关键词。一行一个变量：格式：描述。示例预览：预览。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            helpHint.Margin = new Thickness(0, 0, 0, 8);
            helpHint.TextWrapping = TextWrapping.Wrap;
            helpPanel.Children.Add(helpHint);
            helpPanel.Children.Add(_formatHelpSearchBox);
            helpPanel.Children.Add(_formatHelpCount);
            helpPanel.Children.Add(_formatHelpScroll);

            helpCard.Child = helpPanel;
            panel.Children.Add(helpCard);

            return panel;
        }

        /// <summary>
        /// 构建「各时钟的显示格式」提示卡：说明悬浮窗与任务栏各自保存格式，本页只负责全局时区与变量说明。
        /// </summary>
        private UIElement BuildFormatScopeHint()
        {
            Border card = UiTheme.MakeCard();
            StackPanel panel = new StackPanel();
            panel.Children.Add(UiTheme.MakeSectionTitle("各时钟的显示格式"));
            // 这张卡片只放说明文字，实际编辑入口在悬浮窗与任务栏页签。
            TextBlock hint = SettingsUiHints.Hint(
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
        /// 构建悬浮窗的格式编辑区：格式语言单选、内置常用格式下拉、自定义格式输入与换行按钮。
        /// 该区块由外观页签调用，编辑的是 <c>_working</c> 里的悬浮窗格式字段。
        /// </summary>
        private UIElement BuildOverlayFormatSection()
        {
            StackPanel section = new StackPanel();

            // 第一块：格式语言。两种语言各自保存一份文本，来回切换不会丢内容。
            Border presetCard = UiTheme.MakeCard();
            StackPanel presetPanel = new StackPanel();
            presetPanel.Children.Add(UiTheme.MakeSectionTitle("格式语言"));
            // 灰色说明解释两种格式语言的差异，统一由 SettingsUiHints.Hint 创建。
            TextBlock modeHint = SettingsUiHints.Hint(
                "“兼容 .NET”沿用原有格式；“简洁变量”支持农历、干支、星期和天气变量。两种格式分别保存，切换不会丢失。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            modeHint.Margin = new Thickness(0, 0, 0, 8);
            modeHint.TextWrapping = TextWrapping.Wrap;
            presetPanel.Children.Add(modeHint);
            StackPanel modeRow = new StackPanel();
            modeRow.Orientation = Orientation.Horizontal;
            modeRow.Children.Add(_dotNetFormatRadio);
            modeRow.Children.Add(_tokenFormatRadio);
            presetPanel.Children.Add(modeRow);
            presetCard.Child = presetPanel;
            section.Children.Add(presetCard);

            // 第二块：内置常用格式，选中后把格式串填入下面的输入框。
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
            presetsPanel.Children.Add(_formatCombo);
            presetsCard.Child = presetsPanel;
            section.Children.Add(presetsCard);

            // 第三块：自定义格式输入框，输入即预览；下方按钮用于插入唯一允许的换行。
            Border customCard = UiTheme.MakeCard();
            StackPanel customPanel = new StackPanel();
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义格式"));
            customPanel.Children.Add(_formatHintText);
            customPanel.Children.Add(_formatBox);

            // 左侧放「插入换行」按钮，右侧用灰色提示说明不必额外确认。
            Grid actions = new Grid();
            actions.Margin = new Thickness(0, 8, 0, 0);
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button newlineButton = new Button();
            newlineButton.Content = "插入换行";
            // 换行不用 Enter 直接插入，避免和“Enter 应用预览”的快捷键冲突。
            newlineButton.Style = UiTheme.SecondaryButtonStyle;
            newlineButton.Padding = new Thickness(12, 5, 12, 5);
            newlineButton.Height = 30;
            newlineButton.HorizontalAlignment = HorizontalAlignment.Left;
            newlineButton.Click += OnInsertNewline;
            newlineButton.ToolTip = "格式最多支持一个换行（两行文字）";
            AutomationProperties.SetAutomationId(newlineButton, "InsertNewlineButton");
            actions.Children.Add(newlineButton);
            Grid.SetColumn(newlineButton, 0);

            // 右侧提示靠右对齐，告诉用户输入后已经自动预览。
            TextBlock noNeedEnter = SettingsUiHints.Hint("输入即预览，也可以直接保存。", 11, UiTheme.TextMuted, FontWeights.Normal);
            noNeedEnter.VerticalAlignment = VerticalAlignment.Center;
            noNeedEnter.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Children.Add(noNeedEnter);
            Grid.SetColumn(noNeedEnter, 1);

            customPanel.Children.Add(actions);
            customPanel.Children.Add(_formatStatus);
            customCard.Child = customPanel;
            section.Children.Add(customCard);

            return section;
        }

        // ===== 格式语言与变量帮助 =====

        /// <summary>
        /// 切换格式语言：先把当前输入框内容存回旧语言的字段，再载入新语言已保存的文本。
        /// 两种格式分别保存，因此来回切换不会丢失任何一方的输入。
        /// </summary>
        private void OnFormatModeChanged(object sender, RoutedEventArgs e)
        {
            // 回填单选按钮时会触发 Checked，靠标志位忽略，避免覆盖用户刚做的选择。
            if (_updating)
            {
                return;
            }

            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string newMode = tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet;
            // 新旧模式相同就不做任何事，避免无谓地重建列表和刷新帮助。
            if (string.Equals(_working.FormatMode, newMode, StringComparison.Ordinal))
            {
                return;
            }

            // 按“切换前”的语言决定文本该存到 TokenTimeFormat 还是 TimeFormat。
            if (FormatEngine.IsTokenMode(_working.FormatMode))
            {
                _working.TokenTimeFormat = _formatBox.Text;
            }
            else
            {
                _working.TimeFormat = _formatBox.Text;
            }
            // 记录新语言，并把下拉数据源切到该语言对应的常用格式集合。
            _working.FormatMode = newMode;
            _formatPresets = tokenMode ? _tokenFormatPresets : _dotNetFormatPresets;

            // 整体替换下拉数据源和输入框文本会触发选择/文本事件，用 _updating 包住。
            _updating = true;
            try
            {
                _formatCombo.ItemsSource = _formatPresets;
                _formatBox.Text = tokenMode ? _working.TokenTimeFormat : _working.TimeFormat;
            }
            finally
            {
                // 无论赋值是否抛异常都要复位标志，否则界面会永远忽略用户输入。
                _updating = false;
            }

            // 帮助列表跟着语言切换；HandleFormatTextChanged 负责校验并刷新预览。
            UpdateFormatHelp();
            HandleFormatTextChanged();
            MarkDirty();
        }

        /// <summary>
        /// 按当前格式语言重建变量帮助：提示文案、示例数据与变量清单都随语言变化。
        /// 示例时间取当前显示时区，天气用内置样例，保证离线也能看到效果。
        /// </summary>
        private void UpdateFormatHelp()
        {
            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            // 两种语言的占位符含义相反（mm 在简洁变量里是月份，在 .NET 里是分钟），提示必须分别写。
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

            // 示例时间取当前显示时区，天气用内置样例，保证离线也能看到效果。
            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            _formatHelpItems = FormatHelpCatalog.Create(
                tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet,
                now,
                CreateSampleWeather());
            // 帮助项只在语言或时区变化时重建，搜索时复用同一份数据。
            RebuildFormatHelp();
        }

        /// <summary>变量帮助搜索框变化：仅在帮助数据已初始化时重建列表，避免构造顺序问题。</summary>
        private void OnFormatHelpSearchChanged(object sender, TextChangedEventArgs e)
        {
            // 帮助数据尚未初始化时直接返回，避免分部类构造顺序导致空引用。
            if (_formatHelpItems == null)
            {
                return;
            }
            // 输入为空也走重建，等于恢复显示全部变量。
            RebuildFormatHelp();
        }

        /// <summary>
        /// 按搜索词过滤变量帮助并重建列表；无匹配时显示一条引导提示，同时更新“显示 x / y”计数。
        /// </summary>
        private void RebuildFormatHelp()
        {
            // 面板或数据还没准备好时直接返回，避免构造阶段触发空引用。
            if (_formatHelpPanel == null || _formatHelpItems == null)
            {
                return;
            }

            // 搜索词统一去首尾空格，空串视为显示全部。
            string query = (_formatHelpSearchBox.Text ?? string.Empty).Trim();
            // 变量总数只有几十条，直接清空重建比增量更新更简单可靠。
            _formatHelpPanel.Children.Clear();
            // 每输入一个字符都会重建，先滚回顶部，避免停留在上一次的滚动位置。
            _formatHelpScroll.ScrollToTop();

            int shown = 0;
            // 逐条过滤，命中的才创建行并计入“显示 x / y”。
            for (int i = 0; i < _formatHelpItems.Count; i++)
            {
                FormatHelpItem item = _formatHelpItems[i];
                // 变量名、说明、示例任一命中即显示。
                if (!MatchesFormatHelp(item, query))
                {
                    continue;
                }
                _formatHelpPanel.Children.Add(CreateFormatHelpRow(item));
                shown++;
            }

            // 空结果给一条可操作的提示，而不是留白让用户以为界面坏了。
            if (shown == 0)
            {
                TextBlock empty = SettingsUiHints.Hint(
                    "没有匹配的变量，试试 yyyy、mm、农历、星期。",
                    11.5,
                    UiTheme.TextMuted,
                    FontWeights.Normal);
                empty.Margin = new Thickness(2, 6, 2, 6);
                _formatHelpPanel.Children.Add(empty);
            }

            // 计数放在列表下方，方便判断是过滤掉了还是本来就没有变量。
            _formatHelpCount.Text = "显示 " + shown + " / " + _formatHelpItems.Count + " 个变量";
        }

        /// <summary>
        /// 判断一条变量帮助是否命中搜索词：变量名、说明、示例三项任一包含即算匹配，忽略大小写。
        /// </summary>
        /// <param name="item">待检查的帮助项。</param>
        /// <param name="query">已去除首尾空白的搜索词；为空表示全部匹配。</param>
        /// <returns>命中返回 true。</returns>
        private static bool MatchesFormatHelp(FormatHelpItem item, string query)
        {
            // 空搜索词等价于“显示全部”，不必再做三次字符串查找。
            if (string.IsNullOrEmpty(query))
            {
                return true;
            }
            return item.Token.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Example.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 创建一行变量帮助：变量名用等宽字体和强调色，后面跟说明与示例预览，整行悬停显示完整文本。
        /// </summary>
        /// <param name="item">要展示的帮助项。</param>
        /// <returns>可直接加入帮助面板的文本行。</returns>
        private static UIElement CreateFormatHelpRow(FormatHelpItem item)
        {
            TextBlock line = new TextBlock();
            // 固定不换行并用省略号截断，长说明不会把帮助面板撑出横向滚动。
            line.FontFamily = new FontFamily("Microsoft YaHei UI");
            line.FontSize = 11.5;
            line.TextWrapping = TextWrapping.NoWrap;
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            line.Margin = new Thickness(2, 3, 2, 3);

            // 变量名单独用一个 Run 着色，方便在密集文字里快速定位。
            Run token = new Run(item.Token);
            token.FontFamily = new FontFamily("Consolas");
            token.FontWeight = FontWeights.SemiBold;
            token.Foreground = UiTheme.Accent;
            line.Inlines.Add(token);
            line.Inlines.Add(new Run("：" + item.Description + "。")
            {
                Foreground = UiTheme.TextSecondary
            });
            // 说明与示例用不同灰度区分层级，示例是生成后的实际文本。
            line.Inlines.Add(new Run("示例预览：" + item.Example)
            {
                Foreground = UiTheme.TextMuted
            });
            line.ToolTip = item.DisplayText;
            return line;
        }

        // ===== 时区与格式输入 =====

        /// <summary>
        /// 选中内置常用格式：把格式串填入输入框并立即校验预览；回填期间的选择变化直接忽略。
        /// </summary>
        private void OnFormatPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            // 回填下拉选中项时会再次触发 SelectionChanged，靠标志位挡住。
            if (_updating)
            {
                return;
            }

            TimeFormatPreset preset = _formatCombo.SelectedItem as TimeFormatPreset;
            // “自定义”占位项没有格式串，选中它时保持输入框内容不变。
            if (preset == null || string.IsNullOrEmpty(preset.Format))
            {
                return;
            }

            // 改文本会触发 TextChanged，这里临时置位避免重复走一遍校验。
            _updating = true;
            try
            {
                _formatBox.Text = preset.Format;
            }
            finally
            {
                // 即使赋值抛出异常也要复位标志，保持界面可编辑。
                _updating = false;
            }
            // 统一走 HandleFormatTextChanged，保证下拉选择与手动输入得到同样的预览和状态提示。
            HandleFormatTextChanged();
        }

        /// <summary>
        /// 时区下拉变化：把时区 ID 写回编辑副本，并刷新状态行与预览；真正生效要等点「应用」。
        /// </summary>
        private void OnTimeZoneChanged(object sender, SelectionChangedEventArgs e)
        {
            // 回填下拉值时忽略事件，避免把初始化当成用户切换。
            if (_updating)
            {
                return;
            }

            TimeZoneChoice choice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            // 未选中任何项时保持原时区，防止清空下拉时写入空 ID。
            if (choice == null)
            {
                return;
            }

            _working.TimeZoneId = choice.Id;
            UpdateTimeZoneStatus();
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 「同步系统时区」：读取 Windows 当前时区，选中对应下拉项并刷新状态与预览。
        /// 只改编辑副本，页脚会提示仍需点「应用」才生效。
        /// </summary>
        private void OnSyncSystemTimeZone(object sender, RoutedEventArgs e)
        {
            // 统一通过 TimeZoneCatalog.GetSystemId 读取，设置窗口不直接访问 TimeZoneInfo.Local。
            string systemId = TimeZoneCatalog.GetSystemId();
            _working.TimeZoneId = systemId;
            // 系统时区可能在列表里没有同名项，SelectTimeZoneChoice 内部会规范化并兜底。
            SelectTimeZoneChoice(systemId);
            UpdateTimeZoneStatus();
            UpdatePreview();
            MarkDirty();

            // 页脚状态栏可能尚未创建（分部类构造顺序），判空后再写文案。
            if (_footerStatus != null)
            {
                _footerStatus.Text = "已同步系统时区：" + TimeZoneCatalog.GetOffsetLabel(systemId)
                    + " · " + systemId + "，点「应用」生效。";
            }
        }

        /// <summary>
        /// 刷新时区状态行：与系统时区一致时用灰色说明，不一致时用强调色提示可以一键同步。
        /// 颜色通过 SettingsUiHints.SetStatusColor 设置，保证提示总开关关闭时引导语自动隐藏。
        /// </summary>
        private void UpdateTimeZoneStatus()
        {
            TimeZoneChoice choice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            // 用户可能还没选时区，直接返回，避免空引用。
            if (choice == null)
            {
                return;
            }

            string systemId = TimeZoneCatalog.GetSystemId();
            string current = TimeZoneCatalog.GetOffsetLabel(choice.Id) + " · " + choice.Id;
            // 一致时只陈述事实，并用灰色引导语。
            if (string.Equals(choice.Id, systemId, StringComparison.OrdinalIgnoreCase))
            {
                _timeZoneStatus.Text = "当前：" + current + "\n已与系统时区一致。";
                SettingsUiHints.SetStatusColor(_timeZoneStatus, UiTheme.TextMuted);
            }
            else
            {
                // 不一致时把系统时区也列出来，方便用户判断要不要同步。
                _timeZoneStatus.Text = "当前：" + current
                    + "\n系统时区：" + TimeZoneCatalog.GetOffsetLabel(systemId) + " · " + systemId
                    + "，可点「同步系统时区」。";
                // 强调色表示这里存在可操作的不一致，SetStatusColor 会让它始终可见。
                SettingsUiHints.SetStatusColor(_timeZoneStatus, UiTheme.Accent);
            }
        }

        /// <summary>
        /// 按 ID 选中时区下拉项：先规范化 ID，找不到时退回列表第一项，回填过程不触发用户事件。
        /// </summary>
        /// <param name="id">要选中的时区 ID，可带不同大小写或别名。</param>
        private void SelectTimeZoneChoice(string id)
        {
            string normalized = TimeZoneCatalog.Normalize(id);
            TimeZoneChoice match = null;
            // 下拉项按规范化后的 ID 比较，忽略大小写，兼容旧配置里的别名。
            for (int i = 0; i < _timeZoneChoices.Count; i++)
            {
                if (string.Equals(_timeZoneChoices[i].Id, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    match = _timeZoneChoices[i];
                    break;
                }
            }
            // 找不到对应项时退回第一项，至少保证下拉框有可见选择，不留空白。
            if (match == null && _timeZoneChoices.Count > 0)
            {
                match = _timeZoneChoices[0];
            }

            // 保存并恢复原来的 _updating，避免外层事件处理期间标志被冲掉。
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
            // 选中项变化后刷新状态行；是否 MarkDirty 由调用方决定。
            UpdateTimeZoneStatus();
        }

        /// <summary>自定义格式输入框文本变化：转交统一的校验/预览流程处理。</summary>
        private void OnFormatTextChanged(object sender, TextChangedEventArgs e)
        {
            // 回填输入框时忽略，避免把程序赋值当成用户编辑。
            if (_updating)
            {
                return;
            }
            HandleFormatTextChanged();
        }

        /// <summary>
        /// 校验当前格式文本：合法就规范化后写回对应字段并刷新预览，非法就把错误显示在状态行。
        /// 无论合法与否都会同步常用格式下拉的选中项，并标记有未保存修改。
        /// </summary>
        private void HandleFormatTextChanged()
        {
            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string normalized;
            string error;
            // 两种格式语言用各自的规范化函数，失败时 error 是可读的中文原因。
            bool valid = tokenMode
                ? FormatEngine.TryNormalizeTokenFormat(_formatBox.Text, true, out normalized, out error)
                : TimeFormatter.TryNormalizeFormat(_formatBox.Text, out normalized, out error);

            // 只有校验通过才写回，避免把半截输入或非法格式存进配置。
            if (valid)
            {
                if (tokenMode)
                {
                    _working.TokenTimeFormat = normalized;
                }
                else
                {
                    _working.TimeFormat = normalized;
                }
                // 合法时状态行回到灰色引导语，颜色交给 SetStatusColor 统一处理显隐。
                _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
                SettingsUiHints.SetStatusColor(_formatStatus, UiTheme.TextMuted);
                UpdatePreview();
            }
            else
            {
                // 非法时滚动到状态行，长页面下用户也能立刻看到错误原因。
                _formatStatus.Text = error;
                SettingsUiHints.SetStatusColor(_formatStatus, UiTheme.Accent);
                _formatStatus.BringIntoView();
            }

            // 输入可能正好等于某个内置格式，让下拉框跟着高亮；没有匹配则显示“自定义”。
            UpdateFormatComboSelection();
            MarkDirty();
        }

        /// <summary>
        /// 让常用格式下拉框反映当前输入：文本与某个内置格式完全相同时选中它，否则选中“自定义”占位项。
        /// </summary>
        private void UpdateFormatComboSelection()
        {
            TimeFormatPreset match = null;
            // 用序数比较，格式串区分大小写（MM 与 mm 含义不同）。
            for (int i = 0; i < _formatPresets.Count; i++)
            {
                if (string.Equals(_formatPresets[i].Format, _formatBox.Text, StringComparison.Ordinal))
                {
                    match = _formatPresets[i];
                    break;
                }
            }

            // 改选中项会触发 SelectionChanged，用 _updating 包住避免再次回填输入框。
            _updating = true;
            try
            {
                _formatCombo.SelectedItem = match != null ? (object)match : _customFormatPreset;
            }
            finally
            {
                _updating = false;
            }
        }

        /// <summary>
        /// 格式输入框按键：Enter 应用预览，Ctrl+Enter 插入换行；其他按键交给输入框默认处理。
        /// </summary>
        private void OnFormatKeyDown(object sender, KeyEventArgs e)
        {
            // 只拦截 Enter，方向键、退格等编辑键必须保持原生行为。
            if (e.Key != Key.Enter)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Ctrl+Enter 插入换行而不是提交，因为格式最多允许两行。
                InsertFormatNewline();
            }
            else
            {
                HandleFormatTextChanged();
            }
            // 标记已处理，避免 Enter 再触发默认的换行或按钮点击。
            e.Handled = true;
        }

        /// <summary>「插入换行」按钮：转交 <see cref="InsertFormatNewline"/> 处理。</summary>
        private void OnInsertNewline(object sender, RoutedEventArgs e)
        {
            InsertFormatNewline();
        }

        /// <summary>
        /// 在光标处插入一个换行；格式最多允许一个换行，超过时只在状态行提示，不修改文本。
        /// </summary>
        private void InsertFormatNewline()
        {
            // 先数现有换行，已有一个就拒绝，保持“最多两行”的约定。
            if (TimeFormatter.CountNewlines(_formatBox.Text) >= 1)
            {
                _formatStatus.Text = "最多只能插入一个换行。";
                SettingsUiHints.SetStatusColor(_formatStatus, UiTheme.Accent);
                return;
            }

            int caret = _formatBox.CaretIndex;
            // 光标位置异常（例如输入框尚未获得焦点）时退到末尾插入。
            if (caret < 0)
            {
                caret = _formatBox.Text.Length;
            }
            _formatBox.Text = _formatBox.Text.Insert(caret, "\n");
            // 插入后把光标移到换行之后，方便继续输入第二行。
            _formatBox.CaretIndex = caret + 1;
            // 显式走一次校验与预览，让状态行和预览立即反映新文本。
            HandleFormatTextChanged();
        }
    }
}
