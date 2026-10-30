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

// 设置窗口主文件：只放字段、构造与公共骨架；各页签的控件与事件按内容域拆在同目录分部文件。
// 边界：
// 1. 窗口编辑的是 _working 克隆副本，点“应用/保存”后才由控制器写盘并刷新显示面；
// 2. 无边框窗口靠 WindowChrome 提供四边缩放（ResizeBorderThickness=6），标题拖动在 Shell 分部；
// 3. 本文件不得直接读写 SettingsStore/注册表/显示窗口，那些副作用归 AppController。
namespace GameClock
{
    /// <summary>
    /// 设置主窗口：无边框暗色模态窗，构造时一次性搭好全部控件并回填当前设置。
    /// 类型按页签拆成多个 partial 文件，每个分部文件只负责自己的控件与事件。
    /// </summary>
    internal sealed partial class SettingsWindow : Window
    {
        // 页签索引与 Shell 分部里的建页顺序一一对应；校验失败跳页时按这些常量定位。
        private const int TabAppearance = 0;
        private const int TabTaskbar = 1;
        private const int TabFormat = 2;
        private const int TabReminders = 3;
        private const int TabSchedule = 4;
        private const int TabWeather = 5;

        // 控件引用全部在构造时创建一次并声明为 readonly：页签切换不重建控件，
        // 事件只挂一次，避免重复挂接导致一次点击触发多遍。
        private readonly AppSettings _working;
        private readonly LayeredText _previewText;
        private readonly List<ColorPreset> _presets;
        private readonly List<FontChoice> _fontChoices;
        private readonly ComboBox _fontCombo;
        private readonly Slider _sizeSlider;
        private readonly TextBlock _sizeValueText;
        private readonly Slider _opacitySlider;
        private readonly TextBlock _opacityValueText;
        private readonly Border _colorSwatch;
        private readonly TextBlock _colorHexText;
        private readonly CheckBox _shadowCheck;
        private readonly CheckBox _threeDCheck;
        private readonly CheckBox _clickThroughCheck;
        private readonly CheckBox _topmostCheck;
        private readonly CheckBox _startWithWindowsCheck;
        private readonly CheckBox _hideFullscreenCheck;
        private readonly CheckBox _desktopOverlayCheck;
        private readonly CheckBox _taskbarClockCheck;

        // —— 任务栏时钟页控件（与悬浮窗字段一一对应但完全独立，不要互相复用）——
        // 两套预设置换列表分别缓存 .NET/简洁变量两种语言的选项，切换语言时整体换 ItemsSource。
        private readonly ComboBox _taskbarFontCombo;
        private readonly Slider _taskbarSizeSlider;
        private readonly TextBlock _taskbarSizeValueText;
        private readonly Border _taskbarColorSwatch;
        private readonly TextBox _taskbarColorBox;
        private readonly TextBox _taskbarFormatBox;
        private readonly TextBlock _taskbarFormatStatus;
        private readonly ComboBox _taskbarFormatCombo;
        private readonly RadioButton _taskbarDotNetFormatRadio;
        private readonly RadioButton _taskbarTokenFormatRadio;
        private readonly TextBlock _taskbarFormatHintText;
        private readonly List<TimeFormatPreset> _taskbarDotNetPresets;
        private readonly List<TimeFormatPreset> _taskbarTokenPresets;
        private readonly TimeFormatPreset _taskbarCustomPreset;
        private List<TimeFormatPreset> _taskbarActivePresets;

        // —— 天气页控件（含预览用的天气行 _previewWeatherText，它归预览卡而非页签）——
        // _weatherColorPresets 是天气专用调色板记录，与时间颜色的 _presets 分开，互不高亮。
        private readonly CheckBox _weatherEnabledCheck;
        private readonly TextBox _weatherCityBox;
        private readonly Button _weatherTestButton;
        private readonly TextBlock _weatherTestStatus;
        private readonly ComboBox _weatherFontCombo;
        private readonly Slider _weatherSizeSlider;
        private readonly TextBlock _weatherSizeValueText;
        private readonly Border _weatherColorSwatch;
        private readonly TextBlock _weatherColorHexText;
        private readonly CheckBox _weatherShadowCheck;
        private readonly CheckBox _weatherBoldCheck;
        private readonly CheckBox _weatherItalicCheck;
        private readonly TextBlock _previewWeatherText;
        private readonly List<ColorPreset> _weatherColorPresets;
        private readonly ComboBox _weatherRefreshCombo;
        private readonly List<WeatherRefreshOption> _weatherRefreshOptions;
        // 测试查询的版本号：每次测试自增，回调回来时版本不一致就丢弃，避免旧结果覆盖新结果。
        private int _weatherTestVersion;
        private readonly TabControl _tabs;

        // —— 全局时间格式页控件 ——
        // _formatPresets 永远指向“当前语言”的预设列表（_dotNetFormatPresets 或 _tokenFormatPresets）。
        private List<TimeFormatPreset> _formatPresets;
        private readonly List<TimeFormatPreset> _dotNetFormatPresets;
        private readonly List<TimeFormatPreset> _tokenFormatPresets;
        private readonly TimeFormatPreset _customFormatPreset;
        private readonly ComboBox _formatCombo;
        private readonly TextBox _formatBox;
        private readonly TextBlock _formatStatus;
        private readonly TextBlock _formatHintText;
        private readonly TextBox _formatHelpSearchBox;
        private readonly StackPanel _formatHelpPanel;
        private readonly ScrollViewer _formatHelpScroll;
        private readonly TextBlock _formatHelpCount;
        private List<FormatHelpItem> _formatHelpItems;
        private readonly RadioButton _dotNetFormatRadio;
        private readonly RadioButton _tokenFormatRadio;
        private readonly TextBox _weatherFormatBox;
        private readonly TextBlock _weatherFormatStatus;
        private readonly ComboBox _timeZoneCombo;
        private readonly List<TimeZoneChoice> _timeZoneChoices;
        private readonly TextBlock _timeZoneStatus;

