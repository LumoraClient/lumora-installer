// Lumora Setup - the launcher's hero scene as window background: a voxel night landscape (starry sky, glowing
// horizon, blocky forest, castle silhouette; own artwork, generated), an aurora drifting slowly and twinkling stars.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lumora.Setup
{
    internal sealed class Scene : Grid
    {
        private readonly Image landscape = new Image();
        private readonly Ellipse auroraA = new Ellipse();
        private readonly Ellipse auroraB = new Ellipse();
        private readonly TranslateTransform moveA = new TranslateTransform();
        private readonly TranslateTransform moveB = new TranslateTransform();
        private readonly Canvas starLayer = new Canvas();
        private readonly List<Rectangle> stars = new List<Rectangle>();
        private readonly List<double[]> starPhase = new List<double[]>();
        private readonly DateTime start = DateTime.Now;
        private Size built;

        public Scene()
        {
            ClipToBounds = true;
            Background = T.Frozen(T.Bg);
            landscape.Stretch = Stretch.Fill;
            RenderOptions.SetBitmapScalingMode(landscape, BitmapScalingMode.NearestNeighbor);
            Children.Add(landscape);

            var canvas = new Canvas();
            canvas.IsHitTestVisible = false;
            Setup(auroraA, T.A(T.Accent, 62), moveA);
            Setup(auroraB, T.A(T.Accent2, 48), moveB);
            canvas.Children.Add(auroraA);
            canvas.Children.Add(auroraB);
            Children.Add(canvas);
            starLayer.IsHitTestVisible = false;
            Children.Add(starLayer);

            SizeChanged += delegate { Rebuild(); };
            Loaded += delegate { CompositionTarget.Rendering += Tick; };
            Unloaded += delegate { CompositionTarget.Rendering -= Tick; };
        }

        private static void Setup(Ellipse e, Color c, TranslateTransform move)
        {
            var g = new RadialGradientBrush(c, T.A(c, 0));
            g.Freeze();
            e.Fill = g;
            e.RenderTransform = move;
            e.CacheMode = new BitmapCache(0.5);
            e.IsHitTestVisible = false;
        }

        private void Rebuild()
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 10 || h < 10 || (Math.Abs(built.Width - w) < 1 && Math.Abs(built.Height - h) < 1))
            {
                return;
            }
            built = new Size(w, h);
            landscape.Source = Render((int)w, (int)h);
            double r = Math.Max(w, h) * 0.42;
            auroraA.Width = auroraA.Height = r * 2;
            auroraB.Width = auroraB.Height = r * 1.7;
            starLayer.Children.Clear();
            stars.Clear();
            starPhase.Clear();
            var rnd = new Random(7);
            for (int i = 0; i < 34; i++)
            {
                var s = new Rectangle();
                s.Width = s.Height = rnd.Next(5) == 0 ? 3 : 2;
                s.Fill = T.Frozen(Color.FromRgb(220, 238, 255));
                Canvas.SetLeft(s, rnd.Next((int)w));
                Canvas.SetTop(s, rnd.Next((int)(h * 0.48)));
                starLayer.Children.Add(s);
                stars.Add(s);
                starPhase.Add(new[] { 1.2 + rnd.NextDouble() * 2.2, rnd.NextDouble() * 6.3 });
            }
            Tick(null, EventArgs.Empty);
        }

        private void Tick(object sender, EventArgs e)
        {
            double t = (DateTime.Now - start).TotalSeconds;
            if (T.Calm)
            {
                t = 0;
            }
            double w = ActualWidth;
            double h = ActualHeight;
            moveA.X = w * (0.30 + 0.18 * Math.Sin(t * 0.21)) - auroraA.Width / 2;
            moveA.Y = h * (0.28 + 0.08 * Math.Cos(t * 0.33)) - auroraA.Height / 2;
            moveB.X = w * (0.68 + 0.16 * Math.Cos(t * 0.17)) - auroraB.Width / 2;
            moveB.Y = h * (0.22 + 0.10 * Math.Sin(t * 0.27)) - auroraB.Height / 2;
            for (int i = 0; i < stars.Count; i++)
            {
                double tw = 0.5 + 0.5 * Math.Sin(t * starPhase[i][0] + starPhase[i][1]);
                stars[i].Opacity = T.Calm ? 0.6 : 0.15 + 0.75 * tw * tw;
            }
        }

        /// Static landscape (port of HeroPanel.render from the launcher), rendered once per size.
        private static ImageSource Render(int w, int h)
        {
            var dv = new DrawingVisual();
            using (DrawingContext g = dv.RenderOpen())
            {
                var rnd = new Random(2612);
                Color accent = T.Accent;
                Color deep = T.Mix(T.Rgb(0x040A18), accent, 0.20);
                Color dusk = T.Mix(T.Rgb(0x0E2552), accent, 0.45);
                var sky = new LinearGradientBrush(deep, dusk, new Point(0, 0), new Point(0, 0.78));
                g.DrawRectangle(sky, null, new Rect(0, 0, w, h));
                Glow(g, w, h, w * 0.82, h * 0.64, Math.Max(w, h) * 0.45, Color.FromArgb(120, 150, 225, 255),
                    T.A(T.Mix(accent, T.Rgb(0x22D3EE), 0.5), 70));
                Glow(g, w, h, w * 0.18, h * 0.35, Math.Max(w, h) * 0.40, T.A(accent, 80), T.A(accent, 0));

                for (int i = 0; i < w * h / 2400; i++)
                {
                    int sx = rnd.Next(w);
                    int sy = rnd.Next((int)(h * 0.55));
                    int a = 50 + rnd.Next(150);
                    int s = rnd.Next(10) == 0 ? 3 : 2;
                    g.DrawRectangle(T.Frozen(Color.FromArgb((byte)a, 255, 255, 255)), null, new Rect(sx, sy, s, s));
                }

                int block = Math.Max(8, h / 34);
                Color far = T.Mix(T.Rgb(0x0B1A36), accent, 0.30);
                Hills(g, rnd, w, h, block, far, T.Mix(far, accent, 0.35), 0.70, 0.08);
                Castle(g, w, h, Math.Max(6, h / 52), T.Mix(T.Rgb(0x07122A), accent, 0.12), T.A(T.Rgb(0xBFE8FF), 150));
                Color near = T.Mix(T.Rgb(0x050B1A), accent, 0.10);
                Hills(g, rnd, w, h, block, near, T.Mix(near, accent, 0.25), 0.84, 0.06);
                Trees(g, rnd, w, h, block, near);

                // darker band under the content card and fade into the page colour at the bottom
                var bottom = new LinearGradientBrush(T.A(T.Bg, 0), T.A(T.Bg, 225), new Point(0, 0.55), new Point(0, 1));
                g.DrawRectangle(bottom, null, new Rect(0, 0, w, h));
                var right = new LinearGradientBrush(T.A(T.Bg, 0), T.A(T.Bg, 120), new Point(0.25, 0), new Point(0.75, 0));
                g.DrawRectangle(right, null, new Rect(0, 0, w, h));
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static void Glow(DrawingContext g, int w, int h, double x, double y, double r, Color inner, Color outer)
        {
            var b = new RadialGradientBrush();
            b.MappingMode = BrushMappingMode.Absolute;
            b.Center = new Point(x, y);
            b.GradientOrigin = new Point(x, y);
            b.RadiusX = r;
            b.RadiusY = r;
            b.GradientStops.Add(new GradientStop(inner, 0));
            b.GradientStops.Add(new GradientStop(outer, 0.4));
            b.GradientStops.Add(new GradientStop(T.A(outer, 0), 1));
            g.DrawRectangle(b, null, new Rect(0, 0, w, h));
        }

        private static void Hills(DrawingContext g, Random rnd, int w, int h, int block, Color body, Color rim, double bas, double amp)
        {
            double p1 = rnd.NextDouble() * 10;
            double p2 = rnd.NextDouble() * 10;
            Brush bb = T.Frozen(body);
            Brush rb = T.Frozen(rim);
            for (int x = 0; x < w + block; x += block)
            {
                double t = x / (double)w;
                double n = Math.Sin(t * Math.PI * 2.2 + p1) * 0.6 + Math.Sin(t * Math.PI * 6.1 + p2) * 0.4;
                int top = (int)(h * (bas - amp * n)) / block * block;
                g.DrawRectangle(bb, null, new Rect(x, top, block, h - top));
                g.DrawRectangle(rb, null, new Rect(x, top, block, Math.Max(2, block / 4)));
            }
        }

        private static void Castle(DrawingContext g, int w, int h, int b, Color stone, Color light)
        {
            int ground = (int)(h * 0.76) / b * b;
            // left, next to the step rail (the content card covers the right side of the window)
            int x0 = (int)(w * 0.035) / b * b;
            Brush sb = T.Frozen(stone);
            int[][] parts = { new[] { 0, 7, 9 }, new[] { 2, 5, 13 }, new[] { 7, 4, 17 }, new[] { 11, 6, 11 }, new[] { 17, 3, 15 }, new[] { 20, 8, 8 } };
            foreach (int[] p in parts)
            {
                int px = x0 + p[0] * b;
                int top = ground - p[2] * b;
                g.DrawRectangle(sb, null, new Rect(px, top, p[1] * b, ground - top + b));
                for (int i = 0; i < p[1]; i += 2)
                {
                    g.DrawRectangle(sb, null, new Rect(px + i * b, top - b, b, b));
                }
            }
            var spire = new StreamGeometry();
            using (StreamGeometryContext c = spire.Open())
            {
                c.BeginFigure(new Point(x0 + 7 * b, ground - 17 * b), true, true);
                c.LineTo(new Point(x0 + 9 * b, ground - 23 * b), false, false);
                c.LineTo(new Point(x0 + 11 * b, ground - 17 * b), false, false);
            }
            g.DrawGeometry(sb, null, spire);
            Brush lb = T.Frozen(light);
            int[][] windows = { new[] { 8, 12 }, new[] { 9, 9 }, new[] { 3, 9 }, new[] { 12, 7 }, new[] { 18, 10 }, new[] { 21, 5 }, new[] { 13, 5 } };
            foreach (int[] wnd in windows)
            {
                g.DrawRectangle(lb, null, new Rect(x0 + wnd[0] * b + b / 4, ground - wnd[1] * b, Math.Max(3, b / 2), Math.Max(4, b * 3 / 4)));
            }
        }

        private static void Trees(DrawingContext g, Random rnd, int w, int h, int b, Color dark)
        {
            int ground = (int)(h * 0.80);
            Brush trunk = T.Frozen(T.Mix(dark, Colors.Black, 0.3));
            Brush crown = T.Frozen(dark);
            for (int x = (int)(w * 0.30); x < w * 0.62; x += b * (2 + rnd.Next(3)))
            {
                int height = b * (4 + rnd.Next(5));
                int top = ground - height;
                g.DrawRectangle(trunk, null, new Rect(x + b, top + b * 2, Math.Max(3, b / 2), height));
                g.DrawRectangle(crown, null, new Rect(x - b / 2, top, b * 3, b * 3));
                g.DrawRectangle(crown, null, new Rect(x, top - b, b * 2, b));
            }
        }
    }

    /// Small previews for the design page: Lumora theme (aurora, glass buttons) and classic Minecraft-style menus
    /// (stone-coloured buttons on a dark block background). Both drawn here, no game assets.
    internal static class Previews
    {
        public static FrameworkElement Lumora(double w, double h)
        {
            var root = new Grid();
            root.Width = w;
            root.Height = h;
            root.ClipToBounds = true;
            var sky = new LinearGradientBrush(T.Mix(T.Rgb(0x040A18), T.Accent, 0.2), T.Mix(T.Rgb(0x0E2552), T.Accent, 0.45), 90);
            sky.Freeze();
            root.Background = sky;

            var canvas = new Canvas();
            var glow = new Ellipse();
            glow.Width = glow.Height = h * 1.3;
            var gb = new RadialGradientBrush(T.A(T.Accent2, 90), T.A(T.Accent2, 0));
            gb.Freeze();
            glow.Fill = gb;
            var move = new TranslateTransform(-h * 0.3, -h * 0.5);
            glow.RenderTransform = move;
            canvas.Children.Add(glow);
            if (!T.Calm)
            {
                var a = new System.Windows.Media.Animation.DoubleAnimation(-h * 0.4, w - h * 0.9, new Duration(TimeSpan.FromSeconds(7)));
                a.AutoReverse = true;
                a.RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever;
                var ease = new System.Windows.Media.Animation.SineEase();
                ease.EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut;
                a.EasingFunction = ease;
                move.BeginAnimation(TranslateTransform.XProperty, a);
            }
            // castle and hills
            int b = 6;
            Brush hill = T.Frozen(T.Mix(T.Rgb(0x0B1A36), T.Accent, 0.3));
            for (int x = 0; x < w; x += b)
            {
                double top = h * 0.80 - Math.Round(Math.Sin(x / w * 7) * 2) * b;
                var r = new Rectangle();
                r.Width = b;
                r.Height = h - top;
                r.Fill = hill;
                Canvas.SetLeft(r, x);
                Canvas.SetTop(r, top);
                canvas.Children.Add(r);
            }
            Brush stone = T.Frozen(T.Mix(T.Rgb(0x07122A), T.Accent, 0.12));
            int[][] parts = { new[] { 0, 3, 5 }, new[] { 3, 2, 8 }, new[] { 5, 3, 6 } };
            foreach (int[] p in parts)
            {
                var r = new Rectangle();
                r.Width = p[1] * b;
                r.Height = p[2] * b;
                r.Fill = stone;
                Canvas.SetLeft(r, w * 0.76 + p[0] * b);
                Canvas.SetTop(r, h * 0.84 - p[2] * b);
                canvas.Children.Add(r);
            }
            root.Children.Add(canvas);

            var col = new StackPanel();
            col.HorizontalAlignment = HorizontalAlignment.Left;
            col.VerticalAlignment = VerticalAlignment.Center;
            col.Margin = new Thickness(16, 0, 0, 0);
            TextBlock title = T.Label("LUMORA", 15, FontWeights.Bold, Colors.White);
            title.FontStyle = FontStyles.Italic;
            title.Margin = new Thickness(1, 0, 0, 8);
            col.Children.Add(title);
            string[] labels = { "Einzelspieler", "Mehrspieler", "Einstellungen" };
            for (int i = 0; i < labels.Length; i++)
            {
                var btn = new Border();
                btn.Width = 104;
                btn.Height = 20;
                btn.CornerRadius = new CornerRadius(6);
                btn.Margin = new Thickness(0, 0, 0, 5);
                if (i == 0)
                {
                    btn.Background = T.Flow();
                }
                else
                {
                    btn.Background = T.Frozen(T.A(T.Surface, 200));
                    btn.BorderBrush = T.Frozen(T.A(T.AccentHover, 70));
                    btn.BorderThickness = new Thickness(1);
                }
                TextBlock l = T.Label(labels[i], 9.5, FontWeights.SemiBold, Colors.White);
                l.HorizontalAlignment = HorizontalAlignment.Center;
                l.VerticalAlignment = VerticalAlignment.Center;
                btn.Child = l;
                col.Children.Add(btn);
            }
            root.Children.Add(col);
            return root;
        }

        public static FrameworkElement Classic(double w, double h)
        {
            var root = new Grid();
            root.Width = w;
            root.Height = h;
            root.ClipToBounds = true;
            // dark block background (generated noise of brownish greys)
            var dv = new DrawingVisual();
            int cell = 8;
            using (DrawingContext g = dv.RenderOpen())
            {
                var rnd = new Random(189);
                for (int y = 0; y < h; y += cell)
                {
                    for (int x = 0; x < w; x += cell)
                    {
                        int v = 38 + rnd.Next(18);
                        g.DrawRectangle(T.Frozen(Color.FromRgb((byte)(v + 6), (byte)(v + 2), (byte)(v - 4))), null, new Rect(x, y, cell, cell));
                    }
                }
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            var bg = new Image();
            bg.Source = bmp;
            RenderOptions.SetBitmapScalingMode(bg, BitmapScalingMode.NearestNeighbor);
            root.Children.Add(bg);

            var col = new StackPanel();
            col.HorizontalAlignment = HorizontalAlignment.Center;
            col.VerticalAlignment = VerticalAlignment.Center;
            string[] labels = { "Einzelspieler", "Mehrspieler", "Optionen..." };
            for (int i = 0; i < labels.Length; i++)
            {
                col.Children.Add(StoneButton(labels[i], 150, 21, i == 0));
            }
            root.Children.Add(col);
            return root;
        }

        /// Square grey button with light top edge and dark bottom edge (the classic block-game button shape).
        private static FrameworkElement StoneButton(string label, double w, double h, bool hover)
        {
            var outer = new Border();
            outer.Width = w;
            outer.Height = h;
            outer.Margin = new Thickness(0, 0, 0, 5);
            outer.Background = T.Frozen(hover ? Color.FromRgb(0xEE, 0xEE, 0xEE) : Colors.Black);
            outer.Padding = new Thickness(1);
            var face = new Border();
            face.Background = T.Frozen(hover ? Color.FromRgb(0x7E, 0x88, 0xBF) : Color.FromRgb(0x6F, 0x6F, 0x6F));
            face.BorderBrush = T.Frozen(hover ? Color.FromRgb(0xA9, 0xB4, 0xE8) : Color.FromRgb(0xA8, 0xA8, 0xA8));
            face.BorderThickness = new Thickness(1, 1, 0, 0);
            var bottom = new Border();
            bottom.BorderBrush = T.Frozen(hover ? Color.FromRgb(0x4A, 0x52, 0x82) : Color.FromRgb(0x4C, 0x4C, 0x4C));
            bottom.BorderThickness = new Thickness(0, 0, 1, 2);
            face.Child = bottom;
            var g = new Grid();
            TextBlock shadow = T.Label(label, 9.5, FontWeights.SemiBold, Color.FromRgb(0x3F, 0x3F, 0x3F));
            shadow.HorizontalAlignment = HorizontalAlignment.Center;
            shadow.VerticalAlignment = VerticalAlignment.Center;
            shadow.Margin = new Thickness(1.2, 1.2, 0, 0);
            TextBlock text = T.Label(label, 9.5, FontWeights.SemiBold, hover ? Color.FromRgb(0xFF, 0xFF, 0xA0) : Color.FromRgb(0xE0, 0xE0, 0xE0));
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(shadow);
            g.Children.Add(text);
            bottom.Child = g;
            outer.Child = face;
            return outer;
        }
    }
}
