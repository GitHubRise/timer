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

// 设置窗口外壳：内容骨架（标题/预览/页签/页脚）、事件挂载、控件回填与提交校验。
// 调用方：SettingsWindow 主文件的构造函数；本文件被主文件与各页签分部共用。
// 边界：
// 1. 标题区拖动必须挂在整个 header Grid 上（Cursor=SizeAll），只挂标题文字会拖不动；
// 2. 关闭按钮只关闭不保存，“应用”只触发 ApplyRequested，写盘与重建窗口归 AppController；
// 3. CommitAll 是唯一校验入口：失败时要把用户带到出错页签并聚焦控件，且不得关闭窗口；
// 4. 已知坑：_updating 为 true 时所有页签事件直接返回，回填控件前后必须成对设置。
namespace GameClock
{
    internal sealed partial class SettingsWindow
    {
        /// <summary>用户点“应用”：编辑副本已提交，请求控制器保存并刷新显示面（窗口不关闭）。</summary>
        public event EventHandler ApplyRequested;

        /// <summary>用户在提醒页点“预览”：请求控制器在悬浮窗或托盘气泡里演示一次提醒。</summary>
        public event EventHandler<ReminderPreviewEventArgs> ReminderTestRequested;

        /// <summary>
        /// 提交后的编辑副本；只有在 CommitAll 成功后才交给控制器使用。
        /// 控制器会再 Clone 并接管位置字段，不能长期持有这个引用。
        /// </summary>
        public AppSettings Result
        {
            get { return _working; }
        }

        /// <summary>
        /// 搭出窗口骨架：外层 Border → 四行 Grid（标题 / 预览卡 / 页签 / 页脚）。
        /// 所有页面内容都放进 _tabs，切换页签不销毁控件，滑块与输入框状态自然保留。
        /// </summary>
        private UIElement BuildWindowContent()
        {
            // 外层 Border 承担 1px 描边：WindowStyle=None 时系统不画边框，只能自绘。
            Border shell = new Border();
            shell.Background = UiTheme.WindowBackground;
            shell.BorderBrush = UiTheme.CardBorder;
            shell.BorderThickness = new Thickness(1);

            // 四行：标题固定高、预览卡固定高、页签占满剩余空间、页脚固定高。
            // 中间行用 Star，窗口拉伸时只有页签内容变高，标题与页脚不会跟着变形。
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标题区整块 Grid 可拖动：光标 SizeAll + MouseLeftButtonDown→DragMove。
            // 拖动必须挂在这个 Grid 上而不是只挂标题文字，空白处也要能拖。
            Grid header = new Grid();
            header.Margin = new Thickness(26, 18, 18, 0);
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 透明背景是必须的：没有背景的 Panel 不参与命中测试，鼠标点上去不会触发拖动。
            header.Background = Brushes.Transparent;
            header.Cursor = Cursors.SizeAll;
            header.MouseLeftButtonDown += OnHeaderMouseDown;

            // 标题区左侧：主标题 + 一行操作提示（说明“应用只保存不关闭”的行为差异）。
            // 标题 22 号 SemiBold，与正文 13 号拉开层级；副标题 11.5 号次要色，弱化处理。
            StackPanel titleStack = new StackPanel();
            titleStack.Background = Brushes.Transparent;
            TextBlock title = UiTheme.MakeText("时间悬浮窗设置", 22, UiTheme.TextPrimary, FontWeights.SemiBold);
            // 副标题本身也是操作提示，随 SettingsUiHints.ShowHints 一起显隐。
            TextBlock subtitle = SettingsUiHints.Hint(
                "颜色、字体、格式和提醒；点击“应用”只保存不关闭。",
                11.5,
                UiTheme.TextSecondary,
                FontWeights.Normal);
            subtitle.Margin = new Thickness(0, 4, 0, 0);
            titleStack.Children.Add(title);
            titleStack.Children.Add(subtitle);
            header.Children.Add(titleStack);
            Grid.SetColumn(titleStack, 0);

            // 关闭按钮只关闭窗口、不应用未保存的修改（ToolTip 与 Esc 行为一致）；
            // 光标强制回箭头，避免处在标题区时仍显示 SizeAll。
            // 36×32 是标题区右上角的标准点击热区，再小在高分屏上会难点中。
            Button closeButton = new Button();
            closeButton.Content = "×";
            closeButton.Style = UiTheme.GhostButtonStyle;
            closeButton.FontFamily = new FontFamily("Segoe UI");
            closeButton.Width = 36;
            closeButton.Height = 32;
            closeButton.FontSize = 20;
            closeButton.Padding = new Thickness(0);
            closeButton.HorizontalAlignment = HorizontalAlignment.Right;
            closeButton.VerticalAlignment = VerticalAlignment.Top;
            closeButton.ToolTip = "关闭（不应用尚未保存的修改）";
            closeButton.Cursor = Cursors.Arrow;
            closeButton.Click += OnClose;
            AutomationProperties.SetAutomationId(closeButton, "CloseButton");
            header.Children.Add(closeButton);
            Grid.SetColumn(closeButton, 1);

            root.Children.Add(header);
            Grid.SetRow(header, 0);

            // 预览卡固定高度（116）且不随窗口拉伸：与真实悬浮窗等比缩小，避免误导用户。
            // 左右 26 与标题区内边距对齐，上下 14/2 让它紧贴标题、与页签留一点空隙。
            Border previewCard = BuildPreviewCard();
            previewCard.Margin = new Thickness(26, 14, 26, 2);
            root.Children.Add(previewCard);
            Grid.SetRow(previewCard, 1);

            // 页签顺序必须与主文件的 TabAppearance~TabWeather 常量一一对应，
            // 校验失败跳页（_tabs.SelectedIndex = TabFormat 等）依赖这个顺序。
            _tabs.Items.Add(CreateTab("桌面悬浮窗", BuildAppearanceTab()));
            _tabs.Items.Add(CreateTab("任务栏时钟", BuildTaskbarTab()));
            _tabs.Items.Add(CreateTab("全局时间格式", BuildFormatTab()));
            _tabs.Items.Add(CreateTab("定时提醒", BuildRemindersTab()));
            _tabs.Items.Add(CreateTab("日程", BuildScheduleTab()));
            _tabs.Items.Add(CreateTab("天气", BuildWeatherTab()));
            // 默认停在“桌面悬浮窗”页：这是用户最常改的一页，也对应出厂默认显示位置。
            _tabs.SelectedIndex = TabAppearance;
            root.Children.Add(_tabs);
            Grid.SetRow(_tabs, 2);

            Border footer = BuildFooter();
            root.Children.Add(footer);
            Grid.SetRow(footer, 3);

            shell.Child = root;
            return shell;
        }

