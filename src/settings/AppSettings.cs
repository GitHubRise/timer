// 应用设置的配置模型、归一化与 JSON 持久化入口。
// 承载时间显示、字体与颜色、任务栏时钟、天气、提醒、日程与窗口位置等
// 全部可持久化字段，由 SettingsStore 统一读写 %APPDATA%\GameClock\settings.json。
// 约束：旧配置缺失字段由 SetDefaults 补齐；读取后必须调用 Normalize，
// 把数值、颜色、格式、时区与列表收敛到合法范围；Clone 供设置窗口建立编辑副本。
// 兼容说明：TaskbarFontFamilyName / TaskbarFontSize / TaskbarColorHex 仅为旧配置保留，
// 任务栏时钟外观跟随系统，这三个字段最多在读取系统字体失败时兜底。

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Media;

namespace GameClock
{

    /// <summary>
    /// 可持久化的应用设置模型。由 <see cref="SettingsStore"/> 从 JSON 载入，
    /// 设置窗口克隆一份编辑，点「应用」后再写回；所有公开字段在
    /// <see cref="Normalize"/> 中收敛到合法范围。
    /// </summary>
    [DataContract]
    internal sealed class AppSettings
    {
        // ===== 取值范围与默认值 =====

        /// <summary>时间字号下限（像素）；归一化会把更小的值抬到此值。</summary>
        public const double MinFontSize = 16.0;

        /// <summary>时间字号上限（像素）；滚轮缩放与设置页都以此为界。</summary>
        public const double MaxFontSize = 160.0;

        /// <summary>透明度下限；再低文字几乎不可见，因此不允许。</summary>
        public const double MinOpacity = 0.20;

        /// <summary>透明度上限；1.0 表示完全不透明。</summary>
        public const double MaxOpacity = 1.00;

        /// <summary>时间文字默认颜色（iOS 红）。</summary>
        private const string DefaultColor = "#FF3B30";

        /// <summary>时间文字默认字体。</summary>
        private const string DefaultFont = "Consolas";

        /// <summary>时间文字默认字号（像素）。</summary>
        private const double DefaultFontSize = 52.0;

        /// <summary>时间文字默认透明度。</summary>
        private const double DefaultOpacity = 0.92;

        /// <summary>【兼容字段】任务栏默认字体，仅作系统字体读取失败时的兜底。</summary>
        private const string DefaultTaskbarFont = "Microsoft YaHei UI";

        /// <summary>【兼容字段】任务栏默认字号（像素），仅作兜底。</summary>
        private const double DefaultTaskbarFontSize = 10.0;

        /// <summary>【兼容字段】任务栏默认文字颜色，仅作兜底。</summary>
        private const string DefaultTaskbarColor = "#000000";

        /// <summary>任务栏字号缩放的默认百分比，100 表示与系统原生字号一致。</summary>
        private const int DefaultTaskbarFontScale = 100;

        // ===== 字体与颜色 =====

        /// <summary>时间文字颜色，<c>#RRGGBB</c>；默认 <c>#FF3B30</c>，非法值在 <see cref="ColorUtil.FromHex"/> 中回落为默认红，保存前统一转成大写十六进制。</summary>
        [DataMember(Name = "color")]
        public string ColorHex { get; set; }

        /// <summary>时间文字字体名；默认 Consolas，空白值回落到默认字体，归一化时去除首尾空格。</summary>
        [DataMember(Name = "fontFamily")]
        public string FontFamilyName { get; set; }

        /// <summary>时间字号（像素）；默认 52，取值范围 <see cref="MinFontSize"/>–<see cref="MaxFontSize"/>，NaN/无穷先回落默认值再 Clamp。</summary>
        [DataMember(Name = "fontSize")]
        public double FontSize { get; set; }

        /// <summary>时间文字透明度；默认 0.92，取值范围 <see cref="MinOpacity"/>–<see cref="MaxOpacity"/>，NaN/无穷先回落默认值再 Clamp。</summary>
        [DataMember(Name = "opacity")]
        public double Opacity { get; set; }

        /// <summary>是否给时间文字加黑色阴影；默认 true，与 3D 立体字可同时开启。</summary>
        [DataMember(Name = "showShadow")]
        public bool ShowShadow { get; set; }

