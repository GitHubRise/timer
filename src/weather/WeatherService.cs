using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

// 天气服务：Open-Meteo 地理编码/预报/空气质量 + ipify 公网 IP，全部走 HTTPS 后台线程。
// 调用方：AppController（定时刷新、设置页预览），回调在**线程池线程**上触发，不能直接碰 UI 控件。
// 边界：任何网络/解析失败都不抛异常，回调 WeatherInfo 为 null，由 UI 保持上次天气或隐藏天气行。
// 坑一：.NET Framework 4.8 默认可能不含 TLS 1.2，请求前必须 ConfigureTls；坑二：Open-Meteo 的 daily
// 数组按请求天数返回，取前 6 天要按 Min/Max/Code 三数组的最小长度截断；坑三：回调里的异常必须吞掉，
// 否则线程池未处理异常会直接终止进程。日志只在请求开始/成功/失败三个节点各 1 条，避免每次刷新刷屏。
namespace GameClock
{
    /// <summary>
    /// 天气刷新频率下拉项：分钟数 + 中文标签，ComboBox 直接显示 <see cref="Label"/>。
    /// </summary>
    internal sealed class WeatherRefreshOption
    {
        /// <summary>构造一个刷新频率选项；minutes 必须落在 <see cref="WeatherRefreshOptions.AllowedMinutes"/> 内。</summary>
        public WeatherRefreshOption(int minutes, string label)
        {
            Minutes = minutes;
            Label = label;
        }

        /// <summary>刷新间隔分钟数（10/20/30/45/60/120）。</summary>
        public int Minutes { get; private set; }

        /// <summary>下拉显示文本，如「每 30 分钟（默认）」。</summary>
        public string Label { get; private set; }

        /// <summary>ComboBox 默认取 ToString，转给 Label 避免显示类型名。</summary>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 天气刷新频率的白名单与归一化：只接受固定几档，非法值回退 30 分钟（默认值）。
    /// </summary>
    internal static class WeatherRefreshOptions
    {
        // 白名单：太少会频繁打 Open-Meteo 免费接口，太多天气会明显过时；30 分钟为默认档。
        private static readonly int[] AllowedMinutes = new int[] { 10, 20, 30, 45, 60, 120 };

        /// <summary>生成下拉框的六个选项（与白名单一一对应），默认档在标签上注明。</summary>
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

        /// <summary>
        /// 归一化刷新间隔：命中白名单原样返回，否则回退 30 分钟。
        /// 纯校验函数，由 AppSettings.Normalize 在载入配置时调用，因此不做日志。
        /// </summary>
        public static int Normalize(int minutes)
        {
            // 线性扫描六个值即可，白名单很小，不值得用集合或二分。
            for (int i = 0; i < AllowedMinutes.Length; i++)
            {
                if (AllowedMinutes[i] == minutes)
                {
                    return minutes;
                }
            }
            // 旧配置/手改配置文件可能写入任意分钟数，统一收敛到默认值，避免刷新过频。
            return 30;
        }
    }

    /// <summary>
    /// 单日预报：日期字符串（Open-Meteo 原样返回 yyyy-MM-dd）、WMO 天气码、中文天气描述与最低/最高温。
    /// </summary>
    internal sealed class DailyForecast
    {
        /// <summary>预报日期，Open-Meteo 的 "time" 字段原文（yyyy-MM-dd），不做二次解析。</summary>
        public string Date { get; set; }

        /// <summary>WMO 天气码，-1 表示接口未给出（描述会退化成「天气」）。</summary>
        public int Code { get; set; }

        /// <summary>由 WMO 码翻译出的中文描述，见 <see cref="WeatherService.DescribeWeather"/>。</summary>
        public string Condition { get; set; }

        /// <summary>当日最低温（摄氏度）；接口缺字段时为 null。</summary>
        public double? Min { get; set; }

        /// <summary>当日最高温（摄氏度）；接口缺字段时为 null。</summary>
        public double? Max { get; set; }
    }

    /// <summary>
    /// 一次天气快照：当前实况 + 最多 6 天预报 + 可选 AQI/公网 IP。
    /// 由后台线程构造后交给回调，构造完成后不再修改，因此可以跨线程安全读取。
    /// </summary>
    internal sealed class WeatherInfo
    {
        /// <summary>初始化空快照：字符串字段给空串而非 null，避免格式引擎拼出「null」字样。</summary>
        public WeatherInfo()
        {
            Condition = string.Empty;
            WindDirection = string.Empty;
            City = string.Empty;
            Days = new List<DailyForecast>();
        }

