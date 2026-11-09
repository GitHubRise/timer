// 时间格式的归一化、校验与渲染工具。
// 供设置窗口预览、悬浮窗与任务栏时钟共用；输入是用户填写的 .NET 自定义格式。
// 规则：最多两行（一个换行）、总长 120 字符；"\n" 视为换行，"SSS" 等价 "fff"，
// "WW" 在渲染前替换为 ISO-8601 周数；单字符格式补 "%" 避免被当成标准模式。
// 非法格式不抛异常：Try 版本返回错误文案，非 Try 版本回退 DefaultFormat。
// 纯函数，不读配置、不缓存，可安全地在 UI 线程与后台线程调用。

using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameClock
{

    /// <summary>
    /// 时间格式的归一化与渲染工具：校验用户输入、展开自定义占位符，
    /// 并把结果交给 <see cref="DateTime.ToString(string, IFormatProvider)"/>。
    /// 无状态，所有方法都可重复调用。
    /// </summary>
    internal static class TimeFormatter
    {
        // ===== 常量 =====

        /// <summary>格式非法或为空时使用的默认格式。</summary>
        public const string DefaultFormat = "HH:mm:ss";

        /// <summary>格式字符串最大长度，防止超长输入拖慢预览与渲染。</summary>
        public const int MaxFormatLength = 120;

        /// <summary>最多允许的行数；一个换行即两行，与悬浮窗两行布局对应。</summary>
        public const int MaxLines = 2;

        // ===== 格式校验 =====

        /// <summary>
        /// 校验并归一化用户输入的格式：统一换行、展开转义、限制行数与长度，
        /// 并用固定示例时间试渲染，提前发现非法占位符。
        /// </summary>
        /// <param name="input">用户输入的原始格式，可包含字面量 <c>\n</c>。</param>
        /// <param name="normalized">成功时的规范格式；失败时为 null。</param>
        /// <param name="error">失败时的中文错误提示；成功时为 null。</param>
        /// <returns>格式可用返回 true，否则返回 false。</returns>
        public static bool TryNormalizeFormat(string input, out string normalized, out string error)
        {
            normalized = null;
            error = null;

            // 空格式没有可渲染内容，直接给出明确提示。
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "时间格式不能为空。";
                return false;
            }

            // 先把不同平台的换行统一成 \n，后面只按一种分隔符分行。
            string format = input.Replace("\r\n", "\n").Replace('\r', '\n');
            // 用户在输入框里键入的是两个字符“\n”，这里转成真正的换行符。
            format = format.Replace("\\n", "\n");
            // 兼容说明表里的 SSS 写法，内部统一按 .NET 的毫秒占位符 fff 处理。
            format = format.Replace("SSS", "fff");

            // 先按换行切分，保留用户填写的每一行。
            string[] lines = format.Split('\n');
            // 超过两行时悬浮窗放不下，直接拒绝而不是截断。
            if (lines.Length > MaxLines)
            {
                error = "最多只支持一个换行（两行文字）。";
                return false;
            }

            // 逐行去掉首尾空白，并拒绝空行，避免出现只有一行的“假两行”。
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Trim();
                // 去掉空白后为空，说明换行这一侧没有填写内容。
                if (lines[i].Length == 0)
                {
                    error = "换行两侧都需要填写内容。";
                    return false;
                }
            }

            format = string.Join("\n", lines);
            // 长度限制放在归一化之后，按实际生效的格式计算。
            if (format.Length > MaxFormatLength)
            {
                error = "格式太长，请控制在 " + MaxFormatLength + " 个字符以内。";
                return false;
            }

            // 试渲染只做语法检查，不关心输出内容。
            try
            {
                // 固定样例让校验结果稳定，不随当前时间变化。
                DateTime sample = new DateTime(2026, 10, 26, 23, 45, 12, 345);
                // WW 不是 .NET 原生占位符，先替换成真实周数再交给 DateTime 校验。
                string probe = ToRenderableFormat(
                    format.Replace("WW", GetIsoWeek(sample).ToString("00", CultureInfo.InvariantCulture)));
                sample.ToString(probe, CultureInfo.CurrentCulture);
            }
            // 试渲染失败只说明格式语法有问题，转成用户可读的提示而不是抛出。
            catch (FormatException)
            {
                error = "格式无效，请检查日期占位符或引号是否完整。";
                return false;
            }

            normalized = format;
            return true;
        }

        /// <summary>
        /// 归一化格式；校验失败时回退 <see cref="DefaultFormat"/>，供不需要错误提示的调用方使用。
        /// </summary>
        public static string NormalizeFormat(string input)
        {
            string normalized;
            string error;
            // 校验通过就用规范格式，否则回退默认格式。
            if (TryNormalizeFormat(input, out normalized, out error))
            {
                return normalized;
            }
            return DefaultFormat;
        }

        // ===== 渲染 =====

        /// <summary>
        /// 按指定格式渲染时间：先归一化，再替换 WW 为 ISO 周数、补单字符前缀，
        /// 最后用当前区域设置输出。渲染异常时回退默认格式。
        /// </summary>
        public static string Format(DateTime time, string format)
        {
            string normalized = NormalizeFormat(format);
            // WW 不是 .NET 占位符，按目标时间计算 ISO 周数后替换。
            string week = GetIsoWeek(time).ToString("00", CultureInfo.InvariantCulture);
            string prepared = ToRenderableFormat(normalized.Replace("WW", week));
            // 渲染失败时回退默认格式，保证调用方总能拿到字符串。
            try
            {
                return time.ToString(prepared, CultureInfo.CurrentCulture);
            }
            // 理论上归一化已排除非法格式；这里仍兜底，防止区域规则引发异常。
            catch (FormatException)
            {
                return time.ToString(DefaultFormat, CultureInfo.CurrentCulture);
            }
        }

        /// <summary>
        /// 把单字符自定义格式补成 <c>%M</c> 形式，避免 .NET 将其解释为标准日期模式。
        /// </summary>
        private static string ToRenderableFormat(string format)
        {
            // 单字符格式会被 .NET 当成标准模式（"M" = 月/日，"d" = 短日期，
            // "s" = 可排序格式）。补 "%" 前缀后才会按文档中的单字符自定义说明符解析。
            if (format.Length == 1 && "MdHhmsf".IndexOf(format[0]) >= 0)
            {
                return "%" + format;
            }
            return format;
        }

        /// <summary>
        /// 统计文本中的换行符数量；CR 与 LF 分别计数，因此 CRLF 会记两次。
        /// 调用方只用它判断是否包含换行，不做精确行数计算。
        /// </summary>
        public static int CountNewlines(string text)
        {
            // 空文本没有换行可数，直接返回 0。
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            int count = 0;
            // 逐字符扫描，任何换行写法都计入。
            for (int i = 0; i < text.Length; i++)
            {
                // 同时兼容 \n、\r 与 \r\n，避免不同来源的文本漏判。
                if (text[i] == '\n' || text[i] == '\r')
                {
                    count++;
                }
            }
            return count;
        }

        // ===== 时间输入校验 =====

        /// <summary>
        /// 校验“时:分”输入并归一化为 <c>HH:mm</c>；接受一位或两位小时。
        /// 空值或格式不符返回 false，不抛异常。
        /// </summary>
        public static bool TryNormalizeTime(string input, out string normalized)
        {
            normalized = null;
            // 空输入无法解析成时间，直接返回失败。
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            // 兼容中文输入法打出的全角冒号，并去掉首尾空白。
            string text = input.Trim().Replace('：', ':');
            string[] formats = new string[] { "H:mm", "HH:mm" };
            DateTime parsed;
            // 只接受 24 小时制的“时:分”，避免把日期或其他文本误判为时间。
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
            return false;
        }

        // ===== ISO 周数 =====

        /// <summary>
        /// 计算 ISO-8601 周数：周一为一周起点，包含当年第一个周四的那周为第 1 周。
        /// </summary>
        public static int GetIsoWeek(DateTime date)
        {
            DayOfWeek day = CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(date);
            // 周一到周三可能属于上一年的最后一周，先推到本周四再取周数。
            if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday)
            {
                date = date.AddDays(3);
            }
            return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                date,
                CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday);
        }

        // ===== 预设格式 =====

        /// <summary>
        /// 创建设置窗口的常用格式预设：每条包含名称、格式串与按示例时间渲染的预览。
        /// 预览中的换行替换为“ / ”，保证下拉列表保持单行。
        /// </summary>
        public static List<TimeFormatPreset> CreatePresets(DateTime sample)
        {
            List<TimeFormatPreset> list = new List<TimeFormatPreset>();

            // ===== 纯时间 =====
            Add(list, "时间（24 小时，默认）", "HH:mm:ss", sample);
            Add(list, "时间（12 小时 + 上下午）", "hh:mm:ss tt", sample);
            Add(list, "时间 + 毫秒", "HH:mm:ss.fff", sample);

            // ===== 日期与完整时间戳 =====
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

            // ===== 日期或时间搭配星期、周数 =====
            Add(list, "日期 + 星期", "yyyy-MM-dd dddd", sample);
            Add(list, "时间 + 星期", "HH:mm:ss dddd", sample);
            Add(list, "年 + 第几周", "yyyy 第WW周", sample);

            // ===== 两行布局 =====
            Add(list, "两行：日期 / 时间", "yyyy-MM-dd\nHH:mm:ss", sample);
            Add(list, "两行：时间 / 日期+星期", "HH:mm:ss\nyyyy-MM-dd dddd", sample);
            Add(list, "两行：中文日期 / 时间", "yyyy年M月d日\nHH:mm:ss", sample);
            return list;
        }

        /// <summary>
        /// 追加一条预设：格式串原样保存，预览里把换行折叠成单行显示的“ / ”。
        /// </summary>
        private static void Add(List<TimeFormatPreset> list, string name, string format, DateTime sample)
        {
            string example = Format(sample, format).Replace("\n", " / ");
            list.Add(new TimeFormatPreset(name, format, example));
        }
    }

    /// <summary>
    /// 常用格式预设：名称、可编辑的格式串与渲染后的示例。
    /// 生命周期：每次打开设置窗口时重新创建，不持久化。
    /// </summary>
    internal sealed class TimeFormatPreset
    {

        /// <summary>保存预设名称、格式串与示例文本。</summary>
        public TimeFormatPreset(string name, string format, string example)
        {
            Name = name;
            Format = format;
            Example = example;
        }

        /// <summary>下拉列表显示的名称。</summary>
        public string Name { get; private set; }

        /// <summary>选中后写入设置的真实格式串。</summary>
        public string Format { get; private set; }

        /// <summary>按示例时间渲染出的预览文本，换行已折叠。</summary>
        public string Example { get; private set; }

        /// <summary>下拉行显示文本：名称与示例用分隔符隔开。</summary>
        public string Display
        {
            get { return Name + "   ·   " + Example; }
        }

        /// <summary>让 WPF 下拉框直接显示名称与示例，而不是类型名。</summary>
        public override string ToString()
        {
            return Display;
        }
    }
}