        /// <summary>是否绘制 3D 立体字；默认 false，与阴影设置相互独立。</summary>
        [DataMember(Name = "text3d")]
        public bool Text3D { get; set; }

        // ===== 时间格式与时区 =====

        /// <summary>时间文字的 .NET 日期格式；默认 <see cref="TimeFormatter.DefaultFormat"/>，空串或非法格式由 NormalizeFormat 归一到默认。</summary>
        [DataMember(Name = "timeFormat")]
        public string TimeFormat { get; set; }

        /// <summary>悬浮窗时间格式模式（.NET 兼容或简洁变量）；默认 <see cref="FormatEngine.ModeToken"/>，由 FormatEngine.NormalizeMode 收敛。</summary>
        [DataMember(Name = "formatMode")]
        public string FormatMode { get; set; }

        /// <summary>悬浮窗简洁变量格式串；默认 <see cref="FormatEngine.DefaultTokenFormat"/>，由 NormalizeTokenFormat 归一并限制长度。</summary>
        [DataMember(Name = "tokenTimeFormat")]
        public string TokenTimeFormat { get; set; }

        /// <summary>天气行格式；默认空串表示使用内置默认格式（城市 · 天气 · 温度 · 今日高低温）。</summary>
        [DataMember(Name = "weatherFormat")]
        public string WeatherFormat { get; set; }

        /// <summary>显示时区 ID；默认 <see cref="TimeZoneCatalog.DefaultId"/>，空或未知 ID 由 TimeZoneCatalog.Normalize 回落到默认时区。</summary>
        [DataMember(Name = "timeZoneId")]
        public string TimeZoneId { get; set; }

        // ===== 显示与窗口 =====

        /// <summary>是否开启鼠标穿透；默认 false，开启后只能从系统托盘菜单恢复交互。</summary>
        [DataMember(Name = "clickThrough")]
        public bool ClickThrough { get; set; }

        /// <summary>悬浮窗是否始终置顶；默认 true。</summary>
        [DataMember(Name = "topmost")]
        public bool Topmost { get; set; }

        /// <summary>是否随当前用户登录自动启动；默认 false，只写 HKCU 启动项，不需要管理员权限。</summary>
        [DataMember(Name = "startWithWindows")]
        public bool StartWithWindows { get; set; }

        /// <summary>是否显示桌面悬浮窗；默认 true，与任务栏时钟开关相互独立，两者可同时关闭。</summary>
        [DataMember(Name = "showDesktopOverlay")]
        public bool ShowDesktopOverlay { get; set; }

        /// <summary>是否显示任务栏时钟覆盖窗；默认 true，只覆盖不移动系统原生时钟。</summary>
        [DataMember(Name = "showTaskbarClock")]
        public bool ShowTaskbarClock { get; set; }

        /// <summary>检测到全屏应用时是否暂时隐藏悬浮窗；默认 true，只隐藏窗口，不停止时钟与提醒计时。</summary>
        [DataMember(Name = "hideOnFullscreen")]
        public bool HideOnFullscreen { get; set; }

        // ===== 任务栏时钟 =====

        /// <summary>【兼容字段】任务栏字体名；默认 Microsoft YaHei UI。外观跟随系统，仅在读取系统字体失败时兜底。</summary>
        [DataMember(Name = "taskbarFontFamily")]
        public string TaskbarFontFamilyName { get; set; }

        /// <summary>【兼容字段】任务栏字号（像素）；默认 10.0，范围 7–40，仅作兜底。</summary>
        [DataMember(Name = "taskbarFontSize")]
        public double TaskbarFontSize { get; set; }

        /// <summary>任务栏字号相对系统原生字号的百分比；默认 100，范围 100–160，小于等于 0 视为未配置并回落 100。</summary>
        [DataMember(Name = "taskbarFontScale")]
        public int TaskbarFontScale { get; set; }

        /// <summary>任务栏时钟是否加粗；默认 false，字重越大按宽度收紧得越多。</summary>
        [DataMember(Name = "taskbarBold")]
        public bool TaskbarBold { get; set; }

        /// <summary>【兼容字段】任务栏文字颜色；默认 <c>#000000</c>。正常按任务栏背景亮度自动选择，仅作兜底。</summary>
        [DataMember(Name = "taskbarColor")]
        public string TaskbarColorHex { get; set; }