        /// <summary>城市显示名：命中内置城市表时用用户输入，否则用地理编码返回的规范名。</summary>
        public string City { get; set; }

        /// <summary>当前天气中文描述（晴/多云/小雨…）。</summary>
        public string Condition { get; set; }

        /// <summary>当前 WMO 天气码，供 <see cref="WeatherService.IconForCode"/> 选图标。</summary>
        public int WeatherCode { get; set; }

        /// <summary>当前温度（摄氏度），保留原始小数，显示时再四舍五入。</summary>
        public double Temperature { get; set; }

        /// <summary>今日最低温，来自预报第一天；没有预报数据时为 null。</summary>
        public double? Min { get; set; }

        /// <summary>今日最高温，来自预报第一天；没有预报数据时为 null。</summary>
        public double? Max { get; set; }

        /// <summary>相对湿度百分比（已四舍五入为整数），接口未给出时为 null。</summary>
        public int? Humidity { get; set; }

        /// <summary>风向中文（北风/东北风…），无数据时为空串。</summary>
        public string WindDirection { get; set; }

        /// <summary>美国 AQI 数值（仅启用空气质量时填充）。</summary>
        public int? AqiValue { get; set; }

        /// <summary>AQI 等级中文（优/良/轻度污染…），随 <see cref="AqiValue"/> 一起填充。</summary>
        public string AqiLevel { get; set; }

        /// <summary>公网 IP（仅启用时填充），取不到为空串。</summary>
        public string PublicIp { get; set; }

        /// <summary>未来最多 6 天的预报，按日期升序，<see cref="Days"/>[0] 即今天。</summary>
        public List<DailyForecast> Days { get; set; }

        /// <summary>抓取时刻（UTC），用于判断数据是否过旧。</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>单行展示文本：城市 · 天气 · 温度 · 今日温度区间，供设置页预览与调试使用。</summary>
        public string DisplayText
        {
            get
            {
                string text = City + " · " + Condition + " · " + Round(Temperature) + "°C";
                // 只有同时拿到今日最低/最高温才追加区间，否则会出现「今日 °~°」的残句。
                if (Min.HasValue && Max.HasValue)
                {
                    text += " · 今日 " + Round(Min.Value) + "°~" + Round(Max.Value) + "°";
                }
                return text;
            }
        }

