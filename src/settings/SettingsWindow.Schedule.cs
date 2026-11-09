// 设置窗口「日程」页签：搜索筛选、日程列表、添加/编辑/删除与启用停用。
// 配置落在 AppSettings.Schedules（List<ScheduleItem>），本页只改构造时克隆的编辑副本
// _working，点「应用」后由主窗口统一提交并持久化；编辑弹框见 ScheduleEditorWindow。
// 列表每次操作后整体重建，不做增量更新，条数上限由 AppSettings.Normalize 收敛到 100。
// 状态文字走 SetScheduleSuccess → SettingsUiHints.SetStatusColor：成功绿、普通灰，
// 关闭灰提示时只保留有色结果；任务栏时钟外观跟随系统任务栏，不在本页签设置。

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
        // ===== 页签布局构建 =====

        /// <summary>
        /// 构建日程页签：顶部工具栏（说明、筛选框、添加按钮、状态行）与下方日程列表卡片。
        /// 控件在构造阶段已由主文件创建，这里只负责组装并补齐页签专属样式。
        /// </summary>
        /// <returns>可直接加入页签容器的根面板。</returns>
        private UIElement BuildScheduleTab()
        {
            // 页签整体纵向排列，两张卡片之间靠卡片自身外边距留白。
            StackPanel panel = new StackPanel();

            // 工具栏卡片：标题、灰色说明、筛选行与状态行。
            Border toolbarCard = UiTheme.MakeCard();
            StackPanel toolbar = new StackPanel();
            toolbar.Children.Add(UiTheme.MakeSectionTitle("日程列表"));
            // 说明由 SettingsUiHints 创建，关闭灰提示时只折叠、不删控件，布局高度随之收缩。
            TextBlock intro = SettingsUiHints.Hint(
                "生日、纪念日、会议、机票等重要约定集中展示。添加或编辑时使用弹框。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            intro.Margin = new Thickness(0, 0, 0, 10);
            intro.TextWrapping = TextWrapping.Wrap;
            toolbar.Children.Add(intro);

            // 筛选行：左列输入区自适应，右列「添加日程」按钮按内容取宽。
            Grid filterRow = new Grid();
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 输入区再分两列：左侧固定 28 px 图标区，右侧输入框。
            StackPanel filterColumn = new StackPanel();
            filterColumn.Margin = new Thickness(0, 0, 12, 0);
            Grid searchBox = new Grid();
            searchBox.Height = 36;
            searchBox.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            searchBox.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 图标宿主只画上、左、下三边，右边框留给输入框，拼起来才像一个完整输入框。
            Border searchIconHost = new Border();
            searchIconHost.Background = UiTheme.FieldBackground;
            searchIconHost.BorderBrush = UiTheme.FieldBorder;
            searchIconHost.BorderThickness = new Thickness(1, 1, 0, 1);
            searchIconHost.CornerRadius = new CornerRadius(7, 0, 0, 7);
            Path searchIcon = SvgIcons.Create(SvgIcons.Search, UiTheme.TextMuted, 15);
            searchIconHost.Child = searchIcon;
            searchBox.Children.Add(searchIconHost);
            Grid.SetColumn(searchIconHost, 0);

            // 筛选框的基础样式已在主文件构造时套好，这里只补拼接边框与内边距。
            _scheduleFilterBox.Background = UiTheme.FieldBackground;
            _scheduleFilterBox.BorderThickness = new Thickness(0, 1, 1, 1);
            _scheduleFilterBox.BorderBrush = UiTheme.FieldBorder;
            _scheduleFilterBox.Padding = new Thickness(7, 0, 7, 0);
            _scheduleFilterBox.FontSize = 13;
            _scheduleFilterBox.VerticalContentAlignment = VerticalAlignment.Center;
            searchBox.Children.Add(_scheduleFilterBox);
            Grid.SetColumn(_scheduleFilterBox, 1);
            filterColumn.Children.Add(searchBox);
            // 筛选说明紧贴输入框下方，告诉用户可以搜哪些字段。
            filterColumn.Children.Add(_scheduleFilterHint);
            filterRow.Children.Add(filterColumn);
            Grid.SetColumn(filterColumn, 0);

            // 「添加日程」按钮：主按钮样式、图标加文字，固定最小宽度防止文字变化时抖动。
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
            // 状态行只占一行，文字与颜色都由 SetScheduleSuccess / 空态逻辑改写。
            toolbar.Children.Add(_scheduleStatus);
            toolbarCard.Child = toolbar;
            panel.Children.Add(toolbarCard);

            // 列表卡片：标题行（标题 + 条数）在上，滚动列表与空态提示在下。
            Border listCard = UiTheme.MakeCard();
            StackPanel listPanel = new StackPanel();
            Grid listHeader = new Grid();
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock listTitle = UiTheme.MakeText("全部日程", 12, UiTheme.TextSecondary, FontWeights.SemiBold);
            listTitle.Margin = new Thickness(0, 0, 0, 8);
            listHeader.Children.Add(listTitle);
            // 条数文本右对齐，筛选时显示「匹配 x / 总数」。
            listHeader.Children.Add(_scheduleCountText);
            Grid.SetColumn(_scheduleCountText, 1);
            listPanel.Children.Add(listHeader);
            // 滚动区限高 430，由主文件设置；行容器在每次重建时整体清空。
            listPanel.Children.Add(_scheduleScroll);
            // 空态提示与列表互斥显示，避免空列表下面再吊一句「没有内容」。
            listPanel.Children.Add(_scheduleEmptyText);
            listCard.Child = listPanel;
            panel.Children.Add(listCard);

            return panel;
        }

        // ===== 事件处理 =====

        /// <summary>
        /// 筛选框文本变化时重建列表。日程最多 100 条，全量重建成本可接受，因此不做防抖。
        /// </summary>
        private void OnScheduleFilterChanged(object sender, TextChangedEventArgs e)
        {
            RebuildScheduleRows();
        }

        /// <summary>
        /// 「添加日程」按钮：弹出新建对话框，确认后把结果加入编辑副本并刷新列表。
        /// 新日程默认启用，避免用户添加完却收不到提醒。
        /// </summary>
        private void OnAddSchedule(object sender, RoutedEventArgs e)
        {
            // 传 null 表示新建；弹框返回的是独立对象，必须显式拷进 _working.Schedules。
            ScheduleEditorWindow dialog = new ScheduleEditorWindow(null);
            dialog.Owner = this;
            // ShowDialog 为 true 且结果非空才算确认，取消或关闭一律不动数据。
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                dialog.Result.Enabled = true;
                _working.Schedules.Add(dialog.Result);
                // 先重建列表再写状态行，保证界面与反馈描述的是同一份数据。
                RebuildScheduleRows();
                SetScheduleSuccess("已添加日程：" + dialog.Result.Title);
                // 只标记脏，不直接落盘：保存时机由主窗口的「应用」统一决定。
                MarkDirty();
            }
        }

        /// <summary>
        /// 编辑指定日程：把对话框结果逐字段写回原对象，保持列表中的引用不变。
        /// 编辑后清空 <c>LastTriggeredKey</c>，让改过日期或时间的日程能重新触发。
        /// </summary>
        /// <param name="item">要编辑的日程；为 null 时直接返回。</param>
        private void EditSchedule(ScheduleItem item)
        {
            // 行控件重建后旧委托仍可能带着旧项触发，这里做一次空引用保护。
            if (item == null)
            {
                return;
            }

            ScheduleEditorWindow dialog = new ScheduleEditorWindow(item);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                // 逐字段覆盖而不是替换对象，避免列表行缓存与提醒服务持有旧引用。
                item.Type = dialog.Result.Type;
                item.Title = dialog.Result.Title;
                item.Date = dialog.Result.Date;
                item.Time = dialog.Result.Time;
                item.AdvanceMinutes = dialog.Result.AdvanceMinutes;
                item.Annual = dialog.Result.Annual;
                item.Note = dialog.Result.Note;
                // 触发键记录「哪一次已经提醒过」，编辑后必须清掉，否则新时间不会再响。
                item.LastTriggeredKey = "";
                RebuildScheduleRows();
                SetScheduleSuccess("已保存日程：" + item.Title);
                MarkDirty();
            }
        }

        /// <summary>删除指定日程；列表与状态行立即刷新，保存仍由「应用」统一提交。</summary>
        /// <param name="item">要删除的日程；为 null 时直接返回。</param>
        private void DeleteSchedule(ScheduleItem item)
        {
            if (item == null)
            {
                return;
            }

            // 删除是立即生效的编辑副本操作，没有二次确认，状态行文案负责说明结果。
            _working.Schedules.Remove(item);
            RebuildScheduleRows();
            SetScheduleSuccess("已删除日程：" + item.Title);
            MarkDirty();
        }

        /// <summary>
        /// 切换指定日程的启用状态。停用只是不再触发提醒，条目仍保留在列表中；
        /// 停用行在 <see cref="CreateScheduleRow"/> 里整体降低不透明度以示区别。
        /// </summary>
        /// <param name="item">要切换的日程；为 null 时直接返回。</param>
        private void ToggleSchedule(ScheduleItem item)
        {
            if (item == null)
            {
                return;
            }

            item.Enabled = !item.Enabled;
            RebuildScheduleRows();
            // 状态文案跟随切换后的状态，用户不必回看行内的电源图标颜色。
            SetScheduleSuccess(item.Enabled
                ? "已启用日程：" + item.Title
                : "已停用日程：" + item.Title);
            MarkDirty();
        }

        // ===== 列表重建与筛选 =====

        /// <summary>
        /// 按当前关键词重建全部日程行：清空旧控件、逐项筛选创建、更新条数与空态。
        /// 每次都整体重建，让行控件与 <see cref="_scheduleRows"/> 始终一一对应。
        /// </summary>
        private void RebuildScheduleRows()
        {
            // 先清控件再清缓存，两处状态必须同步，否则后续按索引取行会错位。
            _scheduleRowsPanel.Children.Clear();
            _scheduleRows.Clear();

            // 关键词只 Trim 一次；空串表示不过滤，与 ScheduleMatches 的约定一致。
            string filter = (_scheduleFilterBox.Text ?? string.Empty).Trim();
            int visible = 0;
            // 用索引遍历而不是 foreach，便于和 _working.Schedules 的顺序保持一致。
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
            // 无关键词时只报总数；有筛选时同时报匹配数，用户能判断过滤是否生效。
            _scheduleCountText.Text = filter.Length == 0
                ? total.ToString(CultureInfo.InvariantCulture) + " 条"
                : "匹配 " + visible.ToString(CultureInfo.InvariantCulture)
                    + " / " + total.ToString(CultureInfo.InvariantCulture) + " 条";

            // 「一条都没有」和「有日程但没匹配」是两种空态，引导文案不同。
            if (total == 0)
            {
                _scheduleEmptyText.Text = "还没有日程。点击右上角“添加日程”创建生日、纪念日、会议或机票提醒。";
            }
            else if (SettingsUiHints.ShowHints && visible == 0)
            {
                _scheduleEmptyText.Text = "没有匹配的日程，换个关键词试试。";
            }

            // 空态文字本身也是灰提示：关闭提示开关时既不显示，也不占高度。
            _scheduleEmptyText.Visibility = SettingsUiHints.ShowHints && visible == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            // 没有可见行时隐藏滚动区，避免留下一条没有内容的滚动槽。
            _scheduleScroll.Visibility = visible == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>
        /// 判断一条日程是否命中关键词：把界面上展示的字段拼成一段文本做不区分大小写的包含匹配。
        /// </summary>
        /// <param name="item">待判断的日程。</param>
        /// <param name="filter">已 Trim 的关键词；空串表示全部命中。</param>
        /// <returns>命中返回 true。</returns>
        private static bool ScheduleMatches(ScheduleItem item, string filter)
        {
            // 空关键词直接放行，省去拼接整段文本的开销。
            if (filter.Length == 0)
            {
                return true;
            }

            // 只拼用户看得见的字段，保证「搜得到」与「看得到」一致。
            string haystack = (item.Title ?? string.Empty)
                + " " + ScheduleCategories.DisplayName(item.Type)
                + " " + (item.Date ?? string.Empty)
                + " " + (item.Time ?? string.Empty)
                + " " + ScheduleAdvanceOptions.DisplayName(item.AdvanceMinutes)
                + " " + (item.Annual ? "每年重复" : "一次性")
                + " " + (item.Enabled ? "启用" : "停用")
                + " " + (item.Note ?? string.Empty);
            // 用当前区域性比较，中文城市名与英文标题都能按本地习惯匹配大小写。
            return haystack.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        // ===== 行控件构建 =====

        /// <summary>
        /// 创建一行日程：电源按钮、类型标签、标题、编辑/删除按钮与详情行。
        /// 返回的行对象同时缓存数据项与容器，重建列表时整批丢弃即可。
        /// </summary>
        /// <param name="item">该行对应的日程数据项。</param>
        /// <returns>行数据与容器控件的组合对象。</returns>
        private ScheduleRow CreateScheduleRow(ScheduleItem item)
        {
            ScheduleRow row = new ScheduleRow(item);

            // 行容器只画下边框做分隔；停用项整体降到 52% 不透明度，仍保持可读。
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

            // 顶部网格四列：电源、类型、标题（占满剩余宽度）、操作按钮。
            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 电源按钮图标不变，靠颜色与提示文字区分当前是启用还是停用。
            Button power = CreateScheduleIconButton(
                SvgIcons.Power,
                item.Enabled ? "停用日程" : "启用日程",
                item.Enabled ? UiTheme.Success : UiTheme.TextMuted,
                delegate { ToggleSchedule(item); });
            power.Margin = new Thickness(0, 0, 8, 0);
            AutomationProperties.SetAutomationId(power, "ScheduleToggleButton");
            top.Children.Add(power);
            Grid.SetColumn(power, 0);

            // 类型标签用强调色，让生日、会议等类别一眼可辨。
            TextBlock category = UiTheme.MakeText(
                ScheduleCategories.DisplayName(item.Type),
                11,
                UiTheme.Accent,
                FontWeights.SemiBold);
            category.VerticalAlignment = VerticalAlignment.Center;
            category.Margin = new Thickness(0, 0, 9, 0);
            top.Children.Add(category);
            Grid.SetColumn(category, 1);

            // 标题单行省略，长标题不会把右侧操作按钮挤出可视区域。
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

            // 编辑按钮用次要文字色，点击后打开同一个编辑弹框。
            Button edit = CreateScheduleIconButton(
                SvgIcons.Pencil,
                "编辑日程",
                UiTheme.TextSecondary,
                delegate { EditSchedule(item); });
            AutomationProperties.SetAutomationId(edit, "ScheduleEditButton");
            actions.Children.Add(edit);

            // 删除按钮用强调色，提示这是不可撤销的操作。
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

            // 详情行左缩进 32 px 与标题起点对齐，同样单行省略。
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

        /// <summary>
        /// 创建行内的小图标按钮：幽灵按钮样式、30×30 命中区、图标加悬停提示。
        /// </summary>
        /// <param name="pathData">SVG 路径数据，来自 <see cref="SvgIcons"/>。</param>
        /// <param name="toolTip">悬停提示，同时说明点击动作。</param>
        /// <param name="iconBrush">图标颜色，随启用状态或操作风险变化。</param>
        /// <param name="click">点击回调，通常绑定到本行数据项的闭包。</param>
        /// <returns>可直接加入行布局的按钮。</returns>
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

        /// <summary>
        /// 生成行内详情文本：日期、时间、提前提醒，再按需追加每年重复、停用与备注摘要。
        /// </summary>
        /// <param name="item">要描述的日程。</param>
        /// <returns>单行可省略的详情字符串。</returns>
        private static string GetScheduleDetails(ScheduleItem item)
        {
            DateTime date;
            string dateText = item.Date;
            // 日期能解析时统一格式化；解析失败就原样显示，避免编辑中的脏数据被吞掉。
            if (ScheduleService.TryParseDate(item.Date, out date))
            {
                dateText = item.Annual
                    ? "每年 " + date.ToString("MM-dd", CultureInfo.InvariantCulture)
                    : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            // 基础信息固定包含日期、时间与提前量。
            string details = dateText + " " + item.Time
                + " · " + ScheduleAdvanceOptions.DisplayName(item.AdvanceMinutes);
            if (item.Annual)
            {
                details += " · 每年重复";
            }
            // 停用状态写进详情，不依赖行透明度这一视觉线索。
            if (!item.Enabled)
            {
                details += " · 已停用";
            }
            if (!string.IsNullOrWhiteSpace(item.Note))
            {
                // 备注截到 36 字，超长部分用省略号收尾，保证单行可读。
                string note = item.Note.Trim();
                if (note.Length > 36)
                {
                    note = note.Substring(0, 36) + "…";
                }
                details += " · " + note;
            }
            return details;
        }

        // ===== 状态行与内部类型 =====

        /// <summary>
        /// 写入日程页状态行并改成成功色。走 <see cref="SettingsUiHints.SetStatusColor"/>，
        /// 关闭灰提示时成功色仍然可见，操作结果不会被一并隐藏。
        /// </summary>
        /// <param name="text">要显示的结果文案。</param>
        private void SetScheduleSuccess(string text)
        {
            _scheduleStatus.Text = text;
            SettingsUiHints.SetStatusColor(_scheduleStatus, UiTheme.Success);
        }

        /// <summary>
        /// 一行日程的渲染结果：数据项加容器控件。列表重建时整批丢弃，不持有跨帧状态。
        /// </summary>
        private sealed class ScheduleRow
        {

            /// <summary>创建行对象并绑定数据项；容器由创建方法随后补上。</summary>
            /// <param name="schedule">该行对应的日程。</param>
            public ScheduleRow(ScheduleItem schedule)
            {
                Schedule = schedule;
            }

            /// <summary>对应的日程数据项，编辑、删除、启停都以它为句柄。</summary>
            public ScheduleItem Schedule { get; private set; }

            /// <summary>行容器；由 <see cref="CreateScheduleRow"/> 赋值，重建列表时整体丢弃。</summary>
            public Border Container { get; set; }
        }
    }
}