        // —— 定时提醒页控件 ——
        // 提醒行（时间/内容/状态）由 RebuildReminderRows 动态生成，引用保存在 _reminderRows 里。
        private readonly TextBox _reminderTimeBox;
        private readonly TextBox _reminderMessageBox;
        private readonly ComboBox _newReminderModeCombo;
        private readonly List<ReminderModeOption> _reminderModeOptions;
        private readonly RadioButton _workModeRadio;
        private readonly RadioButton _leisureModeRadio;
        private readonly ComboBox _reminderDurationCombo;
        private readonly CheckBox _reminderHoverCheck;
        private readonly Slider _reminderBlinkSlider;
        private readonly TextBlock _reminderBlinkValueText;
        private readonly CheckBox _reminderSoundCheck;
        private readonly List<ReminderDisplayOption> _reminderDisplayOptions;
        private readonly TextBlock _reminderStatus;
        private readonly StackPanel _reminderRowsPanel;
        private readonly ScrollViewer _reminderScroll;
        private readonly TextBlock _reminderEmptyText;
        private readonly TextBlock _reminderCountText;
        private readonly List<ReminderRow> _reminderRows;

        // —— 日程页控件 ——
        // 日程行同样是动态生成（_scheduleRows），筛选只影响展示，不改列表与配置。
        private readonly List<ScheduleRow> _scheduleRows;
        private readonly TextBox _scheduleFilterBox;
        private readonly TextBlock _scheduleFilterHint;
        private readonly TextBlock _scheduleStatus;
        private readonly StackPanel _scheduleRowsPanel;
        private readonly ScrollViewer _scheduleScroll;
        private readonly TextBlock _scheduleEmptyText;
        private readonly TextBlock _scheduleCountText;
        private Button _scheduleAddButton;
        // 页脚状态文字：整个窗口唯一的“操作结果”反馈位，任何提示都写它而不是弹 MessageBox。
        private readonly TextBlock _footerStatus;

        /// <summary>
        /// 正在用代码回填控件值：为 true 时各页签事件直接返回，
        /// 避免初始化/恢复默认时把程序写入当成用户修改，误报“有未应用的修改”。
        /// </summary>
        private bool _updating;

        /// <summary>
        /// 构造并初始化设置窗口。传入 null 时退化为出厂默认设置，
        /// 保证控制器在没有配置的情况下也能打开设置界面。
        /// </summary>
        public SettingsWindow(AppSettings current)
        {
            // 编辑副本：窗口内所有改动先落在 _working 上，取消/关闭不会影响正在运行的设置。
            _working = current != null ? current.Clone() : AppSettings.CreateDefault();
            // 克隆值可能来自手改 JSON，先收敛一次，避免滑块/绑定拿到 NaN 等非法值抛异常。
            _working.Normalize();

            // 窗口级滚动条样式：放进 Resources 让所有折叠面板的 ScrollViewer 一起生效。
            Resources.Add(typeof(ScrollBar), ModernScrollBar.CreateStyle());

            Title = "时间悬浮窗设置";
            // 尺寸沿用旧版布局（680×820），最小 640×560 是内容不被裁切的底线；
            // 页签新增控件时只应放宽高度，别动这两个最小值（窗口壳与 ResizeMode 依赖它）。
            Width = 680;
            Height = 820;
            MinWidth = 640;
            MinHeight = 560;
            ResizeMode = ResizeMode.CanResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            // 无边框窗口的四边缩放完全由 WindowChrome 提供：CaptionHeight=0 表示标题栏自绘、
            // 不显示系统按钮，6px 缩放热区与 AGENTS 约定一致，不要改回 NoResize。
            // 这里若抛异常会冒泡到 Program 的全局 UI 异常处理（已记 AppLog.Error），
            // 因此不在本地重复捕获，避免同一次失败写两条日志。
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            Topmost = true;
            Background = UiTheme.WindowBackground;
            Foreground = UiTheme.TextPrimary;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            // 辅助列表先建好，随后各控件在构造时直接引用，避免 null 检查散落各处。
            _presets = new List<ColorPreset>();
            _weatherColorPresets = new List<ColorPreset>();
            _fontChoices = FontCatalog.GetChoices();
            DateTime presetSample = DateTime.Now;
            // 两套预设都用“当前时间”生成示例文本，让“yyyy年MM月”之类写法在列表里一眼能看懂；
            // 示例只用于展示，选中预设后真正生效的是它的格式串，与这个时间无关。
            _dotNetFormatPresets = TimeFormatter.CreatePresets(presetSample);
            _tokenFormatPresets = FormatEngine.CreatePresets(presetSample);
            _formatPresets = _dotNetFormatPresets;
            // 默认语言是 .NET 格式（与 AppSettings 出厂默认的 ModeDotNet 一致）；
            // 同一份“自定义格式”对象被插入两个列表首位，切换语言时不需要重建。
            _customFormatPreset = new TimeFormatPreset("自定义格式", null, "使用下方输入框");
            _dotNetFormatPresets.Insert(0, _customFormatPreset);
            _tokenFormatPresets.Insert(0, _customFormatPreset);
            _timeZoneChoices = TimeZoneCatalog.GetChoices();
            // 列表容器只创建一次，行控件在 Rebuild*Rows 里按数据重建；日程筛选不改动这些集合。
            _reminderRows = new List<ReminderRow>();
            _reminderModeOptions = ReminderModes.CreateOptions();
            _reminderDisplayOptions = ReminderDisplayOptions.Create();
            _scheduleRows = new List<ScheduleRow>();

            // 预览用分层文字：与真实悬浮窗共用 LayeredText，保证阴影/3D 效果所见即所得。
            _previewText = new LayeredText();
            _previewText.Margin = new Thickness(10);

            // 预览天气行：初始给一条示例文案，字号/颜色随后由 UpdatePreview 按配置覆盖。
            _previewWeatherText = new TextBlock();
            _previewWeatherText.Text = "北京 · 晴 · 12°C · 今日 9°~17°";
            _previewWeatherText.FontFamily = new FontFamily("Microsoft YaHei UI");
            _previewWeatherText.FontSize = 15;
            _previewWeatherText.TextAlignment = TextAlignment.Center;
            _previewWeatherText.HorizontalAlignment = HorizontalAlignment.Center;
            _previewWeatherText.TextWrapping = TextWrapping.NoWrap;
            _previewWeatherText.Margin = new Thickness(0, 4, 0, 0);
            // 天气关闭时整行隐藏（Collapsed 而不是 Hidden），否则预览卡会多出一条空白，高度也随之上跳。
            _previewWeatherText.Visibility = Visibility.Collapsed;

            // 字体下拉：项模板用字体自身渲染字体名，用户能直接看出效果。
            _fontCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_fontCombo);
            _fontCombo.ItemsSource = _fontChoices;
            _fontCombo.ItemTemplate = CreateFontItemTemplate();
            // 字体下拉可能几百项，限高 360 让它内部出现滚动条，避免下拉超出窗口底部。
            _fontCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_fontCombo, "FontCombo");
            AutomationProperties.SetName(_fontCombo, "字体");