        /// <summary>
        /// 温度取整：用 AwayFromZero 而非银行家舍入，保证 24.5 显示成 25 而不是 24（符合温度直觉）。
        /// </summary>
        private static string Round(double value)
        {
            // 用不变文化转字符串，避免某些区域把小数点写成逗号。
            return Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 抓取选项：空气质量与公网 IP 各要额外一次 HTTP 请求，默认都不抓（悬浮窗通常只显示温度）。
    /// </summary>
    internal sealed class WeatherFetchOptions
    {
        /// <summary>是否额外请求 Open-Meteo 空气质量接口（AQI + 等级）。</summary>
        public bool IncludeAirQuality { get; set; }

        /// <summary>是否额外请求 ipify 获取公网 IP（失败不影响天气本身）。</summary>
        public bool IncludePublicIp { get; set; }
    }

    /// <summary>
    /// 天气取数入口：异步抓取 Open-Meteo 当前天气/预报（可选 AQI）与 ipify 公网 IP，失败回调 null。
    /// </summary>
    internal static class WeatherService
    {
        // 三个 Open-Meteo 端点：免费、无需 API Key；地理编码用于把任意城市名转经纬度。
        private const string GeocodingEndpoint = "https://geocoding-api.open-meteo.com/v1/search";
        private const string ForecastEndpoint = "https://api.open-meteo.com/v1/forecast";
        private const string AirQualityEndpoint = "https://air-quality-api.open-meteo.com/v1/air-quality";

        // 公网 IP 缓存：ipify 有速率限制，且 IP 变化不频繁，6 小时内复用同一结果。
        private static readonly object IpLock = new object();
        private static string CachedIp;
        private static DateTime CachedIpExpiresUtc = DateTime.MinValue;

        // 内置城市表：常见中文城市直接给出经纬度，省掉一次地理编码请求（设置页默认城市是北京）。
        // 键为城市名（不区分大小写），值为 {纬度, 经度}；故意只收录 14 个常用城市，其余走在线地理编码。
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

        /// <summary>
        /// 简化重载：不抓 AQI 与公网 IP，等价于 options 为 null。
        /// </summary>
        public static void FetchAsync(string city, Action<WeatherInfo> callback)
        {
            FetchAsync(city, null, callback);
        }

        /// <summary>
        /// 异步抓取天气：立即返回，结果通过回调在**线程池线程**上交付；失败时回调参数为 null。
        /// </summary>
        /// <param name="city">城市名；空串/空白时默认「北京」。</param>
        /// <param name="options">可选的 AQI / 公网 IP 开关，null 表示都不抓。</param>
        /// <param name="callback">结果回调，必须非 null，且实现方负责切回 UI 线程。</param>
        public static void FetchAsync(string city, WeatherFetchOptions options, Action<WeatherInfo> callback)
        {
            // 没有回调就没有交付对象，直接返回，避免排一个无人消费的后台任务。
            if (callback == null)
            {
                return;
            }

            // 城市名去空格并给默认值：空白城市会让地理编码返回空结果，白白多一次请求。
            string requestedCity = string.IsNullOrWhiteSpace(city) ? "北京" : city.Trim();
            WeatherFetchOptions fetchOptions = options ?? new WeatherFetchOptions();
            // 请求开始日志（一次刷新链路唯一一条「开始」）：只此一处，避免定时刷新把日志刷爆。
            AppLog.Info(
                "Weather",
                "开始获取天气：城市=" + requestedCity
                + "，含空气质量=" + (fetchOptions.IncludeAirQuality ? "是" : "否")
                + "，含公网IP=" + (fetchOptions.IncludePublicIp ? "是" : "否"));

            // 网络请求绝不能在 UI 线程做（会卡住 1 秒的时钟刷新），统一丢给线程池串行执行。
            ThreadPool.QueueUserWorkItem(delegate
            {
                WeatherInfo info = null;
                try
                {
                    info = Fetch(requestedCity, fetchOptions);
                }
                catch (Exception ex)
                {
                    // 任何网络/解析异常都折算成「本次无数据」，保证回调一定会被调用（UI 靠它复位加载态）。
                    AppLog.Warn("Weather", "天气获取异常，本次不更新天气；城市=" + requestedCity + "，异常：" + ex.Message, ex);
                    info = null;
                }

                // 数据为空时在交付前记一条：成功路径已在 Fetch 内记录，这里只覆盖「拿不到数据」的分支。
                if (info == null)
                {
                    AppLog.Warn("Weather", "天气获取失败，回调空结果；城市=" + requestedCity + "（网络不可用或城市无法定位）");
                }

                try
                {
                    callback(info);
                }
                catch (Exception ex)
                {
                    // 回调由 UI 层实现，可能因窗口已关闭而抛异常；线程池未处理异常会结束进程，必须吞掉并记录。
                    AppLog.Warn("Weather", "天气回调处理失败，已忽略本次结果；异常：" + ex.Message, ex);
                }
            });
        }

        /// <summary>
        /// 同步抓取一次天气（在后台线程调用）：定位城市 → 请求预报 → 组装快照 → 可选 AQI/公网 IP。
        /// 返回 null 表示本次失败（网络、地理编码无结果、响应缺关键字段），由 <see cref="FetchAsync"/> 记日志。
        /// </summary>
        private static WeatherInfo Fetch(string city, WeatherFetchOptions options)
        {
            // 必须在发请求前开启 TLS 1.2，否则 .NET Framework 4.8 在旧系统上会报「基础连接已关闭」。
            ConfigureTls();

            double latitude;
            double longitude;
            string displayCity = city;

            // 优先查内置城市表：命中就省掉一次地理编码往返，也避免同名城市定位到国外。
            double[] known;
            if (KnownCities.TryGetValue(city, out known))
            {
                latitude = known[0];
                longitude = known[1];
            }
            else
            {
                // 未收录的城市走 Open-Meteo 地理编码，取第一个匹配结果；无结果则视为城市名无效。
                GeocodingResult result = Geocode(city);
                if (result == null)
                {
                    return null;
                }
                latitude = result.Latitude;
                longitude = result.Longitude;
                // 地理编码会返回规范名（如「Dali」），用它更新显示，用户输入拼音时也能看到正确地名。
                if (!string.IsNullOrWhiteSpace(result.Name))
                {
                    displayCity = result.Name;
                }
            }

            // 拼预报 URL：坐标保留 4 位小数（约 11 米精度，足够城市级天气），走 timezone=auto 让接口按当地时间返回日界。
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
            // 温度是必填项：接口在参数错误时会返回空对象或缺少 current，此时按失败处理。
            if (forecast == null || forecast.Current == null || !forecast.Current.Temperature.HasValue)
            {
                return null;
            }

            // 天气码缺失时用 -1 占位，DescribeWeather/IconForCode 都会退化成「天气」+ 通用云图标。
            int code = forecast.Current.WeatherCode.HasValue ? forecast.Current.WeatherCode.Value : -1;
            WeatherInfo info = new WeatherInfo();
            info.City = displayCity;
            info.Condition = DescribeWeather(code);
            info.WeatherCode = code;
            info.Temperature = forecast.Current.Temperature.Value;
            // 湿度按 AwayFromZero 取整，接口可能返回浮点（如 62.4）。
            info.Humidity = forecast.Current.Humidity.HasValue
                ? (int?)Math.Round(forecast.Current.Humidity.Value, MidpointRounding.AwayFromZero)
                : null;
            info.WindDirection = DescribeWindDirection(forecast.Current.WindDirection);
            // 记录抓取时刻（UTC），显示层若需要「几分钟前更新」可据此换算。
            info.UpdatedAt = DateTime.UtcNow;

            // daily 的三个数组理论上等长，但接口异常时可能缺项，取三者的最小长度再遍历，避免越界。
            if (forecast.Daily != null
                && forecast.Daily.Min != null
                && forecast.Daily.Max != null)
            {
                int count = Math.Min(forecast.Daily.Min.Length, forecast.Daily.Max.Length);
                if (forecast.Daily.WeatherCode != null)
                {
                    count = Math.Min(count, forecast.Daily.WeatherCode.Length);
                }
                // 最多保留 6 天：格式变量 we1/we6 等只到第 6 天，多取无意义。
                for (int i = 0; i < count && i < 6; i++)
                {
                    // 单个日期的天气码可能缺失，单独兜底 -1，不影响其它天的数据。
                    int dailyCode = forecast.Daily.WeatherCode != null && i < forecast.Daily.WeatherCode.Length
                        ? forecast.Daily.WeatherCode[i]
                        : -1;
                    DailyForecast day = new DailyForecast();
                    day.Code = dailyCode;
                    day.Condition = DescribeWeather(dailyCode);
                    day.Min = forecast.Daily.Min[i];
                    day.Max = forecast.Daily.Max[i];
                    // 日期数组同样可能短于温度数组，短了就不填，显示层会跳过无日期的项。
                    if (forecast.Daily.Time != null && i < forecast.Daily.Time.Length)
                    {
                        day.Date = forecast.Daily.Time[i];
                    }
                    info.Days.Add(day);
                }
            }

            // 今日最低/最高温直接取预报第一天，供格式变量 tl/th 与悬浮窗「今日 12°~24°」使用。
            if (info.Days.Count > 0)
            {
                info.Min = info.Days[0].Min;
                info.Max = info.Days[0].Max;
            }

            // 两个可选项各多一次网络请求，失败不影响主体天气（内部自行吞异常并记 Warn）。
            if (options.IncludeAirQuality)
            {
                ApplyAirQuality(info, latitude, longitude);
            }
            if (options.IncludePublicIp)
            {
                info.PublicIp = GetPublicIp();
            }

            // 成功日志：一次刷新链路唯一一条「结果」，带城市/天气/温度/AQI 便于事后核对显示内容。
            AppLog.Info(
                "Weather",
                "天气获取成功：城市=" + info.City
                + "，天气=" + info.Condition
                + "，温度=" + info.Temperature.ToString("0.#", CultureInfo.InvariantCulture) + "°C"
                + "，AQI=" + (info.AqiValue.HasValue ? info.AqiValue.Value.ToString(CultureInfo.InvariantCulture) : "无")
                + "，公网IP=" + (string.IsNullOrEmpty(info.PublicIp) ? "无" : info.PublicIp));

            return info;
        }

        /// <summary>
        /// 抓取美国 AQI 并写入 info；任何失败都只丢 AQI，不影响天气主体。
        /// </summary>
        private static void ApplyAirQuality(WeatherInfo info, double latitude, double longitude)
        {
            try
            {
                // 与预报用同一坐标与 timezone=auto，保证 AQI 对应的是当前城市。
                string url = string.Format(
                    CultureInfo.InvariantCulture,
                    AirQualityEndpoint +
                    "?latitude={0:0.####}&longitude={1:0.####}&current=us_aqi&timezone=auto",
                    latitude,
                    longitude);
                AqiResponse response = ParseJson<AqiResponse>(DownloadString(url));
                // us_aqi 可能缺失（部分区域/海洋），缺失时保持 AqiValue 为 null，格式变量显示空。
                if (response != null && response.Current != null && response.Current.UsAqi.HasValue)
                {
                    // AQI 是整数等级，四舍五入后再查等级文案。
                    info.AqiValue = (int)Math.Round(response.Current.UsAqi.Value, MidpointRounding.AwayFromZero);
                    info.AqiLevel = DescribeAqi(info.AqiValue.Value);
                }
            }
            catch (Exception ex)
            {
                // 空气质量是附加信息：接口超时/限流时静默降级为「无 AQI」，只记一条 Warn 便于判断是否为网络问题。
                AppLog.Warn("Weather", "空气质量获取失败，已忽略 AQI；城市=" + info.City + "，异常：" + ex.Message, ex);
            }
        }

        /// <summary>
        /// 获取公网 IP（ipify），进程内缓存 6 小时；失败返回空串而不是 null，方便直接拼到格式串里。
        /// </summary>
        private static string GetPublicIp()
        {
            // 先查缓存：ipify 免费接口有速率限制，且公网 IP 变化不频繁。
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
                // ipify 返回 {"ip":"x.x.x.x"}，这里的第二个参数 format=json 是接口约定。
                IpResponse response = ParseJson<IpResponse>(DownloadString("https://api.ipify.org?format=json"));
                if (response != null && !string.IsNullOrWhiteSpace(response.Ip))
                {
                    ip = response.Ip.Trim();
                }
            }
            catch (Exception ex)
            {
                // IP 只是锦上添花的格式变量：失败时保持空串，不阻断天气刷新。
                AppLog.Warn("Weather", "公网 IP 获取失败，已忽略；异常：" + ex.Message, ex);
            }

            // 只在拿到有效 IP 时写缓存，失败不缓存，下次刷新会重试。
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

        /// <summary>
        /// 把风向角度翻译成八方位中文（北/东北/东/东南/南/西南/西/西北）；无数据或 NaN 返回空串。
        /// </summary>
        private static string DescribeWindDirection(double? degrees)
        {
            // 无值或 NaN 都表示接口没给有效风向，返回空串让调用方决定是否显示。
            if (!degrees.HasValue || double.IsNaN(degrees.Value))
            {
                return string.Empty;
            }
            // 八方位表，索引 0 指向正北，每格 45°。
            string[] names = new string[] { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
            // 先把角度收敛到 [0,360)：接口可能给负数或超过 360 的值。
            double value = ((degrees.Value % 360) + 360) % 360;
            // 除以 45 再四舍五入取最近方位；%8 处理 337.5°~360° 归到「北」。
            int index = (int)Math.Round(value / 45.0, MidpointRounding.AwayFromZero) % 8;
            return names[index] + "风";
        }

        /// <summary>
        /// 按美国 AQI 标准翻译等级：0–50 优、51–100 良、101–150 轻度、151–200 中度、201–300 重度、300+ 严重。
        /// </summary>
        private static string DescribeAqi(int value)
        {
            // 逐级判断即可：AQI 阈值固定，无需表驱动。
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
            // 超过 300 属严重污染（含 301 以上所有值）。
            return "严重污染";
        }

        /// <summary>
        /// WMO 天气码 → 图标字符；未知码回退云图标，保证「有天气就一定有图标」不会出现空位。
        /// </summary>
        public static string IconForCode(int code)
        {
            // 按天气族分组：晴/多云/阴雾、各类降水、固态降水、雷暴，其余归入默认云。
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
                    // 雾天用云图标区分度不足，但图标集里没有雾符号，沿用云。
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
                    // 毛毛雨/冻雨/雨/阵雨统一用伞。
                    return "☂";
                case 71:
                case 73:
                case 75:
                case 77:
                case 85:
                case 86:
                    // 雪/米雪/阵雪统一用雪花。
                    return "❄";
                case 95:
                case 96:
                case 99:
                    // 雷雨/雷暴伴冰雹用闪电。
                    return "⚡";
                default:
                    // 未知码（含 -1 占位）不应让界面上出现空白，回退云图标。
                    return "☁";
            }
        }

        /// <summary>
        /// 城市名 → 经纬度：调用 Open-Meteo 地理编码，只取第一个结果；无结果返回 null。
        /// </summary>
        private static GeocodingResult Geocode(string city)
        {
            // EscapeDataString 处理中文城市名与空格；language=zh 让返回的地名是中文。
            string url = string.Format(
                CultureInfo.InvariantCulture,
                GeocodingEndpoint + "?name={0}&count=1&language=zh&format=json",
                Uri.EscapeDataString(city));
            string json = DownloadString(url);
            GeocodingResponse response = ParseJson<GeocodingResponse>(json);
            // 结果为空说明城市名无法识别（拼错、生僻地名），由上层按「获取失败」处理。
            if (response == null || response.Results == null || response.Results.Count == 0)
            {
                return null;
            }
            return response.Results[0];
        }

        /// <summary>
        /// 简单的同步 GET，返回响应正文；网络错误原样抛出，由调用链最外层折算成「无数据」。
        /// </summary>
        private static string DownloadString(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            // 自定义 UA：Open-Meteo 建议标识客户端；同时避免个别代理拦截空 UA 请求。
            request.UserAgent = "GameClock/1.0 (weather overlay)";
            request.Accept = "application/json";
            // 10 秒超时：刷新由定时器驱动，卡太久会拖住线程池队列，宁愿放弃本次。
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;

            // 三层 using 保证响应流与读取器释放；HttpWebResponse 不释放会占连接。
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                // 抛异常时响应对象仍挂在异常上：不关就要等 GC 终结器才还连接，
                // 连续失败（断网、接口 4xx/5xx）会攒下半开连接，所以显式关掉再原样抛出。
                if (ex.Response != null)
                {
                    ex.Response.Close();
                }
                throw;
            }
        }

        /// <summary>
        /// 反序列化 Open-Meteo/ipify 的 JSON；空响应返回 default(T)，不抛异常。
        /// </summary>
        private static T ParseJson<T>(string json)
        {
            // 空串常见于 204 或代理返回空 body，直接返回默认值让上层走「缺字段」分支。
            if (string.IsNullOrWhiteSpace(json))
            {
                return default(T);
            }

            // DataContractJsonSerializer 是 .NET Framework 自带方案，无需引第三方 JSON 库。
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(stream);
            }
        }

