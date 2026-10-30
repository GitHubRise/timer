using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GameClock
{
    /// <summary>
    /// A text block with an optional soft shadow and an optional extruded 3D look.
    /// The main text defines the layout; the extrusion layers only use render transforms.
    /// </summary>
    internal sealed class LayeredText : Grid
    {
        private readonly TextBlock _main;
        private readonly List<ExtrusionLayer> _layers;

        public LayeredText()
        {
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            _layers = new List<ExtrusionLayer>();

            for (int depth = 3; depth >= 1; depth--)
            {
                TextBlock layer = CreateTextBlock();
                layer.Visibility = Visibility.Collapsed;
                layer.RenderTransform = new TranslateTransform(0, 0);
                Children.Add(layer);
                _layers.Add(new ExtrusionLayer(layer, depth));
            }

            _main = CreateTextBlock();
            Children.Add(_main);
        }

        public string Text
        {
            get { return _main.Text; }
            set
            {
                string safe = value ?? string.Empty;
                _main.Text = safe;
                for (int i = 0; i < _layers.Count; i++)
                {
                    _layers[i].Text.Text = safe;
                }
            }
        }

        public void ApplyStyle(
            FontFamily fontFamily,
            double fontSize,
            FontWeight fontWeight,
            Color color,
            bool showShadow,
            bool show3D)
        {
            FontFamily family = fontFamily ?? new FontFamily("Consolas");

            SolidColorBrush mainBrush = UiTheme.BrushFrom(color);
            _main.FontFamily = family;
            _main.FontSize = fontSize;
            _main.FontWeight = fontWeight;
            _main.Foreground = mainBrush;
            _main.Effect = showShadow ? CreateShadow(color, fontSize) : null;

            Color edgeColor = CreateEdgeColor(color);
            SolidColorBrush edgeBrush = UiTheme.BrushFrom(edgeColor);
            double step = Math.Max(0.9, Math.Min(2.6, fontSize * 0.034));

            for (int i = 0; i < _layers.Count; i++)
            {
                ExtrusionLayer layer = _layers[i];
                layer.Text.FontFamily = family;
                layer.Text.FontSize = fontSize;
                layer.Text.FontWeight = fontWeight;
                layer.Text.Foreground = edgeBrush;
                layer.Text.Visibility = show3D ? Visibility.Visible : Visibility.Collapsed;
                double offset = step * layer.Depth;
                layer.Text.RenderTransform = new TranslateTransform(offset, offset);
            }
        }

        private static TextBlock CreateTextBlock()
        {
            TextBlock block = new TextBlock();
            block.TextAlignment = TextAlignment.Center;
            block.HorizontalAlignment = HorizontalAlignment.Center;
            block.VerticalAlignment = VerticalAlignment.Center;
            block.IsHitTestVisible = false;
            return block;
        }

        private static DropShadowEffect CreateShadow(Color color, double fontSize)
        {
            bool darkText = ColorUtil.Luminance(color) < 0.22;
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.Color = darkText ? Colors.White : Colors.Black;
            shadow.Direction = 315;
            shadow.ShadowDepth = Math.Max(1.0, fontSize * 0.045);
            shadow.BlurRadius = Math.Max(2.0, fontSize * 0.075);
            shadow.Opacity = darkText ? 0.55 : 0.88;
            return shadow;
        }

        private static Color CreateEdgeColor(Color color)
        {
            if (ColorUtil.Luminance(color) < 0.22)
            {
                return Blend(color, Colors.White, 0.48);
            }
            return Blend(color, Colors.Black, 0.50);
        }

        private static Color Blend(Color from, Color to, double amount)
        {
            double inverse = 1.0 - amount;
            byte r = (byte)Math.Round(from.R * inverse + to.R * amount);
            byte g = (byte)Math.Round(from.G * inverse + to.G * amount);
            byte b = (byte)Math.Round(from.B * inverse + to.B * amount);
            return Color.FromArgb(from.A, r, g, b);
        }

        private sealed class ExtrusionLayer
        {
            public ExtrusionLayer(TextBlock text, int depth)
            {
                Text = text;
                Depth = depth;
            }

            public TextBlock Text { get; private set; }
            public int Depth { get; private set; }
        }
    }
}
