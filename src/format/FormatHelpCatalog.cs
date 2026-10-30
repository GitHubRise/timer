using System;
using System.Collections.Generic;

// 格式变量帮助目录：给设置页「变量说明」列表提供「变量名 / 说明 / 示例预览」三列数据。
// 调用方是设置窗口，每次打开设置页或切换格式模式时重建一次列表；不缓存、不订阅事件。
// 示例预览用传入的 sample 时间与 weather 当样例渲染，因此这里的天气变量显示的是真实天气或假数据，与时钟无关。
// 纯数据组装，不读写配置、不发请求，也不需要日志：重建失败等价于设置页打不开，属于上层职责。
// 已知边界：.NET 与简洁变量两套语法分别由 CreateDotNet / CreateToken 生成，示例必须走各自引擎渲染。
namespace GameClock
{
    /// <summary>
    /// 帮助列表的一行：变量名、中文说明、示例预览；<see cref="SearchText"/> 供搜索框过滤，<see cref="DisplayText"/> 供列表显示。
    /// </summary>
    internal sealed class FormatHelpItem
    {
        /// <summary>构造一行帮助数据；example 允许为空串，由 <see cref="FormatHelpCatalog"/> 统一兜底。</summary>
        public FormatHelpItem(string token, string description, string example)
        {
            Token = token;
            Description = description;
            Example = example;
        }

        /// <summary>变量原文，如 yyyy、we1、MM。</summary>
        public string Token { get; private set; }

        /// <summary>中文说明，如「四位年份」。</summary>
        public string Description { get; private set; }

        /// <summary>用样例时间渲染出的预览值，如 2026。</summary>
        public string Example { get; private set; }

        /// <summary>搜索匹配用的拼接文本（变量 + 说明 + 预览），包含三者任意片段都能命中。</summary>
        public string SearchText
        {
            get { return Token + " " + Description + " " + Example; }
        }

        /// <summary>列表项显示文本：变量名 + 说明 + 示例预览，用中文标点分隔。</summary>
        public string DisplayText
        {
            get { return Token + "：" + Description + "。示例预览：" + Example; }
        }
    }

    /// <summary>
    /// 按当前格式模式生成帮助条目列表；只做组装与渲染示例，不修改任何状态。
    /// </summary>
    internal static class FormatHelpCatalog
    {
        /// <summary>按模式分派：简洁变量走 token 引擎，其它（含未知值）走 .NET 格式串引擎。</summary>
        public static List<FormatHelpItem> Create(string mode, DateTime sample, WeatherInfo weather)
        {
            // 判断复用 FormatEngine.IsTokenMode，保证与时钟渲染的模式判定完全一致（含大小写兼容）。
            return FormatEngine.IsTokenMode(mode)
                ? CreateToken(sample, weather)
                : CreateDotNet(sample);
        }

        /// <summary>
        /// 生成 .NET 格式串的帮助项；示例直接调用 <see cref="TimeFormatter.Format"/>，确保预览与实际渲染同源。
        /// </summary>
        private static List<FormatHelpItem> CreateDotNet(DateTime sample)
        {
            List<FormatHelpItem> list = new List<FormatHelpItem>();
            // 日期组：年、月、日、星期的 .NET 占位符。
            Add(list, "yyyy", "四位年份", TimeFormatter.Format(sample, "yyyy"));
            Add(list, "yy", "两位年份", TimeFormatter.Format(sample, "yy"));
            Add(list, "MM", "月份（两位）", TimeFormatter.Format(sample, "MM"));
            Add(list, "M", "月份（不补零）", TimeFormatter.Format(sample, "M"));
            Add(list, "dd", "日期（两位）", TimeFormatter.Format(sample, "dd"));
            Add(list, "d", "日期（不补零）", TimeFormatter.Format(sample, "d"));
            Add(list, "ddd", "星期（短）", TimeFormatter.Format(sample, "ddd"));
            Add(list, "dddd", "星期（长）", TimeFormatter.Format(sample, "dddd"));
            // 时间组：24/12 小时制、分钟、秒、毫秒。
            Add(list, "HH", "24 小时（两位）", TimeFormatter.Format(sample, "HH"));
            Add(list, "H", "24 小时（不补零）", TimeFormatter.Format(sample, "H"));
            Add(list, "hh", "12 小时（两位）", TimeFormatter.Format(sample, "hh"));
            Add(list, "h", "12 小时（不补零）", TimeFormatter.Format(sample, "h"));
            Add(list, "mm", "分钟（两位）", TimeFormatter.Format(sample, "mm"));
            Add(list, "m", "分钟（不补零）", TimeFormatter.Format(sample, "m"));
            Add(list, "ss", "秒（两位）", TimeFormatter.Format(sample, "ss"));
            Add(list, "s", "秒（不补零）", TimeFormatter.Format(sample, "s"));
            Add(list, "fff", "毫秒（三位）", TimeFormatter.Format(sample, "fff"));
            Add(list, "SSS", "毫秒（等同 fff）", TimeFormatter.Format(sample, "SSS"));
            // 上下午与扩展：tt 受当前文化影响；WW 是本项目对 ISO 周数的扩展写法；zzz 为时区偏移。
            Add(list, "tt", "上午/下午", TimeFormatter.Format(sample, "tt"));
            Add(list, "WW", "ISO 周数（两位）", TimeFormatter.Format(sample, "WW"));
            Add(list, "zzz", "时区偏移", TimeFormatter.Format(sample, "zzz"));
            // 转义与字面量：示例列直接给文字，不参与渲染（\\n 渲染结果就是换行，无法在单行列表里展示）。
            Add(list, "\\n", "换行", "换行");
            Add(list, "'文字'", "原样输出", "文字");
            return list;
        }

