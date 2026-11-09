// 设置窗口主文件：页签索引、控件字段、构造与公共骨架。
// 只负责创建全部控件、设置无边框窗口外观、建立编辑副本，并在构造末尾装载当前设置；
// 各页签的布局装配在 SettingsWindow.Appearance / Taskbar / Format / Reminders /
// Schedule / Weather 等分部文件，界面骨架与事件绑定在 SettingsWindow.Shell。
// 约束：窗口编辑的是传入设置的深拷贝，点「应用」后才由控制器写回并持久化；
// 构造期间用 _updating 屏蔽控件事件，避免装载初值时触发校验与预览。

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
    /// 设置窗口：无边框、可缩放，六个页签分别编辑外观、任务栏、格式、提醒、日程与天气。
    /// 生命周期由控制器管理（再次启动程序会唤醒已打开的实例）；关闭窗口时未应用的修改直接丢弃。
    /// </summary>
    internal sealed partial class SettingsWindow : Window
    {
        // ===== 页签索引 =====

        /// <summary>「桌面悬浮窗」页签索引，与 Shell 中页签的添加顺序一致。</summary>
        private const int TabAppearance = 0;

        /// <summary>「任务栏时钟」页签索引。</summary>
        private const int TabTaskbar = 1;

        /// <summary>「时间格式」页签索引。</summary>
        private const int TabFormat = 2;

        /// <summary>「定时提醒」页签索引。</summary>
        private const int TabReminders = 3;

        /// <summary>「日程」页签索引。</summary>
        private const int TabSchedule = 4;

        /// <summary>「天气」页签索引。</summary>
        private const int TabWeather = 5;

        // ===== 编辑副本与外观页控件 =====

        /// <summary>编辑副本：构造时从传入设置深拷贝，各页签只改这份副本，点「应用」后才由控制器写回。</summary>
        private readonly AppSettings _working;

        /// <summary>悬浮窗预览控件，直接复用运行时的 <see cref="LayeredText"/>，保证预览与真实渲染一致。</summary>
        private readonly LayeredText _previewText;

        /// <summary>外观页的颜色预设列表（含自定义入口），由 Appearance 分部填充。</summary>
        private readonly List<ColorPreset> _presets;

        /// <summary>可选字体列表，来自 FontCatalog；时间字体与天气字体下拉框共用。</summary>
        private readonly List<FontChoice> _fontChoices;

        /// <summary>时间文字字体下拉框，显示字体名与示例字形。</summary>
        private readonly ComboBox _fontCombo;

        /// <summary>时间字号滑块，范围 16–160。</summary>
        private readonly Slider _sizeSlider;

        /// <summary>字号数值文本（如「52 px」），与滑块联动。</summary>
        private readonly TextBlock _sizeValueText;

        /// <summary>透明度滑块，界面用 20–100 的百分比，换算出 0.20–1.00 后保存。</summary>
        private readonly Slider _opacitySlider;

        /// <summary>透明度百分比文本，与滑块联动。</summary>
        private readonly TextBlock _opacityValueText;

        /// <summary>时间文字颜色色块，点击后打开取色面板。</summary>
        private readonly Border _colorSwatch;

        /// <summary>时间颜色十六进制文本（如「#FF3B30」），随色块更新。</summary>
        private readonly TextBlock _colorHexText;

        /// <summary>「文字阴影」开关，对应 <see cref="AppSettings.ShowShadow"/>。</summary>
        private readonly CheckBox _shadowCheck;

        /// <summary>「3D 立体字」开关，对应 <see cref="AppSettings.Text3D"/>。</summary>
        private readonly CheckBox _threeDCheck;

        /// <summary>「鼠标穿透」开关；开启后只能从托盘菜单恢复，提交前需要二次确认。</summary>
        private readonly CheckBox _clickThroughCheck;

        /// <summary>「始终置顶」开关。</summary>
        private readonly CheckBox _topmostCheck;

        /// <summary>「开机自动启动」开关，只写当前用户的启动项，不申请管理员权限。</summary>
        private readonly CheckBox _startWithWindowsCheck;

        /// <summary>「全屏时自动隐藏」开关，对应 <see cref="AppSettings.HideOnFullscreen"/>。</summary>
        private readonly CheckBox _hideFullscreenCheck;

        /// <summary>「显示桌面悬浮窗」开关，与任务栏时钟开关互相独立。</summary>
        private readonly CheckBox _desktopOverlayCheck;

        /// <summary>「显示任务栏时钟」开关，只控制覆盖窗，不移动系统原生时钟。</summary>
        private readonly CheckBox _taskbarClockCheck;

        // ===== 任务栏页控件 =====

        /// <summary>任务栏字号缩放下拉框，提供 100/112/124/136 四档，默认 100（跟随系统）。</summary>
        private readonly ComboBox _taskbarFontScaleCombo;

        /// <summary>任务栏「加粗显示」开关。</summary>
        private readonly CheckBox _taskbarBoldCheck;

        /// <summary>任务栏格式输入框，最多两行，始终保存简洁变量格式。</summary>
        private readonly TextBox _taskbarFormatBox;

        /// <summary>任务栏格式校验状态行：合法时是灰色引导，非法时改为失败色。</summary>
        private readonly TextBlock _taskbarFormatStatus;

        /// <summary>任务栏常用格式下拉框，切换格式模式时整体替换数据源。</summary>
        private readonly ComboBox _taskbarFormatCombo;

        /// <summary>任务栏「兼容 .NET」模式单选按钮。</summary>
        private readonly RadioButton _taskbarDotNetFormatRadio;

        /// <summary>任务栏「简洁变量」模式单选按钮（默认）。</summary>
        private readonly RadioButton _taskbarTokenFormatRadio;

        /// <summary>任务栏格式说明文字，经 SettingsUiHints 创建，关闭提示时自动折叠。</summary>
        private readonly TextBlock _taskbarFormatHintText;

        /// <summary>任务栏 .NET 格式预设列表，首项是「自定义格式」占位。</summary>
        private readonly List<TimeFormatPreset> _taskbarDotNetPresets;

        /// <summary>任务栏简洁变量格式预设列表，首项是「自定义格式」占位。</summary>
        private readonly List<TimeFormatPreset> _taskbarTokenPresets;

        /// <summary>「自定义格式」占位项，选中它表示以输入框内容为准。</summary>
        private readonly TimeFormatPreset _taskbarCustomPreset;

        /// <summary>当前任务栏模式下生效的预设列表，切换模式时在两组预设间切换。</summary>
        private List<TimeFormatPreset> _taskbarActivePresets;

        // ===== 天气页控件 =====

        /// <summary>「显示天气预报」开关。</summary>
        private readonly CheckBox _weatherEnabledCheck;

        /// <summary>天气城市输入框，最长 40 字，提交时去首尾空格。</summary>
        private readonly TextBox _weatherCityBox;

        /// <summary>「测试地名」按钮，点击后异步查询公共天气 API，只验证不落盘。</summary>
        private readonly Button _weatherTestButton;

        /// <summary>天气测试状态行，显示查询中、成功或失败。</summary>
        private readonly TextBlock _weatherTestStatus;

        /// <summary>天气字体下拉框，与时间字体的候选列表共用。</summary>
        private readonly ComboBox _weatherFontCombo;

        /// <summary>天气字号滑块，范围 9–60。</summary>
        private readonly Slider _weatherSizeSlider;

        /// <summary>天气字号数值文本（如「15 px」）。</summary>
        private readonly TextBlock _weatherSizeValueText;

        /// <summary>天气文字颜色色块。</summary>
        private readonly Border _weatherColorSwatch;

        /// <summary>天气颜色十六进制文本。</summary>
        private readonly TextBlock _weatherColorHexText;

        /// <summary>天气文字阴影开关，与时间文字的阴影设置相互独立。</summary>
        private readonly CheckBox _weatherShadowCheck;

        /// <summary>天气文字加粗开关。</summary>
        private readonly CheckBox _weatherBoldCheck;

        /// <summary>天气文字斜体开关。</summary>
        private readonly CheckBox _weatherItalicCheck;

        /// <summary>天气预览文字，位于时间预览下方，随天气设置刷新。</summary>
        private readonly TextBlock _previewWeatherText;

        /// <summary>天气颜色预设列表，由 Weather 分部填充。</summary>
        private readonly List<ColorPreset> _weatherColorPresets;

        /// <summary>天气刷新频率下拉框，数据来源 <see cref="_weatherRefreshOptions"/>。</summary>
        private readonly ComboBox _weatherRefreshCombo;

        /// <summary>天气刷新频率的可选档位，来自 WeatherRefreshOptions。</summary>
        private readonly List<WeatherRefreshOption> _weatherRefreshOptions;

        /// <summary>天气测试请求序号：回调时序号不一致说明已有更新的请求，旧结果直接丢弃。</summary>
        private int _weatherTestVersion;

        // ===== 页签容器 =====

        /// <summary>页签容器；六个页签内容由 Shell 分部构建后依次加入。</summary>
        private readonly TabControl _tabs;

        // ===== 格式页控件 =====

        /// <summary>格式页当前生效的预设列表，指向 <see cref="_dotNetFormatPresets"/> 或 <see cref="_tokenFormatPresets"/>。</summary>
        private List<TimeFormatPreset> _formatPresets;

        /// <summary>悬浮窗 .NET 格式预设列表，首项是「自定义格式」占位。</summary>
        private readonly List<TimeFormatPreset> _dotNetFormatPresets;

        /// <summary>悬浮窗简洁变量格式预设列表，首项是「自定义格式」占位。</summary>
        private readonly List<TimeFormatPreset> _tokenFormatPresets;

        /// <summary>「自定义格式」占位项，两种模式共用同一个对象。</summary>
        private readonly TimeFormatPreset _customFormatPreset;

        /// <summary>悬浮窗常用格式下拉框，切换模式时重绑数据源。</summary>
        private readonly ComboBox _formatCombo;

        /// <summary>悬浮窗自定义格式输入框，最多两行；Enter 应用预览，Ctrl+Enter 插入换行。</summary>
        private readonly TextBox _formatBox;

        /// <summary>悬浮窗格式校验状态行，合法与非法使用不同颜色。</summary>
        private readonly TextBlock _formatStatus;

        /// <summary>格式说明文字，随当前模式解释 .NET 或简洁变量语法。</summary>
        private readonly TextBlock _formatHintText;

        /// <summary>变量说明搜索框，按关键词过滤下方条目。</summary>
        private readonly TextBox _formatHelpSearchBox;

        /// <summary>变量说明条目容器，条目按需创建以缩短打开窗口的耗时。</summary>
        private readonly StackPanel _formatHelpPanel;

        /// <summary>变量说明滚动区，高度随页签剩余空间自适应。</summary>
        private readonly ScrollViewer _formatHelpScroll;

        /// <summary>变量说明命中条数文本。</summary>
        private readonly TextBlock _formatHelpCount;

        /// <summary>变量说明全集，首次进入格式页时由 FormatEngine 生成并缓存。</summary>
        private List<FormatHelpItem> _formatHelpItems;

        /// <summary>悬浮窗「兼容 .NET」模式单选按钮。</summary>
        private readonly RadioButton _dotNetFormatRadio;

        /// <summary>悬浮窗「简洁变量」模式单选按钮（默认）。</summary>
        private readonly RadioButton _tokenFormatRadio;

        /// <summary>天气行格式输入框；留空表示使用内置默认格式。</summary>
        private readonly TextBox _weatherFormatBox;

        /// <summary>天气格式状态行。</summary>
        private readonly TextBlock _weatherFormatStatus;

        /// <summary>显示时区下拉框，数据来自 TimeZoneCatalog。</summary>
        private readonly ComboBox _timeZoneCombo;

        /// <summary>时区候选列表，包含「同步系统时区」这一特殊项。</summary>
        private readonly List<TimeZoneChoice> _timeZoneChoices;

        /// <summary>时区状态行，显示当前选择的 UTC 偏移。</summary>
        private readonly TextBlock _timeZoneStatus;

        // ===== 提醒页控件 =====

        /// <summary>新提醒的时间输入框，格式 HH:mm，最长 5 个字符。</summary>
        private readonly TextBox _reminderTimeBox;

        /// <summary>新提醒的内容输入框，最长 120 字。</summary>
        private readonly TextBox _reminderMessageBox;

        /// <summary>新提醒适用状态下拉框（全部/仅工作/仅娱乐）。</summary>
        private readonly ComboBox _newReminderModeCombo;

        /// <summary>提醒状态选项列表，来自 ReminderModes。</summary>
        private readonly List<ReminderModeOption> _reminderModeOptions;

        /// <summary>当前状态「工作」单选按钮。</summary>
        private readonly RadioButton _workModeRadio;

        /// <summary>当前状态「娱乐」单选按钮。</summary>
        private readonly RadioButton _leisureModeRadio;

        /// <summary>提醒显示时长下拉框，按秒取值。</summary>
        private readonly ComboBox _reminderDurationCombo;

        /// <summary>「检测鼠标悬停（悬停 3 秒关闭）」开关。</summary>
        private readonly CheckBox _reminderHoverCheck;

        /// <summary>提醒闪烁次数滑块，范围 0–20，0 表示不闪烁。</summary>
        private readonly Slider _reminderBlinkSlider;

        /// <summary>闪烁次数文本（如「3 次」）。</summary>
        private readonly TextBlock _reminderBlinkValueText;

        /// <summary>提醒铃声开关，关闭后完全静音。</summary>
        private readonly CheckBox _reminderSoundCheck;

        /// <summary>提醒显示时长的可选档位，来自 ReminderDisplayOptions。</summary>
        private readonly List<ReminderDisplayOption> _reminderDisplayOptions;

        /// <summary>提醒页状态行，显示添加结果与输入校验错误。</summary>
        private readonly TextBlock _reminderStatus;

        /// <summary>提醒行容器，刷新列表时整体重建。</summary>
        private readonly StackPanel _reminderRowsPanel;

        /// <summary>提醒列表滚动区，限高 218，避免条数多时把窗口撑破。</summary>
        private readonly ScrollViewer _reminderScroll;

        /// <summary>提醒为空时的引导文字。</summary>
        private readonly TextBlock _reminderEmptyText;

        /// <summary>提醒条数文本，右对齐显示。</summary>
        private readonly TextBlock _reminderCountText;

        /// <summary>当前渲染的提醒行控件，与 <see cref="AppSettings.Reminders"/> 一一对应。</summary>
        private readonly List<ReminderRow> _reminderRows;

        // ===== 日程页控件 =====

        /// <summary>当前渲染的日程行控件，与 <see cref="AppSettings.Schedules"/> 一一对应。</summary>
        private readonly List<ScheduleRow> _scheduleRows;

        /// <summary>日程筛选输入框，文本变化时实时过滤列表。</summary>
        private readonly TextBox _scheduleFilterBox;

        /// <summary>筛选说明文字，提示可搜索的字段。</summary>
        private readonly TextBlock _scheduleFilterHint;

        /// <summary>日程页状态行，显示筛选结果与操作反馈。</summary>
        private readonly TextBlock _scheduleStatus;

        /// <summary>日程行容器。</summary>
        private readonly StackPanel _scheduleRowsPanel;

        /// <summary>日程列表滚动区，限高 430。</summary>
        private readonly ScrollViewer _scheduleScroll;

        /// <summary>日程为空时的引导文字。</summary>
        private readonly TextBlock _scheduleEmptyText;

        /// <summary>日程条数文本，右对齐显示。</summary>
        private readonly TextBlock _scheduleCountText;

        /// <summary>「添加日程」按钮；延迟创建，刷新列表时才确保存在，因此不是 readonly。</summary>
        private Button _scheduleAddButton;

        // ===== 公共状态 =====

        /// <summary>底部状态行，显示保存成功或失败等全局提示。</summary>
        private readonly TextBlock _footerStatus;

        /// <summary>装载/刷新控件期间为 true，事件处理器据此跳过回写，避免事件回环。</summary>
        private bool _updating;

        /// <summary>
        /// 创建窗口并完成全部初始化：设置无边框外观、创建各页控件、构建界面树、
        /// 从编辑副本装载当前值，最后绑定事件并刷新首帧预览。
        /// </summary>
        /// <param name="current">当前生效的设置；构造时克隆一份，原对象不会被直接修改。</param>
        public SettingsWindow(AppSettings current)
        {
            // 编辑副本：不在控制器持有的对象上直接改，点「应用」时才整体提交。
            _working = current != null ? current.Clone() : AppSettings.CreateDefault();
            // 先归一化，滑块与下拉框才能拿到合法范围内的初值。
            _working.Normalize();

            // 全局替换滚动条样式，深色主题下原生滚动条过于突兀。
            Resources.Add(typeof(ScrollBar), ModernScrollBar.CreateStyle());

            // 无边框窗口：靠 WindowChrome 提供四边缩放，拖动由 Shell 中标题栏的 DragMove 负责。
            Title = "时间悬浮窗设置";
            Width = 680;
            Height = 820;
            MinWidth = 640;
            MinHeight = 560;
            ResizeMode = ResizeMode.CanResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            // 居中打开，避免首次运行时窗口落在屏幕外；悬浮窗自身位置另由 Left/Top 保存。
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            Topmost = true;
            Background = UiTheme.WindowBackground;
            Foreground = UiTheme.TextPrimary;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            // 预设数据：颜色、字体、格式、时区、提醒模式与显示时长都在这里一次建好复用。
            _presets = new List<ColorPreset>();
            _weatherColorPresets = new List<ColorPreset>();
            _fontChoices = FontCatalog.GetChoices();
            // 格式预设用当前时间做样例，下拉框里直接显示实际效果。
            DateTime presetSample = DateTime.Now;
            _dotNetFormatPresets = TimeFormatter.CreatePresets(presetSample);
            _tokenFormatPresets = FormatEngine.CreatePresets(presetSample);
            _formatPresets = _dotNetFormatPresets;
            _customFormatPreset = new TimeFormatPreset("自定义格式", null, "使用下方输入框");
            _dotNetFormatPresets.Insert(0, _customFormatPreset);
            _tokenFormatPresets.Insert(0, _customFormatPreset);
            _timeZoneChoices = TimeZoneCatalog.GetChoices();
            _reminderRows = new List<ReminderRow>();
            _reminderModeOptions = ReminderModes.CreateOptions();
            _reminderDisplayOptions = ReminderDisplayOptions.Create();
            _scheduleRows = new List<ScheduleRow>();

            // 悬浮窗预览：复用运行时控件，字体、阴影与 3D 效果都与真实窗口一致。
            _previewText = new LayeredText();
            _previewText.Margin = new Thickness(10);

            // 天气预览文字只负责占位，真正内容在 UpdatePreview 中按当前设置刷新。
            _previewWeatherText = new TextBlock();
            _previewWeatherText.Text = "北京 · 晴 · 12°C · 今日 9°~17°";
            _previewWeatherText.FontFamily = new FontFamily("Microsoft YaHei UI");
            _previewWeatherText.FontSize = 15;
            _previewWeatherText.TextAlignment = TextAlignment.Center;
            _previewWeatherText.HorizontalAlignment = HorizontalAlignment.Center;
            _previewWeatherText.TextWrapping = TextWrapping.NoWrap;
            _previewWeatherText.Margin = new Thickness(0, 4, 0, 0);
            _previewWeatherText.Visibility = Visibility.Collapsed;

            // 外观页控件：字体、字号、透明度、颜色与各种开关，只在构造时创建一次。
            _fontCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_fontCombo);
            _fontCombo.ItemsSource = _fontChoices;
            _fontCombo.ItemTemplate = CreateFontItemTemplate();
            _fontCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_fontCombo, "FontCombo");
            AutomationProperties.SetName(_fontCombo, "字体");

            // 滑块按像素取整，数值文本单独维护，拖动时只改文本、不重建控件。
            _sizeSlider = CreateSlider(16, 160, 52, "FontSizeSlider");
            _sizeValueText = UiTheme.MakeText("52 px", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _sizeValueText.HorizontalAlignment = HorizontalAlignment.Right;

            _opacitySlider = CreateSlider(20, 100, 92, "OpacitySlider");
            _opacityValueText = UiTheme.MakeText("92%", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _opacityValueText.HorizontalAlignment = HorizontalAlignment.Right;

            // 色块与十六进制文本成对出现：色块负责点击取色，文本只读展示当前值。
            _colorSwatch = new Border();
            _colorSwatch.Width = 26;
            _colorSwatch.Height = 26;
            _colorSwatch.CornerRadius = new CornerRadius(7);
            _colorSwatch.BorderThickness = new Thickness(1);
            _colorSwatch.BorderBrush = UiTheme.FieldBorder;
            _colorSwatch.VerticalAlignment = VerticalAlignment.Center;

            _colorHexText = UiTheme.MakeText("#FF3B30", 12.5, UiTheme.TextPrimary, FontWeights.Normal);
            _colorHexText.VerticalAlignment = VerticalAlignment.Center;
            _colorHexText.Margin = new Thickness(10, 0, 0, 0);
            _colorHexText.FontFamily = new FontFamily("Consolas");

            // 开关文案里的默认值与 AppSettings 的默认值保持一致，装载后会被实际值覆盖。
            _shadowCheck = CreateCheckBox("文字阴影", true, "ShadowCheck", "为文字添加柔和的黑色阴影，提升游戏画面上的可读性");
            _threeDCheck = CreateCheckBox("3D 立体字", false, "Text3DCheck", "为文字添加立体厚度；与阴影可同时开启");
            _clickThroughCheck = CreateCheckBox(
                "鼠标穿透（从系统托盘恢复）",
                false,
                "ClickThroughCheck",
                "开启后悬浮窗不接收鼠标；请右键系统托盘图标恢复。");
            _topmostCheck = CreateCheckBox("始终置顶", true, "TopmostCheck", "保持在游戏和其他窗口上方");
            _startWithWindowsCheck = CreateCheckBox(
                "开机自动启动（当前用户）",
                false,
                "StartWithWindowsCheck",
                "登录 Windows 后自动启动时间悬浮窗；不需要管理员权限。");
            _hideFullscreenCheck = CreateCheckBox(
                "全屏时自动隐藏",
                true,
                "HideFullscreenCheck",
                "检测到游戏或全屏视频时暂时隐藏悬浮窗，退出全屏后自动恢复。");
            _desktopOverlayCheck = CreateCheckBox(
                "显示桌面悬浮窗",
                true,
                "DesktopOverlayCheck",
                "在桌面上显示可拖动的时间悬浮窗；关闭后不影响任务栏时钟。");
            _taskbarClockCheck = CreateCheckBox(
                "显示任务栏时钟",
                true,
                "TaskbarClockCheck",
                "在任务栏时间区域显示两行时钟，外观跟随系统；不移动、不隐藏系统时钟。");

            // 任务栏页：外观跟随系统，界面只暴露缩放比例、加粗与两套格式。
            _taskbarFontScaleCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_taskbarFontScaleCombo);
            _taskbarFontScaleCombo.Items.Add(CreateTaskbarScaleItem(100, "标准（跟随系统，默认）"));
            _taskbarFontScaleCombo.Items.Add(CreateTaskbarScaleItem(112, "较大（+12%）"));
            _taskbarFontScaleCombo.Items.Add(CreateTaskbarScaleItem(124, "大（+24%）"));
            _taskbarFontScaleCombo.Items.Add(CreateTaskbarScaleItem(136, "更大（+36%）"));
            _taskbarFontScaleCombo.SelectedIndex = 0;
            _taskbarFontScaleCombo.MaxDropDownHeight = 260;
            AutomationProperties.SetAutomationId(_taskbarFontScaleCombo, "TaskbarFontScaleCombo");
            AutomationProperties.SetName(_taskbarFontScaleCombo, "任务栏时钟字号");

            _taskbarBoldCheck = CreateCheckBox(
                "加粗显示",
                false,
                "TaskbarBoldCheck",
                "让任务栏时钟文字更粗；字重越大，按宽度收紧得越多。");

            _taskbarFormatBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_taskbarFormatBox);
            _taskbarFormatBox.FontFamily = new FontFamily("Consolas");
            _taskbarFormatBox.FontSize = 13;
            _taskbarFormatBox.Height = 62;
            _taskbarFormatBox.AcceptsReturn = true;
            _taskbarFormatBox.MaxLines = 2;
            _taskbarFormatBox.TextWrapping = TextWrapping.Wrap;
            _taskbarFormatBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _taskbarFormatBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            AutomationProperties.SetAutomationId(_taskbarFormatBox, "TaskbarFormatBox");
            AutomationProperties.SetName(_taskbarFormatBox, "任务栏时钟格式");

            // 状态行走 SettingsUiHints：关闭提示时折叠，但校验失败的红字仍会显示。
            _taskbarFormatStatus = SettingsUiHints.Hint(
                "简洁变量格式，支持两行；任务栏空间不足时会自动收紧字距。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _taskbarFormatStatus.Margin = new Thickness(0, 6, 0, 0);
            _taskbarFormatStatus.TextWrapping = TextWrapping.Wrap;

            // 任务栏的两套预设与悬浮窗分开建，切换模式时只换下拉框的数据源。
            _taskbarDotNetPresets = TimeFormatter.CreatePresets(presetSample);
            _taskbarTokenPresets = FormatEngine.CreatePresets(presetSample);
            _taskbarCustomPreset = new TimeFormatPreset("自定义格式", null, "使用下方输入框");
            _taskbarDotNetPresets.Insert(0, _taskbarCustomPreset);
            _taskbarTokenPresets.Insert(0, _taskbarCustomPreset);
            _taskbarActivePresets = _taskbarTokenPresets;

            _taskbarFormatCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_taskbarFormatCombo);
            _taskbarFormatCombo.ItemsSource = _taskbarActivePresets;
            _taskbarFormatCombo.DisplayMemberPath = "Display";
            _taskbarFormatCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_taskbarFormatCombo, "TaskbarFormatPresetCombo");
            AutomationProperties.SetName(_taskbarFormatCombo, "任务栏常用格式");

            // 两个单选按钮共用 GroupName，保证同一时刻只有一种格式语言生效。
            _taskbarDotNetFormatRadio = new RadioButton();
            _taskbarDotNetFormatRadio.Content = "兼容 .NET";
            _taskbarDotNetFormatRadio.GroupName = "TaskbarFormatMode";
            _taskbarDotNetFormatRadio.Foreground = UiTheme.TextPrimary;
            _taskbarDotNetFormatRadio.FontSize = 13;
            _taskbarDotNetFormatRadio.Margin = new Thickness(0, 3, 22, 3);
            _taskbarDotNetFormatRadio.VerticalContentAlignment = VerticalAlignment.Center;
            _taskbarDotNetFormatRadio.ToolTip = "任务栏时钟使用 .NET 日期格式";
            UiTheme.ApplyRadioButtonStyle(_taskbarDotNetFormatRadio);
            AutomationProperties.SetAutomationId(_taskbarDotNetFormatRadio, "TaskbarDotNetFormatRadio");

            _taskbarTokenFormatRadio = new RadioButton();
            _taskbarTokenFormatRadio.Content = "简洁变量（默认）";
            _taskbarTokenFormatRadio.GroupName = "TaskbarFormatMode";
            _taskbarTokenFormatRadio.Foreground = UiTheme.TextPrimary;
            _taskbarTokenFormatRadio.FontSize = 13;
            _taskbarTokenFormatRadio.Margin = new Thickness(0, 3, 22, 3);
            _taskbarTokenFormatRadio.VerticalContentAlignment = VerticalAlignment.Center;
            _taskbarTokenFormatRadio.ToolTip = "任务栏时钟使用简洁变量格式";
            UiTheme.ApplyRadioButtonStyle(_taskbarTokenFormatRadio);
            AutomationProperties.SetAutomationId(_taskbarTokenFormatRadio, "TaskbarTokenFormatRadio");

            _taskbarFormatHintText = SettingsUiHints.Hint(
                "支持简洁变量。输入时实时更新，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _taskbarFormatHintText.Margin = new Thickness(0, 0, 0, 8);
            _taskbarFormatHintText.TextWrapping = TextWrapping.Wrap;

            // 天气页：开关、城市、测试按钮、字体颜色与刷新频率。
            _weatherEnabledCheck = CreateCheckBox(
                "显示天气预报（默认开启）",
                true,
                "WeatherEnabledCheck",
                "通过 Open-Meteo 公共 API 查询当前天气和今日温度范围");
            _weatherCityBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_weatherCityBox);
            _weatherCityBox.Text = "北京";
            _weatherCityBox.MaxLength = 40;
            _weatherCityBox.Height = 32;
            AutomationProperties.SetAutomationId(_weatherCityBox, "WeatherCityBox");
            AutomationProperties.SetName(_weatherCityBox, "天气城市");

            _weatherTestButton = new Button();
            _weatherTestButton.Content = "测试地名";
            _weatherTestButton.Style = UiTheme.SecondaryButtonStyle;
            _weatherTestButton.Height = 32;
            _weatherTestButton.MinWidth = 86;
            _weatherTestButton.Padding = new Thickness(12, 5, 12, 5);
            _weatherTestButton.Margin = new Thickness(8, 0, 0, 0);
            _weatherTestButton.ToolTip = "用当前城市名查询公共天气 API，确认地名能被正确解析";
            // 只有点测试按钮时才发请求；查询结果只写状态行，不修改正在显示的天气。
            _weatherTestButton.Click += OnTestWeatherCity;
            AutomationProperties.SetAutomationId(_weatherTestButton, "TestWeatherButton");

            _weatherTestStatus = SettingsUiHints.Hint(
                "输入城市名后，可先测试查询是否成功；不会修改正在显示的天气。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _weatherTestStatus.Margin = new Thickness(0, 8, 0, 0);
            _weatherTestStatus.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(_weatherTestStatus, "WeatherTestStatus");

            _weatherFontCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_weatherFontCombo);
            _weatherFontCombo.ItemsSource = _fontChoices;
            _weatherFontCombo.ItemTemplate = CreateFontItemTemplate();
            _weatherFontCombo.MaxDropDownHeight = 320;
            AutomationProperties.SetAutomationId(_weatherFontCombo, "WeatherFontCombo");
            AutomationProperties.SetName(_weatherFontCombo, "天气字体");

            _weatherSizeSlider = CreateSlider(9, 60, 15, "WeatherFontSizeSlider");
            _weatherSizeValueText = UiTheme.MakeText("15 px", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _weatherSizeValueText.HorizontalAlignment = HorizontalAlignment.Right;

            _weatherColorSwatch = new Border();
            _weatherColorSwatch.Width = 26;
            _weatherColorSwatch.Height = 26;
            _weatherColorSwatch.CornerRadius = new CornerRadius(7);
            _weatherColorSwatch.BorderThickness = new Thickness(1);
            _weatherColorSwatch.BorderBrush = UiTheme.FieldBorder;
            _weatherColorSwatch.VerticalAlignment = VerticalAlignment.Center;

            _weatherColorHexText = UiTheme.MakeText("#FF3B30", 12.5, UiTheme.TextPrimary, FontWeights.Normal);
            _weatherColorHexText.VerticalAlignment = VerticalAlignment.Center;
            _weatherColorHexText.Margin = new Thickness(10, 0, 0, 0);
            _weatherColorHexText.FontFamily = new FontFamily("Consolas");

            _weatherShadowCheck = CreateCheckBox(
                "天气文字阴影",
                true,
                "WeatherShadowCheck",
                "为天气行单独添加阴影；与时间文字的阴影设置相互独立");

            // 粗体与斜体并排放在同一行，边距里带右间距区分两个复选框。
            _weatherBoldCheck = CreateCheckBox(
                "粗体",
                false,
                "WeatherBoldCheck",
                "天气文字使用粗体");
            _weatherBoldCheck.Margin = new Thickness(0, 2, 18, 4);

            _weatherItalicCheck = CreateCheckBox(
                "倾斜",
                false,
                "WeatherItalicCheck",
                "天气文字使用斜体");
            _weatherItalicCheck.Margin = new Thickness(0, 2, 0, 4);

            // 刷新频率用 SelectedValue 绑定分钟数，避免依赖显示文案。
            _weatherRefreshOptions = WeatherRefreshOptions.Create();
            _weatherRefreshCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_weatherRefreshCombo);
            _weatherRefreshCombo.ItemsSource = _weatherRefreshOptions;
            _weatherRefreshCombo.DisplayMemberPath = "Label";
            _weatherRefreshCombo.SelectedValuePath = "Minutes";
            _weatherRefreshCombo.SelectedValue = 30;
            AutomationProperties.SetAutomationId(_weatherRefreshCombo, "WeatherRefreshCombo");
            AutomationProperties.SetName(_weatherRefreshCombo, "天气更新频率");

            // 格式页：两套格式语言各存一份，切换模式只换预设列表，不改写输入框内容。
            _formatCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_formatCombo);
            _formatCombo.ItemsSource = _formatPresets;
            _formatCombo.DisplayMemberPath = "Display";
            _formatCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_formatCombo, "FormatPresetCombo");
            AutomationProperties.SetName(_formatCombo, "常用时间格式");

            // 格式输入框允许两行并禁用横向滚动，长格式自动折行而不是撑宽窗口。
            _formatBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_formatBox);
            _formatBox.FontFamily = new FontFamily("Consolas");
            _formatBox.FontSize = 13;
            _formatBox.Height = 62;
            _formatBox.AcceptsReturn = true;
            _formatBox.MaxLines = 2;
            _formatBox.TextWrapping = TextWrapping.Wrap;
            _formatBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _formatBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            AutomationProperties.SetAutomationId(_formatBox, "CustomFormatBox");
            AutomationProperties.SetName(_formatBox, "自定义时间格式");

            _formatStatus = SettingsUiHints.Hint("输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。", 11, UiTheme.TextMuted, FontWeights.Normal);
            _formatStatus.Margin = new Thickness(0, 6, 0, 0);
            _formatStatus.TextWrapping = TextWrapping.Wrap;

            _formatHintText = SettingsUiHints.Hint(
                "支持 .NET 日期格式。输入时实时预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _formatHintText.Margin = new Thickness(0, 0, 0, 8);
            _formatHintText.TextWrapping = TextWrapping.Wrap;

            // 变量说明面板：先建容器与搜索框，条目在首次进入格式页时懒加载。
            _formatHelpSearchBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_formatHelpSearchBox);
            _formatHelpSearchBox.Height = 32;
            _formatHelpSearchBox.Margin = new Thickness(0, 0, 0, 6);
            _formatHelpSearchBox.MaxLength = 40;
            AutomationProperties.SetAutomationId(_formatHelpSearchBox, "FormatHelpSearchBox");
            AutomationProperties.SetName(_formatHelpSearchBox, "搜索格式变量");

            _formatHelpCount = UiTheme.MakeText("", 11, UiTheme.TextMuted, FontWeights.Normal);
            _formatHelpCount.Margin = new Thickness(0, 0, 0, 4);

            _formatHelpPanel = new StackPanel();
            _formatHelpScroll = new ScrollViewer();
            _formatHelpScroll.MaxHeight = double.PositiveInfinity;
            _formatHelpScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _formatHelpScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _formatHelpScroll.Content = _formatHelpPanel;

            // 两个模式单选按钮共用 GroupName，切换时由事件处理器重绑预设与说明文字。
            _dotNetFormatRadio = new RadioButton();
            _dotNetFormatRadio.Content = "兼容 .NET";
            _dotNetFormatRadio.GroupName = "FormatMode";
            _dotNetFormatRadio.Foreground = UiTheme.TextPrimary;
            _dotNetFormatRadio.FontSize = 13;
            _dotNetFormatRadio.Margin = new Thickness(0, 3, 22, 3);
            _dotNetFormatRadio.VerticalContentAlignment = VerticalAlignment.Center;
            _dotNetFormatRadio.ToolTip = "使用 .NET 自定义日期格式，兼容已有格式";
            UiTheme.ApplyRadioButtonStyle(_dotNetFormatRadio);
            AutomationProperties.SetAutomationId(_dotNetFormatRadio, "DotNetFormatRadio");

            _tokenFormatRadio = new RadioButton();
            _tokenFormatRadio.Content = "简洁变量（默认）";
            _tokenFormatRadio.GroupName = "FormatMode";
            _tokenFormatRadio.Foreground = UiTheme.TextPrimary;
            _tokenFormatRadio.FontSize = 13;
            _tokenFormatRadio.Margin = new Thickness(0, 3, 0, 3);
            _tokenFormatRadio.VerticalContentAlignment = VerticalAlignment.Center;
            _tokenFormatRadio.ToolTip = "使用 yy、M、D、week、rtm 等简洁变量，支持农历、干支和天气";
            UiTheme.ApplyRadioButtonStyle(_tokenFormatRadio);
            AutomationProperties.SetAutomationId(_tokenFormatRadio, "TokenFormatRadio");

            _weatherFormatBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_weatherFormatBox);
            _weatherFormatBox.FontFamily = new FontFamily("Consolas");
            _weatherFormatBox.FontSize = 13;
            _weatherFormatBox.Height = 34;
            _weatherFormatBox.MaxLength = FormatEngine.MaxFormatLength;
            AutomationProperties.SetAutomationId(_weatherFormatBox, "WeatherFormatBox");
            AutomationProperties.SetName(_weatherFormatBox, "天气格式");

            _weatherFormatStatus = SettingsUiHints.Hint(
                "留空使用默认格式：城市 · 天气 · 温度 · 今日高低温。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _weatherFormatStatus.Margin = new Thickness(0, 6, 0, 0);
            _weatherFormatStatus.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(_weatherFormatStatus, "WeatherFormatStatus");

            // 时区下拉框只列出可选时区，实际生效值在点「应用」后由控制器读取。
            _timeZoneCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_timeZoneCombo);
            _timeZoneCombo.ItemsSource = _timeZoneChoices;
            _timeZoneCombo.DisplayMemberPath = "Display";
            _timeZoneCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_timeZoneCombo, "TimeZoneCombo");
            AutomationProperties.SetName(_timeZoneCombo, "显示时区");

            // 初始文本只是占位，选中时区变化后由事件处理器刷新。
            _timeZoneStatus = SettingsUiHints.Hint("当前：UTC+08:00", 11, UiTheme.TextMuted, FontWeights.Normal);
            _timeZoneStatus.Margin = new Thickness(0, 6, 0, 0);
            _timeZoneStatus.TextWrapping = TextWrapping.Wrap;

            // 提醒页：新建提醒的输入区，以及全局的提醒行为设置。
            _reminderTimeBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_reminderTimeBox);
            _reminderTimeBox.Width = 74;
            _reminderTimeBox.MaxLength = 5;
            _reminderTimeBox.FontFamily = new FontFamily("Consolas");
            _reminderTimeBox.Text = "12:00";
            AutomationProperties.SetAutomationId(_reminderTimeBox, "ReminderTimeBox");
            AutomationProperties.SetName(_reminderTimeBox, "提醒时间");

            _reminderMessageBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_reminderMessageBox);
            _reminderMessageBox.MaxLength = 120;
            _reminderMessageBox.Text = "该吃点美食补充能量了。";
            AutomationProperties.SetAutomationId(_reminderMessageBox, "ReminderMessageBox");
            AutomationProperties.SetName(_reminderMessageBox, "提醒内容");

            _newReminderModeCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_newReminderModeCombo);
            _newReminderModeCombo.ItemsSource = _reminderModeOptions;
            _newReminderModeCombo.DisplayMemberPath = "Label";
            _newReminderModeCombo.SelectedValuePath = "Value";
            _newReminderModeCombo.SelectedValue = ReminderModes.Both;
            _newReminderModeCombo.Height = 30;
            _newReminderModeCombo.Width = 106;
            AutomationProperties.SetAutomationId(_newReminderModeCombo, "NewReminderModeCombo");
            AutomationProperties.SetName(_newReminderModeCombo, "新提醒适用状态");

            // 当前状态用单选按钮表示：同一时刻只能是工作或娱乐，GroupName 保证互斥。
            _workModeRadio = new RadioButton();
            _workModeRadio.Content = "工作状态";
            _workModeRadio.GroupName = "CurrentMode";
            _workModeRadio.Foreground = UiTheme.TextPrimary;
            _workModeRadio.FontSize = 13.5;
            _workModeRadio.Margin = new Thickness(0, 3, 22, 3);
            _workModeRadio.VerticalContentAlignment = VerticalAlignment.Center;
            _workModeRadio.ToolTip = "只触发“全部状态”和“仅工作”的提醒";
            UiTheme.ApplyRadioButtonStyle(_workModeRadio);
            AutomationProperties.SetAutomationId(_workModeRadio, "WorkModeRadio");

            _leisureModeRadio = new RadioButton();
            _leisureModeRadio.Content = "娱乐状态";
            _leisureModeRadio.GroupName = "CurrentMode";
            _leisureModeRadio.Foreground = UiTheme.TextPrimary;
            _leisureModeRadio.FontSize = 13.5;
            _leisureModeRadio.Margin = new Thickness(0, 3, 22, 3);
            _leisureModeRadio.VerticalContentAlignment = VerticalAlignment.Center;
            _leisureModeRadio.ToolTip = "只触发“全部状态”和“仅娱乐”的提醒";
            UiTheme.ApplyRadioButtonStyle(_leisureModeRadio);
            AutomationProperties.SetAutomationId(_leisureModeRadio, "LeisureModeRadio");

            _reminderDurationCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_reminderDurationCombo);
            _reminderDurationCombo.ItemsSource = _reminderDisplayOptions;
            _reminderDurationCombo.DisplayMemberPath = "Label";
            _reminderDurationCombo.SelectedValuePath = "Seconds";
            _reminderDurationCombo.SelectedValue = 12;
            AutomationProperties.SetAutomationId(_reminderDurationCombo, "ReminderDurationCombo");
            AutomationProperties.SetName(_reminderDurationCombo, "提醒显示时长");

            _reminderHoverCheck = CreateCheckBox(
                "检测鼠标悬停（悬停 3 秒关闭）",
                true,
                "ReminderHoverCheck",
                "鼠标在提醒卡片上停留 3 秒后关闭该提醒；穿透状态下同样有效");

            _reminderBlinkSlider = CreateSlider(0, 20, 3, "ReminderBlinkSlider");
            _reminderBlinkValueText = UiTheme.MakeText("3 次", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _reminderBlinkValueText.HorizontalAlignment = HorizontalAlignment.Right;

            _reminderSoundCheck = CreateCheckBox(
                "提醒铃声（取消勾选静音）",
                true,
                "ReminderSoundCheck",
                "提醒出现时播放短促的闹钟提示音；关闭后完全静音");

            // 状态行初始为空，添加或校验失败后才显示文字。
            _reminderStatus = UiTheme.MakeText("", 11.5, UiTheme.TextSecondary, FontWeights.Normal);
            _reminderStatus.Margin = new Thickness(0, 8, 0, 0);
            _reminderStatus.TextWrapping = TextWrapping.Wrap;

            // 提醒列表容器：行控件动态生成，滚动区限高，避免条数多时把窗口撑破。
            _reminderRowsPanel = new StackPanel();
            _reminderScroll = new ScrollViewer();
            _reminderScroll.MaxHeight = 218;
            _reminderScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _reminderScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _reminderScroll.Margin = new Thickness(0, 4, 0, 0);
            _reminderScroll.Content = _reminderRowsPanel;

            _reminderEmptyText = SettingsUiHints.Hint(
                "还没有定时提醒。点击上面的内置提醒，或在下方自行添加。",
                11.5,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _reminderEmptyText.Margin = new Thickness(2, 8, 2, 4);
            _reminderEmptyText.TextWrapping = TextWrapping.Wrap;

            _reminderCountText = UiTheme.MakeText("0 条", 11.5, UiTheme.TextMuted, FontWeights.Normal);
            _reminderCountText.HorizontalAlignment = HorizontalAlignment.Right;

            // 日程页：筛选框 + 状态行 + 动态行列表；新增按钮在刷新列表时才确保存在。
            _scheduleFilterBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_scheduleFilterBox);
            _scheduleFilterBox.Height = 34;
            _scheduleFilterBox.ToolTip = "按标题、类型、日期、时间或备注筛选日程";
            AutomationProperties.SetAutomationId(_scheduleFilterBox, "ScheduleFilterBox");
            AutomationProperties.SetName(_scheduleFilterBox, "筛选日程");
            _scheduleFilterBox.TextChanged += OnScheduleFilterChanged;

            _scheduleFilterHint = UiTheme.MakeText(
                "输入关键词筛选日程，支持标题、类型、日期和备注。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _scheduleFilterHint.Margin = new Thickness(0, 5, 0, 0);

            _scheduleStatus = SettingsUiHints.Hint(
                "日程按当前显示时区触发；启用或停用、编辑、删除会立即反映到列表。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _scheduleStatus.Margin = new Thickness(0, 8, 0, 0);
            _scheduleStatus.TextWrapping = TextWrapping.Wrap;

            // 日程行比提醒行高，滚动区给到 430，仍保证窗口最小高度下可用。
            _scheduleRowsPanel = new StackPanel();
            _scheduleScroll = new ScrollViewer();
            _scheduleScroll.MaxHeight = 430;
            _scheduleScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _scheduleScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _scheduleScroll.Margin = new Thickness(0, 4, 0, 0);
            _scheduleScroll.Content = _scheduleRowsPanel;

            _scheduleEmptyText = SettingsUiHints.Hint(
                "还没有日程。添加生日、纪念日、会议或机票等重要约定。",
                11.5,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _scheduleEmptyText.Margin = new Thickness(2, 8, 2, 4);
            _scheduleEmptyText.TextWrapping = TextWrapping.Wrap;

            _scheduleCountText = UiTheme.MakeText("0 条", 11.5, UiTheme.TextMuted, FontWeights.Normal);
            _scheduleCountText.HorizontalAlignment = HorizontalAlignment.Right;

            // 底部状态行：显示保存结果与全局校验提示，颜色由状态方法控制。
            _footerStatus = UiTheme.MakeText("", 11.5, UiTheme.TextMuted, FontWeights.Normal);
            _footerStatus.VerticalAlignment = VerticalAlignment.Center;
            _footerStatus.Margin = new Thickness(0, 0, 14, 0);

            // 页签容器：各页内容在分部文件里构建，这里只建容器与全局样式。
            _tabs = new TabControl();
            UiTheme.ApplyDarkTabs(_tabs);
            _tabs.Margin = new Thickness(26, 8, 26, 0);

            // 先建好控件再拼界面树：分部文件里的 Build*Tab 依赖上面这些字段。
            Content = BuildWindowContent();

            // 装载期间屏蔽控件事件，避免初始化赋值触发校验与预览重入。
            _updating = true;
            try
            {
                LoadControlsFromWorking();
            }
            finally
            {
                // 即使装载抛异常也要恢复事件开关，否则窗口之后都无法响应编辑。
                _updating = false;
            }

            // 界面就绪后才绑定事件，防止装载阶段的事件提前触发回写。
            AttachEvents();
            // 首帧预览：让用户一打开窗口就看到当前设置的真实效果。
            UpdatePreview();
        }
    }
}
