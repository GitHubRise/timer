using System;
using System.Collections.Generic;
using System.Globalization;

// .NET 格式串的时间格式化：校验、归一化、渲染，以及「时间点」文本解析（用于提醒时间输入）。
// 调用方：设置页（校验/预设列表）、ClockWindow 与 TaskbarClockWindow（每秒渲染）、Reminder（解析 HH:mm）。
// 边界：非法输入一律回退 <see cref="TimeFormatter.DefaultFormat"/>，绝不抛异常给渲染路径。
// 本文件包含每秒调用的 <see cref="TimeFormatter.Format"/>，因此**纯渲染路径不得写日志**；
// 只有「用户编辑格式/输入时间」这类低频路径才允许 Warn。所有格式串按当前文化渲染（中文系统即中文星期）。
namespace GameClock
{
    /// <summary>
    /// .NET 格式串的校验与渲染工具；另提供 ISO 周数、HH:mm 解析和常用预设列表。
    /// </summary>
    internal static class TimeFormatter
    {
        // 回退格式：任何校验失败的格式最终都渲染成 HH:mm:ss，保证悬浮窗永远不会显示空白。
        public const string DefaultFormat = "HH:mm:ss";

        // 与 FormatEngine 保持一致的上限：单行 120 字符、最多两行（超长会撑出悬浮窗可用区域）。
        public const int MaxFormatLength = 120;
        public const int MaxLines = 2;

        /// <summary>
        /// 校验并归一化 .NET 格式串：统一换行、把字面量 \n 转成换行、SSS 当作毫秒别名替换为 fff，
        /// 按行去空格并限制行数/长度，最后用固定样例时间试渲染一次以捕获非法占位符。
        /// </summary>
        /// <param name="input">用户输入的原始格式串。</param>
        /// <param name="normalized">成功时输出归一化结果，失败时为 null。</param>
        /// <param name="error">失败时输出可直接展示的中文原因。</param>
        public static bool TryNormalizeFormat(string input, out string normalized, out string error)
        {
            normalized = null;
            error = null;

            // 空格式在 .NET 里等价于默认短日期格式，但用户本意通常是「还没填」，直接拒绝更清晰。
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "时间格式不能为空。";
                return false;
            }

            // 统一换行符并把字面量 \n 转成真实换行：配置里存的是真实换行，用户可能敲的是两个字符。
            string format = input.Replace("\r\n", "\n").Replace('\r', '\n');
            format = format.Replace("\\n", "\n");
            // SSS 是简洁变量方言里的毫秒写法，这里统一别名成 .NET 的 fff，避免用户在两套格式间照抄出错。
            format = format.Replace("SSS", "fff");

            string[] lines = format.Split('\n');
            // 超过两行会顶掉任务栏/悬浮窗的布局空间，直接拒绝而不是截断。
            if (lines.Length > MaxLines)
            {
                error = "最多只支持一个换行（两行文字）。";
                return false;
            }

            // 逐行去首尾空格并判空：换行两侧都是空白行时，界面上看起来就是「什么都没显示」。
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Trim();
                if (lines[i].Length == 0)
                {
                    error = "换行两侧都需要填写内容。";
                    return false;
                }
            }

            format = string.Join("\n", lines);
            // 长度上限在拼回单串后判断，与最终落盘的字符串口径一致。
            if (format.Length > MaxFormatLength)
            {
                error = "格式太长，请控制在 " + MaxFormatLength + " 个字符以内。";
                return false;
            }

            // 试渲染：用固定样例（2026-10-26 23:45:12.345，周一，当年第 44 周）走一遍真实渲染路径，
            // 这样非法占位符/未闭合引号会在保存时就被拦下，而不是每秒渲染时才发现。
            try
            {
                DateTime sample = new DateTime(2026, 10, 26, 23, 45, 12, 345);
                string probe = ToRenderableFormat(
                    format.Replace("WW", GetIsoWeek(sample).ToString("00", CultureInfo.InvariantCulture)));
                sample.ToString(probe, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                // 只捕获 FormatException：这是 .NET 对非法格式串的约定异常，其它异常继续上抛。
                // 此处不记日志——本方法虽由设置页调用，但 Format() 会经 NormalizeFormat 反复进入，
                // 逐次记录会退化成每秒一条日志；用户已经能从 error 文本看到原因。
                error = "格式无效，请检查日期占位符或引号是否完整。";
                return false;
            }

            normalized = format;
            return true;
        }

