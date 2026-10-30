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
        private UIElement BuildFormatTab()
        {
            StackPanel panel = new StackPanel();

            Border timeZoneCard = UiTheme.MakeCard();
            StackPanel timeZonePanel = new StackPanel();
            timeZonePanel.Children.Add(UiTheme.MakeSectionTitle("显示时区"));
            TextBlock timeZoneHint = UiTheme.MakeText(
                "默认使用 UTC+08:00（中国标准时间）。切换后，时间、日期、周数和提醒触发都按所选时区计算。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            timeZoneHint.Margin = new Thickness(0, 0, 0, 8);
            timeZoneHint.TextWrapping = TextWrapping.Wrap;
            timeZonePanel.Children.Add(timeZoneHint);

            Grid timeZoneRow = new Grid();
            timeZoneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timeZoneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timeZoneRow.Children.Add(_timeZoneCombo);
            Grid.SetColumn(_timeZoneCombo, 0);

            Button syncButton = new Button();
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
            StackPanel helpPanel = new StackPanel();
            helpPanel.Children.Add(UiTheme.MakeSectionTitle("格式说明"));
            TextBlock helpHint = UiTheme.MakeText(
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

        private UIElement BuildFormatScopeHint()
        {
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

        private UIElement BuildOverlayFormatSection()
        {
            StackPanel section = new StackPanel();

            Border presetCard = UiTheme.MakeCard();
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
            modeRow.Orientation = Orientation.Horizontal;
            modeRow.Children.Add(_dotNetFormatRadio);
            modeRow.Children.Add(_tokenFormatRadio);
            presetPanel.Children.Add(modeRow);
            presetCard.Child = presetPanel;
            section.Children.Add(presetCard);

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
            presetsPanel.Children.Add(_formatCombo);
            presetsCard.Child = presetsPanel;
            section.Children.Add(presetsCard);

            Border customCard = UiTheme.MakeCard();
            StackPanel customPanel = new StackPanel();
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义格式"));
            customPanel.Children.Add(_formatHintText);
            customPanel.Children.Add(_formatBox);

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
            newlineButton.Click += OnInsertNewline;
            newlineButton.ToolTip = "格式最多支持一个换行（两行文字）";
            AutomationProperties.SetAutomationId(newlineButton, "InsertNewlineButton");
            actions.Children.Add(newlineButton);
            Grid.SetColumn(newlineButton, 0);

            TextBlock noNeedEnter = UiTheme.MakeText("输入即预览，也可以直接保存。", 11, UiTheme.TextMuted, FontWeights.Normal);
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

        private void OnFormatModeChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string newMode = tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet;
            if (string.Equals(_working.FormatMode, newMode, StringComparison.Ordinal))
            {
                return;
            }

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

        private void UpdateFormatHelp()
        {
            bool tokenMode = _tokenFormatRadio.IsChecked == true;
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

            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            _formatHelpItems = FormatHelpCatalog.Create(
                tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet,
                now,
                CreateSampleWeather());
            RebuildFormatHelp();
        }

        private void OnFormatHelpSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (_formatHelpItems == null)
            {
                return;
            }
            RebuildFormatHelp();
        }

        private void RebuildFormatHelp()
        {
            if (_formatHelpPanel == null || _formatHelpItems == null)
            {
                return;
            }

            string query = (_formatHelpSearchBox.Text ?? string.Empty).Trim();
            _formatHelpPanel.Children.Clear();
            _formatHelpScroll.ScrollToTop();

            int shown = 0;
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

        private static bool MatchesFormatHelp(FormatHelpItem item, string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return true;
            }
            return item.Token.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Example.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static UIElement CreateFormatHelpRow(FormatHelpItem item)
        {
            TextBlock line = new TextBlock();
            line.FontFamily = new FontFamily("Microsoft YaHei UI");
            line.FontSize = 11.5;
            line.TextWrapping = TextWrapping.NoWrap;
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            line.Margin = new Thickness(2, 3, 2, 3);

            Run token = new Run(item.Token);
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
            line.ToolTip = item.DisplayText;
            return line;
        }

        private void OnFormatPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            TimeFormatPreset preset = _formatCombo.SelectedItem as TimeFormatPreset;
            if (preset == null || string.IsNullOrEmpty(preset.Format))
            {
                return;
            }

            _updating = true;
            try
            {
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
            if (_updating)
            {
                return;
            }

            TimeZoneChoice choice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            if (choice == null)
            {
                return;
            }

            _working.TimeZoneId = choice.Id;
            UpdateTimeZoneStatus();
            UpdatePreview();
            MarkDirty();
        }

        private void OnSyncSystemTimeZone(object sender, RoutedEventArgs e)
        {
            string systemId = TimeZoneCatalog.GetSystemId();
            _working.TimeZoneId = systemId;
            SelectTimeZoneChoice(systemId);
            UpdateTimeZoneStatus();
            UpdatePreview();
            MarkDirty();

            if (_footerStatus != null)
            {
                _footerStatus.Text = "已同步系统时区：" + TimeZoneCatalog.GetOffsetLabel(systemId)
                    + " · " + systemId + "，点「应用」生效。";
            }
        }

        private void UpdateTimeZoneStatus()
        {
            TimeZoneChoice choice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            if (choice == null)
            {
                return;
            }

            string systemId = TimeZoneCatalog.GetSystemId();
            string current = TimeZoneCatalog.GetOffsetLabel(choice.Id) + " · " + choice.Id;
            if (string.Equals(choice.Id, systemId, StringComparison.OrdinalIgnoreCase))
            {
                _timeZoneStatus.Text = "当前：" + current + "\n已与系统时区一致。";
                _timeZoneStatus.Foreground = UiTheme.TextMuted;
            }
            else
            {
                _timeZoneStatus.Text = "当前：" + current
                    + "\n系统时区：" + TimeZoneCatalog.GetOffsetLabel(systemId) + " · " + systemId
                    + "，可点「同步系统时区」。";
                _timeZoneStatus.Foreground = UiTheme.Accent;
            }
        }

        private void SelectTimeZoneChoice(string id)
        {
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

        private void OnFormatTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            HandleFormatTextChanged();
        }

        private void HandleFormatTextChanged()
        {
            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string normalized;
            string error;
            bool valid = tokenMode
                ? FormatEngine.TryNormalizeTokenFormat(_formatBox.Text, true, out normalized, out error)
                : TimeFormatter.TryNormalizeFormat(_formatBox.Text, out normalized, out error);

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
                _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
                _formatStatus.Foreground = UiTheme.TextMuted;
                UpdatePreview();
            }
            else
            {
                _formatStatus.Text = error;
                _formatStatus.Foreground = UiTheme.Accent;
                _formatStatus.BringIntoView();
            }

            UpdateFormatComboSelection();
            MarkDirty();
        }

        private void UpdateFormatComboSelection()
        {
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
                _formatCombo.SelectedItem = match != null ? (object)match : _customFormatPreset;
            }
            finally
            {
                _updating = false;
            }
        }

        private void OnFormatKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

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

        private void OnInsertNewline(object sender, RoutedEventArgs e)
        {
            InsertFormatNewline();
        }

        private void InsertFormatNewline()
        {
            if (TimeFormatter.CountNewlines(_formatBox.Text) >= 1)
            {
                _formatStatus.Text = "最多只能插入一个换行。";
                _formatStatus.Foreground = UiTheme.Accent;
                return;
            }

            int caret = _formatBox.CaretIndex;
            if (caret < 0)
            {
                caret = _formatBox.Text.Length;
            }
            _formatBox.Text = _formatBox.Text.Insert(caret, "\n");
            _formatBox.CaretIndex = caret + 1;
            HandleFormatTextChanged();
        }
    }
}
