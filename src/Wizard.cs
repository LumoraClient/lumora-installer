// Lumora Setup - the wizard window: animated scene, step rail on the left, content card on the right with sliding
// pages. Install mode: Willkommen, Versionen, Speicherorte, Design, Installation, Fertig. Uninstall mode: Bestätigen,
// Entfernen, Fertig.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using IOPath = System.IO.Path;
using Path = System.Windows.Shapes.Path;

namespace Lumora.Setup
{
    internal sealed class Wizard : Window
    {
        private const double CardWidth = 640;

        private readonly bool uninstall;
        private readonly Plan plan = new Plan();
        private readonly Existing existing;
        private readonly List<Version> versions;
        private readonly string recommended;
        private readonly string[] stepNames;

        private readonly Grid root = new Grid();
        private readonly Grid pageHost = new Grid();
        private readonly StackPanel stepList = new StackPanel();
        private readonly Border stepPill = new Border();
        private readonly TranslateTransform pillMove = new TranslateTransform();
        private readonly Grid overlay = new Grid();
        private readonly Canvas fx = new Canvas();
        private PrimaryButton next;
        private SecondaryButton back;
        private SecondaryButton skip;
        private LinkText cancel;
        private int page = -1;
        private bool busy;
        private bool finished;

        // install state
        private volatile bool skipDownloads;
        private readonly List<string> notDownloaded = new List<string>();
        private string failure;
        private bool failureRetry;
        private Action<double, string> installProgress;
        private Action<int, int, string> installRow;
        private Action<string, bool> installError;
        // uninstall state
        private string appDir;
        private bool purge;

        public Wizard(bool uninstall, string uninstallDir)
        {
            this.uninstall = uninstall;
            existing = Engine.Detect();
            versions = Engine.Versions(out recommended);
            appDir = uninstallDir;
            stepNames = uninstall
                ? new[] { "Bestätigen", "Entfernen", "Fertig" }
                : new[] { "Willkommen", "Versionen", "Speicherorte", "Design", "Installation", "Fertig" };
            Defaults();

            Title = uninstall ? "Lumora Client deinstallieren" : "Lumora Client Setup";
            Width = 1000;
            Height = 680;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = T.Frozen(T.Bg);
            FontFamily = T.Font;
            UseLayoutRounding = true;
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            var chrome = new System.Windows.Shell.WindowChrome();
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(0);
            chrome.GlassFrameThickness = new Thickness(0, 0, 0, 1);
            chrome.UseAeroCaptionButtons = false;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
            try
            {
                using (Stream s = Res.Open("lumora.ico"))
                {
                    if (s != null)
                    {
                        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(s, System.Windows.Media.Imaging.BitmapCreateOptions.None,
                            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    }
                }
            }
            catch (Exception)
            {
            }

            Build();
            Content = root;
            SourceInitialized += delegate { Native.StyleWindow(new WindowInteropHelper(this).Handle); };
            Loaded += delegate { Go(0, 1); };
            Closing += OnClosing;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape && overlay.Children.Count > 0 && !busy)
                {
                    CloseOverlay();
                }
            };
        }

        /// Development aid (--ui-shot): shows a page off screen, waits for the entrance animations and saves a PNG.
        public void Shot(string file, int index, double scale)
        {
            Left = -20000;
            Top = 0;
            ShowInTaskbar = false;
            Loaded += delegate
            {
                if (index > 0)
                {
                    if (!uninstall && index == 5)
                    {
                        notDownloaded.Add("1.8.9");
                    }
                    if (!uninstall && index == 4)
                    {
                        plan.Profiles = new List<string> { recommended, "1.8.9" };
                    }
                    Go(index, 1);
                    if (index == 4 || (uninstall && index == 1))
                    {
                        installRow(0, 2, null);
                        installRow(1, 2, null);
                        installRow(2, uninstall ? 1 : 2, null);
                        if (!uninstall)
                        {
                            installRow(3, 1, "1/2 · Minecraft 26.1.2");
                            skip.Visibility = Visibility.Visible;
                        }
                        installProgress(0.46, "Assets werden geladen… 812 von 3410 Dateien");
                    }
                }
                var t = new System.Windows.Threading.DispatcherTimer();
                t.Interval = TimeSpan.FromMilliseconds(1600);
                t.Tick += delegate
                {
                    t.Stop();
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(ActualWidth * scale), (int)(ActualHeight * scale),
                        96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bmp.Render(root);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using (var f = File.Create(file))
                    {
                        enc.Save(f);
                    }
                    finished = true;
                    Close();
                };
                t.Start();
            };
        }

        private void Defaults()
        {
            plan.AppDir = existing.AppDir ?? Engine.DefaultAppDir;
            plan.DataDir = existing.DataDir ?? Engine.DefaultDataDir;
            plan.Profiles = existing.Profiles != null
                ? existing.Profiles.Where(p => versions.Any(v => v.Id == p)).ToList()
                : new List<string>();
            if (plan.Profiles.Count == 0 && recommended != null)
            {
                plan.Profiles.Add(recommended);
            }
            plan.Theme = existing.Theme == "vanilla" ? "vanilla" : "lumora";
            plan.Desktop = existing.Desktop ?? true;
            plan.StartMenu = existing.StartMenu ?? true;
            plan.Autostart = existing.Autostart;
            if (uninstall)
            {
                appDir = appDir ?? existing.AppDir ?? Engine.DefaultAppDir;
            }
        }

        // ================================================================== frame