        /// <summary>
        /// 生成简洁变量模式的帮助项；每组前空一行，让设置页列表按「年/月日/时间/星期/天气」分组可读。
        /// </summary>
        private static List<FormatHelpItem> CreateToken(DateTime sample, WeatherInfo weather)
        {
            List<FormatHelpItem> list = new List<FormatHelpItem>();
            // 年份组：公历数字年与中文数字年、生肖、干支。
            Add(list, "yy", "两位年份", Token(sample, weather, "yy"));
            Add(list, "yyyy", "四位年份", Token(sample, weather, "yyyy"));
            Add(list, "YY", "中文数字两位年", Token(sample, weather, "YY"));
            Add(list, "YYYY", "中文数字四位年", Token(sample, weather, "YYYY"));
            Add(list, "nn", "农历生肖年", Token(sample, weather, "nn"));
            Add(list, "NN", "农历干支+生肖年", Token(sample, weather, "NN"));

            // 月日组：小写 m/d 是公历，大写 M/D 是农历（与 .NET 语义相反，帮助项说明里刻意写明）。
            Add(list, "m", "公历月（不补零）", Token(sample, weather, "m"));
            Add(list, "mm", "公历月（两位）", Token(sample, weather, "mm"));
            Add(list, "M", "农历月", Token(sample, weather, "M"));
            Add(list, "d", "公历日（不补零）", Token(sample, weather, "d"));
            Add(list, "dd", "公历日（两位）", Token(sample, weather, "dd"));
            Add(list, "D", "农历日", Token(sample, weather, "D"));

            // 时间组：12/24 小时制；f/ff 是分钟、SSS 是毫秒（本方言特有，容易与 .NET 混淆）。
            Add(list, "h", "12 小时（不补零）", Token(sample, weather, "h"));
            Add(list, "hh", "12 小时（两位）", Token(sample, weather, "hh"));
            Add(list, "H", "24 小时（不补零）", Token(sample, weather, "H"));
            Add(list, "HH", "24 小时（两位）", Token(sample, weather, "HH"));
            Add(list, "f", "分钟（不补零）", Token(sample, weather, "f"));
            Add(list, "ff", "分钟（两位）", Token(sample, weather, "ff"));
            Add(list, "s", "秒（不补零）", Token(sample, weather, "s"));
            Add(list, "ss", "秒（两位）", Token(sample, weather, "ss"));
            Add(list, "SSS", "毫秒（三位）", Token(sample, weather, "SSS"));

            // 星期与英文/中文扩展组：英文名称固定不变文化，星期/ISO 周带中文前后缀。
            Add(list, "ddd", "星期（短）", Token(sample, weather, "ddd"));
            Add(list, "dddd", "星期（长）", Token(sample, weather, "dddd"));
            Add(list, "EM3", "英文月份（缩写）", Token(sample, weather, "EM3"));
            Add(list, "EM", "英文月份（全称）", Token(sample, weather, "EM"));
            Add(list, "EW3", "英文星期（缩写）", Token(sample, weather, "EW3"));
            Add(list, "EW", "英文星期（全称）", Token(sample, weather, "EW"));
            Add(list, "week", "ISO 周数（含“第/周”）", Token(sample, weather, "week"));
            Add(list, "day", "年内第几天（含“第/天”）", Token(sample, weather, "day"));
            Add(list, "tt", "英文上午/下午", Token(sample, weather, "tt"));
            Add(list, "TT", "中文上午/下午", Token(sample, weather, "TT"));

            // 六天预报：we/tl/th 各 6 条，用循环生成避免手写 18 行重复代码。
            for (int i = 1; i <= 6; i++)
            {
                Add(list, "we" + i, "第 " + i + " 天天气", Token(sample, weather, "we" + i));
            }
            // 最低温系列：数据源缺某天温度时预览为空串，帮助项仍然列出（说明变量本身合法）。
            for (int i = 1; i <= 6; i++)
            {
                Add(list, "tl" + i, "第 " + i + " 天最低温", Token(sample, weather, "tl" + i));
            }
            // 最高温系列：与 tl 对称，数字 1 表示今天。
            for (int i = 1; i <= 6; i++)
            {
                Add(list, "th" + i, "第 " + i + " 天最高温", Token(sample, weather, "th" + i));
            }

            // 实时天气字段组：城市、温度、湿度、风向、图标、AQI、公网 IP。
            Add(list, "pos", "城市", Token(sample, weather, "pos"));
            Add(list, "rtm", "实时温度", Token(sample, weather, "rtm"));
            Add(list, "rhu", "实时湿度", Token(sample, weather, "rhu"));
            Add(list, "rwd", "风向", Token(sample, weather, "rwd"));
            Add(list, "icn", "天气图标", Token(sample, weather, "icn"));
            Add(list, "aqi", "空气质量", Token(sample, weather, "aqi"));
            Add(list, "ip", "公网 IP", Token(sample, weather, "ip"));

            // 转义与字面量：本方言用英文双引号包住普通文字，示例列直接写文字。
            Add(list, "\\n", "换行", "换行");
            Add(list, "\"文字\"", "原样输出", "文字");
            return list;
        }

        /// <summary>用简洁变量引擎渲染一个预览值；格式串来自本文件的固定常量，不会解析失败。</summary>
        private static string Token(DateTime sample, WeatherInfo weather, string format)
        {
            return FormatEngine.Format(sample, weather, format);
        }

        /// <summary>追加一行帮助；example 为 null 时按空串处理，避免列表显示「null」。</summary>
        private static void Add(List<FormatHelpItem> list, string token, string description, string example)
        {
            list.Add(new FormatHelpItem(token, description, example ?? string.Empty));
        }
    }
}
