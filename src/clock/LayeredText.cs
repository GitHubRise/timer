// 分层文字控件：主文字 + 3 层挤出偏移，做出可自由配色的立体字/描边观感。
// 由 ClockWindow 的时钟文字与设置页的预览文字创建，调用方只设置 Text 与 ApplyStyle。
// 约束：主层参与布局并决定控件尺寸，挤出层只用 RenderTransform 位移，不改变测量结果，
// 因此开关 3D 只引起重绘，不会让窗口尺寸与位置跳动。
// 已知坑：配色推导按亮度阈值 0.22 分支——深色文字配白边白光、浅色文字配黑边黑影；
// 新增极浅或极深的主题色时需要复核该阈值，否则立体边会与文字糊成一片。

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GameClock
{

    /// <summary>
    /// 带可选阴影与 3D 挤出效果的文字控件：主文字决定布局，挤出层只做渲染位移。
    /// 生命周期：随宿主窗口创建，长期复用；换字号/颜色只调 <see cref="ApplyStyle"/>，不重建子元素。
    /// </summary>
    internal sealed class LayeredText : Grid
    {
        // ===== 视觉子元素 =====

        /// <summary>主文字层：唯一定义尺寸与对齐的元素，<see cref="Text"/> 也以它为准。</summary>
        private readonly TextBlock _main;

        /// <summary>挤出层集合，按深度 3→1 保存；加入顺序决定绘制顺序（先加先画）。</summary>
        private readonly List<ExtrusionLayer> _layers;

        /// <summary>
        /// 构造控件：先建 3 个默认隐藏的挤出层，再建主层叠到最上。
        /// 挤出层只有 <see cref="ApplyStyle"/> 收到 show3D=true 时才显示。
        /// </summary>
        public LayeredText()
        {
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            _layers = new List<ExtrusionLayer>();

            // 从最深处（3）往浅处（1）添加：先加先绘制，主层最后加入，天然压在挤出层之上。
            for (int depth = 3; depth >= 1; depth--)
            {
                TextBlock layer = CreateTextBlock();
                // 先折叠，避免构造瞬间露出没有内容、没有配色的空层。
                layer.Visibility = Visibility.Collapsed;
                // 初始偏移为 0，真正偏移在 ApplyStyle 里按字号与深度计算。
                layer.RenderTransform = new TranslateTransform(0, 0);
                Children.Add(layer);
                _layers.Add(new ExtrusionLayer(layer, depth));
            }

            _main = CreateTextBlock();
            Children.Add(_main);
        }

        /// <summary>
        /// 文字内容：主层与全部挤出层同步写入，保证立体字每层形状完全一致。
        /// 传入 null 时按空串处理，调用方无需先判空。
        /// </summary>
        public string Text
        {
            get { return _main.Text; }
            set
            {
                string safe = value ?? string.Empty;
                _main.Text = safe;
                // 逐层同步；层数固定为 3，循环比逐个字段更不容易漏改。
                for (int i = 0; i < _layers.Count; i++)
                {
                    _layers[i].Text.Text = safe;
                }
            }
        }

        /// <summary>
        /// 统一套用字体、字号、颜色、阴影与 3D 开关。
        /// 宿主每次改设置都调用；内部重建画刷与阴影，但复用已有 TextBlock，不改变控件树与布局层级。
        /// </summary>
        /// <param name="fontFamily">字体；为 null 时退回 <c>Consolas</c>，保证时间数字等宽、刷新不跳动。</param>
        /// <param name="fontSize">主文字字号，挤出步长与阴影尺寸都按它缩放。</param>
        /// <param name="fontWeight">字重，时钟通常传 SemiBold。</param>
        /// <param name="color">主文字颜色，挤出边与阴影颜色由它推导。</param>
        /// <param name="showShadow">是否加柔和投影。</param>
        /// <param name="show3D">是否显示 3 层挤出；false 时只留主层，纯平面显示。</param>
        public void ApplyStyle(
            FontFamily fontFamily,
            double fontSize,
            FontWeight fontWeight,
            Color color,
            bool showShadow,
            bool show3D)
        {
            // 字体缺失时退回等宽字体，避免数字宽度不一导致每次刷新都轻微位移。
            FontFamily family = fontFamily ?? new FontFamily("Consolas");

            // 主层：颜色、字体、字号、阴影一次设全；阴影关闭时必须显式置 null，旧 Effect 不会自动清除。
            SolidColorBrush mainBrush = UiTheme.BrushFrom(color);
            _main.FontFamily = family;
            _main.FontSize = fontSize;
            _main.FontWeight = fontWeight;
            _main.Foreground = mainBrush;
            _main.Effect = showShadow ? CreateShadow(color, fontSize) : null;

            // 挤出边颜色由主色向白/黑混合得到，保证任何主色下都有对比度。
            Color edgeColor = CreateEdgeColor(color);
            SolidColorBrush edgeBrush = UiTheme.BrushFrom(edgeColor);
            // 每层偏移量随字号线性增长并夹在 0.9–2.6 像素：小字号不至于糊成一块，大字号也不会散开。
            double step = Math.Max(0.9, Math.Min(2.6, fontSize * 0.034));

            // 逐层更新：层数固定为 3，用循环比逐个字段更不容易漏改。
            for (int i = 0; i < _layers.Count; i++)
            {
                ExtrusionLayer layer = _layers[i];
                layer.Text.FontFamily = family;
                layer.Text.FontSize = fontSize;
                layer.Text.FontWeight = fontWeight;
                // 三层共用同一支画刷，避免重复创建对象。
                layer.Text.Foreground = edgeBrush;
                // 关闭 3D 时整层折叠，不参与渲染，但保留对象以便下次开启时立即复用。
                layer.Text.Visibility = show3D ? Visibility.Visible : Visibility.Collapsed;
                // 深度越大偏移越远，形成由深到浅的立体阶梯。
                double offset = step * layer.Depth;
                layer.Text.RenderTransform = new TranslateTransform(offset, offset);
            }
        }

        /// <summary>
        /// 创建一层文字并套上统一的居中、不参与命中测试的样式。
        /// 主层与挤出层共用，保证度量一致、各层完全重合。
        /// </summary>
        private static TextBlock CreateTextBlock()
        {
            TextBlock block = new TextBlock();
            block.TextAlignment = TextAlignment.Center;
            block.HorizontalAlignment = HorizontalAlignment.Center;
            block.VerticalAlignment = VerticalAlignment.Center;
            // 所有层都不吃鼠标：拖动与右键必须落到外层 Border（窗口靠它实现拖动）。
            block.IsHitTestVisible = false;
            return block;
        }

        /// <summary>
        /// 按主色亮度生成柔和投影：深色文字投白影、浅色文字投黑影，任何背景上都能读出轮廓。
        /// 尺寸随字号缩放，避免大字号仍用固定小阴影显得脏。
        /// </summary>
        /// <param name="color">主文字颜色，用于判断明暗。</param>
        /// <param name="fontSize">当前字号，决定阴影深度与模糊半径。</param>
        private static DropShadowEffect CreateShadow(Color color, double fontSize)
        {
            // 亮度低于 0.22 视为深色文字，需要浅色阴影托底；阈值与 CreateEdgeColor 保持一致。
            bool darkText = ColorUtil.Luminance(color) < 0.22;
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.Color = darkText ? Colors.White : Colors.Black;
            // 315° 表示光来自左上，投影落向右下，与常见的立体感方向一致。
            shadow.Direction = 315;
            shadow.ShadowDepth = Math.Max(1.0, fontSize * 0.045);
            shadow.BlurRadius = Math.Max(2.0, fontSize * 0.075);
            // 白影过强会让深色文字发灰，因此取更低的不透明度；黑影可以更实一些。
            shadow.Opacity = darkText ? 0.55 : 0.88;
            return shadow;
        }

        /// <summary>
        /// 推导挤出边颜色：深色文字向白混、浅色文字向黑混，保证边缘始终与主色有对比。
        /// </summary>
        /// <param name="color">主文字颜色。</param>
        private static Color CreateEdgeColor(Color color)
        {
            // 与 CreateShadow 共用 0.22 阈值，避免阴影和描边对“深/浅”判断不一致。
            if (ColorUtil.Luminance(color) < 0.22)
            {
                // 深色文字提亮 48% 做边，既看得见又不至于盖过主色。
                return Blend(color, Colors.White, 0.48);
            }
            // 浅色文字压暗 50% 做边，形成阴影侧厚度。
            return Blend(color, Colors.Black, 0.50);
        }

        /// <summary>
        /// 按比例线性混合两种颜色，保留 <paramref name="from"/> 的 alpha。
        /// 用于生成与主色同透明度体系的挤出边，避免边缘出现半透明缺口。
        /// </summary>
        /// <param name="from">起始色（主色）。</param>
        /// <param name="to">目标色（白或黑）。</param>
        /// <param name="amount">混合比例，0 = 全为 from，1 = 全为 to。</param>
        private static Color Blend(Color from, Color to, double amount)
        {
            // 预先算好反向权重，逐通道 lerp，比逐通道写两次乘法更直观。
            double inverse = 1.0 - amount;
            byte r = (byte)Math.Round(from.R * inverse + to.R * amount);
            byte g = (byte)Math.Round(from.G * inverse + to.G * amount);
            byte b = (byte)Math.Round(from.B * inverse + to.B * amount);
            return Color.FromArgb(from.A, r, g, b);
        }

        /// <summary>
        /// 挤出层的配对记录：把 <see cref="TextBlock"/> 与其深度绑在一起，
        /// 深度只用于计算偏移倍数，不参与布局。
        /// </summary>
        private sealed class ExtrusionLayer
        {

            /// <summary>绑定文字层与深度；深度 1 最近主色，3 最远。</summary>
            public ExtrusionLayer(TextBlock text, int depth)
            {
                Text = text;
                Depth = depth;
            }

            /// <summary>该层的文字元素，字体与颜色由 <see cref="ApplyStyle"/> 统一设置。</summary>
            public TextBlock Text { get; private set; }

            /// <summary>挤出深度（1–3），乘以步长得到实际偏移像素。</summary>
            public int Depth { get; private set; }
        }
    }
}
