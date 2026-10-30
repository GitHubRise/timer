// 设置窗口「天气」页签分部（partial SettingsWindow）：天气开关、字体/字号/颜色/阴影、城市、更新频率与天气行格式。
// 调用方：SettingsWindow.Shell 构建页签与接线；「测试地名」直接调 WeatherService.FetchAsync 做一次性验证。
// 边界：本页只写 AppSettings 的 Weather* 字段；取数、重试与定时刷新由 weather/ 模块和控制器负责。
// 不变量：FetchAsync 回调在网络线程触发，UI 更新必须 Dispatcher.BeginInvoke 回来；查询失败回调 null 而不抛异常。
// 已知坑：城市输入框 TextChanged 逐字触发，不在输入时记日志，只在测试与开关切换处记录。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace GameClock
{
    // 本分部文件：天气页签的控件构造、颜色选择与地名测试；应用/保存链路在 SettingsWindow.Shell.cs。
    internal sealed partial class SettingsWindow
    {
        /// <summary>
        /// 构造「天气」页签根面板：天气开关与字体/字号/颜色/阴影 → 更新频率 → 城市与测试 → 天气行格式 → 说明。
        /// </summary>
        private UIElement BuildWeatherTab()
        {
            // 所有控件都直接写 _working.Weather*；天气数据本身由控制器定时用 WeatherService 拉取后下发给悬浮窗。
            StackPanel panel = new StackPanel();

            // 卡片一：天气主设置，控件顺序 = 用户调参顺序（开关 → 外观 → 刷新 → 城市 → 格式）。
            Border weatherCard = UiTheme.MakeCard();
            StackPanel content = new StackPanel();
            content.Children.Add(UiTheme.MakeSectionTitle("天气预报"));
            // 关闭天气只隐藏天气行，不影响时间显示，也不停止后台刷新计时器。
            content.Children.Add(_weatherEnabledCheck);

            // 天气字体与时间字体分开配置：天气行常要换成更细的字体才不压住时间。
            TextBlock fontLabel = UiTheme.MakeText("天气字体", 13, UiTheme.TextPrimary, FontWeights.Normal);
            fontLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(fontLabel);
            content.Children.Add(_weatherFontCombo);

            StackPanel styleRow = new StackPanel();
            // 粗体/斜体两个勾选横向并排，间距交给各自控件的 Margin，行本身不再设 Margin。
            styleRow.Orientation = Orientation.Horizontal;
            styleRow.Margin = new Thickness(0, 4, 0, 0);
            styleRow.Children.Add(_weatherBoldCheck);
            styleRow.Children.Add(_weatherItalicCheck);
            content.Children.Add(styleRow);

            // 字号滑块复用通用块（标签 + 滑块 + 数值）；9–60 px 与 AppSettings.Normalize 的钳制范围一致。
            content.Children.Add(BuildSliderBlock(
                "天气字号",
                _weatherSizeSlider,
                _weatherSizeValueText,
                "天气行单独使用的字号，9 到 60 像素",
                new Thickness(0, 12, 0, 2)));
            content.Children.Add(BuildWeatherColorRow());
            content.Children.Add(BuildWeatherPalette());
            // 阴影单独一个勾选，放在外观组末尾：它只影响可读性，不影响任何取数行为。
            content.Children.Add(_weatherShadowCheck);

            // 刷新频率下拉存的是分钟数（WeatherRefreshMinutes），显示文案与数值解耦。
            TextBlock refreshLabel = UiTheme.MakeText("自动更新频率", 13, UiTheme.TextPrimary, FontWeights.Normal);
            refreshLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(refreshLabel);
            content.Children.Add(_weatherRefreshCombo);

            TextBlock cityLabel = UiTheme.MakeText("城市", 13, UiTheme.TextPrimary, FontWeights.Normal);
            cityLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(cityLabel);

            Grid cityRow = new Grid();
            // 城市行：输入框吃满剩余宽度，「测试」按钮固定在最右列。
            cityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cityRow.Children.Add(_weatherCityBox);
            cityRow.Children.Add(_weatherTestButton);
            Grid.SetColumn(_weatherTestButton, 1);
            content.Children.Add(cityRow);

            // 测试结果就地显示在输入框下方，不弹窗、不阻塞改其它设置。
            content.Children.Add(_weatherTestStatus);

            // 提示写明可中英文、以及「保存或应用后才刷新」，避免测试成功却看不到变化。
            TextBlock cityHint = SettingsUiHints.Hint(
                "支持中文或英文城市名，例如：北京、上海、Tokyo。保存或应用后会立即刷新。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            cityHint.Margin = new Thickness(0, 6, 0, 0);
            cityHint.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(cityHint);

            // 天气格式留空即回落到默认格式，非法写法由 CommitAll 统一拒绝并给出原因。
            TextBlock formatLabel = UiTheme.MakeText("天气格式（可选）", 13, UiTheme.TextPrimary, FontWeights.Normal);
            formatLabel.Margin = new Thickness(0, 14, 0, 6);
            content.Children.Add(formatLabel);
            content.Children.Add(_weatherFormatBox);
            content.Children.Add(_weatherFormatStatus);

            // 变量表写死在说明里：与 WeatherFormatEngine 支持的 token 一一对应，改引擎必须同步改这段。
            TextBlock formatHelp = SettingsUiHints.Hint(
                "可用变量：pos 城市  we1~we6 6日天气  tl1~tl6 低温  th1~th6 高温\n" +
                "rtm 实时温度  rhu 湿度  rwd 风向  icn 天气图标  aqi 空气质量  ip 公网 IP\n\n" +
                "示例：pos · we1 · rtm\n" +
                "示例：icn rtm · rhu · rwd\n" +
                "示例：pos · we1 · rtm · 今日 tl1~th1\n" +
                "留空时使用默认格式：城市 · 天气 · 温度 · 今日高低温。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            formatHelp.Margin = new Thickness(0, 8, 0, 0);
            formatHelp.TextWrapping = TextWrapping.Wrap;
            formatHelp.LineHeight = 18;
            content.Children.Add(formatHelp);

            // 说明数据来源与「网络不可用会隐藏天气行」，减少“天气怎么不显示”的困惑。
            TextBlock apiHint = SettingsUiHints.Hint(
                "天气数据来自 Open-Meteo 公共 API，按设置的频率自动更新（默认 30 分钟）；无需 API Key。网络不可用时会自动隐藏天气行。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            apiHint.Margin = new Thickness(0, 10, 0, 0);
            apiHint.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(apiHint);

            weatherCard.Child = content;
            panel.Children.Add(weatherCard);

            // 卡片二：纯只读说明，不承载任何可编辑控件，因此不参与 MarkDirty。
            Border noteCard = UiTheme.MakeCard();
            StackPanel note = new StackPanel();
            note.Children.Add(UiTheme.MakeSectionTitle("显示说明"));
            // 首行即效果示例：先让用户看到成品，再去调上面的外观参数。
            TextBlock noteText = SettingsUiHints.Hint(
                "天气会显示在时间下方，例如：\n北京 · 晴 · 11°C · 今日 5°~16°\n\n" +
                "天气的字体、字号、颜色和阴影都可以独立于时间设置。\n" +
                "只发送城市名称和坐标到公共天气服务，不包含任何个人账号信息。",
                11.5,
                UiTheme.TextSecondary,
                FontWeights.Normal);
            noteText.LineHeight = 19;
            note.Children.Add(noteText);
            noteCard.Child = note;
            panel.Children.Add(noteCard);

            // 根面板只有两张卡片；纵向滚动由 Shell 的外层 ScrollViewer 负责，这里不再嵌套滚动区。
            return panel;
        }

        /// <summary>
        /// 构建「天气颜色」行：左侧色块与十六进制文本只做展示，右侧按钮打开系统取色对话框。
        /// </summary>
        private UIElement BuildWeatherColorRow()
        {
            Grid row = new Grid();
            // 与外观页的颜色行同构（左展示、右取色），保证两个页签的操作手感一致。
            row.Margin = new Thickness(0, 12, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧：标签 + 色块 + 十六进制文本，三者共用 VerticalAlignment.Center 保证与右侧按钮基线对齐。
            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            TextBlock label = UiTheme.MakeText("天气颜色", 13, UiTheme.TextPrimary, FontWeights.Normal);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(label);
            left.Children.Add(_weatherColorSwatch);
            left.Children.Add(_weatherColorHexText);
            row.Children.Add(left);
            Grid.SetColumn(left, 0);

            // 右侧：系统取色对话框入口，与外观页的 OnPickColor 是同一套 WinForms owner 处理方式。
            Button pick = new Button();
            pick.Content = "选择…";
            pick.Style = UiTheme.SecondaryButtonStyle;
            pick.Padding = new Thickness(13, 6, 13, 6);
            pick.Height = 30;
            pick.Click += OnPickWeatherColor;
            AutomationProperties.SetAutomationId(pick, "PickWeatherColorButton");
            row.Children.Add(pick);
            Grid.SetColumn(pick, 1);

            return row;
        }

        /// <summary>
        /// 构建天气颜色的圆形取色盘（9 个固定色）。Border 没有 Click 事件，只能在 MouseLeftButtonDown 上挂回调。
        /// </summary>
        private UIElement BuildWeatherPalette()
        {
            WrapPanel palette = new WrapPanel();
            // 取色盘紧跟在颜色行下方，同属「天气行外观」这一组设置。
            palette.Margin = new Thickness(0, 10, 0, 2);
            // 颜色取常用高对比度色，第一个红色即默认值（AppSettings 里 WeatherColorHex 默认 #FF3B30）。
            Color[] presetColors = new Color[]
            {
                Color.FromRgb(0xFF, 0x3B, 0x30),
                Color.FromRgb(0xFF, 0x95, 0x00),
                Color.FromRgb(0xFF, 0xD6, 0x0A),
                Color.FromRgb(0x34, 0xC7, 0x59),
                Color.FromRgb(0x00, 0xC7, 0xBE),
                Color.FromRgb(0x0A, 0x84, 0xFF),
                Color.FromRgb(0xBF, 0x5A, 0xF2),
                Color.FromRgb(0x00, 0x00, 0x00),
                Color.FromRgb(0xF2, 0xF2, 0xF7)
            };
            // 名称与 presetColors 按同一索引一一对应，改颜色数组时必须同步改这里，否则 ToolTip 会张冠李戴。
            string[] names = new string[]
            {
                "红色（默认）", "橙色", "黄色", "绿色", "青色", "蓝色", "紫色", "黑色", "白色"
            };

            // 逐个生成色块：24px 圆形 + 1px 边框，选中态粗细由 UpdateWeatherColorUi 回写。
            for (int i = 0; i < presetColors.Length; i++)
            {
                Color presetColor = presetColors[i];
                Border swatch = new Border();
                swatch.Width = 24;
                swatch.Height = 24;
                swatch.CornerRadius = new CornerRadius(12);
                swatch.Background = UiTheme.BrushFrom(presetColor);
                swatch.BorderThickness = new Thickness(1);
                // 默认 1px 灰边；被选中时 UpdateWeatherColorUi 改成 3px 主文字色，靠粗细区分而非换色。
                swatch.BorderBrush = UiTheme.Frozen(0x55, 0x55, 0x60);
                swatch.Margin = new Thickness(0, 0, 9, 0);
                swatch.Cursor = Cursors.Hand;
                // Border 没有按钮语义，只能靠手型光标表达「这里有东西可点」。
                swatch.ToolTip = names[i] + "  " + ColorUtil.ToHex(presetColor);
                // 闭包捕获循环变量：C# 5 的 foreach 有捕获陷阱，这里用方法内的局部副本 presetColor 规避。
                swatch.MouseLeftButtonDown += delegate
                {
                    SelectWeatherColor(presetColor);
                };
                palette.Children.Add(swatch);
                _weatherColorPresets.Add(new ColorPreset(presetColor, swatch));
            }
            return palette;
        }

        /// <summary>
        /// 天气总开关：关闭时同时禁用城市输入框，避免用户以为改了城市就会生效。
        /// </summary>
        private void OnWeatherChanged(object sender, RoutedEventArgs e)
        {
            // 回填 IsChecked 也会触发 Checked，_updating 期间忽略。
            if (_updating)
            {
                return;
            }
            _working.WeatherEnabled = _weatherEnabledCheck.IsChecked == true;
            // 开关只控制天气行是否绘制；取数计时器由控制器统一管理，关闭后仍按频率刷新，再开即时有数据。
            // 关闭天气时立刻禁用城市输入：让「现在改城市也没用」一眼可见。
            _weatherCityBox.IsEnabled = _working.WeatherEnabled;
            // 开关是用户可见决策，记一条；天气取数失败由 WeatherService 自己记，不在这里重复。
            AppLog.Info("Weather", "天气显示开关：" + (_working.WeatherEnabled ? "开启" : "关闭") + "。");
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 把配置里的刷新分钟数映射到下拉项；找不到时退回第 3 项（30 分钟，与 AppSettings 默认值一致）。
        /// </summary>
        private void SelectWeatherRefreshOption(int minutes)
        {
            WeatherRefreshOption match = null;
            // 线性查找即可，选项只有几项；必须精确匹配，否则自定义分钟数会被静默改写。
            for (int i = 0; i < _weatherRefreshOptions.Count; i++)
            {
                if (_weatherRefreshOptions[i].Minutes == minutes)
                {
                    match = _weatherRefreshOptions[i];
                    break;
                }
            }
            // 配置文件被手改成非预设值时的兜底，索引 2 是「30 分钟」。
            if (match == null)
            {
                match = _weatherRefreshOptions[2];
            }

            // 回填期间不允许触发 OnWeatherRefreshChanged，否则会把兜底值当用户选择写回。
            bool wasUpdating = _updating;
            _updating = true;
            try
            {
                _weatherRefreshCombo.SelectedItem = match;
            }
            finally
            {
                _updating = wasUpdating;
            }
        }

        /// <summary>
        /// 城市输入框：逐字写回 WeatherCity。真正的取数发生在「应用/保存」后，由控制器触发，因此这里不记日志。
        /// </summary>
        private void OnWeatherCityChanged(object sender, TextChangedEventArgs e)
        {
            // 每次按键都会触发，属于高频输入，不能在此记日志。
            if (_updating)
            {
                return;
            }
            _working.WeatherCity = (_weatherCityBox.Text ?? string.Empty).Trim();
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>天气字体选择：只影响天气行，与时间字体各自独立。</summary>
        private void OnWeatherFontChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            FontChoice choice = _weatherFontCombo.SelectedItem as FontChoice;
            // 选中项不是 FontChoice（例如被清空）时保留旧字体，避免把字体名写成空串。
            if (choice != null)
            {
                _working.WeatherFontFamilyName = choice.Name;
                UpdatePreview();
                MarkDirty();
            }
        }

        /// <summary>天气字号滑块，范围 9–60 像素；拖动期间连续触发，不记日志。</summary>
        private void OnWeatherSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherFontSize = e.NewValue;
            // 提示文本取整显示，字段保留 double，避免「9.5 显示成 10 但实际存 9.5」的观感不一致。
            _weatherSizeValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + " px";
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>天气阴影开关：透明悬浮窗在浅色壁纸上需要阴影才能看清文字。</summary>
        private void OnWeatherShadowChanged(object sender, RoutedEventArgs e)
        {
            // 回填 IsChecked 同样会触发事件，_updating 期间一律忽略。
            if (_updating)
            {
                return;
            }
            _working.WeatherShowShadow = _weatherShadowCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>天气粗体/斜体：两个复选框共用这一个处理器，一次性读两个 IsChecked 写回。</summary>
        private void OnWeatherFontStyleChanged(object sender, RoutedEventArgs e)
        {
            // 同一处理器服务粗体与斜体两个复选框：任一变化都重读两个值，简单且不会漏写。
            if (_updating)
            {
                return;
            }
            _working.WeatherFontBold = _weatherBoldCheck.IsChecked == true;
            _working.WeatherFontItalic = _weatherItalicCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>自动更新频率：写回分钟数，实际定时器由控制器按该值重建。</summary>
        private void OnWeatherRefreshChanged(object sender, SelectionChangedEventArgs e)
        {
            // 回填时 SelectedItem 会先被清空再赋值，触发两次；不屏蔽会把 null 当成用户选择。
            if (_updating)
            {
                return;
            }
            WeatherRefreshOption option = _weatherRefreshCombo.SelectedItem as WeatherRefreshOption;
            // 选中项类型不符时保持旧值，避免把刷新频率写成 0 导致控制器空转。
            if (option != null)
            {
                _working.WeatherRefreshMinutes = option.Minutes;
                MarkDirty();
            }
        }

        /// <summary>
        /// 「测试地名」：用当前输入的城市做一次真实查询，只更新页内状态文本，不改任何设置。
        /// </summary>
        private void OnTestWeatherCity(object sender, RoutedEventArgs e)
        {
            string city = (_weatherCityBox.Text ?? string.Empty).Trim();
            // 空城市直接拒绝，不发无意义的网络请求（用户可见拒绝，记一条 Warn 带原因）。
            if (city.Length == 0)
            {
                _weatherTestStatus.Text = "请先填写城市名。";
                SettingsUiHints.SetStatusColor(_weatherTestStatus, UiTheme.Accent);
                AppLog.Warn("Weather", "测试地名被拒绝：城市为空。");
                return;
            }

            // 版本号用于丢弃过期回调：连续点两次测试时，先发出的那次回调不能覆盖后一次的结果。
            int version = ++_weatherTestVersion;
            _weatherTestButton.IsEnabled = false;
            // 查询期间改文案并禁用按钮：一次网络往返可能数百毫秒，必须给出进行中反馈，防止重复点击。
            _weatherTestButton.Content = "查询中…";
            _weatherTestStatus.Text = "正在查询 " + city + " 的天气，请稍候…";
            SettingsUiHints.SetStatusColor(_weatherTestStatus, UiTheme.TextMuted);

            // FetchAsync 在后台线程回调；失败时回调 null 而不抛异常（weather/ 模块契约）。
            WeatherService.FetchAsync(city, delegate(WeatherInfo info)
            {
                try
                {
                    // 必须切回 UI 线程再碰控件；BeginInvoke 而不是 Invoke，避免后台线程等 UI 线程造成卡顿。
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        // 回调到达时可能已经有更新的测试，版本不一致就直接丢弃。
                        if (version != _weatherTestVersion)
                        {
                            return;
                        }
                        _weatherTestButton.IsEnabled = true;
                        // 无论成败都先恢复按钮，否则一次失败后再也点不动。
                        _weatherTestButton.Content = "测试地名";
                        // 契约是失败回调 null，所以 null 分支即「没找到或断网」，客户端不细分原因。
                        if (info != null)
                        {
                            _weatherTestStatus.Text = "✓ 查询成功：" + info.DisplayText;
                            SettingsUiHints.SetStatusColor(_weatherTestStatus, UiTheme.Success);
                            AppLog.Info("Weather", "测试地名成功：" + city + " → " + info.DisplayText + "。");
                        }
                        else
                        {
                            _weatherTestStatus.Text = "✕ 查询失败：未找到该地名，或网络暂时不可用。";
                            SettingsUiHints.SetStatusColor(_weatherTestStatus, UiTheme.Accent);
                            AppLog.Warn("Weather", "测试地名失败：" + city + "，地名未找到或网络不可用。");
                        }
                    }));
                }
                catch (InvalidOperationException ex)
                {
                    // 关闭设置窗口后 Dispatcher 已停止排队，属于正常退出路径；仍记一条，便于解释「点了测试没反应」。
                    AppLog.Error("Weather", "测试地名回调无法回到 UI 线程（窗口可能已关闭）。", ex);
                }
            });
        }

        /// <summary>
        /// 打开系统取色对话框选择天气颜色；与外观页 OnPickColor 相同的 owner 处理方式。
        /// </summary>
        private void OnPickWeatherColor(object sender, RoutedEventArgs e)
        {
            // 对话框期间不改任何字段；点「取消」即完全无副作用（不脏、不重绘）。
            using (WinForms.ColorDialog dialog = new WinForms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                Color current = _working.WeatherColor;
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

                // 无边框置顶窗口必须显式给 owner，否则对话框可能被压到后面。
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WinForms.IWin32Window owner = new SimpleWin32Window(handle);
                // 取消不改字段，避免把「点了取消」当成一次颜色变更。
                if (dialog.ShowDialog(owner) == WinForms.DialogResult.OK)
                {
                    SelectWeatherColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
                }
            }
        }

        /// <summary>
        /// 把天气颜色写进编辑副本并刷新色块、预览与脏标记；取色盘与系统对话框共用这一个落点。
        /// </summary>
        private void SelectWeatherColor(Color color)
        {
            // 取色盘与系统取色对话框共用这一个落点：UI 刷新与脏标记只做一次，避免两套代码漂移。
            // 与外观页一致：持久化的是 #RRGGBB 十六进制串，不是 Color 对象，改名格式会破坏旧配置。
            _working.WeatherColorHex = ColorUtil.ToHex(color);
            UpdateWeatherColorUi();
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 刷新天气色块与取色盘选中态；选中判断基于十六进制串，忽略大小写，与外观页保持一致的视觉规则。
        /// </summary>
        private void UpdateWeatherColorUi()
        {
            // 与外观页同一套视觉规则：选中 3px、未选中 1px，颜色取自主题而不是硬编码。
            Color color = _working.WeatherColor;
            // 色块与十六进制文本同源读取 WeatherColor，避免两处显示不一致。
            _weatherColorSwatch.Background = UiTheme.BrushFrom(color);
            _weatherColorHexText.Text = ColorUtil.ToHex(color);

            // 逐个比对：设置 BorderThickness 而不是换色块，避免选中色与色块本身颜色混淆。
            for (int i = 0; i < _weatherColorPresets.Count; i++)
            {
                ColorPreset preset = _weatherColorPresets[i];
                bool selected = string.Equals(
                    ColorUtil.ToHex(preset.Color),
                    _working.WeatherColorHex,
                    StringComparison.OrdinalIgnoreCase);
                preset.Swatch.BorderThickness = new Thickness(selected ? 3 : 1);
                preset.Swatch.BorderBrush = selected
                    ? UiTheme.TextPrimary
                    : UiTheme.Frozen(0x55, 0x55, 0x60);
            }
        }

        /// <summary>
        /// 天气行自定义格式：空串表示「用默认格式」，非空则按简洁变量语法规范化后写回。
        /// </summary>
        private void OnWeatherFormatTextChanged(object sender, TextChangedEventArgs e)
        {
            // 回填时写 Text 会触发本事件，_updating 期间必须忽略，否则会把回填值再标脏。
            if (_updating)
            {
                return;
            }

            string text = (_weatherFormatBox.Text ?? string.Empty).Trim();
            // 留空是合法状态（回退默认格式），必须单独处理，不能让校验器判成非法。
            if (text.Length == 0)
            {
                _working.WeatherFormat = string.Empty;
                UpdateWeatherFormatStatus();
                UpdatePreview();
                MarkDirty();
                return;
            }

            string normalized;
            string error;
            // 第二个参数 false：天气行不允许换行，天气只显示在时间下方一行。
            if (FormatEngine.TryNormalizeTokenFormat(text, false, out normalized, out error))
            {
                _working.WeatherFormat = normalized;
                UpdateWeatherFormatStatus();
                UpdatePreview();
            }
            else
            {
                // 逐字输入都会进来，只更新状态文本，不记日志，避免打字刷满 app.log。
                _weatherFormatStatus.Text = error;
                SettingsUiHints.SetStatusColor(_weatherFormatStatus, UiTheme.Accent);
            }
            MarkDirty();
        }

        /// <summary>
        /// 刷新天气格式的状态/预览文本：留空时说明默认格式，非空时用示例天气渲染一行真实效果。
        /// </summary>
        private void UpdateWeatherFormatStatus()
        {
            // 空格式代表默认：文案要与 AppSettings 里的默认天气格式保持一致。
            // 空格式用弱化色说明默认值，非空用真实渲染结果做预览：两种状态文案颜色不同。
            if (string.IsNullOrWhiteSpace(_working.WeatherFormat))
            {
                _weatherFormatStatus.Text = "留空使用默认格式：城市 · 天气 · 温度 · 今日高低温。";
                SettingsUiHints.SetStatusColor(_weatherFormatStatus, UiTheme.TextMuted);
                return;
            }

            // 用当前时区的时间 + 示例天气渲染，用户能在输入时直接看到效果。
            // 示例天气固定为北京/晴/12°C（CreateSampleWeather），只用于预览，不代表真实数据。
            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            string example = FormatEngine.Format(now, CreateSampleWeather(), _working.WeatherFormat);
            _weatherFormatStatus.Text = "预览：" + example;
            SettingsUiHints.SetStatusColor(_weatherFormatStatus, UiTheme.TextMuted);
        }

        /// <summary>
        /// 把 WPF 窗口句柄包装成 WinForms.IWin32Window，供 ColorDialog 的 owner 参数使用。
        /// </summary>
        private sealed class SimpleWin32Window : WinForms.IWin32Window
        {
            // WinForms 的 ShowDialog 要求 IWin32Window，而 WPF 的 Window 不实现它，所以只能自己包一层句柄。
            private readonly IntPtr _handle;

            public SimpleWin32Window(IntPtr handle)
            {
                _handle = handle;
            }

            /// <summary>ColorDialog 只读取该句柄，用于把对话框挂到正确的父窗口上。</summary>
            public IntPtr Handle
            {
                get { return _handle; }
            }
        }
    }
}
