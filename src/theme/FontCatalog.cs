using System;
using System.Collections.Generic;
using System.Windows.Media;

// 可用字体目录：枚举系统已安装字体，生成设置页下拉框用的 FontChoice 列表，并提供名称 → FontFamily 的解析。
// 边界：不读写 AppSettings；调用方拿到 FontFamily 后自行应用，本类不持有窗口或控件引用。
// 系统字体枚举可能失败（字体缓存损坏、注册表被裁剪），策略是降级到默认等宽字体而不中断启动。
// 结果按进程缓存一次：设置窗口每次重建都会取用，反复枚举 SystemFontFamilies 会造成明显停顿。
namespace GameClock
{
    /// <summary>
    /// 字体下拉框的一个选项：显示名与可用 FontFamily 成对保存，避免重复解析字体名。
    /// </summary>
    internal sealed class FontChoice
    {
        /// <summary>用显示名与已解析的字体族构造选项。</summary>
        public FontChoice(string name, FontFamily family)
        {
            Name = name;
            Family = family;
        }

        /// <summary>字体显示名（FontFamily.Source），也用于与配置中的字体名匹配。</summary>
        public string Name { get; private set; }

        /// <summary>可直接赋给控件的字体族。</summary>
        public FontFamily Family { get; private set; }

        /// <summary>ComboBox 显示用：直接返回名称，无需额外设置 DisplayMemberPath。</summary>
        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// 系统字体目录：提供字体列表、名称解析与名称查找；全部为静态无状态方法（只有一份列表缓存）。
    /// </summary>
    internal static class FontCatalog
    {
        /// <summary>默认字体（等宽），任何解析失败都回退到它，保证时钟数字不会因缺字体而错位。</summary>
        public const string DefaultFont = "Consolas";

        // 进程级字体列表缓存：null 表示尚未枚举，首次 GetChoices 时填充，之后只读。
        private static List<FontChoice> _choices;

        /// <summary>
        /// 返回可用字体列表：优先字体按预设顺序排在前面，其余按名称排序追加，最后兜底默认字体。
        /// 首次调用枚举系统字体并缓存；后续调用直接返回同一 List，调用方只读、不得增删。
        /// </summary>
        public static List<FontChoice> GetChoices()
        {
            // 已枚举过就直接复用：设置窗口每次重建都会调用，重复枚举会卡住 UI 线程。
            if (_choices != null)
            {
                return _choices;
            }

            List<FontChoice> result = new List<FontChoice>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 优先展示顺序：这些字体在 Win10/11 上基本自带，前几项即可覆盖中英文与等宽场景；
            // 不在列表里的字体仍会排在后面，这里只影响下拉框的默认观感。
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
            // 枚举放在 try 里：底层字体缓存损坏时 WPF 可能抛异常，不能让它阻止设置窗口打开。
            try
            {
                // 逐个收集而非直接使用枚举器：后续需要 Sort 与多次遍历。
                foreach (FontFamily family in Fonts.SystemFontFamilies)
                {
                    installed.Add(family);
                }
            }
            // 枚举失败时 installed 为空，后面会走「无可用字体 → 默认字体」的兜底分支；
            // 所以这里只记录现场，不向上抛：缺字体不应阻止设置窗口打开。
            catch (Exception ex)
            {
                AppLog.Error("Font", "枚举系统字体失败，字体列表将回退到默认字体", ex);
            }

            // 字体名去重忽略大小写：同一字体族的 Source 在不同系统上大小写可能不同。
            // 建名称索引，供下面的优先字体顺序直接命中；忽略大小写见上一行说明。
            Dictionary<string, FontFamily> lookup = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
            // 遍历已安装字体建立索引；空名（部分字体 Source 为空）直接跳过。
            for (int i = 0; i < installed.Count; i++)
            {
                FontFamily family = installed[i];
                string name = family.Source;
                // 同名只保留第一个：不同字体文件可能暴露相同的 Source。
                if (!string.IsNullOrWhiteSpace(name) && !lookup.ContainsKey(name))
                {
                    lookup.Add(name, family);
                }
            }

            // 按 preferred 的顺序挑出本机存在的字体；seen 兼作已加入结果的去重集合。
            for (int i = 0; i < preferred.Length; i++)
            {
                string name = preferred[i];
                FontFamily family;
                // TryGetValue 失败说明本机没装该字体，跳过即可，不算错误。
                if (lookup.TryGetValue(name, out family) && seen.Add(family.Source))
                {
                    result.Add(new FontChoice(family.Source, family));
                }
            }

            // 其余字体按当前区域性名称排序，保证每次打开的列表顺序稳定（SystemFontFamilies 顺序不保证）。
            installed.Sort(delegate(FontFamily left, FontFamily right)
            {
                return string.Compare(left.Source, right.Source, StringComparison.CurrentCultureIgnoreCase);
            });

            // 追加其余字体；seen 已在优先字体阶段填充，这里只补未出现过的。
            for (int i = 0; i < installed.Count; i++)
            {
                FontFamily family = installed[i];
                // 过滤空名，并用 seen.Add 的返回值去重（返回 false 表示已加过）。
                if (!string.IsNullOrWhiteSpace(family.Source) && seen.Add(family.Source))
                {
                    result.Add(new FontChoice(family.Source, family));
                }
            }

            // 列表为空说明枚举失败或全被过滤：必须补默认项，否则下拉框无选项也无法恢复。
            if (result.Count == 0)
            {
                // 仅告警不抛异常：字体缺失不影响计时功能，用户仍可在设置页重新选择。
                AppLog.Warn("Font", "系统未返回任何可用字体，字体列表仅含默认字体 " + DefaultFont);
                result.Add(new FontChoice(DefaultFont, new FontFamily(DefaultFont)));
            }

            _choices = result;
            return _choices;
        }

