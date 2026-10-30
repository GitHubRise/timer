// 格式引擎：把用户填写的「简洁变量格式」解析成 token 序列，再逐项渲染成文字。
// 变量语法（大小写敏感）：ASCII 字母组成的变量名，如 yyyy / MM / ff / rtm / we1；
//   普通文字必须用英文双引号括起（"第"），\n 表示换行、\\ 表示反斜杠本身。
// 边界：纯函数，非法输入返回空字符串或默认格式，不抛异常；Format() 每秒被时钟调用，
//   内部（包括解析失败）一律不写日志，格式校验的日志只在上层「编辑/应用格式」路径触发。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameClock
{
    /// <summary>
    /// 简洁变量格式的 token 类型；枚举顺序参与区间判断（天气 token 用 WeatherDay1..High6 的连续区间），
    /// 新增 token 时不要插进已有区间中间，否则 IsWeatherToken 的范围判断会跟着改变。
    /// </summary>
    internal enum FormatToken
    {
        // —— 基础：字面量片段（引号内文字 / 数字 / 标点等不参与变量解析的内容）——
        Literal = 0,            // 原样输出的文本片段，内容存在 FormatPart.Literal

        // —— 年：公历数字年与中文数字年、生肖、干支 ——
        Year2,                  // yy：两位公历年（2026 → 26）
        Year4,                  // yyyy：四位公历年
        ChineseYear2,           // YY：中文数字的两位年（二六）
        ChineseYear4,           // YYYY：中文数字的完整年（二〇二六）
        Zodiac,                 // nn：生肖（鼠/牛/…），按农历年取
        GanzhiZodiac,           // NN：干支 + 生肖（丙午马）

        // —— 月与日：小写 m/d 是公历，大写 M/D 是农历 ——
        Month,                  // m：公历月，不补零
        MonthPadded,            // mm：公历月，补零
        LunarMonth,             // M：农历月中文名（正月/闰四月…）
        EnglishMonthShort,      // EM3：英文月缩写（Oct）
        EnglishMonthLong,       // EM：英文月全称（October）
        Day,                    // d：公历日，不补零
        DayPadded,              // dd：公历日，补零
        LunarDay,               // D：农历日中文（初五/廿三）

        // —— 时：12/24 小时制各两种补零方式 ——
        Hour12,                 // h：12 小时制，不补零（0 点显示 12）
        Hour12Padded,           // hh：12 小时制补零
        Hour24,                 // H：24 小时制，不补零
        Hour24Padded,           // HH：24 小时制补零

        // —— 分/秒/毫秒：本方言 f 是分钟、s 是秒，与 .NET 的 f=小数秒不同 ——
        Minute,                 // f：分钟，不补零
        MinutePadded,           // ff：分钟补零
        Second,                 // s：秒，不补零
        SecondPadded,           // ss：秒补零
        Millisecond,            // SSS：毫秒，固定三位

        // —— 星期/周数：中文与英文各长短两种，另加 ISO 周与年内天数 ——
        WeekdayShort,           // ddd：周一
        WeekdayLong,            // dddd：星期一
        EnglishWeekdayShort,    // EW3：Mon
        EnglishWeekdayLong,     // EW：Monday
        IsoWeek,                // week：第 39 周
        DayOfYear,              // day：第 303 天

        // —— 上下午 ——
        AmPmEnglish,            // tt：AM/PM
        AmPmChinese,            // TT：上午/下午

        // —— 六天预报（区间必须连续，IsWeatherToken 依赖区间判断）——
        WeatherDay1,            // we1：今天天气现象
        WeatherDay2,
        WeatherDay3,
        WeatherDay4,
        WeatherDay5,
        WeatherDay6,            // we6：第 6 天天气现象
        Low1,                   // tl1：今天最低温
        Low2,
        Low3,
        Low4,
        Low5,
        Low6,
        High1,                  // th1：今天最高温
        High2,
        High3,
        High4,
        High5,
        High6,

        // —— 实时天气与定位/网络字段 ——
        Temperature,            // rtm：当前温度（°C）
        Humidity,               // rhu：湿度（湿度45%）
        WindDirection,          // rwd：风向（东北风）
        WeatherIcon,            // icn：天气图标字符
        AirQuality,             // aqi：空气质量（优 49）
        PublicIp,               // ip：公网 IP
        City                    // pos：城市名
    }

    /// <summary>
    /// 解析后的单个片段：要么是字面量文本（Token=Literal 时读 Literal），要么是一个变量 token。
    /// 用字段而非属性，因为解析每秒可能产出多条，避免属性访问开销。
    /// </summary>
    internal sealed class FormatPart
    {
        public FormatToken Token;
        public string Literal;
    }

    /// <summary>
    /// 一次解析的完整结果：片段列表 + 是否含天气变量。
    /// HasWeatherToken 在构造时算好，供上层判断是否需要等天气数据（无天气 token 时不请求天气）。
    /// </summary>
    internal sealed class ParsedFormat
    {
        public readonly List<FormatPart> Parts;
        public bool HasWeatherToken;

        // 构造即完成天气标记扫描：解析时才知道是否含天气变量，之后每次渲染只读这个布尔值。
        public ParsedFormat(List<FormatPart> parts)
        {
            Parts = parts;
            // 任一天气 token 命中就置位并提前结束；格式片段通常只有个位数，无需建表。
            for (int i = 0; i < parts.Count; i++)
            {
                if (FormatEngine.IsWeatherToken(parts[i].Token))
                {
                    HasWeatherToken = true;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 简洁变量格式的解析与渲染入口；解析结果按格式串缓存，渲染只做字符串拼接。
    /// 主要变量分组（完整映射见 BuildTokenTable）：
    ///   日期：yy yyyy YY YYYY nn NN m mm M d dd D ddd dddd EM3 EM EW3 EW week day；
    ///   时间：h hh H HH f ff s ss SSS tt TT；
    ///   天气：we1..we6 tl1..tl6 th1..th6 rtm rhu rwd icn aqi pos ip。
    /// 注意：本格式语言与 .NET 格式串语义不同——m 是公历月、f 是分钟、M/D 是农历月/日；
    ///   三连写（MMM、fff、sss…）会命中 CheckRunTrap 给出针对性错误，而不是静默当成字面量。
    /// </summary>
    internal static class FormatEngine
    {
        public const string ModeDotNet = "dotnet";
        public const string ModeToken = "token";
        public const string DefaultTokenFormat = "HH:ff:ss";

        // 任务栏默认两行：日期行 + 农历/星期/实时温度行；这里的 \n 是真实换行符（不是字面量反斜杠 n）。
        public const string DefaultTaskbarTokenFormat = "yyyy-mm-dd HH:ff:ss\nMD ddd rtm";

        // 上限：单行 120 字符、最多两行；再长会超出悬浮窗可用宽度被裁掉。
        public const int MaxFormatLength = 120;
        public const int MaxLines = 2;

        // 渲染固定文化：中文星期/农历固定 zh-CN，英文月份/星期固定不变文化，避免跟随系统区域漂移。
        private static readonly CultureInfo ChineseCulture = CultureInfo.GetCultureInfo("zh-CN");
        private static readonly CultureInfo EnglishCulture = CultureInfo.InvariantCulture;
        private static readonly Dictionary<string, FormatToken> Tokens = BuildTokenTable();

        // 解析结果缓存：格式串基本不变，缓存后每秒渲染只做一次字典查找；
        // 用 Ordinal 比较，因为变量名大小写敏感（M 与 m 是不同 token）。
        private static readonly Dictionary<string, ParsedFormat> Cache =
            new Dictionary<string, ParsedFormat>(StringComparer.Ordinal);
        private static readonly object CacheLock = new object();

        /// <summary>判断格式模式是否为简洁变量（兼容大小写，历史配置里出现过 Token/TOKEN）。</summary>
        public static bool IsTokenMode(string mode)
        {
            return string.Equals(mode, ModeToken, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把任意大小的模式串归一化为 <see cref="ModeDotNet"/> 或 <see cref="ModeToken"/>，未知值按 .NET 处理。</summary>
        public static string NormalizeMode(string mode)
        {
            return IsTokenMode(mode) ? ModeToken : ModeDotNet;
        }

        /// <summary>
        /// 校验并归一化简洁变量格式：统一换行符、把字面量 \n 转成真实换行、按行去首尾空格、限制行数与长度，
        /// 最后走一次完整解析确认没有非法变量。
        /// </summary>
        /// <param name="input">用户输入的原始格式串。</param>
        /// <param name="allowNewline">true 为悬浮窗/任务栏格式（最多两行）；false 为天气格式（只允许一行）。</param>
        /// <param name="normalized">成功时输出归一化后的格式串，失败时为 null。</param>
        /// <param name="error">失败时输出可直接展示给用户的中文原因。</param>
        public static bool TryNormalizeTokenFormat(string input, bool allowNewline, out string normalized, out string error)
        {
            normalized = null;
            error = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "时间格式不能为空。";
                return false;
            }

            string format = input.Replace("\r\n", "\n").Replace('\r', '\n');
            format = format.Replace("\\n", "\n");

            // 拆行前必须先统一换行符：Windows 剪贴板常带 \r\n，用户也可能直接敲字面量 \n。
            string[] lines = format.Split('\n');
            if (lines.Length > MaxLines)
            {
                error = allowNewline
                    ? "最多只支持一个换行（两行文字）。"
                    : "天气格式只能是一行文字。";
                return false;
            }
            // 天气行贴在时间行下面，多行会把悬浮窗撑高并破坏两处显示面的排版，所以硬性限制单行。
            if (!allowNewline && lines.Length > 1)
            {
                error = "天气格式只能是一行文字。";
                return false;
            }

            // 逐行去首尾空格再判空：两端空格用户看不见，却会让「换行两侧都需要填写内容」误判或留下空行。
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Trim();
                if (lines[i].Length == 0)
                {
                    error = allowNewline ? "换行两侧都需要填写内容。" : "天气格式不能为空。";
                    return false;
                }
            }

            // 用 \n 重新拼回单串后再判长度，保证长度口径与最终落盘/缓存的字符串一致。
            format = string.Join("\n", lines);
            if (format.Length > MaxFormatLength)
            {
                error = "格式太长，请控制在 " + MaxFormatLength + " 个字符以内。";
                return false;
            }

            // 只有「用户编辑/应用格式」会走到这里：时钟渲染走 Format → TryParse，不经过本方法，
            // 所以这里可以安全记一条 Warn；设置页仍会把 error 原文显示给用户。
            ParsedFormat parsed;
            if (!TryParse(format, out parsed, out error))
            {
                AppLog.Warn(
                    "Format",
                    "简洁变量格式校验失败（" + (allowNewline ? "多行" : "单行") + "）：" + error
                        + "；原始输入：" + format);
                return false;
            }

            normalized = format;
            return true;
        }

        /// <summary>
        /// 载入/应用配置时的宽松归一化：非法输入回退 <see cref="DefaultTokenFormat"/>，保证时钟总有可显示格式。
        /// 只在配置载入与应用路径调用（非每秒渲染），回退时记一条 Warn 便于定位坏配置。
        /// </summary>
        public static string NormalizeTokenFormat(string input)
        {
            string normalized;
            string error;
            if (TryNormalizeTokenFormat(input, true, out normalized, out error))
            {
                return normalized;
            }
            // 配置里的格式串坏了：回退默认值维持显示，同时把原始值写进日志，避免用户只看到「格式变回去了」。
            AppLog.Warn(
                "Format",
                "简洁变量格式非法，已回退默认格式 " + DefaultTokenFormat + "；原始输入："
                    + (input == null ? "(null)" : input));
            return DefaultTokenFormat;
        }

        /// <summary>
        /// 天气格式归一化：非法输入回退空串（等价于不额外显示天气），正常输入必须是单行。
        /// 天气格式只影响天气那一行的排版，坏了不应影响时间显示，因此这里静默降级为空。
        /// </summary>
        public static string NormalizeWeatherFormat(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }
            string normalized;
            string error;
            if (TryNormalizeTokenFormat(input, false, out normalized, out error))
            {
                return normalized;
            }
            // 天气格式非法：回退为「不显示天气行」，避免整块格式解析失败连带时钟变成空串。
            AppLog.Warn(
                "Format",
                "天气格式非法，已回退为空（本次不显示天气行）；原始输入：" + input);
            return string.Empty;
        }

        /// <summary>
        /// 解析简洁变量格式并写入缓存；空格式视为合法的空结构，其余非法输入返回 false 与中文原因。
        /// 本方法每秒都会被 <see cref="Format"/> 调用，因此**不在任何失败分支写日志**，日志由上层编辑/应用路径负责。
        /// </summary>
        public static bool TryParse(string format, out ParsedFormat parsed, out string error)
        {
            parsed = null;
            error = null;

            // 空格式刻意当作「合法但无内容」：设置页把格式清空时界面应显示空白而不是报错。
            if (string.IsNullOrEmpty(format))
            {
                parsed = new ParsedFormat(new List<FormatPart>());
                return true;
            }

            // 命中缓存直接返回，这是每秒渲染的热路径；锁只包住字典读写，解析在锁外进行。
            lock (CacheLock)
            {
                if (Cache.TryGetValue(format, out parsed))
                {
                    return true;
                }
            }

            List<FormatPart> parts;
            if (!ParseSlow(format, out parts, out error))
            {
                parsed = null;
                return false;
            }

            // 先构造好再入缓存：ParsedFormat 构造时预计算 HasWeatherToken，重复计算没有必要。
            parsed = new ParsedFormat(parts);
            // 缓存上限 256 条：正常用户最多十几个格式，超过说明在频繁试错，整表清空比 LRU 更简单可靠。
            lock (CacheLock)
            {
                if (Cache.Count > 256)
                {
                    Cache.Clear();
                }
                Cache[format] = parsed;
            }
            return true;
        }

        /// <summary>
        /// 按已解析结构渲染一次显示文本；解析失败返回空串（由调用方决定是否再回退默认格式）。
        /// </summary>
        /// <remarks>
        /// 本方法被悬浮窗与任务栏时钟每秒调用一次，**禁止在此记录日志**，否则 app.log 会被秒级刷屏。
        /// </remarks>
        public static string Format(DateTime time, WeatherInfo weather, string format)
        {
            ParsedFormat parsed;
            string error;
            // 解析失败不在这里回退：回退策略由调用方（ClockWindow / TaskbarClockWindow）决定，避免两层各退一次。
            if (!TryParse(format, out parsed, out error) || parsed == null)
            {
                return string.Empty;
            }

            // 预估容量：格式长度 + 16 覆盖「MMM→中文三月」「we1→晴」这类字面量比变量名长的情况。
            StringBuilder builder = new StringBuilder(format.Length + 16);
            // 顺序遍历解析片段：字面量原样追加，变量 token 交给 AppendToken 求值。
            for (int i = 0; i < parsed.Parts.Count; i++)
            {
                FormatPart part = parsed.Parts[i];
                if (part.Token == FormatToken.Literal)
                {
                    builder.Append(part.Literal);
                    continue;
                }
                AppendToken(builder, part.Token, time, weather);
            }
            // 天气为 null 时天气 token 会渲染成空串，可能留下整行空行，最后统一收掉。
            return CollapseEmptyLines(builder.ToString());
        }

        /// <summary>
        /// 去掉落空的行：天气数据未就绪时天气变量渲染为空，会留下只有换行的空行，导致时钟两行只剩一行。
        /// </summary>
        private static string CollapseEmptyLines(string text)
        {
            // 单行文本是绝大多数情况，快速返回避免无谓的 Split/Join 分配。
            if (text.IndexOf('\n') < 0)
            {
                return text;
            }

            string[] lines = text.Split('\n');
            List<string> kept = new List<string>(lines.Length);
            // 按「Trim 后是否为空」判断：只含空格的行同样视为空行，一起丢掉。
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0)
                {
                    kept.Add(lines[i]);
                }
            }
            return string.Join("\n", kept.ToArray());
        }

        /// <summary>
        /// 判断格式是否含任意天气变量；调用方据此决定是否启用天气行与是否触发天气拉取。
        /// 解析失败按「不含天气」处理，宁可少显示一次天气，也不让坏格式拖垮时间显示。
        /// </summary>
        public static bool ContainsWeatherToken(string format)
        {
            ParsedFormat parsed;
            string error;
            // 直接复用缓存里的解析结果：上层每次应用格式都会问一次，重复解析没有意义。
            return TryParse(format, out parsed, out error) && parsed != null && parsed.HasWeatherToken;
        }

        /// <summary>
        /// 格式是否引用空气质量；为 true 时天气请求才会附带 AQI 查询（多一次 HTTP 请求）。
        /// </summary>
        public static bool RequiresAirQuality(string format)
        {
            // 只是查 token 表，不发请求；真正决定是否访问空气质量接口的是调用方。
            return ContainsToken(format, FormatToken.AirQuality);
        }

        /// <summary>
        /// 格式是否引用公网 IP；为 true 时天气请求才会访问 ipify（多一次 HTTP 请求）。
        /// </summary>
        public static bool RequiresPublicIp(string format)
        {
            // 同上：格式里没有 ip 变量时不值得为一次 ipify 请求拖慢天气刷新。
            return ContainsToken(format, FormatToken.PublicIp);
        }

        /// <summary>线性查找解析结果里是否含指定 token；解析失败返回 false。</summary>
        private static bool ContainsToken(string format, FormatToken token)
        {
            ParsedFormat parsed;
            string error;
            if (!TryParse(format, out parsed, out error) || parsed == null)
            {
                return false;
            }
            // 片段数量通常是个位数，线性扫描比建 HashSet 更划算。
            for (int i = 0; i < parsed.Parts.Count; i++)
            {
                if (parsed.Parts[i].Token == token)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 判断 token 是否依赖天气数据：天气 token 渲染为空时上层会跳过整行，避免出现「湿度」这类半截文本。
        /// </summary>
        public static bool IsWeatherToken(FormatToken token)
        {
            // 六天预报的 18 个 token 在枚举里连续排列（WeatherDay1..High6），用区间判断省去逐个比较。
            // City/PublicIp 不依赖 Open-Meteo 天气，但同样来自天气服务，算作天气 token 才能共享空行收尾逻辑。
            return (token >= FormatToken.WeatherDay1 && token <= FormatToken.High6)
                || token == FormatToken.Temperature
                || token == FormatToken.Humidity
                || token == FormatToken.WindDirection
                || token == FormatToken.WeatherIcon
                || token == FormatToken.AirQuality
                || token == FormatToken.PublicIp
                || token == FormatToken.City;
        }

        /// <summary>
        /// 生成简洁变量模式的「常用格式」预设列表；示例预览用固定样例时间与假天气渲染，与实际时钟无关。
        /// </summary>
        public static List<TimeFormatPreset> CreatePresets(DateTime sample)
        {
            List<TimeFormatPreset> list = new List<TimeFormatPreset>();
            // 顺序即设置页下拉框顺序，把最常用的默认格式放最前，减少用户翻找。
            // 单行基础组合：时间、12 小时制、毫秒、日期。
            Add(list, "时间（默认）", "HH:ff:ss", sample);
            Add(list, "时间（12 小时 + 上下午）", "TT hh:ff:ss", sample);
            Add(list, "时间 + 毫秒", "HH:ff:ss.SSS", sample);
            Add(list, "仅日期", "yyyy-mm-dd", sample);

            // 日期 + 时间 + 星期/农历的两行组合，覆盖任务栏默认样式及其变体。
            Add(list, "日期 + 时间", "yyyy-mm-dd HH:ff:ss", sample);
            Add(list, "日期 + 时间 + 星期", "yyyy-mm-dd HH:ff:ss dddd", sample);
            Add(list, "中文日期 + 时间", "yyyy年mm月dd日 HH:ff:ss", sample);
            Add(list, "日期 + 时间 + 农历 + 星期", "yyyy-mm-dd HH:ff:ss\nMD ddd", sample);
            Add(list, "日期 + 时间 + 农历 + 星期 + 温度", "yyyy-mm-dd HH:ff:ss\nMD ddd rtm", sample);
            Add(list, "日期 + 时分 + 生肖 + 农历 + 星期", "yyyy-mm-dd HH:ff\nnn MD ddd", sample);
            Add(list, "短日期 + 时分 + 农历 + 星期", "mm-dd HH:ff\nMD ddd", sample);
            Add(list, "短日期 + 农历 + 星期 + 时间", "mm-dd MD ddd HH:ff", sample);
            Add(list, "农历 + 星期 + 短日期 + 时间", "MD ddd mm-dd HH:ff", sample);
            Add(list, "中文日期 + 数字年 + 农历 + 星期", "yyyy年mm月dd日 HH:ff\nYYYY年MD ddd", sample);
            Add(list, "中文日期 + 生肖 + 农历 + 星期", "yyyy年mm月dd日 HH:ff\nnn YYYY年MD ddd", sample);
            Add(list, "斜杠日期 + 农历 + 星期 + 中文上下午", "yyyy/mm/dd MD ddd\nTT HH:ff:ss", sample);
            Add(list, "斜杠日期 + 农历 + 星期 + 英文上下午", "yyyy/mm/dd MD ddd\ntt HH:ff:ss", sample);
            Add(list, "时间在上 / 日期在下", "HH:ff:ss\nyyyy/mm/dd", sample);
            Add(list, "两行：中文日期 / 时间", "yyyy年mm月dd日\nHH:ff:ss", sample);
            Add(list, "干支 + 农历 + 星期", "NN MD ddd", sample);
            Add(list, "日期 + 农历 + 年内进度", "yyyy年mm月dd日 NN MD\nday week 今天 HH:ff:ss", sample);
            Add(list, "英文日期 + 星期", "EW, EM d, yyyy", sample);
            return list;
        }

        /// <summary>追加一个预设；换行在预览里显示成「 / 」，否则会被列表项截断成一行。</summary>
        private static void Add(List<TimeFormatPreset> list, string name, string format, DateTime sample)
        {
            // 预览用假天气渲染一次：渲染失败会得到空串，条目仍显示名称与格式，不影响选择。
            string example = Format(sample, PresetWeather, format).Replace("\n", " / ");
            list.Add(new TimeFormatPreset(name, format, example));
        }

        // 预设预览共用的样例天气：固定值保证预览不随真实天气变化，也不是用于展示的实时数据。
        private static readonly WeatherInfo PresetWeather = CreatePresetWeather();

        /// <summary>构造预设预览用的假天气（北京、晴、20°C、六天预报），仅服务于设置页预览。</summary>
        private static WeatherInfo CreatePresetWeather()
        {
            // 样例值写死在这里而不是取当前天气：保证设置页每次打开的预览都一致、可对比。
            WeatherInfo weather = new WeatherInfo();
            weather.City = "北京";
            weather.Condition = "晴";
            weather.WeatherCode = 0;
            weather.Temperature = 20;
            weather.Min = 12;
            weather.Max = 24;
            weather.Humidity = 45;
            weather.WindDirection = "北风";
            weather.AqiValue = 49;
            weather.AqiLevel = "优";
            weather.PublicIp = "221.222.190.200";

            // 六天预报：天气码对应的中文条件与最低/最高温度，让 we/tl/th 系列预览都有内容可显示。
            string[] conditions = new string[] { "晴", "多云", "阴", "小雨", "晴", "晴" };
            double[] lows = new double[] { 12, 10, 11, 9, 10, 13 };
            double[] highs = new double[] { 24, 25, 23, 20, 22, 25 };
            for (int i = 0; i < conditions.Length; i++)
            {
                DailyForecast day = new DailyForecast();
                day.Condition = conditions[i];
                day.Min = lows[i];
                day.Max = highs[i];
                weather.Days.Add(day);
            }
            return weather;
        }

        /// <summary>
        /// 把一个变量 token 的渲染结果追加到输出；天气类 token 在数据缺失时追加空串（由上层收掉空行）。
        /// 所有数值一律用不变文化格式化，避免跟随系统区域出现阿拉伯数字/本地数字混排。
        /// </summary>
        private static void AppendToken(StringBuilder builder, FormatToken token, DateTime time, WeatherInfo weather)
        {
            // 大 switch 按「年→月→日→时→分秒→星期→上下午→天气」排列，与 BuildTokenTable 的注册顺序对应。
            switch (token)
            {
                // 年份组：yy/yyyy 是公历数字年，YY/YYYY 是中文数字年，nn/NN 是生肖与干支-生肖。
                case FormatToken.Year2:
                    builder.Append((time.Year % 100).ToString("00", CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Year4:
                    builder.Append(time.Year.ToString("0000", CultureInfo.InvariantCulture));
                    break;
                // 中文年：把公历年份逐位转成「〇一二…」，不是干支纪年（干支见 NN）。
                case FormatToken.ChineseYear2:
                    builder.Append(ChineseCalendarInfo.YearDigits(time.Year % 100));
                    break;
                case FormatToken.ChineseYear4:
                    builder.Append(ChineseCalendarInfo.YearDigits(time.Year));
                    break;
                case FormatToken.Zodiac:
                    builder.Append(ChineseCalendarInfo.ZodiacName(time));
                    break;
                case FormatToken.GanzhiZodiac:
                    builder.Append(ChineseCalendarInfo.GanzhiZodiacName(time));
                    break;
                // 月份组：m 是公历月数字、mm 补零、M 是农历月（与 .NET 的 M=月 语义相反，别混用）。
                case FormatToken.Month:
                    builder.Append(time.Month.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.MonthPadded:
                    builder.Append(time.Month.ToString("00", CultureInfo.InvariantCulture));
                    break;
                // 农历月与英文月：英文月固定不变文化，保证系统语言切换后预览文字不变。
                case FormatToken.LunarMonth:
                    builder.Append(ChineseCalendarInfo.LunarMonthName(time));
                    break;
                case FormatToken.EnglishMonthShort:
                    builder.Append(time.ToString("MMM", EnglishCulture));
                    break;
                case FormatToken.EnglishMonthLong:
                    builder.Append(time.ToString("MMMM", EnglishCulture));
                    break;
                // 日期组：d 是公历日数字、dd 补零、D 是农历日（初一/廿三这类中文写法）。
                case FormatToken.Day:
                    builder.Append(time.Day.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.DayPadded:
                    builder.Append(time.Day.ToString("00", CultureInfo.InvariantCulture));
                    break;
                case FormatToken.LunarDay:
                    builder.Append(ChineseCalendarInfo.LunarDayName(time));
                    break;
                // 12 小时制：调用 Hour12 做 0→12 的换算，0 点显示为 12 而不是 0。
                case FormatToken.Hour12:
                    builder.Append(Hour12(time).ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Hour12Padded:
                    builder.Append(Hour12(time).ToString("00", CultureInfo.InvariantCulture));
                    break;
                // 24 小时制：H 不补零、HH 补零，时钟默认格式 HH:ff:ss 用的就是这两个。
                case FormatToken.Hour24:
                    builder.Append(time.Hour.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Hour24Padded:
                    builder.Append(time.Hour.ToString("00", CultureInfo.InvariantCulture));
                    break;
                // 分与秒：注意本方言里 f 是「分钟」、s/ss 是「秒」，与 .NET 的 f=小数秒不同。
                case FormatToken.Minute:
                    builder.Append(time.Minute.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.MinutePadded:
                    builder.Append(time.Minute.ToString("00", CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Second:
                    builder.Append(time.Second.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.SecondPadded:
                    builder.Append(time.Second.ToString("00", CultureInfo.InvariantCulture));
                    break;
                // 毫秒固定三位（SSS），不足补零，方便做「时间 + 毫秒」的等宽显示。
                case FormatToken.Millisecond:
                    builder.Append(time.Millisecond.ToString("000", CultureInfo.InvariantCulture));
                    break;
                // 星期组：中文固定 zh-CN（周一/星期一），英文固定不变文化（Mon/Monday）。
                case FormatToken.WeekdayShort:
                    builder.Append(time.ToString("ddd", ChineseCulture));
                    break;
                case FormatToken.WeekdayLong:
                    builder.Append(time.ToString("dddd", ChineseCulture));
                    break;
                case FormatToken.EnglishWeekdayShort:
                    builder.Append(time.ToString("ddd", EnglishCulture));
                    break;
                case FormatToken.EnglishWeekdayLong:
                    builder.Append(time.ToString("dddd", EnglishCulture));
                    break;
                // ISO 周与年内天数带中文前缀，输出「第39周」「第303天」；周数走 TimeFormatter 保证 ISO 口径一致。
                case FormatToken.IsoWeek:
                    builder.Append("第");
                    builder.Append(TimeFormatter.GetIsoWeek(time).ToString("00", CultureInfo.InvariantCulture));
                    builder.Append("周");
                    break;
                case FormatToken.DayOfYear:
                    builder.Append("第");
                    builder.Append(time.DayOfYear.ToString(CultureInfo.InvariantCulture));
                    builder.Append("天");
                    break;
                // 上下午：tt 英文 AM/PM、TT 中文 上午/下午；12 点整归入下午，与常见时钟一致。
                case FormatToken.AmPmEnglish:
                    builder.Append(time.Hour < 12 ? "AM" : "PM");
                    break;
                case FormatToken.AmPmChinese:
                    builder.Append(time.Hour < 12 ? "上午" : "下午");
                    break;
                // 六天预报的天气现象：token - WeatherDay1 得到第几天（0 = 今天），越界或天气为空则不追加。
                case FormatToken.WeatherDay1:
                case FormatToken.WeatherDay2:
                case FormatToken.WeatherDay3:
                case FormatToken.WeatherDay4:
                case FormatToken.WeatherDay5:
                case FormatToken.WeatherDay6:
                    {
                        DailyForecast day = GetDay(weather, token - FormatToken.WeatherDay1);
                        if (day != null)
                        {
                            builder.Append(day.Condition);
                        }
                        break;
                    }
                // 六天预报的最低温：Min 可空（数据源缺该日温度），缺值时不显示而不是显示 0°C。
                case FormatToken.Low1:
                case FormatToken.Low2:
                case FormatToken.Low3:
                case FormatToken.Low4:
                case FormatToken.Low5:
                case FormatToken.Low6:
                    {
                        DailyForecast day = GetDay(weather, token - FormatToken.Low1);
                        if (day != null && day.Min.HasValue)
                        {
                            builder.Append(FormatTemperature(day.Min.Value));
                        }
                        break;
                    }
                // 六天预报的最高温：与 Low 系列对称，同样允许缺值。
                case FormatToken.High1:
                case FormatToken.High2:
                case FormatToken.High3:
                case FormatToken.High4:
                case FormatToken.High5:
                case FormatToken.High6:
                    {
                        DailyForecast day = GetDay(weather, token - FormatToken.High1);
                        if (day != null && day.Max.HasValue)
                        {
                            builder.Append(FormatTemperature(day.Max.Value));
                        }
                        break;
                    }
                // 实时天气字段组：weather 为空（首次拉取未返回/断网）时全部渲染为空，不抛异常。
                case FormatToken.Temperature:
                    if (weather != null)
                    {
                        builder.Append(FormatTemperature(weather.Temperature));
                    }
                    break;
                // 湿度带「湿度」前缀与百分号，做成「湿度45%」的自解释文本。
                case FormatToken.Humidity:
                    if (weather != null && weather.Humidity.HasValue)
                    {
                        builder.Append("湿度");
                        builder.Append(weather.Humidity.Value.ToString(CultureInfo.InvariantCulture));
                        builder.Append('%');
                    }
                    break;
                // 风向/图标/AQI/IP/城市同样按「有值才追加」处理；AQI 有等级时前置等级词（如「优 49」）。
                case FormatToken.WindDirection:
                    if (weather != null)
                    {
                        builder.Append(weather.WindDirection);
                    }
                    break;
                case FormatToken.WeatherIcon:
                    if (weather != null)
                    {
                        builder.Append(WeatherService.IconForCode(weather.WeatherCode));
                    }
                    break;
                case FormatToken.AirQuality:
                    if (weather != null && weather.AqiValue.HasValue)
                    {
                        if (!string.IsNullOrEmpty(weather.AqiLevel))
                        {
                            builder.Append(weather.AqiLevel);
                            builder.Append(' ');
                        }
                        builder.Append(weather.AqiValue.Value.ToString(CultureInfo.InvariantCulture));
                    }
                    break;
                case FormatToken.PublicIp:
                    if (weather != null)
                    {
                        builder.Append(weather.PublicIp);
                    }
                    break;
                case FormatToken.City:
                    if (weather != null)
                    {
                        builder.Append(weather.City);
                    }
                    break;
            }
        }

        /// <summary>把 24 小时制换算成 12 小时制；0 点与 12 点都输出 12（12 小时钟表习惯）。</summary>
        private static int Hour12(DateTime time)
        {
            // 12 小时制里 0 点与 12 点都显示 12，其余小时取余数。
            int hour = time.Hour % 12;
            return hour == 0 ? 12 : hour;
        }

        /// <summary>
        /// 取第 index 天的预报（0 = 今天）；天气为空或索引越界返回 null，调用方按「无内容」处理。
        /// 越界是正常情况：格式里写了 we6 但数据只回传了 3 天时不该报错。
        /// </summary>
        private static DailyForecast GetDay(WeatherInfo weather, int index)
        {
            // 三种「没有这一天」都归为 null：天气未就绪、预报条数不足、token 下标越界。
            if (weather == null || weather.Days == null || index < 0 || index >= weather.Days.Count)
            {
                return null;
            }
            return weather.Days[index];
        }

        /// <summary>
        /// 温度格式化为「20°C」；四舍五入用 AwayFromZero，避免 .NET 默认银行家舍入把 0.5 舍掉造成显示偏差。
        /// </summary>
        public static string FormatTemperature(double value)
        {
            // 固定不变文化输出阿拉伯数字加「°C」，防止系统区域设置改动单位或数字形状。
            return Math.Round(value, 0, MidpointRounding.AwayFromZero)
                .ToString(CultureInfo.InvariantCulture) + "°C";
        }

        /// <summary>
        /// 逐字符解析格式串（缓存未命中时才走这里，所以叫 Slow）；
        /// 规则：英文双引号包裹字面量（内部 "" 转义为一个 "）、\n 与 \\ 为转义、非 ASCII 字母直接当字面量、
        /// ASCII 字母按最长匹配识别变量，识别不了就报错并指出是第几个字符。
        /// </summary>
        private static bool ParseSlow(string format, out List<FormatPart> parts, out string error)
        {
            parts = new List<FormatPart>();
            error = null;
            // literal 累积连续的普通文字，遇到变量或结尾时一次性 flush 成一个 Literal 片段，减少片段数量。
            StringBuilder literal = new StringBuilder();
            int i = 0;

            // 主循环：每轮消费一段字面量、一个引号块或一个变量 token。
            while (i < format.Length)
            {
                char c = format[i];

                // 引号块：引号内的内容一律当字面量，用来写「第」「年」和在变量邻近处强制断开识别。
                if (c == '"')
                {
                    i++;
                    bool closed = false;
                    // 引号内遇到 "" 表示一个字面量双引号（CSV 式转义），单个 " 表示块结束。
                    while (i < format.Length)
                    {
                        if (format[i] == '"')
                        {
                            if (i + 1 < format.Length && format[i + 1] == '"')
                            {
                                literal.Append('"');
                                i += 2;
                                continue;
                            }
                            i++;
                            closed = true;
                            break;
                        }
                        literal.Append(format[i]);
                        i++;
                    }
                    // 未闭合引号是明确的用户错误，直接给出可操作的提示而不是猜测补全。
                    if (!closed)
                    {
                        error = "英文引号没有闭合，请补上结束引号。";
                        return false;
                    }
                    continue;
                }

                // 转义：\n 生成独立换行片段（便于 CollapseEmptyLines 按行处理），\\ 输出一个反斜杠，
                // 其它 \x 原样保留两个字符——不吞掉用户写的反斜杠，避免路径类文字被破坏。
                if (c == '\\' && i + 1 < format.Length)
                {
                    char next = format[i + 1];
                    if (next == 'n')
                    {
                        FlushLiteral(parts, literal);
                        parts.Add(new FormatPart { Token = FormatToken.Literal, Literal = "\n" });
                        i += 2;
                        continue;
                    }
                    if (next == '\\')
                    {
                        literal.Append('\\');
                        i += 2;
                        continue;
                    }
                    literal.Append('\\');
                    literal.Append(next);
                    i += 2;
                    continue;
                }

                // 中文等非 ASCII 字符不可能出现在变量名里，直接当字面量，省掉用户写引号的负担。
                if (!IsAsciiLetter(c))
                {
                    literal.Append(c);
                    i++;
                    continue;
                }

                // 先数连续同字母：MM/DD/fff 这类「看起来像 .NET 格式」的写法要给出精准提示，而不是落入「无法识别」。
                int run = CountRun(format, i, c);
                string trap = CheckRunTrap(c, run);
                if (trap != null)
                {
                    error = trap;
                    return false;
                }

                FormatToken token;
                int length;
                // 最长优先匹配：we1 与 w 都要先试长串，否则会被拆成两个短变量。
                if (TryMatchToken(format, i, out token, out length))
                {
                    FlushLiteral(parts, literal);
                    parts.Add(new FormatPart { Token = token });
                    i += length;
                    continue;
                }

                // 匹配失败：取最多 6 个字符做片段展示，告诉用户从第几个字符开始出错（用 1 基序号）。
                FlushLiteral(parts, literal);
                string snippet = format.Substring(i, Math.Min(6, format.Length - i));
                error = "无法识别格式变量“" + snippet + "”（第 " + (i + 1)
                    + " 个字符）。普通文字请用英文双引号括起来，例如 \"第\"。";
                return false;
            }

            // 收尾：把最后一段普通文字写入片段列表。
            FlushLiteral(parts, literal);
            return true;
        }

        /// <summary>
        /// 三连写（MM、DD、fff 等）的针对性错误提示；返回 null 表示不必特殊解释，交给「无法识别格式变量」兜底。
        /// 这些写法都是 .NET 格式串里的合法占位符，用户最容易照抄过来，所以要单独告诉正确写法。
        /// </summary>
        private static string CheckRunTrap(char c, int run)
        {
            switch (c)
            {
                // M/D：本方言单字母才是农历，双写会与 .NET 的公历月/日混淆，必须纠正。
                case 'M':
                    // 单个 M 是合法的农历月变量，只有重复写成 MM 才需要纠正。
                    if (run >= 2)
                    {
                        return "“MM”不是月份变量：公历月用 m 或 mm，农历月用 M。";
                    }
                    break;
                case 'D':
                    // 同理：单个 D 是合法的农历日变量，DD 才报错。
                    if (run >= 2)
                    {
                        return "“DD”不是日期变量：公历日用 d 或 dd，农历日用 D。";
                    }
                    break;
                // f：.NET 里 f 是小数秒，这里 ff 表示两位分钟，fff 更容易被误写成毫秒，提示改用 SSS。
                case 'f':
                    // ff 是合法的两位分钟，三连写才提示；fff 在 .NET 里又是毫秒，最容易混淆。
                    if (run >= 3)
                    {
                        return "毫秒请用 SSS；f 表示分钟（ff 为两位分钟）。";
                    }
                    break;
                // y/YYY：三位年份在 .NET 里合法，这里不支持，只接受两位或四位。
                case 'y':
                    // yy / yyyy 都合法，唯一不支持的是三位年。
                    if (run == 3)
                    {
                        return "年份请用 yy（两位）或 yyyy（四位）。";
                    }
                    break;
                case 'Y':
                    // 中文年同理：YY / YYYY 合法，三位报错。
                    if (run == 3)
                    {
                        return "中文年份请用 YY（两位）或 YYYY（四位）。";
                    }
                    break;
                // m/h/H/s：连续三个以上没有对应语义，明确告诉用户可用的一/两位写法。
                case 'm':
                    if (run >= 3)
                    {
                        return "月份请用 m 或 mm。";
                    }
                    break;
                case 'h':
                    if (run >= 3)
                    {
                        return "小时请用 h 或 hh（12 小时制），H 或 HH（24 小时制）。";
                    }
                    break;
                case 'H':
                    if (run >= 3)
                    {
                        return "小时请用 h 或 hh（12 小时制），H 或 HH（24 小时制）。";
                    }
                    break;
                case 's':
                    if (run >= 3)
                    {
                        return "秒请用 s 或 ss。";
                    }
                    break;
            }
            return null;
        }

        /// <summary>从 start 开始数连续相同字符的个数，用于识别 MMM/fff 这类三连写。</summary>
        private static int CountRun(string format, int start, char c)
        {
            int count = 0;
            // 只比较同一字符的游程；不同字符不参与，避免把 MMDD 当成一个整体误判。
            while (start + count < format.Length && format[start + count] == c)
            {
                count++;
            }
            return count;
        }

        /// <summary>
        /// 在当前位置尝试匹配变量名；按 4→1 长度从长到短试，命中即返回，保证 yyyy/we1 这类长变量优先。
        /// </summary>
        private static bool TryMatchToken(string format, int start, out FormatToken token, out int length)
        {
            token = FormatToken.Literal;
            length = 0;
            // 最长变量是 4 个字符（yyyy/YYYY/dddd/week），所以从 4 开始向下试。
            for (int size = 4; size >= 1; size--)
            {
                // 剩余字符不够长就跳过，避免 Substring 越界。
                if (start + size > format.Length)
                {
                    continue;
                }
                string candidate = format.Substring(start, size);
                FormatToken found;
                // 表用 Ordinal 比较，大小写敏感是刻意设计：m/M、h/H、d/D 语义完全不同。
                if (Tokens.TryGetValue(candidate, out found))
                {
                    token = found;
                    length = size;
                    return true;
                }
            }
            return false;
        }

        /// <summary>把累积的普通文字收成一个 Literal 片段；为空时什么也不做，避免产生空片段。</summary>
        private static void FlushLiteral(List<FormatPart> parts, StringBuilder literal)
        {
            if (literal.Length == 0)
            {
                return;
            }
            parts.Add(new FormatPart { Token = FormatToken.Literal, Literal = literal.ToString() });
            // 清空复用同一个 StringBuilder，减少每段字面量的分配。
            literal.Length = 0;
        }

        /// <summary>ASCII 字母判定：只有字母才可能是变量名，其它字符一律进字面量。</summary>
        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        /// <summary>
        /// 构建变量名到 token 的映射表；键区分大小写，同一张表同时被解析与帮助目录使用。
        /// 分组规则：先注册 4 字符长变量（保证最长匹配），再接 3 字符、天气系列、2 字符，最后是 1 字符单字母。
        /// </summary>
        private static Dictionary<string, FormatToken> BuildTokenTable()
        {
            Dictionary<string, FormatToken> table =
                new Dictionary<string, FormatToken>(StringComparer.Ordinal);

            // 4 字符组：四位年、长星期、ISO 周、年内天、毫秒；它们必须能整体命中，不能被拆成短变量。
            table["yyyy"] = FormatToken.Year4;
            table["YYYY"] = FormatToken.ChineseYear4;
            table["dddd"] = FormatToken.WeekdayLong;
            table["week"] = FormatToken.IsoWeek;
            table["day"] = FormatToken.DayOfYear;
            // doy 是同义别名：保留旧写法，避免老用户的格式串失效。
            table["doy"] = FormatToken.DayOfYear;
            table["SSS"] = FormatToken.Millisecond;

            // 3 字符组：英文月/星期的三位缩写、中文星期、天气字段别名（rtm/rhu/rwd/aqi/pos/icn）。
            table["EM3"] = FormatToken.EnglishMonthShort;
            table["EW3"] = FormatToken.EnglishWeekdayShort;
            table["ddd"] = FormatToken.WeekdayShort;
            table["EM"] = FormatToken.EnglishMonthLong;
            table["EW"] = FormatToken.EnglishWeekdayLong;
            table["rtm"] = FormatToken.Temperature;
            table["rhu"] = FormatToken.Humidity;
            table["rwd"] = FormatToken.WindDirection;
            table["aqi"] = FormatToken.AirQuality;
            table["pos"] = FormatToken.City;
            table["icn"] = FormatToken.WeatherIcon;

            // 六天预报系列：we=天气现象、tl=最低温、th=最高温，数字 1..6 表示今天到第五天。
            table["we1"] = FormatToken.WeatherDay1;
            table["we2"] = FormatToken.WeatherDay2;
            table["we3"] = FormatToken.WeatherDay3;
            table["we4"] = FormatToken.WeatherDay4;
            table["we5"] = FormatToken.WeatherDay5;
            table["we6"] = FormatToken.WeatherDay6;
            table["tl1"] = FormatToken.Low1;
            table["tl2"] = FormatToken.Low2;
            table["tl3"] = FormatToken.Low3;
            table["tl4"] = FormatToken.Low4;
            table["tl5"] = FormatToken.Low5;
            table["tl6"] = FormatToken.Low6;
            table["th1"] = FormatToken.High1;
            table["th2"] = FormatToken.High2;
            table["th3"] = FormatToken.High3;
            table["th4"] = FormatToken.High4;
            table["th5"] = FormatToken.High5;
            table["th6"] = FormatToken.High6;

            // 2 字符组：干支/生肖与两位数字变量；这里 M/m、d/D 的大小写差异决定公历还是农历。
            table["NN"] = FormatToken.GanzhiZodiac;
            table["nn"] = FormatToken.Zodiac;
            table["YY"] = FormatToken.ChineseYear2;
            table["yy"] = FormatToken.Year2;
            table["dd"] = FormatToken.DayPadded;
            table["HH"] = FormatToken.Hour24Padded;
            table["hh"] = FormatToken.Hour12Padded;
            table["mm"] = FormatToken.MonthPadded;
            table["ff"] = FormatToken.MinutePadded;
            table["ss"] = FormatToken.SecondPadded;
            table["tt"] = FormatToken.AmPmEnglish;
            table["TT"] = FormatToken.AmPmChinese;
            table["ip"] = FormatToken.PublicIp;

            // 1 字符组：单字母最少输入、最易混淆——M/D 走农历，m/d 走公历，f 是分钟而不是小数秒。
            table["M"] = FormatToken.LunarMonth;
            table["D"] = FormatToken.LunarDay;
            table["d"] = FormatToken.Day;
            table["H"] = FormatToken.Hour24;
            table["h"] = FormatToken.Hour12;
            table["m"] = FormatToken.Month;
            table["f"] = FormatToken.Minute;
            table["s"] = FormatToken.Second;

            return table;
        }
    }

    /// <summary>
    /// 农历/干支的只读换算工具：全部基于 <see cref="ChineseLunisolarCalendar"/>，不做缓存也不产生副作用。
    /// 边界：日期超出农历支持范围（1901–2100 之外）时各方法返回空串而不是抛异常，调用方按「无内容」处理；
    /// 这类异常属于输入越界而非程序缺陷，故 catch 内只回退不记日志（每秒渲染路径）。
    /// </summary>
    internal static class ChineseCalendarInfo
    {
        // 无状态换算，静态复用同一实例；ChineseLunisolarCalendar 本身线程安全（只读查询）。
        private static readonly ChineseLunisolarCalendar Calendar = new ChineseLunisolarCalendar();

        // 农历月名：索引 0 对应正月，11 对应腊月；闰月由 LunarMonthName 单独加「闰」前缀。
        private static readonly string[] MonthNames = new string[]
        {
            "正月", "二月", "三月", "四月", "五月", "六月",
            "七月", "八月", "九月", "十月", "冬月", "腊月"
        };

        // 天干：甲乙丙丁戊己庚辛壬癸。
        private static readonly string[] StemNames = new string[]
        {
            "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"
        };

        // 地支：子丑寅卯辰巳午未申酉戌亥；与生肖一一对应（索引相同）。
        private static readonly string[] BranchNames = new string[]
        {
            "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"
        };

        // 生肖名，索引与地支对齐（子鼠、丑牛…），GanzhiZodiacName 直接用同一个 branch 下标。
        private static readonly string[] ZodiacNames = new string[]
        {
            "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪"
        };

        // 中文数字（〇到九），用于把公历年份逐位转成「二〇二六」；注意 10 以上不在此表，逐位拼接即可。
        private static readonly char[] YearDigitsMap = new char[]
        {
            '〇', '一', '二', '三', '四', '五', '六', '七', '八', '九'
        };

        /// <summary>把公历年份逐位转成中文数字（2026 → 二〇二六）；负数取绝对值，非数字字符被跳过。</summary>
        public static string YearDigits(int year)
        {
            string digits = Math.Abs(year).ToString(CultureInfo.InvariantCulture);
            StringBuilder builder = new StringBuilder(digits.Length);
            // 逐位映射：只保留 0–9 的数字字符，'0' 对应「〇」而不是「零」。
            for (int i = 0; i < digits.Length; i++)
            {
                int value = digits[i] - '0';
                if (value >= 0 && value < YearDigitsMap.Length)
                {
                    builder.Append(YearDigitsMap[value]);
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// 农历月名（正月…腊月，闰月加「闰」前缀）；日期越界或换算失败返回空串。
        /// 闰月处理：ChineseLunisolarCalendar 在闰年把闰月也算进月份序号，闰月之后的月份序号要减 1 才是真实月。
        /// </summary>
        public static string LunarMonthName(DateTime time)
        {
            // 越界（超出农历支持年份）由 catch 兜底返回空串：这是每秒渲染路径，不记日志避免刷屏。
            try
            {
                int year = Calendar.GetYear(time);
                int month = Calendar.GetMonth(time);
                int leapMonth = Calendar.GetLeapMonth(year);
                bool leap = false;
                int realMonth = month;

                // GetLeapMonth 返回 0 表示当年无闰月；否则返回闰月的序号（1 起）。
                if (leapMonth > 0)
                {
                    // 恰好落在闰月：真实月份是闰月序号 - 1，并标记加「闰」。
                    if (month == leapMonth)
                    {
                        leap = true;
                        realMonth = leapMonth - 1;
                    }
                    // 闰月之后的月份：序号整体后移一位，要减 1 才能对回 MonthNames。
                    else if (month > leapMonth)
                    {
                        realMonth = month - 1;
                    }
                }

                // 序号越界说明换算结果不可用，返回空串让上层收掉内容，而不是取错月名。
                if (realMonth < 1 || realMonth > 12)
                {
                    return string.Empty;
                }

                return (leap ? "闰" : string.Empty) + MonthNames[realMonth - 1];
            }
            catch (ArgumentOutOfRangeException)
            {
                // 日期不在农历支持范围内（约 1901–2100）：静默回退空串，属于纯函数的输入越界而非故障。
                return string.Empty;
            }
        }

        /// <summary>农历日名（初一…三十）；日期越界返回空串。</summary>
        public static string LunarDayName(DateTime time)
        {
            // 与 LunarMonthName 同理：越界返回空串，渲染路径不记日志。
            try
            {
                int day = Calendar.GetDayOfMonth(time);
                return DayName(day);
            }
            catch (ArgumentOutOfRangeException)
            {
                // 超出农历支持范围：回退空串，纯函数不做日志。
                return string.Empty;
            }
        }

        /// <summary>生肖年（如「马年」）；地支换算失败返回空串。</summary>
        public static string ZodiacName(DateTime time)
        {
            int branch;
            // 复用 TryGetBranch 的越界处理，避免重复 try/catch。
            if (!TryGetBranch(time, out branch))
            {
                return string.Empty;
            }
            return ZodiacNames[branch - 1] + "年";
        }

        /// <summary>干支 + 生肖（如「丙午马年」）；三种都来自同一 sexagenary 序号的换算，越界返回空串。</summary>
        public static string GanzhiZodiacName(DateTime time)
        {
            // 越界保护同前：返回空串而不是抛异常。
            try
            {
                int sexagenary = Calendar.GetSexagenaryYear(time);
                int stem = Calendar.GetCelestialStem(sexagenary);
                int branch = Calendar.GetTerrestrialBranch(sexagenary);
                // 天干 1–10、地支 1–12；越界说明换算结果不可信，直接放弃整个 token。
                if (stem < 1 || stem > 10 || branch < 1 || branch > 12)
                {
                    return string.Empty;
                }
                return StemNames[stem - 1] + BranchNames[branch - 1] + ZodiacNames[branch - 1] + "年";
            }
            catch (ArgumentOutOfRangeException)
            {
                // 日期超出农历支持范围：回退空串，纯函数不做日志。
                return string.Empty;
            }
        }

        /// <summary>取地支序号（1–12）；日期越界或换算失败返回 false。</summary>
        private static bool TryGetBranch(DateTime time, out int branch)
        {
            branch = 0;
            // 单独抽出来供 ZodiacName 复用，越界时统一返回 false。
            try
            {
                int sexagenary = Calendar.GetSexagenaryYear(time);
                branch = Calendar.GetTerrestrialBranch(sexagenary);
                return branch >= 1 && branch <= 12;
            }
            catch (ArgumentOutOfRangeException)
            {
                // 日期超出农历支持范围：返回 false，由调用方渲染空串。
                return false;
            }
        }

        /// <summary>
        /// 农历日的中文写法：初一…初十、十一…十九、二十、廿一…廿九、三十。
        /// 传入非 1–30 的值返回空串（含农历小月的 30 日缺失情况由 Calendar 先行保证）。
        /// </summary>
        private static string DayName(int day)
        {
            // 先挡掉非法值：农历月最多 30 天，越界不该输出错误文本。
            if (day < 1 || day > 30)
            {
                return string.Empty;
            }
            // 上旬：初一…初九，初十单独命名。
            if (day <= 10)
            {
                return day == 10 ? "初十" : "初" + Digit(day);
            }
            // 中旬：十一…十九（十 + 个位）。
            if (day < 20)
            {
                return "十" + Digit(day - 10);
            }
            // 二十整：没有「二廿」这种写法。
            if (day == 20)
            {
                return "二十";
            }
            // 下旬：廿一…廿九。
            if (day < 30)
            {
                return "廿" + Digit(day - 20);
            }
            // 只剩 30 日：三十。
            return "三十";
        }

        /// <summary>1–9 的中文数字；用于拼「初X」「十X」「廿X」，越界返回空串。</summary>
        private static string Digit(int value)
        {
            // 局部数组而非静态字段：只在拼日名时用到，方法调用频率低（每秒一次渲染最多一两次）。
            string[] digits = new string[] { "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            if (value < 1 || value > 9)
            {
                return string.Empty;
            }
            return digits[value - 1];
        }
    }
}