            // 字号滑块范围 16~160 与 AppSettings.Min/MaxFontSize 一致，数值文本靠右对齐。
            _sizeSlider = CreateSlider(16, 160, 52, "FontSizeSlider");
            // 数值文本与滑块同排：拖动时由 OnSizeChanged 同步更新，这里只给初始值占位。
            _sizeValueText = UiTheme.MakeText("52 px", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _sizeValueText.HorizontalAlignment = HorizontalAlignment.Right;

            // 透明度滑块用整数百分比（20~100），保存时再 /100 转成 0.20~1.00 的小数。
            _opacitySlider = CreateSlider(20, 100, 92, "OpacitySlider");
            // 透明度文本按百分比显示，避免用户去心算 0.92 是多少。
            _opacityValueText = UiTheme.MakeText("92%", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _opacityValueText.HorizontalAlignment = HorizontalAlignment.Right;

            // 当前颜色色块 + Hex 文本：色块只做展示，点击“选择…”才打开取色器。
            // 26×26 圆角 7 与任务栏/天气色块保持同一尺寸，三处视觉一致；边框用 FieldBorder 融入输入框。
            _colorSwatch = new Border();
            _colorSwatch.Width = 26;
            _colorSwatch.Height = 26;
            _colorSwatch.CornerRadius = new CornerRadius(7);
            _colorSwatch.BorderThickness = new Thickness(1);
            _colorSwatch.BorderBrush = UiTheme.FieldBorder;
            _colorSwatch.VerticalAlignment = VerticalAlignment.Center;

            // Hex 文本用等宽字体，颜色值位数对齐，方便肉眼核对。
            _colorHexText = UiTheme.MakeText("#FF3B30", 12.5, UiTheme.TextPrimary, FontWeights.Normal);
            _colorHexText.VerticalAlignment = VerticalAlignment.Center;
            _colorHexText.Margin = new Thickness(10, 0, 0, 0);
            _colorHexText.FontFamily = new FontFamily("Consolas");

            // 外观类复选框：阴影与 3D 可同时开启，ToolTip 说明各自效果。
            _shadowCheck = CreateCheckBox("文字阴影", true, "ShadowCheck", "为文字添加柔和的黑色阴影，提升游戏画面上的可读性");
            _threeDCheck = CreateCheckBox("3D 立体字", false, "Text3DCheck", "为文字添加立体厚度；与阴影可同时开启");
            // 行为类复选框：穿透/Topmost/开机自启/全屏隐藏，均只改编辑副本，点“应用”才生效。
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
            // 显示位置复选框：两个位置互相独立、可同时关闭；真正的窗口开关由控制器执行。
            _desktopOverlayCheck = CreateCheckBox(
                "显示桌面悬浮窗",
                true,
                "DesktopOverlayCheck",
                "在桌面上显示可拖动的时间悬浮窗；关闭后不影响任务栏时钟。");
            _taskbarClockCheck = CreateCheckBox(
                "显示任务栏时钟",
                false,
                "TaskbarClockCheck",
                "把时间显示在任务栏时钟位置，开启期间系统时钟移出任务栏；关闭后立即恢复系统时钟。");

            // 任务栏字体下拉与悬浮窗共用字体清单，但选中项各自保存，互不影响。
            _taskbarFontCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_taskbarFontCombo);
            _taskbarFontCombo.ItemsSource = _fontChoices;
            _taskbarFontCombo.ItemTemplate = CreateFontItemTemplate();
            // 任务栏字体下拉限高略小于悬浮窗（320），因为它所在页签下方还有格式说明文字。
            _taskbarFontCombo.MaxDropDownHeight = 320;
            AutomationProperties.SetAutomationId(_taskbarFontCombo, "TaskbarFontCombo");
            AutomationProperties.SetName(_taskbarFontCombo, "任务栏时钟字体");

            // 任务栏字号 7~40：任务栏高度有限，上限比悬浮窗小得多，与 Normalize 的 7–40 对齐。
            _taskbarSizeSlider = CreateSlider(7, 40, 10, "TaskbarFontSizeSlider");
            // 任务栏字号数值文本：整数像素，跟随滑块。
            _taskbarSizeValueText = UiTheme.MakeText("10 px", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _taskbarSizeValueText.HorizontalAlignment = HorizontalAlignment.Right;

            // 任务栏颜色色块 + 可编辑 Hex 输入框：输入非法时由 ColorUtil 回退黑色并写回规范值。
            _taskbarColorSwatch = new Border();
            _taskbarColorSwatch.Width = 26;
            _taskbarColorSwatch.Height = 26;
            _taskbarColorSwatch.CornerRadius = new CornerRadius(7);
            _taskbarColorSwatch.BorderThickness = new Thickness(1);
            _taskbarColorSwatch.BorderBrush = UiTheme.FieldBorder;
            _taskbarColorSwatch.VerticalAlignment = VerticalAlignment.Center;

            // Hex 输入上限 9（#RRGGBB 或 #AARRGGBB），只允许手输，不做实时语法高亮。
            _taskbarColorBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_taskbarColorBox);
            _taskbarColorBox.Text = "#000000";
            _taskbarColorBox.MaxLength = 9;
            // 宽 120 刚好容纳 #AARRGGBB 九个字符 + 等宽字体留白，再窄会截断末位。
            _taskbarColorBox.Width = 120;
            _taskbarColorBox.Height = 32;
            _taskbarColorBox.Margin = new Thickness(10, 0, 0, 0);
            _taskbarColorBox.FontFamily = new FontFamily("Consolas");
            AutomationProperties.SetAutomationId(_taskbarColorBox, "TaskbarColorBox");
            AutomationProperties.SetName(_taskbarColorBox, "任务栏时钟颜色");

            // 任务栏格式输入框：支持两行（MaxLines=2），Enter 预览、Ctrl+Enter 换行，
            // 自动出现纵向滚动条但禁用横向，避免长格式把页签撑宽。
            _taskbarFormatBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_taskbarFormatBox);
            _taskbarFormatBox.FontFamily = new FontFamily("Consolas");
            _taskbarFormatBox.FontSize = 13;
            // 高 62 ≈ 两行 13 号字 + 上下内边距；固定高度避免换行时整页布局跳动。
            _taskbarFormatBox.Height = 62;
            _taskbarFormatBox.AcceptsReturn = true;
            _taskbarFormatBox.MaxLines = 2;
            _taskbarFormatBox.TextWrapping = TextWrapping.Wrap;
            _taskbarFormatBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _taskbarFormatBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            AutomationProperties.SetAutomationId(_taskbarFormatBox, "TaskbarFormatBox");
            AutomationProperties.SetName(_taskbarFormatBox, "任务栏时钟格式");

