// 设置窗口“时间格式”页的格式变量帮助条目表。
// 按当前格式语言分两套：.NET 自定义格式（CreateDotNet）与简洁变量（CreateToken），
// 示例值由 TimeFormatter / FormatEngine 用传入的 sample 与 weather 实时渲染。
// 调用方只读返回的列表，用于生成帮助列表或搜索；本类不缓存，sample 变化时重新创建。
// 约束：条目顺序即界面展示顺序，新增变量时两套语言都要考虑是否需要同步补充。

using System;
using System.Collections.Generic;

namespace GameClock
{

    /// <summary>
    /// 一条格式变量说明：变量名、中文描述与示例。
    /// <see cref="SearchText"/> 供搜索过滤，<see cref="DisplayText"/> 供列表直接显示。
    /// 生命周期：每次 Create 时创建，随返回列表一起丢弃。
    /// </summary>
    internal sealed class FormatHelpItem
    {

        /// <summary>保存变量名、描述与示例；示例为 null 时按空串处理。</summary>
        /// <param name="token">格式变量本身，例如 <c>yyyy</c> 或 <c>week</c>。</param>
        /// <param name="description">中文用途说明。</param>
        /// <param name="example">用当前示例时间渲染出的结果。</param>
        public FormatHelpItem(string token, string description, string example)
        {
            Token = token;
            Description = description;
            Example = example;
        }

        /// <summary>格式变量名，显示在帮助条目的最前面。</summary>
        public string Token { get; private set; }

        /// <summary>变量的中文用途说明。</summary>
        public string Description { get; private set; }

        /// <summary>用示例时间渲染出的结果，可能为空串。</summary>
        public string Example { get; private set; }

        /// <summary>把变量名、描述与示例拼成搜索用的文本，供列表过滤。</summary>
        public string SearchText
        {
            get { return Token + " " + Description + " " + Example; }
        }

        /// <summary>列表行显示文本：变量名、说明与示例预览。</summary>
        public string DisplayText
        {
            get { return Token + "：" + Description + "。示例预览：" + Example; }
        }
    }

    /// <summary>
    /// 格式变量帮助条目的工厂。无状态，根据格式语言分派到对应条目表；
    /// 示例渲染失败由底层格式化器兜底，本类不抛异常。
    /// </summary>
    internal static class FormatHelpCatalog
    {

        /// <summary>
        /// 根据格式语言创建帮助条目列表。
        /// </summary>
        /// <param name="mode">当前格式模式，交给 <see cref="FormatEngine.IsTokenMode"/> 判断。</param>
        /// <param name="sample">用于生成示例的时间。</param>
        /// <param name="weather">简洁变量中的天气数据，可为 null。</param>
        /// <returns>按界面展示顺序排列的条目列表。</returns>
        public static List<FormatHelpItem> Create(string mode, DateTime sample, WeatherInfo weather)
        {
            return FormatEngine.IsTokenMode(mode)
                ? CreateToken(sample, weather)
                : CreateDotNet(sample);
        }

        /// <summary>
        /// 构建 .NET 自定义格式的条目表，示例统一用 <see cref="TimeFormatter.Format"/> 渲染。
        /// 覆盖日期、时间、毫秒、星期、ISO 周、时区与转义写法。
        /// </summary>
        private static List<FormatHelpItem> CreateDotNet(DateTime sample)
        {
            List<FormatHelpItem> list = new List<FormatHelpItem>();

            // ===== 年、月、日 =====
            Add(list, "yyyy", "四位年份", TimeFormatter.Format(sample, "yyyy"));
            Add(list, "yy", "两位年份", TimeFormatter.Format(sample, "yy"));
            Add(list, "MM", "月份（两位）", TimeFormatter.Format(sample, "MM"));
            Add(list, "M", "月份（不补零）", TimeFormatter.Format(sample, "M"));
            Add(list, "dd", "日期（两位）", TimeFormatter.Format(sample, "dd"));
            Add(list, "d", "日期（不补零）", TimeFormatter.Format(sample, "d"));
            Add(list, "ddd", "星期（短）", TimeFormatter.Format(sample, "ddd"));
            Add(list, "dddd", "星期（长）", TimeFormatter.Format(sample, "dddd"));

            // ===== 时、分、秒与毫秒 =====
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

            // ===== 星期、周数、时区与转义 =====
            Add(list, "tt", "上午/下午", TimeFormatter.Format(sample, "tt"));
            Add(list, "WW", "ISO 周数（两位）", TimeFormatter.Format(sample, "WW"));
            Add(list, "zzz", "时区偏移", TimeFormatter.Format(sample, "zzz"));
            Add(list, "\\n", "换行", "换行");
            Add(list, "'文字'", "原样输出", "文字");
            return list;
        }

