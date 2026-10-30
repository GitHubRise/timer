# AGENTS.md — timer（GameClock）项目入口

本文件是 `sources/timer` 的项目入口：定位、规范登记、目录地图、构建命令与已知缺口。用户使用说明在根 `README.md`；架构细节在 `docs/300001-系统架构与模块契约.md`。

## 1. 项目定位

Windows 10/11 上的桌面时间悬浮窗：无边框透明置顶窗口 + 系统托盘，显示时间、天气、定时提醒与日程，支持鼠标穿透与开机自启。显示位置有两种，各自在独立页签里开关（可同时关闭）：桌面悬浮窗、任务栏时钟（覆盖系统任务栏时间区域）。两种位置各自保存字体、字号、颜色与显示格式；任务栏默认两行 `yyyy-mm-dd HH:ff:ss` / `MD ddd rtm`。单进程 WPF 应用（托盘用 WinForms），无服务端、无数据库，设置以 JSON 存于 `%APPDATA%\GameClock\settings.json`。

## 2. 采用的规范

代码骨架按 `../general-policies/general/code-skeleton.md` 执行；内容根为 `src/`；技术栈为 C# / .NET Framework 4.8 / WPF + WinForms；目录形状服从 `../general-policies/general/skeleton.md`。

| 规范 | 采用方式 |
| --- | --- |
| `code-skeleton` | 采用：`src/` 按内容域分模块，禁止单文件包打天下；模块契约写入 `docs/` |
| `skeleton` | 采用：每个目录一份 `Readme.md`；单目录 15 文件阈值；一级目录单一轴 |
| `docs-numbering` | 采用：`docs/` 六位编号文档（L1） |
| `git-workflow` | 采用：GitHub 私有仓 `GitHubRise/timer`（`origin`），`main` 直接提交并推送；Conventional Commits 中文 subject |
| `memory` / `temp` | 未在本项目启用；临时脚本放工作区 `temp/` |

## 3. 目录地图

| 路径 | 内容 | 不变量 |
| --- | --- | --- |
| `src/app/` | 进程入口、控制器、托盘与共用菜单 | 只做编排，不画窗口、不读写注册表 |
| `src/clock/` | 悬浮窗、任务栏时钟、分层文字、提醒气泡 | 通过事件与控制器交互；任务栏窗只覆盖，不注入 explorer |
| `src/format/` | 时间格式、简洁变量、时区 | 纯函数，非法输入返回空串 |
| `src/platform/` | Win32 P/Invoke、开机自启、全局日志 `AppLog` | 其他模块不得直接 `DllImport` |
| `src/reminders/` | 提醒/日程模型与触发计算 | 不持有窗口引用 |
| `src/settings/` | `AppSettings`、持久化、设置窗口分部类 | 编辑副本，提交后由控制器应用 |
| `src/theme/` | 暗色主题、滚动条、字体、SVG 图标 | 不引用 `AppSettings` |
| `src/weather/` | Open-Meteo / ipify 取数 | 后台回调，失败回调 `null` |
| `src/generated/` | 构建生成的 `BuildInfo.g.cs` | 不手写、不入库 |
| `build/` | 清单、运行配置、编译号计数 | `build.number` 不入库 |
| `assets/` | 图标与 SVG | 被编译/代码路径引用，慎改名 |
| `tools/` | 辅助脚本 | 不参与主构建 |
| `docs/` | 编号文档与截图 | 规则见 `docs/Readme.md` |
| `dist/` | 构建输出（exe、zip、校验） | 不入库，可随时重建 |

## 4. 构建与运行

前置：Windows 10/11 x64、.NET Framework 4.8、PowerShell 5.1+。

```powershell
# 在项目根执行
powershell -ExecutionPolicy Bypass -File build.ps1
```