        private void Build()
        {
            root.Children.Add(new Scene());

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardWidth) });
            root.Children.Add(layout);

            // drag the window anywhere outside the controls
            root.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed && !e.Handled)
                {
                    try
                    {
                        DragMove();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            };

            layout.Children.Add(Sidebar());

            var cardHost = new Grid();
            cardHost.Margin = new Thickness(0, 26, 26, 26);
            Grid.SetColumn(cardHost, 1);
            var shadow = new Border();
            shadow.CornerRadius = new CornerRadius(20);
            shadow.Background = T.Frozen(T.Bg);
            var ds = new DropShadowEffect();
            ds.BlurRadius = 46;
            ds.ShadowDepth = 10;
            ds.Direction = 270;
            ds.Opacity = 0.55;
            ds.Color = Colors.Black;
            shadow.Effect = ds;
            cardHost.Children.Add(shadow);

            var card = new Border();
            card.CornerRadius = new CornerRadius(20);
            card.Background = T.Frozen(T.A(T.Surface, 238));
            card.BorderBrush = T.Frozen(T.A(T.AccentHover, 44));
            card.BorderThickness = new Thickness(1);
            card.ClipToBounds = true;
            cardHost.Children.Add(card);

            var inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.Child = inner;
            // soft accent light in the top corner of the card
            var light = new Border();
            var lb = new RadialGradientBrush(T.A(T.Accent, 34), T.A(T.Accent, 0));
            lb.Center = new Point(1, 0);
            lb.GradientOrigin = new Point(1, 0);
            lb.RadiusX = 0.8;
            lb.RadiusY = 0.6;
            lb.Freeze();
            light.Background = lb;
            light.IsHitTestVisible = false;
            Grid.SetRowSpan(light, 2);
            inner.Children.Add(light);

            pageHost.Margin = new Thickness(38, 34, 38, 8);
            pageHost.ClipToBounds = false;
            inner.Children.Add(pageHost);

            var footer = new Grid();
            footer.Margin = new Thickness(38, 6, 38, 28);
            Grid.SetRow(footer, 1);
            var left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            cancel = new LinkText("Abbrechen", T.Muted);
            cancel.VerticalAlignment = VerticalAlignment.Center;
            cancel.Click += delegate { Close(); };
            left.Children.Add(cancel);
            skip = new SecondaryButton("Download überspringen", null);
            skip.Height = 40;
            skip.Visibility = Visibility.Collapsed;
            skip.Click += delegate
            {
                skipDownloads = true;
                skip.Enabled = false;
                skip.Text = "Wird übersprungen…";
            };
            left.Children.Add(skip);
            footer.Children.Add(left);
            var right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            back = new SecondaryButton("Zurück", Icons.Back);
            back.Margin = new Thickness(0, 0, 12, 0);
            back.Click += delegate { if (!busy && page > 0) { Go(page - 1, -1); } };
            next = new PrimaryButton("Weiter", null);
            next.Click += Next;
            right.Children.Add(back);
            right.Children.Add(next);
            footer.Children.Add(right);
            inner.Children.Add(footer);
            layout.Children.Add(cardHost);

            fx.IsHitTestVisible = false;
            root.Children.Add(fx);
            root.Children.Add(TitleButtons());
            overlay.Visibility = Visibility.Collapsed;
            root.Children.Add(overlay);
        }

        private FrameworkElement Sidebar()
        {
            var col = new Grid();
            col.Margin = new Thickness(40, 40, 24, 30);
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var brand = new StackPanel();
            var logo = new Image();
            logo.Source = Res.Image("logo.png");
            logo.Width = 68;
            logo.Height = 68;
            logo.HorizontalAlignment = HorizontalAlignment.Left;
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            var glow = new DropShadowEffect();
            glow.Color = T.Accent;
            glow.BlurRadius = 34;
            glow.ShadowDepth = 0;
            glow.Opacity = 0.75;
            logo.Effect = glow;
            var floatMove = new TranslateTransform();
            logo.RenderTransform = floatMove;
            if (!T.Calm)
            {
                var a = new DoubleAnimation(-3, 3, new Duration(TimeSpan.FromSeconds(2.8)));
                a.AutoReverse = true;
                a.RepeatBehavior = RepeatBehavior.Forever;
                var ease = new SineEase();
                ease.EasingMode = EasingMode.EaseInOut;
                a.EasingFunction = ease;
                floatMove.BeginAnimation(TranslateTransform.YProperty, a);
                var g = new DoubleAnimation(0.45, 0.85, new Duration(TimeSpan.FromSeconds(2.8)));
                g.AutoReverse = true;
                g.RepeatBehavior = RepeatBehavior.Forever;
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, g);
            }
            brand.Children.Add(logo);
            TextBlock word = T.Label("LUMORA", 34, FontWeights.Bold, Colors.White);
            word.FontStyle = FontStyles.Italic;
            word.Margin = new Thickness(0, 14, 0, 0);
            var wg = new LinearGradientBrush();
            wg.StartPoint = new Point(0, 0);
            wg.EndPoint = new Point(1, 1);
            wg.GradientStops.Add(new GradientStop(Colors.White, 0));
            wg.GradientStops.Add(new GradientStop(T.AccentText, 0.55));
            wg.GradientStops.Add(new GradientStop(T.AccentHover, 1));
            wg.Freeze();
            word.Foreground = wg;
            var wds = new DropShadowEffect();
            wds.Color = T.Accent;
            wds.BlurRadius = 18;
            wds.ShadowDepth = 0;
            wds.Opacity = 0.6;
            word.Effect = wds;
            brand.Children.Add(word);
            TextBlock tag = T.Label(uninstall ? "Deinstallation" : "Play. Optimize. Connect.", 13, FontWeights.Normal, T.Muted);
            tag.Margin = new Thickness(1, 2, 0, 0);
            brand.Children.Add(tag);
            col.Children.Add(brand);

            var steps = new Grid();
            steps.Margin = new Thickness(-12, 40, 0, 0);
            Grid.SetRow(steps, 1);
            stepPill.Height = 44;
            stepPill.VerticalAlignment = VerticalAlignment.Top;
            stepPill.CornerRadius = new CornerRadius(12);
            var pg = new LinearGradientBrush(T.A(T.Accent, 70), T.A(T.Accent, 18), 0);
            pg.Freeze();
            stepPill.Background = pg;
            stepPill.BorderBrush = T.Frozen(T.A(T.AccentHover, 80));
            stepPill.BorderThickness = new Thickness(1);
            stepPill.RenderTransform = pillMove;
            stepPill.Opacity = 0;
            steps.Children.Add(stepPill);
            for (int i = 0; i < stepNames.Length; i++)
            {
                stepList.Children.Add(StepRow(i));
            }
            steps.Children.Add(stepList);
            col.Children.Add(steps);

            var foot = new StackPanel();
            Grid.SetRow(foot, 2);
            foot.Children.Add(T.Label("Version " + Engine.Version, 12, FontWeights.SemiBold, T.Mix(T.Muted, T.Bg, 0.1)));
            foot.Children.Add(T.Label("Ohne Adminrechte · nur für dein Benutzerkonto", 11.5, FontWeights.Normal, T.Mix(T.Muted, T.Bg, 0.3)));
            col.Children.Add(foot);
            return col;
        }

        private FrameworkElement StepRow(int i)
        {
            var row = new Grid();
            row.Height = 44;
            row.Margin = new Thickness(12, 0, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var dot = new Border();
            dot.Width = 26;
            dot.Height = 26;
            dot.CornerRadius = new CornerRadius(13);
            dot.HorizontalAlignment = HorizontalAlignment.Left;
            dot.BorderThickness = new Thickness(1.5);
            row.Children.Add(dot);
            TextBlock label = T.Label(stepNames[i], 14, FontWeights.SemiBold, T.Muted);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            row.Tag = new object[] { dot, label };
            return row;
        }

        private void UpdateSteps()
        {
            for (int i = 0; i < stepList.Children.Count; i++)
            {
                var parts = (object[])((FrameworkElement)stepList.Children[i]).Tag;
                var dot = (Border)parts[0];
                var label = (TextBlock)parts[1];
                bool done = i < page || (finished && i == page);
                bool active = i == page && !done;
                dot.Background = T.Frozen(done ? T.Accent : active ? T.A(T.Accent, 50) : T.A(T.Surface2, 160));
                dot.BorderBrush = T.Frozen(done || active ? T.AccentHover : T.Border);
                if (done)
                {
                    Path check = Icons.Of(Icons.Check, 13, Colors.White, 2.6);
                    dot.Child = check;
                    if (i == page - 1)
                    {
                        var sc = new ScaleTransform(0.2, 0.2, 6.5, 6.5);
                        check.RenderTransform = sc;
                        var e = new BackEase();
                        e.Amplitude = 0.6;
                        e.EasingMode = EasingMode.EaseOut;
                        var a = new DoubleAnimation(1, T.Ms(380));
                        a.EasingFunction = e;
                        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                        sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                    }
                }
                else
                {
                    TextBlock n = T.Label((i + 1).ToString(), 12, FontWeights.Bold, active ? Colors.White : T.Muted);
                    n.HorizontalAlignment = HorizontalAlignment.Center;
                    n.VerticalAlignment = VerticalAlignment.Center;
                    dot.Child = n;
                }
                label.Foreground = T.Frozen(active || done ? T.Text : T.Muted);
            }
            T.Animate(pillMove, TranslateTransform.YProperty, page * 44, 420);
            T.Animate(stepPill, OpacityProperty, 1, 300);
        }

        private FrameworkElement TitleButtons()
        {
            var bar = new StackPanel();
            bar.Orientation = Orientation.Horizontal;
            bar.HorizontalAlignment = HorizontalAlignment.Right;
            bar.VerticalAlignment = VerticalAlignment.Top;
            bar.Margin = new Thickness(0, 30, 34, 0);
            bar.Children.Add(TitleButton(Icons.Minus, false, delegate { WindowState = WindowState.Minimized; }));
            bar.Children.Add(TitleButton(Icons.Close, true, delegate { Close(); }));
            return bar;
        }

        private FrameworkElement TitleButton(string icon, bool close, Action click)
        {
            var b = new Border();
            b.Width = 32;
            b.Height = 30;
            b.CornerRadius = new CornerRadius(8);
            var bg = T.B(T.A(T.Surface2, 0));
            b.Background = bg;
            b.Margin = new Thickness(4, 0, 0, 0);
            b.Cursor = Cursors.Hand;
            b.Child = Icons.Of(icon, 12, T.Muted, 1.8);
            b.MouseEnter += delegate { T.Fade(bg, close ? T.A(T.Fail, 200) : T.A(T.Surface2, 230), 120); };
            b.MouseLeave += delegate { T.Fade(bg, T.A(T.Surface2, 0), 160); };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        // ================================================================== navigation

        private void Go(int index, int direction)
        {
            FrameworkElement old = pageHost.Children.Count > 0 ? (FrameworkElement)pageHost.Children[pageHost.Children.Count - 1] : null;
            page = index;
            FrameworkElement fresh = BuildPage(index);
            var move = new TranslateTransform(direction * 46, 0);
            fresh.RenderTransform = move;
            fresh.Opacity = 0;
            pageHost.Children.Add(fresh);
            T.Animate(move, TranslateTransform.XProperty, 0, 420);
            T.Animate(fresh, OpacityProperty, 1, 320);
            if (old != null)
            {
                old.IsHitTestVisible = false;
                var om = new TranslateTransform();
                old.RenderTransform = om;
                T.Animate(om, TranslateTransform.XProperty, -direction * 46, 300);
                var fade = new DoubleAnimation(0, T.Ms(220));
                fade.Completed += delegate { pageHost.Children.Remove(old); };
                old.BeginAnimation(OpacityProperty, fade);
            }
            UpdateSteps();
            UpdateFooter();
        }

        private FrameworkElement BuildPage(int index)
        {
            if (uninstall)
            {
                switch (index)
                {
                    case 0: return ConfirmPage();
                    case 1: return ProgressPage(Engine.UninstallSteps, "Lumora wird entfernt", "Einen Moment, gleich ist alles weg.");
                    default: return RemovedPage();
                }
            }
            switch (index)
            {
                case 0: return WelcomePage();
                case 1: return VersionsPage();
                case 2: return FoldersPage();
                case 3: return DesignPage();
                case 4: return ProgressPage(InstallRows(), "Lumora wird installiert", "Das dauert nur einen Moment.");
                default: return DonePage();
            }
        }

        private void UpdateFooter()
        {
            int last = stepNames.Length - 1;
            bool progress = page == last - 1;
            back.Visibility = page > 0 && !progress && page != last ? Visibility.Visible : Visibility.Collapsed;
            cancel.Visibility = page < last - 1 ? Visibility.Visible : Visibility.Collapsed;
            skip.Visibility = Visibility.Collapsed;
            next.Enabled = true;
            next.Visibility = Visibility.Visible;
            if (uninstall)
            {
                if (page == 0)
                {
                    next.Text = "Deinstallieren";
                    next.SetIcon(Icons.Trash);
                    next.Danger();
                }
                else if (page == 1)
                {
                    next.Text = "Entferne…";
                    next.SetIcon(null);
                    next.Enabled = false;
                }
                else
                {
                    next.Text = "Schließen";
                    next.SetIcon(null);
                }
                return;
            }
            switch (page)
            {
                case 0:
                    next.Text = existing.Version != null ? "Aktualisieren" : "Los geht's";
                    next.SetIcon(Icons.Arrow);
                    break;
                case 1:
                case 2:
                    next.Text = "Weiter";
                    next.SetIcon(Icons.Arrow);
                    next.Enabled = page != 1 || plan.Profiles.Count > 0;
                    break;
                case 3:
                    next.Text = "Installieren";
                    next.SetIcon(Icons.Download);
                    break;
                case 4:
                    next.Text = "Installiere…";
                    next.SetIcon(null);
                    next.Enabled = false;
                    break;
                default:
                    next.Text = plan.Start ? "Lumora starten" : "Fertig";
                    next.SetIcon(plan.Start ? Icons.Play : null);
                    break;
            }
        }

        private void Next()
        {
            if (busy)
            {
                return;
            }
            int last = stepNames.Length - 1;
            if (page == last)
            {
                if (!uninstall && plan.Start)
                {
                    try
                    {
                        Engine.StartLauncher(plan.AppDir);
                    }
                    catch (Exception e)
                    {
                        Dialog("Start fehlgeschlagen", e.Message, "OK", null, null);
                        return;
                    }
                }
                finished = true;
                Close();
                return;
            }
            if (page == last - 1)
            {
                // retry after an error
                if (uninstall)
                {
                    Go(page, 1);
                    RunUninstall();
                }
                else
                {
                    Go(page, 1);
                    RunInstall();
                }
                return;
            }
            if (!uninstall && page == 2 && !CheckFolders())
            {
                return;
            }
            Go(page + 1, 1);
            if (page == last - 1)
            {
                if (uninstall)
                {
                    RunUninstall();
                }
                else
                {
                    RunInstall();
                }
            }
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (finished)
            {
                return;
            }
            int last = stepNames.Length - 1;
            if (page == last)
            {
                return;
            }
            if (busy)
            {
                e.Cancel = true;
                if (!uninstall && installDownloading)
                {
                    Dialog("Download abbrechen?", "Lumora ist schon installiert. Fehlende Versionen lädt der Launcher beim ersten Start.",
                        "Weiter laden", "Abbrechen und schließen", delegate
                        {
                            skipDownloads = true;
                            closeAfterInstall = true;
                        });
                }
                return;
            }
            if (failure != null || page == last - 1)
            {
                // after an error, or installed and about to show "Fertig"
                return;
            }
            e.Cancel = true;
            Dialog(uninstall ? "Deinstallation abbrechen?" : "Setup beenden?",
                uninstall ? "Lumora bleibt installiert." : "Lumora wurde noch nicht installiert. Du kannst das Setup jederzeit wieder starten.",
                uninstall ? "Zurück" : "Weiter einrichten", "Beenden", delegate
                {
                    finished = true;
                    Close();
                });
        }

        // ================================================================== page helpers

        private static StackPanel Column()
        {
            var s = new StackPanel();
            s.Orientation = Orientation.Vertical;
            return s;
        }

        private static void Header(Panel p, string title, string sub)
        {
            TextBlock t = T.Label(title, 27, FontWeights.Bold, T.Text);
            p.Children.Add(t);
            TextBlock s = T.Label(sub, 14, FontWeights.Normal, T.Muted);
            s.Margin = new Thickness(0, 7, 0, 22);
            s.LineHeight = 21;
            p.Children.Add(s);
            Stagger(t, 0);
            Stagger(s, 1);
        }

        /// Entrance animation: fade and rise, one element after the other.
        private static void Stagger(UIElement e, int i)
        {
            if (T.Calm)
            {
                return;
            }
            var move = new TranslateTransform(0, 14);
            e.RenderTransform = move;
            e.Opacity = 0;
            var a = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(460)));
            a.BeginTime = TimeSpan.FromMilliseconds(70 + i * 55);
            a.EasingFunction = T.EaseOut();
            move.BeginAnimation(TranslateTransform.YProperty, a);
            var o = new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(380)));
            o.BeginTime = a.BeginTime;
            e.BeginAnimation(OpacityProperty, o);
        }

        private static Border Chip(string text, Color color)
        {
            var b = new Border();
            b.CornerRadius = new CornerRadius(999);
            b.Background = T.Frozen(T.A(color, 0x2A));
            b.BorderBrush = T.Frozen(T.A(color, 0x60));
            b.BorderThickness = new Thickness(1);
            b.Padding = new Thickness(11, 4, 11, 5);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            TextBlock t = T.Label(text, 12, FontWeights.Bold, T.Mix(color, Colors.White, 0.45));
            t.TextWrapping = TextWrapping.NoWrap;
            b.Child = t;
            return b;
        }

        /// Row with icon tile, title, description and a control on the right.
        private static Grid OptionRow(string icon, Color color, string title, string desc, FrameworkElement control)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border tile = Icons.Tile(icon, color);
            tile.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(tile);
            StackPanel text = Column();
            text.Margin = new Thickness(14, 0, 14, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(T.Label(title, 14, FontWeights.SemiBold, T.Text));
            if (desc != null)
            {
                TextBlock d = T.Label(desc, 12.5, FontWeights.Normal, T.Muted);
                d.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(d);
            }
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            if (control != null)
            {
                control.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 2);
                g.Children.Add(control);
            }
            return g;
        }

        private static Border Panel(UIElement child)
        {
            var b = new Border();
            b.CornerRadius = new CornerRadius(14);
            b.Background = T.Frozen(T.A(T.Surface2, 150));
            b.BorderBrush = T.Frozen(T.Border);
            b.BorderThickness = new Thickness(1);
            b.Padding = new Thickness(16, 14, 16, 14);
            b.Child = child;
            return b;
        }

        // ================================================================== pages: install

        private FrameworkElement WelcomePage()
        {
            StackPanel p = Column();
            Border chip = existing.Version != null && existing.Version != "?"
                ? Chip(existing.Version == Engine.Version ? "Version " + Engine.Version + " ist schon installiert" : "Update " + existing.Version + "  →  " + Engine.Version, T.Accent)
                : existing.Version == "?" ? Chip("Vorhandene Installation wird aktualisiert", T.Accent) : Chip("Neu · Version " + Engine.Version, T.Accent2);
            chip.Margin = new Thickness(0, 0, 0, 16);
            Stagger(chip, 0);
            p.Children.Add(chip);
            Header(p, "Willkommen bei Lumora", "Dein All-in-One Minecraft Client. In vier kurzen Schritten bist du startklar, und alles lässt sich später im Launcher ändern.");
            string[][] rows =
            {
                new[] { Icons.Cube, "Deine Versionen", "Minecraft 1.8.9 bis 26.3. Wähle genau die, die du spielst." },
                new[] { Icons.Folder, "Deine Ordner", "Programm und Spieldaten dort, wo du sie haben willst." },
                new[] { Icons.Palette, "Dein Look", "Lumora Theme oder ganz klassisch im Minecraft Style." }
            };
            for (int i = 0; i < rows.Length; i++)
            {
                Border r = Panel(OptionRow(rows[i][0], i == 2 ? T.Accent2 : T.Accent, rows[i][1], rows[i][2],
                    T.Label("0" + (i + 1), 22, FontWeights.Bold, T.A(T.AccentHover, 70))));
                r.Margin = new Thickness(0, 0, 0, 10);
                Stagger(r, i + 2);
                p.Children.Add(r);
            }
            if (existing.Version != null)
            {
                TextBlock keep = T.Label("Deine Einstellungen, Welten und Anmeldung bleiben bei der Aktualisierung erhalten.", 12.5, FontWeights.SemiBold, T.Ok);
                keep.Margin = new Thickness(2, 8, 0, 0);
                Stagger(keep, 5);
                p.Children.Add(keep);
            }
            var lic = new WrapPanel();
            lic.Margin = new Thickness(2, 12, 0, 0);
            lic.Children.Add(T.Label("Lumora ist freie Software (GPL-3.0).  ", 12.5, FontWeights.Normal, T.Muted));
            var link = new LinkText("Lizenz anzeigen", T.AccentHover);
            link.FontSize = 12.5;
            link.Click += ShowLicense;
            lic.Children.Add(link);
            Stagger(lic, 6);
            p.Children.Add(lic);
            return p;
        }

        private void ShowLicense()
        {
            string text = Res.Text("LICENSE.txt") ?? "GNU General Public License v3.0 or later.";
            var box = new TextBox();
            box.Text = text;
            box.IsReadOnly = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.Background = T.Frozen(T.A(T.Bg, 160));
            box.Foreground = T.Frozen(T.Text);
            box.BorderBrush = T.Frozen(T.Border);
            box.FontFamily = new FontFamily("Consolas");
            box.FontSize = 11.5;
            box.Padding = new Thickness(10);
            box.Height = 330;
            Dialog("Lizenz", null, "Schließen", null, null, box, 640);
        }

        private FrameworkElement VersionsPage()
        {
            StackPanel p = Column();
            Header(p, "Welche Versionen willst du spielen?", "Jede Version hat ihren eigenen Spielordner. Ändern kannst du das später im Launcher.");

            var tiles = new Dictionary<string, VersionTile>();
            var count = T.Label("", 12.5, FontWeights.SemiBold, T.Muted);
            Action refresh = delegate
            {
                foreach (var kv in tiles)
                {
                    kv.Value.Checked = plan.Profiles.Contains(kv.Key);
                }
                int n = plan.Profiles.Count;
                count.Text = n == 0 ? "Keine ausgewählt" : n == 1 ? "1 Version ausgewählt" : n + " Versionen ausgewählt";
                count.Foreground = T.Frozen(n == 0 ? T.Warn : T.Muted);
                UpdateFooter();
            };

            var quick = new Grid();
            quick.Margin = new Thickness(0, 0, 0, 14);
            var chips = new StackPanel();
            chips.Orientation = Orientation.Horizontal;
            chips.Children.Add(QuickChip("Empfohlen", delegate { plan.Profiles = new List<string> { recommended }; refresh(); }));
            chips.Children.Add(QuickChip("Alle", delegate { plan.Profiles = versions.Select(v => v.Id).ToList(); refresh(); }));
            chips.Children.Add(QuickChip("Keine", delegate { plan.Profiles.Clear(); refresh(); }));
            quick.Children.Add(chips);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Center;
            quick.Children.Add(count);
            Stagger(quick, 2);
            p.Children.Add(quick);

            var groups = new List<KeyValuePair<string, List<Version>>>();
            foreach (Version v in versions)
            {
                string g = Group(v.Minecraft);
                if (groups.Count == 0 || groups[groups.Count - 1].Key != g)
                {
                    groups.Add(new KeyValuePair<string, List<Version>>(g, new List<Version>()));
                }
                groups[groups.Count - 1].Value.Add(v);
            }
            int gi = 0;
            foreach (var group in groups)
            {
                var row = new Grid();
                row.Margin = new Thickness(0, 0, 0, 6);
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                TextBlock head = T.Spaced(group.Key, T.Muted);
                head.Margin = new Thickness(0, 14, 0, 0);
                row.Children.Add(head);
                var wrap = new WrapPanel();
                Grid.SetColumn(wrap, 1);
                foreach (Version v in group.Value)
                {
                    string tag = v.Id == recommended ? "Empfohlen" : v == versions[0] ? "Neueste" : v.Minecraft == "1.8.9" ? "PvP" : null;
                    var tile = new VersionTile(v.Minecraft, tag, plan.Profiles.Contains(v.Id), v.Id == recommended);
                    string id = v.Id;
                    tile.Toggled += delegate(bool on)
                    {
                        if (on && !plan.Profiles.Contains(id))
                        {
                            plan.Profiles.Add(id);
                        }
                        else if (!on)
                        {
                            plan.Profiles.Remove(id);
                        }
                        refresh();
                    };
                    tiles[id] = tile;
                    wrap.Children.Add(tile);
                }
                row.Children.Add(wrap);
                Stagger(row, 3 + gi++);
                p.Children.Add(row);
            }

            FrameworkElement dlRow = SmallToggle(Icons.Download, "Gleich herunterladen, damit du sofort spielen kannst", plan.Download, delegate(bool on) { plan.Download = on; });
            dlRow.Margin = new Thickness(0, 14, 0, 0);
            Stagger(dlRow, 3 + gi);
            p.Children.Add(dlRow);
            refresh();
            return p;
        }

        private static string Group(string mc)
        {
            if (mc.StartsWith("26."))
            {
                return "26.x";
            }
            if (mc.StartsWith("1.21"))
            {
                return "1.21";
            }
            string[] parts = mc.Split('.');
            int minor;
            if (parts.Length > 1 && int.TryParse(parts[1], out minor) && minor >= 16)
            {
                return "1.16 – 1.20";
            }
            return "Klassiker";
        }

        private static FrameworkElement QuickChip(string text, Action click)
        {
            var b = new Border();
            b.CornerRadius = new CornerRadius(999);
            var bg = T.B(T.A(T.Surface2, 200));
            var line = T.B(T.Border);
            b.Background = bg;
            b.BorderBrush = line;
            b.BorderThickness = new Thickness(1);
            b.Padding = new Thickness(13, 5, 13, 6);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.Cursor = Cursors.Hand;
            TextBlock t = T.Label(text, 12.5, FontWeights.SemiBold, T.Text);
            t.TextWrapping = TextWrapping.NoWrap;
            b.Child = t;
            b.MouseEnter += delegate { T.Fade(bg, T.A(T.Accent, 60), 120); T.Fade(line, T.AccentHover, 120); };
            b.MouseLeave += delegate { T.Fade(bg, T.A(T.Surface2, 200), 160); T.Fade(line, T.Border, 160); };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        private FrameworkElement FoldersPage()
        {
            StackPanel p = Column();
            Header(p, "Wo soll Lumora hin?", "Beide Ordner kannst du frei wählen. Alles bleibt in deinem Benutzerkonto, Adminrechte brauchst du nicht.");
            long appSize = Engine.AppSize();
            TextBlock dataNote = T.Label("", 12.5, FontWeights.SemiBold, T.Warn);
            Action updateNote = null;
            FrameworkElement app = PathCard(Icons.Rocket, T.Accent, "Programm", delegate { return plan.AppDir; },
                delegate(string v) { plan.AppDir = v; }, Engine.DefaultAppDir,
                delegate(string dir) { return "Launcher mit eigener Java-Laufzeit · " + (appSize > 0 ? Engine.Size(appSize) : "ca. 200 MB") + Free(dir); }, null);
            app.Margin = new Thickness(0, 0, 0, 10);
            Stagger(app, 2);
            p.Children.Add(app);
            FrameworkElement data = PathCard(Icons.Folder, T.Accent2, "Spieldaten", delegate { return plan.DataDir; },
                delegate(string v) { plan.DataDir = v; if (updateNote != null) { updateNote(); } }, Engine.DefaultDataDir,
                delegate(string dir) { return "Minecraft, Welten, Screenshots und Addons" + Free(dir); }, null);
            Stagger(data, 3);
            p.Children.Add(data);
            dataNote.Margin = new Thickness(2, 8, 0, 0);
            p.Children.Add(dataNote);
            string oldData = existing.DataDir ?? (existing.HasData ? Engine.DefaultDataDir : null);
            updateNote = delegate
            {
                bool moved = oldData != null && Directory.Exists(oldData)
                    && !string.Equals(IOPath.GetFullPath(oldData).TrimEnd('\\'), IOPath.GetFullPath(plan.DataDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                dataNote.Text = moved ? "Deine bisherigen Spieldaten in " + oldData + " werden nicht verschoben." : "";
                dataNote.Visibility = moved ? Visibility.Visible : Visibility.Collapsed;
            };
            updateNote();

            StackPanel opts = Column();
            opts.Margin = new Thickness(0, 16, 0, 0);
            opts.Children.Add(SmallToggle(Icons.Monitor, "Verknüpfung auf dem Desktop", plan.Desktop, delegate(bool on) { plan.Desktop = on; }));
            opts.Children.Add(SmallToggle(Icons.Star, "Eintrag im Startmenü", plan.StartMenu, delegate(bool on) { plan.StartMenu = on; }));
            opts.Children.Add(SmallToggle(Icons.Power, "Mit Windows starten (im Tray, damit Freunde dich erreichen)", plan.Autostart, delegate(bool on) { plan.Autostart = on; }));
            Stagger(opts, 4);
            p.Children.Add(opts);
            return p;
        }

        private static string Free(string dir)
        {
            long free = Engine.FreeBytes(dir);
            if (free < 0)
            {
                return "";
            }
            string drive = IOPath.GetPathRoot(IOPath.GetFullPath(dir)).TrimEnd('\\');
            return " · " + Engine.Size(free) + " frei auf " + drive;
        }

        private FrameworkElement PathCard(string icon, Color color, string title, Func<string> get, Action<string> set, string def,
                                          Func<string, string> hint, Action changed)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Border tile = Icons.Tile(icon, color);
            tile.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(tile);
            StackPanel text = Column();
            text.Margin = new Thickness(14, 0, 12, 0);
            var head = new StackPanel();
            head.Orientation = Orientation.Horizontal;
            head.Children.Add(T.Label(title, 14, FontWeights.SemiBold, T.Text));
            var reset = new LinkText("Standard", T.AccentHover);
            reset.FontSize = 12;
            reset.Margin = new Thickness(10, 1, 0, 0);
            head.Children.Add(reset);
            text.Children.Add(head);
            TextBlock path = T.Label("", 13, FontWeights.Normal, T.AccentText);
            path.TextWrapping = TextWrapping.NoWrap;
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.Margin = new Thickness(0, 3, 0, 3);
            text.Children.Add(path);
            TextBlock info = T.Label("", 12, FontWeights.Normal, T.Muted);
            text.Children.Add(info);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            var change = new SecondaryButton("Ändern", Icons.Folder);
            change.Height = 38;
            change.Padding = new Thickness(14, 0, 14, 0);
            change.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(change, 2);
            g.Children.Add(change);
            Action show = delegate
            {
                string v = get();
                path.Text = v;
                path.ToolTip = v;
                info.Text = hint(v);
                bool isDefault = string.Equals(v.TrimEnd('\\'), def.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                reset.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
            };
            change.Click += delegate
            {
                string picked = null;
                try
                {
                    picked = Native.PickFolder(new WindowInteropHelper(this).Handle, title + " – Ordner wählen", get());
                }
                catch (Exception e)
                {
                    Dialog("Ordnerauswahl", e.Message, "OK", null, null);
                }
                if (picked != null)
                {
                    set(Engine.Normalize(picked));
                    show();
                    Pulse(path);
                    if (changed != null)
                    {
                        changed();
                    }
                }
            };
            reset.Click += delegate
            {
                set(def);
                show();
                Pulse(path);
            };
            show();
            return Panel(g);
        }

        private static void Pulse(UIElement e)
        {
            var a = new DoubleAnimation(0.2, 1, T.Ms(420));
            e.BeginAnimation(OpacityProperty, a);
        }

        private static FrameworkElement SmallToggle(string icon, string text, bool value, Action<bool> changed)
        {
            var g = new Grid();
            g.Margin = new Thickness(0, 0, 0, 10);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Path ic = Icons.Of(icon, 16, T.Muted, 1.8);
            ic.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(ic);
            TextBlock t = T.Label(text, 13.5, FontWeights.Normal, T.Text);
            t.Margin = new Thickness(12, 0, 12, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            var tg = new Toggle(value);
            tg.Changed += changed;
            Grid.SetColumn(tg, 2);
            g.Children.Add(tg);
            g.Background = Brushes.Transparent;
            g.Cursor = Cursors.Hand;
            g.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (!e.Handled)
                {
                    e.Handled = true;
                    tg.Value = !tg.Value;
                }
            };
            g.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            return g;
        }

        private bool CheckFolders()
        {
            try
            {
                string a = IOPath.GetFullPath(plan.AppDir).TrimEnd('\\');
                string d = IOPath.GetFullPath(plan.DataDir).TrimEnd('\\');
                if (string.Equals(a, d, StringComparison.OrdinalIgnoreCase))
                {
                    Dialog("Gleicher Ordner", "Programm und Spieldaten brauchen zwei verschiedene Ordner, damit ein Update nie deine Welten berührt.", "OK", null, null);
                    return false;
                }
                long free = Engine.FreeBytes(a);
                long need = Engine.AppSize() + (50L << 20);
                if (free >= 0 && free < need)
                {
                    Dialog("Zu wenig Speicherplatz", "Auf " + IOPath.GetPathRoot(a) + " sind nur " + Engine.Size(free) + " frei, Lumora braucht " + Engine.Size(need) + ".", "OK", null, null);
                    return false;
                }
            }
            catch (Exception e)
            {
                Dialog("Ungültiger Ordner", e.Message, "OK", null, null);
                return false;
            }
            return true;
        }

        private FrameworkElement DesignPage()
        {
            StackPanel p = Column();
            Header(p, "Wie soll Lumora aussehen?", "Das gilt für die Menüs im Spiel. Umstellen kannst du es jederzeit im Hub (Rechte Umschalttaste) unter Themes.");
            var cards = new Grid();
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            double w = (CardWidth - 76 - 16) / 2;
            ThemeCard lumora = new ThemeCard(Previews.Lumora(w - 2, 150), "Lumora Theme", "Empfohlen",
                "Modernes Hauptmenü, eigene Server- und Weltliste, Ladebildschirm und weiche Übergänge.", plan.Theme == "lumora");
            ThemeCard classic = new ThemeCard(Previews.Classic(w - 2, 150), "Klassisch", null,
                "Minecrafts eigene Menüs, Knöpfe und Schrift. Ganz wie gewohnt.", plan.Theme == "vanilla");
            lumora.Picked += delegate { plan.Theme = "lumora"; lumora.Selected = true; classic.Selected = false; };
            classic.Picked += delegate { plan.Theme = "vanilla"; classic.Selected = true; lumora.Selected = false; };
            cards.Children.Add(lumora);
            Grid.SetColumn(classic, 2);
            cards.Children.Add(classic);
            Stagger(lumora, 2);
            Stagger(classic, 3);
            p.Children.Add(cards);

            var hint = new StackPanel();
            hint.Orientation = Orientation.Horizontal;
            hint.Margin = new Thickness(2, 20, 0, 0);
            Path sp = Icons.Filled(Icons.Sparkle, 14, T.AccentHover);
            sp.Margin = new Thickness(0, 1, 9, 0);
            hint.Children.Add(sp);
            hint.Children.Add(T.Label("Module wie Zoom, HUD-Editor und Freunde hast du in beiden Designs.", 12.5, FontWeights.Normal, T.Muted));
            Stagger(hint, 4);
            p.Children.Add(hint);
            return p;
        }

        // ================================================================== install run

        private bool installDownloading;
        private bool closeAfterInstall;

        private List<string> InstallRows()
        {
            var rows = new List<string>(Engine.InstallSteps);
            if (plan.Download && plan.Profiles.Count > 0)
            {
                rows.Add(plan.Profiles.Count == 1 ? "Minecraft " + Mc(plan.Profiles[0]) + " herunterladen" : plan.Profiles.Count + " Versionen herunterladen");
            }
            return rows;
        }

        private string Mc(string id)
        {
            Version v = versions.FirstOrDefault(x => x.Id == id);
            return v != null ? v.Minecraft : id;
        }

        private FrameworkElement ProgressPage(IList<string> rows, string title, string sub)
        {
            StackPanel p = Column();
            Header(p, title, sub);
            var top = new Grid();
            TextBlock percent = T.Label("0 %", 40, FontWeights.Bold, T.Text);
            top.Children.Add(percent);
            TextBlock detail = T.Label("", 12.5, FontWeights.Normal, T.Muted);
            detail.TextWrapping = TextWrapping.NoWrap;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            detail.VerticalAlignment = VerticalAlignment.Bottom;
            detail.HorizontalAlignment = HorizontalAlignment.Right;
            detail.MaxWidth = 380;
            detail.Margin = new Thickness(0, 0, 0, 9);
            top.Children.Add(detail);
            p.Children.Add(top);
            var bar = new FlowBar();
            bar.Margin = new Thickness(0, 6, 0, 24);
            p.Children.Add(bar);

            var list = Column();
            var icons = new List<Border>();
            var notes = new List<TextBlock>();
            var labels = new List<TextBlock>();
            for (int i = 0; i < rows.Count; i++)
            {
                var g = new Grid();
                g.Height = 36;
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var ic = new Border();
                ic.Width = 22;
                ic.Height = 22;
                ic.CornerRadius = new CornerRadius(11);
                ic.HorizontalAlignment = HorizontalAlignment.Left;
                ic.BorderBrush = T.Frozen(T.Border);
                ic.BorderThickness = new Thickness(1.5);
                g.Children.Add(ic);
                TextBlock l = T.Label(rows[i], 14, FontWeights.SemiBold, T.Muted);
                l.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(l, 1);
                g.Children.Add(l);
                TextBlock n = T.Label("", 12, FontWeights.Normal, T.Muted);
                n.VerticalAlignment = VerticalAlignment.Center;
                n.TextWrapping = TextWrapping.NoWrap;
                n.TextTrimming = TextTrimming.CharacterEllipsis;
                n.MaxWidth = 250;
                Grid.SetColumn(n, 2);
                g.Children.Add(n);
                icons.Add(ic);
                notes.Add(n);
                labels.Add(l);
                Stagger(g, 2 + i);
                list.Children.Add(g);
            }
            p.Children.Add(list);

            var error = new Border();
            error.CornerRadius = new CornerRadius(12);
            error.Background = T.Frozen(T.A(T.Fail, 30));
            error.BorderBrush = T.Frozen(T.A(T.Fail, 110));
            error.BorderThickness = new Thickness(1);
            error.Padding = new Thickness(14, 12, 14, 12);
            error.Margin = new Thickness(0, 14, 0, 0);
            error.Visibility = Visibility.Collapsed;
            var eg = new Grid();
            eg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            eg.ColumnDefinitions.Add(new ColumnDefinition());
            Path ei = Icons.Of(Icons.Alert, 18, T.Fail, 2);
            ei.VerticalAlignment = VerticalAlignment.Top;
            eg.Children.Add(ei);
            TextBlock et = T.Label("", 13, FontWeights.SemiBold, T.Mix(T.Fail, Colors.White, 0.55));
            et.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(et, 1);
            eg.Children.Add(et);
            error.Child = eg;
            p.Children.Add(error);

            installProgress = delegate(double f, string d)
            {
                if (f >= 0)
                {
                    percent.Text = Math.Round(f * 100) + " %";
                    bar.Value = f;
                }
                if (d != null)
                {
                    detail.Text = d;
                }
            };
            // state: 0 idle, 1 active, 2 done, 3 failed, 4 skipped
            installRow = delegate(int i, int state, string note)
            {
                if (i < 0 || i >= icons.Count)
                {
                    return;
                }
                Border ic = icons[i];
                if (note != null)
                {
                    notes[i].Text = note;
                }
                labels[i].Foreground = T.Frozen(state == 0 ? T.Muted : T.Text);
                activeRow = state == 1 ? i : activeRow == i ? -1 : activeRow;
                if (state == 1)
                {
                    ic.BorderThickness = new Thickness(0);
                    ic.Background = Brushes.Transparent;
                    ic.Child = new Spinner(20, T.AccentHover);
                }
                else if (state == 2 || state == 3 || state == 4)
                {
                    Color c = state == 2 ? T.Ok : state == 3 ? T.Fail : T.Warn;
                    ic.BorderThickness = new Thickness(0);
                    ic.Background = T.Frozen(T.A(c, 50));
                    Path mark = Icons.Of(state == 2 ? Icons.Check : state == 3 ? Icons.Close : Icons.Minus, 12, c, 2.6);
                    var sc = new ScaleTransform(0.3, 0.3, 6, 6);
                    mark.RenderTransform = sc;
                    var e = new BackEase();
                    e.EasingMode = EasingMode.EaseOut;
                    var a = new DoubleAnimation(1, T.Ms(360));
                    a.EasingFunction = e;
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                    sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                    ic.Child = mark;
                }
            };
            installError = delegate(string message, bool retry)
            {
                et.Text = message;
                error.Visibility = Visibility.Visible;
                Pulse(error);
            };
            return p;
        }

        private sealed class UiProgress : IProgress
        {
            private readonly Wizard w;
            private readonly double from;
            private readonly double span;
            private int step;
            private readonly int steps;

            public UiProgress(Wizard w, double from, double span, int steps)
            {
                this.w = w;
                this.from = from;
                this.span = span;
                this.steps = steps;
            }

            public void Step(int index, string detail)
            {
                step = index;
                w.Ui(delegate
                {
                    w.installRow(index, 1, null);
                    w.installProgress(from + span * index / steps, detail);
                });
            }

            public void Done(int index, bool ok, string note)
            {
                w.Ui(delegate
                {
                    w.installRow(index, ok ? (note == "Behalten" ? 4 : 2) : 3, note);
                    w.installProgress(from + span * (index + 1) / steps, null);
                });
            }

            public void Progress(double fraction, string detail)
            {
                // only the first step (extracting) reports fine-grained progress
                double f = from + span * (step + Math.Max(0, Math.Min(1, fraction)) * 0.98) / steps;
                w.Ui(delegate { w.installProgress(f, detail); });
            }
        }

        private void Ui(Action a)
        {
            Dispatcher.BeginInvoke(a);
        }

        private void RunInstall()
        {
            busy = true;
            failure = null;
            notDownloaded.Clear();
            skipDownloads = false;
            plan.Profiles = versions.Where(v => plan.Profiles.Contains(v.Id)).Select(v => v.Id).ToList();
            plan.DefaultProfile = plan.Profiles.Contains(recommended) ? recommended : plan.Profiles.FirstOrDefault();
            bool download = plan.Download && plan.Profiles.Count > 0;
            double installSpan = download ? 0.22 : 1.0;
            var thread = new Thread(delegate()
            {
                try
                {
                    Engine.Install(plan, new UiProgress(this, 0, installSpan, Engine.InstallSteps.Length));
                }
                catch (Exception e)
                {
                    var se = e as SetupException;
                    Ui(delegate { InstallFailed(e.Message, se == null || se.Retry); });
                    return;
                }
                if (download)
                {
                    Ui(delegate
                    {
                        installDownloading = true;
                        skip.Visibility = Visibility.Visible;
                        skip.Enabled = true;
                        skip.Text = "Download überspringen";
                        installRow(3, 1, "");
                    });
                    int n = plan.Profiles.Count;
                    for (int i = 0; i < n; i++)
                    {
                        string id = plan.Profiles[i];
                        if (skipDownloads)
                        {
                            notDownloaded.Add(Mc(id));
                            continue;
                        }
                        int index = i;
                        string name = "Minecraft " + Mc(id);
                        Ui(delegate { installRow(3, 1, n > 1 ? (index + 1) + "/" + n + " · " + name : name); });
                        string err = Engine.DownloadVersion(plan.AppDir, id, delegate(double f, string detail)
                        {
                            double overall = installSpan + (1 - installSpan) * (index + Math.Max(0, Math.Min(1, f))) / n;
                            Ui(delegate { installProgress(f >= 0 ? overall : -1, detail); });
                        }, delegate { return skipDownloads; });
                        if (err != null)
                        {
                            notDownloaded.Add(Mc(id));
                        }
                    }
                    Ui(delegate
                    {
                        installDownloading = false;
                        skip.Visibility = Visibility.Collapsed;
                        bool all = notDownloaded.Count == 0;
                        installRow(3, all ? 2 : notDownloaded.Count == n ? 4 : 2,
                            all ? "Fertig" : notDownloaded.Count == n ? "Beim ersten Start" : (n - notDownloaded.Count) + " von " + n);
                    });
                }
                Ui(delegate
                {
                    installProgress(1, "Fertig");
                    busy = false;
                    finished = false;
                    if (closeAfterInstall)
                    {
                        finished = true;
                        Close();
                        return;
                    }
                    var t = new System.Windows.Threading.DispatcherTimer();
                    t.Interval = TimeSpan.FromMilliseconds(T.Calm ? 50 : 650);
                    t.Tick += delegate { t.Stop(); Go(5, 1); };
                    t.Start();
                });
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void InstallFailed(string message, bool retry)
        {
            busy = false;
            failure = message;
            failureRetry = retry;
            installError(message, retry);
            if (activeRow >= 0)
            {
                installRow(activeRow, 3, "Fehlgeschlagen");
            }
            next.Enabled = retry;
            next.Text = "Erneut versuchen";
            next.SetIcon(null);
            cancel.Visibility = Visibility.Visible;
            cancel.Text = "Schließen";
            // e.g. no write access: back to the previous pages to choose another folder
            back.Visibility = !uninstall && !retry ? Visibility.Visible : Visibility.Collapsed;
        }

        private int activeRow = -1;

        private FrameworkElement DonePage()
        {
            var p = Column();
            p.HorizontalAlignment = HorizontalAlignment.Stretch;
            FrameworkElement badge = SuccessBadge();
            badge.Margin = new Thickness(0, 6, 0, 22);
            p.Children.Add(badge);
            TextBlock t = T.Label("Alles bereit!", 30, FontWeights.Bold, T.Text);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            Stagger(t, 2);
            p.Children.Add(t);
            TextBlock s = T.Label("Lumora " + Engine.Version + " ist installiert. Viel Spaß beim Spielen!", 14, FontWeights.Normal, T.Muted);
            s.HorizontalAlignment = HorizontalAlignment.Center;
            s.TextAlignment = TextAlignment.Center;
            s.Margin = new Thickness(0, 6, 0, 18);
            Stagger(s, 3);
            p.Children.Add(s);

            var chips = new WrapPanel();
            chips.HorizontalAlignment = HorizontalAlignment.Center;
            Border c1 = Chip(plan.Profiles.Count == 1 ? "Minecraft " + Mc(plan.Profiles[0]) : plan.Profiles.Count + " Versionen", T.Accent);
            Border c2 = Chip(plan.Theme == "vanilla" ? "Klassisches Design" : "Lumora Theme", T.Accent2);
            c1.Margin = new Thickness(4);
            c2.Margin = new Thickness(4);
            chips.Children.Add(c1);
            chips.Children.Add(c2);
            Stagger(chips, 4);
            p.Children.Add(chips);

            if (notDownloaded.Count > 0)
            {
                TextBlock warn = T.Label("Noch nicht heruntergeladen: " + string.Join(", ", notDownloaded) + ". Der Launcher lädt das beim ersten Start.",
                    12.5, FontWeights.SemiBold, T.Warn);
                warn.TextAlignment = TextAlignment.Center;
                warn.Margin = new Thickness(20, 12, 20, 0);
                Stagger(warn, 5);
                p.Children.Add(warn);
            }

            FrameworkElement start = Panel(OptionRow(Icons.Play, T.Accent, "Lumora jetzt starten", "Öffnet den Launcher. Melde dich dort mit deinem Microsoft-Konto an.",
                StartToggle()));
            start.Margin = new Thickness(0, 22, 0, 0);
            Stagger(start, 6);
            p.Children.Add(start);
            if (!T.Calm)
            {
                Dispatcher.BeginInvoke(new Action(Confetti), System.Windows.Threading.DispatcherPriority.Background);
            }
            return p;
        }

        private Toggle StartToggle()
        {
            var tg = new Toggle(plan.Start);
            tg.Changed += delegate(bool on)
            {
                plan.Start = on;
                UpdateFooter();
            };
            return tg;
        }

        /// Accent circle with a check that draws itself and a ring that pulses outwards.
        private static FrameworkElement SuccessBadge()
        {
            var g = new Grid();
            g.Width = 110;
            g.Height = 110;
            g.HorizontalAlignment = HorizontalAlignment.Center;
            var ring = new Ellipse();
            ring.Width = 92;
            ring.Height = 92;
            ring.Stroke = T.Frozen(T.AccentHover);
            ring.StrokeThickness = 2;
            var rs = new ScaleTransform(1, 1, 46, 46);
            ring.RenderTransform = rs;
            ring.Opacity = 0;
            g.Children.Add(ring);
            var circle = new Ellipse();
            circle.Width = 92;
            circle.Height = 92;
            var fill = new LinearGradientBrush(T.Accent2, T.AccentDeep, 45);
            fill.Freeze();
            circle.Fill = fill;
            var glow = new DropShadowEffect();
            glow.Color = T.Accent;
            glow.BlurRadius = 40;
            glow.ShadowDepth = 0;
            glow.Opacity = 0.8;
            circle.Effect = glow;
            var cs = new ScaleTransform(0.4, 0.4, 46, 46);
            circle.RenderTransform = cs;
            g.Children.Add(circle);
            var check = new Path();
            check.Data = Geometry.Parse("M30,48 L42,60 L64,36");
            check.Stroke = T.Frozen(Colors.White);
            check.StrokeThickness = 7;
            check.StrokeStartLineCap = PenLineCap.Round;
            check.StrokeEndLineCap = PenLineCap.Round;
            check.StrokeLineJoin = PenLineJoin.Round;
            check.Width = 92;
            check.Height = 92;
            check.StrokeDashArray = new DoubleCollection(new double[] { 8, 8 });
            check.StrokeDashOffset = 8;
            g.Children.Add(check);
            ring.HorizontalAlignment = circle.HorizontalAlignment = check.HorizontalAlignment = HorizontalAlignment.Center;
            ring.VerticalAlignment = circle.VerticalAlignment = check.VerticalAlignment = VerticalAlignment.Center;

            var back = new BackEase();
            back.Amplitude = 0.5;
            back.EasingMode = EasingMode.EaseOut;
            var pop = new DoubleAnimation(1, T.Ms(520));
            pop.EasingFunction = back;
            cs.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            cs.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            var draw = new DoubleAnimation(8, 0, T.Ms(420));
            draw.BeginTime = TimeSpan.FromMilliseconds(T.Calm ? 0 : 260);
            draw.EasingFunction = T.EaseOut();
            check.BeginAnimation(Shape.StrokeDashOffsetProperty, draw);
            if (!T.Calm)
            {
                var grow = new DoubleAnimation(1, 1.7, new Duration(TimeSpan.FromSeconds(1.8)));
                grow.RepeatBehavior = RepeatBehavior.Forever;
                grow.EasingFunction = T.EaseOut();
                rs.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                rs.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                var fade = new DoubleAnimation(0.7, 0, new Duration(TimeSpan.FromSeconds(1.8)));
                fade.RepeatBehavior = RepeatBehavior.Forever;
                ring.BeginAnimation(OpacityProperty, fade);
            }
            return g;
        }

        /// Voxel sparkles bursting from the success badge (small squares in the accent colours).
        private void Confetti()
        {
            Point origin;
            try
            {
                origin = new Point(ActualWidth - 26 - CardWidth / 2, 26 + 34 + 6 + 55);
            }
            catch (Exception)
            {
                return;
            }
            var rnd = new Random();
            var parts = new List<double[]>();
            var shapes = new List<Rectangle>();
            Color[] colors = { T.Accent, T.Accent2, T.AccentHover, T.AccentText, Colors.White, T.Ok };
            for (int i = 0; i < 64; i++)
            {
                var r = new Rectangle();
                double size = 4 + rnd.Next(5);
                r.Width = size;
                r.Height = size;
                r.Fill = T.Frozen(colors[rnd.Next(colors.Length)]);
                var tg = new TransformGroup();
                tg.Children.Add(new RotateTransform(rnd.Next(90), size / 2, size / 2));
                tg.Children.Add(new TranslateTransform(origin.X, origin.Y));
                r.RenderTransform = tg;
                fx.Children.Add(r);
                shapes.Add(r);
                double angle = rnd.NextDouble() * Math.PI * 2;
                double speed = 160 + rnd.NextDouble() * 320;
                parts.Add(new[] { origin.X, origin.Y, Math.Cos(angle) * speed, Math.Sin(angle) * speed - 180, rnd.NextDouble() * 400 - 200 });
            }
            DateTime start = DateTime.Now;
            DateTime last = start;
            EventHandler tick = null;
            tick = delegate
            {
                DateTime now = DateTime.Now;
                double dt = Math.Min(0.05, (now - last).TotalSeconds);
                last = now;
                double age = (now - start).TotalSeconds;
                for (int i = 0; i < shapes.Count; i++)
                {
                    double[] q = parts[i];
                    q[3] += 520 * dt;
                    q[2] *= 1 - 0.9 * dt;
                    q[0] += q[2] * dt;
                    q[1] += q[3] * dt;
                    var tg = (TransformGroup)shapes[i].RenderTransform;
                    var rot = (RotateTransform)tg.Children[0];
                    rot.Angle += q[4] * dt;
                    var tr = (TranslateTransform)tg.Children[1];
                    tr.X = q[0];
                    tr.Y = q[1];
                    shapes[i].Opacity = Math.Max(0, 1 - age / 1.8);
                }
                if (age > 1.9)
                {
                    CompositionTarget.Rendering -= tick;
                    fx.Children.Clear();
                }
            };
            CompositionTarget.Rendering += tick;
        }

        // ================================================================== pages: uninstall

        private TextBlock purgeInfo;

        private FrameworkElement ConfirmPage()
        {
            StackPanel p = Column();
            Header(p, "Lumora deinstallieren?", "Der Launcher, die Verknüpfungen und der Eintrag in den Windows-Einstellungen werden entfernt.");
            FrameworkElement where = Panel(OptionRow(Icons.Rocket, T.Accent, "Programm", appDir, null));
            Stagger(where, 2);
            p.Children.Add(where);

            var line = T.B(T.Border);
            var danger = new Toggle(purge);
            purgeInfo = T.Label("Größe wird berechnet…", 12, FontWeights.SemiBold, T.Muted);
            string data = existing.DataDir ?? Engine.DefaultDataDir;
            Grid row = OptionRow(Icons.Trash, T.Fail, "Auch Spieldaten löschen",
                "Welten, Screenshots, Einstellungen, Addons und die Anmeldung in " + data + ". Das lässt sich nicht rückgängig machen.", danger);
            var col = Column();
            col.Children.Add(row);
            purgeInfo.Margin = new Thickness(52, 6, 0, 0);
            col.Children.Add(purgeInfo);
            Border box = Panel(col);
            box.BorderBrush = line;
            box.Margin = new Thickness(0, 12, 0, 0);
            danger.Changed += delegate(bool on)
            {
                purge = on;
                T.Fade(line, on ? T.Fail : T.Border, 200);
            };
            Stagger(box, 3);
            p.Children.Add(box);
            TextBlock keep = T.Label("Ohne diesen Schalter bleiben deine Welten erhalten, und eine neue Installation findet sie wieder.", 12.5, FontWeights.Normal, T.Muted);
            keep.Margin = new Thickness(2, 16, 0, 0);
            Stagger(keep, 4);
            p.Children.Add(keep);
            var th = new Thread(delegate()
            {
                long size = Engine.DataSize(data);
                Ui(delegate { if (purgeInfo != null) { purgeInfo.Text = size > 0 ? "Belegt: " + Engine.Size(size) : "Keine Spieldaten gefunden"; } });
            });
            th.IsBackground = true;
            th.Start();
            return p;
        }

        private void RunUninstall()
        {
            busy = true;
            failure = null;
            string data = existing.DataDir ?? Engine.DefaultDataDir;
            var thread = new Thread(delegate()
            {
                try
                {
                    Engine.Uninstall(appDir, data, purge, new UiProgress(this, 0, 1, Engine.UninstallSteps.Length));
                }
                catch (Exception e)
                {
                    var se = e as SetupException;
                    Ui(delegate { InstallFailed(e.Message, se == null || se.Retry); });
                    return;
                }
                Ui(delegate
                {
                    installProgress(1, "Fertig");
                    busy = false;
                    var t = new System.Windows.Threading.DispatcherTimer();
                    t.Interval = TimeSpan.FromMilliseconds(T.Calm ? 50 : 600);
                    t.Tick += delegate { t.Stop(); Go(2, 1); };
                    t.Start();
                });
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private FrameworkElement RemovedPage()
        {
            var p = Column();
            FrameworkElement badge = SuccessBadge();
            badge.Margin = new Thickness(0, 30, 0, 22);
            p.Children.Add(badge);
            TextBlock t = T.Label("Lumora wurde entfernt", 28, FontWeights.Bold, T.Text);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            Stagger(t, 2);
            p.Children.Add(t);
            string data = existing.DataDir ?? Engine.DefaultDataDir;
            TextBlock s = T.Label(purge ? "Programm und Spieldaten sind gelöscht. Danke, dass du Lumora ausprobiert hast."
                : "Deine Spieldaten liegen weiter in " + data + ". Danke, dass du Lumora ausprobiert hast.", 14, FontWeights.Normal, T.Muted);
            s.TextAlignment = TextAlignment.Center;
            s.Margin = new Thickness(20, 8, 20, 0);
            Stagger(s, 3);
            p.Children.Add(s);
            finished = true;
            return p;
        }

        // ================================================================== dialog overlay

        private void Dialog(string title, string message, string primary, string secondary, Action secondaryAction)
        {
            Dialog(title, message, primary, secondary, secondaryAction, null, 440);
        }

        private void Dialog(string title, string message, string primary, string secondary, Action secondaryAction, FrameworkElement extra, double width)
        {
            overlay.Children.Clear();
            overlay.Visibility = Visibility.Visible;
            var dim = new Border();
            dim.Background = T.Frozen(T.A(T.Bg, 190));
            overlay.Children.Add(dim);
            var card = new Border();
            card.Width = width;
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.CornerRadius = new CornerRadius(18);
            card.Background = T.Frozen(T.Surface);
            card.BorderBrush = T.Frozen(T.A(T.AccentHover, 60));
            card.BorderThickness = new Thickness(1);
            card.Padding = new Thickness(26, 24, 26, 22);
            var ds = new DropShadowEffect();
            ds.BlurRadius = 40;
            ds.ShadowDepth = 8;
            ds.Direction = 270;
            ds.Opacity = 0.6;
            card.Effect = ds;
            var col = Column();
            col.Children.Add(T.Label(title, 19, FontWeights.Bold, T.Text));
            if (message != null)
            {
                TextBlock m = T.Label(message, 13.5, FontWeights.Normal, T.Muted);
                m.Margin = new Thickness(0, 8, 0, 0);
                m.LineHeight = 20;
                col.Children.Add(m);
            }
            if (extra != null)
            {
                extra.Margin = new Thickness(0, 14, 0, 0);
                col.Children.Add(extra);
            }
            var buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 22, 0, 0);
            if (secondary != null)
            {
                var sb = new SecondaryButton(secondary, null);
                sb.Height = 42;
                sb.Margin = new Thickness(0, 0, 10, 0);
                sb.Click += delegate
                {
                    CloseOverlay();
                    if (secondaryAction != null)
                    {
                        secondaryAction();
                    }
                };
                buttons.Children.Add(sb);
            }
            var pb = new PrimaryButton(primary, null);
            pb.Height = 42;
            pb.MinWidth = 110;
            pb.Click += CloseOverlay;
            buttons.Children.Add(pb);
            col.Children.Add(buttons);
            card.Child = col;
            overlay.Children.Add(card);
            var sc = new ScaleTransform(0.94, 0.94);
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            card.RenderTransform = sc;
            overlay.Opacity = 0;
            T.Animate(overlay, OpacityProperty, 1, 180);
            T.Animate(sc, ScaleTransform.ScaleXProperty, 1, 260);
            T.Animate(sc, ScaleTransform.ScaleYProperty, 1, 260);
            dim.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            card.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
        }

        private void CloseOverlay()
        {
            var a = new DoubleAnimation(0, T.Ms(140));
            a.Completed += delegate
            {
                overlay.Children.Clear();
                overlay.Visibility = Visibility.Collapsed;
            };
            overlay.BeginAnimation(OpacityProperty, a);
        }
    }

    /// Toggle tile for one Minecraft version: accent border and fill when chosen, check badge pops in.
    internal sealed class VersionTile : Grid
    {
        private readonly Border body = new Border();
        private readonly SolidColorBrush bg = T.B(T.A(T.Surface2, 170));
        private readonly SolidColorBrush line = T.B(T.Border);
        private readonly Border badge = new Border();
        private readonly ScaleTransform badgeScale = new ScaleTransform(0, 0, 8, 8);
        private readonly TranslateTransform lift = new TranslateTransform();
        private bool on;
        public event Action<bool> Toggled;

        public VersionTile(string version, string tag, bool value, bool recommended)
        {
            Width = 84;
            Height = 40;
            Margin = new Thickness(0, 6, 5, 0);
            Cursor = Cursors.Hand;
            RenderTransform = lift;
            body.CornerRadius = new CornerRadius(10);
            body.Background = bg;
            body.BorderBrush = line;
            body.BorderThickness = new Thickness(1.2);
            var col = new StackPanel();
            col.VerticalAlignment = VerticalAlignment.Center;
            col.HorizontalAlignment = HorizontalAlignment.Center;
            TextBlock v = T.Label(version, 13.5, FontWeights.Bold, T.Text);
            v.HorizontalAlignment = HorizontalAlignment.Center;
            v.TextWrapping = TextWrapping.NoWrap;
            col.Children.Add(v);
            if (tag != null)
            {
                TextBlock t = T.Label(tag, 9.5, FontWeights.Bold, recommended ? T.AccentHover : T.Muted);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.Margin = new Thickness(0, -1, 0, 0);
                col.Children.Add(t);
            }
            body.Child = col;
            Children.Add(body);
            badge.Width = 16;
            badge.Height = 16;
            badge.CornerRadius = new CornerRadius(8);
            badge.Background = T.Frozen(T.Accent);
            badge.BorderBrush = T.Frozen(T.Surface);
            badge.BorderThickness = new Thickness(1.5);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Top;
            badge.Margin = new Thickness(0, -5, -5, 0);
            badge.Child = Icons.Of(Icons.Check, 8, Colors.White, 3);
            badge.RenderTransform = badgeScale;
            Children.Add(badge);
            Set(value, false);
            MouseEnter += delegate { T.Animate(lift, TranslateTransform.YProperty, -2, 140); if (!on) { T.Fade(line, T.Mix(T.Border, T.Accent, 0.5), 120); } };
            MouseLeave += delegate { T.Animate(lift, TranslateTransform.YProperty, 0, 180); if (!on) { T.Fade(line, T.Border, 160); } };
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Set(!on, true);
                if (Toggled != null)
                {
                    Toggled(on);
                }
            };
        }

        private void Set(bool value, bool animate)
        {
            on = value;
            double ms = animate ? 220 : 1;
            T.Fade(bg, value ? T.A(T.Accent, 64) : T.A(T.Surface2, 170), ms);
            T.Fade(line, value ? T.AccentHover : T.Border, ms);
            var e = new BackEase();
            e.Amplitude = 0.7;
            e.EasingMode = value ? EasingMode.EaseOut : EasingMode.EaseIn;
            var a = new DoubleAnimation(value ? 1 : 0, T.Ms(animate ? 300 : 1));
            a.EasingFunction = e;
            badgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            badgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (value != on)
                {
                    Set(value, true);
                }
            }
        }
    }

    /// Selectable design card with a preview on top, radio dot, title and description.
    internal sealed class ThemeCard : Grid
    {
        private readonly Border frame = new Border();
        private readonly SolidColorBrush line = T.B(T.Border);
        private readonly DropShadowEffect glow = new DropShadowEffect();
        private readonly Ellipse dot = new Ellipse();
        private readonly ScaleTransform dotScale = new ScaleTransform(0, 0, 5, 5);
        private readonly Ellipse radio = new Ellipse();
        private readonly TranslateTransform lift = new TranslateTransform();
        private bool selected;
        public event Action Picked;

        public ThemeCard(FrameworkElement preview, string title, string tag, string desc, bool value)
        {
            Cursor = Cursors.Hand;
            RenderTransform = lift;
            glow.Color = T.Accent;
            glow.BlurRadius = 26;
            glow.ShadowDepth = 0;
            glow.Opacity = 0;
            var glowHost = new Border();
            glowHost.CornerRadius = new CornerRadius(16);
            glowHost.Background = T.Frozen(T.Surface);
            glowHost.Effect = glow;
            Children.Add(glowHost);

            frame.CornerRadius = new CornerRadius(16);
            frame.Background = T.Frozen(T.Mix(T.Surface, T.Surface2, 0.6));
            frame.BorderBrush = line;
            frame.BorderThickness = new Thickness(1.6);
            var col = new StackPanel();
            var clip = new Border();
            clip.CornerRadius = new CornerRadius(14, 14, 0, 0);
            clip.Child = preview;
            clip.Height = 150;
            preview.Loaded += delegate
            {
                clip.Clip = new RectangleGeometry(new Rect(0, 0, clip.ActualWidth, clip.ActualHeight + 14), 14, 14);
            };
            col.Children.Add(clip);
            var text = new StackPanel();
            text.Margin = new Thickness(16, 14, 16, 16);
            var head = new StackPanel();
            head.Orientation = Orientation.Horizontal;
            var rg = new Grid();
            rg.Width = 18;
            rg.Height = 18;
            rg.Margin = new Thickness(0, 0, 10, 0);
            radio.Stroke = T.Frozen(T.Muted);
            radio.StrokeThickness = 1.6;
            rg.Children.Add(radio);
            dot.Width = 10;
            dot.Height = 10;
            dot.Fill = T.Frozen(T.AccentHover);
            dot.RenderTransform = dotScale;
            rg.Children.Add(dot);
            head.Children.Add(rg);
            TextBlock t = T.Label(title, 15.5, FontWeights.Bold, T.Text);
            t.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(t);
            if (tag != null)
            {
                var chip = new Border();
                chip.CornerRadius = new CornerRadius(999);
                chip.Background = T.Frozen(T.A(T.Accent, 50));
                chip.Padding = new Thickness(8, 2, 8, 3);
                chip.Margin = new Thickness(10, 0, 0, 0);
                chip.VerticalAlignment = VerticalAlignment.Center;
                chip.Child = T.Label(tag, 10.5, FontWeights.Bold, T.AccentText);
                head.Children.Add(chip);
            }
            text.Children.Add(head);
            TextBlock d = T.Label(desc, 12.5, FontWeights.Normal, T.Muted);
            d.Margin = new Thickness(28, 6, 0, 0);
            d.LineHeight = 18;
            text.Children.Add(d);
            col.Children.Add(text);
            frame.Child = col;
            Children.Add(frame);
            Selected = value;
            MouseEnter += delegate { T.Animate(lift, TranslateTransform.YProperty, -4, 180); if (!selected) { T.Fade(line, T.Mix(T.Border, T.Accent, 0.55), 140); } };
            MouseLeave += delegate { T.Animate(lift, TranslateTransform.YProperty, 0, 220); if (!selected) { T.Fade(line, T.Border, 180); } };
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (Picked != null)
                {
                    Picked();
                }
            };
        }

        public bool Selected
        {
            get { return selected; }
            set
            {
                selected = value;
                T.Fade(line, value ? T.AccentHover : T.Border, 220);
                T.Animate(glow, DropShadowEffect.OpacityProperty, value ? 0.75 : 0, 260);
                radio.Stroke = T.Frozen(value ? T.AccentHover : T.Muted);
                var e = new BackEase();
                e.Amplitude = 0.8;
                e.EasingMode = EasingMode.EaseOut;
                var a = new DoubleAnimation(value ? 1 : 0, T.Ms(value ? 320 : 140));
                a.EasingFunction = e;
                dotScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                dotScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }
    }
}