        /// <summary>任务栏简洁变量格式；默认 <see cref="FormatEngine.DefaultTaskbarTokenFormat"/>，始终保存变量格式，切换模式也不改写。</summary>
        [DataMember(Name = "taskbarFormat")]
        public string TaskbarFormat { get; set; }

        /// <summary>任务栏格式模式；默认 <see cref="FormatEngine.ModeToken"/>，与悬浮窗的格式模式分开保存。</summary>
        [DataMember(Name = "taskbarFormatMode")]
        public string TaskbarFormatMode { get; set; }

        /// <summary>任务栏 .NET 日期格式；默认 <see cref="TimeFormatter.DefaultFormat"/>，与变量格式各存一份。</summary>
        [DataMember(Name = "taskbarDotNetFormat")]
        public string TaskbarDotNetFormat { get; set; }

        // ===== 提醒状态 =====

        /// <summary>当前工作/娱乐状态，决定哪些提醒会被触发；默认 <see cref="ReminderModes.Leisure"/>，非法值由 ReminderModes.Normalize 收敛。</summary>
        [DataMember(Name = "currentMode")]
        public string CurrentMode { get; set; }

        // ===== 天气 =====

        /// <summary>是否显示天气预报行；默认 true，查询失败时界面保留占位而不报错。</summary>
        [DataMember(Name = "weatherEnabled")]
        public bool WeatherEnabled { get; set; }

        /// <summary>查询天气的城市名；默认「北京」，归一化时去空格、空串回落北京、超长截断到 40 字。</summary>
        [DataMember(Name = "weatherCity")]
        public string WeatherCity { get; set; }

        /// <summary>天气文字字体；默认 Microsoft YaHei UI，空白值回落默认字体并去首尾空格。</summary>
        [DataMember(Name = "weatherFontFamily")]
        public string WeatherFontFamilyName { get; set; }

        /// <summary>天气字号（像素）；默认 15，范围 9–60，NaN/无穷先回落默认值再 Clamp。</summary>
        [DataMember(Name = "weatherFontSize")]
        public double WeatherFontSize { get; set; }

        /// <summary>天气文字颜色；默认 <c>#FF3B30</c>，非法值回落默认红，保存前统一转大写十六进制。</summary>
        [DataMember(Name = "weatherColor")]
        public string WeatherColorHex { get; set; }

        /// <summary>天气文字是否加阴影；默认 true，与时间文字的阴影设置相互独立。</summary>
        [DataMember(Name = "weatherShadow")]
        public bool WeatherShowShadow { get; set; }

        /// <summary>天气自动刷新间隔（分钟）；默认 30，由 WeatherRefreshOptions.Normalize 收敛到可选档位。</summary>
        [DataMember(Name = "weatherRefreshMinutes")]
        public int WeatherRefreshMinutes { get; set; }

        /// <summary>天气文字是否加粗；默认 false。</summary>
        [DataMember(Name = "weatherBold")]
        public bool WeatherFontBold { get; set; }

        /// <summary>天气文字是否使用斜体；默认 false。</summary>
        [DataMember(Name = "weatherItalic")]
        public bool WeatherFontItalic { get; set; }

        // ===== 提醒 =====

        /// <summary>定时提醒列表；默认由 ReminderDefaults.Create() 生成，归一化时剔除 null/无效项并最多保留 50 条。</summary>
        [DataMember(Name = "reminders")]
        public List<Reminder> Reminders { get; set; }

        /// <summary>日程列表；默认空列表，归一化时剔除 null/无效项并最多保留 100 条。</summary>
        [DataMember(Name = "schedules")]
        public List<ScheduleItem> Schedules { get; set; }

        /// <summary>提醒气泡停留秒数；默认 12，由 ReminderDisplayOptions.Normalize 收敛到可选档位。</summary>
        [DataMember(Name = "reminderDisplaySeconds")]
        public int ReminderDisplaySeconds { get; set; }

        /// <summary>鼠标在提醒卡片上悬停 3 秒是否自动关闭；默认 true，穿透状态下同样有效。</summary>
        [DataMember(Name = "reminderHoverClose")]
        public bool ReminderHoverCloseEnabled { get; set; }

