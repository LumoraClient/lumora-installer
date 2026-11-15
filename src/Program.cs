// Lumora Setup - entry point. One program, two roles:
//   LumoraClient-Setup.exe   wizard; downloads the newest launcher from GitHub Releases (lumora-launcher)
//   Deinstallieren.exe       a copy of the setup in the program folder; under this name (or with --uninstall) it shows
//                            the same window in uninstall mode
//
// Command line (both): /S or --silent runs without a window. Setup: --dir=<programm> --data=<spieldaten>
// --versions=26.1.2,1.8.9 --theme=lumora|vanilla --download --no-desktop --no-startmenu --start
// --source=<LumoraClient.exe> (install from a local file, no download).
// Uninstaller: --purge also deletes the game data. Exit code 0 = success. Log: %TEMP%\LumoraSetup.log
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("Lumora Client Setup")]
[assembly: AssemblyProduct("Lumora Client")]
[assembly: AssemblyDescription("Lumora Client installer")]
[assembly: AssemblyCompany("Lumora Client contributors")]
[assembly: AssemblyCopyright("Copyright © 2026 Lumora Client contributors, GPL-3.0-or-later")]

namespace Lumora.Setup
{
    internal static class Program
    {
        /// Path of this program (the setup, or its copy Deinstallieren.exe in the program folder).
        public static readonly string Self = Assembly.GetExecutingAssembly().Location;

        /// Version of this setup program (resource version.txt, set by build.ps1).
        public static readonly string SetupVersion = (Res.Text("version.txt") ?? "dev").Trim();
        /// --source=<LumoraClient.exe>: install from a local file instead of downloading from GitHub.
        public static string Source;

