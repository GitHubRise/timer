using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security;

// 时区目录：系统时区列表、时区 ID 校验/归一化、当前时区时间换算与「同步系统时区」入口。
// 调用方：设置页（下拉列表、预览、同步按钮）、ClockWindow/TaskbarClockWindow（取 TimeZoneInfo 与当前时间）。
// 边界：非法/不存在的时区 ID 一律回退 <see cref="TimeZoneCatalog.DefaultId"/>（中国标准时间，缺则 UTC），
// 取不到 System 时区时至少保证列表里有 UTC 一项，任何路径都不向调用方抛异常。
// 注意：只读 TimeZoneInfo.Local（Windows 注册表缓存快照），设置窗口不得直接读它，必须走 GetSystemId()。
namespace GameClock
{
    /// <summary>
    /// 时区下拉项：包装 <see cref="TimeZoneInfo"/>，显示文本带 UTC 偏移与 Windows 时区 ID 便于排查配置。
    /// </summary>
    internal sealed class TimeZoneChoice
    {
        /// <summary>由一个系统时区构造下拉项；Display 里保留 ID，用户反馈时能直接看出选的是哪个时区。</summary>
        public TimeZoneChoice(TimeZoneInfo zone)
        {
            Zone = zone;
            Id = zone.Id;
            Display = zone.DisplayName + "  [" + zone.Id + "]";
        }

        /// <summary>Windows 时区 ID（如 "China Standard Time"），落盘用的就是这个值。</summary>
        public string Id { get; private set; }

        /// <summary>列表显示文本：本地化时区名 + 方括号内的稳定 ID。</summary>
        public string Display { get; private set; }

        /// <summary>原始时区对象，供 OffsetLabel 与时间换算直接使用。</summary>
        public TimeZoneInfo Zone { get; private set; }

        /// <summary>ComboBox 默认取 ToString 显示，转给 Display 避免出现类型全名。</summary>
        public override string ToString()
        {
            return Display;
        }
    }

    /// <summary>
    /// 时区目录与默认时区策略：列表缓存、ID 校验、UTC 偏移标签、系统时区同步。
    /// </summary>
    internal static class TimeZoneCatalog
    {
        // 进程级缓存：GetSystemTimeZones 会读注册表且返回上百项，而设置页只在打开时用一次。
        private static List<TimeZoneChoice> _choices;

        // 默认时区 ID 惰性计算：FindDefaultId 要遍历系统时区，没必要在启动时付这个成本。
        private static string _defaultId;

        /// <summary>
        /// 默认时区（未配置或配置非法时的回退目标）：优先中国标准时间，退而求其次 UTC+8，最后 UTC。
        /// </summary>
        public static string DefaultId
        {
            get
            {
                // 双检外的简单判空即可：多线程同时算一次 FindDefaultId 也无副作用，只是多几次注册表读取。
                if (string.IsNullOrEmpty(_defaultId))
                {
                    _defaultId = FindDefaultId();
                }
                return _defaultId;
            }
        }

