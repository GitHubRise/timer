using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Media;

// 设置模型与持久化：AppSettings 承载全部可持久化配置，SettingsStore 负责
// %APPDATA%\GameClock\settings.json 的原子读写。
// 边界：
// 1. 新增设置项必须同时改字段、SetDefaults、Normalize/Clone 与旧配置兼容路径；
// 2. 反序列化缺字段由 [OnDeserializing] 填默认值，脏值由 Normalize 收敛；
// 3. 读写失败一律回退默认或返回 false，绝不把异常抛给 UI 线程。
namespace GameClock
{
    /// <summary>
    /// 全部可持久化设置的内存模型（DataContract JSON）；设置窗口编辑的始终是
    /// 它 Clone 出来的副本，点“应用/保存”后才替换控制器持有的实例。
    /// </summary>
    [DataContract]
    internal sealed class AppSettings
    {
        // 字号/透明度硬上下界：滑块可拖范围与 Normalize 的收敛区间共用这几个常量，
        // 避免出现“界面拖得到、落盘却被裁剪”的两套边界。
        public const double MinFontSize = 16.0;
        public const double MaxFontSize = 160.0;
        public const double MinOpacity = 0.20;
        public const double MaxOpacity = 1.00;

        // 出厂默认值集中在此，SetDefaults 与兼容路径引用同一份来源，改名时只改一处。
        private const string DefaultColor = "#FF3B30";
        private const string DefaultFont = "Consolas";
        private const double DefaultFontSize = 52.0;
        private const double DefaultOpacity = 0.92;
        private const string DefaultTaskbarFont = "Microsoft YaHei UI";
        private const double DefaultTaskbarFontSize = 10.0;
        private const string DefaultTaskbarColor = "#000000";

        // —— 桌面悬浮窗外观（时间文字）——
        // 颜色与字体都存字符串而非 WPF 对象：JSON 需要跨版本稳定，Color/FontFamily 不可序列化。
        [DataMember(Name = "color")]
        public string ColorHex { get; set; }

        [DataMember(Name = "fontFamily")]
        public string FontFamilyName { get; set; }

        [DataMember(Name = "fontSize")]
        public double FontSize { get; set; }

        [DataMember(Name = "opacity")]
        public double Opacity { get; set; }

        [DataMember(Name = "showShadow")]
        public bool ShowShadow { get; set; }

        [DataMember(Name = "text3d")]
        public bool Text3D { get; set; }

        [DataMember(Name = "timeFormat")]
        public string TimeFormat { get; set; }

        [DataMember(Name = "formatMode")]
        public string FormatMode { get; set; }

        [DataMember(Name = "tokenTimeFormat")]
        public string TokenTimeFormat { get; set; }

        [DataMember(Name = "weatherFormat")]
        public string WeatherFormat { get; set; }

        [DataMember(Name = "timeZoneId")]
        public string TimeZoneId { get; set; }

        // —— 窗口行为与显示位置：两个显示位置各自独立，允许同时关闭 ——
        [DataMember(Name = "clickThrough")]
        public bool ClickThrough { get; set; }

        [DataMember(Name = "topmost")]
        public bool Topmost { get; set; }

        [DataMember(Name = "startWithWindows")]
        public bool StartWithWindows { get; set; }

        [DataMember(Name = "showDesktopOverlay")]
        public bool ShowDesktopOverlay { get; set; }

        [DataMember(Name = "showTaskbarClock")]
        public bool ShowTaskbarClock { get; set; }

        [DataMember(Name = "hideOnFullscreen")]
        public bool HideOnFullscreen { get; set; }

        // —— 任务栏时钟：外观与格式独立于悬浮窗，不复用上面的字体/格式字段 ——
        [DataMember(Name = "taskbarFontFamily")]
        public string TaskbarFontFamilyName { get; set; }

        [DataMember(Name = "taskbarFontSize")]
        public double TaskbarFontSize { get; set; }