        /// <summary>
        /// 为当前进程启用 TLS 1.2：.NET Framework 4.8 在旧系统上默认协议可能是 TLS 1.0，会被 Open-Meteo 拒绝。
        /// </summary>
        private static void ConfigureTls()
        {
            try
            {
                // 用 |= 而非 =，保留系统已启用的其它协议版本。
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (NotSupportedException ex)
            {
                // 极老系统（Win7 未打补丁）不认识 Tls12 枚举值；此时无法补救，后续 HTTPS 会以网络错误形式失败，
                // 由 Weather 的失败日志覆盖，这里只提示一次「TLS 设置未生效」。
                AppLog.Warn("Weather", "当前系统不支持 TLS 1.2，HTTPS 请求可能失败；异常：" + ex.Message, ex);
            }
        }

        /// <summary>
        /// WMO 天气码 → 中文描述；未知码返回「天气」，保证格式串不会拼出空段。
        /// </summary>
        private static string DescribeWeather(int code)
        {
            // 按 WMO 4677 码表分组映射，同一描述的多码合并 case。
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
                // 未知码（含 -1 占位）用「天气」兜底，读起来仍通顺。
                default: return "天气";
            }
        }

        // ── 以下为 JSON 反序列化模型（DataContractJsonSerializer 用 DataMember 映射接口字段名）──
        // 这些类型只在内部使用，字段名与 Open-Meteo/ipify 的 JSON 键一一对应；加字段时记得同步接口参数。