        /// <summary>
        /// 构建简洁变量的条目表，示例统一交给 <see cref="FormatEngine.Format"/> 渲染，
        /// 因此天气未就绪时示例可能为空，但条目本身仍保留。
        /// </summary>
        private static List<FormatHelpItem> CreateToken(DateTime sample, WeatherInfo weather)
        {
            List<FormatHelpItem> list = new List<FormatHelpItem>();

            // ===== 年份与农历年 =====
            Add(list, "yy", "两位年份", Token(sample, weather, "yy"));
            Add(list, "yyyy", "四位年份", Token(sample, weather, "yyyy"));
            Add(list, "YY", "中文数字两位年", Token(sample, weather, "YY"));
            Add(list, "YYYY", "中文数字四位年", Token(sample, weather, "YYYY"));
            Add(list, "nn", "农历生肖年", Token(sample, weather, "nn"));
            Add(list, "NN", "农历干支+生肖年", Token(sample, weather, "NN"));

            // ===== 公历与农历的月、日 =====
            Add(list, "m", "公历月（不补零）", Token(sample, weather, "m"));
            Add(list, "mm", "公历月（两位）", Token(sample, weather, "mm"));
            Add(list, "M", "农历月", Token(sample, weather, "M"));
            Add(list, "d", "公历日（不补零）", Token(sample, weather, "d"));
            Add(list, "dd", "公历日（两位）", Token(sample, weather, "dd"));
            Add(list, "D", "农历日", Token(sample, weather, "D"));

            // ===== 时、分、秒与毫秒 =====
            Add(list, "h", "12 小时（不补零）", Token(sample, weather, "h"));
            Add(list, "hh", "12 小时（两位）", Token(sample, weather, "hh"));
            Add(list, "H", "24 小时（不补零）", Token(sample, weather, "H"));
            Add(list, "HH", "24 小时（两位）", Token(sample, weather, "HH"));
            Add(list, "f", "分钟（不补零）", Token(sample, weather, "f"));
            Add(list, "ff", "分钟（两位）", Token(sample, weather, "ff"));
            Add(list, "s", "秒（不补零）", Token(sample, weather, "s"));
            Add(list, "ss", "秒（两位）", Token(sample, weather, "ss"));
            Add(list, "SSS", "毫秒（三位）", Token(sample, weather, "SSS"));

            // ===== 星期、英文名称与周数 =====
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

            // ===== 未来 6 天天气 =====
            // 批量生成 we1..we6：序号与天气数据源的下标一一对应。
            for (int i = 1; i <= 6; i++)
            {
                Add(list, "we" + i, "第 " + i + " 天天气", Token(sample, weather, "we" + i));
            }
            // 批量生成 tl1..tl6：第 N 天最低温。
            for (int i = 1; i <= 6; i++)
            {
                Add(list, "tl" + i, "第 " + i + " 天最低温", Token(sample, weather, "tl" + i));
            }
            // 批量生成 th1..th6：第 N 天最高温。
            for (int i = 1; i <= 6; i++)
            {
                Add(list, "th" + i, "第 " + i + " 天最高温", Token(sample, weather, "th" + i));
            }

            // ===== 实时天气与网络信息 =====
            Add(list, "pos", "城市", Token(sample, weather, "pos"));
            Add(list, "rtm", "实时温度", Token(sample, weather, "rtm"));
            Add(list, "rhu", "实时湿度", Token(sample, weather, "rhu"));
            Add(list, "rwd", "风向", Token(sample, weather, "rwd"));
            Add(list, "icn", "天气图标", Token(sample, weather, "icn"));
            Add(list, "aqi", "空气质量", Token(sample, weather, "aqi"));
            Add(list, "ip", "公网 IP", Token(sample, weather, "ip"));

            // ===== 转义 =====
            Add(list, "\\n", "换行", "换行");
            Add(list, "\"文字\"", "原样输出", "文字");
            return list;
        }

        /// <summary>
        /// 用简洁变量引擎渲染单个变量的示例；渲染失败时的兜底由
        /// <see cref="FormatEngine.Format"/> 负责。
        /// </summary>
        private static string Token(DateTime sample, WeatherInfo weather, string format)
        {
            return FormatEngine.Format(sample, weather, format);
        }

        /// <summary>
        /// 向列表追加一条帮助项；示例为 null 时转成空串，避免界面显示 null。
        /// </summary>
        private static void Add(List<FormatHelpItem> list, string token, string description, string example)
        {
            list.Add(new FormatHelpItem(token, description, example ?? string.Empty));
        }
    }
}