        [DataMember(Name = "taskbarColor")]
        public string TaskbarColorHex { get; set; }

        [DataMember(Name = "taskbarFormat")]
        public string TaskbarFormat { get; set; }

        [DataMember(Name = "taskbarFormatMode")]
        public string TaskbarFormatMode { get; set; }

        [DataMember(Name = "taskbarDotNetFormat")]
        public string TaskbarDotNetFormat { get; set; }

        [DataMember(Name = "currentMode")]
        public string CurrentMode { get; set; }

        // —— 天气：城市/字体/刷新频率与提醒互不影响，可整体关闭 ——
        [DataMember(Name = "weatherEnabled")]
        public bool WeatherEnabled { get; set; }

        [DataMember(Name = "weatherCity")]
        public string WeatherCity { get; set; }

        [DataMember(Name = "weatherFontFamily")]
        public string WeatherFontFamilyName { get; set; }

        [DataMember(Name = "weatherFontSize")]
        public double WeatherFontSize { get; set; }

        [DataMember(Name = "weatherColor")]
        public string WeatherColorHex { get; set; }

        [DataMember(Name = "weatherShadow")]
        public bool WeatherShowShadow { get; set; }

        [DataMember(Name = "weatherRefreshMinutes")]
        public int WeatherRefreshMinutes { get; set; }

        [DataMember(Name = "weatherBold")]
        public bool WeatherFontBold { get; set; }

        [DataMember(Name = "weatherItalic")]
        public bool WeatherFontItalic { get; set; }

        // —— 提醒与日程：列表元素自带 Normalize，缺失或非法条目在收敛时被剔除 ——
        [DataMember(Name = "reminders")]
        public List<Reminder> Reminders { get; set; }

        [DataMember(Name = "schedules")]
        public List<ScheduleItem> Schedules { get; set; }

        [DataMember(Name = "reminderDisplaySeconds")]
        public int ReminderDisplaySeconds { get; set; }

        [DataMember(Name = "reminderHoverClose")]
        public bool ReminderHoverCloseEnabled { get; set; }

        [DataMember(Name = "reminderSoundEnabled")]
        public bool ReminderSoundEnabled { get; set; }

        [DataMember(Name = "reminderBlinkCount")]
        public int ReminderBlinkCount { get; set; }

        // —— 悬浮窗位置缓存：两者必须成对出现，任一为空都视为“未定位” ——
        [DataMember(Name = "left")]
        public double? Left { get; set; }

        [DataMember(Name = "top")]
        public double? Top { get; set; }

        /// <summary>新建一份出厂默认设置；设置窗口在没有现成配置时也走这里。</summary>
        public AppSettings()
        {
            SetDefaults();
        }