        /// <summary>
        /// 宽松归一化：校验失败回退 <see cref="DefaultFormat"/>，供渲染路径与配置载入使用。
        /// 这是每秒渲染会走到的路径（Format 先调本方法），所以**失败时也不记日志**。
        /// </summary>
        public static string NormalizeFormat(string input)
        {
            string normalized;
            string error;
            if (TryNormalizeFormat(input, out normalized, out error))
            {
                return normalized;
            }
            // 坏格式回退默认值：每秒渲染都会走这里，静默回退即可；用户侧的错误提示由设置页负责。
            return DefaultFormat;
        }

        /// <summary>
        /// 渲染时间；格式非法或渲染抛 <see cref="FormatException"/> 时回退默认格式渲染。
        /// </summary>
        /// <remarks>悬浮窗与任务栏每秒各调用一次，**严禁在此记录任何日志**。</remarks>
        public static string Format(DateTime time, string format)
        {
            string normalized = NormalizeFormat(format);
            // WW 是本项目扩展的「ISO 周数」写法，.NET 没有对应占位符，先替换成两位数字面量再渲染。
            string week = GetIsoWeek(time).ToString("00", CultureInfo.InvariantCulture);
            string prepared = ToRenderableFormat(normalized.Replace("WW", week));
            try
            {
                return time.ToString(prepared, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                // 理论上 NormalizeFormat 已校验过；但 CurrentCulture 变化（用户改系统区域）仍可能让格式失效，
                // 此时回退默认格式，保证时钟继续走字而不是整块空白。
                return time.ToString(DefaultFormat, CultureInfo.CurrentCulture);
            }
        }

        /// <summary>
        /// 单字符自定义格式需要加 %：.NET 会把 "M"/"d"/"s" 等单独解释成标准格式（月日/短日期/可排序格式），
        /// 加前缀后才按文档里的单字符自定义占位符处理。
        /// </summary>
        private static string ToRenderableFormat(string format)
        {
            // 只处理会被 .NET 误判为标准格式的单字符（M/d/H/h/m/s/f），其它字符不受影响。
            if (format.Length == 1 && "MdHhmsf".IndexOf(format[0]) >= 0)
            {
                return "%" + format;
            }
            return format;
        }

        /// <summary>统计换行符个数（\n 与 \r 都算），调用方据此把超出行数的文本截断显示。</summary>
        public static int CountNewlines(string text)
        {
            // 空文本没有换行，直接返回 0，省掉遍历。
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            int count = 0;
            // 逐个字符统计：\r\n 会算成两个，这里的用途是「是否超行」的粗判断，不需要精确到行。
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n' || text[i] == '\r')
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 把用户输入的时间文本解析成规范化的 HH:mm；支持 H:mm / HH:mm，并把中文全角冒号当半角处理。
        /// 失败返回 false（调用方据此在编辑框上提示），不抛异常。
        /// </summary>
        public static bool TryNormalizeTime(string input, out string normalized)
        {
            normalized = null;
            // 空输入不算错误也算「没填」，返回 false 让调用方保持原值。
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            // 全角冒号替换成半角：中文输入法下用户常打出「08：30」，直接 TryParseExact 会失败。
            string text = input.Trim().Replace('：', ':');
            string[] formats = new string[] { "H:mm", "HH:mm" };
            DateTime parsed;
            // 用不变文化 + 精确格式解析，避免跟随系统区域把「8:3」之类宽松写法也吃进来。
            if (DateTime.TryParseExact(
                text,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed))
            {
                normalized = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
                return true;
            }
            // 用户手动填写的提醒时间无法解析：属低频编辑路径，记一条 Warn 便于排查「保存后时间被重置」的反馈。
            AppLog.Warn("Format", "时间文本无法解析，已忽略（应为 H:mm 或 HH:mm）；原始输入：" + text);
            return false;
        }

        /// <summary>
        /// 计算 ISO-8601 周数（周一为一周首日、含 1 月 4 日的那周为第 1 周）。
        /// 标准库只提供 CalendarWeekRule.FirstFourDayWeek，需要把周一至周三的日期 +3 天再取周号才能对齐 ISO。
        /// </summary>
        public static int GetIsoWeek(DateTime date)
        {
            DayOfWeek day = CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(date);
            // 周一至周三可能属于「下一周」：先挪到本周四（ISO 约定以周四所在周定年周），再取周号。
            if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday)
            {
                date = date.AddDays(3);
            }
            // FirstFourDayWeek + Monday 首日即 ISO 口径；用不变文化保证与系统区域设置无关。
            return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                date,
                CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday);
        }

