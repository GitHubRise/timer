// 日程编辑器弹框（ScheduleEditorWindow）：类型/标题/日期/时间/提前提醒/每年重复/备注的填写与校验，结果交回日程列表。
// 调用方：SettingsWindow.Schedule 的 OnAddSchedule / EditSchedule 以 ShowDialog 打开，返回 true 时读取 Result。
// 边界：只做输入校验与 ScheduleItem 组装，不直接改 AppSettings.Schedules；加入列表由调用方负责。
// 不变量：编辑时克隆传入对象作底稿，取消不污染列表；Enter 提交、Ctrl+Enter 提交、Esc 取消、备注框内 Enter 换行。
// 已知坑：校验失败只写 _status 并聚焦出错控件，不弹 MessageBox，也不关闭窗口。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GameClock
{
    /// <summary>
    /// 日程编辑器弹框：校验用户输入并组装 <see cref="ScheduleItem"/>，通过 showDialog 返回 true 时由调用方取走 <see cref="Result"/>。
    /// </summary>
    internal sealed class ScheduleEditorWindow : Window
    {
        // 编辑底稿：传入已有日程时先克隆一份，用户取消时原对象完全不受影响。
        private readonly ScheduleItem _existing;
        // 下拉框选项集合在构造时创建一次，之后只读，避免每次打开弹框重新反射枚举。
        private readonly List<ScheduleCategoryOption> _types;
        private readonly List<ScheduleAdvanceOption> _advances;
        // 以下控件在构造期创建、由 BuildContent 挂进可视树，之后不再替换，所以用 readonly 字段。
        private readonly ComboBox _typeCombo;
        private readonly TextBox _titleBox;
        private readonly TextBox _dateBox;
        private readonly TextBox _timeBox;
        private readonly ComboBox _advanceCombo;
        private readonly CheckBox _annualCheck;
        private readonly TextBox _noteBox;
        private readonly TextBlock _status;
        private readonly TextBlock _headerTitle;
        private bool _updating;

        /// <summary>
        /// 构造日程编辑器；existing 为 null 表示新建，否则克隆一份作为编辑底稿，取消时不影响列表中的原对象。
        /// </summary>
        public ScheduleEditorWindow(ScheduleItem existing)
        {
            // 必须 Clone：直接持有列表里的原引用，会让「取消」也把改动留在列表对象上。
            _existing = existing != null ? existing.Clone() : null;
            _types = ScheduleCategories.Create();
            // 选项集合构造期建一次、生命周期内只读；弹框存活时间短，没必要做全局缓存。
            _advances = ScheduleAdvanceOptions.Create();
            // 提前量选项同样只读，单位是分钟，与 ScheduleItem.AdvanceMinutes 直接对应。

            Title = existing == null ? "添加日程" : "编辑日程";
            // 窗口标题与自绘标题栏文案同源：都取决于是否为编辑已有日程。
            Width = 540;
            // 540×620：三行表单 + 76px 备注框在 100% 缩放下刚好不出现滚动条，再矮就要滚。
            Height = 620;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            // 与设置窗口保持一致：保持 false，避免子控件退回软件渲染导致输入框滚动卡顿。
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Topmost = true;
            Background = UiTheme.WindowBackground;
            Foreground = UiTheme.TextPrimary;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            Resources.Add(typeof(ScrollBar), ModernScrollBar.CreateStyle());
            // 只在本窗口替换滚动条样式：不动 App 级资源，避免影响悬浮窗与其它窗口。

            _typeCombo = new ComboBox();
            // 用 SelectedValue 绑定类别规范值，显示文案交给 DisplayMemberPath，判断逻辑不依赖文案。
            UiTheme.ApplyComboBoxStyle(_typeCombo);
            _typeCombo.ItemsSource = _types;
            _typeCombo.DisplayMemberPath = "Label";
            _typeCombo.SelectedValuePath = "Value";
            _typeCombo.SelectedValue = ScheduleCategories.Meeting;
            // 默认「会议」：生日/纪念日这类每年重复的类别通常需要用户主动选择。
            _typeCombo.Height = 32;
            AutomationProperties.SetAutomationId(_typeCombo, "ScheduleDialogType");
            _typeCombo.SelectionChanged += OnTypeChanged;

            _titleBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_titleBox);
            _titleBox.MaxLength = 80;
            // 80 字上限：列表行标题单行显示，再长的部分用户也看不到。
            _titleBox.Height = 32;
            AutomationProperties.SetAutomationId(_titleBox, "ScheduleDialogTitle");

            _dateBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_dateBox);
            _dateBox.Text = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            // 默认今天：绝大多数日程是今天或之后的约定，可省一次改动。
            _dateBox.MaxLength = 10;
            // 限长 10 = "yyyy-MM-dd" 的字符数，多余输入直接进不来，减少一次校验分支。
            _dateBox.Height = 32;
            _dateBox.FontFamily = new FontFamily("Consolas");
            AutomationProperties.SetAutomationId(_dateBox, "ScheduleDialogDate");

            _timeBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_timeBox);
            _timeBox.Text = "09:00";
            // 默认上午 9 点：工作时段内最容易被理解的时间，也和内置提醒示例一致。
            _timeBox.MaxLength = 5;
            _timeBox.Height = 32;
            _timeBox.FontFamily = new FontFamily("Consolas");
            AutomationProperties.SetAutomationId(_timeBox, "ScheduleDialogTime");

            _advanceCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_advanceCombo);
            _advanceCombo.ItemsSource = _advances;
            _advanceCombo.DisplayMemberPath = "Label";
            _advanceCombo.SelectedValuePath = "Minutes";
            _advanceCombo.SelectedValue = 15;
            // 默认提前 15 分钟：与 ScheduleAdvanceOptions 的默认项保持同一个数值。
            _advanceCombo.Height = 32;
            AutomationProperties.SetAutomationId(_advanceCombo, "ScheduleDialogAdvance");

            _annualCheck = new CheckBox();
            // 说明文字直接放进 Content：勾选框自带标签，不另外排「标签 + 控件」两列。
            _annualCheck.Content = "每年重复（生日、纪念日）";
            _annualCheck.Foreground = UiTheme.TextPrimary;
            _annualCheck.FontSize = 12.5;
            _annualCheck.VerticalContentAlignment = VerticalAlignment.Center;
            _annualCheck.Margin = new Thickness(0, 10, 0, 0);
            AutomationProperties.SetAutomationId(_annualCheck, "ScheduleDialogAnnual");

            _noteBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_noteBox);
            _noteBox.MaxLength = 240;
            _noteBox.Height = 76;
            // 76 高约等于 4 行中文备注；再高会把日期/时间行挤出可视区。
            _noteBox.AcceptsReturn = true;
            // 允许回车换行，因此提交不能只靠 IsDefault，还要在 PreviewKeyDown 里判断焦点在哪。
            _noteBox.TextWrapping = TextWrapping.Wrap;
            _noteBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            AutomationProperties.SetAutomationId(_noteBox, "ScheduleDialogNote");

            _status = UiTheme.MakeText("", 11, UiTheme.Accent, FontWeights.Normal);
            // 状态行常驻但初始为空：校验失败才写文案，常驻可避免出错时整块表单往下跳。
            _status.Margin = new Thickness(0, 8, 0, 0);
            _status.TextWrapping = TextWrapping.Wrap;

            _headerTitle = UiTheme.MakeText(
                // 标题文案同时用于窗口标题与自绘标题栏，两处必须一致，所以都取自 existing 判断。
                existing == null ? "添加日程" : "编辑日程",
                15,
                UiTheme.TextPrimary,
                FontWeights.SemiBold);
            _headerTitle.VerticalAlignment = VerticalAlignment.Center;

            Content = BuildContent();

            _updating = true;
            // 回填期间屏蔽联动事件：设置类别会触发 OnTypeChanged 改「每年重复」，必须防止覆盖已有值。
            try
            {
                if (_existing != null)
                {
                    // 编辑：以克隆底稿回填；日期解析失败退回今天，避免脏数据让输入框显示为空。
                    _typeCombo.SelectedValue = ScheduleCategories.Normalize(_existing.Type);
                    _titleBox.Text = _existing.Title;
                    DateTime date;
                    _dateBox.Text = ScheduleService.TryParseDate(_existing.Date, out date)
                        ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    _timeBox.Text = _existing.Time;
                    _advanceCombo.SelectedValue = ScheduleAdvanceOptions.Normalize(_existing.AdvanceMinutes);
                    _annualCheck.IsChecked = _existing.Annual;
                    _noteBox.Text = _existing.Note;
                }
                else
                {
                    _annualCheck.IsChecked = false;
                    // 新建：只显式关掉「每年重复」，其余控件在构造期已给好初始值。
                }
            }
            finally
            {
                _updating = false;
            }

            Loaded += delegate
            {
                // 用 Loaded 而不是构造期聚焦：此时可视树已挂载，Focus/CaretIndex 才会真正生效。
                Activate();
                _titleBox.Focus();
                Keyboard.Focus(_titleBox);
                // 逻辑焦点与键盘焦点一起设：只设一个时输入光标可能停在别的控件上。
                _titleBox.CaretIndex = _titleBox.Text.Length;
                // 光标移到末尾：编辑已有日程时用户多半想追加内容，而不是覆盖整段标题。
            };
            KeyDown += OnWindowKeyDown;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <summary>
        /// 点击「添加/保存」后的结果对象；仅当 DialogResult 为 true 时非空，调用方据此决定是否写回列表。
        /// </summary>
        public ScheduleItem Result { get; private set; }
        // 只有 OnSave 全部校验通过才赋值，与 DialogResult=true 在同一时刻产生，调用方无需二次判空。

        /// <summary>
        /// 构建弹框外观：无边框 + 自绘标题栏（可拖动）、中部可滚动表单、底部按钮区。
        /// 用三行 Grid 而不是 DockPanel，是为了让中部表单独占剩余高度并在内容超长时滚动。
        /// </summary>
        private UIElement BuildContent()
        {
            Grid root = new Grid();
            // 三行：标题栏 Auto / 表单星号（可滚动）/ 按钮区 Auto。
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border header = new Border();
            header.Background = UiTheme.Frozen(0x17, 0x17, 0x1C);
            header.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.Padding = new Thickness(22, 12, 14, 12);
            // 左 22 与滚动区对齐；右 14 是给 30px 关闭按钮留的视觉白边，不是按钮宽度。
            // 标题栏拖动：只有按下左键且在标题区域拖动才移动窗口。
            header.MouseLeftButtonDown += delegate
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException ex)
                {
                    // DragMove 要求触发时左键仍处于按下状态；快速点击或非左键触发会抛异常，
                    // 这种异常不影响使用，记录一条错误日志便于排查「窗口拖不动」的反馈。
                    AppLog.Error("Schedule", "日程编辑器拖动失败：DragMove 未处于左键按下状态。", ex);
                }
            };
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(_headerTitle);

            Button close = new Button();
            close.Content = "×";
            close.Style = UiTheme.GhostButtonStyle;
            close.Width = 30;
            close.Height = 30;
            close.Padding = new Thickness(0);
            close.FontSize = 18;
            close.ToolTip = "关闭";
            // 关闭与取消都只置 DialogResult=false：真正的「不改动」由调用方的 Result 判空保证。
            close.Click += delegate { DialogResult = false; };
            AutomationProperties.SetAutomationId(close, "ScheduleDialogClose");
            headerGrid.Children.Add(close);
            Grid.SetColumn(close, 1);
            header.Child = headerGrid;
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            ScrollViewer scroll = new ScrollViewer();
            // 只允许纵向滚动：横向滚动会让整块表单出现意外的水平位移。
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Padding = new Thickness(22, 18, 22, 12);
            // 左右 22 与标题栏/页脚一致，三块区域的左边界才对得齐。

            Border card = UiTheme.MakeCard();
            // 表单套一层卡片：滚动区底色与卡片底色分层，内容滚动时才看得出边界。
            card.Margin = new Thickness(0);
            StackPanel form = new StackPanel();
            form.Children.Add(UiTheme.MakeSectionTitle("日程信息"));

            Grid rowOne = new Grid();
            // 第一行两列：类型固定 122，标题吃满剩余宽度。
            rowOne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            rowOne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowOne.Children.Add(BuildLabeledField("类型", _typeCombo, new Thickness(0)));
            StackPanel titleField = BuildLabeledField("标题", _titleBox, new Thickness(10, 0, 0, 0));
            rowOne.Children.Add(titleField);
            Grid.SetColumn(titleField, 1);
            form.Children.Add(rowOne);

            Grid rowTwo = new Grid();
            // 第二行三列：日期 152（含 yyyy-MM-dd 提示）/ 时间 86 / 提前量自适应。
            rowTwo.Margin = new Thickness(0, 12, 0, 0);
            rowTwo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(152) });
            rowTwo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            rowTwo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowTwo.Children.Add(BuildLabeledField("日期 yyyy-MM-dd", _dateBox, new Thickness(0)));
            StackPanel timeField = BuildLabeledField("时间 HH:mm", _timeBox, new Thickness(10, 0, 0, 0));
            rowTwo.Children.Add(timeField);
            Grid.SetColumn(timeField, 1);
            StackPanel advanceField = BuildLabeledField("提前提醒", _advanceCombo, new Thickness(10, 0, 0, 0));
            rowTwo.Children.Add(advanceField);
            Grid.SetColumn(advanceField, 2);
            form.Children.Add(rowTwo);

            form.Children.Add(_annualCheck);

            StackPanel noteField = BuildLabeledField("备注（可选）", _noteBox, new Thickness(0, 12, 0, 0));
            // 备注独占一行并紧贴状态行：校验提示出现时离输入点最近。
            form.Children.Add(noteField);
            form.Children.Add(_status);

            card.Child = form;
            scroll.Content = card;
            root.Children.Add(scroll);
            Grid.SetRow(scroll, 1);

            Border footer = new Border();
            footer.Background = UiTheme.Frozen(0x17, 0x17, 0x1C);
            footer.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(22, 12, 22, 12);
            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;

            Button cancel = new Button();
            // 取消用次要按钮、保存用主按钮：靠视觉权重区分默认动作。
            cancel.Content = "取消";
            cancel.Style = UiTheme.SecondaryButtonStyle;
            cancel.Height = 34;
            cancel.MinWidth = 82;
            // 取消与右上角 × 同义：只关闭弹框，不改动调用方持有的原对象。
            cancel.Click += delegate { DialogResult = false; };
            AutomationProperties.SetAutomationId(cancel, "ScheduleDialogCancel");
            buttons.Children.Add(cancel);

            Button save = new Button();
            save.Content = _existing == null ? "添加" : "保存";
            save.Style = UiTheme.PrimaryButtonStyle;
            save.Height = 34;
            save.MinWidth = 90;
            save.Margin = new Thickness(8, 0, 0, 0);
            save.IsDefault = true;
            // IsDefault 与 PreviewKeyDown 互为兜底：焦点不在输入框时回车也能提交。
            save.Click += OnSave;
            AutomationProperties.SetAutomationId(save, "ScheduleDialogSave");
            buttons.Children.Add(save);

            footer.Child = buttons;
            root.Children.Add(footer);
            Grid.SetRow(footer, 2);

            return root;
        }

        /// <summary>
        /// 类别变化时联动「每年重复」：生日/纪念日默认勾选，会议/机票默认取消；
        /// 用户仍可在勾选后手动改，这里只做默认值建议。
        /// </summary>
        private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            // 构造期回填选项也会触发本事件，_updating 期间必须跳过，否则会覆盖用户已填的重复设置。
            if (_updating)
            {
                return;
            }
            ScheduleCategoryOption option = _typeCombo.SelectedItem as ScheduleCategoryOption;
            // 选中项可能为 null（选项集合被重建），此时保持当前勾选状态不动。
            if (option != null)
            {
                _annualCheck.IsChecked = ScheduleCategories.IsAnnual(option.Value);
            }
        }

        /// <summary>
        /// 校验并提交：标题非空、日期能解析为 yyyy-MM-dd、时间能归一化为 HH:mm、
        /// 类别与提前量已选；全部通过后组装 ScheduleItem 并关闭弹框。
        /// 校验失败只写 _status 并把焦点放回出错字段，不弹对话框打断输入。
        /// </summary>
        private void OnSave(object sender, RoutedEventArgs e)
        {
            string title = (_titleBox.Text ?? string.Empty).Trim();
            // 先 Trim 再判空：只输入空格等于没输入标题。
            // 标题是列表与提醒文案的唯一标识，空标题直接拒绝。
            if (title.Length == 0)
            {
                AppLog.Warn("Schedule", "日程保存被拒绝：标题为空。");
                _status.Text = "请填写日程标题。";
                _titleBox.Focus();
                return;
            }

            DateTime date;
            // 日期只接受 yyyy-MM-dd：统一格式才能和触发计算、排序逻辑对上。
            if (!ScheduleService.TryParseDate(_dateBox.Text, out date))
            {
                AppLog.Warn("Schedule", "日程保存被拒绝：日期格式非法，输入=" + (_dateBox.Text ?? string.Empty) + "。");
                _status.Text = "日期格式应为 yyyy-MM-dd，例如 2026-10-30。";
                _dateBox.Focus();
                return;
            }

            string time;
            // 时间格式 HH:mm（24 小时制），归一化后写回模型，避免 "9:00" 这类写法散落到配置里。
            if (!TimeFormatter.TryNormalizeTime(_timeBox.Text, out time))
            {
                AppLog.Warn("Schedule", "日程保存被拒绝：时间格式非法，输入=" + (_timeBox.Text ?? string.Empty) + "。");
                _status.Text = "时间格式应为 HH:mm，例如 09:30。";
                _timeBox.Focus();
                return;
            }

            ScheduleCategoryOption type = _typeCombo.SelectedItem as ScheduleCategoryOption;
            ScheduleAdvanceOption advance = _advanceCombo.SelectedItem as ScheduleAdvanceOption;
            // 两个下拉都有默认值，落到 null 只可能是选项集合被清空，属于异常状态。
            if (type == null || advance == null)
            {
                _status.Text = "请选择日程类型和提前提醒时间。";
                return;
            }

            ScheduleItem result = _existing != null ? _existing.Clone() : new ScheduleItem();
            // 克隆底稿再改字段：保留 Enabled/LastTriggeredKey 等本表单不展示、但必须延续的值。
            result.Type = type.Value;
            result.Title = title;
            result.Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            result.Time = time;
            result.AdvanceMinutes = advance.Minutes;
            result.Annual = _annualCheck.IsChecked == true;
            result.Note = (_noteBox.Text ?? string.Empty).Trim();
            // 编辑时沿用原启用状态，新增一律默认启用：否则从停用日程点进来一保存就悄悄启用了。
            result.Enabled = _existing != null ? _existing.Enabled : true;
            result.LastTriggeredKey = "";
            // 清空已触发标记：日期/时间可能被改过，保存后当天应能重新触发。
            // Normalize 负责兜底默认值与互斥字段（例如一次性日程清空年份含义），返回 false 表示仍然不完整。
            if (!result.Normalize())
            {
                _status.Text = "日程内容不完整，请检查后重试。";
                return;
            }

            // 保存成功也记一条：标题 + 日期时间 + 类别，便于和列表页的「已保存日程」对上同一次操作。
            AppLog.Info("Schedule", "保存日程：" + result.Title + "（" + result.Date + " " + result.Time
                + "，" + ScheduleCategories.DisplayName(result.Type) + (result.Annual ? "，每年重复" : "") + "）。");
            Result = result;
            DialogResult = true;
        }

        /// <summary>
        /// 窗口级快捷键：Esc 取消，Ctrl+Enter 提交；普通 Enter 由 PreviewKeyDown 处理，
        /// 因为备注框内要保留换行能力。
        /// </summary>
        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                OnSave(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        /// <summary>
        /// 回车快捷提交：备注框内回车用于换行，Ctrl/Shift 组合键留给输入法或换行，其余情况按「保存」处理。
        /// 在 Preview 阶段拦截，才能早于 TextBox 的默认回车行为生效。
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 只关心回车，其他按键一律放行。
            if (e.Key != Key.Enter)
            {
                return;
            }
            // 备注是多行文本，回车应当换行而不是提交。
            if (Keyboard.FocusedElement == _noteBox)
            {
                return;
            }
            // 带修饰键的回车交给系统/输入法，避免与快捷键冲突。
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
            {
                return;
            }

            OnSave(sender, new RoutedEventArgs());
            e.Handled = true;
        }

        /// <summary>
        /// 表单里「标签 + 控件」的标准组合：标签 10.5 号次要色、下留 4px，控件撑满列宽。
        /// </summary>
        /// <param name="labelText">标签文案，含格式提示（如「日期 yyyy-MM-dd」）。</param>
        /// <param name="control">被包裹的输入控件。</param>
        /// <param name="margin">外层外边距，由调用方按栅格位置决定。</param>
        private static StackPanel BuildLabeledField(string labelText, FrameworkElement control, Thickness margin)
        {
            StackPanel panel = new StackPanel();
            panel.Margin = margin;
            // 标签与控件竖排；外层 margin 由调用方按栅格位置给出，本方法不猜自己在哪一列。
            TextBlock label = UiTheme.MakeText(labelText, 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            label.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(label);
            panel.Children.Add(control);
            return panel;
        }
    }
}