        /// <summary>
        /// 反序列化前置回调：DataContractJsonSerializer 只会覆盖 JSON 中出现的字段，
        /// 因此旧版本配置缺新字段时仍保留这里的默认值，不需要额外的版本迁移逻辑。
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        /// <summary>
        /// 写入全部默认值。新增设置项时必须同时在这里、Normalize 与 Clone 中登记，
        /// 否则旧配置加载后会出现 null/0 值，直接传给 WPF 控件就会抛异常。
        /// </summary>
        private void SetDefaults()
        {
            // 悬浮窗外观：颜色/字体/字号/透明度/阴影/3D。
            ColorHex = DefaultColor;
            FontFamilyName = DefaultFont;
            FontSize = DefaultFontSize;
            Opacity = DefaultOpacity;
            ShowShadow = true;
            Text3D = false;
            // 悬浮窗格式：默认用简洁变量模式，.NET 格式作为兼容保留。
            TimeFormat = TimeFormatter.DefaultFormat;
            FormatMode = FormatEngine.ModeToken;
            TokenTimeFormat = FormatEngine.DefaultTokenFormat;
            WeatherFormat = string.Empty;
            TimeZoneId = TimeZoneCatalog.DefaultId;
            // 窗口行为：默认不穿透、置顶、不随系统启动、显示桌面悬浮窗。
            ClickThrough = false;
            Topmost = true;
            StartWithWindows = false;
            ShowDesktopOverlay = true;
            ShowTaskbarClock = false;
            HideOnFullscreen = true;
            // 任务栏时钟外观与格式独立于悬浮窗，默认走两行简洁变量格式。
            TaskbarFontFamilyName = DefaultTaskbarFont;
            TaskbarFontSize = DefaultTaskbarFontSize;
            TaskbarColorHex = DefaultTaskbarColor;
            TaskbarFormat = FormatEngine.DefaultTaskbarTokenFormat;
            TaskbarFormatMode = FormatEngine.ModeToken;
            TaskbarDotNetFormat = TimeFormatter.DefaultFormat;
            // 提醒状态：默认娱乐状态，天气开启、默认城市北京。
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
            // 提醒与日程：提醒用出厂默认列表，日程为空；显示 12 秒、悬停可关、有铃声、闪 3 次。
            Reminders = ReminderDefaults.Create();
            Schedules = new List<ScheduleItem>();
            ReminderDisplaySeconds = 12;
            ReminderHoverCloseEnabled = true;
            ReminderSoundEnabled = true;
            ReminderBlinkCount = 3;
            // 位置为空表示“未定位”，由窗口自己决定初次出现的位置。
            Left = null;
            Top = null;
        }

        // 以下三个只读颜色视图把持久化的 Hex 字符串转成 WPF Color 供绘制使用；
        // 不单独持久化，避免字符串与 Color 两份数据出现不一致。

        /// <summary>悬浮窗时间文字颜色（由 ColorHex 解析，非法值回退默认红）。</summary>
        public Color TimeColor
        {
            get { return ColorUtil.FromHex(ColorHex); }
        }

        public Color WeatherColor
        {
            get { return ColorUtil.FromHex(WeatherColorHex); }
        }

        public Color TaskbarColor
        {
            get { return ColorUtil.FromHex(TaskbarColorHex); }
        }

        /// <summary>
        /// 深拷贝：值类型字段直接复制，提醒/日程列表逐项 Clone，
        /// 保证设置窗口取消编辑时不会污染正在运行的实例。
        /// </summary>
        /// <returns>与当前实例不共享可变状态的新设置对象。</returns>
        public AppSettings Clone()
        {
            AppSettings copy = (AppSettings)MemberwiseClone();
            // 列表字段必须先置空再逐项复制：MemberwiseClone 只做浅拷贝，
            // 两个实例会共享同一个 List，编辑副本里的增删会直接影响运行中的设置。
            copy.Reminders = new List<Reminder>();
            // 旧配置可能没有 reminders 键，列表为 null，这里按空列表处理。
            if (Reminders != null)
            {
                // 提醒条数是用户数据，逐条 Clone 保证取消编辑后原实例不被改。
                for (int i = 0; i < Reminders.Count; i++)
                {
                    copy.Reminders.Add(Reminders[i].Clone());
                }
            }
            copy.Schedules = new List<ScheduleItem>();
            // 日程列表同理：缺字段的旧配置同样可能为 null。
            if (Schedules != null)
            {
                // 日程同样逐条深拷贝，避免编辑副本与运行实例共享状态。
                for (int i = 0; i < Schedules.Count; i++)
                {
                    copy.Schedules.Add(Schedules[i].Clone());
                }
            }
            return copy;
        }

