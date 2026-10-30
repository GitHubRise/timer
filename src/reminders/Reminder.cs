// 提醒领域模型：当前状态（工作/娱乐/全部）的规范值与过滤、提醒实体及反序列化默认值、
// 提醒显示时长选项与预设模板。UI（设置窗口、右键菜单）与控制器经这些类型读写 settings.json。
// 边界：本文件不持有窗口/计时器引用，也不触发提醒；Normalize 只修正数据并按 false 报告
// 「应丢弃」的条目。触发判断在 ClockWindow 的秒级 tick 中完成，因此这里不得按 tick 记日志，
// 仅在对配置做归一化、真正丢弃用户数据时记一条 Warn。

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GameClock
{
    /// <summary>
    /// 提醒状态过滤：每条提醒可设为「全部状态 / 仅工作 / 仅娱乐」，触发时与当前状态比对。
    /// 字符串值同时是 settings.json 中持久化的 mode 字段。
    /// </summary>
    internal static class ReminderModes
    {
        // 持久化值：保持全小写，旧配置升级后仍能被 Normalize 识别。
        public const string Both = "both";
        public const string Work = "work";
        public const string Leisure = "leisure";

        /// <summary>
        /// 把任意输入收敛为三个合法值之一；非法输入（含 null、空串、大小写变体）一律归为 Both。
        /// </summary>
        public static string Normalize(string mode)
        {
            // 大小写不敏感比较：手改配置文件写成 Work/WORK 也能识别。
            if (string.Equals(mode, Work, StringComparison.OrdinalIgnoreCase))
            {
                return Work;
            }
            if (string.Equals(mode, Leisure, StringComparison.OrdinalIgnoreCase))
            {
                return Leisure;
            }
            // 未知值按「全部状态」处理：宁可多提醒，也不要静默丢失提醒。
            return Both;
        }

        /// <summary>状态值的中文显示名（「工作」「娱乐」「全部状态」），用于菜单与提示。</summary>
        public static string DisplayName(string mode)
        {
            string normalized = Normalize(mode);
            // 归一化后只剩三种可能，用简单分支而非字典，避免额外分配。
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

        /// <summary>
        /// 判断某条提醒在当前状态下是否应触发：「全部状态」恒为真，否则要求两者一致。
        /// </summary>
        public static bool AppliesToState(string reminderMode, string currentState)
        {
            string mode = Normalize(reminderMode);
            string state = Normalize(currentState);
            // 「全部状态」不受当前状态影响。
            if (mode == Both)
            {
                return true;
            }
            return mode == state;
        }

        /// <summary>构造设置窗口下拉框用的选项列表（顺序固定：全部、仅工作、仅娱乐）。</summary>
        public static List<ReminderModeOption> CreateOptions()
        {
            List<ReminderModeOption> options = new List<ReminderModeOption>();
            options.Add(new ReminderModeOption(Both, "全部状态"));
            options.Add(new ReminderModeOption(Work, "仅工作"));
            options.Add(new ReminderModeOption(Leisure, "仅娱乐"));
            return options;
        }
    }

    /// <summary>
    /// 下拉框选项载体：Value 存持久化值，Label 存中文显示名，ToString 让 ComboBox 直接显示 Label。
    /// </summary>
    internal sealed class ReminderModeOption
    {
        /// <summary>用持久化值与显示名构造选项，两字段之后只读。</summary>
        public ReminderModeOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        /// <summary>写入 settings.json 的规范值（both/work/leisure）。</summary>
        public string Value { get; private set; }

        /// <summary>界面上展示的中文名称。</summary>
        public string Label { get; private set; }

        /// <summary>ComboBox 默认调用 ToString 显示内容，这里返回中文名。</summary>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 单条时间提醒的序列化实体：HH:mm + 内容 + 状态过滤 + 启用开关 + 最近触发日期。
    /// LastTriggeredDate 是「同一天不重复提醒」的去重依据，必须随配置一起持久化。
    /// </summary>
    [DataContract]
    internal sealed class Reminder
    {
        /// <summary>新建提醒的默认值：中午 12:00、全部状态、启用，避免空对象写出非法 JSON。</summary>
        public Reminder()
        {
            Time = "12:00";
            Message = "";
            Mode = ReminderModes.Both;
            Enabled = true;
            LastTriggeredDate = "";
        }

        /// <summary>
        /// 反序列化前先填默认值：旧版本配置缺字段时，字段不会停在 null，
        /// 因此后续 Normalize/触发逻辑无需到处判空。
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Time = "12:00";
            Message = "";
            Mode = ReminderModes.Both;
            Enabled = true;
            LastTriggeredDate = "";
        }

        /// <summary>提醒时间，规范格式 HH:mm（24 小时制），由 TimeFormatter 归一化。</summary>
        [DataMember(Name = "time")]
        public string Time { get; set; }

        /// <summary>提醒内容，最多 120 字；为空视为无效提醒，保存时会被丢弃。</summary>
        [DataMember(Name = "message")]
        public string Message { get; set; }

        /// <summary>状态过滤值（both/work/leisure），触发时与当前状态比对。</summary>
        [DataMember(Name = "mode")]
        public string Mode { get; set; }

        /// <summary>是否启用；关闭后不参与触发，但保留在配置里。</summary>
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>最近一次触发的日期（yyyy-MM-dd），用于同一天去重；空串表示今天还没触发过。</summary>
        [DataMember(Name = "lastTriggered")]
        public string LastTriggeredDate { get; set; }

        /// <summary>深拷贝一条提醒；设置窗口编辑副本时使用，避免直接改动正在生效的对象。</summary>
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

        /// <summary>
        /// 归一化并校验：时间非法或内容为空时返回 false（调用方应丢弃该条），
        /// 超长内容截断到 120 字，状态值收敛到合法枚举。
        /// </summary>
        /// <returns>true 表示该提醒有效；false 表示应被移除。</returns>
        public bool Normalize()
        {
            string normalizedTime;
            // 时间必须先过统一格式解析：手改配置写成 9:5 或 24:00 都不可用。
            if (!TimeFormatter.TryNormalizeTime(Time, out normalizedTime))
            {
                // 记录被丢弃的原始时间，便于用户对照配置找出是哪一条。
                AppLog.Warn("Reminder", "丢弃时间格式不正确的提醒（time=" + (Time ?? "") + "，需为 HH:mm）");
                return false;
            }
            Time = normalizedTime;

            Message = (Message ?? string.Empty).Trim();
            // 没有内容的提醒弹出来没有意义，直接判为无效。
            if (Message.Length == 0)
            {
                AppLog.Warn("Reminder", "丢弃内容为空的提醒（time=" + Time + "）");
                return false;
            }
            // 与设置窗口的输入上限保持一致，旧配置超长时截断而不是整条丢弃。
            if (Message.Length > 120)
            {
                Message = Message.Substring(0, 120);
            }

            // 未知状态值归一为「全部状态」，避免该提醒永远不触发。
            Mode = ReminderModes.Normalize(Mode);

            // 旧配置可能缺 lastTriggered 字段（反序列化后为 null），统一下降为空串。
            if (string.IsNullOrEmpty(LastTriggeredDate))
            {
                LastTriggeredDate = "";
            }
            return true;
        }

        /// <summary>列表/调试展示用文本：「时间  内容」。</summary>
        public override string ToString()
        {
            return Time + "  " + Message;
        }
    }

    /// <summary>
    /// 提醒触发事件参数：ClockWindow 判定某条提醒到期后，把命中的 Reminder 交给控制器弹托盘气泡。
    /// </summary>
    internal sealed class ReminderEventArgs : EventArgs
    {
        /// <summary>包装命中的提醒对象；事件订阅方只读，不应修改正在生效的配置。</summary>
        public ReminderEventArgs(Reminder reminder)
        {
            Reminder = reminder;
        }

        /// <summary>本次触发的提醒实例。</summary>
        public Reminder Reminder { get; private set; }
    }

    /// <summary>
    /// 设置窗口「测试提醒」的事件参数：只需展示时间与内容，不需要真实的 Reminder 实体。
    /// </summary>
    internal sealed class ReminderPreviewEventArgs : EventArgs
    {
        /// <summary>携带预览用的时间文本与消息文本（均来自设置窗口当前编辑副本）。</summary>
        public ReminderPreviewEventArgs(string time, string message)
        {
            Time = time;
            Message = message;
        }

        /// <summary>预览显示的提醒时间，格式 HH:mm。</summary>
        public string Time { get; private set; }

        /// <summary>预览显示的提醒内容。</summary>
        public string Message { get; private set; }
    }

    /// <summary>
    /// 提醒气泡显示时长选项载体：Seconds 为实际秒数，Label 为下拉框显示文本。
    /// </summary>
    internal sealed class ReminderDisplayOption
    {
        /// <summary>用秒数与显示名构造选项。</summary>
        public ReminderDisplayOption(int seconds, string label)
        {
            Seconds = seconds;
            Label = label;
        }

        /// <summary>气泡停留秒数，直接赋给 ClockWindow 的隐藏计时器。</summary>
        public int Seconds { get; private set; }

        /// <summary>下拉框展示的中文名称（如「12 秒（默认）」）。</summary>
        public string Label { get; private set; }

        /// <summary>ComboBox 默认显示 ToString 结果。</summary>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 提醒显示时长：限定为 5/8/10/12/15 秒五档，非法值统一回落到默认 12 秒。
    /// </summary>
    internal static class ReminderDisplayOptions
    {
        // 允许的时长白名单：与设置窗口下拉框保持一致，改这里必须同步改 Create()。
        private static readonly int[] AllowedSeconds = new int[] { 5, 8, 10, 12, 15 };

        /// <summary>构造设置窗口下拉框的时长选项列表，顺序与 AllowedSeconds 一致。</summary>
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

        /// <summary>把配置里的时长收敛到白名单；不在白名单内（含旧版本值）返回默认 12 秒。</summary>
        public static int Normalize(int seconds)
        {
            // 白名单只有 5 项，线性扫描比建字典更省分配。
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

    /// <summary>
    /// 提醒预设模板：设置窗口「常用提醒」的一键填入项；ButtonText 是按钮文案，Mode 预置状态过滤。
    /// </summary>
    internal sealed class ReminderPreset
    {
        /// <summary>用一个完整预设构造模板；字段之后只读，避免被界面修改。</summary>
        public ReminderPreset(string time, string message, string buttonText, string mode)
        {
            Time = time;
            Message = message;
            ButtonText = buttonText;
            Mode = mode;
        }

        /// <summary>预设时间，格式 HH:mm。</summary>
        public string Time { get; private set; }

        /// <summary>预设提醒内容。</summary>
        public string Message { get; private set; }

        /// <summary>设置窗口里的按钮文案（含时间与主题）。</summary>
        public string ButtonText { get; private set; }

        /// <summary>预设的状态过滤值（both/work/leisure）。</summary>
        public string Mode { get; private set; }

        /// <summary>按钮/提示统一展示的「时间  内容」文本。</summary>
        public string Display
        {
            get { return Time + "  " + Message; }
        }
    }

    /// <summary>
    /// 内置提醒预设清单；只在设置窗口构建界面时调用，不参与运行时触发。
    /// </summary>
    internal static class ReminderPresets
    {
        /// <summary>返回全部预设；顺序即界面按钮顺序。</summary>
        public static List<ReminderPreset> Create()
        {
            List<ReminderPreset> list = new List<ReminderPreset>();
            // 预设覆盖工作开始、午间、下午休息、下班切换与深夜收尾，状态过滤跟随场景。
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

    /// <summary>
    /// 首次启动时的默认提醒列表（不启用预设模板的那 7 条，只保留 5 条核心提醒）。
    /// </summary>
    internal static class ReminderDefaults
    {
        /// <summary>构造默认提醒集合；每次调用都返回新对象，避免共享可变状态。</summary>
        public static List<Reminder> Create()
        {
            List<Reminder> list = new List<Reminder>();

            // 09:30 工作开始：仅工作状态触发。
            Reminder workMorning = new Reminder();
            workMorning.Time = "09:30";
            workMorning.Message = "工作时间开始，先列出今天的重点。";
            workMorning.Mode = ReminderModes.Work;
            list.Add(workMorning);

            // 12:00 午间补充能量：任何状态都提醒。
            Reminder noon = new Reminder();
            noon.Time = "12:00";
            noon.Message = "该吃点美食补充能量了。";
            noon.Mode = ReminderModes.Both;
            list.Add(noon);

            // 18:30 下班切换：仅工作状态触发，提示切到娱乐模式。
            Reminder workEvening = new Reminder();
            workEvening.Time = "18:30";
            workEvening.Message = "工作告一段落，切换娱乐状态吧。";
            workEvening.Mode = ReminderModes.Work;
            list.Add(workEvening);

            // 23:00 早点休息：仅娱乐状态触发，不影响加班场景。
            Reminder night = new Reminder();
            night.Time = "23:00";
            night.Message = "已经很晚了，早点休息。";
            night.Mode = ReminderModes.Leisure;
            list.Add(night);

            // 01:00 深夜下机：仅娱乐状态触发，作为通宵兜底提醒。
            Reminder lateNight = new Reminder();
            lateNight.Time = "01:00";
            lateNight.Message = "夜深了，该下游戏休息了。";
            lateNight.Mode = ReminderModes.Leisure;
            list.Add(lateNight);

            return list;
        }
    }
}
