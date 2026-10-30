# taskbar — 任务栏时钟绘制组件

`GameClockTray.dll`：注入当前用户的 `explorer.exe`，子类化 `TrayClockWClass`，用 GDI 绘制两行时间/日期文本。

## 放什么

- `GameClockTray.cpp`：唯一的源文件，包含注入线程、窗口过程、绘制与看门狗。

## 工作方式

1. 加载后起线程查找 `Shell_TrayWnd` → `TrayNotifyWnd` → `TrayClockWClass`，`SetWindowLongPtrW` 子类化。
2. C# 端每秒发送 `WM_COPYDATA`（`dwData = 'GCT1'`，656 字节 `GCTCONFIG`），DLL 复制配置、启动 1 秒重绘计时器。
3. `WM_PAINT` 用系统原生字体绘制两行，文字颜色按系统主题取（深色白、浅色黑），宽度不足时先压缩字距、仍不足再缩小字号。
4. 15 秒收不到配置、收到 `WM_APP+1`（卸载）或窗口销毁时恢复原 WndProc 与原生绘制。

## 边界

- 不调整窗口宽度、不隐藏/移动系统时钟——扩展或移出会破坏任务栏布局并遮挡托盘图标。
- 配置结构必须与 `../../src/platform/TrayClockInjector.cs` 的 `GCTCONFIG` 保持一致（656 字节、pack 4）。
- 只对同一用户的 64 位 `explorer.exe` 生效；不提升权限、不跨会话。
