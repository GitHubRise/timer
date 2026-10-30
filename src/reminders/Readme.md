# reminders — 提醒与日程

时间提醒与日程的数据模型、内置预设、触发窗口计算和提示音。触发由 `../clock/ClockWindow.cs` 的定时器驱动，到点后以事件上抛。

## 放什么

- `Reminder.cs`：提醒模型、工作/娱乐状态筛选、内置提醒预设、显示时长选项。
- `Schedule.cs`：生日/纪念日/会议/机票日程、提前量、年度重复、到点判定与去重键。
- `AlarmSound.cs`：提醒提示音。

## 不放什么

- 提醒行与日程列表 UI → `../settings/` 的各页签分部类
- 提醒气泡绘制 → `../clock/`
- 时间与时区格式化 → `../format/`

## 边界

- 不持有窗口或控制器引用，不直接保存设置。
- `ScheduleService.TryFindDueOccurrence` 只做纯计算，触发去重键由调用方写回 `ScheduleItem.LastTriggeredKey`。
