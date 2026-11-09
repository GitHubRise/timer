// 时间格式引擎：把用户输入的简洁变量格式解析成记号序列，再按给定时间与天气数据渲染成文本。
// 语法：字母串即变量（yy/yyyy/mm/M/D/ddd/HH/ff/ss、天气 rtm/rhu/we1..we6 等），大小写敏感；
// 其余字符原样输出，英文双引号括起的内容为字面量，"" 表示一个引号，\n 表示换行。
// 行数上限两行（MaxLines），格式串长度上限 120（MaxFormatLength），超限在规范化阶段拒绝。
// 缓存：解析结果按格式串存于 Cache（Ordinal 比较），超过 256 条整体清空，避免长跑累积。
// 线程安全：Tokens 构建后只读，Cache 只在 CacheLock 内读写；解析与渲染均为纯函数，非法输入返回空串。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameClock
{

    /// <summary>
    /// 格式变量记号：解析阶段把格式串切成字面量加若干个记号，渲染阶段由
    /// <see cref="FormatEngine.AppendToken"/> 按记号取值；枚举值只增不改，以免影响缓存里的旧结果。
    /// </summary>
    internal enum FormatToken
    {

        /// <summary>字面量：文本取自 <see cref="FormatPart.Literal"/>，渲染时不查时间与天气。</summary>
        Literal = 0,

        // ===== 年份 =====

        /// <summary>两位公历年（yy），如 26；取 Year % 100 后补零。</summary>
        Year2,

        /// <summary>四位公历年（yyyy），如 2026。</summary>
        Year4,

        /// <summary>两位中文数字年（YY），如“二六”。</summary>
        ChineseYear2,

        /// <summary>四位中文数字年（YYYY），如“二〇二六”。</summary>
        ChineseYear4,

        // ===== 生肖 =====

        /// <summary>生肖年（nn），如“马年”。</summary>
        Zodiac,

        /// <summary>干支生肖年（NN），如“丙午马年”。</summary>
        GanzhiZodiac,

        // ===== 月 =====

        /// <summary>公历月（m），不补零，1–12。</summary>
        Month,

        /// <summary>公历月（mm），两位补零。</summary>
        MonthPadded,

        /// <summary>农历月（M），如“正月”“闰四月”。</summary>
        LunarMonth,

        /// <summary>英文月份缩写（EM3），如“Nov”。</summary>
        EnglishMonthShort,

        /// <summary>英文月份全称（EM），如“November”。</summary>
        EnglishMonthLong,

        // ===== 日 =====

        /// <summary>公历日（d），不补零。</summary>
        Day,

        /// <summary>公历日（dd），两位补零。</summary>
        DayPadded,

        /// <summary>农历日（D），如“初十”“廿三”。</summary>
        LunarDay,

        // ===== 时 =====

        /// <summary>12 小时制小时（h），0 点显示 12。</summary>
        Hour12,

        /// <summary>12 小时制小时（hh），两位补零。</summary>
        Hour12Padded,

        /// <summary>24 小时制小时（H），不补零。</summary>
        Hour24,

        /// <summary>24 小时制小时（HH），两位补零。</summary>
        Hour24Padded,

        // ===== 分秒与毫秒 =====

        /// <summary>分钟（f），不补零。</summary>
        Minute,

        /// <summary>分钟（ff），两位补零。</summary>
        MinutePadded,

        /// <summary>秒（s），不补零。</summary>
        Second,

        /// <summary>秒（ss），两位补零。</summary>
        SecondPadded,

        /// <summary>毫秒（SSS），三位补零。</summary>
        Millisecond,

        // ===== 星期 =====

        /// <summary>中文星期简称（ddd），如“周一”。</summary>
        WeekdayShort,

        /// <summary>中文星期全称（dddd），如“星期一”。</summary>
        WeekdayLong,

        /// <summary>英文星期缩写（EW3），如“Mon”。</summary>
        EnglishWeekdayShort,

        /// <summary>英文星期全称（EW），如“Monday”。</summary>
        EnglishWeekdayLong,

        // ===== 年内进度 =====

        /// <summary>ISO 周序号（week），输出“第 45 周”。</summary>
        IsoWeek,

        /// <summary>年内第几天（day 或 doy），输出“第 314 天”。</summary>
        DayOfYear,

        // ===== 上下午 =====

        /// <summary>英文上下午（tt），AM 或 PM。</summary>
        AmPmEnglish,

        /// <summary>中文上下午（TT），上午或下午。</summary>
        AmPmChinese,

        // ===== 天气预报 =====

        /// <summary>第 1 天天气文字（we1），无数据时输出空。</summary>
        WeatherDay1,

        /// <summary>第 2 天天气文字（we2），无数据时输出空。</summary>
        WeatherDay2,

        /// <summary>第 3 天天气文字（we3），无数据时输出空。</summary>
        WeatherDay3,

        /// <summary>第 4 天天气文字（we4），无数据时输出空。</summary>
        WeatherDay4,

        /// <summary>第 5 天天气文字（we5），无数据时输出空。</summary>
        WeatherDay5,

        /// <summary>第 6 天天气文字（we6），无数据时输出空。</summary>
        WeatherDay6,

        /// <summary>第 1 天最低温（tl1），带 °C。</summary>
        Low1,

        /// <summary>第 2 天最低温（tl2），带 °C。</summary>
        Low2,

        /// <summary>第 3 天最低温（tl3），带 °C。</summary>
        Low3,

        /// <summary>第 4 天最低温（tl4），带 °C。</summary>
        Low4,

        /// <summary>第 5 天最低温（tl5），带 °C。</summary>
        Low5,

        /// <summary>第 6 天最低温（tl6），带 °C。</summary>
        Low6,

        /// <summary>第 1 天最高温（th1），带 °C。</summary>
        High1,

        /// <summary>第 2 天最高温（th2），带 °C。</summary>
        High2,

        /// <summary>第 3 天最高温（th3），带 °C。</summary>
        High3,

        /// <summary>第 4 天最高温（th4），带 °C。</summary>
        High4,

        /// <summary>第 5 天最高温（th5），带 °C。</summary>
        High5,

        /// <summary>第 6 天最高温（th6），带 °C。</summary>
        High6,

        // ===== 天气实况 =====

        /// <summary>当前温度（rtm），带 °C。</summary>
        Temperature,

        /// <summary>湿度（rhu），输出“湿度45%”。</summary>
        Humidity,

        /// <summary>风向（rwd），如“北风”。</summary>
        WindDirection,

        /// <summary>天气图标字符（icn），由天气代码映射。</summary>
        WeatherIcon,

        /// <summary>空气质量（aqi），如“优 49”。</summary>
        AirQuality,

        /// <summary>公网 IP（ip）。</summary>
        PublicIp,

        /// <summary>城市名（pos）。</summary>
        City
    }

    /// <summary>
    /// 格式片段：一个字面量或一个变量记号，是 <see cref="ParsedFormat"/> 列表的元素。
    /// 由解析器创建后不再修改，渲染阶段只读，可被多线程共享。
    /// </summary>
    internal sealed class FormatPart
    {

        /// <summary>片段类型；为 <see cref="FormatToken.Literal"/> 时取 <see cref="Literal"/>。</summary>
        public FormatToken Token;

        /// <summary>字面量文本；仅当 <see cref="Token"/> 为 Literal 时有效，其余情况为 null。</summary>
        public string Literal;
    }

    /// <summary>
    /// 一次解析的结果：按书写顺序展开的片段列表，并预判是否含天气变量。
    /// 由 <see cref="FormatEngine.TryParse"/> 创建并缓存；构造后只读，供渲染与查询方法共享。
    /// </summary>
    internal sealed class ParsedFormat
    {

        /// <summary>按顺序展开的片段；字面量与变量交替出现。</summary>
        public readonly List<FormatPart> Parts;

        /// <summary>是否含天气类变量，供调用方决定是否等天气数据到达后再渲染。</summary>
        public bool HasWeatherToken;

        /// <summary>
        /// 保存片段列表并预判天气变量：命中任一 <see cref="FormatEngine.IsWeatherToken"/> 即置位
        /// <see cref="HasWeatherToken"/>；只要发现一个就停止扫描，避免整表遍历。
        /// </summary>
        public ParsedFormat(List<FormatPart> parts)
        {
            // 列表由调用方传入并直接持有；解析完成后不再改动。
            Parts = parts;
            // 逐段判断记号种类，一旦发现天气变量就结束，后续片段无需再查。
            for (int i = 0; i < parts.Count; i++)
            {
                // 命中即置位并跳出：HasWeatherToken 只在构造阶段写一次。
                if (FormatEngine.IsWeatherToken(parts[i].Token))
                {
                    HasWeatherToken = true;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 简洁变量时间格式引擎：解析（<see cref="TryParse"/>）、渲染（<see cref="Format"/>）、
    /// 格式规范化（<see cref="TryNormalizeTokenFormat"/>）与常用格式预置（<see cref="CreatePresets"/>）。
    /// 生命周期：进程内静态唯一，无实例状态；解析结果按格式串缓存，供悬浮窗与任务栏时钟共用。
    /// 线程安全：<see cref="Tokens"/> 构建后只读，<see cref="Cache"/> 只在 <see cref="CacheLock"/> 内读写。
    /// </summary>
    internal static class FormatEngine
    {

        // ===== 模式与默认格式 =====

        /// <summary>.NET 自定义格式模式的标识值；其余未知模式值都会归一到这里。</summary>
        public const string ModeDotNet = "dotnet";

        /// <summary>简洁变量格式模式的标识值，比较时忽略大小写。</summary>
        public const string ModeToken = "token";

        /// <summary>变量格式为空或非法时的默认格式；该格式里 ff 表示两位分钟。</summary>
        public const string DefaultTokenFormat = "HH:ff:ss";

        /// <summary>任务栏时钟默认两行格式：第一行日期加时间，第二行农历、星期与温度。</summary>
        public const string DefaultTaskbarTokenFormat = "yyyy-mm-dd HH:ff:ss\nMD ddd rtm";

        /// <summary>格式串最大长度（含换行），超长直接拒绝，以限制缓存体积与渲染开销。</summary>
        public const int MaxFormatLength = 120;

        /// <summary>允许的最大行数：两行，对应任务栏的上下两行显示。</summary>
        public const int MaxLines = 2;

        // ===== 区域性 =====

        /// <summary>中文区域：中文星期与农历名称按 zh-CN 取。</summary>
        private static readonly CultureInfo ChineseCulture = CultureInfo.GetCultureInfo("zh-CN");

        /// <summary>英文区域：英文月名与星期名固定用不变文化，不受系统区域影响。</summary>
        private static readonly CultureInfo EnglishCulture = CultureInfo.InvariantCulture;

        // ===== 词法表 =====

        /// <summary>变量文本到记号的查找表，启动时一次性构建，之后只读。</summary>
        private static readonly Dictionary<string, FormatToken> Tokens = BuildTokenTable();

        // ===== 解析缓存 =====

        /// <summary>格式串到解析结果的缓存；Ordinal 比较，避免区域性差异造成误命中。</summary>
        private static readonly Dictionary<string, ParsedFormat> Cache =
            new Dictionary<string, ParsedFormat>(StringComparer.Ordinal);

        /// <summary>缓存读写锁：只在查表与写回时持锁，解析过程在锁外执行。</summary>
        private static readonly object CacheLock = new object();

        /// <summary>判断模式值是否为变量模式（忽略大小写）；其余值一律按 .NET 格式处理。</summary>
        public static bool IsTokenMode(string mode)
        {
            return string.Equals(mode, ModeToken, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把任意模式值归一为两个常量之一，未知值回落为 .NET 模式。</summary>
        public static string NormalizeMode(string mode)
        {
            return IsTokenMode(mode) ? ModeToken : ModeDotNet;
        }

        /// <summary>
        /// 校验并规范化用户输入的变量格式：统一换行、展开 \n 转义、逐行去空白、限制行数与长度，
        /// 最后真正解析一次确认语法合法。成功返回 true 与规范化文本，失败返回 false 与中文 error。
        /// </summary>
        /// <param name="input">用户输入的原始格式串，null 或全空白视为非法。</param>
        /// <param name="allowNewline">是否允许两行；天气格式传 false，只能单行。</param>
        /// <param name="normalized">规范化后的格式串；失败时为 null。</param>
        /// <param name="error">失败原因（中文）；成功时为 null。</param>
        /// <returns>true 表示格式可用。</returns>
        public static bool TryNormalizeTokenFormat(string input, bool allowNewline, out string normalized, out string error)
        {
            normalized = null;
            error = null;

            // 空串或全空白直接判非法，避免后面 Split 出无意义的空行。
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "时间格式不能为空。";
                return false;
            }

            // 先把三种换行写法统一成 \n，再展开字面量 \n 转义，后续只需按 \n 切行。
            string format = input.Replace("\r\n", "\n").Replace('\r', '\n');
            format = format.Replace("\\n", "\n");

            // 按 \n 切行，行数上限交给下面的判断把关。
            string[] lines = format.Split('\n');
            // 超过两行直接拒绝；提示语按调用场景区分（天气单行与悬浮窗两行）。
            if (lines.Length > MaxLines)
            {
                error = allowNewline
                    ? "最多只支持一个换行（两行文字）。"
                    : "天气格式只能是一行文字。";
                return false;
            }
            // 不允许换行的场景（天气）只接受单行。
            if (!allowNewline && lines.Length > 1)
            {
                error = "天气格式只能是一行文字。";
                return false;
            }

            // 逐行去首尾空白，把用户多加的空格清掉。
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Trim();
                // 任一行变空即拒绝：说明写了空行，或只在换行一侧填了内容。
                if (lines[i].Length == 0)
                {
                    error = allowNewline ? "换行两侧都需要填写内容。" : "天气格式不能为空。";
                    return false;
                }
            }

            format = string.Join("\n", lines);
            // 拼回后检查总长度，超长时提示具体上限。
            if (format.Length > MaxFormatLength)
            {
                error = "格式太长，请控制在 " + MaxFormatLength + " 个字符以内。";
                return false;
            }

            ParsedFormat parsed;
            // 真正走一次解析：未闭合引号、易错写法、未知变量都在这里暴露，error 直接透传。
            if (!TryParse(format, out parsed, out error))
            {
                return false;
            }

            normalized = format;
            return true;
        }

        /// <summary>
        /// 规范化变量格式；为空或非法时回落到 <see cref="DefaultTokenFormat"/>，
        /// 保证调用方拿到的永远是可渲染的串。
        /// </summary>
        public static string NormalizeTokenFormat(string input)
        {
            string normalized;
            string error;
            // 校验通过用规范化结果，失败则用内置默认格式兜底。
            if (TryNormalizeTokenFormat(input, true, out normalized, out error))
            {
                return normalized;
            }
            return DefaultTokenFormat;
        }

        /// <summary>
        /// 规范化天气显示格式：空白输入或非法输入都返回空串（表示不显示天气）。
        /// 与 <see cref="NormalizeTokenFormat"/> 不同，这里不回落到默认格式。
        /// </summary>
        public static string NormalizeWeatherFormat(string input)
        {
            // 空白视为“不使用天气格式”，直接返回空串，省掉一次解析。
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }
            string normalized;
            string error;
            // 非法格式同样返回空串，由界面把解析错误展示给用户。
            if (TryNormalizeTokenFormat(input, false, out normalized, out error))
            {
                return normalized;
            }
            return string.Empty;
        }

        /// <summary>
        /// 解析格式串：空串得到空片段列表；命中缓存直接返回同一实例；未命中才走 <see cref="ParseSlow"/>，
        /// 成功后写回缓存。缓存超过 256 条时整体清空，防止长时间运行无限增长。
        /// 失败时 parsed 为 null、error 为中文原因，不抛异常。
        /// </summary>
        /// <param name="format">待解析的格式串，null 或空串都是合法输入。</param>
        /// <param name="parsed">解析结果；失败时为 null。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>true 表示解析成功或命中缓存。</returns>
        public static bool TryParse(string format, out ParsedFormat parsed, out string error)
        {
            parsed = null;
            error = null;

            // 空格式合法：渲染结果为空串，调用方不需要特判。
            if (string.IsNullOrEmpty(format))
            {
                parsed = new ParsedFormat(new List<FormatPart>());
                return true;
            }

            // 先查缓存；持锁范围只有字典查找，解析与渲染都在锁外。
            lock (CacheLock)
            {
                // 命中即返回同一实例：ParsedFormat 构造后只读，可安全共享。
                if (Cache.TryGetValue(format, out parsed))
                {
                    return true;
                }
            }

            // 未命中才做真正的词法/语法分析。
            List<FormatPart> parts;
            if (!ParseSlow(format, out parts, out error))
            {
                parsed = null;
                return false;
            }

            parsed = new ParsedFormat(parts);
            // 写回缓存；条目过多时整体清空，简单但足以限制内存。
            lock (CacheLock)
            {
                // 硬上限 256，清空后重新积累。
                if (Cache.Count > 256)
                {
                    Cache.Clear();
                }
                Cache[format] = parsed;
            }
            return true;
        }

        /// <summary>
        /// 按格式渲染时间与天气：解析失败返回空串；字面量直接追加，变量交给
        /// <see cref="AppendToken"/> 取值；最后压掉变量缺数据造成的空行。
        /// </summary>
        /// <param name="time">要显示的时间。</param>
        /// <param name="weather">天气数据，可为 null（此时天气变量输出为空）。</param>
        /// <param name="format">变量格式串。</param>
        /// <returns>渲染文本；格式非法时为空串。</returns>
        public static string Format(DateTime time, WeatherInfo weather, string format)
        {
            ParsedFormat parsed;
            string error;
            // 解析失败（语法错误）不抛异常，返回空串让调用方显示原始格式。
            if (!TryParse(format, out parsed, out error) || parsed == null)
            {
                return string.Empty;
            }

            // 预估容量：格式长度加上变量展开的余量，减少中途扩容。
            StringBuilder builder = new StringBuilder(format.Length + 16);
            // 顺序展开片段：字面量原样追加，其余按记号渲染。
            for (int i = 0; i < parsed.Parts.Count; i++)
            {
                FormatPart part = parsed.Parts[i];
                // 字面量不依赖时间与天气，直接写入。
                if (part.Token == FormatToken.Literal)
                {
                    builder.Append(part.Literal);
                    continue;
                }
                AppendToken(builder, part.Token, time, weather);
            }
            return CollapseEmptyLines(builder.ToString());
        }

        /// <summary>
        /// 去掉渲染结果中的空行（只含空白的行也算空），避免天气变量缺数据时留下空行占位。
        /// 不含换行时原样返回，省掉 Split 与 Join 的开销。
        /// </summary>
        private static string CollapseEmptyLines(string text)
        {
            // 单行文本没有可压缩的空行，直接返回。
            if (text.IndexOf('\n') < 0)
            {
                return text;
            }

            // 先按 \n 切行，再用列表收集保留下来的行。
            string[] lines = text.Split('\n');
            List<string> kept = new List<string>(lines.Length);
            // 逐行判断，只保留含可见字符的行。
            for (int i = 0; i < lines.Length; i++)
            {
                // 空白行丢弃；非空行保留原内容与顺序。
                if (lines[i].Trim().Length > 0)
                {
                    kept.Add(lines[i]);
                }
            }
            return string.Join("\n", kept.ToArray());
        }

        /// <summary>判断格式是否含天气类变量；解析失败按“不含”处理，供调用方决定是否请求天气。</summary>
        public static bool ContainsWeatherToken(string format)
        {
            ParsedFormat parsed;
            string error;
            return TryParse(format, out parsed, out error) && parsed != null && parsed.HasWeatherToken;
        }

        /// <summary>是否需要空气质量数据（格式含 aqi 变量）。</summary>
        public static bool RequiresAirQuality(string format)
        {
            return ContainsToken(format, FormatToken.AirQuality);
        }

        /// <summary>是否需要公网 IP（格式含 ip 变量）。</summary>
        public static bool RequiresPublicIp(string format)
        {
            return ContainsToken(format, FormatToken.PublicIp);
        }

        /// <summary>通用查询：片段列表中是否出现指定记号；解析失败或结果为空都返回 false。</summary>
        private static bool ContainsToken(string format, FormatToken token)
        {
            ParsedFormat parsed;
            string error;
            // 解析失败或结果为空都按“不含”处理，调用方无需区分两种失败。
            if (!TryParse(format, out parsed, out error) || parsed == null)
            {
                return false;
            }
            // 线性扫描片段，命中即返回；片段数通常是个位数。
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
        /// 判断记号是否属于天气类：6 天预报（we/tl/th）、实况（温度、湿度、风向、图标、空气质量）
        /// 以及公网 IP 与城市。枚举中预报段连续，因此用区间比较减少分支。
        /// </summary>
        public static bool IsWeatherToken(FormatToken token)
        {
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
        /// 生成设置界面的常用格式列表：每条含名称、格式串与用样例时间渲染出的示例；
        /// 示例中的换行替换为 " / "，保证单行展示。
        /// </summary>
        /// <param name="sample">渲染示例用的时间，通常传当前时间。</param>
        /// <returns>按界面展示顺序排列的预置项。</returns>
        public static List<TimeFormatPreset> CreatePresets(DateTime sample)
        {
            List<TimeFormatPreset> list = new List<TimeFormatPreset>();
            // 时间与仅日期预置：默认、12 小时制、毫秒、仅日期。
            Add(list, "时间（默认）", "HH:ff:ss", sample);
            Add(list, "时间（12 小时 + 上下午）", "TT hh:ff:ss", sample);
            Add(list, "时间 + 毫秒", "HH:ff:ss.SSS", sample);
            Add(list, "仅日期", "yyyy-mm-dd", sample);

            // 日期加时间组合，从单行逐步加星期、农历与温度。
            Add(list, "日期 + 时间", "yyyy-mm-dd HH:ff:ss", sample);
            Add(list, "日期 + 时间 + 星期", "yyyy-mm-dd HH:ff:ss dddd", sample);
            Add(list, "中文日期 + 时间", "yyyy年mm月dd日 HH:ff:ss", sample);
            Add(list, "日期 + 时间 + 农历 + 星期", "yyyy-mm-dd HH:ff:ss\nMD ddd", sample);
            Add(list, "日期 + 时间 + 农历 + 星期 + 温度", "yyyy-mm-dd HH:ff:ss\nMD ddd rtm", sample);
            // 双行组合：第一行日期时间，第二行农历、星期、温度或生肖。
            Add(list, "日期 + 时分 + 生肖 + 农历 + 星期", "yyyy-mm-dd HH:ff\nnn MD ddd", sample);
            Add(list, "短日期 + 时分 + 农历 + 星期", "mm-dd HH:ff\nMD ddd", sample);
            Add(list, "短日期 + 农历 + 星期 + 时间", "mm-dd MD ddd HH:ff", sample);
            Add(list, "农历 + 星期 + 短日期 + 时间", "MD ddd mm-dd HH:ff", sample);
            Add(list, "中文日期 + 数字年 + 农历 + 星期", "yyyy年mm月dd日 HH:ff\nYYYY年MD ddd", sample);
            Add(list, "中文日期 + 生肖 + 农历 + 星期", "yyyy年mm月dd日 HH:ff\nnn YYYY年MD ddd", sample);
            // 斜杠日期配农历星期，再对照中英文上下午。
            Add(list, "斜杠日期 + 农历 + 星期 + 中文上下午", "yyyy/mm/dd MD ddd\nTT HH:ff:ss", sample);
            Add(list, "斜杠日期 + 农历 + 星期 + 英文上下午", "yyyy/mm/dd MD ddd\ntt HH:ff:ss", sample);
            // 行序颠倒与单行混排，方便用户选“时间在上、日期在下”。
            Add(list, "时间在上 / 日期在下", "HH:ff:ss\nyyyy/mm/dd", sample);
            Add(list, "两行：中文日期 / 时间", "yyyy年mm月dd日\nHH:ff:ss", sample);
            Add(list, "干支 + 农历 + 星期", "NN MD ddd", sample);
            Add(list, "日期 + 农历 + 年内进度", "yyyy年mm月dd日 NN MD\nday week 今天 HH:ff:ss", sample);
            // 英文日期与星期，依赖 EM 与 EW 系列变量。
            Add(list, "英文日期 + 星期", "EW, EM d, yyyy", sample);
            return list;
        }

        /// <summary>
        /// 追加一条预置：先用样例时间渲染出示例（换行显示为 " / "），再包装成
        /// <see cref="TimeFormatPreset"/> 入表，保证列表示例与真实显示一致。
        /// </summary>
        private static void Add(List<TimeFormatPreset> list, string name, string format, DateTime sample)
        {
            string example = Format(sample, PresetWeather, format).Replace("\n", " / ");
            list.Add(new TimeFormatPreset(name, format, example));
        }

        // ===== 预置示例数据 =====

        /// <summary>预置示例用的固定天气数据，避免打开设置时依赖真实天气是否已到达。</summary>
        private static readonly WeatherInfo PresetWeather = CreatePresetWeather();

        /// <summary>
        /// 构造示例天气：固定实况数值与 6 天预报，数值人工挑选以保证示例文字长度稳定；
        /// 结果只在静态初始化时创建一次。
        /// </summary>
        private static WeatherInfo CreatePresetWeather()
        {
            // 先填实况字段，示例里 rtm/rhu/rwd/aqi/ip 都有值可显示。
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

            // 再补 6 天预报：天气文字与最低、最高温一一对应，供 weN/tlN/thN 示例使用。
            string[] conditions = new string[] { "晴", "多云", "阴", "小雨", "晴", "晴" };
            double[] lows = new double[] { 12, 10, 11, 9, 10, 13 };
            double[] highs = new double[] { 24, 25, 23, 20, 22, 25 };
            // 逐天填入，下标与 weN/tlN/thN 的编号一致（第 1 天对应下标 0）。
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
        /// 渲染单个记号：按 <see cref="FormatToken"/> 分支把时间或天气值写入 builder。
        /// 所有分支都不抛异常：weather 为 null、预报天数不足、可选值缺失时输出空，由调用方决定占位。
        /// </summary>
        /// <param name="builder">输出缓冲，调用方传入并复用。</param>
        /// <param name="token">要渲染的记号；Literal 不会走到这里。</param>
        /// <param name="time">时间来源。</param>
        /// <param name="weather">天气来源，可为 null。</param>
        private static void AppendToken(StringBuilder builder, FormatToken token, DateTime time, WeatherInfo weather)
        {
            // 按记号分支取值；数字一律用 InvariantCulture 输出，避免被本地化成其他数字字形。
            switch (token)
            {
                // 公历年：两位取模补零，四位直接补零。
                case FormatToken.Year2:
                    builder.Append((time.Year % 100).ToString("00", CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Year4:
                    builder.Append(time.Year.ToString("0000", CultureInfo.InvariantCulture));
                    break;
                // 中文年份：逐位映射汉字，与公历数字年互不影响。
                case FormatToken.ChineseYear2:
                    builder.Append(ChineseCalendarInfo.YearDigits(time.Year % 100));
                    break;
                case FormatToken.ChineseYear4:
                    builder.Append(ChineseCalendarInfo.YearDigits(time.Year));
                    break;
                // 生肖与干支都依赖农历年，交由 ChineseCalendarInfo 计算。
                case FormatToken.Zodiac:
                    builder.Append(ChineseCalendarInfo.ZodiacName(time));
                    break;
                case FormatToken.GanzhiZodiac:
                    builder.Append(ChineseCalendarInfo.GanzhiZodiacName(time));
                    break;
                // 公历月：m 不补零，mm 补零。
                case FormatToken.Month:
                    builder.Append(time.Month.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.MonthPadded:
                    builder.Append(time.Month.ToString("00", CultureInfo.InvariantCulture));
                    break;
                // 农历月带“闰”前缀，与公历月是两套写法。
                case FormatToken.LunarMonth:
                    builder.Append(ChineseCalendarInfo.LunarMonthName(time));
                    break;
                // 英文月名固定用不变文化，不受系统区域影响。
                case FormatToken.EnglishMonthShort:
                    builder.Append(time.ToString("MMM", EnglishCulture));
                    break;
                case FormatToken.EnglishMonthLong:
                    builder.Append(time.ToString("MMMM", EnglishCulture));
                    break;
                // 公历日：d 不补零，dd 补零。
                case FormatToken.Day:
                    builder.Append(time.Day.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.DayPadded:
                    builder.Append(time.Day.ToString("00", CultureInfo.InvariantCulture));
                    break;
                // 农历日输出“初X”“廿X”等中文写法。
                case FormatToken.LunarDay:
                    builder.Append(ChineseCalendarInfo.LunarDayName(time));
                    break;
                // 12 小时制：先经 Hour12 把 0 点折算成 12 点。
                case FormatToken.Hour12:
                    builder.Append(Hour12(time).ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Hour12Padded:
                    builder.Append(Hour12(time).ToString("00", CultureInfo.InvariantCulture));
                    break;
                // 24 小时制直接取 time.Hour。
                case FormatToken.Hour24:
                    builder.Append(time.Hour.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Hour24Padded:
                    builder.Append(time.Hour.ToString("00", CultureInfo.InvariantCulture));
                    break;
                // 分、秒、毫秒分别对应 f/ff、s/ss、SSS，补零位数写在格式串里。
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
                case FormatToken.Millisecond:
                    builder.Append(time.Millisecond.ToString("000", CultureInfo.InvariantCulture));
                    break;
                // 中文星期用 zh-CN 输出，英文星期用不变文化输出。
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
                // 年内进度：周序号与第几天都带中文量词，便于直接显示。
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
                // 上下午以 12 点分界，12:00 起算下午。
                case FormatToken.AmPmEnglish:
                    builder.Append(time.Hour < 12 ? "AM" : "PM");
                    break;
                case FormatToken.AmPmChinese:
                    builder.Append(time.Hour < 12 ? "上午" : "下午");
                    break;
                // 预报文字：用“记号减去 WeatherDay1”得到 0–5 的下标，省掉逐天分支。
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
                // 最低温：该天缺数据或没有最低温时输出空，不写占位符。
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
                // 最高温：与最低温同样按偏移取下标，逻辑保持一致。
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
                // 实况温度：weather 为空时输出空，避免显示 0°C 误导。
                case FormatToken.Temperature:
                    if (weather != null)
                    {
                        builder.Append(FormatTemperature(weather.Temperature));
                    }
                    break;
                // 湿度带“湿度”前缀与百分号；没有数值时整段不输出。
                case FormatToken.Humidity:
                    if (weather != null && weather.Humidity.HasValue)
                    {
                        builder.Append("湿度");
                        builder.Append(weather.Humidity.Value.ToString(CultureInfo.InvariantCulture));
                        builder.Append('%');
                    }
                    break;
                // 风向直接取实况字段，缺数据时为空。
                case FormatToken.WindDirection:
                    if (weather != null)
                    {
                        builder.Append(weather.WindDirection);
                    }
                    break;
                // 图标由天气代码映射，与文字条件解耦。
                case FormatToken.WeatherIcon:
                    if (weather != null)
                    {
                        builder.Append(WeatherService.IconForCode(weather.WeatherCode));
                    }
                    break;
                // 空气质量：有等级时输出“等级 数值”，否则只输出数值。
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
                // 公网 IP 取不到时保持空串，不显示旧值。
                case FormatToken.PublicIp:
                    if (weather != null)
                    {
                        builder.Append(weather.PublicIp);
                    }
                    break;
                // 城市名同样可为空，由调用方决定如何处理空段。
                case FormatToken.City:
                    if (weather != null)
                    {
                        builder.Append(weather.City);
                    }
                    break;
            }
        }

        /// <summary>把 0–23 小时折算成 12 小时制：0 点显示 12 点，其余直接取模。</summary>
        private static int Hour12(DateTime time)
        {
            int hour = time.Hour % 12;
            // 0 点和 12 点取模后都是 0，统一显示为 12。
            return hour == 0 ? 12 : hour;
        }

        /// <summary>按 0–5 下标安全取预报：weather 为空、Days 为空或下标越界都返回 null。</summary>
        private static DailyForecast GetDay(WeatherInfo weather, int index)
        {
            // 三层防护：没有天气、没有预报列表、下标越界，一律返回 null 由调用方跳过输出。
            if (weather == null || weather.Days == null || index < 0 || index >= weather.Days.Count)
            {
                return null;
            }
            return weather.Days[index];
        }

        /// <summary>温度格式化为整数加 “°C”：四舍五入（远离零），固定不变文化输出。</summary>
        public static string FormatTemperature(double value)
        {
            return Math.Round(value, 0, MidpointRounding.AwayFromZero)
                .ToString(CultureInfo.InvariantCulture) + "°C";
        }

        /// <summary>
        /// 词法/语法分析：逐字符扫描，识别引号字面量、\n 与 \\ 转义、变量记号，其余字符累积为字面量。
        /// 变量按最长优先匹配（4 到 1 个字符），匹配前先拦截 MM/DD/fff 等易错写法。
        /// 成功返回 true 与片段列表；失败返回 false 与中文 error（引号未闭合、易错写法、未知变量）。
        /// </summary>
        /// <param name="format">待解析的格式串，长度与行数已由规范化阶段把关。</param>
        /// <param name="parts">解析出的片段列表；失败时可能只含部分结果，调用方应整体丢弃。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>true 表示整串解析成功。</returns>
        private static bool ParseSlow(string format, out List<FormatPart> parts, out string error)
        {
            // 先准备结果列表与字面量缓冲，flush 时把缓冲落成 Literal 片段。
            parts = new List<FormatPart>();
            error = null;
            StringBuilder literal = new StringBuilder();
            int i = 0;

            // 主扫描：i 前进到串尾即结束，每轮消费一个字符或一整个记号。
            while (i < format.Length)
            {
                char c = format[i];

                // 英文双引号开启字面量段：内部字符原样保留，"" 表示一个字面引号。
                if (c == '"')
                {
                    i++;
                    bool closed = false;
                    // 逐步消费直到结束引号，连续两个引号视为一个引号的转义。
                    while (i < format.Length)
                    {
                        if (format[i] == '"')
                        {
                            // "" 转义为一个引号，因此格式串里写不出空字面量段。
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
                        // 非引号字符原样进入缓冲区，引号段内反斜杠不再有转义含义。
                        literal.Append(format[i]);
                        i++;
                    }
                    // 走到串尾仍未闭合：整段作废，提示用户补上结束引号。
                    if (!closed)
                    {
                        error = "英文引号没有闭合，请补上结束引号。";
                        return false;
                    }
                    continue;
                }

                // 反斜杠转义：\n 产出换行片段，\\ 产出反斜杠，其余组合按原样输出两个字符。
                if (c == '\\' && i + 1 < format.Length)
                {
                    char next = format[i + 1];
                    // \n 单独成段：空行可能在渲染阶段被 CollapseEmptyLines 压缩。
                    if (next == 'n')
                    {
                        FlushLiteral(parts, literal);
                        parts.Add(new FormatPart { Token = FormatToken.Literal, Literal = "\n" });
                        i += 2;
                        continue;
                    }
                    // 显式反斜杠，避免被当成转义前缀吞掉后面的字符。
                    if (next == '\\')
                    {
                        literal.Append('\\');
                        i += 2;
                        continue;
                    }
                    // 其它转义不认识，按字面保留“反斜杠加字符”，避免静默吞字。
                    literal.Append('\\');
                    literal.Append(next);
                    i += 2;
                    continue;
                }

                // 非 ASCII 字母（中文、标点、空格）直接并入字面量，无需引号。
                if (!IsAsciiLetter(c))
                {
                    literal.Append(c);
                    i++;
                    continue;
                }

                // 字母可能是变量：先数连续相同字符，用于拦截 MM、DD 等易错写法。
                int run = CountRun(format, i, c);
                string trap = CheckRunTrap(c, run);
                // 命中易错写法立即报错，比落到“无法识别变量”更容易看懂。
                if (trap != null)
                {
                    error = trap;
                    return false;
                }

                FormatToken token;
                int length;
                // 最长优先匹配成功：先落盘已累积的字面量，再追加记号并跳过对应长度。
                if (TryMatchToken(format, i, out token, out length))
                {
                    FlushLiteral(parts, literal);
                    parts.Add(new FormatPart { Token = token });
                    i += length;
                    continue;
                }

                // 匹配失败：给出位置与片段，并提示中文用英文双引号包裹。
                FlushLiteral(parts, literal);
                string snippet = format.Substring(i, Math.Min(6, format.Length - i));
                error = "无法识别格式变量“" + snippet + "”（第 " + (i + 1)
                    + " 个字符）。普通文字请用英文双引号括起来，例如 \"第\"。";
                return false;
            }

            // 收尾：把最后一段字面量落盘，否则尾部文字会丢失。
            FlushLiteral(parts, literal);
            return true;
        }

        /// <summary>
        /// 拦截易混淆的连续字母写法（MM、DD、fff、yyy、mmm、hhh、HHH、sss 等），返回中文提示；
        /// 返回 null 表示没问题，交由后续最长匹配处理。
        /// </summary>
        private static string CheckRunTrap(char c, int run)
        {
            // 只在“某字母重复到一定次数”时判定为误写，单字符与正常长度一律放行。
            switch (c)
            {
                // MM 常被当成月份，实际月份是 m/mm，农历月才是 M。
                case 'M':
                    if (run >= 2)
                    {
                        return "“MM”不是月份变量：公历月用 m 或 mm，农历月用 M。";
                    }
                    break;
                // DD 常被当成日期，公历日是 d/dd，农历日才是 D。
                case 'D':
                    if (run >= 2)
                    {
                        return "“DD”不是日期变量：公历日用 d 或 dd，农历日用 D。";
                    }
                    break;
                // fff 常被当成毫秒；毫秒是 SSS，f/ff 表示分钟。
                case 'f':
                    if (run >= 3)
                    {
                        return "毫秒请用 SSS；f 表示分钟（ff 为两位分钟）。";
                    }
                    break;
                // yyy 不存在：年份只有两位 yy 或四位 yyyy。
                case 'y':
                    if (run == 3)
                    {
                        return "年份请用 yy（两位）或 yyyy（四位）。";
                    }
                    break;
                // YYY 同理，中文年份只有 YY 与 YYYY。
                case 'Y':
                    if (run == 3)
                    {
                        return "中文年份请用 YY（两位）或 YYYY（四位）。";
                    }
                    break;
                // mmm 不是月份缩写；英文月份缩写是 EM3。
                case 'm':
                    if (run >= 3)
                    {
                        return "月份请用 m 或 mm。";
                    }
                    break;
                // hhh 越界：12 小时制只有 h 与 hh。
                case 'h':
                    if (run >= 3)
                    {
                        return "小时请用 h 或 hh（12 小时制），H 或 HH（24 小时制）。";
                    }
                    break;
                // HHH 越界：24 小时制只有 H 与 HH。
                case 'H':
                    if (run >= 3)
                    {
                        return "小时请用 h 或 hh（12 小时制），H 或 HH（24 小时制）。";
                    }
                    break;
                // sss 越界：秒只有 s 与 ss，毫秒见 SSS。
                case 's':
                    if (run >= 3)
                    {
                        return "秒请用 s 或 ss。";
                    }
                    break;
            }
            return null;
        }

        /// <summary>从 <paramref name="start"/> 起数连续相同字符的个数，用于判断变量长度与误写。</summary>
        private static int CountRun(string format, int start, char c)
        {
            // 只要同一字符连续就继续，遇到不同字符或串尾即停止。
            int count = 0;
            while (start + count < format.Length && format[start + count] == c)
            {
                count++;
            }
            return count;
        }

        /// <summary>
        /// 在 <paramref name="start"/> 处做最长优先匹配：先试 4 个字符，再 3、2、1 个，
        /// 命中即返回记号与消耗长度；都不命中返回 false，由调用方报“无法识别”。
        /// </summary>
        private static bool TryMatchToken(string format, int start, out FormatToken token, out int length)
        {
            token = FormatToken.Literal;
            length = 0;
            // 从长到短尝试：dddd 必须先于 ddd，ddd 必须先于 dd。
            for (int size = 4; size >= 1; size--)
            {
                // 剩余长度不足时跳过该长度，避免 Substring 越界。
                if (start + size > format.Length)
                {
                    continue;
                }
                string candidate = format.Substring(start, size);
                FormatToken found;
                // 命中词典即采用，size 就是该变量占用的字符数。
                if (Tokens.TryGetValue(candidate, out found))
                {
                    token = found;
                    length = size;
                    return true;
                }
            }
            return false;
        }

        /// <summary>把累积的字面量落成 Literal 片段并清空缓冲；缓冲为空时什么也不做。</summary>
        private static void FlushLiteral(List<FormatPart> parts, StringBuilder literal)
        {
            // 空缓冲不产生片段，避免片段列表里出现无意义的空字面量。
            if (literal.Length == 0)
            {
                return;
            }
            parts.Add(new FormatPart { Token = FormatToken.Literal, Literal = literal.ToString() });
            literal.Length = 0;
        }

        /// <summary>判断是否为 ASCII 字母；只有字母才可能是变量，其余字符一律当字面量。</summary>
        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        /// <summary>
        /// 构建变量查找表：键是大小写敏感的变量文本，值是记号。分五批注册：多字符专用词、
        /// 字母缩写、6 天预报、双字符变量、单字符变量；重复键会被后写覆盖，新增变量时注意别撞车。
        /// 词典在静态初始化时建一次，之后只读。
        /// </summary>
        private static Dictionary<string, FormatToken> BuildTokenTable()
        {
            Dictionary<string, FormatToken> table =
                new Dictionary<string, FormatToken>(StringComparer.Ordinal);

            // 第一批：三、四个字符的专用词，必须整体匹配，否则会被拆成多个单字符变量。
            table["yyyy"] = FormatToken.Year4;
            table["YYYY"] = FormatToken.ChineseYear4;
            table["dddd"] = FormatToken.WeekdayLong;
            table["week"] = FormatToken.IsoWeek;
            table["day"] = FormatToken.DayOfYear;
            table["doy"] = FormatToken.DayOfYear;
            table["SSS"] = FormatToken.Millisecond;

            // 第二批：字母缩写（英文月、星期、天气实况、城市、图标），语义上不可再拆分。
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

            // 第三批：6 天预报，we 为天气文字、tl 为最低温、th 为最高温，编号 1–6 与下标对应。
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

            // 第四批：双字符变量，大小写区分含义（NN 干支与 nn 生肖、HH 24 小时与 hh 12 小时）。
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

            // 第五批：单字符变量，最易与字面量混淆，因此放最后兜底。
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
    /// 农历与干支信息：封装 <see cref="ChineseLunisolarCalendar"/>，把公历时间折算成
    /// “正月、闰四月”“初二、廿三”“丙午马年”等中文写法。
    /// 调用方是 <see cref="FormatEngine.AppendToken"/>；农历只支持 1901–2100，越界返回空串而不抛异常。
    /// </summary>
    internal static class ChineseCalendarInfo
    {

        // ===== 日历与名称表 =====

        /// <summary>农历日历实例；只做只读换算，可静态共享。</summary>
        private static readonly ChineseLunisolarCalendar Calendar = new ChineseLunisolarCalendar();

        /// <summary>农历月名，下标 0 对应正月；闰月由调用方另加“闰”字。</summary>
        private static readonly string[] MonthNames = new string[]
        {
            "正月", "二月", "三月", "四月", "五月", "六月",
            "七月", "八月", "九月", "十月", "冬月", "腊月"
        };

        /// <summary>十天干，下标 0 为甲。</summary>
        private static readonly string[] StemNames = new string[]
        {
            "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"
        };

        /// <summary>十二地支，下标 0 为子。</summary>
        private static readonly string[] BranchNames = new string[]
        {
            "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"
        };

        /// <summary>十二生肖，下标与地支一一对应（子鼠、丑牛等）。</summary>
        private static readonly string[] ZodiacNames = new string[]
        {
            "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪"
        };

        /// <summary>年份逐位汉字（〇 到 九），用于“二〇二六”式写法。</summary>
        private static readonly char[] YearDigitsMap = new char[]
        {
            '〇', '一', '二', '三', '四', '五', '六', '七', '八', '九'
        };

        /// <summary>
        /// 把年份逐位转成汉字：如 2026 转成“二〇二六”；负数取绝对值，非数字位跳过。
        /// 只做字符映射，不处理“零”的读法，供两位与四位中文年份变量使用。
        /// </summary>
        public static string YearDigits(int year)
        {
            // 先转成十进制串，再逐位查表映射。
            string digits = Math.Abs(year).ToString(CultureInfo.InvariantCulture);
            StringBuilder builder = new StringBuilder(digits.Length);
            // 逐位映射；查表失败（理论上不会发生）时跳过该位，避免越界。
            for (int i = 0; i < digits.Length; i++)
            {
                int value = digits[i] - '0';
                // 只接受 0 到 9 的位，非法字符直接忽略。
                if (value >= 0 && value < YearDigitsMap.Length)
                {
                    builder.Append(YearDigitsMap[value]);
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// 取农历月名：处理闰月（返回“闰X月”），并抵消 .NET 把闰月算进月序造成的偏移。
        /// 日期超出农历支持范围（ArgumentOutOfRangeException）时返回空串，不把异常抛给调用方。
        /// </summary>
        public static string LunarMonthName(DateTime time)
        {
            // 农历换算可能因日期越界抛 ArgumentOutOfRangeException，统一在这里吞掉并输出空。
            try
            {
                int year = Calendar.GetYear(time);
                int month = Calendar.GetMonth(time);
                int leapMonth = Calendar.GetLeapMonth(year);
                bool leap = false;
                int realMonth = month;

                // 有闰月时：等于闰月序号即闰月本身，之后的普通月份序号要整体前移一位。
                if (leapMonth > 0)
                {
                    // 正好落在闰月：标记闰月并把序号还原为前一个月。
                    if (month == leapMonth)
                    {
                        leap = true;
                        realMonth = leapMonth - 1;
                    }
                    // 闰月之后的月份：序号减 1 还原真实月序。
                    else if (month > leapMonth)
                    {
                        realMonth = month - 1;
                    }
                }

                // 月序越界说明换算异常，返回空串而不是抛异常。
                if (realMonth < 1 || realMonth > 12)
                {
                    return string.Empty;
                }

                // 闰月加“闰”前缀，再查月名表。
                return (leap ? "闰" : string.Empty) + MonthNames[realMonth - 1];
            }
            // 农历只支持 1901–2100，越界日期按无数据返回空串。
            catch (ArgumentOutOfRangeException)
            {
                return string.Empty;
            }
        }

        /// <summary>取农历日名：1–30 折算为“初一…初十、十一…二十、廿一…三十”；越界返回空串。</summary>
        public static string LunarDayName(DateTime time)
        {
            // 换算交给 DayName 处理，异常同样按“无数据”处理。
            try
            {
                int day = Calendar.GetDayOfMonth(time);
                return DayName(day);
            }
            // 农历只支持 1901–2100，越界日期按无数据返回空串。
            catch (ArgumentOutOfRangeException)
            {
                return string.Empty;
            }
        }

        /// <summary>取生肖年，如“马年”；地支取不到时返回空串。</summary>
        public static string ZodiacName(DateTime time)
        {
            int branch;
            // 地支越界或农历不支持时 TryGetBranch 返回 false，这里直接给空串。
            if (!TryGetBranch(time, out branch))
            {
                return string.Empty;
            }
            return ZodiacNames[branch - 1] + "年";
        }

        /// <summary>
        /// 取干支生肖：“天干 + 地支 + 生肖 + 年”，如“丙午马年”。
        /// 三个序号任一越界，或农历换算抛异常，都返回空串。
        /// </summary>
        public static string GanzhiZodiacName(DateTime time)
        {
            // 农历换算可能因日期越界抛异常，统一按“无数据”返回空串。
            try
            {
                int sexagenary = Calendar.GetSexagenaryYear(time);
                int stem = Calendar.GetCelestialStem(sexagenary);
                int branch = Calendar.GetTerrestrialBranch(sexagenary);
                // 序号越界（理论上不会）时放弃输出，避免下标越界异常。
                if (stem < 1 || stem > 10 || branch < 1 || branch > 12)
                {
                    return string.Empty;
                }
                // 拼成“天干地支生肖年”，例如“丙午马年”。
                return StemNames[stem - 1] + BranchNames[branch - 1] + ZodiacNames[branch - 1] + "年";
            }
            // 农历只支持 1901–2100，越界日期按无数据返回空串。
            catch (ArgumentOutOfRangeException)
            {
                return string.Empty;
            }
        }

        /// <summary>求年份对应地支序号（1–12）；越界或农历不支持时返回 false。</summary>
        private static bool TryGetBranch(DateTime time, out int branch)
        {
            // branch 先置 0，异常路径下调用方也不会读到未赋值的值。
            branch = 0;
            // 农历换算可能因日期越界抛异常，按失败返回。
            try
            {
                int sexagenary = Calendar.GetSexagenaryYear(time);
                branch = Calendar.GetTerrestrialBranch(sexagenary);
                // 地支 1–12 才有效，其余一律视为失败。
                return branch >= 1 && branch <= 12;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        /// <summary>
        /// 农历日转中文：1–10 用“初X、初十”，11–19 用“十X”，20 用“二十”，
        /// 21–29 用“廿X”，30 用“三十”；越界返回空串。
        /// </summary>
        private static string DayName(int day)
        {
            // 超出 1–30 说明输入非法，返回空串。
            if (day < 1 || day > 30)
            {
                return string.Empty;
            }
            // 上旬：初一…初九，初十单独成词。
            if (day <= 10)
            {
                return day == 10 ? "初十" : "初" + Digit(day);
            }
            // 中旬：十一…十九。
            if (day < 20)
            {
                return "十" + Digit(day - 10);
            }
            // 二十单独成词，不拼成“二廿”。
            if (day == 20)
            {
                return "二十";
            }
            // 下旬：廿一…廿九。
            if (day < 30)
            {
                return "廿" + Digit(day - 20);
            }
            // 只剩三十，农历月最多 30 天。
            return "三十";
        }

        /// <summary>1–9 转汉字数字；越界返回空串，供日名拼接使用。</summary>
        private static string Digit(int value)
        {
            // 数字表按需新建，只在拼日名时调用，开销可忽略。
            string[] digits = new string[] { "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            // 越界返回空串，拼接时会自然少一位而不是抛异常。
            if (value < 1 || value > 9)
            {
                return string.Empty;
            }
            return digits[value - 1];
        }
    }
}