        /// <summary>
        /// 返回全部系统时区（按 UTC 偏移、再按显示名排序）；结果缓存到进程结束。
        /// 枚举失败时不抛异常，返回仅含 UTC 的列表，保证设置页下拉框永远可用。
        /// </summary>
        public static List<TimeZoneChoice> GetChoices()
        {
            // 命中缓存直接返回：列表内容随系统时区更新才变，进程内不必重复枚举。
            if (_choices != null)
            {
                return _choices;
            }

            List<TimeZoneChoice> list = new List<TimeZoneChoice>();
            try
            {
                // GetSystemTimeZones 读取 HKLM\...\TimeZoneInformation，正常环境不会失败。
                foreach (TimeZoneInfo zone in TimeZoneInfo.GetSystemTimeZones())
                {
                    list.Add(new TimeZoneChoice(zone));
                }
            }
            catch (Exception ex)
            {
                // 极端环境（注册表被裁剪、权限受限）枚举会抛异常；降级为 UTC 单项，避免设置页直接崩掉。
                AppLog.Warn("TimeZone", "枚举系统时区失败，时区列表回退为仅 UTC；异常：" + ex.Message, ex);
            }

            // 兜底：即使枚举成功但结果为空（部分精简系统），也让用户至少能选 UTC。
            if (list.Count == 0)
            {
                list.Add(new TimeZoneChoice(TimeZoneInfo.Utc));
            }

            // 排序：先按 UTC 偏移（东八区排在前面，符合中国用户习惯），同偏移再按本地化显示名。
            list.Sort(delegate(TimeZoneChoice left, TimeZoneChoice right)
            {
                int offsetCompare = left.Zone.BaseUtcOffset.CompareTo(right.Zone.BaseUtcOffset);
                if (offsetCompare != 0)
                {
                    return offsetCompare;
                }
                return string.Compare(
                    left.Zone.DisplayName,
                    right.Zone.DisplayName,
                    StringComparison.CurrentCultureIgnoreCase);
            });

            // 缓存排序后的列表（同一个 List 实例被多个下拉框共享，调用方不应修改它）。
            _choices = list;
            return _choices;
        }

        /// <summary>
        /// 校验并归一化时区 ID：存在则返回 Trim 后的 ID，否则回退 <see cref="DefaultId"/>。
        /// 供 AppSettings.Normalize 载入配置时使用，非法配置在这里被静默纠正为可用的时区。
        /// </summary>
        public static string Normalize(string id)
        {
            // 空 ID 不尝试 FindSystemTimeZoneById（它会抛 ArgumentNullException），直接走默认值。
            if (!string.IsNullOrWhiteSpace(id))
            {
                try
                {
                    // 先验证再返回：FindSystemTimeZoneById 抛异常就说明这个 ID 在本机不存在。
                    TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
                    return id.Trim();
                }
                catch (TimeZoneNotFoundException)
                {
                    // 配置从别的机器/旧系统拷来，本机没有该时区：下面统一回退并记一条 Warn。
                }
                catch (InvalidTimeZoneException)
                {
                    // 时区注册信息损坏（DST 规则非法）：同样视为不可用，回退默认值。
                }
                catch (SecurityException)
                {
                    // 无权读时区注册表：无法验证就按不可用处理，保证调用方拿到的一定能用。
                }
                // 用户可见的「时区被重置」多数来自这里，记一条带原始 ID 的 Warn 便于排查配置来源。
                AppLog.Warn("TimeZone", "时区 ID 不可用，已回退默认时区 " + DefaultId + "；原始 ID：" + id);
            }
            return DefaultId;
        }

        /// <summary>
        /// 把时区 ID 解析成 <see cref="TimeZoneInfo"/>；任何失败都返回 UTC，绝不返回 null。
        /// </summary>
        public static TimeZoneInfo GetZone(string id)
        {
            // 先走 Normalize：它已把非法 ID 收敛成 DefaultId，这里再 Find 基本不会失败。
            string normalized = Normalize(id);
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(normalized);
            }
            catch (TimeZoneNotFoundException)
            {
                // Normalize 通过但这里失败，只可能是时区在两次调用之间被改动，属罕见竞态；回退 UTC。
                return TimeZoneInfo.Utc;
            }
            catch (InvalidTimeZoneException)
            {
                // 注册信息损坏：仍给出可用对象，避免时钟窗口构造失败。
                return TimeZoneInfo.Utc;
            }
            catch (SecurityException)
            {
                // 无读权限：同上，静默回退；失败原因已由 Normalize/上层日志覆盖，这里不重复记录。
                return TimeZoneInfo.Utc;
            }
        }

