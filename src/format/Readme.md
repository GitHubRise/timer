# format — 时间格式与时区

把 `DateTime`、`WeatherInfo` 与用户格式串渲染成最终文本；同时提供时区目录与格式说明数据。以纯函数与静态目录为主，不持有窗口或设置状态。

## 放什么

- `TimeFormatter.cs`：.NET 日期格式、ISO 周数、12/24 小时制、内置格式预设。
- `FormatEngine.cs`：简洁变量解析、农历/干支/生肖、天气 token 内插、空行折叠。
- `FormatHelpCatalog.cs`：格式说明条目、描述与示例预览。
- `TimeZoneCatalog.cs`：系统时区列表、时区解析、当前时区时间与偏移标签、`GetSystemId()` 读取 Windows 当前时区（供设置页「同步系统时区」使用）。

## 不放什么

- 悬浮窗与设置窗口控件 → `../clock/`、`../settings/`
- 天气网络请求 → `../weather/`
- 提醒/日程触发规则 → `../reminders/`

## 边界

- 输入非法格式时返回 `string.Empty`，不抛异常；调用方负责回退显示。
- 天气参数允许为 `null`，此时天气类 token 渲染为空。