        /// <summary>
        /// 集中挂接所有控件事件。控件在窗口生命周期内不重建，所以只需挂一次、
        /// 也不需要对应的解绑；新增页签事件请加到本方法末尾，不要散落在构造函数里。
        /// </summary>
        private void AttachEvents()
        {
            // 外观页：字体、字号、透明度、阴影/3D、行为开关。
            // 外观页：字体、字号、透明度与阴影/3D 效果最终都落到同一个预览刷新。
            _fontCombo.SelectionChanged += OnFontChanged;
            _sizeSlider.ValueChanged += OnSizeChanged;
            _opacitySlider.ValueChanged += OnOpacityChanged;
            _shadowCheck.Checked += OnEffectChanged;
            _shadowCheck.Unchecked += OnEffectChanged;
            _threeDCheck.Checked += OnEffectChanged;
            _threeDCheck.Unchecked += OnEffectChanged;
            _clickThroughCheck.Checked += OnBehaviorChanged;
            _clickThroughCheck.Unchecked += OnBehaviorChanged;
            _topmostCheck.Checked += OnBehaviorChanged;
            _topmostCheck.Unchecked += OnBehaviorChanged;
            _startWithWindowsCheck.Checked += OnBehaviorChanged;
            _startWithWindowsCheck.Unchecked += OnBehaviorChanged;
            _hideFullscreenCheck.Checked += OnBehaviorChanged;
            _hideFullscreenCheck.Unchecked += OnBehaviorChanged;
            // 显示位置两个开关共用 OnDisplaySurfaceChanged：这里只改编辑副本，
            // 窗口的创建/销毁仍由控制器在提交后统一处理。
            _desktopOverlayCheck.Checked += OnDisplaySurfaceChanged;
            _desktopOverlayCheck.Unchecked += OnDisplaySurfaceChanged;
            _taskbarClockCheck.Checked += OnDisplaySurfaceChanged;
            _taskbarClockCheck.Unchecked += OnDisplaySurfaceChanged;
            // 任务栏页：格式框的 TextChanged 只更新预览状态，真正校验在 CommitAll。
            _taskbarFontCombo.SelectionChanged += OnTaskbarFontChanged;
            _taskbarSizeSlider.ValueChanged += OnTaskbarSizeChanged;
            _taskbarColorBox.TextChanged += OnTaskbarColorChanged;
            _taskbarFormatBox.TextChanged += OnTaskbarFormatChanged;
            _taskbarFormatBox.PreviewKeyDown += OnTaskbarFormatKeyDown;
            _taskbarFormatCombo.SelectionChanged += OnTaskbarFormatPresetChanged;
            _taskbarDotNetFormatRadio.Checked += OnTaskbarFormatModeChanged;
            _taskbarTokenFormatRadio.Checked += OnTaskbarFormatModeChanged;
            // 全局时间格式页：模式单选、预设下拉与输入框三者联动，任一变化都只改预览。
            _formatCombo.SelectionChanged += OnFormatPresetChanged;
            _formatBox.TextChanged += OnFormatTextChanged;
            _formatBox.PreviewKeyDown += OnFormatKeyDown;
            _dotNetFormatRadio.Checked += OnFormatModeChanged;
            _tokenFormatRadio.Checked += OnFormatModeChanged;
            _formatHelpSearchBox.TextChanged += OnFormatHelpSearchChanged;
            _weatherFormatBox.TextChanged += OnWeatherFormatTextChanged;
            _timeZoneCombo.SelectionChanged += OnTimeZoneChanged;
            _reminderMessageBox.KeyDown += OnReminderMessageKeyDown;
            // 定时提醒页的“当前状态/外观/行为”开关只改 _working，不触发调度器，
            // 调度器按新配置运行要等控制器应用之后。
            _workModeRadio.Checked += OnCurrentStateChanged;
            _leisureModeRadio.Checked += OnCurrentStateChanged;
            _reminderDurationCombo.SelectionChanged += OnReminderDurationChanged;
            _reminderHoverCheck.Checked += OnReminderHoverChanged;
            _reminderHoverCheck.Unchecked += OnReminderHoverChanged;
            _reminderBlinkSlider.ValueChanged += OnReminderBlinkChanged;
            _reminderSoundCheck.Checked += OnReminderSoundChanged;
            _reminderSoundCheck.Unchecked += OnReminderSoundChanged;
            // 天气页：城市输入只更新占位预览（不发请求），测试按钮才访问网络。
            _weatherEnabledCheck.Checked += OnWeatherChanged;
            _weatherEnabledCheck.Unchecked += OnWeatherChanged;
            _weatherCityBox.TextChanged += OnWeatherCityChanged;
            _weatherFontCombo.SelectionChanged += OnWeatherFontChanged;
            _weatherSizeSlider.ValueChanged += OnWeatherSizeChanged;
            _weatherShadowCheck.Checked += OnWeatherShadowChanged;
            _weatherShadowCheck.Unchecked += OnWeatherShadowChanged;
            _weatherBoldCheck.Checked += OnWeatherFontStyleChanged;
            _weatherBoldCheck.Unchecked += OnWeatherFontStyleChanged;
            _weatherItalicCheck.Checked += OnWeatherFontStyleChanged;
            _weatherItalicCheck.Unchecked += OnWeatherFontStyleChanged;
            // 窗口级按键：Esc 关闭（不保存），与标题栏“×”一致，所以挂在 PreviewKeyDown 上。
            _weatherRefreshCombo.SelectionChanged += OnWeatherRefreshChanged;
            PreviewKeyDown += OnWindowPreviewKeyDown;
        }

