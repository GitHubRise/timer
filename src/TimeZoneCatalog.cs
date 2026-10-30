using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security;

namespace GameClock
{
    internal sealed class TimeZoneChoice
    {
        public TimeZoneChoice(TimeZoneInfo zone)
        {
            Zone = zone;
            Id = zone.Id;
            Display = zone.DisplayName + "  [" + zone.Id + "]";
        }

        public string Id { get; private set; }
        public string Display { get; private set; }
        public TimeZoneInfo Zone { get; private set; }

        public override string ToString()
        {
            return Display;
        }
    }

    internal static class TimeZoneCatalog
    {
        private static List<TimeZoneChoice> _choices;
        private static string _defaultId;

        public static string DefaultId
        {
            get
            {
                if (string.IsNullOrEmpty(_defaultId))
                {
                    _defaultId = FindDefaultId();
                }
                return _defaultId;
            }
        }

        public static List<TimeZoneChoice> GetChoices()
        {
            if (_choices != null)
            {
                return _choices;
            }

            List<TimeZoneChoice> list = new List<TimeZoneChoice>();
            try
            {
                foreach (TimeZoneInfo zone in TimeZoneInfo.GetSystemTimeZones())
                {
                    list.Add(new TimeZoneChoice(zone));
                }
            }
            catch (Exception)
            {
            }

            if (list.Count == 0)
            {
                list.Add(new TimeZoneChoice(TimeZoneInfo.Utc));
            }

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

            _choices = list;
            return _choices;
        }

        public static string Normalize(string id)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
                    return id.Trim();
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
                catch (SecurityException)
                {
                }
            }
            return DefaultId;
        }

        public static TimeZoneInfo GetZone(string id)
        {
            string normalized = Normalize(id);
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(normalized);
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
            catch (SecurityException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        public static DateTime GetNow(TimeZoneInfo zone)
        {
            TimeZoneInfo target = zone ?? TimeZoneInfo.Utc;
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, target);
            }
            catch (ArgumentException)
            {
                return DateTime.Now;
            }
        }

        public static string GetOffsetLabel(string id)
        {
            TimeZoneInfo zone = GetZone(id);
            TimeSpan offset = zone.BaseUtcOffset;
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            TimeSpan absolute = offset.Duration();
            return string.Format(
                CultureInfo.InvariantCulture,
                "UTC{0}{1:00}:{2:00}",
                sign,
                absolute.Hours,
                absolute.Minutes);
        }

        private static string FindDefaultId()
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                return "China Standard Time";
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
            catch (SecurityException)
            {
            }

            try
            {
                TimeZoneInfo best = null;
                foreach (TimeZoneInfo zone in TimeZoneInfo.GetSystemTimeZones())
                {
                    if (zone.BaseUtcOffset == TimeSpan.FromHours(8.0))
                    {
                        if (!zone.SupportsDaylightSavingTime)
                        {
                            return zone.Id;
                        }
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
            catch (Exception)
            {
            }

            return "UTC";
        }
    }
}
