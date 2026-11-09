// 日程（Schedule）领域模型与到点判定：一条“日期 + 时间 + 分类 + 提前量”的日程记录。
// 调用方：SettingsWindow 的日程页与 ScheduleEditorWindow 负责增删改，ClockWindow 每分钟扫描，
// 由 ScheduleService 算出本次发生时刻并用 occurrence key 去重，AppSettings 负责整体持久化。
// 约束：本文件不持有窗口引用、不播放声音、不读系统时钟；当前时间一律由调用方以参数传入。
// 重复规则很克制：只有生日/纪念日按“每年”重复，其余分类都是一次性固定日期，不处理每周/每月。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace GameClock
{

    /// <summary>
    /// 日程分类下拉框的一项：<see cref="Value"/> 是落盘用的英文常量，<see cref="Label"/> 是中文显示名。
    /// 不可变对象，创建后只读；<see cref="ToString"/> 让下拉框直接显示中文。
    /// </summary>
    internal sealed class ScheduleCategoryOption
    {

        /// <summary>保存分类取值与显示文案，构造函数不做合法性校验。</summary>
        public ScheduleCategoryOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        /// <summary>落盘用的分类常量，必定是 <see cref="ScheduleCategories"/> 的成员之一。</summary>
        public string Value { get; private set; }

        /// <summary>界面显示文案，如“生日”。</summary>
        public string Label { get; private set; }

        /// <summary>让下拉框直接显示 <see cref="Label"/>，无需 DisplayMemberPath。</summary>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 日程分类的字符串常量集合与配套规则：分类决定图标/文案，也决定是否按年重复
    /// （只有生日与纪念日算“每年一次”，见 <see cref="IsAnnual"/>）。
    /// 所有外部输入都经 <see cref="Normalize"/> 收敛，未知分类一律回落“会议”。
    /// </summary>
    internal static class ScheduleCategories
    {
        // ===== 分类取值 =====

        /// <summary>生日：按年重复，遇到 2 月 29 日由 <see cref="ScheduleService"/> 做月末收敛。</summary>
        public const string Birthday = "birthday";

        /// <summary>纪念日：按年重复，判定规则与生日相同。</summary>
        public const string Anniversary = "anniversary";

        /// <summary>会议：默认分类，一次性固定日期。</summary>
        public const string Meeting = "meeting";

        /// <summary>机票：一次性固定日期，通常配合较长的提前量。</summary>
        public const string Flight = "flight";

        /// <summary>其他：一次性固定日期，兜住不属于上面四类的日程。</summary>
        public const string Other = "other";

        // ===== 选项与归一 =====

        /// <summary>生成下拉框选项；顺序即显示顺序，与 <see cref="DisplayName"/> 的分支顺序无关。</summary>
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
        /// 把任意输入收敛成五个合法分类之一。忽略大小写以容忍手改配置；
        /// 未知值回落 <see cref="Meeting"/>，因为会议是默认分类，显示上最不突兀。
        /// </summary>
        public static string Normalize(string type)
        {
            // 逐个常量做序号比较，命中即返回，避免大小写与区域设置影响结果。
            if (string.Equals(type, Birthday, StringComparison.OrdinalIgnoreCase))
            {
                return Birthday;
            }

            if (string.Equals(type, Anniversary, StringComparison.OrdinalIgnoreCase))
            {
                return Anniversary;
            }

            // 先判机票再判其他：两者都是一次性，但文案不同，不能合并。
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

        /// <summary>
        /// 取分类的中文名。先收敛再比较，未知分类显示为“会议”；
        /// 这里的单行 if 写法与文件其余部分一致，便于一眼看完五个分支。
        /// </summary>
        public static string DisplayName(string type)
        {
            string normalized = Normalize(type);
            if (normalized == Birthday) return "生日";
            // 纪念日与生日同属按年重复，但显示文案必须区分。
            if (normalized == Anniversary) return "纪念日";
            if (normalized == Flight) return "机票";
            if (normalized == Other) return "其他";
            return "会议";
        }

        /// <summary>
        /// 判断分类是否按年重复。只有生日与纪念日返回 true，
        /// 其余分类都按一次性固定日期处理，到点判定见 <see cref="ScheduleService.TryFindDueOccurrence"/>。
        /// </summary>
        public static bool IsAnnual(string type)
        {
            string normalized = Normalize(type);
            // 两个按年分类并列判断；写成 || 是为了让调用方只关心“是否每年”，不必知道具体是哪一个。
            return normalized == Birthday || normalized == Anniversary;
        }
    }

    /// <summary>
    /// 提前量下拉框的一项：<see cref="Minutes"/> 是提前的分钟数（0 表示准时），<see cref="Label"/> 是中文文案。
    /// 不可变选项对象，取值必须来自 <see cref="ScheduleAdvanceOptions"/> 的白名单。
    /// </summary>
    internal sealed class ScheduleAdvanceOption
    {

        /// <summary>保存提前分钟数与显示文案，不做范围校验。</summary>
        public ScheduleAdvanceOption(int minutes, string label)
        {
            Minutes = minutes;
            Label = label;
        }

        /// <summary>提前量（分钟）；0 表示到点才提醒。</summary>
        public int Minutes { get; private set; }

        /// <summary>界面显示文案，如“提前 15 分钟”。</summary>
        public string Label { get; private set; }

        /// <summary>让下拉框直接显示 <see cref="Label"/>。</summary>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 提前量的可选档位与归一化：从准时到提前 1 天共 8 档，
    /// 手改配置写出其它分钟数时回落 15 分钟（默认档），避免出现“提前 3 分钟”这类不可选状态。
    /// </summary>
    internal static class ScheduleAdvanceOptions
    {
        // ===== 选项与归一 =====

        /// <summary>
        /// 生成提前量选项，顺序由小到大。每次调用都新建列表，
        /// 因为 WPF 下拉框会持有并可能修改这些选项对象，共享实例容易被误改。
        /// </summary>
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
            // 1440 = 24 小时，用于机票、生日这类需要提前一天准备的事项。
            list.Add(new ScheduleAdvanceOption(1440, "提前 1 天"));
            return list;
        }

        /// <summary>
        /// 把任意分钟数收敛到白名单：命中就原样返回，否则回落 15。
        /// 每次调用 <see cref="Create"/> 会产生 8 个小对象，但归一化只在保存与触发时发生，代价可忽略。
        /// </summary>
        public static int Normalize(int minutes)
        {
            List<ScheduleAdvanceOption> options = Create();
            // 线性扫描 8 个档位；命中即返回原值，保证配置里存的值与界面选项一一对应。
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Minutes == minutes)
                {
                    return minutes;
                }
            }

            return 15;
        }

        /// <summary>
        /// 取提前量的中文文案。先归一再去匹配，因此越界值显示的是“提前 15 分钟”，
        /// 与实际生效的兜底值一致，不会出现显示与行为不符。
        /// </summary>
        public static string DisplayName(int minutes)
        {
            int normalized = Normalize(minutes);
            List<ScheduleAdvanceOption> options = Create();
            // 用归一后的值再查一次表，而不是直接拼字符串，保证文案与下拉框完全同源。
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
    /// 一条日程：分类 + 标题 + 日期 + 时间 + 提前量 + 是否按年重复 + 备注 + 去重键。
    /// 由设置窗口与日程编辑窗维护，<see cref="ClockWindow"/> 每分钟交给 <see cref="ScheduleService"/> 判定，
    /// 命中后回写 <see cref="LastTriggeredKey"/> 防止同一次发生重复播报；本类不负责弹窗与声音。
    /// </summary>
    [DataContract]
    internal sealed class ScheduleItem
    {

        /// <summary>新建一条可用日程：会议、今天、09:00、提前 15 分钟、不按年、启用。</summary>
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
        /// DataContract 反序列化不调用构造函数，这里补一次同样的默认值，
        /// 保证旧配置缺少新增字段时不会出现 null 日期或 null 去重键。
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Type = ScheduleCategories.Meeting;
            Title = "";
            // 默认日期取“今天”，用不受区域设置影响的固定格式，避免反序列化后解析失败。
            Date = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Time = "09:00";
            AdvanceMinutes = 15;
            Annual = false;
            Enabled = true;
            Note = "";
            LastTriggeredKey = "";
        }

        // ===== 模型字段 =====

        /// <summary>日程分类，取 <see cref="ScheduleCategories"/> 的常量；决定图标文案与是否按年重复。</summary>
        [DataMember(Name = "type")]
        public string Type { get; set; }

        /// <summary>日程标题，最多 80 字；空白视为无效，超长在归一化时截断。</summary>
        [DataMember(Name = "title")]
        public string Title { get; set; }

        /// <summary>日程日期，固定格式 <c>yyyy-MM-dd</c>；用字符串存是为了让配置文件可读、跨区域稳定。</summary>
        [DataMember(Name = "date")]
        public string Date { get; set; }

        /// <summary>日程时间，格式 <c>HH:mm</c>；与日期分开存，便于按年重复时只替换年份。</summary>
        [DataMember(Name = "time")]
        public string Time { get; set; }

        /// <summary>提前提醒的分钟数，必须落在 <see cref="ScheduleAdvanceOptions"/> 的白名单内。</summary>
        [DataMember(Name = "advanceMinutes")]
        public int AdvanceMinutes { get; set; }

        /// <summary>是否按年重复；只有生日/纪念日会被置为 true，但落盘独立保存，允许用户显式改。</summary>
        [DataMember(Name = "annual")]
        public bool Annual { get; set; }

        /// <summary>是否启用；停用的日程保留在列表里但不会被扫描触发。</summary>
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>备注，最多 240 字；可为空，超长在归一化时截断。</summary>
        [DataMember(Name = "note")]
        public string Note { get; set; }

        /// <summary>
        /// 最近一次已播报的发生实例键（<c>yyyy-MM-dd HH:mm</c>）。
        /// 用具体发生时刻而不是“今天”，才能让按年重复的日程在跨年后仍然只响一次。
        /// </summary>
        [DataMember(Name = "lastTriggeredKey")]
        public string LastTriggeredKey { get; set; }

        // ===== 复制与清洗 =====

        /// <summary>
        /// 逐字段复制出一条独立实例，供设置窗口编辑副本使用，避免改动已生效的日程。
        /// </summary>
        public ScheduleItem Clone()
        {
            ScheduleItem copy = new ScheduleItem();
            // 全部字段是值类型或不可变字符串，直接赋值即可。
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
        /// 就地清洗并校验：标题必填、日期与时间必须可解析，其余字段收敛到合法范围。
        /// 返回 false 表示这条日程不可用（标题空白、日期或时间非法），调用方应剔除；
        /// 标题/备注超长只截断，提前量越界回落默认档，尽量不丢用户的输入。
        /// </summary>
        public bool Normalize()
        {
            // 分类先归一，后续判断“是否按年”与显示文案都依赖它。
            Type = ScheduleCategories.Normalize(Type);

            // 标题是列表里唯一可辨识的内容，去空白后为空即判无效。
            Title = (Title ?? string.Empty).Trim();
            if (Title.Length == 0)
            {
                return false;
            }

            // 超长标题截断而不是拒绝，保留前 80 字。
            if (Title.Length > 80)
            {
                Title = Title.Substring(0, 80);
            }

            // 日期必须严格匹配 yyyy-MM-dd：宽松解析会把 "2026-2-3" 之类的输入悄悄改写成别的日期。
            DateTime date;
            if (!ScheduleService.TryParseDate(Date, out date))
            {
                return false;
            }

            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string normalizedTime;
            // 时间统一成 HH:mm；解析失败判无效，避免留下永远不会触发的日程。
            if (!TimeFormatter.TryNormalizeTime(Time, out normalizedTime))
            {
                return false;
            }

            Time = normalizedTime;

            // 提前量收敛到白名单，防止手改配置后出现界面无法表达的档位。
            AdvanceMinutes = ScheduleAdvanceOptions.Normalize(AdvanceMinutes);

            // 备注是可选字段，只做去空白与截断，不参与有效性判断。
            Note = (Note ?? string.Empty).Trim();
            if (Note.Length > 240)
            {
                Note = Note.Substring(0, 240);
            }

            // 老配置可能没有这个字段，反序列化后为 null；统一成空串，后续比较不必判空。
            if (LastTriggeredKey == null)
            {
                LastTriggeredKey = "";
            }

            return true;
        }
    }

    /// <summary>
    /// 日程触发事件的参数：带上触发的那条日程与本次具体发生时刻。
    /// 发生时刻是“算出来的这一次”，按年重复时年份可能与配置里的日期不同，订阅方应以它为准。
    /// </summary>
    internal sealed class ScheduleTriggeredEventArgs : EventArgs
    {

        /// <summary>保存日程引用与发生时刻；日程不复制，订阅方不应修改它。</summary>
        public ScheduleTriggeredEventArgs(ScheduleItem schedule, DateTime occurrence)
        {
            Schedule = schedule;
            Occurrence = occurrence;
        }

        /// <summary>刚刚触发的日程。</summary>
        public ScheduleItem Schedule { get; private set; }

        /// <summary>本次发生时刻（含年份），用于显示与生成去重键。</summary>
        public DateTime Occurrence { get; private set; }
    }

    /// <summary>
    /// 日程判定服务：纯函数集合，负责日期解析、到点窗口判定与去重键生成。
    /// 不持有状态、不碰 UI，当前时间由调用方传入，因此可以在任意线程调用，也便于手工验证。
    /// </summary>
    internal static class ScheduleService
    {
        // ===== 解析与判定 =====

        /// <summary>
        /// 严格解析 <c>yyyy-MM-dd</c> 日期文本，供本类与设置窗口共用。
        /// 用 <see cref="CultureInfo.InvariantCulture"/> 避免不同区域对分隔符/日历的差异；null 按空串处理。
        /// </summary>
        public static bool TryParseDate(string text, out DateTime date)
        {
            return DateTime.TryParseExact(
                (text ?? string.Empty).Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        /// <summary>
        /// 判断日程此刻是否处于提醒窗口内，并输出本次发生时刻。
        /// 一次性日程按配置日期；按年日程在“去年/今年/明年”三个年份里逐个试，覆盖跨年与补提醒。
        /// 输入日期或时间非法时返回 false，不抛异常，调用方按“未到点”处理即可。
        /// </summary>
        /// <param name="item">待判定的日程，内部只读，不会修改。</param>
        /// <param name="now">当前本地时间，由调用方传入以保证可测试。</param>
        /// <param name="occurrence">命中时输出本次发生时刻，未命中为 <c>default(DateTime)</c>。</param>
        public static bool TryFindDueOccurrence(ScheduleItem item, DateTime now, out DateTime occurrence)
        {
            occurrence = default(DateTime);

            DateTime date;
            // 日期非法就直接放弃：无法确定哪一天，任何回退猜测都可能误报。
            if (!TryParseDate(item.Date, out date))
            {
                return false;
            }

            DateTime time;
            // 时间非法同样放弃；格式必须是 HH:mm，与界面输入限制一致。
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

            // 按年重复：年份不固定，需要在相邻三年里找“当前处于提醒窗口”的那一次。
            if (item.Annual)
            {
                // 偏移 -1/0/+1 覆盖跨年夜与提前一天提醒：只试今年会漏掉 12 月 31 日提醒次年 1 月 1 日的情况。
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

            // 一次性日程：把配置里的日期与时间拼成完整时刻，秒固定为 0（时间只到分钟）。
            DateTime fixedOccurrence = date.Date.AddHours(time.Hour).AddMinutes(time.Minute);

            // 命中提醒窗口就返回该时刻，由调用方据此生成去重键。
            if (IsInReminderWindow(now, fixedOccurrence, advance))
            {
                occurrence = fixedOccurrence;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 生成一次发生实例的去重键，精确到分钟：<c>yyyy-MM-dd HH:mm</c>。
        /// 键里带年份，按年重复的日程在跨年后会得到不同的键，因此不会被去年的记录挡住。
        /// </summary>
        public static string GetOccurrenceKey(DateTime occurrence)
        {
            return occurrence.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        // ===== 内部工具 =====

        /// <summary>
        /// 把“月/日/时间”套到指定年份上。2 月 29 日在平年不存在，
        /// 用当月最后一天兜底（2 月 28 日），保证生日/纪念日每年都有落点而不是抛异常。
        /// </summary>
        private static DateTime MakeAnnualOccurrence(int year, DateTime original, DateTime time)
        {
            int day = Math.Min(original.Day, DateTime.DaysInMonth(year, original.Month));
            return new DateTime(year, original.Month, day, time.Hour, time.Minute, 0);
        }

        /// <summary>
        /// 提醒窗口判定：从“发生时刻 - 提前量”开始，到“发生时刻 + 30 分钟”为止都算到点。
        /// 向后多留 30 分钟，是为了容忍轮询间隔、休眠唤醒与系统卡顿造成的晚到，避免错过一次提醒。
        /// </summary>
        private static bool IsInReminderWindow(DateTime now, DateTime occurrence, int advanceMinutes)
        {
            DateTime remindAt = occurrence.AddMinutes(-advanceMinutes);
            return now >= remindAt && now <= occurrence.AddMinutes(30);
        }
    }
}
