# platform — Windows 系统集成

封装与操作系统交互的底层能力，向上暴露语义化方法，P/Invoke 细节不外泄。

## 放什么

- `NativeMethods.cs`：窗口样式、置顶、鼠标穿透、分层窗口等 Win32 声明与包装。
- `AutoStartManager.cs`：开机自启注册表项（`HKCU` Run）的写入、删除与查询。

## 不放什么

- 窗口、控件、业务状态 → 其他模块
- 设置持久化 → `../settings/AppSettings.cs`
- 托盘图标 → `../app/AppController.cs`

## 边界

- 所有方法对无效句柄/缺失注册表项静默返回或返回 `false`，不向 UI 抛异常。
- 新增 P/Invoke 必须在本目录声明，其他模块不得直接 `DllImport`。