        /// <summary>
        /// 把 _working 的值回填到控件（打开窗口与“恢复默认”共用）。
        /// 调用方必须用 _updating 包裹，否则回填会触发 Changed 事件把状态误标成“已修改”。
        /// </summary>
        private void LoadControlsFromWorking()
        {
            // 配置里的字体可能已被卸载（换机/旧配置），找不到就逐级回退到默认字体，
            // 否则 ComboBox 会停在空选项上，用户看不出字体已失效。
            FontChoice choice = FontCatalog.FindChoice(_fontChoices, _working.FontFamilyName);
            // 配置字体不在本机字体清单里：先试默认字体（Consolas）。
            if (choice == null)
            {
                choice = FontCatalog.FindChoice(_fontChoices, FontCatalog.DefaultFont);
            }
            _fontCombo.SelectedItem = choice;

            // 字号直接回填；透明度按百分比回填（存储是 0.20–1.00，界面是 20–100）。
            // 数值文本同步刷新，避免打开窗口瞬间出现“滑块位置与数字不一致”。
            _sizeSlider.Value = _working.FontSize;
            _opacitySlider.Value = Math.Round(_working.Opacity * 100.0);
            _sizeValueText.Text = ((int)Math.Round(_working.FontSize)).ToString(CultureInfo.InvariantCulture) + " px";
            _opacityValueText.Text = ((int)Math.Round(_working.Opacity * 100.0)).ToString(CultureInfo.InvariantCulture) + "%";

            // 行为与显示位置开关：全部来自配置原值，这里只做展示。
            // 两个显示位置开关互相独立，回填顺序无关，真正的窗口显隐由控制器在提交后处理。
            _shadowCheck.IsChecked = _working.ShowShadow;
            _threeDCheck.IsChecked = _working.Text3D;
            _clickThroughCheck.IsChecked = _working.ClickThrough;
            _topmostCheck.IsChecked = _working.Topmost;
            _startWithWindowsCheck.IsChecked = _working.StartWithWindows;
            _hideFullscreenCheck.IsChecked = _working.HideOnFullscreen;
            _desktopOverlayCheck.IsChecked = _working.ShowDesktopOverlay;
            _taskbarClockCheck.IsChecked = _working.ShowTaskbarClock;

            // 任务栏字体回退链与悬浮窗相同，但默认值优先是“微软雅黑”（任务栏显示更清晰）。
            FontChoice taskbarFont = FontCatalog.FindChoice(_fontChoices, _working.TaskbarFontFamilyName);
            // 首选任务栏默认字体：微软雅黑在 10px 左右的中文可读性最好。
            if (taskbarFont == null)
            {
                taskbarFont = FontCatalog.FindChoice(_fontChoices, "Microsoft YaHei UI");
            }
            // 连雅黑也找不到（极简系统）时退到 Consolas，保证下拉框永远有选中项。
            if (taskbarFont == null)
            {
                taskbarFont = FontCatalog.FindChoice(_fontChoices, FontCatalog.DefaultFont);
            }
            _taskbarFontCombo.SelectedItem = taskbarFont;
            // 任务栏字号同样是整数像素；颜色框回填规范十六进制写法（保存时已归一）。
            _taskbarSizeSlider.Value = _working.TaskbarFontSize;
            _taskbarSizeValueText.Text = ((int)Math.Round(_working.TaskbarFontSize)).ToString(CultureInfo.InvariantCulture) + " px";
            _taskbarColorBox.Text = _working.TaskbarColorHex;
            // 任务栏格式语言：决定下拉挂哪组预设、输入框显示哪个字符串。
            // TaskbarFormat 永远存简洁变量，TaskbarDotNetFormat 永远存 .NET 格式，两者不混用。
            bool taskbarTokenMode = FormatEngine.IsTokenMode(_working.TaskbarFormatMode);
            // 格式语言决定三件事：单选按钮、预设列表 ItemsSource、输入框里的字符串。
            // 三者必须一起切，否则会出现“选中简洁变量却显示 .NET 格式串”的错位。
            _taskbarDotNetFormatRadio.IsChecked = !taskbarTokenMode;
            _taskbarTokenFormatRadio.IsChecked = taskbarTokenMode;
            _taskbarActivePresets = taskbarTokenMode ? _taskbarTokenPresets : _taskbarDotNetPresets;
            _taskbarFormatCombo.ItemsSource = _taskbarActivePresets;
            _taskbarFormatBox.Text = taskbarTokenMode ? _working.TaskbarFormat : _working.TaskbarDotNetFormat;
            UpdateTaskbarColorUi();
            UpdateTaskbarFormatHint();
            UpdateTaskbarFormatComboSelection();
            _taskbarFormatStatus.Text = taskbarTokenMode
                ? "简洁变量格式，支持两行；任务栏空间不足时会自动缩小字号。"
                : ".NET 格式，支持两行；任务栏空间不足时会自动缩小字号。";
            SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.TextMuted);