        /// <summary>
        /// 取指定时区的当前时间；zone 为 null 时按 UTC 处理。
        /// 从 UTC 换算而非直接读本地时间，避免宿主系统时区与用户所选时区不一致时出现偏差。
        /// </summary>
        public static DateTime GetNow(TimeZoneInfo zone)
        {
            // 兜底为 UTC：调用方可能拿着未初始化的时区字段进来，不能让它空引用崩溃。
            TimeZoneInfo target = zone ?? TimeZoneInfo.Utc;
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, target);
            }
            catch (ArgumentException)
            {
                // 时区对象被释放或数据损坏时 ConvertTimeFromUtc 会抛 ArgumentException；
                // 回退系统本地时间（仍是「墙上时间」，只可能偏移不对），保证时钟不停走。
                return DateTime.Now;
            }
        }

        /// <summary>
        /// 生成 "UTC+08:00" 形式的偏移标签，用于设置页显示当前时区偏移。
        /// 用 BaseUtcOffset 而非 GetUtcOffset(now)：标签表示时区基准偏移，不随夏令时跳动。
        /// </summary>
        public static string GetOffsetLabel(string id)
        {
            TimeZoneInfo zone = GetZone(id);
            TimeSpan offset = zone.BaseUtcOffset;
            // Duration() 取绝对值，正负号单独拼，避免负偏移格式化成 "UTC-1:-30"。
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            TimeSpan absolute = offset.Duration();
            // 用不变文化格式化，防止系统区域把冒号换成别的分隔符。
            return string.Format(
                CultureInfo.InvariantCulture,
                "UTC{0}{1:00}:{2:00}",
                sign,
                absolute.Hours,
                absolute.Minutes);
        }

        /// <summary>
        /// 「同步系统时区」的唯一入口：返回本机当前时区在本目录中可用的 ID。
        /// 设置窗口必须调用本方法，不得直接读 <see cref="TimeZoneInfo.Local"/>。
        /// </summary>
        public static string GetSystemId()
        {
            try
            {
                return Normalize(TimeZoneInfo.Local.Id);
            }
            catch (Exception ex)
            {
                // TimeZoneInfo.Local 在时区注册表损坏时可能抛异常；同步按钮不能因此失效，退回默认时区。
                AppLog.Warn("TimeZone", "读取系统时区失败，已回退默认时区 " + DefaultId + "；异常：" + ex.Message, ex);
                return DefaultId;
            }
        }

        /// <summary>
        /// 计算默认时区：优先 Windows 的 "China Standard Time"，其次任意 UTC+8 且无夏令时的时区，
        /// 再次任意 UTC+8，最后 "UTC"。只在首次访问 DefaultId 时执行一次。
        /// </summary>
        private static string FindDefaultId()
        {
            // 首选 Windows 标准 ID：跨系统版本稳定，而 "China Standard Time" 在 .NET 的映射表里总是存在。
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                return "China Standard Time";
            }
            catch (TimeZoneNotFoundException)
            {
                // 非中文系统可能不含该 ID，下面按 UTC+8 扫描兜底。
            }
            catch (InvalidTimeZoneException)
            {
                // 该 ID 存在但数据损坏，同样走扫描兜底。
            }
            catch (SecurityException)
            {
                // 无权读取注册表时无法确认，交给下面的扫描（同样可能失败，再由最外层 catch 兜住）。
            }

            try
            {
                TimeZoneInfo best = null;
                foreach (TimeZoneInfo zone in TimeZoneInfo.GetSystemTimeZones())
                {
                    // 只考虑基准偏移正好 8 小时的时区（中国无夏令时，但新加坡/西澳等也要排除 DST 场景）。
                    if (zone.BaseUtcOffset == TimeSpan.FromHours(8.0))
                    {
                        // 无夏令时的时区最贴近中国用法，直接采用。
                        if (!zone.SupportsDaylightSavingTime)
                        {
                            return zone.Id;
                        }
                        // 否则先记下第一个 UTC+8 时区作为次优解，继续找无 DST 的。
                        if (best == null)
                        {
                            best = zone;
                        }
                    }
                }
                if (best != null)
                {
                    return best.Id;
                }
            }
            catch (Exception ex)
            {
                // 扫描失败（注册表异常）不影响可用性：最终回退 UTC，只是失去了自动识别中国时区的能力。
                AppLog.Warn("TimeZone", "扫描系统时区失败，默认时区回退 UTC；异常：" + ex.Message, ex);
            }

            return "UTC";
        }
    }
}
