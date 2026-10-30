using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace GameClock
{
    internal sealed class WeatherRefreshOption
    {
        public WeatherRefreshOption(int minutes, string label)
        {
            Minutes = minutes;
            Label = label;
        }

        public int Minutes { get; private set; }
        public string Label { get; private set; }

        public override string ToString()
        {
            return Label;
        }
    }

    internal static class WeatherRefreshOptions
    {
        private static readonly int[] AllowedMinutes = new int[] { 10, 20, 30, 45, 60, 120 };

        public static List<WeatherRefreshOption> Create()
        {
            List<WeatherRefreshOption> list = new List<WeatherRefreshOption>();
            list.Add(new WeatherRefreshOption(10, "每 10 分钟"));
            list.Add(new WeatherRefreshOption(20, "每 20 分钟"));
            list.Add(new WeatherRefreshOption(30, "每 30 分钟（默认）"));
            list.Add(new WeatherRefreshOption(45, "每 45 分钟"));
            list.Add(new WeatherRefreshOption(60, "每 1 小时"));
            list.Add(new WeatherRefreshOption(120, "每 2 小时"));
            return list;
        }

        public static int Normalize(int minutes)
        {
            for (int i = 0; i < AllowedMinutes.Length; i++)
            {
                if (AllowedMinutes[i] == minutes)
                {
                    return minutes;
                }
            }
            return 30;
        }
    }

    internal sealed class DailyForecast
    {
        public string Date { get; set; }
        public int Code { get; set; }
        public string Condition { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
    }

    internal sealed class WeatherInfo
    {
        public WeatherInfo()
        {
            Condition = string.Empty;
            WindDirection = string.Empty;
            City = string.Empty;
            Days = new List<DailyForecast>();
        }

        public string City { get; set; }
        public string Condition { get; set; }
        public int WeatherCode { get; set; }
        public double Temperature { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public int? Humidity { get; set; }
        public string WindDirection { get; set; }
        public int? AqiValue { get; set; }
        public string AqiLevel { get; set; }
        public string PublicIp { get; set; }
        public List<DailyForecast> Days { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string DisplayText
        {
            get
            {
                string text = City + " · " + Condition + " · " + Round(Temperature) + "°C";
                if (Min.HasValue && Max.HasValue)
                {
                    text += " · 今日 " + Round(Min.Value) + "°~" + Round(Max.Value) + "°";
                }
                return text;
            }
        }

        private static string Round(double value)
        {
            return Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed class WeatherFetchOptions
    {
        public bool IncludeAirQuality { get; set; }
        public bool IncludePublicIp { get; set; }
    }

    internal static class WeatherService
    {
        private const string GeocodingEndpoint = "https://geocoding-api.open-meteo.com/v1/search";
        private const string ForecastEndpoint = "https://api.open-meteo.com/v1/forecast";
        private const string AirQualityEndpoint = "https://air-quality-api.open-meteo.com/v1/air-quality";

        private static readonly object IpLock = new object();
        private static string CachedIp;
        private static DateTime CachedIpExpiresUtc = DateTime.MinValue;

        private static readonly Dictionary<string, double[]> KnownCities =
            new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "北京", new double[] { 39.9042, 116.4074 } },
                { "Beijing", new double[] { 39.9042, 116.4074 } },
                { "上海", new double[] { 31.2304, 121.4737 } },
                { "Shanghai", new double[] { 31.2304, 121.4737 } },
                { "广州", new double[] { 23.1291, 113.2644 } },
                { "Guangzhou", new double[] { 23.1291, 113.2644 } },
                { "深圳", new double[] { 22.5431, 114.0579 } },
                { "Shenzhen", new double[] { 22.5431, 114.0579 } },
                { "杭州", new double[] { 30.2741, 120.1551 } },
                { "Hangzhou", new double[] { 30.2741, 120.1551 } },
                { "成都", new double[] { 30.5728, 104.0668 } },
                { "Chengdu", new double[] { 30.5728, 104.0668 } },
                { "重庆", new double[] { 29.5630, 106.5516 } },
                { "Chongqing", new double[] { 29.5630, 106.5516 } },
                { "武汉", new double[] { 30.5928, 114.3055 } },
                { "Wuhan", new double[] { 30.5928, 114.3055 } },
                { "西安", new double[] { 34.3416, 108.9398 } },
                { "Xi'an", new double[] { 34.3416, 108.9398 } },
                { "南京", new double[] { 32.0603, 118.7969 } },
                { "Nanjing", new double[] { 32.0603, 118.7969 } },
                { "天津", new double[] { 39.0842, 117.2009 } },
                { "Tianjin", new double[] { 39.0842, 117.2009 } },
                { "苏州", new double[] { 31.2989, 120.5853 } },
                { "Suzhou", new double[] { 31.2989, 120.5853 } },
                { "长沙", new double[] { 28.2282, 112.9388 } },
                { "Changsha", new double[] { 28.2282, 112.9388 } },
                { "郑州", new double[] { 34.7466, 113.6254 } },
                { "Zhengzhou", new double[] { 34.7466, 113.6254 } }
            };

        public static void FetchAsync(string city, Action<WeatherInfo> callback)
        {
            FetchAsync(city, null, callback);
        }

        public static void FetchAsync(string city, WeatherFetchOptions options, Action<WeatherInfo> callback)
        {
            if (callback == null)
            {
                return;
            }

            string requestedCity = string.IsNullOrWhiteSpace(city) ? "北京" : city.Trim();
            WeatherFetchOptions fetchOptions = options ?? new WeatherFetchOptions();
            ThreadPool.QueueUserWorkItem(delegate
            {
                WeatherInfo info = null;
                try
                {
                    info = Fetch(requestedCity, fetchOptions);
                }
                catch (Exception)
                {
                    info = null;
                }

                try
                {
                    callback(info);
                }
                catch (Exception)
                {
                }
            });
        }

        private static WeatherInfo Fetch(string city, WeatherFetchOptions options)
        {
            ConfigureTls();

            double latitude;
            double longitude;
            string displayCity = city;

            double[] known;
            if (KnownCities.TryGetValue(city, out known))
            {
                latitude = known[0];
                longitude = known[1];
            }
            else
            {
                GeocodingResult result = Geocode(city);
                if (result == null)
                {
                    return null;
                }
                latitude = result.Latitude;
                longitude = result.Longitude;
                if (!string.IsNullOrWhiteSpace(result.Name))
                {
                    displayCity = result.Name;
                }
            }

            string url = string.Format(
                CultureInfo.InvariantCulture,
                ForecastEndpoint +
                "?latitude={0:0.####}&longitude={1:0.####}" +
                "&current=temperature_2m,weather_code,relative_humidity_2m,wind_direction_10m" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                "&timezone=auto&forecast_days=6",
                latitude,
                longitude);

            string json = DownloadString(url);
            ForecastResponse forecast = ParseJson<ForecastResponse>(json);
            if (forecast == null || forecast.Current == null || !forecast.Current.Temperature.HasValue)
            {
                return null;
            }

            int code = forecast.Current.WeatherCode.HasValue ? forecast.Current.WeatherCode.Value : -1;
            WeatherInfo info = new WeatherInfo();
            info.City = displayCity;
            info.Condition = DescribeWeather(code);
            info.WeatherCode = code;
            info.Temperature = forecast.Current.Temperature.Value;
            info.Humidity = forecast.Current.Humidity.HasValue
                ? (int?)Math.Round(forecast.Current.Humidity.Value, MidpointRounding.AwayFromZero)
                : null;
            info.WindDirection = DescribeWindDirection(forecast.Current.WindDirection);
            info.UpdatedAt = DateTime.UtcNow;

            if (forecast.Daily != null
                && forecast.Daily.Min != null
                && forecast.Daily.Max != null)
            {
                int count = Math.Min(forecast.Daily.Min.Length, forecast.Daily.Max.Length);
                if (forecast.Daily.WeatherCode != null)
                {
                    count = Math.Min(count, forecast.Daily.WeatherCode.Length);
                }
                for (int i = 0; i < count && i < 6; i++)
                {
                    int dailyCode = forecast.Daily.WeatherCode != null && i < forecast.Daily.WeatherCode.Length
                        ? forecast.Daily.WeatherCode[i]
                        : -1;
                    DailyForecast day = new DailyForecast();
                    day.Code = dailyCode;
                    day.Condition = DescribeWeather(dailyCode);
                    day.Min = forecast.Daily.Min[i];
                    day.Max = forecast.Daily.Max[i];
                    if (forecast.Daily.Time != null && i < forecast.Daily.Time.Length)
                    {
                        day.Date = forecast.Daily.Time[i];
                    }
                    info.Days.Add(day);
                }
            }

            if (info.Days.Count > 0)
            {
                info.Min = info.Days[0].Min;
                info.Max = info.Days[0].Max;
            }

            if (options.IncludeAirQuality)
            {
                ApplyAirQuality(info, latitude, longitude);
            }
            if (options.IncludePublicIp)
            {
                info.PublicIp = GetPublicIp();
            }

            return info;
        }

        private static void ApplyAirQuality(WeatherInfo info, double latitude, double longitude)
        {
            try
            {
                string url = string.Format(
                    CultureInfo.InvariantCulture,
                    AirQualityEndpoint +
                    "?latitude={0:0.####}&longitude={1:0.####}&current=us_aqi&timezone=auto",
                    latitude,
                    longitude);
                AqiResponse response = ParseJson<AqiResponse>(DownloadString(url));
                if (response != null && response.Current != null && response.Current.UsAqi.HasValue)
                {
                    info.AqiValue = (int)Math.Round(response.Current.UsAqi.Value, MidpointRounding.AwayFromZero);
                    info.AqiLevel = DescribeAqi(info.AqiValue.Value);
                }
            }
            catch (Exception)
            {
            }
        }

        private static string GetPublicIp()
        {
            lock (IpLock)
            {
                if (!string.IsNullOrEmpty(CachedIp) && DateTime.UtcNow < CachedIpExpiresUtc)
                {
                    return CachedIp;
                }
            }

            string ip = null;
            try
            {
                IpResponse response = ParseJson<IpResponse>(DownloadString("https://api.ipify.org?format=json"));
                if (response != null && !string.IsNullOrWhiteSpace(response.Ip))
                {
                    ip = response.Ip.Trim();
                }
            }
            catch (Exception)
            {
            }

            if (!string.IsNullOrEmpty(ip))
            {
                lock (IpLock)
                {
                    CachedIp = ip;
                    CachedIpExpiresUtc = DateTime.UtcNow.AddHours(6);
                }
            }
            return ip ?? string.Empty;
        }

        private static string DescribeWindDirection(double? degrees)
        {
            if (!degrees.HasValue || double.IsNaN(degrees.Value))
            {
                return string.Empty;
            }
            string[] names = new string[] { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
            double value = ((degrees.Value % 360) + 360) % 360;
            int index = (int)Math.Round(value / 45.0, MidpointRounding.AwayFromZero) % 8;
            return names[index] + "风";
        }

        private static string DescribeAqi(int value)
        {
            if (value <= 50)
            {
                return "优";
            }
            if (value <= 100)
            {
                return "良";
            }
            if (value <= 150)
            {
                return "轻度污染";
            }
            if (value <= 200)
            {
                return "中度污染";
            }
            if (value <= 300)
            {
                return "重度污染";
            }
            return "严重污染";
        }

        public static string IconForCode(int code)
        {
            switch (code)
            {
                case 0:
                case 1:
                    return "☀";
                case 2:
                    return "⛅";
                case 3:
                    return "☁";
                case 45:
                case 48:
                    return "☁";
                case 51:
                case 53:
                case 55:
                case 56:
                case 57:
                case 61:
                case 63:
                case 65:
                case 66:
                case 67:
                case 80:
                case 81:
                case 82:
                    return "☂";
                case 71:
                case 73:
                case 75:
                case 77:
                case 85:
                case 86:
                    return "❄";
                case 95:
                case 96:
                case 99:
                    return "⚡";
                default:
                    return "☁";
            }
        }

        private static GeocodingResult Geocode(string city)
        {
            string url = string.Format(
                CultureInfo.InvariantCulture,
                GeocodingEndpoint + "?name={0}&count=1&language=zh&format=json",
                Uri.EscapeDataString(city));
            string json = DownloadString(url);
            GeocodingResponse response = ParseJson<GeocodingResponse>(json);
            if (response == null || response.Results == null || response.Results.Count == 0)
            {
                return null;
            }
            return response.Results[0];
        }

        private static string DownloadString(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "GameClock/1.0 (weather overlay)";
            request.Accept = "application/json";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static T ParseJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default(T);
            }

            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(stream);
            }
        }

        private static void ConfigureTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (NotSupportedException)
            {
            }
        }

        private static string DescribeWeather(int code)
        {
            switch (code)
            {
                case 0: return "晴";
                case 1: return "大部晴朗";
                case 2: return "多云";
                case 3: return "阴";
                case 45:
                case 48: return "有雾";
                case 51: return "小毛毛雨";
                case 53: return "毛毛雨";
                case 55: return "强毛毛雨";
                case 56:
                case 57: return "冻毛毛雨";
                case 61: return "小雨";
                case 63: return "中雨";
                case 65: return "大雨";
                case 66:
                case 67: return "冻雨";
                case 71: return "小雪";
                case 73: return "中雪";
                case 75: return "大雪";
                case 77: return "米雪";
                case 80: return "小阵雨";
                case 81: return "阵雨";
                case 82: return "强阵雨";
                case 85: return "小阵雪";
                case 86: return "强阵雪";
                case 95: return "雷雨";
                case 96:
                case 99: return "雷暴伴冰雹";
                default: return "天气";
            }
        }

        [DataContract]
        private sealed class GeocodingResponse
        {
            [DataMember(Name = "results")]
            public List<GeocodingResult> Results { get; set; }
        }

        [DataContract]
        private sealed class GeocodingResult
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "latitude")]
            public double Latitude { get; set; }

            [DataMember(Name = "longitude")]
            public double Longitude { get; set; }

            [DataMember(Name = "admin1")]
            public string Admin1 { get; set; }

            [DataMember(Name = "country")]
            public string Country { get; set; }
        }

        [DataContract]
        private sealed class ForecastResponse
        {
            [DataMember(Name = "current")]
            public CurrentWeather Current { get; set; }

            [DataMember(Name = "daily")]
            public DailyWeather Daily { get; set; }
        }

        [DataContract]
        private sealed class CurrentWeather
        {
            [DataMember(Name = "temperature_2m")]
            public double? Temperature { get; set; }

            [DataMember(Name = "weather_code")]
            public int? WeatherCode { get; set; }

            [DataMember(Name = "relative_humidity_2m")]
            public double? Humidity { get; set; }

            [DataMember(Name = "wind_direction_10m")]
            public double? WindDirection { get; set; }
        }

        [DataContract]
        private sealed class DailyWeather
        {
            [DataMember(Name = "time")]
            public string[] Time { get; set; }

            [DataMember(Name = "weather_code")]
            public int[] WeatherCode { get; set; }

            [DataMember(Name = "temperature_2m_max")]
            public double[] Max { get; set; }

            [DataMember(Name = "temperature_2m_min")]
            public double[] Min { get; set; }
        }

        [DataContract]
        private sealed class AqiResponse
        {
            [DataMember(Name = "current")]
            public AqiCurrent Current { get; set; }
        }

        [DataContract]
        private sealed class AqiCurrent
        {
            [DataMember(Name = "us_aqi")]
            public double? UsAqi { get; set; }
        }

        [DataContract]
        private sealed class IpResponse
        {
            [DataMember(Name = "ip")]
            public string Ip { get; set; }
        }
    }
}
