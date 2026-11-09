// 提醒（Reminder）领域模型与配套选项工厂：一条“每天几点 + 一句提醒 + 适用状态”的轻量记录。
// 调用方：SettingsWindow 的提醒页负责编辑与校验，ClockWindow 每分钟扫描并在到点时弹气泡，
// SettingsStore 通过 DataContract 把它写进 settings.json（落盘字段名由 [DataMember] 固定）。
// 约束：本文件只放数据与纯计算，不持有窗口引用、不播放声音、不读系统时钟；是否到点由调用方判断。
// 兼容：旧配置缺字段时由 [OnDeserializing] 预置默认值；状态字符串一律先经 ReminderModes.Normalize 收敛。

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GameClock
{

    /// <summary>
    /// 提醒适用状态的字符串常量集合：用可读字符串落盘，便于手工改配置与向前兼容。
    /// 外部输入一律经 <see cref="Normalize"/> 归一，非法值回落 <see cref="Both"/>；
    /// 不用枚举是为了让 settings.json 里直接出现 <c>"work"</c>/<c>"leisure"</c>。
    /// </summary>
    internal static class ReminderModes
    {
        // ===== 状态取值 =====

        /// <summary>工作与娱乐都提醒；也是缺字段、空值、未知值的兜底。</summary>
        public const string Both = "both";

        /// <summary>仅工作时提醒；值与设置里的当前状态字符串一致，可直接比较。</summary>
        public const string Work = "work";

        /// <summary>仅娱乐时提醒；与 <see cref="Work"/> 互斥，判定见 <see cref="AppliesToState"/>。</summary>
        public const string Leisure = "leisure";

        // ===== 归一与判定 =====

        /// <summary>
        /// 把任意输入收敛成三个合法值之一。比较忽略大小写，因为配置可能被手工改成 "Work"；
        /// 空值与未知值都当作 <see cref="Both"/>，宁可多提醒也不静默丢掉一条提醒。
        /// </summary>
        public static string Normalize(string mode)
        {
            // 用序号比较忽略大小写，不随系统区域设置变化，避免 Turkish-I 之类的大小写陷阱。
            if (string.Equals(mode, Work, StringComparison.OrdinalIgnoreCase))
            {
                return Work;
            }

            // 娱乐状态同理：命中就返回规范化后的常量，后续比较不必再管大小写。
            if (string.Equals(mode, Leisure, StringComparison.OrdinalIgnoreCase))
            {
                return Leisure;
            }

            return Both;
        }

        /// <summary>
        /// 取界面上显示的中文名。先经 <see cref="Normalize"/> 收敛，
        /// 因此未知状态显示为“全部状态”，不会把英文原值露给用户。
        /// </summary>
        public static string DisplayName(string mode)
        {
            // 收敛一次后只与常量比较，避免三处重复写大小写不敏感的判断。
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

        /// <summary>
        /// 判断某条提醒在当前状态下是否该响：两侧都先归一，
        /// 提醒为 <see cref="Both"/> 时无条件通过，否则要求状态字符串完全相同。
        /// </summary>
        public static bool AppliesToState(string reminderMode, string currentState)
        {
            // 两侧归一后才比较，避免配置里大小写不一致导致同一条提醒时而生效时而失效。
            string mode = Normalize(reminderMode);
            string state = Normalize(currentState);

            // “全部状态”的提醒不看当前状态，工作与娱乐都响。
            if (mode == Both)
            {
                return true;
            }

            return mode == state;
        }

        /// <summary>
        /// 生成设置界面下拉框的选项列表；顺序即显示顺序，“全部状态”放第一项作为默认选择。
        /// </summary>
        public static List<ReminderModeOption> CreateOptions()
        {
            List<ReminderModeOption> options = new List<ReminderModeOption>();
            options.Add(new ReminderModeOption(Both, "全部状态"));
            // 只工作/只娱乐两项的文案与 <see cref="DisplayName"/> 保持区分：这里带“仅”字强调排他。
            options.Add(new ReminderModeOption(Work, "仅工作"));
            options.Add(new ReminderModeOption(Leisure, "仅娱乐"));
            return options;
        }
    }

    /// <summary>
    /// 提醒状态下拉框的一项：<see cref="Value"/> 是落盘用的常量，<see cref="Label"/> 是界面文案。
    /// 不可变对象，创建后只读；<see cref="ToString"/> 让 WPF 下拉框直接显示中文。
    /// </summary>
    internal sealed class ReminderModeOption
    {

        /// <summary>保存取值与显示文案；两者都由调用方给定，构造函数不做校验。</summary>
        public ReminderModeOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        /// <summary>落盘与比较用的状态值，必定是 <see cref="ReminderModes"/> 的常量之一。</summary>
        public string Value { get; private set; }

        /// <summary>界面显示文案，如“仅工作”。</summary>
        public string Label { get; private set; }

        /// <summary>让 WPF 控件与调试输出直接显示 <see cref="Label"/>，无需再写 DisplayMemberPath。</summary>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 一条每日提醒：时间精确到分钟 + 一句文本 + 适用状态 + 当天是否已响过。
    /// 由设置窗口增删改，<see cref="ClockWindow"/> 每分钟扫描并回写 <see cref="LastTriggeredDate"/> 去重。
    /// 生命周期跟随 settings.json：可序列化、可克隆；本类不负责弹窗、闪烁与声音。
    /// </summary>
    [DataContract]
    internal sealed class Reminder
    {

        /// <summary>新建一条可用提醒：12:00、空文本、全部状态、启用；文本为空时由 <see cref="Normalize"/> 判为无效。</summary>
        public Reminder()
        {
            Time = "12:00";
            Message = "";
            Mode = ReminderModes.Both;
            Enabled = true;
            LastTriggeredDate = "";
        }

        /// <summary>
        /// DataContract 反序列化不会调用构造函数，这里补一次同样的默认值，
        /// 保证旧配置缺少新增字段时字段不会是 null（尤其是 <see cref="Mode"/> 与 <see cref="LastTriggeredDate"/>）。
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

        // ===== 模型字段 =====

        /// <summary>提醒时间，格式 <c>HH:mm</c>；由 <see cref="Normalize"/> 用 <c>TimeFormatter.TryNormalizeTime</c> 统一。</summary>
        [DataMember(Name = "time")]
        public string Time { get; set; }

        /// <summary>提醒正文，最多 120 字；空白视为无效，超长在归一化时截断而不是丢弃整条。</summary>
        [DataMember(Name = "message")]
        public string Message { get; set; }

        /// <summary>适用状态，取 <see cref="ReminderModes"/> 的常量；为空或未知时归一成“全部状态”。</summary>
        [DataMember(Name = "mode")]
        public string Mode { get; set; }

        /// <summary>是否启用；停用的提醒保留在列表里但不会被扫描触发。</summary>
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>最近一次触发日期（<c>yyyy-MM-dd</c>）；同一天只响一次，是触发去重的唯一依据。</summary>
        [DataMember(Name = "lastTriggered")]
        public string LastTriggeredDate { get; set; }

        // ===== 复制与清洗 =====

        /// <summary>
        /// 逐字段复制出一条独立实例。设置窗口编辑的是克隆副本，避免编辑过程中改到正在生效的列表。
        /// </summary>
        public Reminder Clone()
        {
            Reminder copy = new Reminder();
            // 字段都是值类型或不可变字符串，直接赋值即可，不需要深拷贝。
            copy.Time = Time;
            copy.Message = Message;
            copy.Mode = Mode;
            copy.Enabled = Enabled;
            copy.LastTriggeredDate = LastTriggeredDate;
            return copy;
        }

        /// <summary>
        /// 就地清洗并校验：时间必须能解析、正文不能为空，其余字段收敛到合法范围。
        /// 返回 false 表示这条提醒不可用（时间非法或正文空白），调用方应把它从列表里剔除；
        /// 超长正文与超长状态不会导致失败，而是截断/回落，尽量保住用户的输入。
        /// </summary>
        public bool Normalize()
        {
            string normalizedTime;
            // 时间统一成 HH:mm；解析失败直接判无效，避免留下一条永远不会触发的提醒。
            if (!TimeFormatter.TryNormalizeTime(Time, out normalizedTime))
            {
                return false;
            }

            Time = normalizedTime;

            // 正文去首尾空白后为空即视为无效：没有内容的提醒弹出来只会打扰用户。
            Message = (Message ?? string.Empty).Trim();
            if (Message.Length == 0)
            {
                return false;
            }

            // 超长正文截断而不是拒绝，保留前 120 字；界面输入框本身也有同样的长度限制。
            if (Message.Length > 120)
            {
                Message = Message.Substring(0, 120);
            }

            // 状态允许用户手改配置，这里统一收敛；未知值会变成“全部状态”。
            Mode = ReminderModes.Normalize(Mode);

            // 老配置可能没有这个字段，反序列化后为 null；统一成空串，后续字符串比较不必判空。
            if (string.IsNullOrEmpty(LastTriggeredDate))
            {
                LastTriggeredDate = "";
            }

            return true;
        }

        /// <summary>调试与列表兜底显示用：<c>HH:mm  正文</c>，界面实际排版由设置窗口负责。</summary>
        public override string ToString()
        {
            return Time + "  " + Message;
        }
    }

    /// <summary>
    /// 提醒触发事件的参数：把触发的那条 <see cref="Reminder"/> 原样传给订阅方，
    /// 便于控制器做托盘气泡等附加处理；事件本身由 <see cref="ClockWindow"/> 在入队后抛出。
    /// </summary>
    internal sealed class ReminderEventArgs : EventArgs
    {

        /// <summary>只保存触发对象引用；不复制，订阅方不应修改它。</summary>
        public ReminderEventArgs(Reminder reminder)
        {
            Reminder = reminder;
        }

        /// <summary>刚刚触发的那条提醒。</summary>
        public Reminder Reminder { get; private set; }
    }

    /// <summary>
    /// 提醒预览事件的参数：只带时间与文案两个字符串，
    /// 用于设置界面“预览气泡效果”这类不需要完整模型的场景。
    /// </summary>
    internal sealed class ReminderPreviewEventArgs : EventArgs
    {

        /// <summary>保存预览用的时间文本与正文，调用方负责保证时间已格式化。</summary>
        public ReminderPreviewEventArgs(string time, string message)
        {
            Time = time;
            Message = message;
        }

        /// <summary>预览显示的时间文本，通常是 <c>HH:mm</c>。</summary>
        public string Time { get; private set; }

        /// <summary>预览显示的提醒正文。</summary>
        public string Message { get; private set; }
    }

    /// <summary>
    /// 提醒气泡停留时长下拉框的一项：<see cref="Seconds"/> 是秒数，<see cref="Label"/> 是界面文案。
    /// 不可变选项对象，取值必须来自 <see cref="ReminderDisplayOptions"/> 的白名单。
    /// </summary>
    internal sealed class ReminderDisplayOption
    {

        /// <summary>保存秒数与显示文案；不做范围校验，合法性由白名单保证。</summary>
        public ReminderDisplayOption(int seconds, string label)
        {
            Seconds = seconds;
            Label = label;
        }

        /// <summary>气泡停留秒数，落盘进 <c>ReminderDisplaySeconds</c>。</summary>
        public int Seconds { get; private set; }

        /// <summary>界面显示文案，如“12 秒（默认）”。</summary>
        public string Label { get; private set; }

        /// <summary>让下拉框直接显示 <see cref="Label"/>。</summary>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 提醒气泡停留时长的可选档位与归一化：只允许 5/8/10/12/15 秒，
    /// 手改配置写出其它值时统一回落 12 秒，避免气泡一闪而过或长时间挡屏。
    /// </summary>
    internal static class ReminderDisplayOptions
    {
        // ===== 可选档位 =====

        /// <summary>允许的秒数白名单；顺序与下拉框一致，12 同时作为默认值与非法值兜底。</summary>
        private static readonly int[] AllowedSeconds = new int[] { 5, 8, 10, 12, 15 };

        // ===== 选项与归一 =====

        /// <summary>生成设置界面下拉框的选项列表；文案里的“默认”标记与 <see cref="Normalize"/> 的兜底值保持一致。</summary>
        public static List<ReminderDisplayOption> Create()
        {
            List<ReminderDisplayOption> list = new List<ReminderDisplayOption>();
            list.Add(new ReminderDisplayOption(5, "5 秒"));
            list.Add(new ReminderDisplayOption(8, "8 秒"));
            list.Add(new ReminderDisplayOption(10, "10 秒"));
            // 12 秒是默认值：既标在文案上，也是 <see cref="Normalize"/> 越界时的落点。
            list.Add(new ReminderDisplayOption(12, "12 秒（默认）"));
            list.Add(new ReminderDisplayOption(15, "15 秒"));
            return list;
        }

        /// <summary>
        /// 把任意秒数收敛到白名单：命中就原样返回，否则回落 12。
        /// 线性扫描足够，列表只有 5 项，且避免额外分配集合。
        /// </summary>
        public static int Normalize(int seconds)
        {
            // 逐项比对白名单，命中即返回原值，保证界面选项与生效值一一对应。
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
    /// 设置界面的“快捷提醒”预设：一行按钮 = 一组时间/文案/状态。
    /// 只服务界面上一键添加，不参与默认配置生成；按钮文本已含时间，便于用户快速扫读。
    /// </summary>
    internal sealed class ReminderPreset
    {

        /// <summary>保存时间、正文、按钮文案与适用状态；四个值都由 <see cref="ReminderPresets"/> 写死。</summary>
        public ReminderPreset(string time, string message, string buttonText, string mode)
        {
            Time = time;
            Message = message;
            ButtonText = buttonText;
            Mode = mode;
        }

        /// <summary>预设时间，格式 <c>HH:mm</c>。</summary>
        public string Time { get; private set; }

        /// <summary>预设提醒正文。</summary>
        public string Message { get; private set; }

        /// <summary>快捷按钮上的文字，通常已包含时间。</summary>
        public string ButtonText { get; private set; }

        /// <summary>预设适用状态，取 <see cref="ReminderModes"/> 的常量。</summary>
        public string Mode { get; private set; }

        /// <summary>鼠标悬停提示用的一行摘要：<c>HH:mm  正文</c>。</summary>
        public string Display
        {
            get { return Time + "  " + Message; }
        }
    }

    /// <summary>
    /// 快捷提醒预设清单：集中写死常见作息（上班、午饭、休息、下班、喝水、睡觉），
    /// 由设置窗口渲染成一排按钮，点击后把该预设填进编辑区，不直接写入配置。
    /// </summary>
    internal static class ReminderPresets
    {

        /// <summary>
        /// 生成预设列表。顺序即按钮顺序，按一天的时间推进排列；
        /// 文案与 <see cref="ReminderDefaults"/> 有意重合但不共享，避免改默认配置时误动快捷按钮。
        /// </summary>
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

    /// <summary>
    /// 内置默认提醒：首次启动或配置里没有提醒列表时使用，共 5 条覆盖工作与娱乐两个时段。
    /// 返回新列表与新对象，调用方可以直接改，不会污染其他实例。
    /// </summary>
    internal static class ReminderDefaults
    {

        /// <summary>
        /// 构造默认提醒集合。这里逐个赋值而不是复用 <see cref="ReminderPresets"/>，
        /// 因为默认配置要更精简（5 条），而快捷按钮需要覆盖更多场景。
        /// </summary>
        public static List<Reminder> Create()
        {
            List<Reminder> list = new List<Reminder>();

            // 上午上班：进入工作状态时提醒今天的重点。
            Reminder workMorning = new Reminder();
            workMorning.Time = "09:30";
            workMorning.Message = "工作时间开始，先列出今天的重点。";
            workMorning.Mode = ReminderModes.Work;
            list.Add(workMorning);

            // 午饭：不分工作/娱乐，两个状态都提醒。
            Reminder noon = new Reminder();
            noon.Time = "12:00";
            noon.Message = "该吃点美食补充能量了。";
            noon.Mode = ReminderModes.Both;
            list.Add(noon);

            // 傍晚下班：仍是工作状态，提醒收尾并准备切换。
            Reminder workEvening = new Reminder();
            workEvening.Time = "18:30";
            workEvening.Message = "工作告一段落，切换娱乐状态吧。";
            workEvening.Mode = ReminderModes.Work;
            list.Add(workEvening);

            // 深夜两连提醒只在娱乐状态出现，避免工作时弹“早点休息”。
            Reminder night = new Reminder();
            night.Time = "23:00";
            night.Message = "已经很晚了，早点休息。";
            night.Mode = ReminderModes.Leisure;
            list.Add(night);

            // 凌晨 01:00 属于跨天时段，按每日时间字符串比较即可，无需特殊处理日期。
            Reminder lateNight = new Reminder();
            lateNight.Time = "01:00";
            lateNight.Message = "夜深了，该下游戏休息了。";
            lateNight.Mode = ReminderModes.Leisure;
            list.Add(lateNight);

            return list;
        }
    }
}