        /// <summary>提醒出现时是否播放提示音；默认 true，关闭后完全静音。</summary>
        [DataMember(Name = "reminderSoundEnabled")]
        public bool ReminderSoundEnabled { get; set; }

        /// <summary>提醒文字闪烁次数；默认 3，范围 0–20，0 表示不闪烁。</summary>
        [DataMember(Name = "reminderBlinkCount")]
        public int ReminderBlinkCount { get; set; }

        // ===== 窗口位置 =====

        /// <summary>悬浮窗上次保存的左边缘（DIP）；默认 null 表示由系统居中定位。NaN/无穷或与 Top 缺一不可时整对置空。</summary>
        [DataMember(Name = "left")]
        public double? Left { get; set; }

        /// <summary>悬浮窗上次保存的上边缘（DIP）；默认 null。位置必须成对保存，缺任一边都会整对清空。</summary>
        [DataMember(Name = "top")]
        public double? Top { get; set; }

        // ===== 持久化 =====

        /// <summary>创建实例并立即填充默认值，保证不经过反序列化时字段也有合法初值。</summary>
        public AppSettings()
        {
            SetDefaults();
        }

        /// <summary>反序列化开始前先填默认值，使旧 JSON 中缺失的字段保留默认，而不是变成 0 或 null。</summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        /// <summary>
        /// 把所有字段重置为出厂默认值；构造函数与 <see cref="OnDeserializing"/> 共用。
        /// 这里必须覆盖每个持久化字段，否则旧配置缺字段时会读到 0 或 null。
        /// </summary>
        private void SetDefaults()
        {
            // 字体与颜色：默认红色 Consolas 52 px、0.92 透明度，阴影开、3D 关。
            ColorHex = DefaultColor;
            FontFamilyName = DefaultFont;
            FontSize = DefaultFontSize;
            Opacity = DefaultOpacity;
            ShowShadow = true;
            Text3D = false;

            // 格式与时区：默认简洁变量格式，时区取系统默认，天气格式留空表示内置样式。
            TimeFormat = TimeFormatter.DefaultFormat;
            FormatMode = FormatEngine.ModeToken;
            TokenTimeFormat = FormatEngine.DefaultTokenFormat;
            WeatherFormat = string.Empty;
            TimeZoneId = TimeZoneCatalog.DefaultId;

            // 显示与窗口：默认不穿透、置顶、显示桌面悬浮窗与任务栏时钟、全屏隐藏，不随机启动。
            ClickThrough = false;
            Topmost = true;
            StartWithWindows = false;
            ShowDesktopOverlay = true;
            ShowTaskbarClock = true;
            HideOnFullscreen = true;

            // 任务栏时钟：默认跟随系统字号（100%）、不加粗，兼容字段写入兜底值。
            TaskbarFontFamilyName = DefaultTaskbarFont;
            TaskbarFontSize = DefaultTaskbarFontSize;
            TaskbarFontScale = DefaultTaskbarFontScale;
            TaskbarBold = false;
            TaskbarColorHex = DefaultTaskbarColor;
            TaskbarFormat = FormatEngine.DefaultTaskbarTokenFormat;
            TaskbarFormatMode = FormatEngine.ModeToken;
            TaskbarDotNetFormat = TimeFormatter.DefaultFormat;

            // 提醒状态与天气：默认娱乐状态、开启天气，城市北京、字号 15、每 30 分钟刷新。
            CurrentMode = ReminderModes.Leisure;
            WeatherEnabled = true;
            WeatherCity = "北京";
            WeatherFontFamilyName = "Microsoft YaHei UI";
            WeatherFontSize = 15.0;
            WeatherColorHex = "#FF3B30";
            WeatherShowShadow = true;
            WeatherRefreshMinutes = 30;
            WeatherFontBold = false;
            WeatherFontItalic = false;

            // 提醒与日程：内置几条默认提醒，日程初始为空；气泡停留 12 秒、悬停关闭、有铃声、闪 3 次。
            Reminders = ReminderDefaults.Create();
            Schedules = new List<ScheduleItem>();
            ReminderDisplaySeconds = 12;
            ReminderHoverCloseEnabled = true;
            ReminderSoundEnabled = true;
            ReminderBlinkCount = 3;

            // 窗口位置默认不指定，交给系统居中；保存过一次后才会有值。
            Left = null;
            Top = null;
        }

