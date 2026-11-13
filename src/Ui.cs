// Lumora Setup - design tokens and controls (WPF, C# 5, .NET Framework 4.8).
// Colours follow the launcher (launcher-app/.../ui/Theme.java): deep navy surfaces, blue accent, ice-blue second accent.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Lumora.Setup
{
    internal static class T
    {
        public static readonly Color Bg = Rgb(0x070B14);
        public static readonly Color Surface = Rgb(0x0D1424);
        public static readonly Color Surface2 = Rgb(0x16213A);
        public static readonly Color Border = Rgb(0x22314F);
        public static readonly Color Text = Rgb(0xE8EEFA);
        public static readonly Color Muted = Rgb(0x8D9BB8);
        public static readonly Color Accent = Rgb(0x3B82F6);
        public static readonly Color AccentHover = Rgb(0x5E9BF8);
        public static readonly Color Accent2 = Rgb(0x2DAEF2);
        public static readonly Color AccentDeep = Rgb(0x365CC1);
        public static readonly Color AccentText = Rgb(0xCFE0FD);
        public static readonly Color Ok = Rgb(0x3DD68C);
        public static readonly Color Warn = Rgb(0xF5B342);
        public static readonly Color Fail = Rgb(0xF2555A);

        public static FontFamily Font = new FontFamily("Segoe UI");
        /// No automatic animations (Windows "Animationen anzeigen" off).
        public static bool Calm;

        public static Color Rgb(int rgb)
        {
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        public static Color A(Color c, int alpha)
        {
            return Color.FromArgb((byte)Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        public static SolidColorBrush B(Color c)
        {
            return new SolidColorBrush(c);
        }

        public static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// Loads Inter (extracted from the setup's resources) or keeps Segoe UI.
        public static void LoadFonts()
        {
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lumora-setup-fonts");
                Directory.CreateDirectory(dir);
                foreach (string name in new[] { "Inter-Regular.otf", "Inter-SemiBold.otf", "Inter-Bold.otf", "Inter-BoldItalic.otf" })
                {
                    string target = System.IO.Path.Combine(dir, name);
                    using (Stream s = Res.Open(name))
                    {
                        if (s == null)
                        {
                            return;
                        }
                        if (!File.Exists(target) || new FileInfo(target).Length != s.Length)
                        {
                            using (FileStream f = File.Create(target))
                            {
                                s.CopyTo(f);
                            }
                        }
                    }
                }
                Font = new FontFamily(new Uri(dir + "\\"), "./#Inter, Segoe UI");
            }
            catch (Exception)
            {
                Font = new FontFamily("Segoe UI");
            }
        }

        public static TextBlock Label(string text, double size, FontWeight weight, Color color)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontFamily = Font;
            t.FontSize = size;
            t.FontWeight = weight;
            t.Foreground = Frozen(color);
            t.TextWrapping = TextWrapping.Wrap;
            TextOptions.SetTextFormattingMode(t, size < 15 ? TextFormattingMode.Display : TextFormattingMode.Ideal);
            return t;
        }

        public static TextBlock Spaced(string text, Color color)
        {
            TextBlock t = Label(text.ToUpperInvariant(), 10.5, FontWeights.Bold, color);
            t.TextWrapping = TextWrapping.NoWrap;
            return t;
        }

        public static Duration Ms(double ms)
        {
            return new Duration(TimeSpan.FromMilliseconds(Calm ? 1 : ms));
        }

        public static IEasingFunction EaseOut()
        {
            var e = new CubicEase();
            e.EasingMode = EasingMode.EaseOut;
            return e;
        }

        public static void Animate(Animatable target, DependencyProperty p, double to, double ms)
        {
            var a = new DoubleAnimation(to, Ms(ms));
            a.EasingFunction = EaseOut();
            target.BeginAnimation(p, a);
        }

        public static void Animate(UIElement target, DependencyProperty p, double to, double ms)
        {
            var a = new DoubleAnimation(to, Ms(ms));
            a.EasingFunction = EaseOut();
            target.BeginAnimation(p, a);
        }

        public static void Fade(SolidColorBrush brush, Color to, double ms)
        {
            var a = new ColorAnimation(to, Ms(ms));
            a.EasingFunction = EaseOut();
            brush.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }

        /// Flowing gradient accent -> ice blue -> accent (the launcher's play button).
        public static LinearGradientBrush Flow()
        {
            var g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(1, 0.4);
            g.SpreadMethod = GradientSpreadMethod.Reflect;
            g.GradientStops.Add(new GradientStop(Accent, 0));
            g.GradientStops.Add(new GradientStop(Accent2, 0.5));
            g.GradientStops.Add(new GradientStop(Mix(Accent, AccentDeep, 0.4), 1));
            var tt = new TranslateTransform();
            g.RelativeTransform = tt;
            if (!Calm)
            {
                var a = new DoubleAnimation(-1, 1, new Duration(TimeSpan.FromSeconds(8)));
                a.AutoReverse = true;
                a.RepeatBehavior = RepeatBehavior.Forever;
                tt.BeginAnimation(TranslateTransform.XProperty, a);
            }
            return g;
        }
    }

    /// Small vector icons (24x24 stroke paths, own drawings).
    internal static class Icons
    {
        public const string Check = "M5,12.5 L10,17.5 L19,7";
        public const string Cube = "M12,2.5 L20.5,7 L20.5,17 L12,21.5 L3.5,17 L3.5,7 Z M3.5,7 L12,11.5 L20.5,7 M12,11.5 L12,21.5";
        public const string Folder = "M3,6.5 C3,5.4 3.9,4.5 5,4.5 L9.5,4.5 L11.5,7 L19,7 C20.1,7 21,7.9 21,9 L21,17.5 C21,18.6 20.1,19.5 19,19.5 L5,19.5 C3.9,19.5 3,18.6 3,17.5 Z";
        public const string Palette = "M12,3 C7,3 3,7 3,12 C3,17 7,21 12,21 C13.2,21 13.8,20.2 13.8,19.3 C13.8,18.3 13,17.9 13,17 C13,16 13.8,15.3 14.8,15.3 L16.5,15.3 C19,15.3 21,13.3 21,10.8 C21,6.5 17,3 12,3 Z M7.5,12 A1.2,1.2 0 1 0 7.5,11.99 M10,7.5 A1.2,1.2 0 1 0 10,7.49 M15,7.5 A1.2,1.2 0 1 0 15,7.49";
        public const string Download = "M12,3.5 L12,15 M7,10.5 L12,15.5 L17,10.5 M4,19.5 L20,19.5";
        public const string Rocket = "M12,15 L9,12 C10,8 12.5,4.5 18.5,3.5 C19,3.5 20.5,5 20.5,5.5 C19.5,11.5 16,14 12,15 Z M9,12 L5.5,12 L4,9.5 L8,8 M12,15 L12,18.5 L14.5,20 L16,16 M6,15 C4.5,15.5 4,17.5 4,20 C6.5,20 8.5,19.5 9,18";
        public const string Star = "M12,3 L14.4,9.2 L21,9.6 L15.9,13.8 L17.6,20.3 L12,16.6 L6.4,20.3 L8.1,13.8 L3,9.6 L9.6,9.2 Z";
        public const string Monitor = "M3.5,5 L20.5,5 L20.5,16 L3.5,16 Z M9,20 L15,20 M12,16 L12,20";
        public const string Trash = "M4.5,6.5 L19.5,6.5 M9.5,6.5 L9.5,4.5 L14.5,4.5 L14.5,6.5 M6.5,6.5 L7.5,20 L16.5,20 L17.5,6.5 M10.5,10 L10.5,16.5 M13.5,10 L13.5,16.5";
        public const string Alert = "M12,4 L21,19.5 L3,19.5 Z M12,10 L12,14 M12,16.8 L12,17";
        public const string Close = "M6,6 L18,18 M18,6 L6,18";
        public const string Minus = "M6,12 L18,12";
        public const string Arrow = "M5,12 L19,12 M13,6 L19,12 L13,18";
        public const string Back = "M19,12 L5,12 M11,6 L5,12 L11,18";
        public const string Sparkle = "M12,3 C12.6,8.4 15.6,11.4 21,12 C15.6,12.6 12.6,15.6 12,21 C11.4,15.6 8.4,12.6 3,12 C8.4,11.4 11.4,8.4 12,3 Z";
        public const string Play = "M8,5 L19,12 L8,19 Z";
        public const string Power = "M12,3 L12,11 M7,6 C4.6,7.6 3.5,10 3.5,12.5 C3.5,17.2 7.3,21 12,21 C16.7,21 20.5,17.2 20.5,12.5 C20.5,10 19.4,7.6 17,6";

        public static Path Of(string data, double size, Color color, double thickness)
        {
            var p = new Path();
            p.Data = Geometry.Parse(data);
            p.Stroke = T.Frozen(color);
            p.StrokeThickness = thickness;
            p.StrokeLineJoin = PenLineJoin.Round;
            p.StrokeStartLineCap = PenLineCap.Round;
            p.StrokeEndLineCap = PenLineCap.Round;
            p.Stretch = Stretch.Uniform;
            p.Width = size;
            p.Height = size;
            p.SnapsToDevicePixels = false;
            return p;
        }

        public static Path Filled(string data, double size, Color color)
        {
            var p = new Path();
            p.Data = Geometry.Parse(data);
            p.Fill = T.Frozen(color);
            p.Stretch = Stretch.Uniform;
            p.Width = size;
            p.Height = size;
            return p;
        }

        /// Icon on a soft accent tile, like the launcher's setting rows.
        public static Border Tile(string data, Color color)
        {
            var b = new Border();
            b.Width = 38;
            b.Height = 38;
            b.CornerRadius = new CornerRadius(10);
            b.Background = T.Frozen(T.A(color, 0x24));
            b.BorderBrush = T.Frozen(T.A(color, 0x40));
            b.BorderThickness = new Thickness(1);
            b.Child = Of(data, 18, T.Mix(color, Colors.White, 0.25), 1.8);
            return b;
        }
    }

    /// Big accent button: flowing gradient, light sweep every few seconds, breathing glow (launcher PlayButton).
    internal sealed class PrimaryButton : Grid
    {
        private readonly Border body = new Border();
        private readonly Border glow = new Border();
        private readonly Rectangle sweep = new Rectangle();
        private readonly TextBlock text;
        private readonly StackPanel content = new StackPanel();
        private readonly DropShadowEffect shadow = new DropShadowEffect();
        private readonly Border shade = new Border();
        private Path icon;
        private bool enabled = true;
        private Brush flow;
        public event Action Click;

        public PrimaryButton(string label, string iconData)
        {
            Height = 46;
            MinWidth = 150;
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;

            shadow.Color = T.Accent;
            shadow.BlurRadius = 22;
            shadow.ShadowDepth = 3;
            shadow.Direction = 270;
            shadow.Opacity = 0.55;
            glow.CornerRadius = new CornerRadius(12);
            glow.Background = T.Frozen(T.Accent);
            glow.Margin = new Thickness(4, 4, 4, 0);
            glow.Effect = shadow;
            Children.Add(glow);

            flow = T.Flow();
            body.CornerRadius = new CornerRadius(12);
            body.Background = flow;
            body.ClipToBounds = true;
            Children.Add(body);

            var inner = new Grid();
            body.Child = inner;
            var highlight = new Border();
            var hl = new LinearGradientBrush(T.A(Colors.White, 46), T.A(Colors.White, 0), 90);
            hl.Freeze();
            highlight.Background = hl;
            highlight.VerticalAlignment = VerticalAlignment.Top;
            highlight.Height = 22;
            inner.Children.Add(highlight);

            var sg = new LinearGradientBrush();
            sg.StartPoint = new Point(0, 0);
            sg.EndPoint = new Point(1, 0.5);
            sg.GradientStops.Add(new GradientStop(T.A(Colors.White, 0), 0));
            sg.GradientStops.Add(new GradientStop(T.A(Colors.White, 70), 0.5));
            sg.GradientStops.Add(new GradientStop(T.A(Colors.White, 0), 1));
            sg.Freeze();
            sweep.Fill = sg;
            sweep.Width = 90;
            sweep.HorizontalAlignment = HorizontalAlignment.Left;
            sweep.RenderTransform = new TranslateTransform(-120, 0);
            inner.Children.Add(sweep);

            shade.Background = T.Frozen(Colors.Black);
            shade.Opacity = 0;
            inner.Children.Add(shade);

            content.Orientation = Orientation.Horizontal;
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            content.Margin = new Thickness(26, 0, 26, 0);
            text = T.Label(label, 15, FontWeights.Bold, Colors.White);
            text.TextWrapping = TextWrapping.NoWrap;
            SetIcon(iconData);
            content.Children.Add(text);
            inner.Children.Add(content);

            MouseEnter += delegate { if (enabled) { T.Animate(shade, OpacityProperty, 0, 120); T.Animate(shadow, DropShadowEffect.BlurRadiusProperty, 32, 200); Lift(-1.5); } };
            MouseLeave += delegate { if (enabled) { T.Animate(shade, OpacityProperty, 0, 120); T.Animate(shadow, DropShadowEffect.BlurRadiusProperty, 22, 260); Lift(0); } };
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (enabled) { shade.Opacity = 0.18; CaptureMouse(); e.Handled = true; } };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                bool inside = IsMouseCaptured && IsMouseOver;
                ReleaseMouseCapture();
                shade.Opacity = 0;
                if (enabled && inside && Click != null)
                {
                    e.Handled = true;
                    Click();
                }
            };
            RenderTransform = new TranslateTransform();
            Loaded += delegate { StartLoops(); };
        }

        private void Lift(double y)
        {
            T.Animate((TranslateTransform)RenderTransform, TranslateTransform.YProperty, y, 160);
        }

        private void StartLoops()
        {
            if (T.Calm)
            {
                return;
            }
            var a = new DoubleAnimationUsingKeyFrames();
            a.Duration = new Duration(TimeSpan.FromSeconds(3.6));
            a.RepeatBehavior = RepeatBehavior.Forever;
            a.KeyFrames.Add(new LinearDoubleKeyFrame(-120, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(-120, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.6))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(Math.Max(260, ActualWidth + 40), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.6)), T.EaseOut()));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(Math.Max(260, ActualWidth + 40), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.6))));
            ((TranslateTransform)sweep.RenderTransform).BeginAnimation(TranslateTransform.XProperty, a);
            var b = new DoubleAnimation(0.35, 0.7, new Duration(TimeSpan.FromSeconds(1.5)));
            b.AutoReverse = true;
            b.RepeatBehavior = RepeatBehavior.Forever;
            var sine = new SineEase();
            sine.EasingMode = EasingMode.EaseInOut;
            b.EasingFunction = sine;
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, b);
        }

        public void SetIcon(string data)
        {
            if (icon != null)
            {
                content.Children.Remove(icon);
                icon = null;
            }
            if (data != null)
            {
                icon = Icons.Of(data, 16, Colors.White, 2.2);
                icon.Margin = new Thickness(0, 0, 10, 0);
                content.Children.Insert(0, icon);
            }
        }

        public string Text
        {
            get { return text.Text; }
            set { text.Text = value; }
        }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                Cursor = value ? Cursors.Hand : Cursors.Arrow;
                body.Background = value ? flow : T.Frozen(T.Surface2);
                text.Foreground = T.Frozen(value ? Colors.White : T.Muted);
                if (icon != null)
                {
                    icon.Stroke = T.Frozen(value ? Colors.White : T.Muted);
                }
                glow.Visibility = value ? Visibility.Visible : Visibility.Hidden;
                sweep.Visibility = value ? Visibility.Visible : Visibility.Hidden;
            }
        }

        /// Red variant for destructive actions (uninstall).
        public void Danger()
        {
            var g = new LinearGradientBrush(T.Fail, T.Mix(T.Fail, Color.FromRgb(0xC0, 0x26, 0x6D), 0.6), 0);
            g.Freeze();
            flow = g;
            body.Background = g;
            glow.Background = T.Frozen(T.Fail);
            shadow.Color = T.Fail;
        }
    }

    /// Calm secondary button (surface colour, border).
    internal sealed class SecondaryButton : Border
    {
        private readonly SolidColorBrush bg = T.B(T.Surface2);
        private readonly SolidColorBrush line = T.B(T.Border);
        private readonly TextBlock text;
        private bool enabled = true;
        public event Action Click;

        public SecondaryButton(string label, string iconData)
        {
            Height = 46;
            Padding = new Thickness(20, 0, 20, 0);
            CornerRadius = new CornerRadius(12);
            Background = bg;
            BorderBrush = line;
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.VerticalAlignment = VerticalAlignment.Center;
            row.HorizontalAlignment = HorizontalAlignment.Center;
            if (iconData != null)
            {
                Path p = Icons.Of(iconData, 15, T.Text, 2);
                p.Margin = new Thickness(0, 0, 9, 0);
                row.Children.Add(p);
            }
            text = T.Label(label, 14, FontWeights.SemiBold, T.Text);
            text.TextWrapping = TextWrapping.NoWrap;
            row.Children.Add(text);
            Child = row;
            MouseEnter += delegate { if (enabled) { T.Fade(bg, T.Mix(T.Surface2, T.Accent, 0.16), 140); T.Fade(line, T.Mix(T.Border, T.Accent, 0.5), 140); } };
            MouseLeave += delegate { T.Fade(bg, T.Surface2, 200); T.Fade(line, T.Border, 200); };
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (enabled) { T.Fade(bg, T.Mix(T.Surface2, T.Accent, 0.3), 60); CaptureMouse(); e.Handled = true; } };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                bool inside = IsMouseCaptured && IsMouseOver;
                ReleaseMouseCapture();
                T.Fade(bg, inside ? T.Mix(T.Surface2, T.Accent, 0.16) : T.Surface2, 120);
                if (enabled && inside && Click != null)
                {
                    e.Handled = true;
                    Click();
                }
            };
        }

        public string Text
        {
            get { return text.Text; }
            set { text.Text = value; }
        }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                Opacity = value ? 1 : 0.45;
                Cursor = value ? Cursors.Hand : Cursors.Arrow;
            }
        }
    }

    /// Text link (muted, brighter on hover).
    internal sealed class LinkText : TextBlock
    {
        public event Action Click;

        public LinkText(string label, Color color)
        {
            Text = label;
            FontFamily = T.Font;
            FontSize = 13;
            FontWeight = FontWeights.SemiBold;
            var brush = T.B(color);
            Foreground = brush;
            Cursor = Cursors.Hand;
            MouseEnter += delegate { T.Fade(brush, T.Mix(color, Colors.White, 0.45), 120); TextDecorations = System.Windows.TextDecorations.Underline; };
            MouseLeave += delegate { T.Fade(brush, color, 180); TextDecorations = null; };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { if (Click != null) { e.Handled = true; Click(); } };
        }
    }

    /// Switch like the launcher's ToggleSwitch: sliding knob, accent track.
    internal sealed class Toggle : Border
    {
        private readonly SolidColorBrush track = T.B(T.Surface2);
        private readonly SolidColorBrush edge = T.B(T.Border);
        private readonly Ellipse knob = new Ellipse();
        private readonly TranslateTransform move = new TranslateTransform();
        private bool on;
        public event Action<bool> Changed;

        public Toggle(bool value)
        {
            Width = 42;
            Height = 24;
            CornerRadius = new CornerRadius(12);
            Background = track;
            BorderBrush = edge;
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            knob.Width = 16;
            knob.Height = 16;
            knob.Fill = T.Frozen(Colors.White);
            knob.HorizontalAlignment = HorizontalAlignment.Left;
            knob.Margin = new Thickness(3, 0, 0, 0);
            knob.RenderTransform = move;
            var ds = new DropShadowEffect();
            ds.BlurRadius = 6;
            ds.ShadowDepth = 1;
            ds.Opacity = 0.4;
            knob.Effect = ds;
            Child = knob;
            Set(value, false);
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Value = !on;
            };
        }

        private void Set(bool value, bool animate)
        {
            on = value;
            double ms = animate ? 200 : 1;
            T.Animate(move, TranslateTransform.XProperty, value ? 18 : 0, ms);
            T.Fade(track, value ? T.Accent : T.Surface2, ms);
            T.Fade(edge, value ? T.AccentHover : T.Border, ms);
            knob.Fill = T.Frozen(value ? Colors.White : T.Muted);
        }

        public bool Value
        {
            get { return on; }
            set
            {
                if (value == on)
                {
                    return;
                }
                Set(value, true);
                if (Changed != null)
                {
                    Changed(value);
                }
            }
        }
    }

    /// Rotating arc (busy indicator).
    internal sealed class Spinner : Canvas
    {
        public Spinner(double size, Color color)
        {
            Width = size;
            Height = size;
            var p = new Path();
            double r = size / 2 - 1.5;
            p.Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "M{0},{1} A{2},{2} 0 1 1 {3},{4}", size / 2, 1.5, r, 1.5, size / 2));
            p.Stroke = T.Frozen(color);
            p.StrokeThickness = 2.4;
            p.StrokeStartLineCap = PenLineCap.Round;
            p.StrokeEndLineCap = PenLineCap.Round;
            Children.Add(p);
            var rt = new RotateTransform(0, size / 2, size / 2);
            RenderTransform = rt;
            var a = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.9)));
            a.RepeatBehavior = RepeatBehavior.Forever;
            rt.BeginAnimation(RotateTransform.AngleProperty, a);
        }
    }

    /// Progress bar with the flowing accent gradient and moving stripes (launcher PlayButton progress).
    internal sealed class FlowBar : Grid
    {
        private readonly Border fill = new Border();
        private readonly ScaleTransform scale = new ScaleTransform(0, 1);
        private double value;

        public FlowBar()
        {
            Height = 10;
            var track = new Border();
            track.CornerRadius = new CornerRadius(5);
            track.Background = T.Frozen(T.Surface2);
            track.BorderBrush = T.Frozen(T.Border);
            track.BorderThickness = new Thickness(1);
            Children.Add(track);
            fill.CornerRadius = new CornerRadius(5);
            fill.Background = T.Flow();
            fill.RenderTransform = scale;
            var stripes = new Rectangle();
            var vb = new VisualBrush();
            var tile = new Canvas();
            tile.Width = 20;
            tile.Height = 20;
            var poly = new Polygon();
            poly.Points = new PointCollection(new[] { new Point(0, 20), new Point(8, 20), new Point(20, 8), new Point(20, 0), new Point(12, 0), new Point(0, 12) });
            poly.Fill = T.Frozen(T.A(Colors.White, 40));
            tile.Children.Add(poly);
            vb.Visual = tile;
            vb.TileMode = TileMode.Tile;
            vb.Viewport = new Rect(0, 0, 20, 20);
            vb.ViewportUnits = BrushMappingMode.Absolute;
            vb.Viewbox = new Rect(0, 0, 20, 20);
            vb.ViewboxUnits = BrushMappingMode.Absolute;
            var move = new TranslateTransform();
            vb.Transform = move;
            if (!T.Calm)
            {
                var a = new DoubleAnimation(0, 20, new Duration(TimeSpan.FromSeconds(0.7)));
                a.RepeatBehavior = RepeatBehavior.Forever;
                move.BeginAnimation(TranslateTransform.XProperty, a);
            }
            stripes.Fill = vb;
            stripes.RadiusX = 5;
            stripes.RadiusY = 5;
            var g = new Grid();
            g.Children.Add(stripes);
            fill.Child = g;
            fill.ClipToBounds = true;
            Children.Add(fill);
        }

        public double Value
        {
            get { return value; }
            set
            {
                this.value = Math.Max(0, Math.Min(1, value));
                T.Animate(scale, ScaleTransform.ScaleXProperty, this.value, 260);
            }
        }
    }

    internal static class Res
    {
        public static Stream Open(string name)
        {
            return typeof(Res).Assembly.GetManifestResourceStream(name);
        }

        public static string Text(string name)
        {
            using (Stream s = Open(name))
            {
                if (s == null)
                {
                    return null;
                }
                using (var r = new StreamReader(s, System.Text.Encoding.UTF8))
                {
                    return r.ReadToEnd();
                }
            }
        }

        public static System.Windows.Media.Imaging.BitmapImage Image(string name)
        {
            Stream s = Open(name);
            if (s == null)
            {
                return null;
            }
            var img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit();
            img.StreamSource = s;
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            s.Dispose();
            return img;
        }
    }
}
