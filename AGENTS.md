# AGENTS.md — timer（GameClock）项目入口

本文件是 `sources/timer` 的项目入口：定位、规范登记、目录地图、构建命令与已知缺口。用户使用说明在根 `README.md`；架构细节在 `docs/300001-系统架构与模块契约.md`。

## 1. 项目定位

Windows 10/11 上的桌面时间悬浮窗：无边框透明置顶窗口 + 系统托盘，显示时间、天气、定时提醒与日程，支持鼠标穿透与开机自启。显示位置有两种，各自在独立页签里开关（可同时关闭）：桌面悬浮窗、任务栏时钟（覆盖系统任务栏时间区域）。两种位置各自保存显示格式；桌面悬浮窗另存字体、字号与颜色，任务栏时钟跟随系统任务栏外观。任务栏默认两行 `yyyy-mm-dd HH:ff:ss` / `MD ddd rtm`。单进程 WPF 应用（托盘用 WinForms），无服务端、无数据库，设置以 JSON 存于 `%APPDATA%\GameClock\settings.json`。

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
| `src/clock/` | 悬浮窗、任务栏时钟、今日面板、分层文字、提醒气泡 | 通过事件与控制器交互；任务栏窗只覆盖，不注入 explorer |
| `src/format/` | 时间格式、简洁变量、时区 | 纯函数，非法输入返回空串 |
| `src/platform/` | Win32 P/Invoke、开机自启 | 其他模块不得直接 `DllImport` |
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

脚本行为：生成 `src/generated/BuildInfo.g.cs`（版本、`yyyyMMdd.N` 编译号、编译时间）→ 自增 `build/build.number` → 用 `csc.exe` 递归编译 `src/` 下全部 `.cs` → 输出 `dist/GameClock.exe`、复制 `GameClock.exe.config` 与 `README.md` → 打包 `dist/GameClock-v0.0.2.zip` 并写 `dist/SHA256SUMS.txt`。

- 编译器：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，对应 C# 5 语法上限：不可用字符串内插、`?.`、`nameof`、表达式体成员等新语法。
- 项目没有 `.sln`/`.csproj`；在 IDE 中新增文件后，必须以 `build.ps1` 的实际编译结果为准。
- 运行：启动 `dist\GameClock.exe`；再次启动会唤醒已运行实例的设置窗口。

## 5. 开发约定