        /// <summary>
        /// 把任意来源（旧配置、手改 JSON、界面输入）的值收敛到可用范围。
        /// 幂等且不抛异常：重复调用结果不变，保证任何坏配置下应用仍能启动。
        /// </summary>
        public void Normalize()
        {
            // 颜色统一写回 #RRGGBB：ColorConverter 还接受 #RGB、颜色名等写法，
            // 直接回写原串会让下次解析结果不稳定。
            Color color = ColorUtil.FromHex(ColorHex);
            ColorHex = ColorUtil.ToHex(color);

            // 旧配置可能没有 fontFamily 键，null/空白都回退默认字体再去除首尾空格，
            // 否则 FontFamily 构造会因空串抛异常。
            if (string.IsNullOrWhiteSpace(FontFamilyName))
            {
                FontFamilyName = DefaultFont;
            }
            FontFamilyName = FontFamilyName.Trim();

            // 先处理 NaN/Infinity 再 Clamp：NaN 参与任何比较都返回 false，
            // 只靠 Clamp 会把非法值原样留下（数据绑定或滑块可能写入这种值）。
            if (double.IsNaN(FontSize) || double.IsInfinity(FontSize))
            {
                FontSize = DefaultFontSize;
            }
            FontSize = Clamp(FontSize, MinFontSize, MaxFontSize);

            // 透明度是 0~1 的小数，与字号同样的 NaN 防御顺序：先判非法，再夹到区间。
            if (double.IsNaN(Opacity) || double.IsInfinity(Opacity))
            {
                Opacity = DefaultOpacity;
            }
            Opacity = Clamp(Opacity, MinOpacity, MaxOpacity);

            // 格式类字段交给各自的引擎做容错收敛（非法格式回退默认格式），
            // 这里不重复实现格式语法判断，避免两处规则漂移。
            TimeFormat = TimeFormatter.NormalizeFormat(TimeFormat);
            FormatMode = FormatEngine.NormalizeMode(FormatMode);
            TokenTimeFormat = FormatEngine.NormalizeTokenFormat(TokenTimeFormat);
            WeatherFormat = FormatEngine.NormalizeWeatherFormat(WeatherFormat);
            TimeZoneId = TimeZoneCatalog.Normalize(TimeZoneId);
            CurrentMode = ReminderModes.Normalize(CurrentMode);

            // 城市名上限 40 与设置窗口 TextBox.MaxLength 一致；为空时回落到默认城市，
            // 否则天气请求会带着空地名发出，必然查不到。
            WeatherCity = (WeatherCity ?? string.Empty).Trim();
            // 空城市名无法查询天气，统一回退到默认城市而不是留空。
            if (WeatherCity.Length == 0)
            {
                WeatherCity = "北京";
            }
            // 超长城市名会被 API 拒绝，按界面允许的最大长度截断。
            if (WeatherCity.Length > 40)
            {
                WeatherCity = WeatherCity.Substring(0, 40);
            }

            // 天气字体为空时回退系统默认中文字体，保证中文城市名能正常显示。
            if (string.IsNullOrWhiteSpace(WeatherFontFamilyName))
            {
                WeatherFontFamilyName = "Microsoft YaHei UI";
            }
            WeatherFontFamilyName = WeatherFontFamilyName.Trim();

            // 天气字号同样先排 NaN 再夹到 9~60 px（与设置窗口滑块范围一致）。
            if (double.IsNaN(WeatherFontSize) || double.IsInfinity(WeatherFontSize))
            {
                WeatherFontSize = 15.0;
            }
            WeatherFontSize = Clamp(WeatherFontSize, 9.0, 60.0);

            // 天气颜色与刷新频率：颜色去 Alpha 写回，频率收敛到允许的选项集合。
            Color weatherColor = ColorUtil.FromHex(WeatherColorHex);
            WeatherColorHex = ColorUtil.ToHex(weatherColor);
            WeatherRefreshMinutes = WeatherRefreshOptions.Normalize(WeatherRefreshMinutes);

            // 任务栏字体为空时回退到任务栏默认字体（不是悬浮窗字体，两者独立）。
            if (string.IsNullOrWhiteSpace(TaskbarFontFamilyName))
            {
                TaskbarFontFamilyName = DefaultTaskbarFont;
            }
            TaskbarFontFamilyName = TaskbarFontFamilyName.Trim();

            // 任务栏字号上限 40 px 是因为任务栏高度有限，过大会被裁切。
            if (double.IsNaN(TaskbarFontSize) || double.IsInfinity(TaskbarFontSize))
            {
                TaskbarFontSize = DefaultTaskbarFontSize;
            }
            TaskbarFontSize = Clamp(TaskbarFontSize, 7.0, 40.0);

            // 任务栏颜色与格式：TaskbarFormat 永远存简洁变量、TaskbarDotNetFormat 永远存
            // .NET 格式，两个字段不互换，否则切换格式模式时会显示错语言。
            Color taskbarColor = ColorUtil.FromHex(TaskbarColorHex);
            TaskbarColorHex = ColorUtil.ToHex(taskbarColor);
            TaskbarFormat = FormatEngine.NormalizeTokenFormat(TaskbarFormat);
            TaskbarFormatMode = FormatEngine.NormalizeMode(TaskbarFormatMode);
            TaskbarDotNetFormat = TimeFormatter.NormalizeFormat(TaskbarDotNetFormat);

            // 旧配置没有 reminders 键时给空列表，避免后续遍历空引用。
            if (Reminders == null)
            {
                Reminders = new List<Reminder>();
            }
            // 倒序遍历以便安全删除；50/100 的上限只是防御异常配置把列表撑到卡住界面，
            // 正常使用远达不到。
            for (int i = Reminders.Count - 1; i >= 0; i--)
            {
                Reminder reminder = Reminders[i];
                // 空条目或 Normalize 返回 false（时间/内容非法）的提醒直接剔除，
                // 否则打开设置窗口时会显示一条无法触发、也无法删除的坏提醒。
                if (reminder == null || !reminder.Normalize())
                {
                    Reminders.RemoveAt(i);
                }
            }
            // 兜底上限：防止被手工编辑或异常同步撑到几千条后拖慢触发计算。
            while (Reminders.Count > 50)
            {
                Reminders.RemoveAt(Reminders.Count - 1);
            }

            // 日程列表与提醒同样的三步：补空列表 → 剔除非法项 → 截断到上限。
            if (Schedules == null)
            {
                Schedules = new List<ScheduleItem>();
            }
            // 倒序遍历删除，正序删会漏掉被前移的元素。
            for (int i = Schedules.Count - 1; i >= 0; i--)
            {
                ScheduleItem schedule = Schedules[i];
                // 日程的 Normalize 会校验日期/标题，非法项返回 false 即淘汰。
                if (schedule == null || !schedule.Normalize())
                {
                    Schedules.RemoveAt(i);
                }
            }
            // 日程上限 100 条：再多列表滚动和触发计算都会明显变慢。
            while (Schedules.Count > 100)
            {
                Schedules.RemoveAt(Schedules.Count - 1);
            }

            // 提醒显示时长走选项集合收敛，闪烁次数是 0~20 的整数区间。
            ReminderDisplaySeconds = ReminderDisplayOptions.Normalize(ReminderDisplaySeconds);
            // 负数闪烁次数会让动画无限循环，夹到 0（不闪）。
            if (ReminderBlinkCount < 0)
            {
                ReminderBlinkCount = 0;
            }
            // 上限定 20 次：再多的闪烁既刺眼又拖长提醒展示时间。
            if (ReminderBlinkCount > 20)
            {
                ReminderBlinkCount = 20;
            }

            // 位置只认成对值：历史版本出现过只存了 Left 的配置，这里统一按“未定位”处理，
            // 让窗口回到默认位置，而不是飞到屏幕外。
            if (Left.HasValue && (double.IsNaN(Left.Value) || double.IsInfinity(Left.Value)))
            {
                Left = null;
            }
            // Top 同样先排 NaN/Infinity，避免窗口被定位到不可见坐标。
            if (Top.HasValue && (double.IsNaN(Top.Value) || double.IsInfinity(Top.Value)))
            {
                Top = null;
            }
            // 任一为空就成对清空：单边坐标无法还原窗口位置，留着只会误导恢复逻辑。
            if (!Left.HasValue || !Top.HasValue)
            {
                Left = null;
                Top = null;
            }
        }

