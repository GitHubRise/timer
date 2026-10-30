# weather — 天气数据获取

通过 Open-Meteo 公共 API 获取地理编码、当前天气、逐日预报与空气质量，按需查询公网 IP。只负责取数与模型，不负责渲染与调度。

## 放什么

- `WeatherService.cs`：`FetchAsync` 异步抓取与 JSON 解析；`WeatherInfo`、`DailyForecast`、`WeatherFetchOptions`、`WeatherRefreshOptions`。

## 不放什么

- 天气文本拼接与格式 token → `../format/`
- 刷新定时器 → `../app/AppController.cs`
- 天气页签 UI → `../settings/SettingsWindow.Weather.cs`

## 边界

- 回调在后台线程执行，调用方自行切回 UI 线程；抓取失败仍回调，`WeatherInfo` 为 `null`。
- 端点与字段映射集中在本文件，不在其他模块拼 URL。
- 已知缺口：回调异常被空 `catch` 吞掉，排障时只能依赖上游日志。