            // 任务栏格式状态文案：默认灰字提示语法，校验失败时由 CommitAll 改成强调色错误。
            _taskbarFormatStatus = SettingsUiHints.Hint(
                "简洁变量格式，支持两行；任务栏空间不足时会自动缩小字号。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _taskbarFormatStatus.Margin = new Thickness(0, 6, 0, 0);
            _taskbarFormatStatus.TextWrapping = TextWrapping.Wrap;

            // 任务栏两组预设共用同一个“自定义格式”占位项（放在索引 0），
            // 两种格式语言靠 Mode 切换，输入框内容与预设列表必须同步换组。
            // 示例时间与全局格式页共用 presetSample，保证两页看到同一组示例效果。
            _taskbarDotNetPresets = TimeFormatter.CreatePresets(presetSample);
            _taskbarTokenPresets = FormatEngine.CreatePresets(presetSample);
            _taskbarCustomPreset = new TimeFormatPreset("自定义格式", null, "使用下方输入框");
            _taskbarDotNetPresets.Insert(0, _taskbarCustomPreset);
            _taskbarTokenPresets.Insert(0, _taskbarCustomPreset);
            // 默认激活简洁变量列表（任务栏出厂默认就是变量格式），与 LoadControlsFromWorking 的回填方向一致。
            _taskbarActivePresets = _taskbarTokenPresets;

            // 常用格式下拉：ItemsSource 会随格式语言切换重建，选项变化不触发额外的写盘。
            _taskbarFormatCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_taskbarFormatCombo);
            _taskbarFormatCombo.ItemsSource = _taskbarActivePresets;
            _taskbarFormatCombo.DisplayMemberPath = "Display";
            _taskbarFormatCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_taskbarFormatCombo, "TaskbarFormatPresetCombo");
            AutomationProperties.SetName(_taskbarFormatCombo, "任务栏常用格式");

            // 格式语言单选：GroupName 保证两选一；切换时清空另一份格式状态，避免两套字符串互相污染。
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

            // 提示文案单独一行放在输入框上方，内容随格式语言在 LoadControlsFromWorking 里对调。
            _taskbarFormatHintText = SettingsUiHints.Hint(
                "支持简洁变量。输入时实时更新，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _taskbarFormatHintText.Margin = new Thickness(0, 0, 0, 8);
            _taskbarFormatHintText.TextWrapping = TextWrapping.Wrap;

            // —— 天气页控件 ——
            // 天气总开关关闭时只禁用输入控件，不清空已填城市，重新开启即可继续用。
            _weatherEnabledCheck = CreateCheckBox(
                "显示天气预报（默认开启）",
                true,
                "WeatherEnabledCheck",
                "通过 Open-Meteo 公共 API 查询当前天气和今日温度范围");
            // 城市输入上限 40 与 AppSettings.Normalize 的裁剪长度一致；测试按钮不修改正在显示的天气。
            _weatherCityBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_weatherCityBox);
            _weatherCityBox.Text = "北京";
            _weatherCityBox.MaxLength = 40;
            _weatherCityBox.Height = 32;
            AutomationProperties.SetAutomationId(_weatherCityBox, "WeatherCityBox");
            AutomationProperties.SetName(_weatherCityBox, "天气城市");

            // “测试地名”只发起一次查询并把结果显示在下方状态行，不改动正在显示的天气。
            _weatherTestButton = new Button();
            _weatherTestButton.Content = "测试地名";
            _weatherTestButton.Style = UiTheme.SecondaryButtonStyle;
            _weatherTestButton.Height = 32;
            // MinWidth 86 让“测试地名”四个字不被压缩换行；高度 32 与城市输入框底边对齐。
            _weatherTestButton.MinWidth = 86;
            _weatherTestButton.Padding = new Thickness(12, 5, 12, 5);
            _weatherTestButton.Margin = new Thickness(8, 0, 0, 0);
            _weatherTestButton.ToolTip = "用当前城市名查询公共天气 API，确认地名能被正确解析";
            _weatherTestButton.Click += OnTestWeatherCity;
            AutomationProperties.SetAutomationId(_weatherTestButton, "TestWeatherButton");

            // 测试状态文本：用版本号 _weatherTestVersion 丢弃过期回调，防止旧查询覆盖新结果。
            _weatherTestStatus = SettingsUiHints.Hint(
                "输入城市名后，可先测试查询是否成功；不会修改正在显示的天气。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _weatherTestStatus.Margin = new Thickness(0, 8, 0, 0);
            _weatherTestStatus.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(_weatherTestStatus, "WeatherTestStatus");

            // 天气字体下拉同样共享字体清单，但独立保存，不影响时间文字。
            _weatherFontCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_weatherFontCombo);
            _weatherFontCombo.ItemsSource = _fontChoices;
            _weatherFontCombo.ItemTemplate = CreateFontItemTemplate();
            _weatherFontCombo.MaxDropDownHeight = 320;
            AutomationProperties.SetAutomationId(_weatherFontCombo, "WeatherFontCombo");
            AutomationProperties.SetName(_weatherFontCombo, "天气字体");

            // 天气字号 9~60：比时间字号小一档，与 Normalize 的 9–60 区间一致。
            _weatherSizeSlider = CreateSlider(9, 60, 15, "WeatherFontSizeSlider");
            // 天气字号数值文本：整数像素，跟随滑块。
            _weatherSizeValueText = UiTheme.MakeText("15 px", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _weatherSizeValueText.HorizontalAlignment = HorizontalAlignment.Right;

            // 天气颜色与时间颜色完全独立：换时间色不会连带改天气行。
            // 色块尺寸/圆角与时间色块保持一致，避免同一页里两个方块大小不同显得没对齐。
            _weatherColorSwatch = new Border();
            _weatherColorSwatch.Width = 26;
            _weatherColorSwatch.Height = 26;
            _weatherColorSwatch.CornerRadius = new CornerRadius(7);
            _weatherColorSwatch.BorderThickness = new Thickness(1);
            _weatherColorSwatch.BorderBrush = UiTheme.FieldBorder;
            _weatherColorSwatch.VerticalAlignment = VerticalAlignment.Center;

            // 天气 Hex 文本与时间色同款等宽展示，由 UpdateWeatherColorUi 刷新。
            _weatherColorHexText = UiTheme.MakeText("#FF3B30", 12.5, UiTheme.TextPrimary, FontWeights.Normal);
            _weatherColorHexText.VerticalAlignment = VerticalAlignment.Center;
            _weatherColorHexText.Margin = new Thickness(10, 0, 0, 0);
            _weatherColorHexText.FontFamily = new FontFamily("Consolas");

            // 天气阴影独立于时间阴影：深色背景上天气行常需要单独关掉阴影才不糊。
            _weatherShadowCheck = CreateCheckBox(
                "天气文字阴影",
                true,
                "WeatherShadowCheck",
                "为天气行单独添加阴影；与时间文字的阴影设置相互独立");

            // 粗体/倾斜只对天气行生效，与时间文字的 FontWeights.SemiBold 无关。
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

            // 刷新频率选项由 WeatherRefreshOptions 统一提供，归一化也在那里做，避免两处阈值。
            _weatherRefreshOptions = WeatherRefreshOptions.Create();
            _weatherRefreshCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_weatherRefreshCombo);
            _weatherRefreshCombo.ItemsSource = _weatherRefreshOptions;
            _weatherRefreshCombo.DisplayMemberPath = "Label";
            _weatherRefreshCombo.SelectedValuePath = "Minutes";
            // 默认选中 30 分钟只是占位：真实值由 LoadControlsFromWorking 按配置回填，
            // SelectedValue 与选项对象里的 Minutes 匹配，不依赖选项顺序。
            _weatherRefreshCombo.SelectedValue = 30;
            AutomationProperties.SetAutomationId(_weatherRefreshCombo, "WeatherRefreshCombo");
            AutomationProperties.SetName(_weatherRefreshCombo, "天气更新频率");

            // —— 全局时间格式页控件 ——
            // 主时间格式下拉：与任务栏一样有两套预设，切换语言时整体换 ItemsSource。
            // 初始 ItemsSource 指向 .NET 列表（与 _formatPresets 初值一致），回填时若配置是变量模式会整体换掉。
            _formatCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_formatCombo);
            _formatCombo.ItemsSource = _formatPresets;
            _formatCombo.DisplayMemberPath = "Display";
            _formatCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_formatCombo, "FormatPresetCombo");
            AutomationProperties.SetName(_formatCombo, "常用时间格式");

            // 主时间格式输入框：MaxLines=2 允许两行显示，Enter 只预览、Ctrl+Enter 才换行。
            _formatBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_formatBox);
            _formatBox.FontFamily = new FontFamily("Consolas");
            _formatBox.FontSize = 13;
            // 与任务栏格式框同高（62）：两页的输入框视觉一致，切页签时不会“跳高度”。
            _formatBox.Height = 62;
            _formatBox.AcceptsReturn = true;
            _formatBox.MaxLines = 2;
            _formatBox.TextWrapping = TextWrapping.Wrap;
            _formatBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _formatBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            AutomationProperties.SetAutomationId(_formatBox, "CustomFormatBox");
            AutomationProperties.SetName(_formatBox, "自定义时间格式");

            // 校验状态默认灰字，校验失败时 CommitAll 会改成强调色并把窗口带到本页签。
            _formatStatus = SettingsUiHints.Hint("输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。", 11, UiTheme.TextMuted, FontWeights.Normal);
            _formatStatus.Margin = new Thickness(0, 6, 0, 0);
            _formatStatus.TextWrapping = TextWrapping.Wrap;

            // 语法提示：内容随当前格式语言变化（.NET / 简洁变量），由 LoadControlsFromWorking 同步。
            _formatHintText = SettingsUiHints.Hint(
                "支持 .NET 日期格式。输入时实时预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _formatHintText.Margin = new Thickness(0, 0, 0, 8);
            _formatHintText.TextWrapping = TextWrapping.Wrap;

            // 变量说明搜索框：只过滤下方帮助列表，不影响已填格式。
            _formatHelpSearchBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_formatHelpSearchBox);
            _formatHelpSearchBox.Height = 32;
            _formatHelpSearchBox.Margin = new Thickness(0, 0, 0, 6);
            // 上限 40：变量名最长也就二十来个字符，限长可防止粘贴超长文本把搜索框撑破。
            _formatHelpSearchBox.MaxLength = 40;
            AutomationProperties.SetAutomationId(_formatHelpSearchBox, "FormatHelpSearchBox");
            AutomationProperties.SetName(_formatHelpSearchBox, "搜索格式变量");

            // 命中数量提示：筛选后显示“共 n 条”，空结果显示提示语而不是留白。
            // 文本由 UpdateFormatHelp 生成，这里只给空串占位，避免初始化时闪一下“0”。
            _formatHelpCount = UiTheme.MakeText("", 11, UiTheme.TextMuted, FontWeights.Normal);
            _formatHelpCount.Margin = new Thickness(0, 0, 0, 4);

            // 帮助面板放在 ScrollViewer 里由外层页签统一滚动，这里不再套第二层滚动条，
            // 否则滚轮会在嵌套滚动区域之间跳。
            _formatHelpPanel = new StackPanel();
            // 帮助列表的滚动容器：高度不限（double.PositiveInfinity）由外层页签统一滚动，
            // 横向滚动禁用，长变量说明靠换行而不是横滚。
            _formatHelpScroll = new ScrollViewer();
            _formatHelpScroll.MaxHeight = double.PositiveInfinity;
            _formatHelpScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _formatHelpScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _formatHelpScroll.Content = _formatHelpPanel;

            // 格式语言单选：GroupName 与任务栏页的分组名不同，两组互不干扰。
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

            // 简洁变量模式为首选项（与 AppSettings 默认 FormatMode 一致）。
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

            // 天气格式框留空表示用内置默认（城市 · 天气 · 温度 · 今日高低温）；
            // 上限取 FormatEngine.MaxFormatLength，与校验器共享同一个长度约束。
            _weatherFormatBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_weatherFormatBox);
            _weatherFormatBox.FontFamily = new FontFamily("Consolas");
            _weatherFormatBox.FontSize = 13;
            // 单行 34 高即可：天气格式按约定只有一行，不提供 Ctrl+Enter 换行。
            _weatherFormatBox.Height = 34;
            _weatherFormatBox.MaxLength = FormatEngine.MaxFormatLength;
            AutomationProperties.SetAutomationId(_weatherFormatBox, "WeatherFormatBox");
            AutomationProperties.SetName(_weatherFormatBox, "天气格式");

            // 天气格式状态行：默认灰字说明留空语义，校验失败时改成强调色并跳到本页签。
            _weatherFormatStatus = SettingsUiHints.Hint(
                "留空使用默认格式：城市 · 天气 · 温度 · 今日高低温。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _weatherFormatStatus.Margin = new Thickness(0, 6, 0, 0);
            _weatherFormatStatus.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(_weatherFormatStatus, "WeatherFormatStatus");

            // 时区下拉的选项与文案统一由 TimeZoneCatalog 提供，设置窗口不直接读 TimeZoneInfo.Local。
            _timeZoneCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_timeZoneCombo);
            _timeZoneCombo.ItemsSource = _timeZoneChoices;
            _timeZoneCombo.DisplayMemberPath = "Display";
            // 时区项可能上百个（含历史时区），限高 360 让下拉内部滚动，不遮住页脚按钮。
            _timeZoneCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_timeZoneCombo, "TimeZoneCombo");
            AutomationProperties.SetName(_timeZoneCombo, "显示时区");

            // 时区状态：显示当前选择偏移与“是否跟随系统”的原因说明，避免用户以为没生效。
            _timeZoneStatus = UiTheme.MakeText("当前：UTC+08:00", 11, UiTheme.TextMuted, FontWeights.Normal);
            _timeZoneStatus.Margin = new Thickness(0, 6, 0, 0);
            _timeZoneStatus.TextWrapping = TextWrapping.Wrap;

            // —— 定时提醒页控件 ——
            // 时间输入限定 5 个字符（HH:mm），等宽字体便于对齐；提交时再统一校验与补零。
            // 宽度 74 刚好放下 5 个等宽字符 + 内边距，再窄会把光标挤到可视区外。
            _reminderTimeBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_reminderTimeBox);
            _reminderTimeBox.Width = 74;
            _reminderTimeBox.MaxLength = 5;
            _reminderTimeBox.FontFamily = new FontFamily("Consolas");
            // 初始示例文案只是占位，真正列表由 RebuildReminderRows 按 _working 生成。
            _reminderTimeBox.Text = "12:00";
            AutomationProperties.SetAutomationId(_reminderTimeBox, "ReminderTimeBox");
            AutomationProperties.SetName(_reminderTimeBox, "提醒时间");

            // 提醒内容上限 120，与 AppSettings/CommitAll 的裁剪长度保持一致（超出直接截断）。
            _reminderMessageBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_reminderMessageBox);
            _reminderMessageBox.MaxLength = 120;
            _reminderMessageBox.Text = "该吃点美食补充能量了。";
            AutomationProperties.SetAutomationId(_reminderMessageBox, "ReminderMessageBox");
            AutomationProperties.SetName(_reminderMessageBox, "提醒内容");

            // 新提醒的适用状态（全部/仅工作/仅娱乐）：默认“全部”，避免用户漏收提醒。
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

            // 当前状态单选：只影响“哪些提醒会触发”，与窗口显示位置无关。
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

            // 娱乐状态与工作状态同组互斥；切换会立即影响提醒气泡的触发判断。
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

            // 提醒显示时长：选项值来自 ReminderDisplayOptions，Normalize 也走同一套取值。
            _reminderDurationCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_reminderDurationCombo);
            _reminderDurationCombo.ItemsSource = _reminderDisplayOptions;
            _reminderDurationCombo.DisplayMemberPath = "Label";
            _reminderDurationCombo.SelectedValuePath = "Seconds";
            _reminderDurationCombo.SelectedValue = 12;
            AutomationProperties.SetAutomationId(_reminderDurationCombo, "ReminderDurationCombo");
            AutomationProperties.SetName(_reminderDurationCombo, "提醒显示时长");

            // 悬停关闭：鼠标在提醒卡片上停 3 秒即关，穿透状态下依然生效，所以默认开启。
            _reminderHoverCheck = CreateCheckBox(
                "检测鼠标悬停（悬停 3 秒关闭）",
                true,
                "ReminderHoverCheck",
                "鼠标在提醒卡片上停留 3 秒后关闭该提醒；穿透状态下同样有效");

            // 闪烁次数 0~20：0 表示不闪只常亮，上限与 Normalize 的 0–20 一致。
            _reminderBlinkSlider = CreateSlider(0, 20, 3, "ReminderBlinkSlider");
            // 闪烁次数文本：“3 次”带单位，0 次时表示常亮不闪。
            _reminderBlinkValueText = UiTheme.MakeText("3 次", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _reminderBlinkValueText.HorizontalAlignment = HorizontalAlignment.Right;

            // 铃声开关：关闭后完全静音（不播放系统提示音），由提醒模块读取该值。
            _reminderSoundCheck = CreateCheckBox(
                "提醒铃声（取消勾选静音）",
                true,
                "ReminderSoundCheck",
                "提醒出现时播放短促的闹钟提示音；关闭后完全静音");

            // 提醒页底部状态：显示新增/删除结果与校验错误，成功用次要色、失败用强调色。
            _reminderStatus = UiTheme.MakeText("", 11.5, UiTheme.TextSecondary, FontWeights.Normal);
            _reminderStatus.Margin = new Thickness(0, 8, 0, 0);
            _reminderStatus.TextWrapping = TextWrapping.Wrap;

            // 提醒行容器：行控件由 RebuildReminderRows 按当前列表重建，滚动区高度上限 218，
            // 再多的提醒靠纵向滚动查看，避免把页签撑到窗口之外。
            _reminderRowsPanel = new StackPanel();
            // 提醒列表滚动区：只纵向滚动、禁用横向，滚动条按需自动出现。
            _reminderScroll = new ScrollViewer();
            _reminderScroll.MaxHeight = 218;
            _reminderScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _reminderScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _reminderScroll.Margin = new Thickness(0, 4, 0, 0);
            _reminderScroll.Content = _reminderRowsPanel;

            // 空列表提示：只在 0 条提醒时显示，文案引导用户从上方内置提醒或下方表单添加。
            _reminderEmptyText = UiTheme.MakeText(
                "还没有定时提醒。点击上面的内置提醒，或在下方自行添加。",
                11.5,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _reminderEmptyText.Margin = new Thickness(2, 8, 2, 4);
            _reminderEmptyText.TextWrapping = TextWrapping.Wrap;

            // 条目计数靠右显示，与列表标题对齐，避免用户误以为列表被截断。
            _reminderCountText = UiTheme.MakeText("0 条", 11.5, UiTheme.TextMuted, FontWeights.Normal);
            _reminderCountText.HorizontalAlignment = HorizontalAlignment.Right;

            // —— 日程页控件 ——
            // 筛选框按标题/类型/日期/备注做本地过滤，不触发写盘，也不改变列表顺序。
            _scheduleFilterBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_scheduleFilterBox);
            _scheduleFilterBox.Height = 34;
            _scheduleFilterBox.ToolTip = "按标题、类型、日期、时间或备注筛选日程";
            AutomationProperties.SetAutomationId(_scheduleFilterBox, "ScheduleFilterBox");
            AutomationProperties.SetName(_scheduleFilterBox, "筛选日程");
            _scheduleFilterBox.TextChanged += OnScheduleFilterChanged;

            // 筛选提示：说明匹配字段，避免用户以为只能搜标题。
            _scheduleFilterHint = SettingsUiHints.Hint(
                "输入关键词筛选日程，支持标题、类型、日期和备注。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _scheduleFilterHint.Margin = new Thickness(0, 5, 0, 0);

            // 日程状态：说明触发时区与“操作立即反映到列表”，不涉及保存时机。
            _scheduleStatus = SettingsUiHints.Hint(
                "日程按当前显示时区触发；启用或停用、编辑、删除会立即反映到列表。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _scheduleStatus.Margin = new Thickness(0, 8, 0, 0);
            _scheduleStatus.TextWrapping = TextWrapping.Wrap;

            // 日程行容器：行控件由 RebuildScheduleRows 重建；高度上限 430，
            // 让日程页在 820 高的窗口里仍能看到页脚按钮。
            _scheduleRowsPanel = new StackPanel();
            // 日程列表滚动区：上限 430 是“窗口 820 高时仍露出页脚”的经验值。
            _scheduleScroll = new ScrollViewer();
            _scheduleScroll.MaxHeight = 430;
            _scheduleScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _scheduleScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _scheduleScroll.Margin = new Thickness(0, 4, 0, 0);
            _scheduleScroll.Content = _scheduleRowsPanel;

            // 空日程提示：列出常见用途（生日/纪念日/会议/机票），引导用户添加第一条。
            _scheduleEmptyText = UiTheme.MakeText(
                "还没有日程。添加生日、纪念日、会议或机票等重要约定。",
                11.5,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _scheduleEmptyText.Margin = new Thickness(2, 8, 2, 4);
            _scheduleEmptyText.TextWrapping = TextWrapping.Wrap;

            // 日程条目计数：筛选时显示命中数，未筛选时显示总数。
            _scheduleCountText = UiTheme.MakeText("0 条", 11.5, UiTheme.TextMuted, FontWeights.Normal);
            _scheduleCountText.HorizontalAlignment = HorizontalAlignment.Right;

            // 页脚状态：承担“有未应用的修改 / 已应用 / 校验失败”三种提示，是唯一的操作反馈位。
            _footerStatus = UiTheme.MakeText("", 11.5, UiTheme.TextMuted, FontWeights.Normal);
            _footerStatus.VerticalAlignment = VerticalAlignment.Center;
            // 右留 14 让文字不贴页脚右边框；左对齐，避免状态长短变化时整行跳动。
            _footerStatus.Margin = new Thickness(0, 0, 14, 0);

            // 页签容器：具体页面由 Shell 分部的 BuildWindowContent 逐页填入；
            // Margin 只控外边距，页签内容自身的内边距在 CreateTab 里设置。
            _tabs = new TabControl();
            UiTheme.ApplyDarkTabs(_tabs);
            // 上边距 8 与预览卡的 2 底部留白相加，形成标题区与页签之间的呼吸感。
            _tabs.Margin = new Thickness(26, 8, 26, 0);

            Content = BuildWindowContent();

            // 回填期间置 _updating，让各页签的 Changed 事件跳过“用户修改”分支；
            // 先回填再挂事件是双保险（事件表在 AttachEvents 里才建立）。
            _updating = true;
            try
            {
                LoadControlsFromWorking();
            }
            finally
            {
                _updating = false;
            }

            AttachEvents();
            // 构造末尾再刷一次预览：AttachEvents 之前 _updating 已复位，这里的刷新不会反向写 _working。
            UpdatePreview();

            // 打开窗口只记一条：状态与两个显示位置开关足以在排障时区分“没打开”和“打开后卡住”。
            AppLog.Info(
                "Settings",
                "设置窗口已打开：页签 6 个，状态=" + _working.CurrentMode
                    + "，桌面悬浮窗" + (_working.ShowDesktopOverlay ? "开" : "关")
                    + "，任务栏时钟" + (_working.ShowTaskbarClock ? "开" : "关"));
        }
    }
}