            // 提醒页：当前工作/娱乐状态、显示时长、悬停关闭、闪烁次数、铃声。
            string mode = ReminderModes.Normalize(_working.CurrentMode);
            _workModeRadio.IsChecked = mode == ReminderModes.Work;
            _leisureModeRadio.IsChecked = mode == ReminderModes.Leisure;
            SelectReminderDurationOption(_working.ReminderDisplaySeconds);
            _reminderHoverCheck.IsChecked = _working.ReminderHoverCloseEnabled;
            _reminderBlinkSlider.Value = _working.ReminderBlinkCount;
            _reminderBlinkValueText.Text = _working.ReminderBlinkCount + " 次";
            _reminderSoundCheck.IsChecked = _working.ReminderSoundEnabled;

            // 天气总开关只控制城市/测试控件是否可编辑；关闭时仍回填城市名，重新开启可继续用。
            _weatherEnabledCheck.IsChecked = _working.WeatherEnabled;
            _weatherCityBox.Text = _working.WeatherCity;
            _weatherCityBox.IsEnabled = _working.WeatherEnabled;

            // 天气字体回退链：同样先试微软雅黑再退默认字体。
            FontChoice weatherFont = FontCatalog.FindChoice(_fontChoices, _working.WeatherFontFamilyName);
            // 首选天气默认字体：与时间字体区分开，字号通常更小。
            if (weatherFont == null)
            {
                weatherFont = FontCatalog.FindChoice(_fontChoices, "Microsoft YaHei UI");
            }
            // 都找不到时退默认字体，避免下拉框空选。
            if (weatherFont == null)
            {
                weatherFont = FontCatalog.FindChoice(_fontChoices, FontCatalog.DefaultFont);
            }
            _weatherFontCombo.SelectedItem = weatherFont;
            _weatherSizeSlider.Value = _working.WeatherFontSize;
            _weatherSizeValueText.Text = ((int)Math.Round(_working.WeatherFontSize)).ToString(CultureInfo.InvariantCulture) + " px";
            _weatherShadowCheck.IsChecked = _working.WeatherShowShadow;
            _weatherBoldCheck.IsChecked = _working.WeatherFontBold;
            _weatherItalicCheck.IsChecked = _working.WeatherFontItalic;
            UpdateWeatherColorUi();
            SelectWeatherRefreshOption(_working.WeatherRefreshMinutes);

            // 主时间格式与任务栏同理：变量模式与 .NET 模式各有一份字符串，不能混用。
            bool tokenMode = FormatEngine.IsTokenMode(_working.FormatMode);
            _dotNetFormatRadio.IsChecked = !tokenMode;
            _tokenFormatRadio.IsChecked = tokenMode;
            _formatPresets = tokenMode ? _tokenFormatPresets : _dotNetFormatPresets;
            _formatCombo.ItemsSource = _formatPresets;
            _formatBox.Text = tokenMode ? _working.TokenTimeFormat : _working.TimeFormat;
            _weatherFormatBox.Text = _working.WeatherFormat;
            // 派生 UI（变量帮助列表、颜色块、下拉选中项）都在这里统一刷新一次，
            // 避免每个 Update* 方法自己再回填一遍、出现顺序依赖。
            UpdateFormatHelp();
            UpdateWeatherFormatStatus();
            SelectTimeZoneChoice(_working.TimeZoneId);
            UpdateFormatComboSelection();
            _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            SettingsUiHints.SetStatusColor(_formatStatus, UiTheme.TextMuted);

            // 列表类控件放在最后重建：它们依赖上面的值（模式、颜色、格式）已经回填完毕。
            UpdateColorUi();
            RebuildReminderRows();
            RebuildScheduleRows();
            // 预览放最后，保证用的是全部回填后的 _working。
            UpdatePreview();
        }