        /// <summary>时间文字颜色，由 <see cref="ColorHex"/> 解析；颜色串非法时返回默认红。</summary>
        public Color TimeColor
        {
            get { return ColorUtil.FromHex(ColorHex); }
        }

        /// <summary>天气文字颜色，由 <see cref="WeatherColorHex"/> 解析；颜色串非法时返回默认红。</summary>
        public Color WeatherColor
        {
            get { return ColorUtil.FromHex(WeatherColorHex); }
        }

        /// <summary>任务栏文字颜色，由 <see cref="TaskbarColorHex"/> 解析；仅作系统字体/颜色读取失败时的兜底。</summary>
        public Color TaskbarColor
        {
            get { return ColorUtil.FromHex(TaskbarColorHex); }
        }

        /// <summary>
        /// 深拷贝一份设置供设置窗口编辑：字符串与值类型直接复制，
        /// 提醒与日程列表逐项 Clone，避免编辑过程中污染正在运行的设置。
        /// </summary>
        public AppSettings Clone()
        {
            // MemberwiseClone 已复制字符串与值类型，只需重建两个引用类型列表。
            AppSettings copy = (AppSettings)MemberwiseClone();
            copy.Reminders = new List<Reminder>();
            // 列表可能为 null（旧配置或手工构造），逐项拷贝前先判空。
            if (Reminders != null)
            {
                for (int i = 0; i < Reminders.Count; i++)
                {
                    copy.Reminders.Add(Reminders[i].Clone());
                }
            }
            copy.Schedules = new List<ScheduleItem>();
            // 日程同样可能为空，空列表不是错误，直接跳过拷贝。
            if (Schedules != null)
            {
                for (int i = 0; i < Schedules.Count; i++)
                {
                    copy.Schedules.Add(Schedules[i].Clone());
                }
            }
            return copy;
        }

