// 日程领域模型与到期计算：类别/提前量选项、ScheduleItem 序列化实体、纯函数式的
// 「当前时刻是否落在提醒窗口内」判断，以及发生实例键（occurrence key）的生成。
// 调用方：设置窗口构建编辑界面并做保存前校验；ClockWindow 每秒 tick 调用 TryFindDueOccurrence。
// 边界：本文件只做纯计算，不弹窗、不持有窗口/计时器引用；TryFindDueOccurrence 位于秒级热路径，
// 不得在其中写日志（否则日志被刷屏），只允许在 Normalize 真正丢弃用户数据时记一条 Warn。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace GameClock
{
    /// <summary>
    /// 日程类别下拉框选项载体：Value 为持久化值，Label 为中文显示名。
    /// </summary>
    internal sealed class ScheduleCategoryOption
    {
        /// <summary>用持久化值与显示名构造选项，之后两字段只读。</summary>
        public ScheduleCategoryOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        /// <summary>写入 settings.json 的类别值（birthday/anniversary/meeting/flight/other）。</summary>
        public string Value { get; private set; }

        /// <summary>界面展示的中文名称。</summary>
        public string Label { get; private set; }

        /// <summary>ComboBox 默认显示 ToString 结果。</summary>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 日程类别：五个固定值决定显示名、是否按年重复（生日/纪念日）以及气泡标题前缀。
    /// </summary>
    internal static class ScheduleCategories
    {
        // 持久化值：保持全小写，旧配置升级后仍能被 Normalize 识别。
        public const string Birthday = "birthday";
        public const string Anniversary = "anniversary";
        public const string Meeting = "meeting";
        public const string Flight = "flight";
        public const string Other = "other";

        /// <summary>构造设置窗口下拉框用的类别列表；顺序固定，方便用户形成肌肉记忆。</summary>
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

        /// <summary>
        /// 把任意输入收敛为五个合法类别之一；未知值回落到「会议」而不是丢弃整条日程。
        /// </summary>
        public static string Normalize(string type)
        {
            // 大小写不敏感：手改配置写成 Meeting/MEETING 也能识别。
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
            // 会议是使用频率最高的类别，作为未知值的兜底可避免日程丢失。
            return Meeting;
        }

        /// <summary>类别中文显示名，用作托盘气泡标题前缀（如「生日日程」）。</summary>
        public static string DisplayName(string type)
        {
            string normalized = Normalize(type);
            // 一元 if 保持与原实现一致：只取显示名，不做其它副作用。
            if (normalized == Birthday) return "生日";
            if (normalized == Anniversary) return "纪念日";
            if (normalized == Flight) return "机票";
            if (normalized == Other) return "其他";
            return "会议";
        }

        /// <summary>
        /// 是否按年重复：生日与纪念日每年同一月日触发，其余类别只在指定日期触发一次。
        /// </summary>
        public static bool IsAnnual(string type)
        {
            string normalized = Normalize(type);
            return normalized == Birthday || normalized == Anniversary;
        }
    }

    /// <summary>
    /// 提前量选项载体：Minutes 为提前分钟数（0 表示准时，1440 表示提前 1 天）。
    /// </summary>
    internal sealed class ScheduleAdvanceOption
    {
        /// <summary>用提前分钟数与显示名构造选项。</summary>
        public ScheduleAdvanceOption(int minutes, string label)
        {
            Minutes = minutes;
            Label = label;
        }

        /// <summary>提前的分钟数，触发窗口由它决定。</summary>
        public int Minutes { get; private set; }

        /// <summary>下拉框展示的中文名称（如「提前 15 分钟」）。</summary>
        public string Label { get; private set; }

        /// <summary>ComboBox 默认显示 ToString 结果。</summary>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 日程提前量：限定为 0/5/10/15/30/60/120/1440 分钟八档，非法值回落到默认 15 分钟。
    /// </summary>
    internal static class ScheduleAdvanceOptions
    {
        /// <summary>构造设置窗口下拉框的提前量选项；顺序即界面顺序。</summary>
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

        /// <summary>把配置里的提前量收敛到白名单；不在白名单内返回默认 15 分钟。</summary>
        public static int Normalize(int minutes)
        {
            List<ScheduleAdvanceOption> options = Create();
            // 选项只有 8 项，线性扫描即可；Normalize 不在 tick 热路径上。
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Minutes == minutes)
                {
                    return minutes;
                }
            }
            return 15;
        }

        /// <summary>把提前量分钟数转成中文显示名；非法值先归一化再匹配，最终兜底「提前 15 分钟」。</summary>
        public static string DisplayName(int minutes)
        {
            int normalized = Normalize(minutes);
            List<ScheduleAdvanceOption> options = Create();
            // 与 Normalize 使用同一份白名单，保证显示值与实际触发提前量一致。
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

    /// <summary>
    /// 单条日程的序列化实体：类别 + 标题 + 日期 + 时间 + 提前量 + 是否每年重复 + 备注 + 去重键。
    /// LastTriggeredKey 记录最近一次已提醒的发生实例（yyyy-MM-dd HH:mm），用于避免重复弹窗。
    /// </summary>
    [DataContract]
    internal sealed class ScheduleItem
    {
        /// <summary>新建日程的默认值：会议、今天 09:00、提前 15 分钟、启用，保证空对象也能写出合法 JSON。</summary>
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

        /// <summary>
        /// 反序列化前先填默认值：旧版本配置缺字段时字段不会停在 null/0，
        /// 后续 Normalize 与到期计算无需到处判空。
        /// </summary>
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

        /// <summary>日程类别值（birthday/anniversary/meeting/flight/other）。</summary>
        [DataMember(Name = "type")]
        public string Type { get; set; }

        /// <summary>日程标题，最多 80 字；为空视为无效日程，保存时会被丢弃。</summary>
        [DataMember(Name = "title")]
        public string Title { get; set; }

        /// <summary>日程日期，固定 yyyy-MM-dd；按年重复时只使用其中的月与日。</summary>
        [DataMember(Name = "date")]
        public string Date { get; set; }

        /// <summary>日程时间，固定 HH:mm（24 小时制）。</summary>
        [DataMember(Name = "time")]
        public string Time { get; set; }

        /// <summary>提前提醒的分钟数，白名单见 ScheduleAdvanceOptions。</summary>
        [DataMember(Name = "advanceMinutes")]
        public int AdvanceMinutes { get; set; }

        /// <summary>是否按年重复；生日/纪念日通常为 true，但以用户实际配置为准。</summary>
        [DataMember(Name = "annual")]
        public bool Annual { get; set; }

        /// <summary>是否启用；关闭后不参与到期判断，但仍保留在配置中。</summary>
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>备注，最多 240 字，展示在提醒气泡第二行。</summary>
        [DataMember(Name = "note")]
        public string Note { get; set; }

        /// <summary>最近一次已提醒的发生实例键（yyyy-MM-dd HH:mm），空串表示尚未提醒过。</summary>
        [DataMember(Name = "lastTriggeredKey")]
        public string LastTriggeredKey { get; set; }

        /// <summary>深拷贝一条日程；设置窗口编辑副本时使用，避免改动正在生效的对象。</summary>
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

        /// <summary>
        /// 归一化并校验：标题为空、日期或时间非法时返回 false（调用方应丢弃该条）；
        /// 类别与提前量收敛到合法值，超长字段按上限截断。
        /// </summary>
        /// <returns>true 表示日程有效；false 表示应被移除。</returns>
        public bool Normalize()
        {
            // 类别未知时回落为会议，避免整条日程因拼写问题消失。
            Type = ScheduleCategories.Normalize(Type);

            Title = (Title ?? string.Empty).Trim();
            // 没有标题的日程在气泡里无法辨识，直接判为无效。
            if (Title.Length == 0)
            {
                AppLog.Warn("Schedule", "丢弃缺少标题的日程（date=" + (Date ?? "") + "）");
                return false;
            }
            // 与设置窗口的输入上限保持一致，旧配置超长时截断而不是整条丢弃。
            if (Title.Length > 80)
            {
                Title = Title.Substring(0, 80);
            }

            DateTime date;
            // 日期必须严格为 yyyy-MM-dd：宽松解析会把 2026-1-5 之类歧义输入带进配置。
            if (!ScheduleService.TryParseDate(Date, out date))
            {
                AppLog.Warn("Schedule", "丢弃日期格式不正确的日程（title=" + Title + "，date=" + (Date ?? "") + "，需为 yyyy-MM-dd）");
                return false;
            }
            // 回写规范格式，统一补零（2026-01-05），后续字符串比较与排序都依赖它。
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string normalizedTime;
            // 时间复用提醒模块的统一解析，保证 HH:mm 规范与提醒一致。
            if (!TimeFormatter.TryNormalizeTime(Time, out normalizedTime))
            {
                AppLog.Warn("Schedule", "丢弃时间格式不正确的日程（title=" + Title + "，time=" + (Time ?? "") + "，需为 HH:mm）");
                return false;
            }
            Time = normalizedTime;

            // 提前量不在白名单内（含旧版本值）时收敛到默认 15 分钟。
            AdvanceMinutes = ScheduleAdvanceOptions.Normalize(AdvanceMinutes);

            Note = (Note ?? string.Empty).Trim();
            // 备注只是补充信息，超长时截断即可，不因此丢弃整条日程。
            if (Note.Length > 240)
            {
                Note = Note.Substring(0, 240);
            }
            // 旧配置可能缺该字段，反序列化后为 null，统一下降为空串。
            if (LastTriggeredKey == null)
            {
                LastTriggeredKey = "";
            }
            return true;
        }
    }

    /// <summary>
    /// 日程触发事件参数：带上命中的日程与本次发生时间，供控制器生成托盘气泡文案。
    /// </summary>
    internal sealed class ScheduleTriggeredEventArgs : EventArgs
    {
        /// <summary>包装命中的日程与发生实例时间；订阅方只读，不应修改配置对象。</summary>
        public ScheduleTriggeredEventArgs(ScheduleItem schedule, DateTime occurrence)
        {
            Schedule = schedule;
            Occurrence = occurrence;
        }

        /// <summary>本次触发的日程实例。</summary>
        public ScheduleItem Schedule { get; private set; }

        /// <summary>本次命中的发生时间（含按年重复推算出的当年日期）。</summary>
        public DateTime Occurrence { get; private set; }
    }

    /// <summary>
    /// 日程到期计算的纯函数集合：解析日期/时间、判断当前时刻是否落在提醒窗口、生成去重键。
    /// 不持有状态、不弹窗；ClockWindow 每秒 tick 调用一次。
    /// </summary>
    internal static class ScheduleService
    {
        /// <summary>
        /// 严格解析 yyyy-MM-dd（InvariantCulture）；失败返回 false 且 date 为 default(DateTime)。
        /// </summary>
        public static bool TryParseDate(string text, out DateTime date)
        {
            // 先 Trim：手改配置时常带首尾空格，宽松处理不影响严格格式校验。
            return DateTime.TryParseExact(
                (text ?? string.Empty).Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        /// <summary>
        /// 判断日程在 now 时刻是否处于提醒窗口内，是则输出本次发生时间 occurrence。
        /// 按年重复的日程会尝试去年/今年/明年三个年份，以覆盖跨年提前量与 30 分钟宽限。
        /// 本方法在秒级 tick 热路径上，禁止写日志。
        /// </summary>
        /// <param name="item">待判断的日程（调用方已确保 Enabled）。</param>
        /// <param name="now">当前本地时间。</param>
        /// <param name="occurrence">命中的发生时间；未命中为 default(DateTime)。</param>
        /// <returns>是否命中提醒窗口。</returns>
        public static bool TryFindDueOccurrence(ScheduleItem item, DateTime now, out DateTime occurrence)
        {
            occurrence = default(DateTime);
            DateTime date;
            // 日期非法直接跳过；Normalize 已保证合法，这里防御手改配置。
            if (!TryParseDate(item.Date, out date))
            {
                return false;
            }

            DateTime time;
            // 时间非法同样跳过；解析到 1900-01-01 的时分部分，只用 Hour/Minute。
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
            // 按年重复：生日/纪念日只保留月日，年份取当前年附近重新拼装。
            if (item.Annual)
            {
                // 偏移 -1..+1 覆盖跨年窗口：如 1 月 1 日 00:10 可能命中去年 12 月 31 日的发生实例。
                for (int offset = -1; offset <= 1; offset++)
                {
                    int year = now.Year + offset;
                    DateTime candidate = MakeAnnualOccurrence(year, date, time);
                    // 命中窗口即返回该候选，保证同一次触发只有一个发生实例。
                    if (IsInReminderWindow(now, candidate, advance))
                    {
                        occurrence = candidate;
                        return true;
                    }
                }
                return false;
            }

            // 非重复日程：日期取配置值，时间取配置时分，秒固定为 0 以便同日去重。
            DateTime fixedOccurrence = date.Date.AddHours(time.Hour).AddMinutes(time.Minute);
            // 只在窗口内触发，避免启动时补发早已过去的日程。
            if (IsInReminderWindow(now, fixedOccurrence, advance))
            {
                occurrence = fixedOccurrence;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 生成发生实例的去重键（yyyy-MM-dd HH:mm）；写入 LastTriggeredKey 后，
        /// 同一实例（即使重启进程）不会重复提醒。
        /// </summary>
        public static string GetOccurrenceKey(DateTime occurrence)
        {
            return occurrence.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 按年重复日程在指定年份的发生时间；2 月 29 日在平年会被夹到 2 月 28 日。
        /// </summary>
        private static DateTime MakeAnnualOccurrence(int year, DateTime original, DateTime time)
        {
            // DaysInMonth 随年份变化：平年 2 月只有 28 天，闰日生日/纪念日退到 28 日触发。
            int day = Math.Min(original.Day, DateTime.DaysInMonth(year, original.Month));
            return new DateTime(year, original.Month, day, time.Hour, time.Minute, 0);
        }

        /// <summary>
        /// 提醒窗口 = [发生时间 - 提前量, 发生时间 + 30 分钟]。
        /// 尾部 30 分钟宽限用于容忍休眠/卡顿或启动稍晚，避免刚过点就永久漏提醒。
        /// </summary>
        private static bool IsInReminderWindow(DateTime now, DateTime occurrence, int advanceMinutes)
        {
            DateTime remindAt = occurrence.AddMinutes(-advanceMinutes);
            // 闭区间：正好落在起止分钟内也算命中，减少边界抖动导致的漏触发。
            return now >= remindAt && now <= occurrence.AddMinutes(30);
        }
    }
}
