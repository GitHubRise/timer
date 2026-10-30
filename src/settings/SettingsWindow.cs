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
    internal sealed partial class SettingsWindow : Window
    {
        private const int TabAppearance = 0;
        private const int TabTaskbar = 1;
        private const int TabFormat = 2;
        private const int TabReminders = 3;
        private const int TabSchedule = 4;
        private const int TabWeather = 5;

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
        private int _weatherTestVersion;
        private readonly TabControl _tabs;
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
        private readonly List<ScheduleRow> _scheduleRows;
        private readonly TextBox _scheduleFilterBox;
        private readonly TextBlock _scheduleFilterHint;
        private readonly TextBlock _scheduleStatus;
        private readonly StackPanel _scheduleRowsPanel;
        private readonly ScrollViewer _scheduleScroll;
        private readonly TextBlock _scheduleEmptyText;
        private readonly TextBlock _scheduleCountText;
        private Button _scheduleAddButton;
        private readonly TextBlock _footerStatus;
        private bool _updating;

        public SettingsWindow(AppSettings current)
        {
            _working = current != null ? current.Clone() : AppSettings.CreateDefault();
            _working.Normalize();

            Resources.Add(typeof(ScrollBar), ModernScrollBar.CreateStyle());

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
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            Topmost = true;
            Background = UiTheme.WindowBackground;
            Foreground = UiTheme.TextPrimary;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            _presets = new List<ColorPreset>();
            _weatherColorPresets = new List<ColorPreset>();
            _fontChoices = FontCatalog.GetChoices();
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

            _previewText = new LayeredText();
            _previewText.Margin = new Thickness(10);

            _previewWeatherText = new TextBlock();
            _previewWeatherText.Text = "北京 · 晴 · 12°C · 今日 9°~17°";
            _previewWeatherText.FontFamily = new FontFamily("Microsoft YaHei UI");
            _previewWeatherText.FontSize = 15;
            _previewWeatherText.TextAlignment = TextAlignment.Center;
            _previewWeatherText.HorizontalAlignment = HorizontalAlignment.Center;
            _previewWeatherText.TextWrapping = TextWrapping.NoWrap;
            _previewWeatherText.Margin = new Thickness(0, 4, 0, 0);
            _previewWeatherText.Visibility = Visibility.Collapsed;

            _fontCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_fontCombo);
            _fontCombo.ItemsSource = _fontChoices;
            _fontCombo.ItemTemplate = CreateFontItemTemplate();
            _fontCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_fontCombo, "FontCombo");
            AutomationProperties.SetName(_fontCombo, "字体");

            _sizeSlider = CreateSlider(16, 160, 52, "FontSizeSlider");
            _sizeValueText = UiTheme.MakeText("52 px", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _sizeValueText.HorizontalAlignment = HorizontalAlignment.Right;

            _opacitySlider = CreateSlider(20, 100, 92, "OpacitySlider");
            _opacityValueText = UiTheme.MakeText("92%", 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            _opacityValueText.HorizontalAlignment = HorizontalAlignment.Right;

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
                false,
                "TaskbarClockCheck",
                "在任务栏时间区域显示两行时钟，外观跟随系统；不移动、不隐藏系统时钟。");

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

            _taskbarFormatStatus = UiTheme.MakeText(
                "简洁变量格式，支持两行；任务栏空间不足时会自动缩小字号。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _taskbarFormatStatus.Margin = new Thickness(0, 6, 0, 0);
            _taskbarFormatStatus.TextWrapping = TextWrapping.Wrap;

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

            _taskbarFormatHintText = UiTheme.MakeText(
                "支持简洁变量。输入时实时更新，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _taskbarFormatHintText.Margin = new Thickness(0, 0, 0, 8);
            _taskbarFormatHintText.TextWrapping = TextWrapping.Wrap;

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
            _weatherTestButton.Click += OnTestWeatherCity;
            AutomationProperties.SetAutomationId(_weatherTestButton, "TestWeatherButton");

            _weatherTestStatus = UiTheme.MakeText(
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

            _weatherRefreshOptions = WeatherRefreshOptions.Create();
            _weatherRefreshCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_weatherRefreshCombo);
            _weatherRefreshCombo.ItemsSource = _weatherRefreshOptions;
            _weatherRefreshCombo.DisplayMemberPath = "Label";
            _weatherRefreshCombo.SelectedValuePath = "Minutes";
            _weatherRefreshCombo.SelectedValue = 30;
            AutomationProperties.SetAutomationId(_weatherRefreshCombo, "WeatherRefreshCombo");
            AutomationProperties.SetName(_weatherRefreshCombo, "天气更新频率");

            _formatCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_formatCombo);
            _formatCombo.ItemsSource = _formatPresets;
            _formatCombo.DisplayMemberPath = "Display";
            _formatCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_formatCombo, "FormatPresetCombo");
            AutomationProperties.SetName(_formatCombo, "常用时间格式");

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

            _formatStatus = UiTheme.MakeText("输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。", 11, UiTheme.TextMuted, FontWeights.Normal);
            _formatStatus.Margin = new Thickness(0, 6, 0, 0);
            _formatStatus.TextWrapping = TextWrapping.Wrap;

            _formatHintText = UiTheme.MakeText(
                "支持 .NET 日期格式。输入时实时预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _formatHintText.Margin = new Thickness(0, 0, 0, 8);
            _formatHintText.TextWrapping = TextWrapping.Wrap;

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

            _weatherFormatStatus = UiTheme.MakeText(
                "留空使用默认格式：城市 · 天气 · 温度 · 今日高低温。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _weatherFormatStatus.Margin = new Thickness(0, 6, 0, 0);
            _weatherFormatStatus.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(_weatherFormatStatus, "WeatherFormatStatus");

            _timeZoneCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_timeZoneCombo);
            _timeZoneCombo.ItemsSource = _timeZoneChoices;
            _timeZoneCombo.DisplayMemberPath = "Display";
            _timeZoneCombo.MaxDropDownHeight = 360;
            AutomationProperties.SetAutomationId(_timeZoneCombo, "TimeZoneCombo");
            AutomationProperties.SetName(_timeZoneCombo, "显示时区");

            _timeZoneStatus = UiTheme.MakeText("当前：UTC+08:00", 11, UiTheme.TextMuted, FontWeights.Normal);
            _timeZoneStatus.Margin = new Thickness(0, 6, 0, 0);
            _timeZoneStatus.TextWrapping = TextWrapping.Wrap;

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

            _reminderStatus = UiTheme.MakeText("", 11.5, UiTheme.TextSecondary, FontWeights.Normal);
            _reminderStatus.Margin = new Thickness(0, 8, 0, 0);
            _reminderStatus.TextWrapping = TextWrapping.Wrap;

            _reminderRowsPanel = new StackPanel();
            _reminderScroll = new ScrollViewer();
            _reminderScroll.MaxHeight = 218;
            _reminderScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _reminderScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _reminderScroll.Margin = new Thickness(0, 4, 0, 0);
            _reminderScroll.Content = _reminderRowsPanel;

            _reminderEmptyText = UiTheme.MakeText(
                "还没有定时提醒。点击上面的内置提醒，或在下方自行添加。",
                11.5,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _reminderEmptyText.Margin = new Thickness(2, 8, 2, 4);
            _reminderEmptyText.TextWrapping = TextWrapping.Wrap;

            _reminderCountText = UiTheme.MakeText("0 条", 11.5, UiTheme.TextMuted, FontWeights.Normal);
            _reminderCountText.HorizontalAlignment = HorizontalAlignment.Right;

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

            _scheduleStatus = UiTheme.MakeText(
                "日程按当前显示时区触发；启用或停用、编辑、删除会立即反映到列表。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _scheduleStatus.Margin = new Thickness(0, 8, 0, 0);
            _scheduleStatus.TextWrapping = TextWrapping.Wrap;

            _scheduleRowsPanel = new StackPanel();
            _scheduleScroll = new ScrollViewer();
            _scheduleScroll.MaxHeight = 430;
            _scheduleScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _scheduleScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _scheduleScroll.Margin = new Thickness(0, 4, 0, 0);
            _scheduleScroll.Content = _scheduleRowsPanel;

            _scheduleEmptyText = UiTheme.MakeText(
                "还没有日程。添加生日、纪念日、会议或机票等重要约定。",
                11.5,
                UiTheme.TextMuted,
                FontWeights.Normal);
            _scheduleEmptyText.Margin = new Thickness(2, 8, 2, 4);
            _scheduleEmptyText.TextWrapping = TextWrapping.Wrap;

            _scheduleCountText = UiTheme.MakeText("0 条", 11.5, UiTheme.TextMuted, FontWeights.Normal);
            _scheduleCountText.HorizontalAlignment = HorizontalAlignment.Right;

            _footerStatus = UiTheme.MakeText("", 11.5, UiTheme.TextMuted, FontWeights.Normal);
            _footerStatus.VerticalAlignment = VerticalAlignment.Center;
            _footerStatus.Margin = new Thickness(0, 0, 14, 0);

            _tabs = new TabControl();
            UiTheme.ApplyDarkTabs(_tabs);
            _tabs.Margin = new Thickness(26, 8, 26, 0);

            Content = BuildWindowContent();

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
            UpdatePreview();
        }
    }
}