        /// <summary>工厂方法：首次启动与读配置失败都通过它拿到一份可用默认值。</summary>
        public static AppSettings CreateDefault()
        {
            return new AppSettings();
        }

        /// <summary>把值限制到 [min, max]；调用方需先排除 NaN/Infinity。</summary>
        private static double Clamp(double value, double min, double max)
        {
            // 低于下界取 min：字号/透明度小于最小值会让文字不可见或不可操作。
            if (value < min)
            {
                return min;
            }
            // 高于上界取 max：上限由界面滑块范围决定，超出后落盘值会与界面显示不一致。
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }

    /// <summary>
    /// 颜色字符串与 WPF Color 的互转工具（#RRGGBB 是唯一落盘格式）。
    /// 解析永不失败：非法输入回退品牌红，保证任何坏配置都还能显示出来。
    /// </summary>
    internal static class ColorUtil
    {
        /// <summary>解析失败时的兜底颜色，与 AppSettings.DefaultColor 保持一致。</summary>
        private static readonly Color Fallback = Color.FromRgb(255, 59, 48);

        /// <summary>
        /// 解析 #RRGGBB/#RGB/已知颜色名；结果统一去掉 Alpha，
        /// 因为窗口透明度由独立的 Opacity 设置控制，颜色里带透明会互相打架。
        /// </summary>
        public static Color FromHex(string hex)
        {
            // 空/空白串直接走兜底色，连转换器都不用调（转换器对空串会抛异常）。
            if (!string.IsNullOrWhiteSpace(hex))
            {
                // 用户手改 JSON 可能写出各种怪格式，转换器抛出的两类异常都在下面吞掉。
                try
                {
                    object value = ColorConverter.ConvertFromString(hex.Trim());
                    // 颜色名/十六进制都能转成 Color，其它类型（如画刷）直接丢弃走兜底。
                    if (value is Color)
                    {
                        // 重新拼一个无 Alpha 的颜色：颜色字符串里的透明度不参与窗口透明度计算。
                        Color color = (Color)value;
                        return Color.FromRgb(color.R, color.G, color.B);
                    }
                }
                catch (FormatException ex)
                {
                    // 手改 JSON 或手输颜色框写出非法格式：回退默认色，并把原始值与异常消息
                    // 一起记日志（Warn 没有异常重载，这里把 Message 拼进文本）。
                    AppLog.Warn("Settings", "颜色值无法解析，回退默认色：" + hex + "（" + ex.Message + "）");
                }
                catch (NotSupportedException ex)
                {
                    // 颜色名无法被当前转换器识别（区域/主题差异）时同样回退默认色并记录，
                    // 否则用户只会看到颜色莫名其妙变红，却查不到原因。
                    AppLog.Warn("Settings", "颜色值不受支持，回退默认色：" + hex + "（" + ex.Message + "）");
                }
            }
            return Fallback;
        }

