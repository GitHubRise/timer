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
    internal sealed class SettingsWindow : Window
    {
        private const int TabAppearance = 0;
        private const int TabFormat = 1;
        private const int TabReminders = 2;
        private const int TabWeather = 4;

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
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
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
            _formatHelpScroll.MaxHeight = 320;
            _formatHelpScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _formatHelpScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _formatHelpScroll.Content = _formatHelpPanel;

            _dotNetFormatRadio = new RadioButton();
            _dotNetFormatRadio.Content = "兼容 .NET（默认）";
            _dotNetFormatRadio.GroupName = "FormatMode";
            _dotNetFormatRadio.Foreground = UiTheme.TextPrimary;
            _dotNetFormatRadio.FontSize = 13;
            _dotNetFormatRadio.Margin = new Thickness(0, 3, 22, 3);
            _dotNetFormatRadio.VerticalContentAlignment = VerticalAlignment.Center;
            _dotNetFormatRadio.ToolTip = "使用 .NET 自定义日期格式，兼容已有格式";
            UiTheme.ApplyRadioButtonStyle(_dotNetFormatRadio);
            AutomationProperties.SetAutomationId(_dotNetFormatRadio, "DotNetFormatRadio");

            _tokenFormatRadio = new RadioButton();
            _tokenFormatRadio.Content = "简洁变量";
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

        public event EventHandler ApplyRequested;
        public event EventHandler<ReminderPreviewEventArgs> ReminderTestRequested;

        public AppSettings Result
        {
            get { return _working; }
        }

        private UIElement BuildWindowContent()
        {
            Border shell = new Border();
            shell.Background = UiTheme.WindowBackground;
            shell.BorderBrush = UiTheme.CardBorder;
            shell.BorderThickness = new Thickness(1);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid();
            header.Margin = new Thickness(26, 18, 18, 0);
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleStack = new StackPanel();
            titleStack.Background = Brushes.Transparent;
            titleStack.MouseLeftButtonDown += OnHeaderMouseDown;
            TextBlock title = UiTheme.MakeText("时间悬浮窗设置", 22, UiTheme.TextPrimary, FontWeights.SemiBold);
            TextBlock subtitle = UiTheme.MakeText(
                "颜色、字体、格式和提醒；点击“应用”只保存不关闭。",
                11.5,
                UiTheme.TextSecondary,
                FontWeights.Normal);
            subtitle.Margin = new Thickness(0, 4, 0, 0);
            titleStack.Children.Add(title);
            titleStack.Children.Add(subtitle);
            header.Children.Add(titleStack);
            Grid.SetColumn(titleStack, 0);

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
            closeButton.Click += OnClose;
            AutomationProperties.SetAutomationId(closeButton, "CloseButton");
            header.Children.Add(closeButton);
            Grid.SetColumn(closeButton, 1);

            root.Children.Add(header);
            Grid.SetRow(header, 0);

            Border previewCard = BuildPreviewCard();
            previewCard.Margin = new Thickness(26, 14, 26, 2);
            root.Children.Add(previewCard);
            Grid.SetRow(previewCard, 1);

            _tabs.Items.Add(CreateTab("外观", BuildAppearanceTab()));
            _tabs.Items.Add(CreateTab("时间格式", BuildFormatTab()));
            _tabs.Items.Add(CreateTab("定时提醒", BuildRemindersTab()));
            _tabs.Items.Add(CreateTab("日程", BuildScheduleTab()));
            _tabs.Items.Add(CreateTab("天气", BuildWeatherTab()));
            _tabs.SelectedIndex = TabAppearance;
            root.Children.Add(_tabs);
            Grid.SetRow(_tabs, 2);

            Border footer = BuildFooter();
            root.Children.Add(footer);
            Grid.SetRow(footer, 3);

            shell.Child = root;
            return shell;
        }

        private Border BuildPreviewCard()
        {
            Border card = new Border();
            card.Background = UiTheme.FieldBackground;
            card.BorderBrush = UiTheme.CardBorder;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(10);
            card.Height = 116;
            card.Padding = new Thickness(16, 12, 16, 12);

            Grid grid = new Grid();
            TextBlock label = UiTheme.MakeText("实时预览", 10.5, UiTheme.TextMuted, FontWeights.SemiBold);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(label);

            Viewbox viewbox = new Viewbox();
            viewbox.Stretch = Stretch.Uniform;
            viewbox.StretchDirection = StretchDirection.DownOnly;
            viewbox.HorizontalAlignment = HorizontalAlignment.Center;
            viewbox.VerticalAlignment = VerticalAlignment.Center;
            viewbox.Margin = new Thickness(0, 16, 0, 0);

            StackPanel previewStack = new StackPanel();
            previewStack.HorizontalAlignment = HorizontalAlignment.Center;
            previewStack.VerticalAlignment = VerticalAlignment.Center;
            previewStack.Children.Add(_previewText);
            previewStack.Children.Add(_previewWeatherText);
            viewbox.Child = previewStack;
            grid.Children.Add(viewbox);

            card.Child = grid;
            return card;
        }

        private TabItem CreateTab(string header, UIElement content)
        {
            TabItem item = new TabItem();
            item.Header = header;

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Margin = new Thickness(0, 2, 2, 0);

            StackPanel host = new StackPanel();
            host.Margin = new Thickness(2, 4, 10, 14);
            host.Children.Add(content);
            scroll.Content = host;
            item.Content = scroll;
            return item;
        }

        private UIElement BuildAppearanceTab()
        {
            StackPanel panel = new StackPanel();

            Border fontCard = UiTheme.MakeCard();
            StackPanel fontPanel = new StackPanel();
            fontPanel.Children.Add(UiTheme.MakeSectionTitle("字体与颜色"));

            TextBlock fontLabel = UiTheme.MakeText("字体", 13, UiTheme.TextPrimary, FontWeights.Normal);
            fontLabel.Margin = new Thickness(0, 0, 0, 6);
            fontPanel.Children.Add(fontLabel);
            fontPanel.Children.Add(_fontCombo);
            fontPanel.Children.Add(BuildSliderBlock("字号", _sizeSlider, _sizeValueText, "文字大小，16 到 160 像素", new Thickness(0, 14, 0, 2)));
            fontPanel.Children.Add(BuildColorRow());
            fontPanel.Children.Add(BuildPalette());
            fontCard.Child = fontPanel;
            panel.Children.Add(fontCard);

            Border effectCard = UiTheme.MakeCard();
            StackPanel effectPanel = new StackPanel();
            effectPanel.Children.Add(UiTheme.MakeSectionTitle("效果"));
            effectPanel.Children.Add(BuildSliderBlock("不透明度", _opacitySlider, _opacityValueText, "整个悬浮窗的不透明度，20% 到 100%", new Thickness(0, 0, 0, 4)));
            effectPanel.Children.Add(_shadowCheck);
            effectPanel.Children.Add(_threeDCheck);
            TextBlock effectHint = UiTheme.MakeText(
                "阴影提升可读性；3D 立体字会为文字增加向下偏移的厚度。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            effectHint.Margin = new Thickness(24, 2, 0, 0);
            effectPanel.Children.Add(effectHint);
            effectCard.Child = effectPanel;
            panel.Children.Add(effectCard);

            Border windowCard = UiTheme.MakeCard();
            StackPanel windowPanel = new StackPanel();
            windowPanel.Children.Add(UiTheme.MakeSectionTitle("窗口行为"));
            windowPanel.Children.Add(_clickThroughCheck);
            windowPanel.Children.Add(_topmostCheck);
            windowPanel.Children.Add(_startWithWindowsCheck);
            TextBlock windowHint = UiTheme.MakeText(
                "穿透开启后，时钟上的右键菜单不可用，请右键系统托盘图标恢复。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            windowHint.Margin = new Thickness(24, 3, 0, 0);
            windowPanel.Children.Add(windowHint);
            windowCard.Child = windowPanel;
            panel.Children.Add(windowCard);

            return panel;
        }

        private UIElement BuildFormatTab()
        {
            StackPanel panel = new StackPanel();

            Border timeZoneCard = UiTheme.MakeCard();
            StackPanel timeZonePanel = new StackPanel();
            timeZonePanel.Children.Add(UiTheme.MakeSectionTitle("显示时区"));
            TextBlock timeZoneHint = UiTheme.MakeText(
                "默认使用 UTC+08:00（中国标准时间）。切换后，时间、日期、周数和提醒触发都按所选时区计算。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            timeZoneHint.Margin = new Thickness(0, 0, 0, 8);
            timeZoneHint.TextWrapping = TextWrapping.Wrap;
            timeZonePanel.Children.Add(timeZoneHint);
            timeZonePanel.Children.Add(_timeZoneCombo);
            timeZonePanel.Children.Add(_timeZoneStatus);
            timeZoneCard.Child = timeZonePanel;
            panel.Children.Add(timeZoneCard);

            Border presetCard = UiTheme.MakeCard();
            StackPanel presetPanel = new StackPanel();
            presetPanel.Children.Add(UiTheme.MakeSectionTitle("格式语言"));
            TextBlock modeHint = UiTheme.MakeText(
                "“兼容 .NET”沿用原有格式；“简洁变量”支持农历、干支、星期和天气变量。两种格式分别保存，切换不会丢失。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            modeHint.Margin = new Thickness(0, 0, 0, 8);
            modeHint.TextWrapping = TextWrapping.Wrap;
            presetPanel.Children.Add(modeHint);
            StackPanel modeRow = new StackPanel();
            modeRow.Orientation = Orientation.Horizontal;
            modeRow.Children.Add(_dotNetFormatRadio);
            modeRow.Children.Add(_tokenFormatRadio);
            presetPanel.Children.Add(modeRow);
            presetCard.Child = presetPanel;
            panel.Children.Add(presetCard);

            Border presetsCard = UiTheme.MakeCard();
            StackPanel presetsPanel = new StackPanel();
            presetsPanel.Children.Add(UiTheme.MakeSectionTitle("常用格式（内置）"));
            TextBlock presetHint = UiTheme.MakeText(
                "直接选择即可，不需要修改格式。示例使用当前日期时间生成。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            presetHint.Margin = new Thickness(0, 0, 0, 8);
            presetsPanel.Children.Add(presetHint);
            presetsPanel.Children.Add(_formatCombo);
            presetsCard.Child = presetsPanel;
            panel.Children.Add(presetsCard);

            Border customCard = UiTheme.MakeCard();
            StackPanel customPanel = new StackPanel();
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义格式"));
            customPanel.Children.Add(_formatHintText);
            customPanel.Children.Add(_formatBox);

            Grid actions = new Grid();
            actions.Margin = new Thickness(0, 8, 0, 0);
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button newlineButton = new Button();
            newlineButton.Content = "插入换行";
            newlineButton.Style = UiTheme.SecondaryButtonStyle;
            newlineButton.Padding = new Thickness(12, 5, 12, 5);
            newlineButton.Height = 30;
            newlineButton.HorizontalAlignment = HorizontalAlignment.Left;
            newlineButton.Click += OnInsertNewline;
            newlineButton.ToolTip = "格式最多支持一个换行（两行文字）";
            AutomationProperties.SetAutomationId(newlineButton, "InsertNewlineButton");
            actions.Children.Add(newlineButton);
            Grid.SetColumn(newlineButton, 0);

            TextBlock noNeedEnter = UiTheme.MakeText("输入即预览，也可以直接保存。", 11, UiTheme.TextMuted, FontWeights.Normal);
            noNeedEnter.VerticalAlignment = VerticalAlignment.Center;
            noNeedEnter.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Children.Add(noNeedEnter);
            Grid.SetColumn(noNeedEnter, 1);

            customPanel.Children.Add(actions);
            customPanel.Children.Add(_formatStatus);
            customCard.Child = customPanel;
            panel.Children.Add(customCard);

            Border helpCard = UiTheme.MakeCard();
            StackPanel helpPanel = new StackPanel();
            helpPanel.Children.Add(UiTheme.MakeSectionTitle("格式说明"));
            TextBlock helpHint = UiTheme.MakeText(
                "支持检索：输入变量名、说明或示例关键词。一行一个变量：格式：描述。示例预览：预览。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            helpHint.Margin = new Thickness(0, 0, 0, 8);
            helpHint.TextWrapping = TextWrapping.Wrap;
            helpPanel.Children.Add(helpHint);
            helpPanel.Children.Add(_formatHelpSearchBox);
            helpPanel.Children.Add(_formatHelpCount);
            helpPanel.Children.Add(_formatHelpScroll);

            helpCard.Child = helpPanel;
            panel.Children.Add(helpCard);

            return panel;
        }

        private UIElement BuildRemindersTab()
        {
            StackPanel panel = new StackPanel();

            Border stateCard = UiTheme.MakeCard();
            StackPanel stateContent = new StackPanel();
            stateContent.Children.Add(UiTheme.MakeSectionTitle("当前状态"));
            TextBlock stateHint = UiTheme.MakeText(
                "标记你现在的状态。提醒会自动过滤：全部状态的提醒始终生效，仅工作和仅娱乐只会在对应状态触发。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            stateHint.Margin = new Thickness(0, 0, 0, 8);
            stateHint.TextWrapping = TextWrapping.Wrap;
            stateContent.Children.Add(stateHint);
            StackPanel stateRow = new StackPanel();
            stateRow.Orientation = Orientation.Horizontal;
            stateRow.Children.Add(_workModeRadio);
            stateRow.Children.Add(_leisureModeRadio);
            stateContent.Children.Add(stateRow);
            stateCard.Child = stateContent;
            panel.Children.Add(stateCard);

            Border displayCard = UiTheme.MakeCard();
            StackPanel displayContent = new StackPanel();
            displayContent.Children.Add(UiTheme.MakeSectionTitle("提醒显示"));
            TextBlock durationLabel = UiTheme.MakeText("显示时长", 13, UiTheme.TextPrimary, FontWeights.Normal);
            durationLabel.Margin = new Thickness(0, 0, 0, 6);
            displayContent.Children.Add(durationLabel);
            displayContent.Children.Add(_reminderDurationCombo);
            displayContent.Children.Add(BuildSliderBlock(
                "闪烁次数（0 = 不闪烁）",
                _reminderBlinkSlider,
                _reminderBlinkValueText,
                "提醒卡片进入时闪烁指定次数，便于在游戏画面中注意到；0 表示不闪烁",
                new Thickness(0, 10, 0, 2)));
            _reminderHoverCheck.Margin = new Thickness(0, 8, 0, 4);
            displayContent.Children.Add(_reminderHoverCheck);
            _reminderSoundCheck.Margin = new Thickness(0, 0, 0, 4);
            displayContent.Children.Add(_reminderSoundCheck);
            TextBlock displayHint = UiTheme.MakeText(
                "时长到点自动关闭；开启悬停检测后，鼠标停留在提醒卡片上满 3 秒也会关闭。闪烁和铃声用于提醒，可在上方调整。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            displayHint.Margin = new Thickness(0, 2, 0, 0);
            displayHint.TextWrapping = TextWrapping.Wrap;
            displayContent.Children.Add(displayHint);
            displayCard.Child = displayContent;
            panel.Children.Add(displayCard);

            Border presetCard = UiTheme.MakeCard();
            StackPanel presetPanel = new StackPanel();
            presetPanel.Children.Add(UiTheme.MakeSectionTitle("内置提醒（点击直接添加）"));
            WrapPanel presetWrap = new WrapPanel();
            presetWrap.Margin = new Thickness(0, 0, 0, 2);
            List<ReminderPreset> presets = ReminderPresets.Create();
            for (int i = 0; i < presets.Count; i++)
            {
                ReminderPreset preset = presets[i];
                Button button = new Button();
                button.Content = preset.ButtonText;
                button.Style = UiTheme.SecondaryButtonStyle;
                button.Padding = new Thickness(11, 6, 11, 6);
                button.Margin = new Thickness(0, 0, 8, 8);
                button.FontSize = 12;
                button.ToolTip = "[" + ReminderModes.DisplayName(preset.Mode) + "] " + preset.Message;
                button.Tag = preset;
                button.Click += OnAddPresetReminder;
                AutomationProperties.SetAutomationId(button, "PresetReminder_" + preset.Time.Replace(":", ""));
                presetWrap.Children.Add(button);
            }
            presetPanel.Children.Add(presetWrap);
            TextBlock presetHint = UiTheme.MakeText(
                "添加后可以在下方修改时间、适用状态或内容，也可以停用或删除。提醒每天触发一次。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            presetHint.Margin = new Thickness(0, 2, 0, 0);
            presetHint.TextWrapping = TextWrapping.Wrap;
            presetPanel.Children.Add(presetHint);
            presetCard.Child = presetPanel;
            panel.Children.Add(presetCard);

            Border customCard = UiTheme.MakeCard();
            customCard.Name = "CustomReminderCard";
            StackPanel customPanel = new StackPanel();
            customPanel.Name = "CustomReminderPanel";
            customPanel.Children.Add(UiTheme.MakeSectionTitle("自定义提醒"));
            Grid form = new Grid();
            form.Name = "CustomReminderForm";
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel timePanel = new StackPanel();
            TextBlock timeLabel = UiTheme.MakeText("时间 HH:mm", 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            timeLabel.Margin = new Thickness(0, 0, 0, 4);
            timePanel.Children.Add(timeLabel);
            timePanel.Children.Add(_reminderTimeBox);
            form.Children.Add(timePanel);
            Grid.SetColumn(timePanel, 0);

            StackPanel modePanel = new StackPanel();
            TextBlock modeLabel = UiTheme.MakeText("适用状态", 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            modeLabel.Margin = new Thickness(0, 0, 0, 4);
            modePanel.Children.Add(modeLabel);
            modePanel.Children.Add(_newReminderModeCombo);
            modePanel.Margin = new Thickness(8, 0, 0, 0);
            form.Children.Add(modePanel);
            Grid.SetColumn(modePanel, 1);

            StackPanel messagePanel = new StackPanel();
            TextBlock messageLabel = UiTheme.MakeText("提醒内容", 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            messageLabel.Margin = new Thickness(0, 0, 0, 4);
            messagePanel.Children.Add(messageLabel);
            messagePanel.Children.Add(_reminderMessageBox);
            messagePanel.Margin = new Thickness(8, 0, 0, 0);
            form.Children.Add(messagePanel);
            Grid.SetColumn(messagePanel, 2);

            StackPanel formActions = new StackPanel();
            formActions.Orientation = Orientation.Horizontal;
            formActions.VerticalAlignment = VerticalAlignment.Bottom;
            formActions.Margin = new Thickness(8, 0, 0, 0);

            Button testButton = new Button();
            testButton.Content = "测试";
            testButton.Style = UiTheme.SecondaryButtonStyle;
            testButton.Height = 32;
            testButton.MinWidth = 54;
            testButton.Padding = new Thickness(8, 5, 8, 5);
            testButton.Margin = new Thickness(0, 0, 6, 0);
            testButton.ToolTip = "按当前时间和内容在悬浮窗上预览一次提醒，不会添加";
            testButton.Click += OnTestCustomReminder;
            AutomationProperties.SetAutomationId(testButton, "TestReminderButton");
            formActions.Children.Add(testButton);

            Button addButton = new Button();
            addButton.Name = "AddReminderButtonControl";
            addButton.Content = "添加";
            addButton.Style = UiTheme.PrimaryButtonStyle;
            addButton.Height = 32;
            addButton.MinWidth = 58;
            addButton.Padding = new Thickness(8, 5, 8, 5);
            addButton.Click += OnAddCustomReminder;
            AutomationProperties.SetAutomationId(addButton, "AddReminderButton");
            formActions.Children.Add(addButton);

            form.Children.Add(formActions);
            Grid.SetColumn(formActions, 3);

            customPanel.Children.Add(form);
            customPanel.Children.Add(_reminderStatus);
            customCard.Child = customPanel;
            panel.Children.Add(customCard);

            Border listCard = UiTheme.MakeCard();
            StackPanel listPanel = new StackPanel();
            Grid listHeader = new Grid();
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            listHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock listLabel = UiTheme.MakeText("已添加提醒", 12, UiTheme.TextSecondary, FontWeights.SemiBold);
            listLabel.Margin = new Thickness(0, 0, 0, 8);
            listHeader.Children.Add(listLabel);
            listHeader.Children.Add(_reminderCountText);
            Grid.SetColumn(_reminderCountText, 1);
            listPanel.Children.Add(listHeader);
            listPanel.Children.Add(_reminderScroll);
            listPanel.Children.Add(_reminderEmptyText);
            listCard.Child = listPanel;
            panel.Children.Add(listCard);

            return panel;
        }

        private UIElement BuildScheduleTab()
        {
            StackPanel panel = new StackPanel();

            Border toolbarCard = UiTheme.MakeCard();
            StackPanel toolbar = new StackPanel();
            toolbar.Children.Add(UiTheme.MakeSectionTitle("日程列表"));
            TextBlock intro = UiTheme.MakeText(
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

        private UIElement BuildWeatherTab()
        {
            StackPanel panel = new StackPanel();

            Border weatherCard = UiTheme.MakeCard();
            StackPanel content = new StackPanel();
            content.Children.Add(UiTheme.MakeSectionTitle("天气预报"));
            content.Children.Add(_weatherEnabledCheck);

            TextBlock fontLabel = UiTheme.MakeText("天气字体", 13, UiTheme.TextPrimary, FontWeights.Normal);
            fontLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(fontLabel);
            content.Children.Add(_weatherFontCombo);

            StackPanel styleRow = new StackPanel();
            styleRow.Orientation = Orientation.Horizontal;
            styleRow.Margin = new Thickness(0, 4, 0, 0);
            styleRow.Children.Add(_weatherBoldCheck);
            styleRow.Children.Add(_weatherItalicCheck);
            content.Children.Add(styleRow);

            content.Children.Add(BuildSliderBlock(
                "天气字号",
                _weatherSizeSlider,
                _weatherSizeValueText,
                "天气行单独使用的字号，9 到 60 像素",
                new Thickness(0, 12, 0, 2)));
            content.Children.Add(BuildWeatherColorRow());
            content.Children.Add(BuildWeatherPalette());
            content.Children.Add(_weatherShadowCheck);

            TextBlock refreshLabel = UiTheme.MakeText("自动更新频率", 13, UiTheme.TextPrimary, FontWeights.Normal);
            refreshLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(refreshLabel);
            content.Children.Add(_weatherRefreshCombo);

            TextBlock cityLabel = UiTheme.MakeText("城市", 13, UiTheme.TextPrimary, FontWeights.Normal);
            cityLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(cityLabel);

            Grid cityRow = new Grid();
            cityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cityRow.Children.Add(_weatherCityBox);
            cityRow.Children.Add(_weatherTestButton);
            Grid.SetColumn(_weatherTestButton, 1);
            content.Children.Add(cityRow);

            content.Children.Add(_weatherTestStatus);

            TextBlock cityHint = UiTheme.MakeText(
                "支持中文或英文城市名，例如：北京、上海、Tokyo。保存或应用后会立即刷新。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            cityHint.Margin = new Thickness(0, 6, 0, 0);
            cityHint.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(cityHint);

            TextBlock formatLabel = UiTheme.MakeText("天气格式（可选）", 13, UiTheme.TextPrimary, FontWeights.Normal);
            formatLabel.Margin = new Thickness(0, 14, 0, 6);
            content.Children.Add(formatLabel);
            content.Children.Add(_weatherFormatBox);
            content.Children.Add(_weatherFormatStatus);

            TextBlock formatHelp = UiTheme.MakeText(
                "可用变量：pos 城市  we1~we6 6日天气  tl1~tl6 低温  th1~th6 高温\n" +
                "rtm 实时温度  rhu 湿度  rwd 风向  icn 天气图标  aqi 空气质量  ip 公网 IP\n\n" +
                "示例：pos · we1 · rtm\n" +
                "示例：icn rtm · rhu · rwd\n" +
                "示例：pos · we1 · rtm · 今日 tl1~th1\n" +
                "留空时使用默认格式：城市 · 天气 · 温度 · 今日高低温。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            formatHelp.Margin = new Thickness(0, 8, 0, 0);
            formatHelp.TextWrapping = TextWrapping.Wrap;
            formatHelp.LineHeight = 18;
            content.Children.Add(formatHelp);

            TextBlock apiHint = UiTheme.MakeText(
                "天气数据来自 Open-Meteo 公共 API，按设置的频率自动更新（默认 30 分钟）；无需 API Key。网络不可用时会自动隐藏天气行。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            apiHint.Margin = new Thickness(0, 10, 0, 0);
            apiHint.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(apiHint);

            weatherCard.Child = content;
            panel.Children.Add(weatherCard);

            Border noteCard = UiTheme.MakeCard();
            StackPanel note = new StackPanel();
            note.Children.Add(UiTheme.MakeSectionTitle("显示说明"));
            TextBlock noteText = UiTheme.MakeText(
                "天气会显示在时间下方，例如：\n北京 · 晴 · 11°C · 今日 5°~16°\n\n" +
                "天气的字体、字号、颜色和阴影都可以独立于时间设置。\n" +
                "只发送城市名称和坐标到公共天气服务，不包含任何个人账号信息。",
                11.5,
                UiTheme.TextSecondary,
                FontWeights.Normal);
            noteText.LineHeight = 19;
            note.Children.Add(noteText);
            noteCard.Child = note;
            panel.Children.Add(noteCard);

            return panel;
        }

        private UIElement BuildWeatherColorRow()
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 12, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            TextBlock label = UiTheme.MakeText("天气颜色", 13, UiTheme.TextPrimary, FontWeights.Normal);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(label);
            left.Children.Add(_weatherColorSwatch);
            left.Children.Add(_weatherColorHexText);
            row.Children.Add(left);
            Grid.SetColumn(left, 0);

            Button pick = new Button();
            pick.Content = "选择…";
            pick.Style = UiTheme.SecondaryButtonStyle;
            pick.Padding = new Thickness(13, 6, 13, 6);
            pick.Height = 30;
            pick.Click += OnPickWeatherColor;
            AutomationProperties.SetAutomationId(pick, "PickWeatherColorButton");
            row.Children.Add(pick);
            Grid.SetColumn(pick, 1);

            return row;
        }

        private UIElement BuildWeatherPalette()
        {
            WrapPanel palette = new WrapPanel();
            palette.Margin = new Thickness(0, 10, 0, 2);
            Color[] presetColors = new Color[]
            {
                Color.FromRgb(0xFF, 0x3B, 0x30),
                Color.FromRgb(0xFF, 0x95, 0x00),
                Color.FromRgb(0xFF, 0xD6, 0x0A),
                Color.FromRgb(0x34, 0xC7, 0x59),
                Color.FromRgb(0x00, 0xC7, 0xBE),
                Color.FromRgb(0x0A, 0x84, 0xFF),
                Color.FromRgb(0xBF, 0x5A, 0xF2),
                Color.FromRgb(0x00, 0x00, 0x00),
                Color.FromRgb(0xF2, 0xF2, 0xF7)
            };
            string[] names = new string[]
            {
                "红色（默认）", "橙色", "黄色", "绿色", "青色", "蓝色", "紫色", "黑色", "白色"
            };

            for (int i = 0; i < presetColors.Length; i++)
            {
                Color presetColor = presetColors[i];
                Border swatch = new Border();
                swatch.Width = 24;
                swatch.Height = 24;
                swatch.CornerRadius = new CornerRadius(12);
                swatch.Background = UiTheme.BrushFrom(presetColor);
                swatch.BorderThickness = new Thickness(1);
                swatch.BorderBrush = UiTheme.Frozen(0x55, 0x55, 0x60);
                swatch.Margin = new Thickness(0, 0, 9, 0);
                swatch.Cursor = Cursors.Hand;
                swatch.ToolTip = names[i] + "  " + ColorUtil.ToHex(presetColor);
                swatch.MouseLeftButtonDown += delegate
                {
                    SelectWeatherColor(presetColor);
                };
                palette.Children.Add(swatch);
                _weatherColorPresets.Add(new ColorPreset(presetColor, swatch));
            }
            return palette;
        }

        private Border BuildFooter()
        {
            Border footer = new Border();
            footer.Background = UiTheme.Frozen(0x1C, 0x1C, 0x22);
            footer.BorderBrush = UiTheme.CardBorder;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(24, 12, 24, 12);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel leftActions = new StackPanel();
            leftActions.Orientation = Orientation.Horizontal;
            leftActions.VerticalAlignment = VerticalAlignment.Center;

            Button reset = new Button();
            reset.Content = "恢复默认";
            reset.Style = UiTheme.GhostButtonStyle;
            reset.MinWidth = 88;
            reset.Height = 34;
            reset.Click += OnResetDefaults;
            AutomationProperties.SetAutomationId(reset, "ResetButton");
            leftActions.Children.Add(reset);

            Button about = new Button();
            about.Content = "关于";
            about.Style = UiTheme.GhostButtonStyle;
            about.MinWidth = 62;
            about.Height = 34;
            about.Margin = new Thickness(2, 0, 0, 0);
            about.Click += OnAbout;
            AutomationProperties.SetAutomationId(about, "AboutButton");
            leftActions.Children.Add(about);

            grid.Children.Add(leftActions);
            Grid.SetColumn(leftActions, 0);

            grid.Children.Add(_footerStatus);
            Grid.SetColumn(_footerStatus, 1);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.VerticalAlignment = VerticalAlignment.Center;

            Button apply = new Button();
            apply.Content = "应用";
            apply.Style = UiTheme.SecondaryButtonStyle;
            apply.MinWidth = 82;
            apply.Height = 34;
            apply.Margin = new Thickness(0, 0, 9, 0);
            apply.Click += OnApply;
            apply.ToolTip = "保存设置但不关闭窗口";
            AutomationProperties.SetAutomationId(apply, "ApplyButton");
            actions.Children.Add(apply);

            Button save = new Button();
            save.Content = "保存并退出";
            save.Style = UiTheme.PrimaryButtonStyle;
            save.MinWidth = 108;
            save.Height = 34;
            save.IsDefault = true;
            save.Click += OnSaveAndExit;
            save.ToolTip = "保存设置并关闭设置窗口";
            AutomationProperties.SetAutomationId(save, "SaveButton");
            actions.Children.Add(save);

            grid.Children.Add(actions);
            Grid.SetColumn(actions, 2);
            footer.Child = grid;
            return footer;
        }

        private UIElement BuildColorRow()
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 14, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            TextBlock label = UiTheme.MakeText("时间颜色", 13, UiTheme.TextPrimary, FontWeights.Normal);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(label);
            left.Children.Add(_colorSwatch);
            left.Children.Add(_colorHexText);
            row.Children.Add(left);
            Grid.SetColumn(left, 0);

            Button pick = new Button();
            pick.Content = "选择…";
            pick.Style = UiTheme.SecondaryButtonStyle;
            pick.Padding = new Thickness(13, 6, 13, 6);
            pick.Height = 30;
            pick.Click += OnPickColor;
            AutomationProperties.SetAutomationId(pick, "PickColorButton");
            row.Children.Add(pick);
            Grid.SetColumn(pick, 1);

            return row;
        }

        private UIElement BuildPalette()
        {
            WrapPanel palette = new WrapPanel();
            palette.Margin = new Thickness(0, 12, 0, 0);
            Color[] presetColors = new Color[]
            {
                Color.FromRgb(0xFF, 0x3B, 0x30),
                Color.FromRgb(0xFF, 0x95, 0x00),
                Color.FromRgb(0xFF, 0xD6, 0x0A),
                Color.FromRgb(0x34, 0xC7, 0x59),
                Color.FromRgb(0x00, 0xC7, 0xBE),
                Color.FromRgb(0x0A, 0x84, 0xFF),
                Color.FromRgb(0xBF, 0x5A, 0xF2),
                Color.FromRgb(0x00, 0x00, 0x00),
                Color.FromRgb(0xF2, 0xF2, 0xF7)
            };
            string[] names = new string[]
            {
                "红色（默认）", "橙色", "黄色", "绿色", "青色", "蓝色", "紫色", "黑色", "白色"
            };

            for (int i = 0; i < presetColors.Length; i++)
            {
                Color presetColor = presetColors[i];
                Border swatch = new Border();
                swatch.Width = 24;
                swatch.Height = 24;
                swatch.CornerRadius = new CornerRadius(12);
                swatch.Background = UiTheme.BrushFrom(presetColor);
                swatch.BorderThickness = new Thickness(1);
                swatch.BorderBrush = UiTheme.Frozen(0x55, 0x55, 0x60);
                swatch.Margin = new Thickness(0, 0, 9, 0);
                swatch.Cursor = Cursors.Hand;
                swatch.ToolTip = names[i] + "  " + ColorUtil.ToHex(presetColor);
                swatch.MouseLeftButtonDown += delegate
                {
                    SelectColor(presetColor);
                };
                palette.Children.Add(swatch);
                _presets.Add(new ColorPreset(presetColor, swatch));
            }
            return palette;
        }

        private UIElement BuildSliderBlock(string labelText, Slider slider, TextBlock valueText, string helpText, Thickness margin)
        {
            StackPanel panel = new StackPanel();
            panel.Margin = margin;

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock label = UiTheme.MakeText(labelText, 13, UiTheme.TextPrimary, FontWeights.Normal);
            header.Children.Add(label);
            header.Children.Add(valueText);
            Grid.SetColumn(valueText, 1);
            panel.Children.Add(header);

            slider.Margin = new Thickness(0, 3, 0, 0);
            slider.ToolTip = helpText;
            panel.Children.Add(slider);
            return panel;
        }

        private static Slider CreateSlider(double min, double max, double value, string automationId)
        {
            Slider slider = new Slider();
            slider.Minimum = min;
            slider.Maximum = max;
            slider.Value = value;
            slider.SmallChange = 1;
            slider.LargeChange = 5;
            slider.IsSnapToTickEnabled = true;
            slider.TickFrequency = 1;
            slider.Focusable = true;
            AutomationProperties.SetAutomationId(slider, automationId);
            return slider;
        }

        private static CheckBox CreateCheckBox(string text, bool isChecked, string automationId, string toolTip)
        {
            CheckBox check = new CheckBox();
            check.Content = text;
            check.IsChecked = isChecked;
            check.Foreground = UiTheme.TextPrimary;
            check.FontSize = 13;
            check.Margin = new Thickness(0, 4, 0, 5);
            check.VerticalContentAlignment = VerticalAlignment.Center;
            check.ToolTip = toolTip;
            AutomationProperties.SetAutomationId(check, automationId);
            return check;
        }

        private static DataTemplate CreateFontItemTemplate()
        {
            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            text.SetBinding(TextBlock.FontFamilyProperty, new Binding("Family"));
            text.SetValue(TextBlock.FontSizeProperty, 14.0);
            text.SetValue(TextBlock.ForegroundProperty, UiTheme.TextPrimary);

            DataTemplate template = new DataTemplate();
            template.VisualTree = text;
            return template;
        }

        private void AttachEvents()
        {
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
            _formatCombo.SelectionChanged += OnFormatPresetChanged;
            _formatBox.TextChanged += OnFormatTextChanged;
            _formatBox.PreviewKeyDown += OnFormatKeyDown;
            _dotNetFormatRadio.Checked += OnFormatModeChanged;
            _tokenFormatRadio.Checked += OnFormatModeChanged;
            _formatHelpSearchBox.TextChanged += OnFormatHelpSearchChanged;
            _weatherFormatBox.TextChanged += OnWeatherFormatTextChanged;
            _timeZoneCombo.SelectionChanged += OnTimeZoneChanged;
            _reminderMessageBox.KeyDown += OnReminderMessageKeyDown;
            _workModeRadio.Checked += OnCurrentStateChanged;
            _leisureModeRadio.Checked += OnCurrentStateChanged;
            _reminderDurationCombo.SelectionChanged += OnReminderDurationChanged;
            _reminderHoverCheck.Checked += OnReminderHoverChanged;
            _reminderHoverCheck.Unchecked += OnReminderHoverChanged;
            _reminderBlinkSlider.ValueChanged += OnReminderBlinkChanged;
            _reminderSoundCheck.Checked += OnReminderSoundChanged;
            _reminderSoundCheck.Unchecked += OnReminderSoundChanged;
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
            PreviewKeyDown += OnWindowPreviewKeyDown;
        }

        private void LoadControlsFromWorking()
        {
            FontChoice choice = FontCatalog.FindChoice(_fontChoices, _working.FontFamilyName);
            if (choice == null)
            {
                choice = FontCatalog.FindChoice(_fontChoices, FontCatalog.DefaultFont);
            }
            _fontCombo.SelectedItem = choice;

            _sizeSlider.Value = _working.FontSize;
            _opacitySlider.Value = Math.Round(_working.Opacity * 100.0);
            _sizeValueText.Text = ((int)Math.Round(_working.FontSize)).ToString(CultureInfo.InvariantCulture) + " px";
            _opacityValueText.Text = ((int)Math.Round(_working.Opacity * 100.0)).ToString(CultureInfo.InvariantCulture) + "%";

            _shadowCheck.IsChecked = _working.ShowShadow;
            _threeDCheck.IsChecked = _working.Text3D;
            _clickThroughCheck.IsChecked = _working.ClickThrough;
            _topmostCheck.IsChecked = _working.Topmost;
            _startWithWindowsCheck.IsChecked = _working.StartWithWindows;

            string mode = ReminderModes.Normalize(_working.CurrentMode);
            _workModeRadio.IsChecked = mode == ReminderModes.Work;
            _leisureModeRadio.IsChecked = mode == ReminderModes.Leisure;
            SelectReminderDurationOption(_working.ReminderDisplaySeconds);
            _reminderHoverCheck.IsChecked = _working.ReminderHoverCloseEnabled;
            _reminderBlinkSlider.Value = _working.ReminderBlinkCount;
            _reminderBlinkValueText.Text = _working.ReminderBlinkCount + " 次";
            _reminderSoundCheck.IsChecked = _working.ReminderSoundEnabled;

            _weatherEnabledCheck.IsChecked = _working.WeatherEnabled;
            _weatherCityBox.Text = _working.WeatherCity;
            _weatherCityBox.IsEnabled = _working.WeatherEnabled;

            FontChoice weatherFont = FontCatalog.FindChoice(_fontChoices, _working.WeatherFontFamilyName);
            if (weatherFont == null)
            {
                weatherFont = FontCatalog.FindChoice(_fontChoices, "Microsoft YaHei UI");
            }
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
            _formatStatus.Foreground = UiTheme.TextMuted;

            UpdateColorUi();
            RebuildReminderRows();
            RebuildScheduleRows();
            UpdatePreview();
        }

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnFontChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            FontChoice choice = _fontCombo.SelectedItem as FontChoice;
            if (choice != null)
            {
                _working.FontFamilyName = choice.Name;
                UpdatePreview();
                MarkDirty();
            }
        }

        private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            _working.FontSize = e.NewValue;
            _sizeValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + " px";
            UpdatePreview();
            MarkDirty();
        }

        private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            _working.Opacity = e.NewValue / 100.0;
            _opacityValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + "%";
            UpdatePreview();
            MarkDirty();
        }

        private void OnEffectChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.ShowShadow = _shadowCheck.IsChecked == true;
            _working.Text3D = _threeDCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        private void OnBehaviorChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.ClickThrough = _clickThroughCheck.IsChecked == true;
            _working.Topmost = _topmostCheck.IsChecked == true;
            _working.StartWithWindows = _startWithWindowsCheck.IsChecked == true;
            MarkDirty();
        }

        private void OnCurrentStateChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.CurrentMode = _workModeRadio.IsChecked == true
                ? ReminderModes.Work
                : ReminderModes.Leisure;
            MarkDirty();
        }

        private void OnReminderDurationChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            ReminderDisplayOption option = _reminderDurationCombo.SelectedItem as ReminderDisplayOption;
            if (option != null)
            {
                _working.ReminderDisplaySeconds = option.Seconds;
                MarkDirty();
            }
        }

        private void OnReminderHoverChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.ReminderHoverCloseEnabled = _reminderHoverCheck.IsChecked == true;
            MarkDirty();
        }

        private void OnReminderBlinkChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            int count = (int)Math.Round(e.NewValue);
            _working.ReminderBlinkCount = count;
            _reminderBlinkValueText.Text = count + " 次";
            MarkDirty();
        }

        private void OnReminderSoundChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.ReminderSoundEnabled = _reminderSoundCheck.IsChecked == true;
            MarkDirty();
        }

        private void SelectReminderDurationOption(int seconds)
        {
            ReminderDisplayOption match = null;
            for (int i = 0; i < _reminderDisplayOptions.Count; i++)
            {
                if (_reminderDisplayOptions[i].Seconds == seconds)
                {
                    match = _reminderDisplayOptions[i];
                    break;
                }
            }
            if (match == null)
            {
                match = _reminderDisplayOptions[3];
            }

            bool wasUpdating = _updating;
            _updating = true;
            try
            {
                _reminderDurationCombo.SelectedItem = match;
            }
            finally
            {
                _updating = wasUpdating;
            }
        }

        private void OnWeatherChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherEnabled = _weatherEnabledCheck.IsChecked == true;
            _weatherCityBox.IsEnabled = _working.WeatherEnabled;
            UpdatePreview();
            MarkDirty();
        }

        private void SelectWeatherRefreshOption(int minutes)
        {
            WeatherRefreshOption match = null;
            for (int i = 0; i < _weatherRefreshOptions.Count; i++)
            {
                if (_weatherRefreshOptions[i].Minutes == minutes)
                {
                    match = _weatherRefreshOptions[i];
                    break;
                }
            }
            if (match == null)
            {
                match = _weatherRefreshOptions[2];
            }

            bool wasUpdating = _updating;
            _updating = true;
            try
            {
                _weatherRefreshCombo.SelectedItem = match;
            }
            finally
            {
                _updating = wasUpdating;
            }
        }
        private void OnWeatherCityChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherCity = (_weatherCityBox.Text ?? string.Empty).Trim();
            UpdatePreview();
            MarkDirty();
        }

        private void OnWeatherFontChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            FontChoice choice = _weatherFontCombo.SelectedItem as FontChoice;
            if (choice != null)
            {
                _working.WeatherFontFamilyName = choice.Name;
                UpdatePreview();
                MarkDirty();
            }
        }

        private void OnWeatherSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherFontSize = e.NewValue;
            _weatherSizeValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + " px";
            UpdatePreview();
            MarkDirty();
        }

        private void OnWeatherShadowChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherShowShadow = _weatherShadowCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        private void OnWeatherFontStyleChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherFontBold = _weatherBoldCheck.IsChecked == true;
            _working.WeatherFontItalic = _weatherItalicCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        private void OnWeatherRefreshChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            WeatherRefreshOption option = _weatherRefreshCombo.SelectedItem as WeatherRefreshOption;
            if (option != null)
            {
                _working.WeatherRefreshMinutes = option.Minutes;
                MarkDirty();
            }
        }

        private void OnTestWeatherCity(object sender, RoutedEventArgs e)
        {
            string city = (_weatherCityBox.Text ?? string.Empty).Trim();
            if (city.Length == 0)
            {
                _weatherTestStatus.Text = "请先填写城市名。";
                _weatherTestStatus.Foreground = UiTheme.Accent;
                return;
            }

            int version = ++_weatherTestVersion;
            _weatherTestButton.IsEnabled = false;
            _weatherTestButton.Content = "查询中…";
            _weatherTestStatus.Text = "正在查询 " + city + " 的天气，请稍候…";
            _weatherTestStatus.Foreground = UiTheme.TextMuted;

            WeatherService.FetchAsync(city, delegate(WeatherInfo info)
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (version != _weatherTestVersion)
                        {
                            return;
                        }
                        _weatherTestButton.IsEnabled = true;
                        _weatherTestButton.Content = "测试地名";
                        if (info != null)
                        {
                            _weatherTestStatus.Text = "✓ 查询成功：" + info.DisplayText;
                            _weatherTestStatus.Foreground = UiTheme.Success;
                        }
                        else
                        {
                            _weatherTestStatus.Text = "✕ 查询失败：未找到该地名，或网络暂时不可用。";
                            _weatherTestStatus.Foreground = UiTheme.Accent;
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void OnPickColor(object sender, RoutedEventArgs e)
        {
            using (WinForms.ColorDialog dialog = new WinForms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                Color current = _working.TimeColor;
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WinForms.IWin32Window owner = new SimpleWin32Window(handle);
                if (dialog.ShowDialog(owner) == WinForms.DialogResult.OK)
                {
                    SelectColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
                }
            }
        }

        private void SelectColor(Color color)
        {
            _working.ColorHex = ColorUtil.ToHex(color);
            UpdateColorUi();
            UpdatePreview();
            MarkDirty();
        }

        private void UpdateColorUi()
        {
            Color color = _working.TimeColor;
            _colorSwatch.Background = UiTheme.BrushFrom(color);
            _colorHexText.Text = ColorUtil.ToHex(color);

            for (int i = 0; i < _presets.Count; i++)
            {
                ColorPreset preset = _presets[i];
                bool selected = string.Equals(
                    ColorUtil.ToHex(preset.Color),
                    _working.ColorHex,
                    StringComparison.OrdinalIgnoreCase);
                preset.Swatch.BorderThickness = new Thickness(selected ? 3 : 1);
                preset.Swatch.BorderBrush = selected
                    ? UiTheme.TextPrimary
                    : UiTheme.Frozen(0x55, 0x55, 0x60);
            }
        }

        private void OnPickWeatherColor(object sender, RoutedEventArgs e)
        {
            using (WinForms.ColorDialog dialog = new WinForms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                Color current = _working.WeatherColor;
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WinForms.IWin32Window owner = new SimpleWin32Window(handle);
                if (dialog.ShowDialog(owner) == WinForms.DialogResult.OK)
                {
                    SelectWeatherColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
                }
            }
        }

        private void SelectWeatherColor(Color color)
        {
            _working.WeatherColorHex = ColorUtil.ToHex(color);
            UpdateWeatherColorUi();
            UpdatePreview();
            MarkDirty();
        }

        private void UpdateWeatherColorUi()
        {
            Color color = _working.WeatherColor;
            _weatherColorSwatch.Background = UiTheme.BrushFrom(color);
            _weatherColorHexText.Text = ColorUtil.ToHex(color);

            for (int i = 0; i < _weatherColorPresets.Count; i++)
            {
                ColorPreset preset = _weatherColorPresets[i];
                bool selected = string.Equals(
                    ColorUtil.ToHex(preset.Color),
                    _working.WeatherColorHex,
                    StringComparison.OrdinalIgnoreCase);
                preset.Swatch.BorderThickness = new Thickness(selected ? 3 : 1);
                preset.Swatch.BorderBrush = selected
                    ? UiTheme.TextPrimary
                    : UiTheme.Frozen(0x55, 0x55, 0x60);
            }
        }

        private void UpdatePreview()
        {
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

            bool inlineWeather = FormatEngine.IsTokenMode(_working.FormatMode)
                && FormatEngine.ContainsWeatherToken(_working.TokenTimeFormat);
            bool showWeather = _working.WeatherEnabled && !inlineWeather;
            _previewWeatherText.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
            if (!showWeather)
            {
                return;
            }

            string city = string.IsNullOrWhiteSpace(_working.WeatherCity) ? "北京" : _working.WeatherCity;
            Color weatherColor = _working.WeatherColor;
            _previewWeatherText.Text = string.IsNullOrWhiteSpace(_working.WeatherFormat)
                ? city + " · 晴 · 12°C · 今日 9°~17°"
                : FormatEngine.Format(now, sample, _working.WeatherFormat);
            _previewWeatherText.FontFamily = FontCatalog.GetFontFamily(_working.WeatherFontFamilyName);
            _previewWeatherText.FontSize = _working.WeatherFontSize;
            _previewWeatherText.FontWeight = _working.WeatherFontBold ? FontWeights.Bold : FontWeights.Normal;
            _previewWeatherText.FontStyle = _working.WeatherFontItalic ? FontStyles.Italic : FontStyles.Normal;
            _previewWeatherText.Foreground = UiTheme.BrushFrom(
                Color.FromArgb(232, weatherColor.R, weatherColor.G, weatherColor.B));
            _previewWeatherText.Margin = new Thickness(0, Math.Max(2, _working.WeatherFontSize * 0.20), 0, 0);
            _previewWeatherText.Opacity = _working.Opacity;
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
                _previewWeatherText.Effect = null;
            }
        }

        private void OnFormatModeChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string newMode = tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet;
            if (string.Equals(_working.FormatMode, newMode, StringComparison.Ordinal))
            {
                return;
            }

            if (FormatEngine.IsTokenMode(_working.FormatMode))
            {
                _working.TokenTimeFormat = _formatBox.Text;
            }
            else
            {
                _working.TimeFormat = _formatBox.Text;
            }
            _working.FormatMode = newMode;
            _formatPresets = tokenMode ? _tokenFormatPresets : _dotNetFormatPresets;

            _updating = true;
            try
            {
                _formatCombo.ItemsSource = _formatPresets;
                _formatBox.Text = tokenMode ? _working.TokenTimeFormat : _working.TimeFormat;
            }
            finally
            {
                _updating = false;
            }

            UpdateFormatHelp();
            HandleFormatTextChanged();
            MarkDirty();
        }

        private void UpdateFormatHelp()
        {
            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            if (tokenMode)
            {
                _formatHintText.Text =
                    "支持简洁变量（区分大小写；mm 是月份、ff 是分钟）。输入时实时预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            }
            else
            {
                _formatHintText.Text =
                    "支持 .NET 日期格式（MM 是月份、mm 是分钟）。输入时实时预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            }

            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            _formatHelpItems = FormatHelpCatalog.Create(
                tokenMode ? FormatEngine.ModeToken : FormatEngine.ModeDotNet,
                now,
                CreateSampleWeather());
            RebuildFormatHelp();
        }

        private void OnFormatHelpSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (_formatHelpItems == null)
            {
                return;
            }
            RebuildFormatHelp();
        }

        private void RebuildFormatHelp()
        {
            if (_formatHelpPanel == null || _formatHelpItems == null)
            {
                return;
            }

            string query = (_formatHelpSearchBox.Text ?? string.Empty).Trim();
            _formatHelpPanel.Children.Clear();
            _formatHelpScroll.ScrollToTop();

            int shown = 0;
            for (int i = 0; i < _formatHelpItems.Count; i++)
            {
                FormatHelpItem item = _formatHelpItems[i];
                if (!MatchesFormatHelp(item, query))
                {
                    continue;
                }
                _formatHelpPanel.Children.Add(CreateFormatHelpRow(item));
                shown++;
            }

            if (shown == 0)
            {
                TextBlock empty = UiTheme.MakeText(
                    "没有匹配的变量，试试 yyyy、mm、农历、星期。",
                    11.5,
                    UiTheme.TextMuted,
                    FontWeights.Normal);
                empty.Margin = new Thickness(2, 6, 2, 6);
                _formatHelpPanel.Children.Add(empty);
            }

            _formatHelpCount.Text = "显示 " + shown + " / " + _formatHelpItems.Count + " 个变量";
        }

        private static bool MatchesFormatHelp(FormatHelpItem item, string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return true;
            }
            return item.Token.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Example.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static UIElement CreateFormatHelpRow(FormatHelpItem item)
        {
            TextBlock line = new TextBlock();
            line.FontFamily = new FontFamily("Microsoft YaHei UI");
            line.FontSize = 11.5;
            line.TextWrapping = TextWrapping.NoWrap;
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            line.Margin = new Thickness(2, 3, 2, 3);

            Run token = new Run(item.Token);
            token.FontFamily = new FontFamily("Consolas");
            token.FontWeight = FontWeights.SemiBold;
            token.Foreground = UiTheme.Accent;
            line.Inlines.Add(token);
            line.Inlines.Add(new Run("：" + item.Description + "。")
            {
                Foreground = UiTheme.TextSecondary
            });
            line.Inlines.Add(new Run("示例预览：" + item.Example)
            {
                Foreground = UiTheme.TextMuted
            });
            line.ToolTip = item.DisplayText;
            return line;
        }

        private void OnWeatherFormatTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            string text = (_weatherFormatBox.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                _working.WeatherFormat = string.Empty;
                UpdateWeatherFormatStatus();
                UpdatePreview();
                MarkDirty();
                return;
            }

            string normalized;
            string error;
            if (FormatEngine.TryNormalizeTokenFormat(text, false, out normalized, out error))
            {
                _working.WeatherFormat = normalized;
                UpdateWeatherFormatStatus();
                UpdatePreview();
            }
            else
            {
                _weatherFormatStatus.Text = error;
                _weatherFormatStatus.Foreground = UiTheme.Accent;
            }
            MarkDirty();
        }

        private void UpdateWeatherFormatStatus()
        {
            if (string.IsNullOrWhiteSpace(_working.WeatherFormat))
            {
                _weatherFormatStatus.Text = "留空使用默认格式：城市 · 天气 · 温度 · 今日高低温。";
                _weatherFormatStatus.Foreground = UiTheme.TextMuted;
                return;
            }

            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            string example = FormatEngine.Format(now, CreateSampleWeather(), _working.WeatherFormat);
            _weatherFormatStatus.Text = "预览：" + example;
            _weatherFormatStatus.Foreground = UiTheme.TextMuted;
        }

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

        private void OnFormatPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            TimeFormatPreset preset = _formatCombo.SelectedItem as TimeFormatPreset;
            if (preset == null || string.IsNullOrEmpty(preset.Format))
            {
                return;
            }

            _updating = true;
            try
            {
                _formatBox.Text = preset.Format;
            }
            finally
            {
                _updating = false;
            }
            HandleFormatTextChanged();
        }

        private void OnTimeZoneChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            TimeZoneChoice choice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            if (choice == null)
            {
                return;
            }

            _working.TimeZoneId = choice.Id;
            UpdateTimeZoneStatus();
            UpdatePreview();
            MarkDirty();
        }

        private void UpdateTimeZoneStatus()
        {
            TimeZoneChoice choice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            if (choice == null)
            {
                return;
            }
            _timeZoneStatus.Text = "当前：" + TimeZoneCatalog.GetOffsetLabel(choice.Id) + " · " + choice.Id;
            _timeZoneStatus.Foreground = UiTheme.TextMuted;
        }

        private void SelectTimeZoneChoice(string id)
        {
            string normalized = TimeZoneCatalog.Normalize(id);
            TimeZoneChoice match = null;
            for (int i = 0; i < _timeZoneChoices.Count; i++)
            {
                if (string.Equals(_timeZoneChoices[i].Id, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    match = _timeZoneChoices[i];
                    break;
                }
            }
            if (match == null && _timeZoneChoices.Count > 0)
            {
                match = _timeZoneChoices[0];
            }

            bool wasUpdating = _updating;
            _updating = true;
            try
            {
                _timeZoneCombo.SelectedItem = match;
            }
            finally
            {
                _updating = wasUpdating;
            }
            UpdateTimeZoneStatus();
        }

        private void OnFormatTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            HandleFormatTextChanged();
        }

        private void HandleFormatTextChanged()
        {
            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string normalized;
            string error;
            bool valid = tokenMode
                ? FormatEngine.TryNormalizeTokenFormat(_formatBox.Text, true, out normalized, out error)
                : TimeFormatter.TryNormalizeFormat(_formatBox.Text, out normalized, out error);

            if (valid)
            {
                if (tokenMode)
                {
                    _working.TokenTimeFormat = normalized;
                }
                else
                {
                    _working.TimeFormat = normalized;
                }
                _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
                _formatStatus.Foreground = UiTheme.TextMuted;
                UpdatePreview();
            }
            else
            {
                _formatStatus.Text = error;
                _formatStatus.Foreground = UiTheme.Accent;
                _formatStatus.BringIntoView();
            }

            UpdateFormatComboSelection();
            MarkDirty();
        }

        private void UpdateFormatComboSelection()
        {
            TimeFormatPreset match = null;
            for (int i = 0; i < _formatPresets.Count; i++)
            {
                if (string.Equals(_formatPresets[i].Format, _formatBox.Text, StringComparison.Ordinal))
                {
                    match = _formatPresets[i];
                    break;
                }
            }

            _updating = true;
            try
            {
                _formatCombo.SelectedItem = match != null ? (object)match : _customFormatPreset;
            }
            finally
            {
                _updating = false;
            }
        }

        private void OnFormatKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                InsertFormatNewline();
            }
            else
            {
                HandleFormatTextChanged();
            }
            e.Handled = true;
        }

        private void OnInsertNewline(object sender, RoutedEventArgs e)
        {
            InsertFormatNewline();
        }

        private void InsertFormatNewline()
        {
            if (TimeFormatter.CountNewlines(_formatBox.Text) >= 1)
            {
                _formatStatus.Text = "最多只能插入一个换行。";
                _formatStatus.Foreground = UiTheme.Accent;
                return;
            }

            int caret = _formatBox.CaretIndex;
            if (caret < 0)
            {
                caret = _formatBox.Text.Length;
            }
            _formatBox.Text = _formatBox.Text.Insert(caret, "\n");
            _formatBox.CaretIndex = caret + 1;
            HandleFormatTextChanged();
        }

        private void OnAddPresetReminder(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            ReminderPreset preset = button != null ? button.Tag as ReminderPreset : null;
            if (preset == null)
            {
                return;
            }

            SyncReminderRows();
            if (ContainsReminder(preset.Time, preset.Message, preset.Mode))
            {
                SetReminderStatus("这个内置提醒已经在列表里了。", false);
                return;
            }

            Reminder reminder = new Reminder();
            reminder.Time = preset.Time;
            reminder.Message = preset.Message;
            reminder.Mode = preset.Mode;
            reminder.Enabled = true;
            _working.Reminders.Add(reminder);
            RebuildReminderRows();
            SetReminderStatus("已添加：" + preset.Time + "  " + preset.Message, true);
            MarkDirty();
        }

        private void OnAddCustomReminder(object sender, RoutedEventArgs e)
        {
            AddCustomReminder();
        }

        private void OnTestCustomReminder(object sender, RoutedEventArgs e)
        {
            string time;
            if (!TimeFormatter.TryNormalizeTime(_reminderTimeBox.Text, out time))
            {
                SetReminderStatus("时间格式应为 HH:mm，例如 23:00。", false);
                _reminderTimeBox.Focus();
                return;
            }

            string message = (_reminderMessageBox.Text ?? string.Empty).Trim();
            if (message.Length == 0)
            {
                SetReminderStatus("请填写提醒内容后再测试。", false);
                _reminderMessageBox.Focus();
                return;
            }

            RaiseReminderTest(time, message);
            SetReminderStatus("已发送测试提醒：" + time + "  " + message, true);
        }

        private void RaiseReminderTest(string time, string message)
        {
            if (ReminderTestRequested != null)
            {
                ReminderTestRequested(this, new ReminderPreviewEventArgs(time, message));
            }
        }

        private void OnReminderMessageKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                e.Handled = true;
                AddCustomReminder();
            }
        }

        private void AddCustomReminder()
        {
            string time;
            if (!TimeFormatter.TryNormalizeTime(_reminderTimeBox.Text, out time))
            {
                SetReminderStatus("时间格式应为 HH:mm，例如 23:00。", false);
                _reminderTimeBox.Focus();
                return;
            }

            string message = (_reminderMessageBox.Text ?? string.Empty).Trim();
            if (message.Length == 0)
            {
                SetReminderStatus("请填写提醒内容。", false);
                _reminderMessageBox.Focus();
                return;
            }
            if (message.Length > 120)
            {
                message = message.Substring(0, 120);
            }

            SyncReminderRows();
            ReminderModeOption modeOption = _newReminderModeCombo.SelectedItem as ReminderModeOption;
            string mode = modeOption != null ? modeOption.Value : ReminderModes.Both;
            if (ContainsReminder(time, message, mode))
            {
                SetReminderStatus("相同时间、内容和状态的提醒已存在。", false);
                return;
            }

            Reminder reminder = new Reminder();
            reminder.Time = time;
            reminder.Message = message;
            reminder.Mode = mode;
            reminder.Enabled = true;
            _working.Reminders.Add(reminder);
            RebuildReminderRows();
            _reminderMessageBox.Text = "";
            SetReminderStatus("已添加：" + time + "  " + message, true);
            MarkDirty();
        }

        private bool ContainsReminder(string time, string message, string mode)
        {
            for (int i = 0; i < _working.Reminders.Count; i++)
            {
                Reminder item = _working.Reminders[i];
                if (string.Equals(item.Time, time, StringComparison.Ordinal)
                    && string.Equals(item.Message, message, StringComparison.Ordinal)
                    && string.Equals(ReminderModes.Normalize(item.Mode), ReminderModes.Normalize(mode), StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private void SetReminderStatus(string text, bool success)
        {
            _reminderStatus.Text = text;
            _reminderStatus.Foreground = success ? UiTheme.TextSecondary : UiTheme.Accent;
        }

        private void RebuildReminderRows()
        {
            _reminderRowsPanel.Children.Clear();
            _reminderRows.Clear();

            for (int i = 0; i < _working.Reminders.Count; i++)
            {
                Reminder reminder = _working.Reminders[i];
                ReminderRow row = CreateReminderRow(reminder);
                _reminderRows.Add(row);
                _reminderRowsPanel.Children.Add(row.Container);
            }

            _reminderCountText.Text = _working.Reminders.Count.ToString(CultureInfo.InvariantCulture) + " 条";
            _reminderEmptyText.Visibility = _working.Reminders.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            _reminderScroll.Visibility = _working.Reminders.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
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
            else if (visible == 0)
            {
                _scheduleEmptyText.Text = "没有匹配的日程，换个关键词试试。";
            }

            _scheduleEmptyText.Visibility = visible == 0
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
            _scheduleStatus.Foreground = UiTheme.Success;
        }

        private ReminderRow CreateReminderRow(Reminder reminder)
        {
            ReminderRow row = new ReminderRow(reminder);

            Border container = new Border();
            container.Background = UiTheme.Frozen(0x1B, 0x1B, 0x21);
            container.BorderBrush = UiTheme.Frozen(0x32, 0x32, 0x3C);
            container.BorderThickness = new Thickness(1);
            container.CornerRadius = new CornerRadius(7);
            container.Padding = new Thickness(8, 6, 8, 6);
            container.Margin = new Thickness(0, 0, 0, 6);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            CheckBox enabled = new CheckBox();
            enabled.IsChecked = reminder.Enabled;
            enabled.VerticalAlignment = VerticalAlignment.Center;
            enabled.ToolTip = "启用或停用这条提醒";
            enabled.Margin = new Thickness(0, 0, 4, 0);
            grid.Children.Add(enabled);
            Grid.SetColumn(enabled, 0);

            TextBox timeBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(timeBox);
            timeBox.Text = reminder.Time;
            timeBox.MaxLength = 5;
            timeBox.Width = 56;
            timeBox.Height = 28;
            timeBox.Padding = new Thickness(5, 3, 5, 3);
            timeBox.FontFamily = new FontFamily("Consolas");
            timeBox.FontSize = 12.5;
            timeBox.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(timeBox);
            Grid.SetColumn(timeBox, 1);

            ComboBox modeCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(modeCombo);
            modeCombo.ItemsSource = _reminderModeOptions;
            modeCombo.DisplayMemberPath = "Label";
            modeCombo.SelectedValuePath = "Value";
            modeCombo.SelectedValue = ReminderModes.Normalize(reminder.Mode);
            modeCombo.Width = 98;
            modeCombo.Height = 28;
            modeCombo.FontSize = 11.5;
            modeCombo.Margin = new Thickness(6, 0, 7, 0);
            modeCombo.VerticalAlignment = VerticalAlignment.Center;
            modeCombo.ToolTip = "这条提醒只在所选状态下触发";
            grid.Children.Add(modeCombo);
            Grid.SetColumn(modeCombo, 2);

            TextBox messageBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(messageBox);
            messageBox.Text = reminder.Message;
            messageBox.MaxLength = 120;
            messageBox.Height = 28;
            messageBox.Padding = new Thickness(7, 3, 7, 3);
            messageBox.FontSize = 12.5;
            messageBox.Margin = new Thickness(0, 0, 8, 0);
            messageBox.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(messageBox);
            Grid.SetColumn(messageBox, 3);

            Button test = new Button();
            test.Content = "测试";
            test.Style = UiTheme.GhostButtonStyle;
            test.Padding = new Thickness(7, 4, 7, 4);
            test.FontSize = 11.5;
            test.Height = 28;
            test.VerticalAlignment = VerticalAlignment.Center;
            test.ToolTip = "在悬浮窗上预览这条提醒，不会触发真实提醒";
            test.Click += delegate
            {
                SyncReminderRows();
                string normalizedTime;
                if (!TimeFormatter.TryNormalizeTime(row.Reminder.Time, out normalizedTime))
                {
                    SetReminderStatus("这条提醒的时间格式不正确，请使用 HH:mm。", false);
                    row.TimeBox.Focus();
                    return;
                }
                string normalizedMessage = (row.Reminder.Message ?? string.Empty).Trim();
                if (normalizedMessage.Length == 0)
                {
                    SetReminderStatus("这条提醒没有填写内容。", false);
                    row.MessageBox.Focus();
                    return;
                }
                RaiseReminderTest(normalizedTime, normalizedMessage);
                SetReminderStatus("已发送测试提醒：" + normalizedTime + "  " + normalizedMessage, true);
            };

            Button delete = new Button();
            delete.Content = "删除";
            delete.Style = UiTheme.GhostButtonStyle;
            delete.Padding = new Thickness(7, 4, 7, 4);
            delete.FontSize = 11.5;
            delete.Height = 28;
            delete.VerticalAlignment = VerticalAlignment.Center;
            delete.ToolTip = "删除这条提醒";
            delete.Click += delegate
            {
                SyncReminderRows();
                _working.Reminders.Remove(reminder);
                RebuildReminderRows();
                SetReminderStatus("已删除一条提醒。", true);
                MarkDirty();
            };

            StackPanel rowActions = new StackPanel();
            rowActions.Orientation = Orientation.Horizontal;
            rowActions.VerticalAlignment = VerticalAlignment.Center;
            rowActions.Children.Add(test);
            rowActions.Children.Add(delete);
            grid.Children.Add(rowActions);
            Grid.SetColumn(rowActions, 4);

            container.Child = grid;
            row.Container = container;
            row.EnabledCheck = enabled;
            row.TimeBox = timeBox;
            row.ModeCombo = modeCombo;
            row.MessageBox = messageBox;
            row.DeleteButton = delete;
            row.OriginalTime = reminder.Time;
            row.OriginalMessage = reminder.Message;
            row.OriginalMode = ReminderModes.Normalize(reminder.Mode);
            return row;
        }

        private void SyncReminderRows()
        {
            for (int i = 0; i < _reminderRows.Count; i++)
            {
                ReminderRow row = _reminderRows[i];
                string newTime = (row.TimeBox.Text ?? string.Empty).Trim();
                string newMessage = (row.MessageBox.Text ?? string.Empty).Trim();
                ReminderModeOption selectedMode = row.ModeCombo.SelectedItem as ReminderModeOption;
                string newMode = selectedMode != null ? ReminderModes.Normalize(selectedMode.Value) : ReminderModes.Both;

                if (!string.Equals(row.OriginalTime, newTime, StringComparison.Ordinal)
                    || !string.Equals(row.OriginalMessage, newMessage, StringComparison.Ordinal)
                    || !string.Equals(row.OriginalMode, newMode, StringComparison.Ordinal))
                {
                    row.Reminder.LastTriggeredDate = "";
                    row.OriginalTime = newTime;
                    row.OriginalMessage = newMessage;
                    row.OriginalMode = newMode;
                }
                row.Reminder.Enabled = row.EnabledCheck.IsChecked == true;
                row.Reminder.Time = newTime;
                row.Reminder.Message = newMessage;
                row.Reminder.Mode = newMode;
            }
        }

        private bool CommitAll(out string error)
        {
            error = null;

            bool tokenMode = _tokenFormatRadio.IsChecked == true;
            string normalizedFormat;
            string formatError;
            bool formatValid = tokenMode
                ? FormatEngine.TryNormalizeTokenFormat(_formatBox.Text, true, out normalizedFormat, out formatError)
                : TimeFormatter.TryNormalizeFormat(_formatBox.Text, out normalizedFormat, out formatError);
            if (!formatValid)
            {
                _formatStatus.Text = formatError;
                _formatStatus.Foreground = UiTheme.Accent;
                _tabs.SelectedIndex = TabFormat;
                _formatStatus.BringIntoView();
                _formatBox.Focus();
                error = formatError;
                return false;
            }

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
                    _weatherFormatStatus.Foreground = UiTheme.Accent;
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

            TimeZoneChoice timeZoneChoice = _timeZoneCombo.SelectedItem as TimeZoneChoice;
            if (timeZoneChoice != null)
            {
                _working.TimeZoneId = timeZoneChoice.Id;
            }
            _formatStatus.Text = "输入后自动预览，无需回车；Enter 应用预览，Ctrl+Enter 插入一个换行。";
            _formatStatus.Foreground = UiTheme.TextMuted;

            FontChoice font = _fontCombo.SelectedItem as FontChoice;
            if (font != null)
            {
                _working.FontFamilyName = font.Name;
            }
            _working.FontSize = _sizeSlider.Value;
            _working.Opacity = _opacitySlider.Value / 100.0;
            _working.ShowShadow = _shadowCheck.IsChecked == true;
            _working.Text3D = _threeDCheck.IsChecked == true;
            _working.ClickThrough = _clickThroughCheck.IsChecked == true;
            _working.Topmost = _topmostCheck.IsChecked == true;
            _working.StartWithWindows = _startWithWindowsCheck.IsChecked == true;
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
            WeatherRefreshOption refreshOption = _weatherRefreshCombo.SelectedItem as WeatherRefreshOption;
            if (refreshOption != null)
            {
                _working.WeatherRefreshMinutes = refreshOption.Minutes;
            }

            SyncReminderRows();
            for (int i = 0; i < _reminderRows.Count; i++)
            {
                ReminderRow row = _reminderRows[i];
                row.TimeBox.BorderBrush = UiTheme.FieldBorder;
                row.MessageBox.BorderBrush = UiTheme.FieldBorder;

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

            _working.Normalize();
            return true;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            string error;
            if (!CommitAll(out error))
            {
                return;
            }

            if (ApplyRequested != null)
            {
                ApplyRequested(this, EventArgs.Empty);
            }
            _footerStatus.Text = "已应用，未关闭";
            _footerStatus.Foreground = UiTheme.Success;
        }

        private void OnSaveAndExit(object sender, RoutedEventArgs e)
        {
            string error;
            if (!CommitAll(out error))
            {
                return;
            }
            DialogResult = true;
        }

        private void OnAbout(object sender, RoutedEventArgs e)
        {
            AboutWindow about = new AboutWindow();
            about.Owner = this;
            about.ShowDialog();
        }

        private void OnResetDefaults(object sender, RoutedEventArgs e)
        {
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
                LoadControlsFromWorking();
            }
            finally
            {
                _updating = false;
            }
            _footerStatus.Text = "已恢复默认外观（提醒和状态保留）";
            _footerStatus.Foreground = UiTheme.TextMuted;
        }

        private void MarkDirty()
        {
            _footerStatus.Text = "有未应用的修改";
            _footerStatus.Foreground = UiTheme.TextMuted;
        }

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

        private sealed class ReminderRow
        {
            public ReminderRow(Reminder reminder)
            {
                Reminder = reminder;
            }

            public Reminder Reminder { get; private set; }
            public Border Container { get; set; }
            public CheckBox EnabledCheck { get; set; }
            public TextBox TimeBox { get; set; }
            public ComboBox ModeCombo { get; set; }
            public TextBox MessageBox { get; set; }
            public Button DeleteButton { get; set; }
            public string OriginalTime { get; set; }
            public string OriginalMessage { get; set; }
            public string OriginalMode { get; set; }
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

        private sealed class SimpleWin32Window : WinForms.IWin32Window
        {
            private readonly IntPtr _handle;

            public SimpleWin32Window(IntPtr handle)
            {
                _handle = handle;
            }

            public IntPtr Handle
            {
                get { return _handle; }
            }
        }
    }
}