        /// <summary>地理编码响应根对象：results 数组，空结果时该字段缺失（null）。</summary>
        [DataContract]
        private sealed class GeocodingResponse
        {
            /// <summary>匹配到的城市列表，取第 0 项。</summary>
            [DataMember(Name = "results")]
            public List<GeocodingResult> Results { get; set; }
        }

        /// <summary>地理编码的单个匹配项：名称 + 经纬度（另有 admin1/country 备用，当前不展示）。</summary>
        [DataContract]
        private sealed class GeocodingResult
        {
            /// <summary>规范化地名，用于替换用户输入的拼音/别名。</summary>
            [DataMember(Name = "name")]
            public string Name { get; set; }

            /// <summary>纬度（-90~90）。</summary>
            [DataMember(Name = "latitude")]
            public double Latitude { get; set; }

            /// <summary>经度（-180~180）。</summary>
            [DataMember(Name = "longitude")]
            public double Longitude { get; set; }

            /// <summary>一级行政区名，当前未使用，保留以便将来做「省·市」显示。</summary>
            [DataMember(Name = "admin1")]
            public string Admin1 { get; set; }

            /// <summary>国家名，当前未使用，保留用于消歧。</summary>
            [DataMember(Name = "country")]
            public string Country { get; set; }
        }

        /// <summary>预报响应根对象：current + daily 两块。</summary>
        [DataContract]
        private sealed class ForecastResponse
        {
            /// <summary>当前实况；字段缺失时为 null。</summary>
            [DataMember(Name = "current")]
            public CurrentWeather Current { get; set; }