        /// <summary>
        /// 把所有字段收敛到合法范围：颜色转标准十六进制、字体去空格、数值 Clamp、
        /// 格式/时区/模式交给各自的 Normalize，提醒与日程剔除无效项并限制条数。
        /// 反序列化后与保存前都会调用，因此必须容忍 null 与明显越界的值。
        /// </summary>
        public void Normalize()
        {
            // 颜色先解析再格式化：解析失败会得到默认红，同时把 #RGB、具名颜色等写法统一成 #RRGGBB。
            Color color = ColorUtil.FromHex(ColorHex);
            ColorHex = ColorUtil.ToHex(color);

            // 字体名允许为空，但不能是空白：回落到 Consolas 后统一去首尾空格，避免匹配不到字体。
            if (string.IsNullOrWhiteSpace(FontFamilyName))
            {
                FontFamilyName = DefaultFont;
            }
            FontFamilyName = FontFamilyName.Trim();

            // 先处理 NaN/无穷（JSON 里可能出现 0/0 之类），否则 Clamp 的比较全部为 false，脏值会直接传给 WPF。
            if (double.IsNaN(FontSize) || double.IsInfinity(FontSize))
            {
                FontSize = DefaultFontSize;
            }
            FontSize = Clamp(FontSize, MinFontSize, MaxFontSize);

            // 透明度同理先兜底再夹取，保证 0.20–1.00 之外的旧值不会让窗口消失。
            if (double.IsNaN(Opacity) || double.IsInfinity(Opacity))
            {
                Opacity = DefaultOpacity;
            }
            Opacity = Clamp(Opacity, MinOpacity, MaxOpacity);

            // 格式串、模式、时区各自有专门的归一化函数，这里只做转交，保证与运行时解析逻辑一致。
            TimeFormat = TimeFormatter.NormalizeFormat(TimeFormat);
            FormatMode = FormatEngine.NormalizeMode(FormatMode);
            TokenTimeFormat = FormatEngine.NormalizeTokenFormat(TokenTimeFormat);
            WeatherFormat = FormatEngine.NormalizeWeatherFormat(WeatherFormat);
            TimeZoneId = TimeZoneCatalog.Normalize(TimeZoneId);
            CurrentMode = ReminderModes.Normalize(CurrentMode);

            // 城市名去掉首尾空格；空串回落北京，超长截断，避免请求串被手工输入撑爆。
            WeatherCity = (WeatherCity ?? string.Empty).Trim();
            if (WeatherCity.Length == 0)
            {
                WeatherCity = "北京";
            }
            // 超长城市名无法被接口正确解析，按 40 字截断。
            if (WeatherCity.Length > 40)
            {
                WeatherCity = WeatherCity.Substring(0, 40);
            }

            // 天气字体与字号：与时间文字一样，空白回落默认、NaN/无穷兜底、字号夹在 9–60。
            if (string.IsNullOrWhiteSpace(WeatherFontFamilyName))
            {
                WeatherFontFamilyName = "Microsoft YaHei UI";
            }
            WeatherFontFamilyName = WeatherFontFamilyName.Trim();

            if (double.IsNaN(WeatherFontSize) || double.IsInfinity(WeatherFontSize))
            {
                WeatherFontSize = 15.0;
            }
            WeatherFontSize = Clamp(WeatherFontSize, 9.0, 60.0);

            // 天气颜色走同一套互转；刷新间隔收敛到界面提供的可选档位，避免出现无法选中的 7 分钟。
            Color weatherColor = ColorUtil.FromHex(WeatherColorHex);
            WeatherColorHex = ColorUtil.ToHex(weatherColor);
            WeatherRefreshMinutes = WeatherRefreshOptions.Normalize(WeatherRefreshMinutes);

            // 任务栏兼容字段也要归一：否则旧配置里的空字体或越界字号被兜底路径取用时仍会出错。
            if (string.IsNullOrWhiteSpace(TaskbarFontFamilyName))
            {
                TaskbarFontFamilyName = DefaultTaskbarFont;
            }
            TaskbarFontFamilyName = TaskbarFontFamilyName.Trim();

            if (double.IsNaN(TaskbarFontSize) || double.IsInfinity(TaskbarFontSize))
            {
                TaskbarFontSize = DefaultTaskbarFontSize;
            }
            TaskbarFontSize = Clamp(TaskbarFontSize, 7.0, 40.0);

            // 缩放比例先处理「未配置」（小于等于 0），再按 100–160 夹取，100 表示与系统原生字号一致。
            if (TaskbarFontScale <= 0)
            {
                TaskbarFontScale = DefaultTaskbarFontScale;
            }
            // 低于 100% 会小于系统原生字号，不符合“跟随系统”的设计，抬回 100。
            if (TaskbarFontScale < 100)
            {
                TaskbarFontScale = 100;
            }
            // 上限 160%，再大任务栏一行放不下，只能靠收紧字距。
            if (TaskbarFontScale > 160)
            {
                TaskbarFontScale = 160;
            }

            // 任务栏颜色与两套格式串分别归一，任务栏格式模式独立于悬浮窗。
            Color taskbarColor = ColorUtil.FromHex(TaskbarColorHex);
            TaskbarColorHex = ColorUtil.ToHex(taskbarColor);
            TaskbarFormat = FormatEngine.NormalizeTokenFormat(TaskbarFormat);
            TaskbarFormatMode = FormatEngine.NormalizeMode(TaskbarFormatMode);
            TaskbarDotNetFormat = TimeFormatter.NormalizeFormat(TaskbarDotNetFormat);

            // 提醒列表：旧配置可能为 null，先补空列表；再从后往前删，避免删除时下标错位。
            if (Reminders == null)
            {
                Reminders = new List<Reminder>();
            }
            for (int i = Reminders.Count - 1; i >= 0; i--)
            {
                Reminder reminder = Reminders[i];
                // Normalize 返回 false 表示这条提醒已无从修复（时间非法、内容为空等），直接丢弃。
                if (reminder == null || !reminder.Normalize())
                {
                    Reminders.RemoveAt(i);
                }
            }
            // 条数上限防止界面与气泡被海量提醒拖慢，超出部分丢弃末尾（较早添加的优先保留）。
            while (Reminders.Count > 50)
            {
                Reminders.RemoveAt(Reminders.Count - 1);
            }

            // 日程列表处理方式与提醒一致：补空列表、从后往前剔除无效项、限制 100 条。
            if (Schedules == null)
            {
                Schedules = new List<ScheduleItem>();
            }
            for (int i = Schedules.Count - 1; i >= 0; i--)
            {
                ScheduleItem schedule = Schedules[i];
                if (schedule == null || !schedule.Normalize())
                {
                    Schedules.RemoveAt(i);
                }
            }
            while (Schedules.Count > 100)
            {
                Schedules.RemoveAt(Schedules.Count - 1);
            }

            // 提醒显示时长收敛到下拉框档位；闪烁次数限制在 0–20，0 表示不闪。
            ReminderDisplaySeconds = ReminderDisplayOptions.Normalize(ReminderDisplaySeconds);
            // 负数没有意义，按不闪烁处理。
            if (ReminderBlinkCount < 0)
            {
                ReminderBlinkCount = 0;
            }
            // 闪烁上限 20 次，再多会显得刺眼。
            if (ReminderBlinkCount > 20)
            {
                ReminderBlinkCount = 20;
            }

            // 位置只接受有限数值；NaN/无穷无法用于布局，直接清空。
            if (Left.HasValue && (double.IsNaN(Left.Value) || double.IsInfinity(Left.Value)))
            {
                Left = null;
            }
            if (Top.HasValue && (double.IsNaN(Top.Value) || double.IsInfinity(Top.Value)))
            {
                Top = null;
            }
            // 位置必须成对存在：只有一边时无法定位，整对清空比猜测另一边更安全。
            if (!Left.HasValue || !Top.HasValue)
            {
                Left = null;
                Top = null;
            }
        }

