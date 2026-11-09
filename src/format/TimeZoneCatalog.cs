// 时区候选、校验与系统时区同步的统一入口。
// 设置窗口的时区下拉、时钟渲染与“同步系统时区”都从这里取数据；
// 候选列表按 UTC 偏移升序、同偏移按显示名排序，并缓存到静态字段。
// 所有外部传入的时区 ID 必须先经 Normalize 校验，非法值回退默认时区
// （优先中国标准时间，其次任意 UTC+8 且无夏令时的时区，最后 UTC）。
// 约束：设置窗口不得直接读 TimeZoneInfo.Local，必须走 GetSystemId。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security;

namespace GameClock
{

    /// <summary>
    /// 时区下拉中的一条候选：时区对象、系统 ID 与“显示名 [ID]”文本。
    /// 生命周期：随 <see cref="TimeZoneCatalog.GetChoices"/> 的缓存一起常驻。
    /// </summary>
    internal sealed class TimeZoneChoice
    {

        /// <summary>从时区对象提取 ID 与显示文本，供下拉框直接使用。</summary>
        /// <param name="zone">系统时区对象，不能为 null。</param>
        public TimeZoneChoice(TimeZoneInfo zone)
        {
            Zone = zone;
            Id = zone.Id;
            Display = zone.DisplayName + "  [" + zone.Id + "]";
        }

        /// <summary>系统时区 ID，保存到设置文件的就是这个值。</summary>
        public string Id { get; private set; }

        /// <summary>下拉框显示文本，包含本地化显示名与 ID。</summary>
        public string Display { get; private set; }

        /// <summary>原始时区对象，用于转换时间与读取偏移。</summary>
        public TimeZoneInfo Zone { get; private set; }

        /// <summary>让 WPF 下拉框直接显示 Display 文本。</summary>
        public override string ToString()
        {
            return Display;
        }
    }

    /// <summary>
    /// 时区候选与校验工具。候选列表惰性枚举并缓存，默认时区也只计算一次；
    /// 所有方法都不抛异常，解析失败时返回 UTC 或默认时区，保证时钟继续运行。
    /// </summary>
    internal static class TimeZoneCatalog
    {
        // ===== 缓存 =====

        /// <summary>候选时区列表缓存；null 表示尚未枚举。</summary>
        private static List<TimeZoneChoice> _choices;

        /// <summary>默认时区 ID 缓存；null 或空表示尚未计算。</summary>
        private static string _defaultId;

        // ===== 默认时区 =====

        /// <summary>
        /// 默认时区 ID，首次读取时惰性计算并缓存。
        /// 用于配置缺失、时区 ID 非法或系统时区不可读时的统一兜底。
        /// </summary>
        public static string DefaultId
        {
            get
            {
                // 首次访问才枚举时区库，避免应用启动时做无谓的系统查询。
                if (string.IsNullOrEmpty(_defaultId))
                {
                    _defaultId = FindDefaultId();
                }
                return _defaultId;
            }
        }

        // ===== 候选列表 =====

        /// <summary>
        /// 返回按时区偏移与显示名排序的候选列表，结果缓存。
        /// 系统时区枚举失败时至少返回 UTC 一项。
        /// </summary>
        public static List<TimeZoneChoice> GetChoices()
        {
            // 已枚举过就直接复用，保证下拉列表顺序稳定。
            if (_choices != null)
            {
                return _choices;
            }

            List<TimeZoneChoice> list = new List<TimeZoneChoice>();
            // 枚举系统时区；失败时保持空列表，由下面的 UTC 兜底。
            try
            {
                // 每个系统时区转成一条下拉候选。
                foreach (TimeZoneInfo zone in TimeZoneInfo.GetSystemTimeZones())
                {
                    list.Add(new TimeZoneChoice(zone));
                }
            }
            // 注册表不可读或权限受限时放弃枚举，后面用 UTC 兜底。
            catch (Exception)
            {
            }

            // 枚举结果为空时仍要给出可选项，否则下拉框会变成空列表。
            if (list.Count == 0)
            {
                list.Add(new TimeZoneChoice(TimeZoneInfo.Utc));
            }

            // 先按 UTC 偏移升序；同一偏移再按本地化显示名排序，方便查找。
            list.Sort(delegate(TimeZoneChoice left, TimeZoneChoice right)
            {
                int offsetCompare = left.Zone.BaseUtcOffset.CompareTo(right.Zone.BaseUtcOffset);
                // 偏移不同先按偏移排；偏移相同才比较显示名。
                if (offsetCompare != 0)
                {
                    return offsetCompare;
                }
                return string.Compare(
                    left.Zone.DisplayName,
                    right.Zone.DisplayName,
                    StringComparison.CurrentCultureIgnoreCase);
            });

            _choices = list;
            return _choices;
        }

        // ===== 校验与查询 =====