            /// <summary>逐日预报数组；字段缺失时为 null。</summary>
            [DataMember(Name = "daily")]
            public DailyWeather Daily { get; set; }
        }

        /// <summary>当前实况字段：温度、天气码、湿度、风向。</summary>
        [DataContract]
        private sealed class CurrentWeather
        {
            /// <summary>2 米高度气温（摄氏度）；缺失时为 null。</summary>
            [DataMember(Name = "temperature_2m")]
            public double? Temperature { get; set; }

            /// <summary>WMO 天气码；缺失时为 null（上层按 -1 处理）。</summary>
            [DataMember(Name = "weather_code")]
            public int? WeatherCode { get; set; }

            /// <summary>2 米高度相对湿度（百分比，可能带小数）。</summary>
            [DataMember(Name = "relative_humidity_2m")]
            public double? Humidity { get; set; }

            /// <summary>10 米高度风向（度，0=北风，顺时针）。</summary>
            [DataMember(Name = "wind_direction_10m")]
            public double? WindDirection { get; set; }
        }

        /// <summary>逐日预报：四个等长数组（日期、天气码、最高温、最低温），按天对齐。</summary>
        [DataContract]
        private sealed class DailyWeather
        {
            /// <summary>日期数组，元素形如 "2026-10-30"。</summary>
            [DataMember(Name = "time")]
            public string[] Time { get; set; }