脚本行为：生成 `src/generated/BuildInfo.g.cs`（版本、`yyyyMMdd.N` 编译号、编译时间）→ 自增 `build/build.number` → 用 `csc.exe` 递归编译 `src/` 下全部 `.cs` → 输出 `dist/GameClock.exe`、复制 `GameClock.exe.config` 与 `README.md` → 打包 `dist/GameClock-v0.0.1.zip` 并写 `dist/SHA256SUMS.txt`。

- 编译器：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，对应 C# 5 语法上限：不可用字符串内插、`?.`、`nameof`、表达式体成员等新语法。
- 项目没有 `.sln`/`.csproj`；在 IDE 中新增文件后，必须以 `build.ps1` 的实际编译结果为准。
- 运行：启动 `dist\GameClock.exe`；再次启动会唤醒已运行实例的设置窗口。

## 5. 开发约定

- 全部类型保持 `namespace GameClock`；新增文件放进对应内容域目录，不新建 `utils/`、`common/` 兜底目录。
- `SettingsWindow` 按页签拆为多个 `partial` 文件：主文件只放字段、构造与公共骨架，新增页签成员进对应分部文件。
- `SettingsWindow` 为无边框窗口，靠 `WindowChrome`（`ResizeBorderThickness=6`）提供四边缩放，`ResizeMode=CanResize`、最小 640×560；拖动由标题 `Grid` 的 `MouseLeftButtonDown → DragMove()` 实现，标题区域光标为 `Cursors.SizeAll`，不要只在标题文字上挂拖动，也不要把 `ResizeMode` 改回 `NoResize`。
- 新增设置项需同时修改 `AppSettings` 字段、`Normalize`/`Clone`、对应页签与持久化兼容路径；旧配置缺字段时按默认值处理。
- 显示位置由 `ShowDesktopOverlay` / `ShowTaskbarClock` 控制，两个开关分属「桌面悬浮窗」「任务栏时钟」页签、互相独立，可同时关闭（关闭后该位置不显示）；窗口的创建、销毁与天气同步统一走 `AppController.ApplyDisplaySurfaces`，不要在设置窗口里直接开关窗口。
- 任务栏显示使用独立设置 `TaskbarFontFamilyName` / `TaskbarFontSize` / `TaskbarColorHex` / `TaskbarFormatMode` / `TaskbarFormat` / `TaskbarDotNetFormat`，不要复用悬浮窗的字体与格式字段；默认格式见 `FormatEngine.DefaultTaskbarTokenFormat`。
- 格式语言与常用/自定义格式按显示位置各存一份：悬浮窗用 `FormatMode` / `TimeFormat` / `TokenTimeFormat`，任务栏用 `TaskbarFormatMode` / `TaskbarDotNetFormat` / `TaskbarFormat`；全局时间格式页只放时区与变量说明，不写格式。`TaskbarFormat` 始终存简洁变量，`TaskbarDotNetFormat` 始终存 .NET 格式。
- 悬浮窗交互：拖动由 `_root.MouseLeftButtonDown → DragMove()` 负责（光标 `Cursors.SizeAll`）；滚轮只缩放字号（步长 4，Clamp 16–160），改 `FontSize` 后重绘并通过 `ScaleChanged` 通知控制器防抖保存，不得用滚轮改透明度或位置。
- 全屏隐藏由 `SHQueryUserNotificationState` 返回的 `QUNS_RUNNING_D3D_FULL_SCREEN`(3) / `QUNS_PRESENTATION_MODE`(4) 驱动，1 秒轮询；只 `Hide()`/`Show()` 窗口，不停止时钟、提醒与日程计时器；关闭设置后必须立即恢复显示。
- 时区同步统一走 `TimeZoneCatalog.GetSystemId()`（内部读 `TimeZoneInfo.Local`），设置窗口不得直接读 `TimeZoneInfo.Local`；「同步系统时区」只改编辑副本，点「应用」后落盘。
- 任务栏覆盖窗**不得隐藏** `TrayClockWClass`（会触发任务栏重排、托盘图标移位）；应把系统时钟移出任务栏保留占位，退出时再移回。`TrayClockWClass` 是任务栏子窗口，`SetWindowPos` 坐标相对父窗口，恢复位置前需用父窗口矩形换算。
- 交付前至少执行一次 `build.ps1` 并确认 `dist\GameClock.exe` 生成；不在 `dist/` 内做手工修改。
- 设置界面的灰色说明文字统一由 `src/settings/SettingsUiHints.cs` 的 `SettingsUiHints.ShowHints` 控制（默认 `false`，即关闭）；新增提示必须用 `SettingsUiHints.Hint(...)` 创建，状态行改色用 `SettingsUiHints.SetStatusColor(...)`，不要直接 `UiTheme.MakeText(... TextMuted ...)`。

