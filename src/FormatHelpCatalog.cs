using System;
using System.Collections.Generic;

namespace GameClock
{
    internal sealed class FormatHelpItem
    {
        public FormatHelpItem(string token, string description, string example)
        {
            Token = token;
            Description = description;
            Example = example;
        }

        public string Token { get; private set; }
        public string Description { get; private set; }
        public string Example { get; private set; }

        public string SearchText
        {
            get { return Token + " " + Description + " " + Example; }
        }

        public string DisplayText
        {
            get { return Token + "：" + Description + "。示例预览：" + Example; }
        }
    }

    internal static class FormatHelpCatalog
    {
        public static List<FormatHelpItem> Create(string mode, DateTime sample, WeatherInfo weather)
        {
            return FormatEngine.IsTokenMode(mode)
                ? CreateToken(sample, weather)
                : CreateDotNet(sample);
        }

        private static List<FormatHelpItem> CreateDotNet(DateTime sample)
        {
            List<FormatHelpItem> list = new List<FormatHelpItem>();
            Add(list, "yyyy", "四位年份", TimeFormatter.Format(sample, "yyyy"));
            Add(list, "yy", "两位年份", TimeFormatter.Format(sample, "yy"));
            Add(list, "MM", "月份（两位）", TimeFormatter.Format(sample, "MM"));
            Add(list, "M", "月份（不补零）", TimeFormatter.Format(sample, "M"));
            Add(list, "dd", "日期（两位）", TimeFormatter.Format(sample, "dd"));
            Add(list, "d", "日期（不补零）", TimeFormatter.Format(sample, "d"));
            Add(list, "ddd", "星期（短）", TimeFormatter.Format(sample, "ddd"));
            Add(list, "dddd", "星期（长）", TimeFormatter.Format(sample, "dddd"));
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
            Add(list, "tt", "上午/下午", TimeFormatter.Format(sample, "tt"));
            Add(list, "WW", "ISO 周数（两位）", TimeFormatter.Format(sample, "WW"));
            Add(list, "zzz", "时区偏移", TimeFormatter.Format(sample, "zzz"));
            Add(list, "\\n", "换行", "换行");
            Add(list, "'文字'", "原样输出", "文字");
            return list;
        }

        private static List<FormatHelpItem> CreateToken(DateTime sample, WeatherInfo weather)
        {
            List<FormatHelpItem> list = new List<FormatHelpItem>();
            Add(list, "yy", "两位年份", Token(sample, weather, "yy"));
            Add(list, "yyyy", "四位年份", Token(sample, weather, "yyyy"));
            Add(list, "YY", "中文数字两位年", Token(sample, weather, "YY"));
            Add(list, "YYYY", "中文数字四位年", Token(sample, weather, "YYYY"));
            Add(list, "nn", "农历生肖年", Token(sample, weather, "nn"));
            Add(list, "NN", "农历干支+生肖年", Token(sample, weather, "NN"));

            Add(list, "m", "公历月（不补零）", Token(sample, weather, "m"));
            Add(list, "mm", "公历月（两位）", Token(sample, weather, "mm"));
            Add(list, "M", "农历月", Token(sample, weather, "M"));
            Add(list, "d", "公历日（不补零）", Token(sample, weather, "d"));
            Add(list, "dd", "公历日（两位）", Token(sample, weather, "dd"));
            Add(list, "D", "农历日", Token(sample, weather, "D"));

            Add(list, "h", "12 小时（不补零）", Token(sample, weather, "h"));
            Add(list, "hh", "12 小时（两位）", Token(sample, weather, "hh"));
            Add(list, "H", "24 小时（不补零）", Token(sample, weather, "H"));
            Add(list, "HH", "24 小时（两位）", Token(sample, weather, "HH"));
            Add(list, "f", "分钟（不补零）", Token(sample, weather, "f"));
            Add(list, "ff", "分钟（两位）", Token(sample, weather, "ff"));
            Add(list, "s", "秒（不补零）", Token(sample, weather, "s"));
            Add(list, "ss", "秒（两位）", Token(sample, weather, "ss"));
            Add(list, "SSS", "毫秒（三位）", Token(sample, weather, "SSS"));

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

            for (int i = 1; i <= 6; i++)
            {
                Add(list, "we" + i, "第 " + i + " 天天气", Token(sample, weather, "we" + i));
            }
            for (int i = 1; i <= 6; i++)
            {
                Add(list, "tl" + i, "第 " + i + " 天最低温", Token(sample, weather, "tl" + i));
            }
            for (int i = 1; i <= 6; i++)
            {
                Add(list, "th" + i, "第 " + i + " 天最高温", Token(sample, weather, "th" + i));
            }

            Add(list, "pos", "城市", Token(sample, weather, "pos"));
            Add(list, "rtm", "实时温度", Token(sample, weather, "rtm"));
            Add(list, "rhu", "实时湿度", Token(sample, weather, "rhu"));
            Add(list, "rwd", "风向", Token(sample, weather, "rwd"));
            Add(list, "icn", "天气图标", Token(sample, weather, "icn"));
            Add(list, "aqi", "空气质量", Token(sample, weather, "aqi"));
            Add(list, "ip", "公网 IP", Token(sample, weather, "ip"));

            Add(list, "\\n", "换行", "换行");
            Add(list, "\"文字\"", "原样输出", "文字");
            return list;
        }

        private static string Token(DateTime sample, WeatherInfo weather, string format)
        {
            return FormatEngine.Format(sample, weather, format);
        }

        private static void Add(List<FormatHelpItem> list, string token, string description, string example)
        {
            list.Add(new FormatHelpItem(token, description, example ?? string.Empty));
        }
    }
}