        [STAThread]
        private static int Main(string[] args)
        {
            var a = new List<string>(args);
            Source = Option(a, "--source");
            if (Source != null)
            {
                Source = Path.GetFullPath(Source);
            }
            bool silent = a.Any(x => x == "/S" || x == "/s" || x == "--silent");
            try
            {
                Engine.CleanLeftovers();
                bool uninstaller = a.Contains("--uninstall")
                    || string.Equals(Path.GetFileName(Self), Engine.Uninstaller, StringComparison.OrdinalIgnoreCase);
                if (uninstaller)
                {
                    string app = Option(a, "--app") ?? Path.GetDirectoryName(Self);
                    // not inside the program folder, so it can be removed at the end
                    Environment.CurrentDirectory = Path.GetTempPath();
                    return silent ? SilentUninstall(app, a.Contains("--purge")) : RunUi(true, app, a);
                }
                bool created;
                using (var mutex = new Mutex(true, "Local\\LumoraClientSetup", out created))
                {
                    if (!created)
                    {
                        if (!silent)
                        {
                            MessageBox.Show("Das Lumora-Setup läuft schon.", "Lumora Client Setup", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        return 1;
                    }
                    return silent ? SilentInstall(a) : RunUi(false, null, a);
                }
            }
            catch (Exception e)
            {
                Log("FEHLER: " + e);
                if (!silent)
                {
                    MessageBox.Show("Das Setup konnte nicht ausgeführt werden:\n\n" + e.Message, "Lumora Client Setup",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return 1;
            }
        }

        private static int RunUi(bool uninstall, string app, List<string> a)
        {
            T.Calm = !SystemParameters.ClientAreaAnimation || Environment.GetEnvironmentVariable("LUMORA_SETUP_CALM") == "1";
            T.LoadFonts();
            var application = new Application();
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            application.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                Log("UI-Fehler: " + e.Exception);
                MessageBox.Show("Unerwarteter Fehler im Setup:\n\n" + e.Exception.Message, "Lumora Client Setup", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            var w = new Wizard(uninstall, app);
            application.MainWindow = w;
            string shot = Option(a, "--ui-shot");
            if (shot != null)
            {
                string pg = Option(a, "--page");
                string sc = Option(a, "--scale");
                w.Shot(shot, pg == null ? 0 : int.Parse(pg), sc == null ? 1 : double.Parse(sc, System.Globalization.CultureInfo.InvariantCulture));
            }
            w.Show();
            return application.Run();
        }

        private sealed class LogProgress : IProgress
        {
            private readonly string[] names;

            public LogProgress(string[] names)
            {
                this.names = names;
            }

            public void Step(int index, string detail)
            {
                Log("[" + (index + 1) + "/" + names.Length + "] " + names[index] + ": " + detail);
            }

            public void Done(int index, bool ok, string note)
            {
                Log("  " + (ok ? "OK" : "FEHLER") + (note != null ? " (" + note + ")" : ""));
            }

            public void Progress(double fraction, string detail)
            {
            }
        }

        private static int SilentInstall(List<string> a)
        {
            Existing existing = Engine.Detect();
            string recommended;
            List<Version> versions;
            var plan = new Plan();
            plan.Source = Source;
            plan.Release = Engine.Resolve(Source, out versions, out recommended);
            plan.AppDir = Option(a, "--dir") ?? existing.AppDir ?? Engine.DefaultAppDir;
            plan.DataDir = Option(a, "--data") ?? existing.DataDir ?? Engine.DefaultDataDir;
            string list = Option(a, "--versions");
            plan.Profiles = list != null ? list.Split(',').Select(s => s.Trim()).ToList()
                : existing.Profiles ?? new List<string> { recommended };
            plan.Profiles = versions.Where(v => plan.Profiles.Contains(v.Id)).Select(v => v.Id).ToList();
            if (plan.Profiles.Count == 0)
            {
                plan.Profiles.Add(recommended);
            }
            plan.DefaultProfile = plan.Profiles.Contains(recommended) ? recommended : plan.Profiles[0];
            plan.Theme = Option(a, "--theme") ?? existing.Theme ?? "lumora";
            plan.Theme = plan.Theme == "vanilla" ? "vanilla" : "lumora";
            plan.Desktop = !a.Contains("--no-desktop") && (existing.Desktop ?? true);
            plan.StartMenu = !a.Contains("--no-startmenu") && (existing.StartMenu ?? true);
            plan.Autostart = existing.Autostart;
            Log("Lumora Setup " + SetupVersion + ", Launcher " + Engine.Version + " (still): " + plan.AppDir + ", Daten " + plan.DataDir + ", Versionen "
                + string.Join(",", plan.Profiles) + ", Design " + plan.Theme);
            Engine.Install(plan, new LogProgress(Engine.InstallSteps));
            if (a.Contains("--download"))
            {
                foreach (string id in plan.Profiles)
                {
                    string err = Engine.DownloadVersion(plan.AppDir, id, delegate { }, delegate { return false; });
                    Log("Download " + id + ": " + (err ?? "OK"));
                }
            }
            if (a.Contains("--start"))
            {
                Engine.StartLauncher(plan.AppDir);
            }
            Log("Fertig.");
            return 0;
        }

        private static int SilentUninstall(string app, bool purge)
        {
            Existing existing = Engine.Detect();
            Log("Lumora deinstallieren (still): " + app + (purge ? ", mit Spieldaten" : ""));
            Engine.Uninstall(app, existing.DataDir ?? Engine.DefaultDataDir, purge, new LogProgress(Engine.UninstallSteps));
            Log("Fertig.");
            return 0;
        }

        private static string Option(List<string> a, string name)
        {
            string prefix = name + "=";
            string hit = a.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
            {
                return hit.Substring(prefix.Length).Trim('"');
            }
            if (name == "--dir")
            {
                // Inno/NSIS style /D=path
                hit = a.FirstOrDefault(x => x.StartsWith("/D=", StringComparison.OrdinalIgnoreCase));
                return hit == null ? null : hit.Substring(3).Trim('"');
            }
            return null;
        }

        private static void Log(string line)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "LumoraSetup.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }
    }
}