        /// <summary>
        /// 校验时区 ID 并返回可用的规范值；空值、未知 ID 或时区数据损坏时回退
        /// <see cref="DefaultId"/>。这是所有外部时区 ID 进入系统的第一道关卡。
        /// </summary>
        public static string Normalize(string id)
        {
            // 只对非空输入做系统查找，省去无意义的异常开销。
            if (!string.IsNullOrWhiteSpace(id))
            {
                // 先确认时区库中存在该 ID；查找失败再走默认时区。
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
                    return id.Trim();
                }
                // ID 不在本机时区库中：交给 DefaultId 兜底。
                catch (TimeZoneNotFoundException)
                {
                }
                // 时区数据损坏：同样视为不可用。
                catch (InvalidTimeZoneException)
                {
                }
                // 无权限读取时区库：不向上抛，避免设置窗口打不开。
                catch (SecurityException)
                {
                }
            }
            return DefaultId;
        }

        /// <summary>
        /// 按 ID 获取时区对象；任何解析失败都回退 UTC，保证调用方拿到可用实例。
        /// </summary>
        public static TimeZoneInfo GetZone(string id)
        {
            string normalized = Normalize(id);
            // 按规范化后的 ID 取对象，异常时统一回退 UTC。
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(normalized);
            }
            // Normalize 已校验过，这里仍兜底，防止枚举与查找之间时区库变化。
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
            // 时区数据损坏时退回 UTC，避免时钟因异常停止刷新。
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
            // 无权限读取时区库时退回 UTC，保证界面仍可显示时间。
            catch (SecurityException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        /// <summary>
        /// 取目标时区的当前时间；zone 为 null 时按 UTC 处理。
        /// 时区规则异常导致转换失败时退回本机时间，避免时钟停摆。
        /// </summary>
        public static DateTime GetNow(TimeZoneInfo zone)
        {
            TimeZoneInfo target = zone ?? TimeZoneInfo.Utc;
            // 从 UTC 转换到目标时区；失败时退回本机时间保持界面可用。
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, target);
            }
            // 时区规则损坏或时间超出范围：退回本机时间保持界面可用。
            catch (ArgumentException)
            {
                return DateTime.Now;
            }
        }

        /// <summary>
        /// 生成 <c>UTC+08:00</c> 形式的偏移标签。
        /// 使用 BaseUtcOffset（不含夏令时），与下拉列表的排序依据保持一致。
        /// </summary>
        public static string GetOffsetLabel(string id)
        {
            TimeZoneInfo zone = GetZone(id);
            TimeSpan offset = zone.BaseUtcOffset;
            // 负偏移要单独取符号，Duration 只负责把绝对值格式化。
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            TimeSpan absolute = offset.Duration();
            return string.Format(
                CultureInfo.InvariantCulture,
                "UTC{0}{1:00}:{2:00}",
                sign,
                absolute.Hours,
                absolute.Minutes);
        }

        // ===== 系统时区同步 =====

        /// <summary>
        /// 读取本机时区并返回校验后的 ID；“同步系统时区”只调用这里。
        /// TimeZoneInfo.Local 在系统时区损坏时也可能抛异常，因此统一兜底。
        /// </summary>
        public static string GetSystemId()
        {
            // 读取本机时区 ID；TimeZoneInfo.Local 自身也可能抛异常。
            try
            {
                return Normalize(TimeZoneInfo.Local.Id);
            }
            // 读取本地时区失败时退回默认时区，保证设置窗口仍能打开。
            catch (Exception)
            {
                return DefaultId;
            }
        }

        /// <summary>
        /// 计算默认时区：优先 Windows 中国标准时间，其次 UTC+8 且无夏令时的时区，
        /// 最后退回 UTC。结果由 <see cref="DefaultId"/> 惰性缓存。
        /// </summary>
        private static string FindDefaultId()
        {
            // 优先使用中文 Windows 的固定 ID，找不到再按偏移兜底。
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                return "China Standard Time";
            }
            // 非中文 Windows 可能没有该 ID，继续按偏移查找。
            catch (TimeZoneNotFoundException)
            {
            }
            // 该 ID 的数据损坏时同样继续按偏移查找。
            catch (InvalidTimeZoneException)
            {
            }
            // 无权限读取时区库时继续按偏移查找，最后仍有 UTC 兜底。
            catch (SecurityException)
            {
            }

            // 退而求其次：找 UTC+8 且无夏令时的时区。
            try
            {
                TimeZoneInfo best = null;
                // 遍历系统时区，挑出 UTC+8 的候选。
                foreach (TimeZoneInfo zone in TimeZoneInfo.GetSystemTimeZones())
                {
                    // 只考虑 UTC+8；优先无夏令时的时区，避免默认时区随季节跳变。
                    if (zone.BaseUtcOffset == TimeSpan.FromHours(8.0))
                    {
                        // 无夏令时的时区一年偏移稳定，优先直接返回。
                        if (!zone.SupportsDaylightSavingTime)
                        {
                            return zone.Id;
                        }
                        // 有夏令时的 UTC+8 先记为候选，继续找更稳定的。
                        if (best == null)
                        {
                            best = zone;
                        }
                    }
                }
                // 没找到无夏令时版本时，才使用候选。
                if (best != null)
                {
                    return best.Id;
                }
            }
            // 枚举时区失败时不再尝试，直接落到 UTC。
            catch (Exception)
            {
            }

            return "UTC";
        }
    }
}
