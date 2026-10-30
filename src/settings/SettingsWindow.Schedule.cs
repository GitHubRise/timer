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
        private UIElement BuildScheduleTab()
        {
            StackPanel panel = new StackPanel();

            Border toolbarCard = UiTheme.MakeCard();
            StackPanel toolbar = new StackPanel();
            toolbar.Children.Add(UiTheme.MakeSectionTitle("日程列表"));
            TextBlock intro = SettingsUiHints.Hint(
                "生日、纪念日、会议、机票等重要约定集中展示。添加或编辑时使用弹框。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            intro.Margin = new Thickness(0, 0, 0, 10);
            intro.TextWrapping = TextWrapping.Wrap;
            toolbar.Children.Add(intro);

            Grid filterRow = new Grid();
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel filterColumn = new StackPanel();
            filterColumn.Margin = new Thickness(0, 0, 12, 0);
            Grid searchBox = new Grid();
            searchBox.Height = 36;
            searchBox.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            searchBox.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border searchIconHost = new Border();
            searchIconHost.Background = UiTheme.FieldBackground;
            searchIconHost.BorderBrush = UiTheme.FieldBorder;
            searchIconHost.BorderThickness = new Thickness(1, 1, 0, 1);
            searchIconHost.CornerRadius = new CornerRadius(7, 0, 0, 7);
            Path searchIcon = SvgIcons.Create(SvgIcons.Search, UiTheme.TextMuted, 15);
            searchIconHost.Child = searchIcon;
            searchBox.Children.Add(searchIconHost);
            Grid.SetColumn(searchIconHost, 0);

            _scheduleFilterBox.Background = UiTheme.FieldBackground;
            _scheduleFilterBox.BorderThickness = new Thickness(0, 1, 1, 1);
            _scheduleFilterBox.BorderBrush = UiTheme.FieldBorder;
            _scheduleFilterBox.Padding = new Thickness(7, 0, 7, 0);
            _scheduleFilterBox.FontSize = 13;
            _scheduleFilterBox.VerticalContentAlignment = VerticalAlignment.Center;
            searchBox.Children.Add(_scheduleFilterBox);
            Grid.SetColumn(_scheduleFilterBox, 1);
            filterColumn.Children.Add(searchBox);
            filterColumn.Children.Add(_scheduleFilterHint);
            filterRow.Children.Add(filterColumn);
            Grid.SetColumn(filterColumn, 0);

            _scheduleAddButton = new Button();
            _scheduleAddButton.Style = UiTheme.PrimaryButtonStyle;
            _scheduleAddButton.Height = 36;
            _scheduleAddButton.MinWidth = 110;
            _scheduleAddButton.VerticalAlignment = VerticalAlignment.Top;
            _scheduleAddButton.Click += OnAddSchedule;
            StackPanel addContent = new StackPanel();
            addContent.Orientation = Orientation.Horizontal;
            addContent.VerticalAlignment = VerticalAlignment.Center;
            Path plus = SvgIcons.Create(SvgIcons.Plus, Brushes.White, 15);
            plus.Margin = new Thickness(0, 0, 5, 0);
            addContent.Children.Add(plus);
            TextBlock addText = UiTheme.MakeText("添加日程", 12.5, Brushes.White, FontWeights.SemiBold);
            addText.VerticalAlignment = VerticalAlignment.Center;
            addContent.Children.Add(addText);
            _scheduleAddButton.Content = addContent;
            AutomationProperties.SetAutomationId(_scheduleAddButton, "AddScheduleButton");
            filterRow.Children.Add(_scheduleAddButton);
            Grid.SetColumn(_scheduleAddButton, 1);
            toolbar.Children.Add(filterRow);
            toolbar.Children.Add(_scheduleStatus);
            toolbarCard.Child = toolbar;
            panel.Children.Add(toolbarCard);

            Border listCard = UiTheme.MakeCard();
            StackPanel listPanel = new StackPanel();
            Grid listHeader = new Grid();
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock listTitle = UiTheme.MakeText("全部日程", 12, UiTheme.TextSecondary, FontWeights.SemiBold);
            listTitle.Margin = new Thickness(0, 0, 0, 8);
            listHeader.Children.Add(listTitle);
            listHeader.Children.Add(_scheduleCountText);
            Grid.SetColumn(_scheduleCountText, 1);
            listPanel.Children.Add(listHeader);
            listPanel.Children.Add(_scheduleScroll);
            listPanel.Children.Add(_scheduleEmptyText);
            listCard.Child = listPanel;
            panel.Children.Add(listCard);

            return panel;
        }

        private void OnScheduleFilterChanged(object sender, TextChangedEventArgs e)
        {
            RebuildScheduleRows();
        }

        private void OnAddSchedule(object sender, RoutedEventArgs e)
        {
            ScheduleEditorWindow dialog = new ScheduleEditorWindow(null);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                dialog.Result.Enabled = true;
                _working.Schedules.Add(dialog.Result);
                RebuildScheduleRows();
                SetScheduleSuccess("已添加日程：" + dialog.Result.Title);
                MarkDirty();
            }
        }

        private void EditSchedule(ScheduleItem item)
        {
            if (item == null)
            {
                return;
            }

            ScheduleEditorWindow dialog = new ScheduleEditorWindow(item);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                item.Type = dialog.Result.Type;
                item.Title = dialog.Result.Title;
                item.Date = dialog.Result.Date;
                item.Time = dialog.Result.Time;
                item.AdvanceMinutes = dialog.Result.AdvanceMinutes;
                item.Annual = dialog.Result.Annual;
                item.Note = dialog.Result.Note;
                item.LastTriggeredKey = "";
                RebuildScheduleRows();
                SetScheduleSuccess("已保存日程：" + item.Title);
                MarkDirty();
            }
        }

        private void DeleteSchedule(ScheduleItem item)
        {
            if (item == null)
            {
                return;
            }

            _working.Schedules.Remove(item);
            RebuildScheduleRows();
            SetScheduleSuccess("已删除日程：" + item.Title);
            MarkDirty();
        }

        private void ToggleSchedule(ScheduleItem item)
        {
            if (item == null)
            {
                return;
            }

            item.Enabled = !item.Enabled;
            RebuildScheduleRows();
            SetScheduleSuccess(item.Enabled
                ? "已启用日程：" + item.Title
                : "已停用日程：" + item.Title);
            MarkDirty();
        }

        private void RebuildScheduleRows()
        {
            _scheduleRowsPanel.Children.Clear();
            _scheduleRows.Clear();

            string filter = (_scheduleFilterBox.Text ?? string.Empty).Trim();
            int visible = 0;
            for (int i = 0; i < _working.Schedules.Count; i++)
            {
                ScheduleItem item = _working.Schedules[i];
                if (!ScheduleMatches(item, filter))
                {
                    continue;
                }

                ScheduleRow row = CreateScheduleRow(item);
                _scheduleRows.Add(row);
                _scheduleRowsPanel.Children.Add(row.Container);
                visible++;
            }

            int total = _working.Schedules.Count;
            _scheduleCountText.Text = filter.Length == 0
                ? total.ToString(CultureInfo.InvariantCulture) + " 条"
                : "匹配 " + visible.ToString(CultureInfo.InvariantCulture)
                    + " / " + total.ToString(CultureInfo.InvariantCulture) + " 条";

            if (total == 0)
            {
                _scheduleEmptyText.Text = "还没有日程。点击右上角“添加日程”创建生日、纪念日、会议或机票提醒。";
            }
            else if (SettingsUiHints.ShowHints && visible == 0)
            {
                _scheduleEmptyText.Text = "没有匹配的日程，换个关键词试试。";
            }

            _scheduleEmptyText.Visibility = SettingsUiHints.ShowHints && visible == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            _scheduleScroll.Visibility = visible == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private static bool ScheduleMatches(ScheduleItem item, string filter)
        {
            if (filter.Length == 0)
            {
                return true;
            }

            string haystack = (item.Title ?? string.Empty)
                + " " + ScheduleCategories.DisplayName(item.Type)
                + " " + (item.Date ?? string.Empty)
                + " " + (item.Time ?? string.Empty)
                + " " + ScheduleAdvanceOptions.DisplayName(item.AdvanceMinutes)
                + " " + (item.Annual ? "每年重复" : "一次性")
                + " " + (item.Enabled ? "启用" : "停用")
                + " " + (item.Note ?? string.Empty);
            return haystack.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private ScheduleRow CreateScheduleRow(ScheduleItem item)
        {
            ScheduleRow row = new ScheduleRow(item);

            Border container = new Border();
            container.Background = Brushes.Transparent;
            container.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            container.BorderThickness = new Thickness(0, 0, 0, 1);
            container.Padding = new Thickness(4, 8, 4, 8);
            container.Margin = new Thickness(0);
            if (!item.Enabled)
            {
                container.Opacity = 0.52;
            }

            StackPanel stack = new StackPanel();

            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Button power = CreateScheduleIconButton(
                SvgIcons.Power,
                item.Enabled ? "停用日程" : "启用日程",
                item.Enabled ? UiTheme.Success : UiTheme.TextMuted,
                delegate { ToggleSchedule(item); });
            power.Margin = new Thickness(0, 0, 8, 0);
            AutomationProperties.SetAutomationId(power, "ScheduleToggleButton");
            top.Children.Add(power);
            Grid.SetColumn(power, 0);

            TextBlock category = UiTheme.MakeText(
                ScheduleCategories.DisplayName(item.Type),
                11,
                UiTheme.Accent,
                FontWeights.SemiBold);
            category.VerticalAlignment = VerticalAlignment.Center;
            category.Margin = new Thickness(0, 0, 9, 0);
            top.Children.Add(category);
            Grid.SetColumn(category, 1);

            TextBlock title = UiTheme.MakeText(
                item.Title,
                13,
                UiTheme.TextPrimary,
                FontWeights.SemiBold);
            title.VerticalAlignment = VerticalAlignment.Center;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.TextWrapping = TextWrapping.NoWrap;
            top.Children.Add(title);
            Grid.SetColumn(title, 2);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.VerticalAlignment = VerticalAlignment.Center;
            actions.Margin = new Thickness(8, 0, 0, 0);

            Button edit = CreateScheduleIconButton(
                SvgIcons.Pencil,
                "编辑日程",
                UiTheme.TextSecondary,
                delegate { EditSchedule(item); });
            AutomationProperties.SetAutomationId(edit, "ScheduleEditButton");
            actions.Children.Add(edit);

            Button delete = CreateScheduleIconButton(
                SvgIcons.Trash,
                "删除日程",
                UiTheme.Accent,
                delegate { DeleteSchedule(item); });
            delete.Margin = new Thickness(3, 0, 0, 0);
            AutomationProperties.SetAutomationId(delete, "ScheduleDeleteButton");
            actions.Children.Add(delete);

            top.Children.Add(actions);
            Grid.SetColumn(actions, 3);
            stack.Children.Add(top);

            TextBlock details = UiTheme.MakeText(
                GetScheduleDetails(item),
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            details.Margin = new Thickness(32, 4, 0, 0);
            details.TextTrimming = TextTrimming.CharacterEllipsis;
            details.TextWrapping = TextWrapping.NoWrap;
            stack.Children.Add(details);

            container.Child = stack;
            row.Container = container;
            return row;
        }

        private static Button CreateScheduleIconButton(
            string pathData,
            string toolTip,
            Brush iconBrush,
            RoutedEventHandler click)
        {
            Button button = new Button();
            button.Style = UiTheme.GhostButtonStyle;
            button.Width = 30;
            button.Height = 30;
            button.Padding = new Thickness(0);
            button.Content = SvgIcons.Create(pathData, iconBrush, 16);
            button.ToolTip = toolTip;
            button.Click += click;
            return button;
        }

        private static string GetScheduleDetails(ScheduleItem item)
        {
            DateTime date;
            string dateText = item.Date;
            if (ScheduleService.TryParseDate(item.Date, out date))
            {
                dateText = item.Annual
                    ? "每年 " + date.ToString("MM-dd", CultureInfo.InvariantCulture)
                    : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            string details = dateText + " " + item.Time
                + " · " + ScheduleAdvanceOptions.DisplayName(item.AdvanceMinutes);
            if (item.Annual)
            {
                details += " · 每年重复";
            }
            if (!item.Enabled)
            {
                details += " · 已停用";
            }
            if (!string.IsNullOrWhiteSpace(item.Note))
            {
                string note = item.Note.Trim();
                if (note.Length > 36)
                {
                    note = note.Substring(0, 36) + "…";
                }
                details += " · " + note;
            }
            return details;
        }

        private void SetScheduleSuccess(string text)
        {
            _scheduleStatus.Text = text;
            SettingsUiHints.SetStatusColor(_scheduleStatus, UiTheme.Success);
        }

        private sealed class ScheduleRow
        {
            public ScheduleRow(ScheduleItem schedule)
            {
                Schedule = schedule;
            }

            public ScheduleItem Schedule { get; private set; }
            public Border Container { get; set; }
        }
    }
}
