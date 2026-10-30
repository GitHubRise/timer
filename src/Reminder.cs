using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GameClock
{
    internal static class ReminderModes
    {
        public const string Both = "both";
        public const string Work = "work";
        public const string Leisure = "leisure";

        public static string Normalize(string mode)
        {
            if (string.Equals(mode, Work, StringComparison.OrdinalIgnoreCase))
            {
                return Work;
            }
            if (string.Equals(mode, Leisure, StringComparison.OrdinalIgnoreCase))
            {
                return Leisure;
            }
            return Both;
        }

        public static string DisplayName(string mode)
        {
            string normalized = Normalize(mode);
            if (normalized == Work)
            {
                return "工作";
            }
            if (normalized == Leisure)
            {
                return "娱乐";
            }
            return "全部状态";
        }

        public static bool AppliesToState(string reminderMode, string currentState)
        {
            string mode = Normalize(reminderMode);
            string state = Normalize(currentState);
            if (mode == Both)
            {
                return true;
            }
            return mode == state;
        }

        public static List<ReminderModeOption> CreateOptions()
        {
            List<ReminderModeOption> options = new List<ReminderModeOption>();
            options.Add(new ReminderModeOption(Both, "全部状态"));
            options.Add(new ReminderModeOption(Work, "仅工作"));
            options.Add(new ReminderModeOption(Leisure, "仅娱乐"));
            return options;
        }
    }

    internal sealed class ReminderModeOption
    {
        public ReminderModeOption(string value, string label)
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

    [DataContract]
    internal sealed class Reminder
    {
        public Reminder()
        {
            Time = "12:00";
            Message = "";
            Mode = ReminderModes.Both;
            Enabled = true;
            LastTriggeredDate = "";
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Time = "12:00";
            Message = "";
            Mode = ReminderModes.Both;
            Enabled = true;
            LastTriggeredDate = "";
        }

        [DataMember(Name = "time")]
        public string Time { get; set; }

        [DataMember(Name = "message")]
        public string Message { get; set; }

        [DataMember(Name = "mode")]
        public string Mode { get; set; }

        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "lastTriggered")]
        public string LastTriggeredDate { get; set; }

        public Reminder Clone()
        {
            Reminder copy = new Reminder();
            copy.Time = Time;
            copy.Message = Message;
            copy.Mode = Mode;
            copy.Enabled = Enabled;
            copy.LastTriggeredDate = LastTriggeredDate;
            return copy;
        }

        public bool Normalize()
        {
            string normalizedTime;
            if (!TimeFormatter.TryNormalizeTime(Time, out normalizedTime))
            {
                return false;
            }
            Time = normalizedTime;

            Message = (Message ?? string.Empty).Trim();
            if (Message.Length == 0)
            {
                return false;
            }
            if (Message.Length > 120)
            {
                Message = Message.Substring(0, 120);
            }

            Mode = ReminderModes.Normalize(Mode);

            if (string.IsNullOrEmpty(LastTriggeredDate))
            {
                LastTriggeredDate = "";
            }
            return true;
        }

        public override string ToString()
        {
            return Time + "  " + Message;
        }
    }

    internal sealed class ReminderEventArgs : EventArgs
    {
        public ReminderEventArgs(Reminder reminder)
        {
            Reminder = reminder;
        }

        public Reminder Reminder { get; private set; }
    }

    internal sealed class ReminderPreviewEventArgs : EventArgs
    {
        public ReminderPreviewEventArgs(string time, string message)
        {
            Time = time;
            Message = message;
        }

        public string Time { get; private set; }
        public string Message { get; private set; }
    }

    internal sealed class ReminderDisplayOption
    {
        public ReminderDisplayOption(int seconds, string label)
        {
            Seconds = seconds;
            Label = label;
        }

        public int Seconds { get; private set; }
        public string Label { get; private set; }

        public override string ToString()
        {
            return Label;
        }
    }

    internal static class ReminderDisplayOptions
    {
        private static readonly int[] AllowedSeconds = new int[] { 5, 8, 10, 12, 15 };

        public static List<ReminderDisplayOption> Create()
        {
            List<ReminderDisplayOption> list = new List<ReminderDisplayOption>();
            list.Add(new ReminderDisplayOption(5, "5 秒"));
            list.Add(new ReminderDisplayOption(8, "8 秒"));
            list.Add(new ReminderDisplayOption(10, "10 秒"));
            list.Add(new ReminderDisplayOption(12, "12 秒（默认）"));
            list.Add(new ReminderDisplayOption(15, "15 秒"));
            return list;
        }

        public static int Normalize(int seconds)
        {
            for (int i = 0; i < AllowedSeconds.Length; i++)
            {
                if (AllowedSeconds[i] == seconds)
                {
                    return seconds;
                }
            }
            return 12;
        }
    }

    internal sealed class ReminderPreset
    {
        public ReminderPreset(string time, string message, string buttonText, string mode)
        {
            Time = time;
            Message = message;
            ButtonText = buttonText;
            Mode = mode;
        }

        public string Time { get; private set; }
        public string Message { get; private set; }
        public string ButtonText { get; private set; }
        public string Mode { get; private set; }

        public string Display
        {
            get { return Time + "  " + Message; }
        }
    }

    internal static class ReminderPresets
    {
        public static List<ReminderPreset> Create()
        {
            List<ReminderPreset> list = new List<ReminderPreset>();
            list.Add(new ReminderPreset("09:30", "工作时间开始，先列出今天的重点。", "09:30 · 工作清单", ReminderModes.Work));
            list.Add(new ReminderPreset("12:00", "该吃点美食补充能量了。", "12:00 · 补充能量", ReminderModes.Both));
            list.Add(new ReminderPreset("15:30", "起来伸个懒腰，让眼睛休息一下。", "15:30 · 休息一下", ReminderModes.Work));
            list.Add(new ReminderPreset("18:30", "工作告一段落，切换娱乐状态吧。", "18:30 · 下班切换", ReminderModes.Work));
            list.Add(new ReminderPreset("22:30", "玩得开心，也别忘了喝水。", "22:30 · 喝水", ReminderModes.Leisure));
            list.Add(new ReminderPreset("23:00", "已经很晚了，早点休息。", "23:00 · 早点休息", ReminderModes.Leisure));
            list.Add(new ReminderPreset("01:00", "夜深了，该下游戏休息了。", "01:00 · 深夜下机", ReminderModes.Leisure));
            return list;
        }
    }

    internal static class ReminderDefaults
    {
        public static List<Reminder> Create()
        {
            List<Reminder> list = new List<Reminder>();

            Reminder workMorning = new Reminder();
            workMorning.Time = "09:30";
            workMorning.Message = "工作时间开始，先列出今天的重点。";
            workMorning.Mode = ReminderModes.Work;
            list.Add(workMorning);

            Reminder noon = new Reminder();
            noon.Time = "12:00";
            noon.Message = "该吃点美食补充能量了。";
            noon.Mode = ReminderModes.Both;
            list.Add(noon);

            Reminder workEvening = new Reminder();
            workEvening.Time = "18:30";
            workEvening.Message = "工作告一段落，切换娱乐状态吧。";
            workEvening.Mode = ReminderModes.Work;
            list.Add(workEvening);

            Reminder night = new Reminder();
            night.Time = "23:00";
            night.Message = "已经很晚了，早点休息。";
            night.Mode = ReminderModes.Leisure;
            list.Add(night);

            Reminder lateNight = new Reminder();
            lateNight.Time = "01:00";
            lateNight.Message = "夜深了，该下游戏休息了。";
            lateNight.Mode = ReminderModes.Leisure;
            list.Add(lateNight);

            return list;
        }
    }
}