        /// <summary>转为落盘格式 #RRGGBB（丢弃 Alpha）。</summary>
        public static string ToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        /// <summary>
        /// 相对亮度（0~1），按 sRGB 感知权重近似；用于决定文字阴影取黑还是取白
        /// （深色文字配白影），具体阈值由调用方决定。
        /// </summary>
        public static double Luminance(Color color)
        {
            return (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        }
    }

    /// <summary>
    /// settings.json 的唯一读写入口（其它模块不得自行拼路径或直接做文件 IO）。
    /// 关键约定：Load 永不抛异常（失败回退默认设置）；Save 先写临时文件再替换，
    /// 避免进程被杀时留下半截 JSON 把下次启动的配置弄坏。
    /// </summary>
    internal static class SettingsStore
    {
        /// <summary>配置目录 %APPDATA%\GameClock（AppLog 的日志也放在同一目录）。</summary>
        public static string FolderPath
        {
            get
            {
                // ApplicationData 是 Roaming 配置目录；不用 LocalApplicationData 是为了
                // 跟随域账号漫游，和旧版本保存位置保持一致，不能改。
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GameClock");
            }
        }

        /// <summary>配置文件全路径；日志与错误提示都用它定位问题。</summary>
        public static string FilePath
        {
            get { return Path.Combine(FolderPath, "settings.json"); }
        }

