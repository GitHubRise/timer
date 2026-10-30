using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace GameClock
{
    internal sealed class FontChoice
    {
        public FontChoice(string name, FontFamily family)
        {
            Name = name;
            Family = family;
        }

        public string Name { get; private set; }
        public FontFamily Family { get; private set; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal static class FontCatalog
    {
        public const string DefaultFont = "Consolas";
        private static List<FontChoice> _choices;

        public static List<FontChoice> GetChoices()
        {
            if (_choices != null)
            {
                return _choices;
            }

            List<FontChoice> result = new List<FontChoice>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            try
            {
                foreach (FontFamily family in Fonts.SystemFontFamilies)
                {
                    installed.Add(family);
                }
            }
            catch (Exception)
            {
            }

            Dictionary<string, FontFamily> lookup = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < installed.Count; i++)
            {
                FontFamily family = installed[i];
                string name = family.Source;
                if (!string.IsNullOrWhiteSpace(name) && !lookup.ContainsKey(name))
                {
                    lookup.Add(name, family);
                }
            }

            for (int i = 0; i < preferred.Length; i++)
            {
                string name = preferred[i];
                FontFamily family;
                if (lookup.TryGetValue(name, out family) && seen.Add(family.Source))
                {
                    result.Add(new FontChoice(family.Source, family));
                }
            }

            installed.Sort(delegate(FontFamily left, FontFamily right)
            {
                return string.Compare(left.Source, right.Source, StringComparison.CurrentCultureIgnoreCase);
            });

            for (int i = 0; i < installed.Count; i++)
            {
                FontFamily family = installed[i];
                if (!string.IsNullOrWhiteSpace(family.Source) && seen.Add(family.Source))
                {
                    result.Add(new FontChoice(family.Source, family));
                }
            }

            if (result.Count == 0)
            {
                result.Add(new FontChoice(DefaultFont, new FontFamily(DefaultFont)));
            }

            _choices = result;
            return _choices;
        }

        public static FontFamily GetFontFamily(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    return new FontFamily(name);
                }
                catch (ArgumentException)
                {
                }
            }
            return new FontFamily(DefaultFont);
        }

        public static FontChoice FindChoice(List<FontChoice> choices, string name)
        {
            if (choices == null)
            {
                return null;
            }
            for (int i = 0; i < choices.Count; i++)
            {
                if (string.Equals(choices[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return choices[i];
                }
            }
            return null;
        }
    }
}