        /// <summary>
        /// 把配置里的字体名解析成 FontFamily；名称为空或不可用时回退到默认等宽字体。
        /// </summary>
        /// <param name="name">配置中保存的字体名（FontFamily.Source）；可为空。</param>
        /// <returns>总是返回非 null 的字体族，调用方无需再判空。</returns>
        public static FontFamily GetFontFamily(string name)
        {
            // 空名直接走默认字体：避免 FontFamily("") 抛异常，也省一次 try。
            if (!string.IsNullOrWhiteSpace(name))
            {
                // 先构造再返回：FontFamily 构造只做语法校验，真正的字体替换由 WPF 回落机制处理。
                try
                {
                    return new FontFamily(name);
                }
                // 名称非法（含路径分隔符、保留字符等）时 WPF 抛异常；配置可能被手改，
                // 这里只告警并回退，不修改配置，用户下次选字体即会覆盖。
                catch (ArgumentException)
                {
                    AppLog.Warn("Font", "字体名不可用，回退默认字体 " + DefaultFont + "：" + name);
                }
            }
            return new FontFamily(DefaultFont);
        }

        /// <summary>
        /// 在列表中按名称查找选项（忽略大小写，因为配置里的名称大小写不保证稳定）；
        /// 找不到返回 null，由调用方决定回退到哪个候选字体。
        /// </summary>
        public static FontChoice FindChoice(List<FontChoice> choices, string name)
        {
            // 防御性判空：调用方可能还没走到 GetChoices 就用 FindChoice 找配置中的字体。
            if (choices == null)
            {
                return null;
            }
            // 线性查找足够：字体数量在百级，调用只发生在设置窗口打开时。
            for (int i = 0; i < choices.Count; i++)
            {
                // 忽略大小写比较，配置里保存的名称大小写可能与系统 Source 不同。
                if (string.Equals(choices[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return choices[i];
                }
            }
            return null;
        }
    }
}