        /// <summary>
        /// 读取配置；文件缺失/为空/损坏/无权限时都回退出厂默认值并记日志，
        /// 保证调用方（Program 与控制器）不必处理异常。
        /// </summary>
        /// <returns>已 Normalize 的设置对象，永不为 null。</returns>
        public static AppSettings Load()
        {
            // 整个读取过程包在 try 里：任何一条失败路径都只回退默认设置并记日志，
            // 绝不把异常抛给启动流程（否则配置文件坏掉就会导致程序起不来）。
            try
            {
                if (!File.Exists(FilePath))
                {
                    // 首次启动的正常路径：记 INFO 便于确认配置目录位置，不当作错误打扰用户。
                    AppLog.Info("Store", "配置文件不存在，使用默认设置：" + FilePath);
                    return AppSettings.CreateDefault();
                }

                // 用 ReadAllBytes + MemoryStream 而不是直接 ReadObject(FileStream)：
                // 先整块读入可以避免反序列化中途失败时文件句柄残留，也便于判断空文件。
                byte[] data = File.ReadAllBytes(FilePath);
                // 长度为 0 时反序列化会直接抛异常，先单独判断以给出更准确的日志。
                if (data.Length == 0)
                {
                    // 空文件多由写入过程中断（断电/强杀）或安全软件截断造成，按损坏配置回退。
                    AppLog.Warn("Store", "配置文件为空，回退默认设置：" + FilePath);
                    return AppSettings.CreateDefault();
                }

                using (MemoryStream stream = new MemoryStream(data))
                {
                    // 反序列化后用 as 转换而非强转：类型不匹配时得到 null 而不是抛异常。
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    AppSettings settings = serializer.ReadObject(stream) as AppSettings;
                    // 字面量 null 会走到这里，同样按损坏配置回退。
                    if (settings == null)
                    {
                        // JSON 字面量为 null 时反序列化会返回 null；按损坏处理但单独记一条，
                        // 便于区分“文件写成 null”和“反序列化抛异常”两种不同的坏法。
                        AppLog.Warn("Store", "配置文件内容为空（null），回退默认设置：" + FilePath);
                        return AppSettings.CreateDefault();
                    }
                    settings.Normalize();
                    return settings;
                }
            }
            catch (IOException ex)
            {
                // 文件被占用/磁盘只读：回退默认设置继续启动，用户仍可在设置窗口里重存。
                AppLog.Error("Store", "读取配置失败，回退默认设置：" + FilePath, ex);
                return AppSettings.CreateDefault();
            }
            catch (UnauthorizedAccessException ex)
            {
                // 权限被组策略或安全软件拦截：同样只回退，不弹窗阻塞启动。
                AppLog.Error("Store", "读取配置无权限，回退默认设置：" + FilePath, ex);
                return AppSettings.CreateDefault();
            }
            catch (SerializationException ex)
            {
                // JSON 结构损坏（手工编辑、跨版本字段类型变化）：回退默认值保住启动。
                AppLog.Error("Store", "配置文件损坏（反序列化失败），回退默认设置：" + FilePath, ex);
                return AppSettings.CreateDefault();
            }
            catch (ArgumentException ex)
            {
                // 序列化目标类型不匹配时 ReadObject 会抛 ArgumentException，按损坏处理。
                AppLog.Error("Store", "配置文件无法被反序列化，回退默认设置：" + FilePath, ex);
                return AppSettings.CreateDefault();
            }
        }

