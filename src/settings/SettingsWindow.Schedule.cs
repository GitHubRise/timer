// 设置窗口「日程」页签分部（partial SettingsWindow）：日程列表、关键词过滤、添加/编辑/删除/启停入口。
// 调用方：SettingsWindow.Shell 构建页签与接线；本文件方法由列表行按钮的回调直接调用。
// 边界：日程字段校验与弹框交互在 ScheduleEditorWindow；本文件只维护 _working.Schedules 集合与列表行。
// 不变量：编辑采用「弹框返回新对象 → 逐字段覆盖原对象」，保持列表顺序与对象引用不变。
// 已知坑：日期/时间被改过必须清空 LastTriggeredKey，否则当天改完不会再触发。

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
    // 本分部文件：日程页签的控件构造、过滤与列表增删改；应用/保存链路在 SettingsWindow.Shell.cs。
    internal sealed partial class SettingsWindow
    {
        /// <summary>
        /// 构造「日程」页签根面板：工具条（过滤 + 添加按钮 + 状态行）→ 日程列表。
        /// </summary>
        private UIElement BuildScheduleTab()
        {
            // 状态行 _scheduleStatus 常驻在工具条里，操作反馈直接写在列表上方，不再弹 MessageBox 打断用户。
            StackPanel panel = new StackPanel();
            // 页签只有两张卡片：上面工具条（过滤 + 添加按钮）、下面列表；卡片间距由外层统一给。

            Border toolbarCard = UiTheme.MakeCard();
            // 工具条卡片同时承载状态行 _scheduleStatus：操作反馈就近显示，不用滚到列表底部找。
            StackPanel toolbar = new StackPanel();
            toolbar.Children.Add(UiTheme.MakeSectionTitle("日程列表"));
            // 日程项的增删改都以对话框完成，列表本身只读，避免行内编辑时误改日期。
            TextBlock intro = SettingsUiHints.Hint(
                "生日、纪念日、会议、机票等重要约定集中展示。添加或编辑时使用弹框。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            intro.Margin = new Thickness(0, 0, 0, 10);
            intro.TextWrapping = TextWrapping.Wrap;
            toolbar.Children.Add(intro);

            Grid filterRow = new Grid();
            // 两列：过滤列（星号）吃满剩余宽度，按钮列固定，窗口变宽时按钮不会被推走。
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel filterColumn = new StackPanel();
            // 输入框与提示行上下叠放；提示行常驻（不随输入增减），避免过滤时行高跳动。
            filterColumn.Margin = new Thickness(0, 0, 12, 0);
            Grid searchBox = new Grid();
            // 固定 36 高 + 28 图标列：图标与输入框拼成一根输入条，而不是两个分离的控件。
            searchBox.Height = 36;
            searchBox.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            searchBox.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border searchIconHost = new Border();
            // 只给左半边圆角（7,0,0,7）：与右侧输入框拼成一根完整的圆角输入条，中间不能出现接缝。
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
            // 输入框去掉左边描边：左边界由图标容器提供，否则接缝处会出现双线。
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
            // 主按钮与过滤框同高（36），最小 110 宽：图标 + 四字文案不会被压成两行。
            _scheduleAddButton.Style = UiTheme.PrimaryButtonStyle;
            _scheduleAddButton.Height = 36;
            _scheduleAddButton.MinWidth = 110;
            _scheduleAddButton.VerticalAlignment = VerticalAlignment.Top;
            _scheduleAddButton.Click += OnAddSchedule;
            StackPanel addContent = new StackPanel();
            addContent.Orientation = Orientation.Horizontal;
            addContent.VerticalAlignment = VerticalAlignment.Center;
            Path plus = SvgIcons.Create(SvgIcons.Plus, Brushes.White, 15);
            // 图标固定白色：主按钮底色是强调色，跟随主题的深色图标在深底上几乎看不见。
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
            // 列表卡片里标题行、滚动区、空态文案三者同时存在，靠 Visibility 互斥切换。
            StackPanel listPanel = new StackPanel();
            Grid listHeader = new Grid();
            // 标题左对齐、计数靠右（Auto 列）：计数文案再长也不会挤掉标题。
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

        /// <summary>
        /// 过滤框输入：每次改动都重建列表行（日程数量有限，全量重建比增量维护更不容易出错）。
        /// </summary>
        private void OnScheduleFilterChanged(object sender, TextChangedEventArgs e)
        {
            // 日程量小，逐字重建最省事；这里刻意不记日志，否则每敲一个字都会写一条。
            RebuildScheduleRows();
        }

        /// <summary>
        /// 打开新建日程对话框；确认后把返回的日程加入列表并标脏。
        /// </summary>
        private void OnAddSchedule(object sender, RoutedEventArgs e)
        {
            // 传入 null 表示新建；结果对象由对话框克隆组装，加入列表前强制启用。
            ScheduleEditorWindow dialog = new ScheduleEditorWindow(null);
            // 弹框期间完全不碰 _working：用户取消时列表必须原样不动。
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                dialog.Result.Enabled = true;
                // 新建的一律启用：刚添加就停用不符合直觉，停用只能由用户显式点电源按钮。
                _working.Schedules.Add(dialog.Result);
                RebuildScheduleRows();
                SetScheduleSuccess("已添加日程：" + dialog.Result.Title);
                AppLog.Info("Schedule", "添加日程：" + dialog.Result.Date + " " + dialog.Result.Time + " " + dialog.Result.Title);
                MarkDirty();
            }
        }

        /// <summary>
        /// 打开编辑对话框并把结果逐字段覆盖到列表中的原对象（保持列表顺序与引用不变）。
        /// </summary>
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
                // 逐字段覆盖而不是整对象替换：列表顺序与已有引用保持不变，行内闭包捕获的 item 依然有效。
                item.Title = dialog.Result.Title;
                item.Date = dialog.Result.Date;
                item.Time = dialog.Result.Time;
                item.AdvanceMinutes = dialog.Result.AdvanceMinutes;
                item.Annual = dialog.Result.Annual;
                item.Note = dialog.Result.Note;
                // 关键改动：时间/日期被改过就必须清空已触发标记，否则当天改完不会再触发。
                item.LastTriggeredKey = "";
                RebuildScheduleRows();
                SetScheduleSuccess("已保存日程：" + item.Title);
                AppLog.Info("Schedule", "保存日程：" + item.Date + " " + item.Time + " " + item.Title);
                MarkDirty();
            }
        }

        /// <summary>
        /// 从列表移除日程。这里不弹确认框：未点「应用」前改动都只存在于编辑副本，可直接丢弃。
        /// </summary>
        private void DeleteSchedule(ScheduleItem item)
        {
            if (item == null)
            {
                return;
            }

            _working.Schedules.Remove(item);
            // 按引用移除：ScheduleItem 没有重写 Equals/GetHashCode，引用本身就是身份。
            RebuildScheduleRows();
            SetScheduleSuccess("已删除日程：" + item.Title);
            AppLog.Info("Schedule", "删除日程：" + item.Date + " " + item.Time + " " + item.Title);
            MarkDirty();
        }

        /// <summary>
        /// 切换启用状态：停用的日程在行上降低不透明度，并由触发计算跳过。
        /// </summary>
        private void ToggleSchedule(ScheduleItem item)
        {
            if (item == null)
            {
                return;
            }

            item.Enabled = !item.Enabled;
            // 取反切换：行内只有一个电源按钮，状态不需要额外参数，避免调用方传错。
            RebuildScheduleRows();
            SetScheduleSuccess(item.Enabled
                ? "已启用日程：" + item.Title
                : "已停用日程：" + item.Title);
            AppLog.Info("Schedule", (item.Enabled ? "启用日程：" : "停用日程：") + item.Title);
            MarkDirty();
        }

        /// <summary>
        /// 按过滤词重建日程列表：只渲染匹配项，计数文案区分「全部」与「匹配 x / 总数」，
        /// 并为「还没有日程」和「没有匹配」两种情况分别写占位文案。
        /// </summary>
        private void RebuildScheduleRows()
        {
            // 全量重建：日程条目通常只有几十条，重建比增量维护更不容易出错。
            _scheduleRowsPanel.Children.Clear();
            // 先清控件再清行列表：两个集合必须同步，否则后续同步/计数会读到已丢弃的行。
            _scheduleRows.Clear();

            string filter = (_scheduleFilterBox.Text ?? string.Empty).Trim();
            // 两端空白不计入匹配：粘贴带空格的日期/标题时不该搜不到。
            int visible = 0;
            // visible 只统计真正渲染出来的行，供「匹配 x / 总数」文案使用。
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
            // 总数取原始集合长度：过滤时也要能看出库里到底有多少条。
            _scheduleCountText.Text = filter.Length == 0
            // 有过滤词才显示「匹配 x / 总数」；没有过滤词时只显示总数，避免重复同一个数字。
                ? total.ToString(CultureInfo.InvariantCulture) + " 条"
                : "匹配 " + visible.ToString(CultureInfo.InvariantCulture)
                    + " / " + total.ToString(CultureInfo.InvariantCulture) + " 条";

            // 空库与「无匹配」是两种语境：文案必须分开写，否则用户会以为过滤功能坏了。
            if (total == 0)
            {
                _scheduleEmptyText.Text = "还没有日程。点击右上角“添加日程”创建生日、纪念日、会议或机票提醒。";
            }
            else if (visible == 0)
            {
                _scheduleEmptyText.Text = "没有匹配的日程，换个关键词试试。";
            }

            // 提示总开关关闭时，空态文案同样不显示，只保留列表区域本身。
            _scheduleEmptyText.Visibility = SettingsUiHints.ShowHints && visible == 0
            // 空态文案与滚动区互斥：同时显示会看到一块空白列表框。
                ? Visibility.Visible
                : Visibility.Collapsed;
            _scheduleScroll.Visibility = visible == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>
        /// 判断一条日程是否匹配过滤词：把标题、类别、日期、时间、提前量、重复方式、启停状态、
        /// 备注拼成一段可检索文本，用当前区域性做忽略大小写的包含匹配（中文没有大小写，英文才生效）。
        /// </summary>
        private static bool ScheduleMatches(ScheduleItem item, string filter)
        {
            // 空过滤词等于不过滤，直接放行，省掉一次字符串拼接。
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
                // 把状态词也拼进检索串：用户会直接搜「每年」「停用」这类词。
                + " " + (item.Enabled ? "启用" : "停用")
                + " " + (item.Note ?? string.Empty);
            return haystack.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
            // 用当前区域性忽略大小写：中文没有大小写，只有英文关键词会受影响。
        }

        /// <summary>
        /// 为一条日程创建列表行：启停按钮 + 类别 + 标题 + 编辑/删除按钮，下方一行详情文本。
        /// 行只持有模型引用，按钮的闭包直接调用本分部类的增删改方法。
        /// </summary>
        private ScheduleRow CreateScheduleRow(ScheduleItem item)
        {
            // 行与模型一一对应，模型由 _working.Schedules 持有，行只保存引用。
            ScheduleRow row = new ScheduleRow(item);

            Border container = new Border();
            container.Background = Brushes.Transparent;
            // 行本身透明，只靠 1px 底边分隔：列表更连续，也少一层背景色要跟主题同步。
            container.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            container.BorderThickness = new Thickness(0, 0, 0, 1);
            // 只保留下边框：首行不会多出上边框，末行也不会被外框包住。
            container.Padding = new Thickness(4, 8, 4, 8);
            container.Margin = new Thickness(0);
            // 停用的日程压到 0.52 不透明度：既不隐藏（否则用户找不到），又能一眼分辨状态。
            if (!item.Enabled)
            {
                container.Opacity = 0.52;
            }

            StackPanel stack = new StackPanel();

            Grid top = new Grid();
            // 四列：电源 32 / 类别 Auto / 标题星号 / 操作 Auto，标题是唯一可伸缩的部分。
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Button power = CreateScheduleIconButton(
                // 电源图标颜色随状态变化：启用绿、停用灰，颜色本身就在表达状态。
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
            // 类别与标题之间留 9px：比行内其它间距略大，把「类别」和「标题」分成两组信息。
            top.Children.Add(category);
            Grid.SetColumn(category, 1);

            TextBlock title = UiTheme.MakeText(
                item.Title,
                13,
                UiTheme.TextPrimary,
                FontWeights.SemiBold);
            title.VerticalAlignment = VerticalAlignment.Center;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            // 标题单行截断：长标题宁可出省略号，也不能把右侧操作按钮挤出可视区。
            title.TextWrapping = TextWrapping.NoWrap;
            top.Children.Add(title);
            Grid.SetColumn(title, 2);

            StackPanel actions = new StackPanel();
            // 操作区靠右、左侧留 8px：与标题拉开距离，降低误点概率。
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
            // 删除与编辑只隔 3px：两个图标按钮属于同一组操作，间距小才像一组。
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
            // 详情行左缩进 32px，与上方标题列对齐，形成「标题 → 详情」的二级层次。
            details.TextTrimming = TextTrimming.CharacterEllipsis;
            // 详情同样单行截断：它是辅助信息，撑成两行会破坏列表行高节奏。
            details.TextWrapping = TextWrapping.NoWrap;
            stack.Children.Add(details);

            container.Child = stack;
            row.Container = container;
            return row;
        }

        /// <summary>
        /// 创建 30×30 的图标按钮（Ghost 样式）：日程行的启停/编辑/删除共用，保证三处视觉与命中区域一致。
        /// </summary>
        /// <param name="pathData">SVG path 数据，直接交给 SvgIcons.Create 解析。</param>
        /// <param name="toolTip">悬停提示，同时充当无障碍名称。</param>
        /// <param name="iconBrush">图标颜色，由调用方按语义给出（成功/次要/强调）。</param>
        /// <param name="click">点击处理，通常是带闭包的 RoutedEventHandler。</param>
        private static Button CreateScheduleIconButton(
            string pathData,
            string toolTip,
            Brush iconBrush,
            RoutedEventHandler click)
        {
            Button button = new Button();
            button.Style = UiTheme.GhostButtonStyle;
            button.Width = 30;
            // 30×30：16px 图标 + 7px 内边距的命中区域下限，再小就不方便点中。
            button.Height = 30;
            button.Padding = new Thickness(0);
            button.Content = SvgIcons.Create(pathData, iconBrush, 16);
            button.ToolTip = toolTip;
            button.Click += click;
            return button;
        }

        /// <summary>
        /// 生成行内详情文本：日期 + 时间 + 提前量 + 是否每年重复 + 是否停用 + 截断后的备注。
        /// 日期解析失败时退回原始字符串，避免一条脏数据让整行显示为空。
        /// </summary>
        private static string GetScheduleDetails(ScheduleItem item)
        {
            DateTime date;
            string dateText = item.Date;
            // 能解析才格式化：非法日期保留用户原文，方便定位是配置坏了。
            if (ScheduleService.TryParseDate(item.Date, out date))
            {
                dateText = item.Annual
                    ? "每年 " + date.ToString("MM-dd", CultureInfo.InvariantCulture)
                    : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            string details = dateText + " " + item.Time
                + " · " + ScheduleAdvanceOptions.DisplayName(item.AdvanceMinutes);
            // 每年重复只显示月日：年份对重复日程没有意义。
            if (item.Annual)
            {
                details += " · 每年重复";
            }
            // 停用状态必须显式标出来，否则只靠行透明度容易看漏。
            if (!item.Enabled)
            {
                details += " · 已停用";
            }
            // 备注限 36 字加省略号：详情行只有一行，过长会挤掉前面的时间信息。
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

        /// <summary>
        /// 写日程页的状态行：成功统一用 Success 绿色，失败路径直接写 _scheduleStatus 并置为强调色。
        /// </summary>
        private void SetScheduleSuccess(string text)
        {
            _scheduleStatus.Text = text;
            SettingsUiHints.SetStatusColor(_scheduleStatus, UiTheme.Success);
            // 成功统一用 Success 绿；失败分支由调用方直接写 _scheduleStatus 并置强调色。
        }

        /// <summary>
        /// 一条日程对应的行控件组：模型引用与容器，只在重建列表时使用。
        /// </summary>
        private sealed class ScheduleRow
        {
            public ScheduleRow(ScheduleItem schedule)
            {
                Schedule = schedule;
            }

            public ScheduleItem Schedule { get; private set; }
            // 模型引用只读：换模型等于换行，必须整行重建。
            public Border Container { get; set; }
        }
    }
}