        /// <summary>创建一份全部为默认值的设置；首次启动或读取配置文件失败时使用。</summary>
        public static AppSettings CreateDefault()
        {
            return new AppSettings();
        }

        /// <summary>把数值夹到 [min, max] 区间；端点本身视为合法值。</summary>
        private static double Clamp(double value, double min, double max)
        {
            // 低于下限时按下限处理，保证字号、透明度不会小到不可用。
            if (value < min)
            {
                return min;
            }
            // 高于上限时按上限处理，避免超出界面与布局的承受范围。
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }

    /// <summary>
    /// 颜色与十六进制字符串的互转工具。解析失败时统一回落到默认红，
    /// 让配置里写错的颜色不会把异常抛到 UI 线程。
    /// </summary>
    internal static class ColorUtil
    {

        /// <summary>解析失败时的兜底颜色（<c>#FF3B30</c>）。</summary>
        private static readonly Color Fallback = Color.FromRgb(255, 59, 48);

        /// <summary>
        /// 解析颜色字符串并丢弃 alpha：支持 <c>#RGB</c>、<c>#ARGB</c>、<c>#RRGGBB</c> 与具名颜色。
        /// 空串、格式非法或颜色名不受支持时返回 <see cref="Fallback"/>，绝不抛出。
        /// </summary>
        public static Color FromHex(string hex)
        {
            // 空串直接回落，省掉一次必然失败的转换。
            if (!string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    // WPF 的转换器接受多种写法，先交给它解析，再统一取 RGB。
                    object value = ColorConverter.ConvertFromString(hex.Trim());
                    if (value is Color)
                    {
                        Color color = (Color)value;
                        // 配置只保存不透明颜色，这里主动丢弃 alpha，避免半透明配置与置顶窗口叠加出意外效果。
                        return Color.FromRgb(color.R, color.G, color.B);
                    }
                }
                catch (FormatException)
                {
                    // 字符串不是合法颜色格式：落到函数末尾返回默认红，配置错误不该阻断启动。
                }
                catch (NotSupportedException)
                {
                    // 颜色名不受当前转换器支持：同样回落默认红，交由用户重新选择。
                }
            }
            return Fallback;
        }

        /// <summary>把颜色格式化为 <c>#RRGGBB</c>（大写，忽略 alpha）。</summary>
        public static string ToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        /// <summary>按 ITU-R BT.709 权重计算相对亮度（0–1），用于按背景选择文字颜色。</summary>
        public static double Luminance(Color color)
        {
            return (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        }
    }

    /// <summary>
    /// 设置的磁盘存储：固定读写 <c>%APPDATA%\GameClock\settings.json</c>。
    /// 读取失败一律回落到默认设置；保存先写临时文件再替换，尽量不写坏原文件。
    /// </summary>
    internal static class SettingsStore
    {

        /// <summary>设置目录：当前用户漫游 AppData 下的 GameClock，与程序安装位置无关。</summary>
        public static string FolderPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GameClock");
            }
        }