        /// <summary>
        /// 原子保存配置：先写 settings.json.tmp 再替换正式文件。
        /// 失败返回 false 而不抛异常——保存失败属于可恢复情况，调用方继续运行即可。
        /// </summary>
        /// <param name="settings">待保存设置；为 null 时直接返回 false（编程错误，不写盘）。</param>
        /// <returns>true = 新配置已落盘。</returns>
        public static bool Save(AppSettings settings)
        {
            // 空对象直接拒绝：没有可写内容，写盘只会生成一份全 null 的配置。
            if (settings == null)
            {
                // 静默返回：控制器只在 _settings 为空时才会走到，属程序问题而非环境问题。
                return false;
            }

            // 落盘前再收敛一次：设置窗口会调用 Normalize，但其它调用方可能直接赋值。
            settings.Normalize();

            // 临时文件必须与正式文件同目录，File.Replace 才能保证是同一卷上的原子替换。
            string tempPath = FilePath + ".tmp";
            // 写入失败（磁盘满/被占用/无权限）统一转成 false + 日志，不向上抛。
            try
            {
                // 配置目录可能已被用户删除，每次保存前确保存在。
                Directory.CreateDirectory(FolderPath);
                // 先序列化到内存再一次性写盘：序列化中途抛异常时不会动到已有的正式文件。
                byte[] data;
                using (MemoryStream stream = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    serializer.WriteObject(stream, settings);
                    data = stream.ToArray();
                }

                // 写临时文件（覆盖旧 .tmp），此时正式配置仍然是完整的上一版。
                File.WriteAllBytes(tempPath, data);

                // 正式文件已存在时优先用 File.Replace 做原子替换，避免“删了旧的、新的没写上”。
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(tempPath, FilePath, null);
                    }
                    catch (Exception)
                    {
                        // 目标被占用时 File.Replace 会失败；退化为“删旧再改名”，
                        // 极端情况下瞬间丢旧配置，但保证新值能落盘。
                        File.Delete(FilePath);
                        File.Move(tempPath, FilePath);
                    }
                }
                else
                {
                    // 首次保存没有旧文件，直接改名即可（同一卷内改名是原子操作）。
                    File.Move(tempPath, FilePath);
                }
                return true;
            }
            catch (IOException ex)
            {
                // 磁盘满、文件被同步软件占用：返回 false 由上层决定是否提示，不抛给 UI 线程。
                AppLog.Error("Store", "保存配置失败（文件写入/替换失败）：" + FilePath, ex);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                // 目录只读或被安全软件保护：同样只记日志并返回 false。
                AppLog.Error("Store", "保存配置失败（无写入权限）：" + FilePath, ex);
                return false;
            }
            catch (SerializationException ex)
            {
                // 字段里出现不可序列化内容（理论上不会发生）：记日志，避免下次启动静默丢配置。
                AppLog.Error("Store", "保存配置失败（序列化异常）：" + FilePath, ex);
                return false;
            }
            finally
            {
                // 无论成功失败都尝试清理 .tmp：成功时它已被改名，通常不残留；
                // 失败时可能残留半截临时文件，清掉避免占空间也避免误读。
                try
                {
                    // 只有确实存在才删除，File.Delete 对不存在路径也会抛异常。
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (IOException)
                {
                    // 清理失败只会残留一个 .tmp，下次保存会用 WriteAllBytes 覆盖；
                    // 这里不记 Warn，避免保存失败时同一次操作出现两条错误。
                }
                catch (UnauthorizedAccessException)
                {
                    // 无权限删除临时文件：同上，仅残留文件，不影响已落盘的配置。
                }
            }
        }
    }
}
