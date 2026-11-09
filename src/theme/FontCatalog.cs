// 设置窗口字体下拉的候选字体来源。
// 首次调用时枚举系统字体，把常用字体排在前列，其余按显示名排序后追加；
// 结果缓存在静态字段中，运行期不再感知新装字体，重启应用后刷新。
// 字体名统一用 FontFamily.Source 去重（忽略大小写），避免同一字体重复出现；
// 枚举失败时退化为只含默认字体的列表，保证下拉框始终有可选项。

using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace GameClock
{

    /// <summary>
    /// 字体下拉中的一条候选：显示名与对应的 <see cref="FontFamily"/>。
    /// 生命周期：随 <see cref="FontCatalog.GetChoices"/> 的缓存一起常驻。
    /// </summary>
    internal sealed class FontChoice
    {

        /// <summary>保存字体显示名与对应的 FontFamily 实例。</summary>
        /// <param name="name">下拉框显示的名称，通常取 <see cref="FontFamily.Source"/>。</param>
        /// <param name="family">实际参与文本渲染的字体对象。</param>
        public FontChoice(string name, FontFamily family)
        {
            Name = name;
            Family = family;
        }

        /// <summary>下拉框显示的字体名。</summary>
        public string Name { get; private set; }

        /// <summary>实际用于创建文本的字体对象。</summary>
        public FontFamily Family { get; private set; }

        /// <summary>让 WPF 下拉框直接显示字体名而不是类型名。</summary>
        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// 系统字体枚举与缓存。调用方为设置窗口的字体下拉；
    /// 首次枚举后结果不变，避免每次打开设置都扫描字体库。
    /// </summary>
    internal static class FontCatalog
    {
        // ===== 默认值与缓存 =====

        /// <summary>找不到任何字体时使用的等宽字体。</summary>
        public const string DefaultFont = "Consolas";

        /// <summary>候选列表缓存；null 表示尚未枚举。</summary>
        private static List<FontChoice> _choices;

        // ===== 候选列表 =====

        /// <summary>
        /// 返回系统字体候选列表：常用字体优先，其余按名称排序，结果缓存。
        /// 枚举或排序失败时至少返回默认字体。
        /// </summary>
        public static List<FontChoice> GetChoices()
        {
            // 已枚举过就直接复用，避免重复扫描字体库。
            if (_choices != null)
            {
                return _choices;
            }

            List<FontChoice> result = new List<FontChoice>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 常用字体：优先保证等宽与中文可读性；未安装的字体会在下面被跳过。
            string[] preferred = new string[]
            {
                "Consolas",
                "Segoe UI",
                "Microsoft YaHei UI",
                "DengXian",
                "SimHei",
                "SimSun",
                "Bahnschrift",
                "Cascadia Mono",
                "Arial",
                "Impact"
            };

            List<FontFamily> installed = new List<FontFamily>();
            // 枚举系统字体；字体服务异常时保留空列表继续。
            try
            {
                // 逐个收集，稍后统一建索引与排序。
                foreach (FontFamily family in Fonts.SystemFontFamilies)
                {
                    installed.Add(family);
                }
            }
            // 字体服务不可用时放弃枚举，后续仍会走默认字体兜底。
            catch (Exception)
            {
            }

            // 先建名称索引，后续按名称查找常用字体是否已安装。
            Dictionary<string, FontFamily> lookup = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
            // 逐个登记字体名；同名以首次出现的实例为准。
            for (int i = 0; i < installed.Count; i++)
            {
                FontFamily family = installed[i];
                string name = family.Source;
                // 跳过空名称，并用首次出现的实例占位，保证同名只保留一个。
                if (!string.IsNullOrWhiteSpace(name) && !lookup.ContainsKey(name))
                {
                    lookup.Add(name, family);
                }
            }

            // 按预设顺序挑出已安装的常用字体，seen 防止后续重复追加。
            for (int i = 0; i < preferred.Length; i++)
            {
                string name = preferred[i];
                FontFamily family;
                // 未安装的常用字体直接跳过，已加入的也不重复追加。
                if (lookup.TryGetValue(name, out family) && seen.Add(family.Source))
                {
                    result.Add(new FontChoice(family.Source, family));
                }
            }

            // 其余字体按当前区域规则排序，保持下拉列表可预期。
            installed.Sort(delegate(FontFamily left, FontFamily right)
            {
                return string.Compare(left.Source, right.Source, StringComparison.CurrentCultureIgnoreCase);
            });

            // 追加所有未在常用列表出现过的字体。
            for (int i = 0; i < installed.Count; i++)
            {
                FontFamily family = installed[i];
                // 跳过空名称与已出现过的字体。
                if (!string.IsNullOrWhiteSpace(family.Source) && seen.Add(family.Source))
                {
                    result.Add(new FontChoice(family.Source, family));
                }
            }

            // 极端情况下字体服务完全不可用，仍给出默认字体。
            if (result.Count == 0)
            {
                result.Add(new FontChoice(DefaultFont, new FontFamily(DefaultFont)));
            }

            _choices = result;
            return _choices;
        }

        // ===== 查询 =====

        /// <summary>
        /// 按字体名创建 <see cref="FontFamily"/>；名称非法或为空时回退默认字体。
        /// 不校验字体是否已安装，WPF 会在渲染时使用自己的回退链。
        /// </summary>
        public static FontFamily GetFontFamily(string name)
        {
            // 空名称直接走默认字体，避免构造异常。
            if (!string.IsNullOrWhiteSpace(name))
            {
                // 交给 WPF 解析字体名；语法错误时回退默认字体。
                try
                {
                    return new FontFamily(name);
                }
                // 名称不符合 FontFamily 语法时吞掉异常，统一走默认字体。
                catch (ArgumentException)
                {
                }
            }
            return new FontFamily(DefaultFont);
        }

        /// <summary>
        /// 在候选列表中按名称查找字体（忽略大小写）；找不到或列表为空时返回 null。
        /// </summary>
        public static FontChoice FindChoice(List<FontChoice> choices, string name)
        {
            // 列表为空时没有可查找项。
            if (choices == null)
            {
                return null;
            }
            // 候选数量有限，线性扫描足够。
            for (int i = 0; i < choices.Count; i++)
            {
                // 名称比较忽略大小写，与去重规则一致。
                if (string.Equals(choices[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return choices[i];
                }
            }
            return null;
        }
    }
}