        /// <summary>设置文件完整路径。</summary>
        public static string FilePath
        {
            get { return Path.Combine(FolderPath, "settings.json"); }
        }

        /// <summary>
        /// 读取并反序列化设置；文件不存在、内容为空、JSON 损坏或无权限时都返回默认设置，
        /// 调用方无需处理异常。成功读入后会先 <see cref="AppSettings.Normalize"/> 再返回。
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                // 首次启动没有配置文件属于正常情况，直接给默认值。
                if (!File.Exists(FilePath))
                {
                    return AppSettings.CreateDefault();
                }

                byte[] data = File.ReadAllBytes(FilePath);
                // 空文件按缺失处理，避免反序列化抛异常。
                if (data.Length == 0)
                {
                    return AppSettings.CreateDefault();
                }

                // 用内存流读取，避免长时间占用配置文件句柄。
                using (MemoryStream stream = new MemoryStream(data))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    AppSettings settings = serializer.ReadObject(stream) as AppSettings;
                    // JSON 末尾可能是 null，统一回落到默认设置。
                    if (settings == null)
                    {
                        return AppSettings.CreateDefault();
                    }
                    // 读入后立刻归一化，兼容旧版本或手工改坏的配置。
                    settings.Normalize();
                    return settings;
                }
            }
            catch (IOException)
            {
                // 文件被占用或磁盘错误：用默认设置继续，界面不应因此打不开。
                return AppSettings.CreateDefault();
            }
            catch (UnauthorizedAccessException)
            {
                // 无读权限时同样降级为默认设置，用户仍可保存到可写位置后重试。
                return AppSettings.CreateDefault();
            }
            catch (SerializationException)
            {
                // JSON 结构损坏或字段类型不匹配：宁可回到默认值，也不让程序启动失败。
                return AppSettings.CreateDefault();
            }
            catch (ArgumentException)
            {
                // 序列化器对异常输入还可能抛参数错误，一并按损坏配置处理。
                return AppSettings.CreateDefault();
            }
        }

        /// <summary>
        /// 归一化后序列化到临时文件，再原子替换正式文件；任何失败返回 false，由调用方提示，
        /// 不抛异常。<see cref="SettingsStore.Load"/> 与本方法都依赖同一份默认值逻辑。
        /// </summary>
        public static bool Save(AppSettings settings)
        {
            // 没有设置对象可写，直接报告失败。
            if (settings == null)
            {
                return false;
            }

            // 保存前归一化，保证落盘值与运行时实际使用的值一致。
            settings.Normalize();
            string tempPath = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(FolderPath);
                byte[] data;
                // 先序列化到内存，确认成功后再落盘，减少写到一半的可能。
                using (MemoryStream stream = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    serializer.WriteObject(stream, settings);
                    data = stream.ToArray();
                }

                File.WriteAllBytes(tempPath, data);

                if (File.Exists(FilePath))
                {
                    try
                    {
                        // 优先用 File.Replace 原子替换，读到的永远是完整文件。
                        File.Replace(tempPath, FilePath, null);
                    }
                    catch (Exception)
                    {
                        // 部分文件系统或权限组合不支持 Replace，退回「删除旧文件 + 移动临时文件」。
                        File.Delete(FilePath);
                        File.Move(tempPath, FilePath);
                    }
                }
                else
                {
                    // 首次保存：临时文件直接改名成正式文件。
                    File.Move(tempPath, FilePath);
                }
                return true;
            }
            catch (IOException)
            {
                // 磁盘满、文件被占用等：返回失败让调用方提示，不抛出到 UI 线程。
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // 目录只读或无写权限：同样返回失败，配置仍保存在内存中。
                return false;
            }
            catch (SerializationException)
            {
                // 设置对象里出现无法序列化的值：不写盘，避免产生半截配置。
                return false;
            }
            finally
            {
                // 无论成功与否都清理残留临时文件；Replace 成功后文件已不存在，删除前必须判存在。
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (IOException)
                {
                    // 清理失败不影响保存结果，残留的 .tmp 会在下次保存时被覆盖。
                }
                catch (UnauthorizedAccessException)
                {
                    // 临时文件删不掉也不该把成功的保存报告成失败，静默忽略。
                }
            }
        }
    }
}
