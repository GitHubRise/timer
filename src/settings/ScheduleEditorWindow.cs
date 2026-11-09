// 日程「添加 / 编辑」对话框：模态窗口，校验通过后把结果写进 Result 并关闭返回 DialogResult = true。
// 调用方是设置窗口的日程页签：new ScheduleEditorWindow(existing) → ShowDialog() → 确认时读 Result 写入设置副本。
// 构造期一次性建好全部控件与布局（本项目不用 XAML），Loaded 时把焦点放到标题框，关闭后实例即废弃。
// 校验线：标题非空 → 日期 yyyy-MM-dd → 时间 HH:mm → 类型 / 提前提醒已选 → Normalize，失败只在页内提示、不关窗。
// 约束：编辑既有条目时只操作 Clone 副本，取消或校验失败都不会污染调用方传入的 ScheduleItem。

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
    /// 日程编辑对话框：类型、标题、日期、时间、提前提醒、年度重复与备注的表单，外加页内校验。
    /// 生命周期由调用方以 <see cref="Window.ShowDialog"/> 持有，关闭即弃、不常驻；确认时经 <see cref="Result"/> 交出对象。
    /// 取消、Esc、右上角关闭按钮统一走 <c>DialogResult = false</c>，调用方据此区分“取消”与“确认”。
    /// </summary>
    internal sealed class ScheduleEditorWindow : Window
    {
        // ===== 输入源与编辑对象 =====

        /// <summary>传入条目的副本；null 表示新建，编辑时靠它保留 Enabled 等表单不展示的字段。</summary>
        private readonly ScheduleItem _existing;

        /// <summary>类型下拉的数据源（会议、生日等）；构造时生成一次，运行期不再变化。</summary>
        private readonly List<ScheduleCategoryOption> _types;

        /// <summary>提前提醒下拉的数据源（分钟数选项）；取值用 Minutes，显示用 Label。</summary>
        private readonly List<ScheduleAdvanceOption> _advances;

        // ===== 表单控件 =====

        /// <summary>类型下拉框；切换类型时按分类规则联动年度重复勾选。</summary>
        private readonly ComboBox _typeCombo;

        /// <summary>标题输入框；最多 80 字，保存时 Trim 后要求非空。</summary>
        private readonly TextBox _titleBox;

        /// <summary>日期输入框；固定 yyyy-MM-dd 文本，用等宽字体便于对齐核对。</summary>
        private readonly TextBox _dateBox;

        /// <summary>时间输入框；固定 HH:mm 文本，交给 TimeFormatter 规范化。</summary>
        private readonly TextBox _timeBox;

        /// <summary>提前提醒下拉框；取值是提前的分钟数，默认 15 分钟。</summary>
        private readonly ComboBox _advanceCombo;

        /// <summary>年度重复勾选框；生日、纪念日类默认勾上，仍允许用户手工改。</summary>
        private readonly CheckBox _annualCheck;

        /// <summary>备注输入框；多行、可滚动，最多 240 字，保存时 Trim。</summary>
        private readonly TextBox _noteBox;

        /// <summary>页内状态行；只显示校验失败原因，成功时不写文字（确认后窗口即关）。</summary>
        private readonly TextBlock _status;

        /// <summary>标题栏文字；与窗口标题一致，由是否传入条目决定“添加”还是“编辑”。</summary>
        private readonly TextBlock _headerTitle;

        // ===== 状态标志 =====

        /// <summary>
        /// 回填抑制标志：构造期把既有条目写进控件会触发 SelectionChanged，
        /// 此时置 true 让联动逻辑跳过，避免初始化时反改用户的年度重复设置。
        /// </summary>
        private bool _updating;

        /// <summary>
        /// 创建对话框并完成布局。构造期不读全局设置、不落盘，只依据传入条目决定初始值；
        /// 窗口本体是模态、居中于所有者、不显示在任务栏。
        /// </summary>
        /// <param name="existing">要编辑的条目；传 null 表示新建。</param>
        public ScheduleEditorWindow(ScheduleItem existing)
        {
            // 只保留副本：后续改动都落在副本上，用户取消时原对象不受影响。
            _existing = existing != null ? existing.Clone() : null;
            _types = ScheduleCategories.Create();
            _advances = ScheduleAdvanceOptions.Create();

            // ===== 窗口外观：模态、无边框、居中于所有者 =====
            Title = existing == null ? "添加日程" : "编辑日程";
            Width = 540;
            Height = 620;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Topmost = true;
            Background = UiTheme.WindowBackground;
            Foreground = UiTheme.TextPrimary;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            // 窗口级覆盖滚动条样式：对话框内的 ScrollViewer 都会命中这条隐式样式。
            Resources.Add(typeof(ScrollBar), ModernScrollBar.CreateStyle());

            // ===== 类型下拉：分类是日程的主维度，放在表单第一行 =====
            _typeCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_typeCombo);
            _typeCombo.ItemsSource = _types;
            _typeCombo.DisplayMemberPath = "Label";
            _typeCombo.SelectedValuePath = "Value";
            _typeCombo.SelectedValue = ScheduleCategories.Meeting;
            _typeCombo.Height = 32;
            // 自动化 ID 供 UI 自动化脚本按下标定位，改名会破坏外部脚本。
            AutomationProperties.SetAutomationId(_typeCombo, "ScheduleDialogType");
            _typeCombo.SelectionChanged += OnTypeChanged;

            // ===== 标题输入：唯一的必填文本字段 =====
            _titleBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_titleBox);
            _titleBox.MaxLength = 80;
            _titleBox.Height = 32;
            AutomationProperties.SetAutomationId(_titleBox, "ScheduleDialogTitle");

            // ===== 日期输入：默认今天，宽 152 恰好容纳 yyyy-MM-dd 与标签 =====
            _dateBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_dateBox);
            _dateBox.Text = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _dateBox.MaxLength = 10;
            _dateBox.Height = 32;
            // 等宽字体让日期数字逐位对齐，输错时更容易一眼看出。
            _dateBox.FontFamily = new FontFamily("Consolas");
            AutomationProperties.SetAutomationId(_dateBox, "ScheduleDialogDate");

            // ===== 时间输入：默认上午九点，长度限制 5 位正好 HH:mm =====
            _timeBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_timeBox);
            _timeBox.Text = "09:00";
            _timeBox.MaxLength = 5;
            _timeBox.Height = 32;
            _timeBox.FontFamily = new FontFamily("Consolas");
            AutomationProperties.SetAutomationId(_timeBox, "ScheduleDialogTime");

            // ===== 提前提醒：与时间同行的窄下拉 =====
            _advanceCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_advanceCombo);
            _advanceCombo.ItemsSource = _advances;
            _advanceCombo.DisplayMemberPath = "Label";
            _advanceCombo.SelectedValuePath = "Minutes";
            _advanceCombo.SelectedValue = 15;
            _advanceCombo.Height = 32;
            AutomationProperties.SetAutomationId(_advanceCombo, "ScheduleDialogAdvance");

            // ===== 年度重复：独立一行，文案自带例子降低理解成本 =====
            _annualCheck = new CheckBox();
            _annualCheck.Content = "每年重复（生日、纪念日）";
            _annualCheck.Foreground = UiTheme.TextPrimary;
            _annualCheck.FontSize = 12.5;
            _annualCheck.VerticalContentAlignment = VerticalAlignment.Center;
            _annualCheck.Margin = new Thickness(0, 10, 0, 0);
            AutomationProperties.SetAutomationId(_annualCheck, "ScheduleDialogAnnual");

            // ===== 备注：多行文本，高度约三行，超出后出现滚动条 =====
            _noteBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_noteBox);
            _noteBox.MaxLength = 240;
            _noteBox.Height = 76;
            _noteBox.AcceptsReturn = true;
            _noteBox.TextWrapping = TextWrapping.Wrap;
            _noteBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            AutomationProperties.SetAutomationId(_noteBox, "ScheduleDialogNote");

            // ===== 状态行与标题栏文字 =====
            // 状态行默认空串，靠 Save 时填错误原因；用强调色保证在暗色底上够醒目。
            _status = UiTheme.MakeText("", 11, UiTheme.Accent, FontWeights.Normal);
            _status.Margin = new Thickness(0, 8, 0, 0);
            _status.TextWrapping = TextWrapping.Wrap;

            _headerTitle = UiTheme.MakeText(
                existing == null ? "添加日程" : "编辑日程",
                15,
                UiTheme.TextPrimary,
                FontWeights.SemiBold);
            _headerTitle.VerticalAlignment = VerticalAlignment.Center;

            // 布局必须在回填之前建好：控件已加入可视树，回填时的属性变化才会正确测量。
            Content = BuildContent();

            // ===== 回填既有条目：期间屏蔽联动，失败也要复位标志 =====
            _updating = true;
            try
            {
                if (_existing != null)
                {
                    // 逐字段回填；类型与提前提醒先做归一化，防旧配置里有已下线的取值。
                    _typeCombo.SelectedValue = ScheduleCategories.Normalize(_existing.Type);
                    _titleBox.Text = _existing.Title;
                    DateTime date;
                    // 旧条目日期可能已损坏，解析失败就退回今天，保证输入框里始终是合法格式。
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
                    // 新建时年度重复默认不勾，由类型联动决定是否自动勾上。
                    _annualCheck.IsChecked = false;
                }
            }
            finally
            {
                // 无论回填是否异常都必须复位，否则后续手动切换类型永远不会联动。
                _updating = false;
            }

            /// <summary>首次显示时激活窗口并把光标定位到标题末尾，方便直接继续输入。</summary>
            Loaded += delegate
            {
                Activate();
                _titleBox.Focus();
                Keyboard.Focus(_titleBox);
                // 光标放到末尾而不是全选，避免用户一按键就覆盖掉原标题。
                _titleBox.CaretIndex = _titleBox.Text.Length;
            };
            // 两个键盘钩子分工：KeyDown 处理 Esc 与 Ctrl+Enter，PreviewKeyDown 拦普通 Enter。
            KeyDown += OnWindowKeyDown;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <summary>确认时交给调用方的日程对象；仅在 <c>DialogResult = true</c> 后才有值。</summary>
        public ScheduleItem Result { get; private set; }

        /// <summary>
        /// 构建整窗布局：三行网格 = 固定标题栏 / 可滚动表单区 / 固定按钮栏。
        /// 表单区用 <see cref="ScrollViewer"/> 包裹，窗口高度固定而字段较多时仍能完整访问。
        /// </summary>
        /// <returns>可直接赋给 <see cref="Window.Content"/> 的根元素。</returns>
        private UIElement BuildContent()
        {
            // 三行：中间一行 Star，窗口尺寸固定时把多余高度全给表单区。
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ===== 标题栏：兼作无边框窗口的拖动区 =====
            Border header = new Border();
            header.Background = UiTheme.Frozen(0x17, 0x17, 0x1C);
            header.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.Padding = new Thickness(22, 12, 14, 12);

            /// <summary>在标题栏按下左键即拖动整个窗口，替代系统标题栏。</summary>
            header.MouseLeftButtonDown += delegate
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // 鼠标已提前松开时 DragMove 会抛此异常；拖动没发生也无副作用，可以安全吞掉。
                }
            };
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(_headerTitle);

            // 关闭按钮：幽灵样式，图标字号放大，宽度固定成方形热区。
            Button close = new Button();
            close.Content = "×";
            close.Style = UiTheme.GhostButtonStyle;
            close.Width = 30;
            close.Height = 30;
            close.Padding = new Thickness(0);
            close.FontSize = 18;
            close.ToolTip = "关闭";

            /// <summary>关闭按钮等同取消，不保存任何输入。</summary>
            close.Click += delegate { DialogResult = false; };
            AutomationProperties.SetAutomationId(close, "ScheduleDialogClose");
            headerGrid.Children.Add(close);
            Grid.SetColumn(close, 1);
            header.Child = headerGrid;
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            // ===== 表单区：滚动容器内套一张圆角卡片 =====
            ScrollViewer scroll = new ScrollViewer();
            // 只允许纵向滚动；横向禁用，避免表单被压窄后出现横向滚动条。
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Padding = new Thickness(22, 18, 22, 12);

            Border card = UiTheme.MakeCard();
            // 卡片外边距清零：留白交给外层 ScrollViewer 的 Padding 统一控制。
            card.Margin = new Thickness(0);
            StackPanel form = new StackPanel();
            form.Children.Add(UiTheme.MakeSectionTitle("日程信息"));

            // 第一行：类型固定 122 宽，标题吃掉剩余宽度。
            Grid rowOne = new Grid();
            rowOne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            rowOne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowOne.Children.Add(BuildLabeledField("类型", _typeCombo, new Thickness(0)));
            StackPanel titleField = BuildLabeledField("标题", _titleBox, new Thickness(10, 0, 0, 0));
            rowOne.Children.Add(titleField);
            Grid.SetColumn(titleField, 1);
            form.Children.Add(rowOne);

            // 第二行：日期 / 时间 / 提前提醒三列，标签文字自带格式示例。
            Grid rowTwo = new Grid();
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

            // 年度重复自带上边距，夹在两行字段之间不需要额外容器。
            form.Children.Add(_annualCheck);

            // 备注与状态行：状态行放在表单末尾，错误提示紧邻输入区而非按钮栏。
            StackPanel noteField = BuildLabeledField("备注（可选）", _noteBox, new Thickness(0, 12, 0, 0));
            form.Children.Add(noteField);
            form.Children.Add(_status);

            card.Child = form;
            scroll.Content = card;
            root.Children.Add(scroll);
            Grid.SetRow(scroll, 1);

            // ===== 按钮栏：右对齐，取消在左、确认在右 =====
            Border footer = new Border();
            footer.Background = UiTheme.Frozen(0x17, 0x17, 0x1C);
            footer.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(22, 12, 22, 12);
            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;

            // 取消：次要样式，与确认按钮拉开视觉层级。
            Button cancel = new Button();
            cancel.Content = "取消";
            cancel.Style = UiTheme.SecondaryButtonStyle;
            cancel.Height = 34;
            cancel.MinWidth = 82;

            /// <summary>取消按钮直接关窗，Result 保持 null。</summary>
            cancel.Click += delegate { DialogResult = false; };
            AutomationProperties.SetAutomationId(cancel, "ScheduleDialogCancel");
            buttons.Children.Add(cancel);

            // 确认：主色样式；是本窗默认按钮，回车即可触发。
            Button save = new Button();
            save.Content = _existing == null ? "添加" : "保存";
            save.Style = UiTheme.PrimaryButtonStyle;
            save.Height = 34;
            save.MinWidth = 90;
            save.Margin = new Thickness(8, 0, 0, 0);
            save.IsDefault = true;
            save.Click += OnSave;
            AutomationProperties.SetAutomationId(save, "ScheduleDialogSave");
            buttons.Children.Add(save);

            footer.Child = buttons;
            root.Children.Add(footer);
            Grid.SetRow(footer, 2);

            return root;
        }

        /// <summary>
        /// 类型切换联动：把年度重复按分类默认值同步勾选。
        /// 仅供用户手动切换时生效，构造期回填由 <see cref="_updating"/> 屏蔽。
        /// </summary>
        private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            // 回填阶段跳过，否则会用分类默认值覆盖条目里已存的 Annual。
            if (_updating)
            {
                return;
            }
            ScheduleCategoryOption option = _typeCombo.SelectedItem as ScheduleCategoryOption;
            if (option != null)
            {
                // 只在切到新类型时给建议值，用户之后仍可手工改回。
                _annualCheck.IsChecked = ScheduleCategories.IsAnnual(option.Value);
            }
        }

        /// <summary>
        /// 校验并提交：按标题 → 日期 → 时间 → 下拉选择的顺序逐项检查，全部通过后组装对象。
        /// 任一环节失败只更新状态行；能定位到具体控件的顺带聚焦，窗口保持打开以便修正。
        /// </summary>
        private void OnSave(object sender, RoutedEventArgs e)
        {
            // 标题 Trim 后判空：全空格视为未填写，避免存进空白日程。
            string title = (_titleBox.Text ?? string.Empty).Trim();
            if (title.Length == 0)
            {
                _status.Text = "请填写日程标题。";
                _titleBox.Focus();
                return;
            }

            DateTime date;
            if (!ScheduleService.TryParseDate(_dateBox.Text, out date))
            {
                _status.Text = "日期格式应为 yyyy-MM-dd，例如 2026-10-30。";
                _dateBox.Focus();
                return;
            }

            string time;
            if (!TimeFormatter.TryNormalizeTime(_timeBox.Text, out time))
            {
                _status.Text = "时间格式应为 HH:mm，例如 09:30。";
                _timeBox.Focus();
                return;
            }

            // 两个下拉都必须有选中项：SelectedValue 未命中时 SelectedItem 会是 null。
            ScheduleCategoryOption type = _typeCombo.SelectedItem as ScheduleCategoryOption;
            ScheduleAdvanceOption advance = _advanceCombo.SelectedItem as ScheduleAdvanceOption;
            if (type == null || advance == null)
            {
                _status.Text = "请选择日程类型和提前提醒时间。";
                return;
            }

            // 编辑时从原条目克隆，保住表单之外的字段；新建则从空对象开始。
            ScheduleItem result = _existing != null ? _existing.Clone() : new ScheduleItem();
            result.Type = type.Value;
            result.Title = title;
            // 日期统一按不变文化写回，避免系统区域设置改动分隔符。
            result.Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            result.Time = time;
            result.AdvanceMinutes = advance.Minutes;
            result.Annual = _annualCheck.IsChecked == true;
            result.Note = (_noteBox.Text ?? string.Empty).Trim();
            // 新建默认启用；编辑沿用原开关状态，不在本对话框里改启用性。
            result.Enabled = _existing != null ? _existing.Enabled : true;
            // 内容变了，旧的触发键作废，否则可能因键相同而漏触发。
            result.LastTriggeredKey = "";
            // 兜底归一化：字段齐备才允许关闭，防止把半成品交给调用方。
            if (!result.Normalize())
            {
                _status.Text = "日程内容不完整，请检查后重试。";
                return;
            }

            Result = result;
            DialogResult = true;
        }

        /// <summary>
        /// 窗口级快捷键：Esc 取消，Ctrl+Enter 提交。
        /// 单独走 KeyDown 是因为 PreviewKeyDown 只拦普通 Enter，修饰键组合在此处理。
        /// </summary>
        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
            // Ctrl+Enter 作为“强制提交”：即使焦点在备注框里也执行保存。
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                OnSave(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        /// <summary>
        /// 预览阶段拦截普通 Enter，让“回车即保存”覆盖绝大多数输入框。
        /// 例外：备注框里的回车用于换行；带 Ctrl / Shift 的组合键留给快捷键逻辑。
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }
            // 备注是多行输入，回车必须保持换行语义，不能顺手提交。
            if (Keyboard.FocusedElement == _noteBox)
            {
                return;
            }
            // 带修饰键的 Enter 交给 OnWindowKeyDown（Esc/Ctrl+Enter），此处不重复处理。
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
            {
                return;
            }

            OnSave(sender, new RoutedEventArgs());
            // 标记已处理，避免默认按钮再触发一次保存。
            e.Handled = true;
        }

        /// <summary>
        /// 把“小标签 + 控件”叠成竖排字段组，并允许调用方给整组设外边距。
        /// 标签用次要色小字号，与输入内容形成层级；控件本身样式由调用方预先套好。
        /// </summary>
        /// <param name="labelText">上方标签文字，需自带上单位或格式示例。</param>
        /// <param name="control">下方实际输入控件。</param>
        /// <param name="margin">整组的外边距，用于同行字段之间留白。</param>
        /// <returns>可直接加进布局的字段组。</returns>
        private static StackPanel BuildLabeledField(string labelText, FrameworkElement control, Thickness margin)
        {
            StackPanel panel = new StackPanel();
            panel.Margin = margin;
            TextBlock label = UiTheme.MakeText(labelText, 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            // 标签与控件之间留 4px，紧贴但不相连。
            label.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(label);
            panel.Children.Add(control);
            return panel;
        }
    }
}