        /// <summary>
        /// 标题区拖动：WPF 的 DragMove 内部进入模态拖动循环，窗口状态被系统改变
        /// （鼠标已释放、窗口正在关闭）时会抛 InvalidOperationException，吞掉即可，不打断用户。
        /// </summary>
        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 只响应左键按下：右键/中键在标题区应保持无操作，避免误拖。
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // DragMove 是同步阻塞调用，异常只可能来自窗口状态变化，属于可恢复情况。
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // 拖动被系统拒绝：记一条 Warn 便于定位“窗口拖不动”的反馈，不在 UI 上提示。
                    AppLog.Warn("Chrome", "标题区拖动被拒绝（鼠标已释放或窗口状态变化），已忽略");
                }
            }
        }

        /// <summary>窗口级按键：Esc 关闭窗口且不保存，与标题栏“×”行为一致。</summary>
        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 只拦截 Esc；其余按键交给 WPF 默认处理，避免影响输入框回车/方向键。
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        /// <summary>标题栏“×”：只关闭不应用；DialogResult 保持 null，控制器据此不写盘。</summary>
        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 窗口关闭（“×”、Esc、“保存并退出”、控制器退出时强制关闭）都汇聚到这里，
        /// 所以关闭日志只记一处，保证每次“打开”都恰好有一条对应的“关闭”。
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            // 先记日志再调基类：基类之后窗口句柄已释放，但日志不依赖窗口状态。
            AppLog.Info("Settings", "设置窗口已关闭");
            base.OnClosed(e);
        }

        /// <summary>
        /// 按 _working 当前值重绘预览卡（主时间用真实格式化引擎，天气用示例数据）。
        /// 这里在任何控件变更时都会被调用，属于高频刷新路径，因此**不得记日志**、也不要创建临时文件。
        /// </summary>
        private void UpdatePreview()
        {
            // 预览必须与真实悬浮窗同一套格式化逻辑，否则用户看到的示例会与桌面实际显示不一致。
            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            WeatherInfo sample = CreateSampleWeather();
            string display = FormatEngine.IsTokenMode(_working.FormatMode)
                ? FormatEngine.Format(now, sample, _working.TokenTimeFormat)
                : TimeFormatter.Format(now, _working.TimeFormat);
            _previewText.Text = display;
            _previewText.ApplyStyle(
                FontCatalog.GetFontFamily(_working.FontFamilyName),
                _working.FontSize,
                FontWeights.SemiBold,
                _working.TimeColor,
                _working.ShowShadow,
                _working.Text3D);
            _previewText.Opacity = _working.Opacity;

            // 天气已内联进时间格式串时，不再单独显示一行天气，否则会重复两遍。
            bool inlineWeather = FormatEngine.IsTokenMode(_working.FormatMode)
                && FormatEngine.ContainsWeatherToken(_working.TokenTimeFormat);
            bool showWeather = _working.WeatherEnabled && !inlineWeather;
            _previewWeatherText.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
            if (!showWeather)
            {
                // 关闭天气行时提前返回，避免为隐藏控件做无意义的排版计算。
                return;
            }

            // 城市留空时用“北京”占位；空格式串回退到内置示例文案，保证预览卡不留白。
            string city = string.IsNullOrWhiteSpace(_working.WeatherCity) ? "北京" : _working.WeatherCity;
            Color weatherColor = _working.WeatherColor;
            _previewWeatherText.Text = string.IsNullOrWhiteSpace(_working.WeatherFormat)
                ? city + " · 晴 · 12°C · 今日 9°~17°"
                : FormatEngine.Format(now, sample, _working.WeatherFormat);
            _previewWeatherText.FontFamily = FontCatalog.GetFontFamily(_working.WeatherFontFamilyName);
            _previewWeatherText.FontSize = _working.WeatherFontSize;
            _previewWeatherText.FontWeight = _working.WeatherFontBold ? FontWeights.Bold : FontWeights.Normal;
            _previewWeatherText.FontStyle = _working.WeatherFontItalic ? FontStyles.Italic : FontStyles.Normal;
            // 天气行统一 232/255 不透明度：比时间行略淡，保持主次层次。
            _previewWeatherText.Foreground = UiTheme.BrushFrom(
                Color.FromArgb(232, weatherColor.R, weatherColor.G, weatherColor.B));
            // 行距按字号比例给（20%），最小 2px，避免小字号时贴住时间行。
            _previewWeatherText.Margin = new Thickness(0, Math.Max(2, _working.WeatherFontSize * 0.20), 0, 0);
            _previewWeatherText.Opacity = _working.Opacity;
            // 阴影颜色按底色亮度自动选白/黑，深色字配白影、浅色字配黑影，否则阴影糊成一片。
            if (_working.WeatherShowShadow)
            {
                _previewWeatherText.Effect = new DropShadowEffect
                {
                    Color = ColorUtil.Luminance(weatherColor) < 0.22 ? Colors.White : Colors.Black,
                    Direction = 315,
                    ShadowDepth = Math.Max(1.0, _working.WeatherFontSize * 0.10),
                    BlurRadius = Math.Max(2.0, _working.WeatherFontSize * 0.20),
                    Opacity = 0.78
                };
            }
            else
            {
                // 关闭阴影时显式置 null，避免上一次的 Effect 残留。
                _previewWeatherText.Effect = null;
            }
        }

        /// <summary>
        /// 构造预览用的固定示例天气（晴、12°C、6 天预报），不访问网络、不读天气缓存。
        /// 示例数据只为展示格式效果，数值本身没有业务含义，改格式时不需要同步修改。
        /// </summary>
        private WeatherInfo CreateSampleWeather()
        {
            WeatherInfo sample = new WeatherInfo();
            sample.City = string.IsNullOrWhiteSpace(_working.WeatherCity) ? "北京" : _working.WeatherCity.Trim();
            sample.Condition = "晴";
            sample.WeatherCode = 0;
            sample.Temperature = 12;
            sample.Min = 9;
            sample.Max = 17;
            sample.Humidity = 48;
            sample.WindDirection = "北风";
            sample.AqiValue = 49;
            sample.AqiLevel = "优";
            sample.PublicIp = "221.222.190.200";

            // 六天示例：条件/代码/最低温/最高温按下标一一对应，长度必须保持一致。
            string[] conditions = new string[] { "晴", "晴", "多云", "阴", "小雨", "晴" };
            int[] codes = new int[] { 0, 0, 2, 3, 61, 0 };
            double[] mins = new double[] { 9, 6, 8, 10, 7, 9 };
            double[] maxs = new double[] { 17, 19, 20, 18, 15, 21 };
            for (int i = 0; i < conditions.Length; i++)
            {
                DailyForecast day = new DailyForecast();
                day.Code = codes[i];
                day.Condition = conditions[i];
                day.Min = mins[i];
                day.Max = maxs[i];
                sample.Days.Add(day);
            }
            return sample;
        }

        /// <summary>
        /// 把控件值校验后写回 _working（“应用”和“保存并退出”共用的提交入口）。
        /// 校验按“格式 → 时区 → 外观 → 提醒行”的顺序进行，失败返回 false 并把错误原文写入 error，
        /// 同时自动切到出错的页签、把焦点移到出错控件，避免用户自己找。
        /// </summary>
        private bool CommitAll(out string error)
        {
            error = null;
            // 出参约定：只有校验失败时才写错误原文，成功时保持 null，调用方无需判断“上一次的旧错误”。

            // 主时间格式：先按当前单选按钮判断语法（变量格式 / .NET 格式），归一化后回写。
            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string normalizedFormat;
            string formatError;
            bool formatValid = tokenMode
                ? FormatEngine.TryNormalizeTokenFormat(_formatBox.Text, true, out normalizedFormat, out formatError)
                : TimeFormatter.TryNormalizeFormat(_formatBox.Text, out normalizedFormat, out formatError);
            // 主格式非法：标红状态行、切到「全局时间格式」页并聚焦输入框，然后中断提交。
            if (!formatValid)
            {
                _formatStatus.Text = formatError;
                SettingsUiHints.SetStatusColor(_formatStatus, UiTheme.Accent);
                _tabs.SelectedIndex = TabFormat;
                _formatStatus.BringIntoView();
                _formatBox.Focus();
                error = formatError;
                return false;
            }

            // 主格式写回：变量模式写 TokenTimeFormat，.NET 模式写 TimeFormat，另一份原样保留。
            // ModeToken/ModeDotNet 与字符串必须成对更新，否则下次打开窗口会读到“模式与串不符”的旧值。
            if (tokenMode)
            {
                _working.TokenTimeFormat = normalizedFormat;
                _working.FormatMode = FormatEngine.ModeToken;
            }
            else
            {
                _working.TimeFormat = normalizedFormat;
                _working.FormatMode = FormatEngine.ModeDotNet;
            }

            // 天气格式是可选项：留空表示不自定义，非空才走变量格式校验。
            string weatherFormatText = (_weatherFormatBox.Text ?? string.Empty).Trim();
            if (weatherFormatText.Length > 0)
            {
                string normalizedWeatherFormat;
                string weatherFormatError;
                if (!FormatEngine.TryNormalizeTokenFormat(
                    weatherFormatText,
                    false,
                    out normalizedWeatherFormat,
                    out weatherFormatError))
                {
                    _weatherFormatStatus.Text = weatherFormatError;
                    SettingsUiHints.SetStatusColor(_weatherFormatStatus, UiTheme.Accent);
                    _weatherFormatStatus.BringIntoView();
                    _tabs.SelectedIndex = TabWeather;
                    _weatherFormatBox.Focus();
                    error = weatherFormatError;
                    return false;
                }
                _working.WeatherFormat = normalizedWeatherFormat;
            }
            else
            {
                _working.WeatherFormat = string.Empty;
            }

            // 任务栏格式：与主格式同样两套语法，但校验失败要切到「任务栏时钟」页。
            bool taskbarTokenMode = _taskbarTokenFormatRadio.IsChecked == true;
            string taskbarFormatText = (_taskbarFormatBox.Text ?? string.Empty).Trim();
            string normalizedTaskbarFormat;
            string taskbarFormatError;
            bool taskbarFormatValid = taskbarTokenMode
                ? FormatEngine.TryNormalizeTokenFormat(
                    taskbarFormatText,
                    true,
                    out normalizedTaskbarFormat,
                    out taskbarFormatError)
                : TimeFormatter.TryNormalizeFormat(
                    taskbarFormatText,
                    out normalizedTaskbarFormat,
                    out taskbarFormatError);
            if (!taskbarFormatValid)
            {
                _taskbarFormatStatus.Text = taskbarFormatError;
                SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.Accent);
                _tabs.SelectedIndex = TabTaskbar;
                _taskbarFormatStatus.BringIntoView();
                _taskbarFormatBox.Focus();
                error = taskbarFormatError;
                return false;
            }

            // 任务栏写回：与主格式同理，两种语言的字符串各存一份，切换不丢历史格式。
            _working.TaskbarFormatMode = taskbarTokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet;
            if (taskbarTokenMode)
            {
                _working.TaskbarFormat = normalizedTaskbarFormat;
            }
            else
            {
                _working.TaskbarDotNetFormat = normalizedTaskbarFormat;
            }

            // 时区：下拉项可能为 null（理论上不会，但防御空引用），为空时保留原值。
            TimeZoneChoice timeZoneChoice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            if (timeZoneChoice != null)
            {
                _working.TimeZoneId = timeZoneChoice.Id;
            }
            // 主格式校验通过后把状态行恢复成普通提示（上一次失败可能把它染成了强调色）。
            _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            SettingsUiHints.SetStatusColor(_formatStatus, UiTheme.TextMuted);

            // 外观页：字体/字号/透明度/阴影，数值直接取滑块（滑块范围与 AppSettings 的 Clamp 区间一致）。
            FontChoice font = _fontCombo.SelectedItem as FontChoice;
            if (font != null)
            {
                _working.FontFamilyName = font.Name;
            }
            // 滑块值直接写回：拖动过程中可能短暂越界，统一在 Normalize（提交后由控制器调用）里 Clamp 兜底。
            _working.FontSize = _sizeSlider.Value;
            _working.Opacity = _opacitySlider.Value / 100.0;
            _working.ShowShadow = _shadowCheck.IsChecked == true;
            _working.Text3D = _threeDCheck.IsChecked == true;
            _working.ClickThrough = _clickThroughCheck.IsChecked == true;
            _working.Topmost = _topmostCheck.IsChecked == true;
            _working.StartWithWindows = _startWithWindowsCheck.IsChecked == true;
            // 显示位置与行为开关：两个显示位置互相独立，真正的窗口开关由控制器在提交后执行。
            _working.HideOnFullscreen = _hideFullscreenCheck.IsChecked == true;
            _working.ShowDesktopOverlay = _desktopOverlayCheck.IsChecked == true;
            _working.ShowTaskbarClock = _taskbarClockCheck.IsChecked == true;
            // 任务栏字体：下拉未命中（配置里的字体已被卸载）时保留原值，交给 Load 的回退链处理。
            FontChoice taskbarFont = _taskbarFontCombo.SelectedItem as FontChoice;
            if (taskbarFont != null)
            {
                _working.TaskbarFontFamilyName = taskbarFont.Name;
            }
            // 任务栏外观：颜色框按十六进制解析后统一回转标准写法，非法串由 FromHex 回退默认色并记 Warn。
            _working.TaskbarFontSize = _taskbarSizeSlider.Value;
            _working.TaskbarColorHex = ColorUtil.ToHex(ColorUtil.FromHex(_taskbarColorBox.Text));
            // 提醒外观与天气设置也在这里落回 _working；具体提醒/日程列表由各自的分部逻辑维护。
            _working.CurrentMode = _workModeRadio.IsChecked == true
                ? ReminderModes.Work
                : ReminderModes.Leisure;
            ReminderDisplayOption displayOption = _reminderDurationCombo.SelectedItem as ReminderDisplayOption;
            if (displayOption != null)
            {
                _working.ReminderDisplaySeconds = displayOption.Seconds;
            }
            _working.ReminderHoverCloseEnabled = _reminderHoverCheck.IsChecked == true;
            _working.ReminderBlinkCount = (int)Math.Round(_reminderBlinkSlider.Value);
            _working.ReminderSoundEnabled = _reminderSoundCheck.IsChecked == true;
            // 天气设置即使开关关闭也照样写回城市/字体/字号：重新开启时能恢复上次的显示样式。
            _working.WeatherEnabled = _weatherEnabledCheck.IsChecked == true;
            _working.WeatherCity = (_weatherCityBox.Text ?? string.Empty).Trim();
            FontChoice weatherFont = _weatherFontCombo.SelectedItem as FontChoice;
            if (weatherFont != null)
            {
                _working.WeatherFontFamilyName = weatherFont.Name;
            }
            _working.WeatherFontSize = _weatherSizeSlider.Value;
            _working.WeatherShowShadow = _weatherShadowCheck.IsChecked == true;
            _working.WeatherFontBold = _weatherBoldCheck.IsChecked == true;
            _working.WeatherFontItalic = _weatherItalicCheck.IsChecked == true;
            // 刷新间隔直接取选项里的分钟数；下拉为空时保留旧值（避免把 0 写进配置）。
            WeatherRefreshOption refreshOption = _weatherRefreshCombo.SelectedItem as WeatherRefreshOption;
            if (refreshOption != null)
            {
                _working.WeatherRefreshMinutes = refreshOption.Minutes;
            }

            // 提醒列表：先把界面行同步进 _working.Reminders，再逐行校验时间与内容。
            // 日程行不在这里提交：日程页有自己的增删改逻辑，直接维护 _working.Schedules。
            SyncReminderRows();
            for (int i = 0; i < _reminderRows.Count; i++)
            {
                ReminderRow row = _reminderRows[i];
                // 每次重校验先复位边框颜色，避免上一轮的红色残留。
                row.TimeBox.BorderBrush = UiTheme.FieldBorder;
                row.MessageBox.BorderBrush = UiTheme.FieldBorder;

                // 时间必须是 HH:mm（TimeFormatter 内部兼容 9:05 之类写法并归一化）。
                string normalizedTime;
                if (!TimeFormatter.TryNormalizeTime(row.Reminder.Time, out normalizedTime))
                {
                    row.TimeBox.BorderBrush = UiTheme.Accent;
                    SetReminderStatus("有一条提醒的时间格式不正确，请使用 HH:mm。", false);
                    _tabs.SelectedIndex = TabReminders;
                    row.TimeBox.Focus();
                    error = "提醒时间格式不正确。";
                    return false;
                }

                // 空内容提醒没有展示意义，直接拦下；超长则静默截断到 120 字（与输入框上限一致）。
                string message = (row.Reminder.Message ?? string.Empty).Trim();
                if (message.Length == 0)
                {
                    row.MessageBox.BorderBrush = UiTheme.Accent;
                    SetReminderStatus("有一条提醒没有填写内容。", false);
                    _tabs.SelectedIndex = TabReminders;
                    row.MessageBox.Focus();
                    error = "提醒内容不能为空。";
                    return false;
                }
                if (message.Length > 120)
                {
                    message = message.Substring(0, 120);
                }

                row.Reminder.Time = normalizedTime;
                row.Reminder.Message = message;
                row.TimeBox.Text = normalizedTime;
                row.MessageBox.Text = message;
            }

            // 最后统一 Normalize：把越界数值夹回合法区间，并补齐各字段间的一致性（如 Left/Top 成对）。
            _working.Normalize();
            return true;
        }

        /// <summary>
        /// “应用”：只保存不关闭。提交成功后才触发 ApplyRequested，由控制器落盘并重建显示面；
        /// 提交失败时停在当前页签并保持窗口打开，方便用户修正。
        /// </summary>
        private void OnApply(object sender, RoutedEventArgs e)
        {
            AppLog.Info("Settings", "设置应用开始：页签=" + _tabs.SelectedIndex);

            string error;
            if (!CommitAll(out error))
            {
                // 校验失败属于用户可见的失败回退：记 Warn 带上错误原文，UI 上由状态行提示。
                AppLog.Warn("Settings", "设置应用中止：校验未通过（" + error + "）");
                return;
            }

            if (ApplyRequested != null)
            {
                ApplyRequested(this, EventArgs.Empty);
            }
            // 成功反馈只写在页脚状态行（不弹对话框），避免“应用”打断用户继续调参。
            _footerStatus.Text = "已应用，未关闭";
            _footerStatus.Foreground = UiTheme.Success;
            AppLog.Info("Settings", "设置应用完成：已提交控制器保存并刷新显示");
        }

        /// <summary>
        /// “保存并退出”：提交成功后置 DialogResult=true 让 ShowDialog 返回，
        /// 控制器据此写盘；没有监听者时窗口同样关闭（DialogResult 赋值即关闭）。
        /// </summary>
        private void OnSaveAndExit(object sender, RoutedEventArgs e)
        {
            string error;
            if (!CommitAll(out error))
            {
                AppLog.Warn("Settings", "保存并退出中止：校验未通过（" + error + "）");
                return;
            }
            AppLog.Info("Settings", "保存并退出：校验通过，提交结果");
            DialogResult = true;
        }

        /// <summary>“关于”：打开独立模态窗口，不改任何设置。</summary>
        private void OnAbout(object sender, RoutedEventArgs e)
        {
            AboutWindow about = new AboutWindow();
            about.Owner = this;
            about.ShowDialog();
        }

        /// <summary>
        /// “恢复默认外观”：只把外观相关字段重置为 AppSettings 默认值，
        /// 提醒、日程与当前工作/娱乐状态保留（用户数据不能因为点错按钮丢失）。
        /// 仅改编辑副本 _working，必须再点“应用”才落盘。
        /// </summary>
        private void OnResetDefaults(object sender, RoutedEventArgs e)
        {
            AppLog.Info("Settings", "恢复默认外观：提醒、日程与当前状态保持不变");
            AppSettings defaults = AppSettings.CreateDefault();
            _working.ColorHex = defaults.ColorHex;
            _working.FontFamilyName = defaults.FontFamilyName;
            _working.FontSize = defaults.FontSize;
            _working.Opacity = defaults.Opacity;
            _working.ShowShadow = defaults.ShowShadow;
            _working.Text3D = defaults.Text3D;
            _working.TimeFormat = defaults.TimeFormat;
            _working.TimeZoneId = defaults.TimeZoneId;
            _working.ClickThrough = defaults.ClickThrough;
            _working.Topmost = defaults.Topmost;
            _working.StartWithWindows = defaults.StartWithWindows;
            _working.WeatherEnabled = defaults.WeatherEnabled;
            _working.WeatherCity = defaults.WeatherCity;
            _working.WeatherFontFamilyName = defaults.WeatherFontFamilyName;
            _working.WeatherFontSize = defaults.WeatherFontSize;
            _working.WeatherColorHex = defaults.WeatherColorHex;
            _working.WeatherShowShadow = defaults.WeatherShowShadow;
            _working.WeatherRefreshMinutes = defaults.WeatherRefreshMinutes;
            _working.WeatherFontBold = defaults.WeatherFontBold;
            _working.WeatherFontItalic = defaults.WeatherFontItalic;
            _working.ReminderDisplaySeconds = defaults.ReminderDisplaySeconds;
            _working.ReminderHoverCloseEnabled = defaults.ReminderHoverCloseEnabled;
            _working.ReminderSoundEnabled = defaults.ReminderSoundEnabled;
            _working.ReminderBlinkCount = defaults.ReminderBlinkCount;

            _updating = true;
            try
            {
                // 打开窗口时 _working 已由构造函数 Clone 过，这里再 Normalize 一次，
                // 让旧配置里的越界值在界面上就表现为合法值，而不是等用户点“应用”才被修好。
                LoadControlsFromWorking();
            }
            finally
            {
                _updating = false;
            }
            _footerStatus.Text = "已恢复默认外观（提醒和状态保留）";
            _footerStatus.Foreground = UiTheme.TextMuted;
        }

        /// <summary>
        /// 标记“有未应用的修改”。每个控件事件都会调用一次，属于高频路径，只更新页脚文字、不记日志。
        /// </summary>
        private void MarkDirty()
        {
            _footerStatus.Text = "有未应用的修改";
            _footerStatus.Foreground = UiTheme.TextMuted;
        }

        /// <summary>调色板里一个色块及其对应的 Color（用于“选中即改当前颜色”的映射）。</summary>
        private sealed class ColorPreset
        {
            public ColorPreset(Color color, Border swatch)
            {
                Color = color;
                Swatch = swatch;
            }

            public Color Color { get; private set; }
            public Border Swatch { get; private set; }
        }
    }
}
