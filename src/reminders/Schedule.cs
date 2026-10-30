using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace GameClock
{
    internal sealed class ScheduleCategoryOption
    {
        public ScheduleCategoryOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; private set; }
        public string Label { get; private set; }

        public override string ToString()
        {
            return Label;
        }
    }

    internal static class ScheduleCategories
    {
        public const string Birthday = "birthday";
        public const string Anniversary = "anniversary";
        public const string Meeting = "meeting";
        public const string Flight = "flight";
        public const string Other = "other";

        public static List<ScheduleCategoryOption> Create()
        {
            List<ScheduleCategoryOption> list = new List<ScheduleCategoryOption>();
            list.Add(new ScheduleCategoryOption(Birthday, "生日"));
            list.Add(new ScheduleCategoryOption(Anniversary, "纪念日"));
            list.Add(new ScheduleCategoryOption(Meeting, "会议"));
            list.Add(new ScheduleCategoryOption(Flight, "机票"));
            list.Add(new ScheduleCategoryOption(Other, "其他"));
            return list;
        }

        public static string Normalize(string type)
        {
            if (string.Equals(type, Birthday, StringComparison.OrdinalIgnoreCase))
            {
                return Birthday;
            }
            if (string.Equals(type, Anniversary, StringComparison.OrdinalIgnoreCase))
            {
                return Anniversary;
            }
            if (string.Equals(type, Flight, StringComparison.OrdinalIgnoreCase))
            {
                return Flight;
            }
            if (string.Equals(type, Other, StringComparison.OrdinalIgnoreCase))
            {
                return Other;
            }
            return Meeting;
        }

        public static string DisplayName(string type)
        {
            string normalized = Normalize(type);
            if (normalized == Birthday) return "生日";
            if (normalized == Anniversary) return "纪念日";
            if (normalized == Flight) return "机票";
            if (normalized == Other) return "其他";
            return "会议";
        }

        public static bool IsAnnual(string type)
        {
            string normalized = Normalize(type);
            return normalized == Birthday || normalized == Anniversary;
        }
    }

    internal sealed class ScheduleAdvanceOption
    {
        public ScheduleAdvanceOption(int minutes, string label)
        {
            Minutes = minutes;
            Label = label;
        }

        public int Minutes { get; private set; }
        public string Label { get; private set; }

        public override string ToString()
        {
            return Label;
        }
    }

    internal static class ScheduleAdvanceOptions
    {
        public static List<ScheduleAdvanceOption> Create()
        {
            List<ScheduleAdvanceOption> list = new List<ScheduleAdvanceOption>();
            list.Add(new ScheduleAdvanceOption(0, "准时"));
            list.Add(new ScheduleAdvanceOption(5, "提前 5 分钟"));
            list.Add(new ScheduleAdvanceOption(10, "提前 10 分钟"));
            list.Add(new ScheduleAdvanceOption(15, "提前 15 分钟"));
            list.Add(new ScheduleAdvanceOption(30, "提前 30 分钟"));
            list.Add(new ScheduleAdvanceOption(60, "提前 1 小时"));
            list.Add(new ScheduleAdvanceOption(120, "提前 2 小时"));
            list.Add(new ScheduleAdvanceOption(1440, "提前 1 天"));
            return list;
        }

        public static int Normalize(int minutes)
        {
            List<ScheduleAdvanceOption> options = Create();
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Minutes == minutes)
                {
                    return minutes;
                }
            }
            return 15;
        }

        public static string DisplayName(int minutes)
        {
            int normalized = Normalize(minutes);
            List<ScheduleAdvanceOption> options = Create();
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Minutes == normalized)
                {
                    return options[i].Label;
                }
            }
            return "提前 15 分钟";
        }
    }

    [DataContract]
    internal sealed class ScheduleItem
    {
        public ScheduleItem()
        {
            Type = ScheduleCategories.Meeting;
            Title = "";
            Date = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Time = "09:00";
            AdvanceMinutes = 15;
            Annual = false;
            Enabled = true;
            Note = "";
            LastTriggeredKey = "";
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Type = ScheduleCategories.Meeting;
            Title = "";
            Date = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Time = "09:00";
            AdvanceMinutes = 15;
            Annual = false;
            Enabled = true;
            Note = "";
            LastTriggeredKey = "";
        }

        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "date")]
        public string Date { get; set; }

        [DataMember(Name = "time")]
        public string Time { get; set; }

        [DataMember(Name = "advanceMinutes")]
        public int AdvanceMinutes { get; set; }

        [DataMember(Name = "annual")]
        public bool Annual { get; set; }

        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "note")]
        public string Note { get; set; }

        [DataMember(Name = "lastTriggeredKey")]
        public string LastTriggeredKey { get; set; }

        public ScheduleItem Clone()
        {
            ScheduleItem copy = new ScheduleItem();
            copy.Type = Type;
            copy.Title = Title;
            copy.Date = Date;
            copy.Time = Time;
            copy.AdvanceMinutes = AdvanceMinutes;
            copy.Annual = Annual;
            copy.Enabled = Enabled;
            copy.Note = Note;
            copy.LastTriggeredKey = LastTriggeredKey;
            return copy;
        }

        public bool Normalize()
        {
            Type = ScheduleCategories.Normalize(Type);

            Title = (Title ?? string.Empty).Trim();
            if (Title.Length == 0)
            {
                return false;
            }
            if (Title.Length > 80)
            {
                Title = Title.Substring(0, 80);
            }

            DateTime date;
            if (!ScheduleService.TryParseDate(Date, out date))
            {
                return false;
            }
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string normalizedTime;
            if (!TimeFormatter.TryNormalizeTime(Time, out normalizedTime))
            {
                return false;
            }
            Time = normalizedTime;

            AdvanceMinutes = ScheduleAdvanceOptions.Normalize(AdvanceMinutes);

            Note = (Note ?? string.Empty).Trim();
            if (Note.Length > 240)
            {
                Note = Note.Substring(0, 240);
            }
            if (LastTriggeredKey == null)
            {
                LastTriggeredKey = "";
            }
            return true;
        }
    }

    internal sealed class ScheduleTriggeredEventArgs : EventArgs
    {
        public ScheduleTriggeredEventArgs(ScheduleItem schedule, DateTime occurrence)
        {
            Schedule = schedule;
            Occurrence = occurrence;
        }

        public ScheduleItem Schedule { get; private set; }
        public DateTime Occurrence { get; private set; }
    }

    internal static class ScheduleService
    {
        public static bool TryParseDate(string text, out DateTime date)
        {
            return DateTime.TryParseExact(
                (text ?? string.Empty).Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        public static bool TryFindDueOccurrence(ScheduleItem item, DateTime now, out DateTime occurrence)
        {
            occurrence = default(DateTime);
            DateTime date;
            if (!TryParseDate(item.Date, out date))
            {
                return false;
            }

            DateTime time;
            if (!DateTime.TryParseExact(
                item.Time,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time))
            {
                return false;
            }

            int advance = ScheduleAdvanceOptions.Normalize(item.AdvanceMinutes);
            if (item.Annual)
            {
                for (int offset = -1; offset <= 1; offset++)
                {
                    int year = now.Year + offset;
                    DateTime candidate = MakeAnnualOccurrence(year, date, time);
                    if (IsInReminderWindow(now, candidate, advance))
                    {
                        occurrence = candidate;
                        return true;
                    }
                }
                return false;
            }

            DateTime fixedOccurrence = date.Date.AddHours(time.Hour).AddMinutes(time.Minute);
            if (IsInReminderWindow(now, fixedOccurrence, advance))
            {
                occurrence = fixedOccurrence;
                return true;
            }
            return false;
        }

        public static string GetOccurrenceKey(DateTime occurrence)
        {
            return occurrence.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static DateTime MakeAnnualOccurrence(int year, DateTime original, DateTime time)
        {
            int day = Math.Min(original.Day, DateTime.DaysInMonth(year, original.Month));
            return new DateTime(year, original.Month, day, time.Hour, time.Minute, 0);
        }

        private static bool IsInReminderWindow(DateTime now, DateTime occurrence, int advanceMinutes)
        {
            DateTime remindAt = occurrence.AddMinutes(-advanceMinutes);
            return now >= remindAt && now <= occurrence.AddMinutes(30);
        }
    }
}