            /// <summary>每日天气码数组。</summary>
            [DataMember(Name = "weather_code")]
            public int[] WeatherCode { get; set; }

            /// <summary>每日最高温数组（摄氏度）。</summary>
            [DataMember(Name = "temperature_2m_max")]
            public double[] Max { get; set; }

            /// <summary>每日最低温数组（摄氏度）。</summary>
            [DataMember(Name = "temperature_2m_min")]
            public double[] Min { get; set; }
        }

        /// <summary>空气质量响应根对象。</summary>
        [DataContract]
        private sealed class AqiResponse
        {
            /// <summary>当前空气质量字段；缺失时为 null。</summary>
            [DataMember(Name = "current")]
            public AqiCurrent Current { get; set; }
        }

        /// <summary>空气质量当前值：只用美国 AQI（us_aqi），其它污染物指标不取。</summary>
        [DataContract]
        private sealed class AqiCurrent
        {
            /// <summary>美国 AQI 数值；接口未覆盖该地区时为 null。</summary>
            [DataMember(Name = "us_aqi")]
            public double? UsAqi { get; set; }
        }

        /// <summary>ipify 响应：只有 ip 一个字段。</summary>
        [DataContract]
        private sealed class IpResponse
        {
            /// <summary>公网 IPv4/IPv6 文本。</summary>
            [DataMember(Name = "ip")]
            public string Ip { get; set; }
        }
    }
}