- 全部类型保持 `namespace GameClock`；新增文件放进对应内容域目录，不新建 `utils/`、`common/` 兜底目录。
- `SettingsWindow` 按页签拆为多个 `partial` 文件：主文件只放字段、构造与公共骨架，新增页签成员进对应分部文件。
- `SettingsWindow` 为无边框窗口，靠 `WindowChrome`（`ResizeBorderThickness=6`）提供四边缩放，`ResizeMode=CanResize`、最小 640×560；拖动由标题 `Grid` 的 `MouseLeftButtonDown → DragMove()` 实现，标题区域光标为 `Cursors.SizeAll`，不要只在标题文字上挂拖动，也不要把 `ResizeMode` 改回 `NoResize`。
- 新增设置项需同时修改 `AppSettings` 字段、`Normalize`/`Clone`、对应页签与持久化兼容路径；旧配置缺字段时按默认值处理。
- 显示位置由 `ShowDesktopOverlay` / `ShowTaskbarClock` 控制，两个开关分属「桌面悬浮窗」「任务栏时钟」页签、互相独立，可同时关闭（关闭后该位置不显示）；窗口的创建、销毁与天气同步统一走 `AppController.ApplyDisplaySurfaces`，不要在设置窗口里直接开关窗口。
- 任务栏时钟外观**跟随系统任务栏**：字体取原生 `TrayClockWClass` 的 `WM_GETFONT`，文字颜色按采样背景亮度选择，背景从任务栏实际像素采样；字号 = 原生字号 × `TaskbarFontScale`/100（默认 124，界面提供 100/112/124/136），`TaskbarBold` 控制加粗；宽度不够时用 Viewbox 横向收紧字距，不要按宽度一路缩小字号。`TaskbarFontFamilyName` / `TaskbarFontSize` / `TaskbarColorHex` 仅为旧配置兼容保留，最多作为字体读取失败时的兜底。格式仍用 `TaskbarFormatMode` / `TaskbarFormat` / `TaskbarDotNetFormat`，默认格式见 `FormatEngine.DefaultTaskbarTokenFormat`。
- 格式语言与常用/自定义格式按显示位置各存一份：悬浮窗用 `FormatMode` / `TimeFormat` / `TokenTimeFormat`，任务栏用 `TaskbarFormatMode` / `TaskbarDotNetFormat` / `TaskbarFormat`；全局时间格式页只放时区与变量说明，不写格式。`TaskbarFormat` 始终存简洁变量，`TaskbarDotNetFormat` 始终存 .NET 格式。
- 悬浮窗交互：拖动由 `_root.MouseLeftButtonDown → DragMove()` 负责（光标 `Cursors.SizeAll`）；滚轮只缩放字号（步长 4，Clamp 16–160），改 `FontSize` 后重绘并通过 `ScaleChanged` 通知控制器防抖保存，不得用滚轮改透明度或位置。
- 全屏隐藏由 `SHQueryUserNotificationState` 返回的 `QUNS_RUNNING_D3D_FULL_SCREEN`(3) / `QUNS_PRESENTATION_MODE`(4) 驱动，1 秒轮询；只 `Hide()`/`Show()` 窗口，不停止时钟、提醒与日程计时器；关闭设置后必须立即恢复显示。
- 时区同步统一走 `TimeZoneCatalog.GetSystemId()`（内部读 `TimeZoneInfo.Local`），设置窗口不得直接读 `TimeZoneInfo.Local`；「同步系统时区」只改编辑副本，点「应用」后落盘。
- 任务栏覆盖窗**不得移动、不得隐藏** `TrayClockWClass`：只按它的矩形绘制不透明覆盖窗，进程退出或异常崩溃后系统时钟自然恢复。覆盖窗用 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`、不透明背景；任务栏会在面板展开/重绘时把自己提到 TOPMOST 组前面，需每 250 ms `RaiseTopmost`，否则会露出原生时钟。
- 任务栏交互：悬停约 0.4 秒或左键单击展开 `TaskbarPanelWindow`，右键弹共用菜单；面板不抢焦点，单击后固定、点击面板外收起；面板数据只读 `AppSettings` 的副本，不直接改设置。
- 交付前至少执行一次 `build.ps1` 并确认 `dist\GameClock.exe` 生成；不在 `dist/` 内做手工修改。

## 6. 已知缺口

| 缺口 | 影响 | 处理方向 |
| --- | --- | --- |
| 无自动测试工程 | 格式解析、日程触发等纯逻辑只能手工验证 | 新增测试工程或独立校验脚本，优先覆盖 `format/`、`reminders/` |
| 无 `.sln`/`.csproj`、无 CI | 依赖本机 `csc.exe`，构建不可复现 | 迁移到 SDK 风格项目并加 CI |
| 无 LICENSE | 无法对外分发 | 需要公开时补许可证并评估第三方依赖 |
| 异常日志无 traceId，`Program.LogException` 等存在空 `catch` | 线上排障只能靠 `%APPDATA%\GameClock\error.log`，部分错误被吞 | 统一错误码与 traceId，空 `catch` 至少记录上下文 |
| 单一命名空间未按模块拆分 | 类型靠目录区分，跨模块引用无编译期约束 | 规模扩大后再按模块拆命名空间并集中 using |

## 7. 相关文档

- 用户说明与格式变量表：`README.md`
- 全库文档索引：`docs/000000-全库总索引.md`
- 架构与模块契约：`docs/300001-系统架构与模块契约.md`
- 工作区规范索引：`../../.policies/README.md`
