// 天气服务：把 Open-Meteo（地理编码 / 实时天气 / 空气质量）与 ipify 的 JSON 取回来，转成 WeatherInfo。
// 调用方：AppController 定时刷新悬浮窗与任务栏天气，SettingsWindow 的“测试”按钮验证城市是否可查。
// 线程模型：<see cref="FetchAsync"/> 立即返回，取数在 ThreadPool 线程完成，回调也在后台线程触发，
// 订阅方必须自行切回 UI 线程；任何失败（网络、超时、JSON 结构不符）都以回调 null 表示，不抛异常。
// 约束：不读写设置、不碰界面、不落盘；内置城市表与 IP 缓存都是进程级静态状态，供多次刷新复用。

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

    /// <summary>
    /// 天气刷新间隔下拉框的一项：<see cref="Minutes"/> 是分钟数，<see cref="Label"/> 是界面文案。
    /// 不可变选项对象，取值必须来自 <see cref="WeatherRefreshOptions"/> 的白名单。
    /// </summary>
    internal sealed class WeatherRefreshOption
    {

        /// <summary>保存间隔分钟数与显示文案，不做范围校验。</summary>
        public WeatherRefreshOption(int minutes, string label)
        {
            Minutes = minutes;
            Label = label;
        }

        /// <summary>刷新间隔（分钟），落盘进 <c>WeatherRefreshMinutes</c>。</summary>
        public int Minutes { get; private set; }

        /// <summary>界面显示文案，如“每 30 分钟（默认）”。</summary>
        public string Label { get; private set; }

        /// <summary>让下拉框直接显示 <see cref="Label"/>。</summary>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 天气刷新间隔的可选档位与归一化：只允许 10/20/30/45/60/120 分钟，
    /// 手改配置写出 0 或负数时统一回落 30 分钟，避免退化成不停外呼的忙轮询。
    /// </summary>
    internal static class WeatherRefreshOptions
    {
        // ===== 可选档位 =====

        /// <summary>允许的分钟数白名单；顺序与下拉框一致，30 同时是默认值与非法值兜底。</summary>
        private static readonly int[] AllowedMinutes = new int[] { 10, 20, 30, 45, 60, 120 };

        // ===== 选项与归一 =====

        /// <summary>生成设置界面下拉框的选项列表；“默认”标记与 <see cref="Normalize"/> 的兜底值保持一致。</summary>
        public static List<WeatherRefreshOption> Create()
        {
            List<WeatherRefreshOption> list = new List<WeatherRefreshOption>();
            list.Add(new WeatherRefreshOption(10, "每 10 分钟"));
            list.Add(new WeatherRefreshOption(20, "每 20 分钟"));
            // 30 分钟是默认档：接口免费额度与天气变化速度之间的折中。
            list.Add(new WeatherRefreshOption(30, "每 30 分钟（默认）"));
            list.Add(new WeatherRefreshOption(45, "每 45 分钟"));
            list.Add(new WeatherRefreshOption(60, "每 1 小时"));
            list.Add(new WeatherRefreshOption(120, "每 2 小时"));
            return list;
        }

        /// <summary>
        /// 把任意分钟数收敛到白名单：命中就原样返回，否则回落 30。
        /// 线性扫描足够，列表只有 6 项，且避免为一次校验分配集合。
        /// </summary>
        public static int Normalize(int minutes)
        {
            // 逐项比对白名单，命中即返回原值，保证界面选项与生效值一一对应。
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

    /// <summary>
    /// 单日预报：日期 + 天气代码 + 中文描述 + 最低/最高温。
    /// 只作为 <see cref="WeatherInfo.Days"/> 的元素，由天气服务填好，界面按 <c>Days[0]</c> 显示今天。
    /// </summary>
    internal sealed class DailyForecast
    {

        /// <summary>预报日期，原样取接口返回的 <c>yyyy-MM-dd</c>；可能为 null（接口缺字段时）。</summary>
        public string Date { get; set; }

        /// <summary>WMO 天气代码，用于选图标；缺数据时为 -1。</summary>
        public int Code { get; set; }

        /// <summary>天气代码对应的中文描述，如“小雨”。</summary>
        public string Condition { get; set; }

        /// <summary>当日最低温（摄氏度）；接口缺失时为 null，界面据此决定是否显示温度区间。</summary>
        public double? Min { get; set; }

        /// <summary>当日最高温（摄氏度）；与 <see cref="Min"/> 成对使用。</summary>
        public double? Max { get; set; }
    }

    /// <summary>
    /// 一次成功取回的天气快照：当前实况 + 最多 6 天预报 + 可选的空气质量与公网 IP。
    /// 由 <see cref="WeatherService.FetchAsync"/> 的回调交付，AppController 缓存后分发给悬浮窗与任务栏窗；
    /// 取数失败时回调收到的是 null，而不是一个空对象，调用方据此保留上一次的数据。
    /// </summary>
    internal sealed class WeatherInfo
    {

        /// <summary>构造时把字符串字段置空、预报列表建好，避免使用方到处判 null。</summary>
        public WeatherInfo()
        {
            Condition = string.Empty;
            WindDirection = string.Empty;
            City = string.Empty;
            Days = new List<DailyForecast>();
        }

        // ===== 实况字段 =====

        /// <summary>显示用城市名；命中内置城市表时是用户输入，否则优先用地理编码返回的标准名。</summary>
        public string City { get; set; }

        /// <summary>当前天气的中文描述，如“多云”。</summary>
        public string Condition { get; set; }

        /// <summary>当前 WMO 天气代码，供 <see cref="WeatherService.IconForCode"/> 选图标。</summary>
        public int WeatherCode { get; set; }

        /// <summary>当前气温（摄氏度）。</summary>
        public double Temperature { get; set; }

        /// <summary>今日最低温；取自 <see cref="Days"/> 第一项，没有预报数据时为 null。</summary>
        public double? Min { get; set; }

        /// <summary>今日最高温；与 <see cref="Min"/> 同时有值才显示温度区间。</summary>
        public double? Max { get; set; }

        /// <summary>相对湿度（百分比）；接口缺失时为 null。</summary>
        public int? Humidity { get; set; }

        /// <summary>风向中文名，如“东南风”；风速与角度不展示，只保留方向。</summary>
        public string WindDirection { get; set; }

        /// <summary>美国 AQI 数值；只有格式里用到空气质量时才会请求，未请求时为 null。</summary>
        public int? AqiValue { get; set; }

        /// <summary>AQI 等级中文名，如“良”；与 <see cref="AqiValue"/> 同时有值。</summary>
        public string AqiLevel { get; set; }

        /// <summary>公网 IP；只有格式里用到该变量时才请求，失败时为空串。</summary>
        public string PublicIp { get; set; }

        /// <summary>最多 6 天的预报，第 0 项是今天；取数失败或接口缺字段时可能为空列表。</summary>
        public List<DailyForecast> Days { get; set; }

        /// <summary>取数完成时刻（UTC）；目前只做诊断用，界面不直接显示。</summary>
        public DateTime UpdatedAt { get; set; }

        // ===== 展示文本 =====

        /// <summary>
        /// 拼一行默认文案：<c>城市 · 天气 · 气温°C</c>，有最高/最低温时追加“今日 x°~y°”。
        /// 温度四舍五入到整数；只在一行里显示，调用方若自定义格式则不会用到它。
        /// </summary>
        public string DisplayText
        {
            get
            {
                string text = City + " · " + Condition + " · " + Round(Temperature) + "°C";

                // 两个温度必须同时存在才拼区间，避免出现“今日 12°~”这种半截文案。
                if (Min.HasValue && Max.HasValue)
                {
                    text += " · 今日 " + Round(Min.Value) + "°~" + Round(Max.Value) + "°";
                }

                return text;
            }
        }

        /// <summary>
        /// 温度取整：四舍五入且 .5 远离零，与用户对“25.5 度算 26 度”的直觉一致；
        /// 用不变文化输出，保证小数点与负号不受系统区域影响。
        /// </summary>
        private static string Round(double value)
        {
            return Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 单次取数的可选开关：是否顺带请求空气质量与公网 IP。
    /// 由调用方按当前显示格式决定（只有格式里出现对应变量才置 true），避免为了不显示的字段多打两次请求。
    /// </summary>
    internal sealed class WeatherFetchOptions
    {

        /// <summary>是否请求 AQI；true 时多一次 air-quality 接口调用，失败不影响主天气。</summary>
        public bool IncludeAirQuality { get; set; }

        /// <summary>是否请求公网 IP；true 时多一次 ipify 调用，结果带 6 小时缓存。</summary>
        public bool IncludePublicIp { get; set; }
    }

    /// <summary>
    /// 天气取数服务：静态工具类，无实例状态，可直接从任意线程调用。
    /// 主流程是“定坐标（内置表或地理编码）→ 取 6 天预报 → 解析成 <see cref="WeatherInfo"/>”，
    /// 再按需补空气质量与公网 IP；所有网络与解析异常都在 <see cref="FetchAsync"/> 的后台任务里兜住。
    /// </summary>
    internal static class WeatherService
    {
        // ===== 接口地址 =====

        /// <summary>Open-Meteo 地理编码：把城市名换成经纬度，只在城市不在内置表时调用。</summary>
        private const string GeocodingEndpoint = "https://geocoding-api.open-meteo.com/v1/search";

        /// <summary>Open-Meteo 天气预报：当前实况 + 每日最高/最低温，主数据来源。</summary>
        private const string ForecastEndpoint = "https://api.open-meteo.com/v1/forecast";

        /// <summary>Open-Meteo 空气质量：只取 <c>us_aqi</c>，属于可选增强数据。</summary>
        private const string AirQualityEndpoint = "https://air-quality-api.open-meteo.com/v1/air-quality";

        // ===== IP 缓存 =====

        /// <summary>公网 IP 缓存的互斥锁；只保护读写两个字段，网络请求在锁外进行。</summary>
        private static readonly object IpLock = new object();

        /// <summary>上次成功取到的公网 IP；null/空表示没有缓存。</summary>
        private static string CachedIp;

        /// <summary>IP 缓存的过期时刻（UTC）；默认最小值表示从未写入，首次请求必然外呼。</summary>
        private static DateTime CachedIpExpiresUtc = DateTime.MinValue;

        // ===== 内置城市坐标 =====

        /// <summary>
        /// 常用城市的经纬度表，键同时收录中文名与拼音/英文名，比较忽略大小写。
        /// 命中时跳过地理编码请求：少一次网络往返，也让设置页的“测试”在网络受限时仍能查常用城市。
        /// 数组顺序固定为“纬度, 经度”，与 Open-Meteo 的参数顺序一致。
        /// </summary>
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

        // ===== 取数入口 =====

        /// <summary>
        /// 不带可选开关的简化版：等价于只取主天气，不查空气质量与公网 IP。
        /// 设置页的“测试”按钮走这里，只关心城市能不能查到、数据是否合理。
        /// </summary>
        public static void FetchAsync(string city, Action<WeatherInfo> callback)
        {
            FetchAsync(city, null, callback);
        }

        /// <summary>
        /// 异步取天气：立即返回，实际请求与回调都在线程池线程执行。
        /// <paramref name="city"/> 为空或全空白时按“北京”处理；<paramref name="options"/> 为 null 时按“都不查”处理。
        /// 回调必定被调用一次：成功给 <see cref="WeatherInfo"/>，任何失败给 null（包括回调自身抛异常，也会被吞掉）。
        /// </summary>
        /// <param name="city">城市名或拼音，允许带首尾空白。</param>
        /// <param name="options">是否附加空气质量 / 公网 IP；null 表示都不附加。</param>
        /// <param name="callback">后台线程回调；为 null 时直接返回，不发起请求。</param>
        public static void FetchAsync(string city, WeatherFetchOptions options, Action<WeatherInfo> callback)
        {
            // 没有回调就没有交付对象，连线程都不必开。
            if (callback == null)
            {
                return;
            }

            // 入口处就把城市收敛好，避免后台线程再去判断空值；默认北京与设置默认值一致。
            string requestedCity = string.IsNullOrWhiteSpace(city) ? "北京" : city.Trim();
            WeatherFetchOptions fetchOptions = options ?? new WeatherFetchOptions();

            // 整个取数 + 回调都丢到线程池：调用方多在 UI 线程，不能在这里同步等网络。
            ThreadPool.QueueUserWorkItem(delegate
            {
                WeatherInfo info = null;
                try
                {
                    // 网络、解析、结构不符等任何异常都只表示“这次没取到”，转成 null 交给回调方决定是否保留旧数据。
                    info = Fetch(requestedCity, fetchOptions);
                }
                catch (Exception)
                {
                    info = null;
                }

                try
                {
                    // 回调异常不能冒泡到线程池（会终止进程），因此在最外层再兜一次。
                    callback(info);
                }
                catch (Exception)
                {
                }
            });
        }

        // ===== 主取数流程 =====

        /// <summary>
        /// 同步取一次天气，仅供线程池任务调用。步骤：配置 TLS → 定坐标 → 请求并解析预报 → 填当前实况与预报列表
        /// → 按需补空气质量与 IP。返回 null 表示这次失败（城市查不到、网络失败或响应缺关键字段）。
        /// </summary>
        private static WeatherInfo Fetch(string city, WeatherFetchOptions options)
        {
            // 老系统上 .NET 默认协议可能不含 TLS 1.2，Open-Meteo 只接受 1.2+，必须先补上。
            ConfigureTls();

            double latitude;
            double longitude;
            string displayCity = city;

            double[] known;
            // 先查内置表：命中就省掉一次地理编码请求，也避免同名城市被解析到别处。
            if (KnownCities.TryGetValue(city, out known))
            {
                latitude = known[0];
                longitude = known[1];
            }
            else
            {
                // 内置表没有才走地理编码；查不到结果就整体判失败，不拿默认坐标硬查。
                GeocodingResult result = Geocode(city);
                if (result == null)
                {
                    return null;
                }

                latitude = result.Latitude;
                longitude = result.Longitude;

                // 接口返回的标准名（可能是繁体或英文）更适合显示，但空值仍保留用户输入。
                if (!string.IsNullOrWhiteSpace(result.Name))
                {
                    displayCity = result.Name;
                }
            }

            // 坐标按最大 4 位小数格式化：约 10 米精度，足够天气网格使用，也避免科学计数法。
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

            // 当前温度是整条数据的底线：缺它就无法显示天气，直接判失败；天气代码缺失只降级为 -1。
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

            // 湿度缺失时保持 null（界面不显示），有值时四舍五入到整数百分比。
            info.Humidity = forecast.Current.Humidity.HasValue
                ? (int?)Math.Round(forecast.Current.Humidity.Value, MidpointRounding.AwayFromZero)
                : null;
            info.WindDirection = DescribeWindDirection(forecast.Current.WindDirection);
            info.UpdatedAt = DateTime.UtcNow;

            // 预报三个数组必须同时存在才处理；任一缺失就跳过整个预报段，只留实况。
            if (forecast.Daily != null
                && forecast.Daily.Min != null
                && forecast.Daily.Max != null)
            {
                // 取三个数组长度的最小值，避免接口少给一天时越界；天气代码数组可选，另行判断。
                int count = Math.Min(forecast.Daily.Min.Length, forecast.Daily.Max.Length);
                if (forecast.Daily.WeatherCode != null)
                {
                    count = Math.Min(count, forecast.Daily.WeatherCode.Length);
                }

                // 最多取 6 天，与请求参数 forecast_days=6 对齐。
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

                    // 日期数组可选：缺失时保持 null，不影响温度与图标显示。
                    if (forecast.Daily.Time != null && i < forecast.Daily.Time.Length)
                    {
                        day.Date = forecast.Daily.Time[i];
                    }

                    info.Days.Add(day);
                }
            }

            // 今日高低温直接取预报第一项，供“当前温度 + 今日区间”这种紧凑文案使用。
            if (info.Days.Count > 0)
            {
                info.Min = info.Days[0].Min;
                info.Max = info.Days[0].Max;
            }

            // 两个附加数据都是可选增强：即使失败也不能让整次取数变成 null。
            if (options.IncludeAirQuality)
            {
                ApplyAirQuality(info, latitude, longitude);
            }

            // 公网 IP 有 6 小时缓存，实际很少真正外呼。
            if (options.IncludePublicIp)
            {
                info.PublicIp = GetPublicIp();
            }

            return info;
        }

        /// <summary>
        /// 补查空气质量并写回 <see cref="WeatherInfo.AqiValue"/> 与 <see cref="WeatherInfo.AqiLevel"/>。
        /// 这是“锦上添花”的数据：接口失败或字段缺失就保持 null，绝不让主天气作废。
        /// </summary>
        private static void ApplyAirQuality(WeatherInfo info, double latitude, double longitude)
        {
            try
            {
                // 只用 us_aqi 一个指标，时区 auto 与主接口保持一致。
                string url = string.Format(
                    CultureInfo.InvariantCulture,
                    AirQualityEndpoint +
                    "?latitude={0:0.####}&longitude={1:0.####}&current=us_aqi&timezone=auto",
                    latitude,
                    longitude);
                AqiResponse response = ParseJson<AqiResponse>(DownloadString(url));

                // 三重判空：响应、current 节点、us_aqi 字段任一缺失都视为“没有空气质量”。
                if (response != null && response.Current != null && response.Current.UsAqi.HasValue)
                {
                    info.AqiValue = (int)Math.Round(response.Current.UsAqi.Value, MidpointRounding.AwayFromZero);
                    // 等级由数值换算，保证界面文案与数值永远一致。
                    info.AqiLevel = DescribeAqi(info.AqiValue.Value);
                }
            }
            catch (Exception)
            {
                // 附加数据失败不改变主结果，静默保留 null 即可。
            }
        }

        // ===== 公网 IP =====

        /// <summary>
        /// 取公网 IP，带 6 小时进程内缓存。缓存读写在锁内、网络请求在锁外，
        /// 避免多个并发刷新时串行等网络；失败返回空串（不是 null），调用方可直接拼字符串。
        /// </summary>
        private static string GetPublicIp()
        {
            lock (IpLock)
            {
                // 未过期就直接用缓存，正常情况下 6 小时内只有第一次真正外呼。
                if (!string.IsNullOrEmpty(CachedIp) && DateTime.UtcNow < CachedIpExpiresUtc)
                {
                    return CachedIp;
                }
            }

            string ip = null;
            try
            {
                // ipify 的 JSON 只有一个 ip 字段，解析成本很低；失败交给外层 catch。
                IpResponse response = ParseJson<IpResponse>(DownloadString("https://api.ipify.org?format=json"));
                if (response != null && !string.IsNullOrWhiteSpace(response.Ip))
                {
                    ip = response.Ip.Trim();
                }
            }
            catch (Exception)
            {
                // IP 只用于格式变量，取不到就显示空，不影响天气本身。
            }

            // 只有真的拿到值才写缓存；失败不缓存，下次刷新会立刻重试。
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

        // ===== 描述转换 =====

        /// <summary>
        /// 把风向角度换成中文方位：把 360 度八等分，每份 45 度，四舍五入后取模。
        /// 先做 <c>((x % 360) + 360) % 360</c> 归一，负角度或超过一圈也能得到正确结果；缺失/NaN 返回空串。
        /// </summary>
        private static string DescribeWindDirection(double? degrees)
        {
            // 接口可能不给风向，或给出 NaN；两种都没有可显示的方向。
            if (!degrees.HasValue || double.IsNaN(degrees.Value))
            {
                return string.Empty;
            }

            // 顺序从北开始顺时针排列，索引 i 对应 [i*45-22.5, i*45+22.5) 的角度区间。
            string[] names = new string[] { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
            double value = ((degrees.Value % 360) + 360) % 360;
            // 四舍五入到最近的 45 度扇区；取模 8 处理 337.5 度以上回落到“北”。
            int index = (int)Math.Round(value / 45.0, MidpointRounding.AwayFromZero) % 8;
            return names[index] + "风";
        }

        /// <summary>
        /// 把美国 AQI 数值换成中文等级，阈值按常见六级划分：
        /// 0–50 优、51–100 良、101–150 轻度、151–200 中度、201–300 重度、300 以上严重。
        /// </summary>
        private static string DescribeAqi(int value)
        {
            // 从小到大逐段判断，命中即返回；顺序不能乱，否则后面的区间永远不可达。
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

        /// <summary>
        /// 把 WMO 天气代码映射成单字符图标，供时间格式里的天气变量使用。
        /// 图标按大类归并（晴/多云/阴/雨/雪/雷），未知代码回落到云朵，保证界面不出现空位。
        /// </summary>
        public static string IconForCode(int code)
        {
            switch (code)
            {
                // 0 晴、1 大部晴朗共用太阳。
                case 0:
                case 1:
                    return "☀";
                // 2 多云用太阳夹云。
                case 2:
                    return "⛅";
                // 3 阴与 45/48 雾都归为云朵，视觉上不区分。
                case 3:
                    return "☁";
                case 45:
                case 48:
                    return "☁";
                // 毛毛雨、冻雨、阵雨全部归入雨伞一组。
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
                // 各类降雪（含米雪、阵雪）统一用雪花。
                case 71:
                case 73:
                case 75:
                case 77:
                case 85:
                case 86:
                    return "❄";
                // 雷雨、雷暴伴冰雹统一用闪电。
                case 95:
                case 96:
                case 99:
                    return "⚡";
                default:
                    // 未知代码不显示空白，退回云朵，避免格式里出现突兀的空格。
                    return "☁";
            }
        }

        // ===== 网络与解析 =====

        /// <summary>
        /// 城市名换坐标：调用地理编码接口，只取第一条结果。
        /// 用 <see cref="Uri.EscapeDataString"/> 处理中文与空格；查不到、响应为空或结果列表为空时返回 null。
        /// </summary>
        private static GeocodingResult Geocode(string city)
        {
            // count=1 只要最匹配的一条；language=zh 让返回名尽量是中文，便于直接显示。
            string url = string.Format(
                CultureInfo.InvariantCulture,
                GeocodingEndpoint + "?name={0}&count=1&language=zh&format=json",
                Uri.EscapeDataString(city));

            string json = DownloadString(url);
            GeocodingResponse response = ParseJson<GeocodingResponse>(json);

            // 三重判空：无响应、无 results 节点、结果为空都表示城市没查到。
            if (response == null || response.Results == null || response.Results.Count == 0)
            {
                return null;
            }

            return response.Results[0];
        }

        /// <summary>
        /// 同步下载一个文本响应。显式设置 UA 与 Accept（部分 CDN 会拒绝空 UA），
        /// 连接与读写超时都设 10 秒，避免弱网下后台线程长时间挂着；HTTP 错误由调用方的 catch 统一处理。
        /// </summary>
        private static string DownloadString(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            // 固定 UA：便于服务端识别来源，也避免被默认 UA 规则拦截。
            request.UserAgent = "GameClock/1.0 (weather overlay)";
            request.Accept = "application/json";
            request.Timeout = 10000;
            // 读写超时单独设置：连接建立后服务端迟迟不返回时也要能放弃。
            request.ReadWriteTimeout = 10000;

            // 三层 using 保证响应、流、读取器按序释放，异常时也不泄漏连接。
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// 用 <see cref="DataContractJsonSerializer"/> 把 JSON 反序列化成指定类型。
        /// 空串直接返回 <c>default(T)</c>，避免反序列化器抛异常；
        /// 结构不符或 JSON 损坏时让异常冒泡，由 <see cref="FetchAsync"/> 统一转成 null 结果。
        /// </summary>
        private static T ParseJson<T>(string json)
        {
            // 空响应视为没有数据，返回 null（引用类型）或默认值，调用方已有判空分支。
            if (string.IsNullOrWhiteSpace(json))
            {
                return default(T);
            }

            // 用内存流喂给序列化器；using 保证流被释放。
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(stream);
            }
        }

        /// <summary>
        /// 确保进程启用 TLS 1.2：部分 Windows 组合下 .NET 的默认协议仍停留在 TLS 1.0/1.1，
        /// 而 Open-Meteo 与 ipify 只接受 1.2+，不补这一位会直接握手失败。
        /// 用 <c>|=</c> 只增不减，保留调用方或系统已启用的其它协议。
        /// </summary>
        private static void ConfigureTls()
        {
            try
            {
                // 按位或：在系统默认值上追加 Tls12，而不是整体覆盖。
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (NotSupportedException)
            {
                // 极老系统或受限环境可能不识别 Tls12 枚举，跳过即可；请求失败会按 null 结果处理。
            }
        }

        /// <summary>
        /// 把 WMO 天气代码翻译成中文描述，覆盖 Open-Meteo 文档里的全部代码；
        /// 未收录的值返回泛称“天气”，保证界面永远有字可显示。
        /// </summary>
        private static string DescribeWeather(int code)
        {
            switch (code)
            {
                // 晴到阴：0–3 是四个基础状态。
                case 0: return "晴";
                case 1: return "大部晴朗";
                case 2: return "多云";
                case 3: return "阴";
                // 45/48 为雾与冻雾，中文都叫“有雾”。
                case 45:
                case 48: return "有雾";
                // 51–57 为不同强度的毛毛雨（含冻毛毛雨）。
                case 51: return "小毛毛雨";
                case 53: return "毛毛雨";
                case 55: return "强毛毛雨";
                case 56:
                case 57: return "冻毛毛雨";
                // 61–67 为雨（含冻雨），按强度区分。
                case 61: return "小雨";
                case 63: return "中雨";
                case 65: return "大雨";
                case 66:
                case 67: return "冻雨";
                // 71–77 为雪（含米雪）。
                case 71: return "小雪";
                case 73: return "中雪";
                case 75: return "大雪";
                case 77: return "米雪";
                // 80–82 为阵雨，85/86 为阵雪。
                case 80: return "小阵雨";
                case 81: return "阵雨";
                case 82: return "强阵雨";
                case 85: return "小阵雪";
                case 86: return "强阵雪";
                // 95–99 为雷暴，96/99 还伴冰雹。
                case 95: return "雷雨";
                case 96:
                case 99: return "雷暴伴冰雹";
                default: return "天气";
            }
        }

        // ===== 响应模型（仅供 JSON 反序列化） =====

        /// <summary>地理编码响应外层：只有一个 <c>results</c> 数组，可能缺失。</summary>
        [DataContract]
        private sealed class GeocodingResponse
        {

            /// <summary>匹配到的城市列表；接口无结果时该节点不存在，反序列化后为 null。</summary>
            [DataMember(Name = "results")]
            public List<GeocodingResult> Results { get; set; }
        }

        /// <summary>地理编码的单条结果：名称 + 经纬度，另有省/国家字段备用但当前未展示。</summary>
        [DataContract]
        private sealed class GeocodingResult
        {

            /// <summary>标准地名，用于覆盖用户输入里的别名或错别字。</summary>
            [DataMember(Name = "name")]
            public string Name { get; set; }

            /// <summary>纬度；接口缺失时保持 0，但上层已判过结果非空。</summary>
            [DataMember(Name = "latitude")]
            public double Latitude { get; set; }

            /// <summary>经度。</summary>
            [DataMember(Name = "longitude")]
            public double Longitude { get; set; }

            /// <summary>一级行政区（省/州），当前不显示，保留以便将来做“同名城市”区分。</summary>
            [DataMember(Name = "admin1")]
            public string Admin1 { get; set; }

            /// <summary>国家名，当前不显示。</summary>
            [DataMember(Name = "country")]
            public string Country { get; set; }
        }

        /// <summary>预报响应外层：当前实况与每日预报两个节点。</summary>
        [DataContract]
        private sealed class ForecastResponse
        {

            /// <summary>当前实况；缺失时整次取数判失败。</summary>
            [DataMember(Name = "current")]
            public CurrentWeather Current { get; set; }

            /// <summary>每日预报；缺失时只保留实况，不生成预报列表。</summary>
            [DataMember(Name = "daily")]
            public DailyWeather Daily { get; set; }
        }

        /// <summary>当前实况节点；除温度外都用可空类型，便于区分“0”与“接口没给”。</summary>
        [DataContract]
        private sealed class CurrentWeather
        {

            /// <summary>2 米气温（摄氏度）；是整条数据的必要字段。</summary>
            [DataMember(Name = "temperature_2m")]
            public double? Temperature { get; set; }

            /// <summary>WMO 天气代码；缺失时上层按 -1 处理并显示泛称。</summary>
            [DataMember(Name = "weather_code")]
            public int? WeatherCode { get; set; }

            /// <summary>2 米相对湿度（百分比）；接口以 double 返回，上层四舍五入取整。</summary>
            [DataMember(Name = "relative_humidity_2m")]
            public double? Humidity { get; set; }

            /// <summary>10 米风向（度，正北为 0 顺时针）；上层换算成八方位中文名。</summary>
            [DataMember(Name = "wind_direction_10m")]
            public double? WindDirection { get; set; }
        }

        /// <summary>
        /// 每日预报节点：四个等长数组，按天对齐；上层取三者长度的最小值再遍历，避免越界。
        /// </summary>
        [DataContract]
        private sealed class DailyWeather
        {

            /// <summary>日期数组（<c>yyyy-MM-dd</c>），可能比温度数组短。</summary>
            [DataMember(Name = "time")]
            public string[] Time { get; set; }

            /// <summary>每日天气代码数组，可为空。</summary>
            [DataMember(Name = "weather_code")]
            public int[] WeatherCode { get; set; }

            /// <summary>每日最高温数组，必填项之一（与最低温同时存在才处理预报）。</summary>
            [DataMember(Name = "temperature_2m_max")]
            public double[] Max { get; set; }

            /// <summary>每日最低温数组，必填项之一。</summary>
            [DataMember(Name = "temperature_2m_min")]
            public double[] Min { get; set; }
        }

        /// <summary>空气质量响应外层：只有一个 <c>current</c> 节点。</summary>
        [DataContract]
        private sealed class AqiResponse
        {

            /// <summary>当前空气质量；缺失表示这次没取到，保持 null。</summary>
            [DataMember(Name = "current")]
            public AqiCurrent Current { get; set; }
        }

        /// <summary>空气质量当前值：只取美国 AQI 一个指标。</summary>
        [DataContract]
        private sealed class AqiCurrent
        {

            /// <summary>美国 AQI 数值；为 double 是因为接口可能带小数，上层四舍五入取整。</summary>
            [DataMember(Name = "us_aqi")]
            public double? UsAqi { get; set; }
        }

        /// <summary>ipify 响应：单个 <c>ip</c> 字段。</summary>
        [DataContract]
        private sealed class IpResponse
        {

            /// <summary>公网 IP 文本；上层会 Trim，空值视为失败。</summary>
            [DataMember(Name = "ip")]
            public string Ip { get; set; }
        }
    }
}
