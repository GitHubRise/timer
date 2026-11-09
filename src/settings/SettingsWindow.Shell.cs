// 设置窗口的外壳分部类（SettingsWindow.Shell.cs）。
// 职责：搭建无边框窗口骨架——标题栏（兼作拖动区）、实时预览卡、六个页签容器、底部动作栏与状态栏，
// 并负责挂接控件事件、把编辑副本回填到界面、把界面读回编辑副本，以及应用/保存/关闭等外壳动作。
// 契约：WindowStyle=None + ResizeMode=CanResize，最小 640×560，四边缩放交给 WindowChrome
// （ResizeBorderThickness=6）；拖动只走标题 Grid 的 MouseLeftButtonDown → DragMove()（光标 Cursors.SizeAll）。
// 边界：本分部只改 _working 副本，不落盘、不直接开关悬浮窗；落盘与生效由控制器在 ApplyRequested 后完成。

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

    /// <summary>
    /// 设置窗口的无边框外壳分部：负责窗口的整体布局与外壳级交互。
    /// 布局自上而下为标题栏、实时预览卡、页签容器（星号行，占满剩余高度）、底部动作栏；
    /// 窗口在 <see cref="SettingsWindow"/> 构造期一次性构建，关闭后随实例一起丢弃，不保留静态状态。
    /// </summary>
    internal sealed partial class SettingsWindow
    {
        // ===== 对外事件与只读结果 =====

        /// <summary>用户点“应用”且校验通过后触发；控制器据此把 <see cref="Result"/> 落盘并立即生效。</summary>
        public event EventHandler ApplyRequested;

        /// <summary>请求试播提醒预览（铃声/闪烁/悬停）时触发，参数携带用户当前填写的提醒内容。</summary>
        public event EventHandler<ReminderPreviewEventArgs> ReminderTestRequested;

        /// <summary>
        /// 校验通过后供控制器读取的编辑副本；未点“应用/保存并退出”前，它只反映界面上的临时状态，
        /// 不代表已生效的配置。窗口构造时会先 <c>Clone</c> 一份，避免直接改调用方的对象。
        /// </summary>
        public AppSettings Result
        {
            get { return _working; }
        }

        // ===== 界面骨架构建 =====

        /// <summary>
        /// 构建窗口根内容：外层 1px 描边 Border 包住四行 Grid（标题栏 / 预览卡 / 页签 / 页脚）。
        /// 只在构造期调用一次；各页签的实际内容由对应分部提供的 <c>Build*Tab</c> 方法生成。
        /// </summary>
        /// <returns>可直接赋给 <see cref="System.Windows.Window.Content"/> 的根元素。</returns>
        private UIElement BuildWindowContent()
        {
            // 无边框窗口没有系统边框，用 1px 描边勾出窗口轮廓，背景与主题保持一致，避免漏出桌面。
            Border shell = new Border();
            shell.Background = UiTheme.WindowBackground;
            shell.BorderBrush = UiTheme.CardBorder;
            shell.BorderThickness = new Thickness(1);

            // 四行：标题栏与预览卡按内容取高，页签行用星号占满剩余空间，页脚固定在底部。
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标题栏两列：左侧标题块吸收剩余宽度，右侧关闭按钮按内容定宽。
            Grid header = new Grid();
            header.Margin = new Thickness(26, 18, 18, 0);
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 拖动契约：透明背景保证标题区空白处也能命中鼠标，光标提示“可拖动”，
            // 按下左键后统一交给 OnHeaderMouseDown → DragMove()，不把拖动挂在标题文字上。
            header.Background = Brushes.Transparent;
            header.Cursor = Cursors.SizeAll;
            header.MouseLeftButtonDown += OnHeaderMouseDown;

            // 标题与副标题：副标题走 Hint 工厂，受设置界面“灰色提示总开关”集中控制。
            StackPanel titleStack = new StackPanel();
            titleStack.Background = Brushes.Transparent;
            TextBlock title = UiTheme.MakeText("时间悬浮窗设置", 22, UiTheme.TextPrimary, FontWeights.SemiBold);
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

            // 关闭按钮用幽灵样式，尺寸固定 36×32，避免不同 DPI/字体下标题栏高度跳动。
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
            // 关闭不做提交：未应用的修改随窗口一起丢弃，ToolTip 必须把这个后果写清楚。
            closeButton.ToolTip = "关闭（不应用尚未保存的修改）";
            closeButton.Cursor = Cursors.Arrow;
            closeButton.Click += OnClose;
            // 自动化测试与屏幕阅读器依赖稳定的 AutomationId 定位这个按钮。
            AutomationProperties.SetAutomationId(closeButton, "CloseButton");
            header.Children.Add(closeButton);
            Grid.SetColumn(closeButton, 1);

            root.Children.Add(header);
            Grid.SetRow(header, 0);

            // 预览卡左右留白与标题栏对齐（26），底部只留 2px，让页签容器紧贴又不重叠。
            Border previewCard = BuildPreviewCard();
            previewCard.Margin = new Thickness(26, 14, 26, 2);
            root.Children.Add(previewCard);
            Grid.SetRow(previewCard, 1);

            // 页签顺序必须与主文件的 TabAppearance..TabWeather 常量一致，
            // 否则 SelectedIndex / 出错跳转（CommitAll）会指到错误的页面。
            _tabs.Items.Add(CreateTab("桌面悬浮窗", BuildAppearanceTab()));
            _tabs.Items.Add(CreateTab("任务栏时钟", BuildTaskbarTab()));
            _tabs.Items.Add(CreateTab("全局时间格式", BuildFormatTab()));
            _tabs.Items.Add(CreateTab("定时提醒", BuildRemindersTab()));
            _tabs.Items.Add(CreateTab("日程", BuildScheduleTab()));
            _tabs.Items.Add(CreateTab("天气", BuildWeatherTab()));
            _tabs.SelectedIndex = TabAppearance;
            root.Children.Add(_tabs);
            Grid.SetRow(_tabs, 2);

            // 页脚最后加入并固定在第 3 行，保证窗口缩放时始终贴在底部。
            Border footer = BuildFooter();
            root.Children.Add(footer);
            Grid.SetRow(footer, 3);

            shell.Child = root;
            return shell;
        }

        // ===== 事件挂接与状态回填 =====

        /// <summary>
        /// 把主文件构造期创建的控件接到本分部的事件处理器上。
        /// 只在构造末尾调用一次；勾选类控件同时监听 Checked/Unchecked，
        /// 否则取消勾选不会触发处理逻辑，界面与 <c>_working</c> 会不一致。
        /// </summary>
        private void AttachEvents()
        {
            // 桌面悬浮窗外观：字体、字号、透明度、阴影、3D 立体字。
            _fontCombo.SelectionChanged += OnFontChanged;
            _sizeSlider.ValueChanged += OnSizeChanged;
            _opacitySlider.ValueChanged += OnOpacityChanged;
            _shadowCheck.Checked += OnEffectChanged;
            _shadowCheck.Unchecked += OnEffectChanged;
            _threeDCheck.Checked += OnEffectChanged;
            _threeDCheck.Unchecked += OnEffectChanged;

            // 行为开关：鼠标穿透、置顶、开机自启、全屏隐藏，统一走 OnBehaviorChanged。
            _clickThroughCheck.Checked += OnBehaviorChanged;
            _clickThroughCheck.Unchecked += OnBehaviorChanged;
            _topmostCheck.Checked += OnBehaviorChanged;
            _topmostCheck.Unchecked += OnBehaviorChanged;
            _startWithWindowsCheck.Checked += OnBehaviorChanged;
            _startWithWindowsCheck.Unchecked += OnBehaviorChanged;
            _hideFullscreenCheck.Checked += OnBehaviorChanged;
            _hideFullscreenCheck.Unchecked += OnBehaviorChanged;

            // 显示位置：桌面悬浮窗与任务栏时钟互相独立，可同时关闭。
            _desktopOverlayCheck.Checked += OnDisplaySurfaceChanged;
            _desktopOverlayCheck.Unchecked += OnDisplaySurfaceChanged;
            _taskbarClockCheck.Checked += OnDisplaySurfaceChanged;
            _taskbarClockCheck.Unchecked += OnDisplaySurfaceChanged;

            // 任务栏格式：输入框实时改预览，Enter 只应用预览不插入换行，预设下拉负责套用模板。
            _taskbarFormatBox.TextChanged += OnTaskbarFormatChanged;
            _taskbarFormatBox.PreviewKeyDown += OnTaskbarFormatKeyDown;
            _taskbarFormatCombo.SelectionChanged += OnTaskbarFormatPresetChanged;
            _taskbarDotNetFormatRadio.Checked += OnTaskbarFormatModeChanged;
            _taskbarTokenFormatRadio.Checked += OnTaskbarFormatModeChanged;

            // 悬浮窗时间格式：与任务栏同理，但参数各自独立保存。
            _formatCombo.SelectionChanged += OnFormatPresetChanged;
            _formatBox.TextChanged += OnFormatTextChanged;
            _formatBox.PreviewKeyDown += OnFormatKeyDown;
            _dotNetFormatRadio.Checked += OnFormatModeChanged;
            _tokenFormatRadio.Checked += OnFormatModeChanged;

            // 格式变量检索框只过滤帮助列表，不参与保存。
            _formatHelpSearchBox.TextChanged += OnFormatHelpSearchChanged;
            _weatherFormatBox.TextChanged += OnWeatherFormatTextChanged;
            _timeZoneCombo.SelectionChanged += OnTimeZoneChanged;

            // 提醒：内容框里按 Enter 直接加一条；工作/娱乐状态切换会影响后续触发。
            _reminderMessageBox.KeyDown += OnReminderMessageKeyDown;
            _workModeRadio.Checked += OnCurrentStateChanged;
            _leisureModeRadio.Checked += OnCurrentStateChanged;
            _reminderDurationCombo.SelectionChanged += OnReminderDurationChanged;
            _reminderHoverCheck.Checked += OnReminderHoverChanged;
            _reminderHoverCheck.Unchecked += OnReminderHoverChanged;
            _reminderBlinkSlider.ValueChanged += OnReminderBlinkChanged;
            _reminderSoundCheck.Checked += OnReminderSoundChanged;
            _reminderSoundCheck.Unchecked += OnReminderSoundChanged;

            // 天气：开关、城市、字体、字号、阴影、粗斜体、刷新频率逐项绑定。
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
            _weatherRefreshCombo.SelectionChanged += OnWeatherRefreshChanged;

            // 窗口级键盘：Esc 关闭（放弃未应用修改），挂在窗口上而不是某个输入框，
            // 保证焦点在滑杆、下拉框里时也能生效。
            PreviewKeyDown += OnWindowPreviewKeyDown;
        }

        /// <summary>
        /// 用编辑副本 <c>_working</c> 回填全部控件，是“副本 → 界面”的唯一入口。
        /// 构造末尾与“恢复默认”后各调用一次；调用方在此期间把 <c>_updating</c> 置 true，
        /// 使控件事件处理器跳过脏标记与写回，避免回填到一半的状态被当成用户修改。
        /// </summary>
        private void LoadControlsFromWorking()
        {
            // 字体按名字查找；配置里的字体已被卸载/改名时退回目录默认字体，避免下拉框空白。
            FontChoice choice = FontCatalog.FindChoice(_fontChoices, _working.FontFamilyName);
            if (choice == null)
            {
                choice = FontCatalog.FindChoice(_fontChoices, FontCatalog.DefaultFont);
            }
            _fontCombo.SelectedItem = choice;

            // 字号与透明度：滑杆取整值，右侧数值标签同步显示单位，避免用户看到小数。
            _sizeSlider.Value = _working.FontSize;
            _opacitySlider.Value = Math.Round(_working.Opacity * 100.0);
            _sizeValueText.Text = ((int)Math.Round(_working.FontSize)).ToString(CultureInfo.InvariantCulture) + " px";
            _opacityValueText.Text = ((int)Math.Round(_working.Opacity * 100.0)).ToString(CultureInfo.InvariantCulture) + "%";

            // 外观与行为开关按布尔值整体回填。
            _shadowCheck.IsChecked = _working.ShowShadow;
            _threeDCheck.IsChecked = _working.Text3D;
            _clickThroughCheck.IsChecked = _working.ClickThrough;
            _topmostCheck.IsChecked = _working.Topmost;
            _startWithWindowsCheck.IsChecked = _working.StartWithWindows;
            _hideFullscreenCheck.IsChecked = _working.HideOnFullscreen;
            _desktopOverlayCheck.IsChecked = _working.ShowDesktopOverlay;
            _taskbarClockCheck.IsChecked = _working.ShowTaskbarClock;

            // 任务栏外观：加粗与字号档位（档位靠 Tag 匹配，见下）。
            _taskbarBoldCheck.IsChecked = _working.TaskbarBold;
            // 字号档位默认先选第一项（标准），再用 Tag 里的整数精确匹配配置值；
            // 显示文案可能随版本调整，不能拿字符串做匹配。
            _taskbarFontScaleCombo.SelectedIndex = 0;
            for (int i = 0; i < _taskbarFontScaleCombo.Items.Count; i++)
            {
                ComboBoxItem item = _taskbarFontScaleCombo.Items[i] as ComboBoxItem;
                if (item != null && item.Tag is int && (int)item.Tag == _working.TaskbarFontScale)
                {
                    _taskbarFontScaleCombo.SelectedIndex = i;
                    break;
                }
            }

            // 任务栏格式：先按模式换预设列表，再填输入框，最后按当前值重新选中预设项。
            bool taskbarTokenMode = FormatEngine.IsTokenMode(_working.TaskbarFormatMode);
            _taskbarDotNetFormatRadio.IsChecked = !taskbarTokenMode;
            _taskbarTokenFormatRadio.IsChecked = taskbarTokenMode;
            _taskbarActivePresets = taskbarTokenMode ? _taskbarTokenPresets : _taskbarDotNetPresets;
            _taskbarFormatCombo.ItemsSource = _taskbarActivePresets;
            _taskbarFormatBox.Text = taskbarTokenMode ? _working.TaskbarFormat : _working.TaskbarDotNetFormat;
            UpdateTaskbarFormatHint();
            UpdateTaskbarFormatComboSelection();
            // 状态行先写成中性说明；这是引导语，改色后由 SetStatusColor 决定显隐。
            _taskbarFormatStatus.Text = taskbarTokenMode
                ? "简洁变量格式，支持两行；任务栏空间不足时会自动收紧字距。"
                : ".NET 格式，支持两行；任务栏空间不足时会自动收紧字距。";
            SettingsUiHints.SetStatusColor(_taskbarFormatStatus, UiTheme.TextMuted);

            // 提醒当前状态：未知值统一归一化为工作状态，避免两个单选都不选中。
            string mode = ReminderModes.Normalize(_working.CurrentMode);
            _workModeRadio.IsChecked = mode == ReminderModes.Work;
            _leisureModeRadio.IsChecked = mode == ReminderModes.Leisure;
            SelectReminderDurationOption(_working.ReminderDisplaySeconds);
            _reminderHoverCheck.IsChecked = _working.ReminderHoverCloseEnabled;
            _reminderBlinkSlider.Value = _working.ReminderBlinkCount;
            _reminderBlinkValueText.Text = _working.ReminderBlinkCount + " 次";
            _reminderSoundCheck.IsChecked = _working.ReminderSoundEnabled;

            // 天气开关、城市与可用性：关闭天气时城市框禁用，避免改了也不生效造成误解。
            _weatherEnabledCheck.IsChecked = _working.WeatherEnabled;
            _weatherCityBox.Text = _working.WeatherCity;
            _weatherCityBox.IsEnabled = _working.WeatherEnabled;

            // 天气字体三级回退：配置字体 → 微软雅黑 UI → 目录默认，保证一定有选中项。
            FontChoice weatherFont = FontCatalog.FindChoice(_fontChoices, _working.WeatherFontFamilyName);
            if (weatherFont == null)
            {
                weatherFont = FontCatalog.FindChoice(_fontChoices, "Microsoft YaHei UI");
            }
            // 微软雅黑 UI 在精简系统上也可能缺失，最后退到字体目录的默认项。
            if (weatherFont == null)
            {
                weatherFont = FontCatalog.FindChoice(_fontChoices, FontCatalog.DefaultFont);
            }
            _weatherFontCombo.SelectedItem = weatherFont;
            // 天气字号、阴影与粗斜体一并回填。
            _weatherSizeSlider.Value = _working.WeatherFontSize;
            _weatherSizeValueText.Text = ((int)Math.Round(_working.WeatherFontSize)).ToString(CultureInfo.InvariantCulture) + " px";
            _weatherShadowCheck.IsChecked = _working.WeatherShowShadow;
            _weatherBoldCheck.IsChecked = _working.WeatherFontBold;
            _weatherItalicCheck.IsChecked = _working.WeatherFontItalic;
            UpdateWeatherColorUi();
            SelectWeatherRefreshOption(_working.WeatherRefreshMinutes);

            // 悬浮窗时间格式：与任务栏同一套逻辑，但读写 FormatMode / TimeFormat / TokenTimeFormat。
            bool tokenMode = FormatEngine.IsTokenMode(_working.FormatMode);
            _dotNetFormatRadio.IsChecked = !tokenMode;
            _tokenFormatRadio.IsChecked = tokenMode;
            _formatPresets = tokenMode ? _tokenFormatPresets : _dotNetFormatPresets;
            _formatCombo.ItemsSource = _formatPresets;
            _formatBox.Text = tokenMode ? _working.TokenTimeFormat : _working.TimeFormat;
            _weatherFormatBox.Text = _working.WeatherFormat;
            UpdateFormatHelp();
            UpdateWeatherFormatStatus();
            SelectTimeZoneChoice(_working.TimeZoneId);
            UpdateFormatComboSelection();
            _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            SettingsUiHints.SetStatusColor(_formatStatus, UiTheme.TextMuted);

            // 颜色块、提醒行、日程行与预览最后刷新：它们依赖前面回填好的其它状态。
            UpdateColorUi();
            RebuildReminderRows();
            RebuildScheduleRows();
            UpdatePreview();
        }

        // ===== 窗口外壳交互 =====

        /// <summary>
        /// 标题区左键按下 → 拖动窗口。只处理左键按下态，右键留给其它交互。
        /// 拖动是窗口唯一的移动方式（WindowStyle=None 没有系统标题栏可拖）。
        /// </summary>
        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 兼容触摸/笔输入转鼠标事件：只有左键确实处于按下状态才进入拖动。
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // 拖动过程中鼠标已被提前释放或窗口正在关闭时，DragMove 会抛该异常；
                    // 拖动失败不影响其它交互，也没有可恢复的动作，因此安全忽略。
                }
            }
        }

        /// <summary>
        /// 窗口级按键：Esc 关闭窗口。只处理 Esc 并标记 Handled，
        /// 防止它继续冒泡到页签内容（例如清空输入框）造成双重行为。
        /// </summary>
        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 只拦截 Esc：其它按键继续冒泡，输入框、下拉框和滑杆的默认行为才不会被抢走。
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                // 与标题栏关闭按钮一致：关闭即放弃未应用的修改。
                Close();
            }
        }

        /// <summary>关闭按钮：直接关窗，不提交编辑副本（未应用的修改随窗口丢弃）。</summary>
        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ===== 实时预览 =====

        /// <summary>
        /// 按当前编辑副本重画预览卡：时间行总是刷新；天气行仅在“开启天气且格式里没有内联天气变量”时显示，
        /// 避免同一行天气重复出现两次。任何外观相关控件变更都会调用，所以这里不做 IO。
        /// </summary>
        private void UpdatePreview()
        {
            // 取当前显示时区的“现在”，预览随用户切换时区立即变化；天气用样例，不依赖网络。
            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            WeatherInfo sample = CreateSampleWeather();
            string display = FormatEngine.IsTokenMode(_working.FormatMode)
                ? FormatEngine.Format(now, sample, _working.TokenTimeFormat)
                : TimeFormatter.Format(now, _working.TimeFormat);
            _previewText.Text = display;

            // 字体、字号、阴影、3D 一起交给 LayeredText 统一重绘，透明度单独设，避免重复分层。
            _previewText.ApplyStyle(
                FontCatalog.GetFontFamily(_working.FontFamilyName),
                _working.FontSize,
                FontWeights.SemiBold,
                _working.TimeColor,
                _working.ShowShadow,
                _working.Text3D);
            _previewText.Opacity = _working.Opacity;

            // 简洁变量格式里已经包含天气 token 时，不再单独画天气行，否则预览会与真实悬浮窗不一致。
            bool inlineWeather = FormatEngine.IsTokenMode(_working.FormatMode)
                && FormatEngine.ContainsWeatherToken(_working.TokenTimeFormat);
            bool showWeather = _working.WeatherEnabled && !inlineWeather;
            _previewWeatherText.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
            if (!showWeather)
            {
                // 不显示天气时提前返回，省掉下面的字体计算与阴影对象分配。
                return;
            }

            // 城市为空时用“北京”占位，让格式里的城市变量在预览中也有内容可看。
            string city = string.IsNullOrWhiteSpace(_working.WeatherCity) ? "北京" : _working.WeatherCity;
            Color weatherColor = _working.WeatherColor;
            _previewWeatherText.Text = string.IsNullOrWhiteSpace(_working.WeatherFormat)
                ? city + " · 晴 · 12°C · 今日 9°~17°"
                : FormatEngine.Format(now, sample, _working.WeatherFormat);
            _previewWeatherText.FontFamily = FontCatalog.GetFontFamily(_working.WeatherFontFamilyName);
            _previewWeatherText.FontSize = _working.WeatherFontSize;
            _previewWeatherText.FontWeight = _working.WeatherFontBold ? FontWeights.Bold : FontWeights.Normal;
            _previewWeatherText.FontStyle = _working.WeatherFontItalic ? FontStyles.Italic : FontStyles.Normal;
            // 与真实悬浮窗一致：文字本身带 232/255 的 alpha，再整体乘透明度。
            _previewWeatherText.Foreground = UiTheme.BrushFrom(
                Color.FromArgb(232, weatherColor.R, weatherColor.G, weatherColor.B));
            // 行间距按字号比例给最小值 2px，小字号时不会贴得太紧，大字号时也不至于离散太远。
            _previewWeatherText.Margin = new Thickness(0, Math.Max(2, _working.WeatherFontSize * 0.20), 0, 0);
            _previewWeatherText.Opacity = _working.Opacity;
            if (_working.WeatherShowShadow)
            {
                // 阴影颜色按文字亮度反选：深色字配白影、浅色字配黑影，保证任何底色上都可读；
                // 阴影深度与模糊半径都跟字号成比例，缩略预览里不会糊成一团。
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
                // 关闭阴影必须显式置 null：Effect 属性不会因为前面的分支而不存在，留着旧值会继续绘制。
                _previewWeatherText.Effect = null;
            }
        }

        /// <summary>
        /// 构造固定的天气样例（含 6 天预报），让 rtm、温度范围、预报类变量在预览里都有内容。
        /// 纯内存对象，不发起网络请求，因此预览不依赖网络状态。
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

            // 六天数组按下标一一对应（天气/代码/最低/最高），长度必须一致，
            // 循环里只用一个下标取值，写错长度会直接数组越界。
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

        // ===== 提交与按钮动作 =====

        /// <summary>
        /// 把界面上的值统一写回 <c>_working</c>，写回前做格式与提醒行校验。
        /// 校验失败时切到对应页签、把焦点交给出错的输入框，并通过 <paramref name="error"/> 返回原因；
        /// 成功时最后调用 <see cref="AppSettings.Normalize"/> 收敛越界值。
        /// </summary>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>true 表示已全部写回，可以落盘或关闭窗口。</returns>
        private bool CommitAll(out string error)
        {
            error = null;

            // 悬浮窗时间格式：按当前模式走简洁变量或 .NET 两套解析器，解析成功才允许写回。
            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string normalizedFormat;
            string formatError;
            bool formatValid = tokenMode
                ? FormatEngine.TryNormalizeTokenFormat(_formatBox.Text, true, out normalizedFormat, out formatError)
                : TimeFormatter.TryNormalizeFormat(_formatBox.Text, out normalizedFormat, out formatError);
            if (!formatValid)
            {
                // 出错就把格式页顶到前台：用户可能正在别的页签点“应用”，
                // 只弹状态文字他根本看不到，所以必须切页 + 滚动到状态行 + 聚焦输入框。
                _formatStatus.Text = formatError;
                SettingsUiHints.SetStatusColor(_formatStatus, UiTheme.Accent);
                _tabs.SelectedIndex = TabFormat;
                _formatStatus.BringIntoView();
                _formatBox.Focus();
                error = formatError;
                return false;
            }

            // 规范化后的格式串才写回；两种模式各存各的字段，切换模式不会互相覆盖。
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

            // 天气格式允许留空：留空表示“用默认天气行格式”，因此只有非空时才校验。
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
                // 留空表示使用默认天气行格式，显式写空串，避免旧格式残留在模型里。
                _working.WeatherFormat = string.Empty;
            }

            // 任务栏格式与悬浮窗格式是两份独立配置，必须单独校验、单独写回。
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

            // 模式与内容同时写：TaskbarFormat 永远存简洁变量，TaskbarDotNetFormat 永远存 .NET 格式。
            _working.TaskbarFormatMode = taskbarTokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet;
            if (taskbarTokenMode)
            {
                _working.TaskbarFormat = normalizedTaskbarFormat;
            }
            else
            {
                _working.TaskbarDotNetFormat = normalizedTaskbarFormat;
            }

            // 时区下拉可能为空（理论上不会），为空就保留原值，避免把有效配置清成空串。
            TimeZoneChoice timeZoneChoice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            if (timeZoneChoice != null)
            {
                _working.TimeZoneId = timeZoneChoice.Id;
            }
            // 校验通过后把格式状态行恢复成中性提示，别让上次的错误文案残留。
            _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            SettingsUiHints.SetStatusColor(_formatStatus, UiTheme.TextMuted);

            // 外观与行为：逐项从控件取值写回副本，保持“界面即真相”。
            // 字体取选中项；为空说明连回退字体都没找到，此时保留原值而不是清成空串。
            FontChoice font = _fontCombo.SelectedItem as FontChoice;
            if (font != null)
            {
                _working.FontFamilyName = font.Name;
            }
            _working.FontSize = _sizeSlider.Value;
            _working.Opacity = _opacitySlider.Value / 100.0;
            _working.ShowShadow = _shadowCheck.IsChecked == true;
            _working.Text3D = _threeDCheck.IsChecked == true;
            // 行为开关逐项独立，谁也不会覆盖谁；真正生效由控制器在应用之后统一执行。
            _working.ClickThrough = _clickThroughCheck.IsChecked == true;
            _working.Topmost = _topmostCheck.IsChecked == true;
            _working.StartWithWindows = _startWithWindowsCheck.IsChecked == true;
            _working.HideOnFullscreen = _hideFullscreenCheck.IsChecked == true;
            // 显示位置的两个开关互相独立，可同时关闭；关闭只是不显示，不销毁配置。
            _working.ShowDesktopOverlay = _desktopOverlayCheck.IsChecked == true;
            _working.ShowTaskbarClock = _taskbarClockCheck.IsChecked == true;
            // 任务栏字号档位取 Tag 里的整数；取不到就沿用旧值，不让下拉空选把档位清 0。
            ComboBoxItem scaleItem = _taskbarFontScaleCombo.SelectedItem as ComboBoxItem;
            if (scaleItem != null && scaleItem.Tag is int)
            {
                _working.TaskbarFontScale = (int)scaleItem.Tag;
            }
            _working.TaskbarBold = _taskbarBoldCheck.IsChecked == true;
            // 状态只可能是工作/娱乐二选一：工作未选中即按娱乐处理，与单选组语义一致。
            _working.CurrentMode = _workModeRadio.IsChecked == true
                ? ReminderModes.Work
                : ReminderModes.Leisure;
            // 显示时长命中所选项才写回；配置里是列表外的自定义值时沿用旧值。
            ReminderDisplayOption displayOption = _reminderDurationCombo.SelectedItem as ReminderDisplayOption;
            if (displayOption != null)
            {
                _working.ReminderDisplaySeconds = displayOption.Seconds;
            }
            // 提醒参数：当前状态、显示时长、悬停关闭、闪烁次数与铃声逐项写回。
            _working.ReminderHoverCloseEnabled = _reminderHoverCheck.IsChecked == true;
            // 闪烁次数滑杆是整数步进，但值为 double，写回前必须取整。
            _working.ReminderBlinkCount = (int)Math.Round(_reminderBlinkSlider.Value);
            _working.ReminderSoundEnabled = _reminderSoundCheck.IsChecked == true;
            _working.WeatherEnabled = _weatherEnabledCheck.IsChecked == true;
            _working.WeatherCity = (_weatherCityBox.Text ?? string.Empty).Trim();
            // 天气字体同理：取不到就保留旧字体名，不让下拉空选破坏已保存的配置。
            FontChoice weatherFont = _weatherFontCombo.SelectedItem as FontChoice;
            if (weatherFont != null)
            {
                _working.WeatherFontFamilyName = weatherFont.Name;
            }
            _working.WeatherFontSize = _weatherSizeSlider.Value;
            // 天气外观：字号、阴影、粗斜体；颜色只有取色面板一个入口，不在这里写。
            _working.WeatherShowShadow = _weatherShadowCheck.IsChecked == true;
            _working.WeatherFontBold = _weatherBoldCheck.IsChecked == true;
            _working.WeatherFontItalic = _weatherItalicCheck.IsChecked == true;
            // 刷新频率取自预设项；列表外的旧值保持不变，避免把合法分钟数清掉。
            WeatherRefreshOption refreshOption = _weatherRefreshCombo.SelectedItem as WeatherRefreshOption;
            if (refreshOption != null)
            {
                _working.WeatherRefreshMinutes = refreshOption.Minutes;
            }

            // 先把提醒行三个编辑控件的内容同步回模型，再逐行校验：
            // 校验要改的是模型里的时间/内容，不先同步就会校验到旧值。
            SyncReminderRows();
            for (int i = 0; i < _reminderRows.Count; i++)
            {
                // 每行先复位边框，下面只在真正出错的行上染强调色，避免旧错误标记残留。
                ReminderRow row = _reminderRows[i];
                row.TimeBox.BorderBrush = UiTheme.FieldBorder;
                row.MessageBox.BorderBrush = UiTheme.FieldBorder;

                // 时间必须是 HH:mm；规范化结果同时写回控件与模型，保证保存的就是看到的。
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

                // 内容不能为空，否则提醒到点只会闪一个空气泡。
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
                    // 直接粘贴的超长内容按 120 字截断，与输入框 MaxLength 和气泡显示能力对齐。
                    message = message.Substring(0, 120);
                }

                row.Reminder.Time = normalizedTime;
                row.Reminder.Message = message;
                row.TimeBox.Text = normalizedTime;
                row.MessageBox.Text = message;
            }

            // 最后统一收敛越界值（字号、透明度、颜色等），保证落盘前模型一定自洽。
            _working.Normalize();
            return true;
        }

        /// <summary>
        /// “应用”按钮：先提交，成功则通知控制器落盘并刷新页脚状态；失败时 CommitAll
        /// 已经把界面切到出错页签并聚焦输入框，这里直接返回，不再覆盖它的提示。
        /// </summary>
        private void OnApply(object sender, RoutedEventArgs e)
        {
            string error;
            // 提交失败时 CommitAll 已切到出错页签并聚焦输入框，这里直接返回，不覆盖它的提示。
            if (!CommitAll(out error))
            {
                return;
            }

            // 事件可能没有订阅者（例如设计期或测试里单独 new 窗口），判空后再触发。
            if (ApplyRequested != null)
            {
                ApplyRequested(this, EventArgs.Empty);
            }
            // 页脚用成功色明确区分“已应用但窗口还开着”，与本按钮 ToolTip 的语义一致。
            _footerStatus.Text = "已应用，未关闭";
            _footerStatus.Foreground = UiTheme.Success;
        }

        /// <summary>
        /// “保存并退出”：提交成功后设 <c>DialogResult = true</c> 关闭窗口；
        /// 控制器在 <c>ShowDialog</c> 返回后读取 <see cref="Result"/> 落盘，因此这里不直接写文件。
        /// </summary>
        private void OnSaveAndExit(object sender, RoutedEventArgs e)
        {
            string error;
            if (!CommitAll(out error))
            {
                return;
            }
            // 走到这里说明提交成功；设 DialogResult 让 ShowDialog 返回，控制器随后读取 Result 落盘。
            DialogResult = true;
        }

        /// <summary>
        /// 打开“关于”对话框，并把 Owner 设为当前窗口：保证它居中于设置窗口，
        /// 也保证设置窗口关闭时关于框不会变成一个孤立的后台窗口。
        /// </summary>
        private void OnAbout(object sender, RoutedEventArgs e)
        {
            AboutWindow about = new AboutWindow();
            about.Owner = this;
            about.ShowDialog();
        }

        /// <summary>
        /// “恢复默认”：只把外观/格式/提醒参数重置为默认值，提醒条目与日程本身保留
        /// （与页脚提示“提醒和状态保留”一致）。回填期间用 <c>_updating</c> 抑制脏标记，
        /// 否则每个控件赋值都会触发一次“有未应用的修改”。
        /// </summary>
        private void OnResetDefaults(object sender, RoutedEventArgs e)
        {
            // 取一份全新默认配置当只读来源，再把需要的字段逐项抄进编辑副本；
            // 不整体替换 _working 引用，避免控件持有旧副本的绑定失效。
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

            // try/finally 防止回填过程中抛异常把 _updating 永久留在 true，
            // 那样后面所有用户操作都不会再标记“有未应用的修改”。
            _updating = true;
            try
            {
                LoadControlsFromWorking();
            }
            finally
            {
                _updating = false;
            }
            // 恢复默认本身也是未保存的修改，所以页脚用中性色提示，而不是成功色。
            _footerStatus.Text = "已恢复默认外观（提醒和状态保留）";
            _footerStatus.Foreground = UiTheme.TextMuted;
        }

        /// <summary>
        /// 标记“有未应用的修改”：只改页脚文案与颜色，不动 <c>_working</c>。
        /// 真正的写回仍由 <see cref="CommitAll"/> 在用户点应用/保存时统一完成。
        /// </summary>
        private void MarkDirty()
        {
            _footerStatus.Text = "有未应用的修改";
            _footerStatus.Foreground = UiTheme.TextMuted;
        }

        // ===== 内部辅助类型 =====

        /// <summary>
        /// 预设色与其色块的绑定记录：选中某个预设色时，靠 <see cref="Swatch"/> 改描边显示“当前选中”，
        /// 因此色块对象必须在创建时登记下来，不能只存在于可视化树里。
        /// </summary>
        private sealed class ColorPreset
        {

            /// <summary>创建一个预设色记录。仅在 <c>BuildPalette</c> 构建色块时调用。</summary>
            /// <param name="color">预设颜色值。</param>
            /// <param name="swatch">对应的圆形色块，用于刷新选中态描边。</param>
            public ColorPreset(Color color, Border swatch)
            {
                Color = color;
                Swatch = swatch;
            }

            /// <summary>预设颜色值。</summary>
            public Color Color { get; private set; }

            /// <summary>该预设对应的色块控件，选中时改变其描边。</summary>
            public Border Swatch { get; private set; }
        }
    }
}