        /// <summary>
        /// 生成 .NET 格式的常用预设；示例用传入样例时间渲染，换行在预览里显示成「 / 」。
        /// </summary>
        public static List<TimeFormatPreset> CreatePresets(DateTime sample)
        {
            List<TimeFormatPreset> list = new List<TimeFormatPreset>();
            // 纯时间：默认 24 小时、12 小时制、带毫秒。
            Add(list, "时间（24 小时，默认）", "HH:mm:ss", sample);
            Add(list, "时间（12 小时 + 上下午）", "hh:mm:ss tt", sample);
            Add(list, "时间 + 毫秒", "HH:mm:ss.fff", sample);

            // 日期 + 时间组合：从纯日期到中文星期、两位年份、斜杠日期。
            Add(list, "仅日期", "yyyy-MM-dd", sample);
            Add(list, "日期 + 时间", "yyyy-MM-dd HH:mm:ss", sample);
            Add(list, "日期 + 时间 + 星期", "yyyy-MM-dd HH:mm:ss dddd", sample);
            Add(list, "中文日期 + 时间", "yyyy年M月d日 HH:mm:ss", sample);
            Add(list, "中文日期 + 星期 + 时间", "yyyy年M月d日 dddd HH:mm:ss", sample);
            Add(list, "短日期 + 时间", "MM-dd HH:mm", sample);
            Add(list, "短日期 + 时间 + 秒", "MM-dd HH:mm:ss", sample);
            Add(list, "两位年份 + 时间", "yy-MM-dd HH:mm:ss", sample);
            Add(list, "斜杠日期 + 时间", "yyyy/MM/dd HH:mm:ss", sample);
            Add(list, "日期 + 12 小时时间", "yyyy-MM-dd hh:mm:ss tt", sample);
            Add(list, "完整时间戳 + 毫秒", "yyyy-MM-dd HH:mm:ss.fff", sample);

            // 非时间主导的组合：只显示日期、时间 + 星期，以及项目扩展的第几周。
            Add(list, "日期 + 星期", "yyyy-MM-dd dddd", sample);
            Add(list, "时间 + 星期", "HH:mm:ss dddd", sample);
            Add(list, "年 + 第几周", "yyyy 第WW周", sample);

            // 两行组合：任务栏时钟的第二行常用于放日期/星期，格式里用 \n 分隔。
            Add(list, "两行：日期 / 时间", "yyyy-MM-dd\nHH:mm:ss", sample);
            Add(list, "两行：时间 / 日期+星期", "HH:mm:ss\nyyyy-MM-dd dddd", sample);
            Add(list, "两行：中文日期 / 时间", "yyyy年M月d日\nHH:mm:ss", sample);
            return list;
        }

        /// <summary>追加一个预设；预览里的换行替换成「 / 」，否则列表项只会显示第一行。</summary>
        private static void Add(List<TimeFormatPreset> list, string name, string format, DateTime sample)
        {
            string example = Format(sample, format).Replace("\n", " / ");
            list.Add(new TimeFormatPreset(name, format, example));
        }
    }

    /// <summary>
    /// .NET 格式串的预设项：名称、格式串、示例预览；<see cref="Display"/> 供下拉列表显示。
    /// </summary>
    internal sealed class TimeFormatPreset
    {
        /// <summary>构造一个预设；example 为按样例时间渲染出的预览文本。</summary>
        public TimeFormatPreset(string name, string format, string example)
        {
            Name = name;
            Format = format;
            Example = example;
        }

        /// <summary>预设名称（中文），如「日期 + 时间 + 星期」。</summary>
        public string Name { get; private set; }

        /// <summary>落盘使用的 .NET 格式串。</summary>
        public string Format { get; private set; }

        /// <summary>样例渲染结果，用于用户在应用前判断效果。</summary>
        public string Example { get; private set; }

        /// <summary>下拉项显示文本：名称 + 中间点 + 示例，让用户一眼看到渲染效果。</summary>
        public string Display
        {
            get { return Name + "   ·   " + Example; }
        }

        /// <summary>ComboBox 默认调用 ToString 显示，直接转给 <see cref="Display"/>，避免出现类型名。</summary>
        public override string ToString()
        {
            return Display;
        }
    }
}