### 注释与日志（中文）

- **注释用中文**：每个 `.cs` 文件顶部写 4–6 行 `//` 文件头，说明该文件职责与不可违反的边界；每个类型上方 `/// <summary>` 一句职责；非自明的 public/internal 方法写 `///` 摘要（参数/返回值语义不显然时补 `<param>`/`<returns>`）；方法体内只解释「为什么这么做、边界值含义、外部 API 的坑」，不逐行复述代码，魔法数字标注单位与来源。
- **注释密度**：按注释行 / 代码行计，目标 **≥25%**；任何 300 行以上的文件不少于 30 条注释；**每个方法（含 private 方法与事件处理器）至少 1 条注释**，每个 `if` / `switch` / `try` / `catch` / 循环块前说明意图；改代码必须同步改注释，注释与实现不一致按缺陷处理。
- **日志统一入口**：只通过 `src/platform/AppLog.cs` 的 `Info/Warn/Error(module, message[, exception])` 记录，写 `%APPDATA%\GameClock\app.log`（单文件 1 MB、滚动保留 3 份），`Error` 额外写 `error.log`。`module` 用目录/职责短名（`Program`、`Settings`、`Weather`、`Taskbar`、`Clock`、`Reminder`…）。
- **日志密度**：只在状态跃迁、外部副作用（写配置、注册表、网络请求、移动系统窗口）、失败回退、用户可见决策处各记 1 条；一次用户操作链路 2–4 条（开始 → 结果/失败），上层已记则下层不重复。**每秒 tick、每次重绘、每次滚动或鼠标移动不得记日志**，否则日志会淹没真正线索。
- **日志内容**：消息用中文，必须带模块名与关键参数（城市、格式串、字号、位置等）；失败必须带异常；**禁止空 `catch`**，至少 `AppLog.Error` 加一句原因注释（日志自身写失败除外）。

## 6. 已知缺口

| 缺口 | 影响 | 处理方向 |
| --- | --- | --- |
| 无自动测试工程 | 格式解析、日程触发等纯逻辑只能手工验证 | 新增测试工程或独立校验脚本，优先覆盖 `format/`、`reminders/` |
| 无 `.sln`/`.csproj`、无 CI | 依赖本机 `csc.exe`，构建不可复现 | 迁移到 SDK 风格项目并加 CI |
| 无 LICENSE | 无法对外分发 | 需要公开时补许可证并评估第三方依赖 |
| 日志无 traceId，同一次操作的多条记录只能按时间近似关联 | 排障时无法精确串联「设置应用 → 保存 → 重建窗口」链路 | 需要时给 `AppLog` 加 traceId 并透传到各模块 |
| 单一命名空间未按模块拆分 | 类型靠目录区分，跨模块引用无编译期约束 | 规模扩大后再按模块拆命名空间并集中 using |

## 7. 相关文档

- 用户说明与格式变量表：`README.md`
- 全库文档索引：`docs/000000-全库总索引.md`
- 架构与模块契约：`docs/300001-系统架构与模块契约.md`
- 工作区规范索引：`../../.policies/README.md`
