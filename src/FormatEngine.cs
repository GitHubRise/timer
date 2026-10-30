using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameClock
{
    internal enum FormatToken
    {
        Literal = 0,
        Year2,
        Year4,
        ChineseYear2,
        ChineseYear4,
        Zodiac,
        GanzhiZodiac,
        Month,
        MonthPadded,
        LunarMonth,
        EnglishMonthShort,
        EnglishMonthLong,
        Day,
        DayPadded,
        LunarDay,
        Hour12,
        Hour12Padded,
        Hour24,
        Hour24Padded,
        Minute,
        MinutePadded,
        Second,
        SecondPadded,
        Millisecond,
        WeekdayShort,
        WeekdayLong,
        EnglishWeekdayShort,
        EnglishWeekdayLong,
        IsoWeek,
        DayOfYear,
        AmPmEnglish,
        AmPmChinese,
        WeatherDay1,
        WeatherDay2,
        WeatherDay3,
        WeatherDay4,
        WeatherDay5,
        WeatherDay6,
        Low1,
        Low2,
        Low3,
        Low4,
        Low5,
        Low6,
        High1,
        High2,
        High3,
        High4,
        High5,
        High6,
        Temperature,
        Humidity,
        WindDirection,
        WeatherIcon,
        AirQuality,
        PublicIp,
        City
    }

    internal sealed class FormatPart
    {
        public FormatToken Token;
        public string Literal;
    }

    internal sealed class ParsedFormat
    {
        public readonly List<FormatPart> Parts;
        public bool HasWeatherToken;

        public ParsedFormat(List<FormatPart> parts)
        {
            Parts = parts;
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

    internal static class FormatEngine
    {
        public const string ModeDotNet = "dotnet";
        public const string ModeToken = "token";
        public const string DefaultTokenFormat = "HH:ff:ss";
        public const int MaxFormatLength = 120;
        public const int MaxLines = 2;

        private static readonly CultureInfo ChineseCulture = CultureInfo.GetCultureInfo("zh-CN");
        private static readonly CultureInfo EnglishCulture = CultureInfo.InvariantCulture;
        private static readonly Dictionary<string, FormatToken> Tokens = BuildTokenTable();
        private static readonly Dictionary<string, ParsedFormat> Cache =
            new Dictionary<string, ParsedFormat>(StringComparer.Ordinal);
        private static readonly object CacheLock = new object();

        public static bool IsTokenMode(string mode)
        {
            return string.Equals(mode, ModeToken, StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeMode(string mode)
        {
            return IsTokenMode(mode) ? ModeToken : ModeDotNet;
        }

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

            string[] lines = format.Split('\n');
            if (lines.Length > MaxLines)
            {
                error = allowNewline
                    ? "最多只支持一个换行（两行文字）。"
                    : "天气格式只能是一行文字。";
                return false;
            }
            if (!allowNewline && lines.Length > 1)
            {
                error = "天气格式只能是一行文字。";
                return false;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Trim();
                if (lines[i].Length == 0)
                {
                    error = allowNewline ? "换行两侧都需要填写内容。" : "天气格式不能为空。";
                    return false;
                }
            }

            format = string.Join("\n", lines);
            if (format.Length > MaxFormatLength)
            {
                error = "格式太长，请控制在 " + MaxFormatLength + " 个字符以内。";
                return false;
            }

            ParsedFormat parsed;
            if (!TryParse(format, out parsed, out error))
            {
                return false;
            }

            normalized = format;
            return true;
        }

        public static string NormalizeTokenFormat(string input)
        {
            string normalized;
            string error;
            if (TryNormalizeTokenFormat(input, true, out normalized, out error))
            {
                return normalized;
            }
            return DefaultTokenFormat;
        }

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
            return string.Empty;
        }

        public static bool TryParse(string format, out ParsedFormat parsed, out string error)
        {
            parsed = null;
            error = null;

            if (string.IsNullOrEmpty(format))
            {
                parsed = new ParsedFormat(new List<FormatPart>());
                return true;
            }

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

            parsed = new ParsedFormat(parts);
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

        public static string Format(DateTime time, WeatherInfo weather, string format)
        {
            ParsedFormat parsed;
            string error;
            if (!TryParse(format, out parsed, out error) || parsed == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(format.Length + 16);
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
            return CollapseEmptyLines(builder.ToString());
        }

        private static string CollapseEmptyLines(string text)
        {
            if (text.IndexOf('\n') < 0)
            {
                return text;
            }

            string[] lines = text.Split('\n');
            List<string> kept = new List<string>(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0)
                {
                    kept.Add(lines[i]);
                }
            }
            return string.Join("\n", kept.ToArray());
        }

        public static bool ContainsWeatherToken(string format)
        {
            ParsedFormat parsed;
            string error;
            return TryParse(format, out parsed, out error) && parsed != null && parsed.HasWeatherToken;
        }

        public static bool RequiresAirQuality(string format)
        {
            return ContainsToken(format, FormatToken.AirQuality);
        }

        public static bool RequiresPublicIp(string format)
        {
            return ContainsToken(format, FormatToken.PublicIp);
        }

        private static bool ContainsToken(string format, FormatToken token)
        {
            ParsedFormat parsed;
            string error;
            if (!TryParse(format, out parsed, out error) || parsed == null)
            {
                return false;
            }
            for (int i = 0; i < parsed.Parts.Count; i++)
            {
                if (parsed.Parts[i].Token == token)
                {
                    return true;
                }
            }
            return false;
        }

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

        public static List<TimeFormatPreset> CreatePresets(DateTime sample)
        {
            List<TimeFormatPreset> list = new List<TimeFormatPreset>();
            Add(list, "时间（默认）", "HH:ff:ss", sample);
            Add(list, "时间（12 小时 + 上下午）", "TT hh:ff:ss", sample);
            Add(list, "时间 + 毫秒", "HH:ff:ss.SSS", sample);
            Add(list, "仅日期", "yyyy-mm-dd", sample);

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

        private static void Add(List<TimeFormatPreset> list, string name, string format, DateTime sample)
        {
            string example = Format(sample, PresetWeather, format).Replace("\n", " / ");
            list.Add(new TimeFormatPreset(name, format, example));
        }

        private static readonly WeatherInfo PresetWeather = CreatePresetWeather();

        private static WeatherInfo CreatePresetWeather()
        {
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

        private static void AppendToken(StringBuilder builder, FormatToken token, DateTime time, WeatherInfo weather)
        {
            switch (token)
            {
                case FormatToken.Year2:
                    builder.Append((time.Year % 100).ToString("00", CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Year4:
                    builder.Append(time.Year.ToString("0000", CultureInfo.InvariantCulture));
                    break;
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
                case FormatToken.Month:
                    builder.Append(time.Month.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.MonthPadded:
                    builder.Append(time.Month.ToString("00", CultureInfo.InvariantCulture));
                    break;
                case FormatToken.LunarMonth:
                    builder.Append(ChineseCalendarInfo.LunarMonthName(time));
                    break;
                case FormatToken.EnglishMonthShort:
                    builder.Append(time.ToString("MMM", EnglishCulture));
                    break;
                case FormatToken.EnglishMonthLong:
                    builder.Append(time.ToString("MMMM", EnglishCulture));
                    break;
                case FormatToken.Day:
                    builder.Append(time.Day.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.DayPadded:
                    builder.Append(time.Day.ToString("00", CultureInfo.InvariantCulture));
                    break;
                case FormatToken.LunarDay:
                    builder.Append(ChineseCalendarInfo.LunarDayName(time));
                    break;
                case FormatToken.Hour12:
                    builder.Append(Hour12(time).ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Hour12Padded:
                    builder.Append(Hour12(time).ToString("00", CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Hour24:
                    builder.Append(time.Hour.ToString(CultureInfo.InvariantCulture));
                    break;
                case FormatToken.Hour24Padded:
                    builder.Append(time.Hour.ToString("00", CultureInfo.InvariantCulture));
                    break;
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
                case FormatToken.AmPmEnglish:
                    builder.Append(time.Hour < 12 ? "AM" : "PM");
                    break;
                case FormatToken.AmPmChinese:
                    builder.Append(time.Hour < 12 ? "上午" : "下午");
                    break;
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
                case FormatToken.Temperature:
                    if (weather != null)
                    {
                        builder.Append(FormatTemperature(weather.Temperature));
                    }
                    break;
                case FormatToken.Humidity:
                    if (weather != null && weather.Humidity.HasValue)
                    {
                        builder.Append("湿度");
                        builder.Append(weather.Humidity.Value.ToString(CultureInfo.InvariantCulture));
                        builder.Append('%');
                    }
                    break;
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

        private static int Hour12(DateTime time)
        {
            int hour = time.Hour % 12;
            return hour == 0 ? 12 : hour;
        }

        private static DailyForecast GetDay(WeatherInfo weather, int index)
        {
            if (weather == null || weather.Days == null || index < 0 || index >= weather.Days.Count)
            {
                return null;
            }
            return weather.Days[index];
        }

        public static string FormatTemperature(double value)
        {
            return Math.Round(value, 0, MidpointRounding.AwayFromZero)
                .ToString(CultureInfo.InvariantCulture) + "°C";
        }

        private static bool ParseSlow(string format, out List<FormatPart> parts, out string error)
        {
            parts = new List<FormatPart>();
            error = null;
            StringBuilder literal = new StringBuilder();
            int i = 0;

            while (i < format.Length)
            {
                char c = format[i];

                if (c == '"')
                {
                    i++;
                    bool closed = false;
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
                    if (!closed)
                    {
                        error = "英文引号没有闭合，请补上结束引号。";
                        return false;
                    }
                    continue;
                }

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

                if (!IsAsciiLetter(c))
                {
                    literal.Append(c);
                    i++;
                    continue;
                }

                int run = CountRun(format, i, c);
                string trap = CheckRunTrap(c, run);
                if (trap != null)
                {
                    error = trap;
                    return false;
                }

                FormatToken token;
                int length;
                if (TryMatchToken(format, i, out token, out length))
                {
                    FlushLiteral(parts, literal);
                    parts.Add(new FormatPart { Token = token });
                    i += length;
                    continue;
                }

                FlushLiteral(parts, literal);
                string snippet = format.Substring(i, Math.Min(6, format.Length - i));
                error = "无法识别格式变量“" + snippet + "”（第 " + (i + 1)
                    + " 个字符）。普通文字请用英文双引号括起来，例如 \"第\"。";
                return false;
            }

            FlushLiteral(parts, literal);
            return true;
        }

        private static string CheckRunTrap(char c, int run)
        {
            switch (c)
            {
                case 'M':
                    if (run >= 2)
                    {
                        return "“MM”不是月份变量：公历月用 m 或 mm，农历月用 M。";
                    }
                    break;
                case 'D':
                    if (run >= 2)
                    {
                        return "“DD”不是日期变量：公历日用 d 或 dd，农历日用 D。";
                    }
                    break;
                case 'f':
                    if (run >= 3)
                    {
                        return "毫秒请用 SSS；f 表示分钟（ff 为两位分钟）。";
                    }
                    break;
                case 'y':
                    if (run == 3)
                    {
                        return "年份请用 yy（两位）或 yyyy（四位）。";
                    }
                    break;
                case 'Y':
                    if (run == 3)
                    {
                        return "中文年份请用 YY（两位）或 YYYY（四位）。";
                    }
                    break;
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

        private static int CountRun(string format, int start, char c)
        {
            int count = 0;
            while (start + count < format.Length && format[start + count] == c)
            {
                count++;
            }
            return count;
        }

        private static bool TryMatchToken(string format, int start, out FormatToken token, out int length)
        {
            token = FormatToken.Literal;
            length = 0;
            for (int size = 4; size >= 1; size--)
            {
                if (start + size > format.Length)
                {
                    continue;
                }
                string candidate = format.Substring(start, size);
                FormatToken found;
                if (Tokens.TryGetValue(candidate, out found))
                {
                    token = found;
                    length = size;
                    return true;
                }
            }
            return false;
        }

        private static void FlushLiteral(List<FormatPart> parts, StringBuilder literal)
        {
            if (literal.Length == 0)
            {
                return;
            }
            parts.Add(new FormatPart { Token = FormatToken.Literal, Literal = literal.ToString() });
            literal.Length = 0;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static Dictionary<string, FormatToken> BuildTokenTable()
        {
            Dictionary<string, FormatToken> table =
                new Dictionary<string, FormatToken>(StringComparer.Ordinal);

            table["yyyy"] = FormatToken.Year4;
            table["YYYY"] = FormatToken.ChineseYear4;
            table["dddd"] = FormatToken.WeekdayLong;
            table["week"] = FormatToken.IsoWeek;
            table["day"] = FormatToken.DayOfYear;
            table["doy"] = FormatToken.DayOfYear;
            table["SSS"] = FormatToken.Millisecond;

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

    internal static class ChineseCalendarInfo
    {
        private static readonly ChineseLunisolarCalendar Calendar = new ChineseLunisolarCalendar();

        private static readonly string[] MonthNames = new string[]
        {
            "正月", "二月", "三月", "四月", "五月", "六月",
            "七月", "八月", "九月", "十月", "冬月", "腊月"
        };

        private static readonly string[] StemNames = new string[]
        {
            "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"
        };

        private static readonly string[] BranchNames = new string[]
        {
            "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"
        };

        private static readonly string[] ZodiacNames = new string[]
        {
            "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪"
        };

        private static readonly char[] YearDigitsMap = new char[]
        {
            '〇', '一', '二', '三', '四', '五', '六', '七', '八', '九'
        };

        public static string YearDigits(int year)
        {
            string digits = Math.Abs(year).ToString(CultureInfo.InvariantCulture);
            StringBuilder builder = new StringBuilder(digits.Length);
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

        public static string LunarMonthName(DateTime time)
        {
            try
            {
                int year = Calendar.GetYear(time);
                int month = Calendar.GetMonth(time);
                int leapMonth = Calendar.GetLeapMonth(year);
                bool leap = false;
                int realMonth = month;

                if (leapMonth > 0)
                {
                    if (month == leapMonth)
                    {
                        leap = true;
                        realMonth = leapMonth - 1;
                    }
                    else if (month > leapMonth)
                    {
                        realMonth = month - 1;
                    }
                }

                if (realMonth < 1 || realMonth > 12)
                {
                    return string.Empty;
                }

                return (leap ? "闰" : string.Empty) + MonthNames[realMonth - 1];
            }
            catch (ArgumentOutOfRangeException)
            {
                return string.Empty;
            }
        }

        public static string LunarDayName(DateTime time)
        {
            try
            {
                int day = Calendar.GetDayOfMonth(time);
                return DayName(day);
            }
            catch (ArgumentOutOfRangeException)
            {
                return string.Empty;
            }
        }

        public static string ZodiacName(DateTime time)
        {
            int branch;
            if (!TryGetBranch(time, out branch))
            {
                return string.Empty;
            }
            return ZodiacNames[branch - 1] + "年";
        }

        public static string GanzhiZodiacName(DateTime time)
        {
            try
            {
                int sexagenary = Calendar.GetSexagenaryYear(time);
                int stem = Calendar.GetCelestialStem(sexagenary);
                int branch = Calendar.GetTerrestrialBranch(sexagenary);
                if (stem < 1 || stem > 10 || branch < 1 || branch > 12)
                {
                    return string.Empty;
                }
                return StemNames[stem - 1] + BranchNames[branch - 1] + ZodiacNames[branch - 1] + "年";
            }
            catch (ArgumentOutOfRangeException)
            {
                return string.Empty;
            }
        }

        private static bool TryGetBranch(DateTime time, out int branch)
        {
            branch = 0;
            try
            {
                int sexagenary = Calendar.GetSexagenaryYear(time);
                branch = Calendar.GetTerrestrialBranch(sexagenary);
                return branch >= 1 && branch <= 12;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static string DayName(int day)
        {
            if (day < 1 || day > 30)
            {
                return string.Empty;
            }
            if (day <= 10)
            {
                return day == 10 ? "初十" : "初" + Digit(day);
            }
            if (day < 20)
            {
                return "十" + Digit(day - 10);
            }
            if (day == 20)
            {
                return "二十";
            }
            if (day < 30)
            {
                return "廿" + Digit(day - 20);
            }
            return "三十";
        }

        private static string Digit(int value)
        {
            string[] digits = new string[] { "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            if (value < 1 || value > 9)
            {
                return string.Empty;
            }
            return digits[value - 1];
        }
    }
}
