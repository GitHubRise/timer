using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameClock
{
    internal static class TimeFormatter
    {
        public const string DefaultFormat = "HH:mm:ss";
        public const int MaxFormatLength = 120;
        public const int MaxLines = 2;

        public static bool TryNormalizeFormat(string input, out string normalized, out string error)
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
            format = format.Replace("SSS", "fff");

            string[] lines = format.Split('\n');
            if (lines.Length > MaxLines)
            {
                error = "最多只支持一个换行（两行文字）。";
                return false;
            }

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
            if (format.Length > MaxFormatLength)
            {
                error = "格式太长，请控制在 " + MaxFormatLength + " 个字符以内。";
                return false;
            }

            try
            {
                DateTime sample = new DateTime(2026, 10, 26, 23, 45, 12, 345);
                string probe = ToRenderableFormat(
                    format.Replace("WW", GetIsoWeek(sample).ToString("00", CultureInfo.InvariantCulture)));
                sample.ToString(probe, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                error = "格式无效，请检查日期占位符或引号是否完整。";
                return false;
            }

            normalized = format;
            return true;
        }

        public static string NormalizeFormat(string input)
        {
            string normalized;
            string error;
            if (TryNormalizeFormat(input, out normalized, out error))
            {
                return normalized;
            }
            return DefaultFormat;
        }

        public static string Format(DateTime time, string format)
        {
            string normalized = NormalizeFormat(format);
            string week = GetIsoWeek(time).ToString("00", CultureInfo.InvariantCulture);
            string prepared = ToRenderableFormat(normalized.Replace("WW", week));
            try
            {
                return time.ToString(prepared, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                return time.ToString(DefaultFormat, CultureInfo.CurrentCulture);
            }
        }

        private static string ToRenderableFormat(string format)
        {
            // Single-character formats would otherwise be interpreted by .NET as
            // standard patterns ("M" = month/day, "d" = short date, "s" = sortable).
            // Prefixing "%" keeps them as the documented single custom specifiers.
            if (format.Length == 1 && "MdHhmsf".IndexOf(format[0]) >= 0)
            {
                return "%" + format;
            }
            return format;
        }

        public static int CountNewlines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n' || text[i] == '\r')
                {
                    count++;
                }
            }
            return count;
        }

        public static bool TryNormalizeTime(string input, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string text = input.Trim().Replace('：', ':');
            string[] formats = new string[] { "H:mm", "HH:mm" };
            DateTime parsed;
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

        public static int GetIsoWeek(DateTime date)
        {
            DayOfWeek day = CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(date);
            if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday)
            {
                date = date.AddDays(3);
            }
            return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                date,
                CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday);
        }

        public static List<TimeFormatPreset> CreatePresets(DateTime sample)
        {
            List<TimeFormatPreset> list = new List<TimeFormatPreset>();
            Add(list, "时间（24 小时，默认）", "HH:mm:ss", sample);
            Add(list, "时间（12 小时 + 上下午）", "hh:mm:ss tt", sample);
            Add(list, "时间 + 毫秒", "HH:mm:ss.fff", sample);

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

            Add(list, "日期 + 星期", "yyyy-MM-dd dddd", sample);
            Add(list, "时间 + 星期", "HH:mm:ss dddd", sample);
            Add(list, "年 + 第几周", "yyyy 第WW周", sample);

            Add(list, "两行：日期 / 时间", "yyyy-MM-dd\nHH:mm:ss", sample);
            Add(list, "两行：时间 / 日期+星期", "HH:mm:ss\nyyyy-MM-dd dddd", sample);
            Add(list, "两行：中文日期 / 时间", "yyyy年M月d日\nHH:mm:ss", sample);
            return list;
        }

        private static void Add(List<TimeFormatPreset> list, string name, string format, DateTime sample)
        {
            string example = Format(sample, format).Replace("\n", " / ");
            list.Add(new TimeFormatPreset(name, format, example));
        }
    }

    internal sealed class TimeFormatPreset
    {
        public TimeFormatPreset(string name, string format, string example)
        {
            Name = name;
            Format = format;
            Example = example;
        }

        public string Name { get; private set; }
        public string Format { get; private set; }
        public string Example { get; private set; }

        public string Display
        {
            get { return Name + "   ·   " + Example; }
        }

        public override string ToString()
        {
            return Display;
        }
    }
}
