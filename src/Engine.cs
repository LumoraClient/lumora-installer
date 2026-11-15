// Lumora Setup - installation and removal (no UI). Per-user: no administrator rights, everything below the user's
// profile and HKCU. User data (%APPDATA%\LumoraClient: settings, sign-in, worlds) is only removed on request.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Lumora.Setup
{
    /// A Minecraft version the launcher offers (from the bundled profiles.json, written at build time).
    internal sealed class Version
    {
        public string Id;
        public string Minecraft;
    }

    /// Everything the user chose in the wizard.
    internal sealed class Plan
    {
        public string AppDir;
        public string DataDir;
        public List<string> Profiles = new List<string>();
        public string DefaultProfile;
        public string Theme = "lumora";
        public bool Download = true;
        public bool Desktop = true;
        public bool StartMenu = true;
        public bool Autostart;
        public bool Start = true;
        /// Launcher release to download (GitHub), or null when Source is set.
        public Release Release;
        /// Local LumoraClient.exe instead of the download (offline install, tests).
        public string Source;
    }

    /// What is installed already (registry, old installers, launcher settings).
    internal sealed class Existing
    {
        public string Version;
        public string AppDir;
        public string DataDir;
        public List<string> Profiles;
        public string Theme;
        public bool? Desktop;
        public bool? StartMenu;
        public bool Autostart;
        public bool HasData;
    }

    internal interface IProgress
    {
        void Step(int index, string detail);
        void Done(int index, bool ok, string note);
        void Progress(double fraction, string detail);
    }

    internal sealed class SetupException : Exception
    {
        public readonly bool Retry;

        public SetupException(string message, bool retry) : base(message)
        {
            Retry = retry;
        }
    }

    internal static class Engine
    {
        public const string AppName = "Lumora Client";
        /// Test mode (LUMORA_SETUP_SANDBOX=1, use with LUMORA_HOME): own registry keys, no shortcuts, no autostart entry,
        /// only launcher processes inside the chosen folder count. Never touches a real installation.
        public static readonly bool Sandbox = Environment.GetEnvironmentVariable("LUMORA_SETUP_SANDBOX") == "1";
        public static readonly string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LumoraClient" + (Sandbox ? "SetupTest" : "");
        public static readonly string SettingsKey = @"Software\LumoraClient" + (Sandbox ? "SetupTest" : "");
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValue = "LumoraLauncher";
        public const string Uninstaller = "Deinstallieren.exe";
        /// Folders the launcher creates in its data directory (LauncherPaths); only these are deleted on request.
        public static readonly string[] DataFolders = { "instances", "versions", "libraries", "assets", "runtime", "cache",
            "logs", "config", "auth", "addons", "diagnostics", "portable", "data" };

        /// Launcher version being installed (from the GitHub release, or the local LumoraClient.exe).
        public static string Version = "";

        /// Rough size of the installed launcher with its Java runtime (the download is compressed).
        public const long InstalledSize = 230L << 20;

        public static string DefaultAppDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LumoraClient"); }
        }

        /// %APPDATA%\LumoraClient: launcher.json always lives here (LauncherPaths.defaultRoot), and so do the data of
        /// installations from before the standard folder (launcher 0.3.42).
        public static string ConfigRoot
        {
            get
            {
                // same override as the launcher (LauncherPaths.defaultRoot)
                string home = Environment.GetEnvironmentVariable("LUMORA_HOME");
                return !string.IsNullOrWhiteSpace(home) ? Path.GetFullPath(home).TrimEnd('\\')
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LumoraClient");
            }
        }

        /// Data directory of a new installation: next to Minecraft's own folder (.minecraft\LumoraClient), where the
        /// versions play by default (InstanceLayout in the launcher). With LUMORA_HOME: LUMORA_HOME\minecraft\LumoraClient,
        /// the same as the launcher, so a test never touches the real .minecraft.
        public static string DefaultDataDir
        {
            get
            {
                string home = Environment.GetEnvironmentVariable("LUMORA_HOME");
                string minecraft = !string.IsNullOrWhiteSpace(home) ? Path.Combine(Path.GetFullPath(home).TrimEnd('\\'), "minecraft")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
                return Path.Combine(minecraft, "LumoraClient");
            }
        }

        public static string LauncherExe(string appDir)
        {
            return Path.Combine(appDir, "LumoraLauncher", "LumoraLauncher.exe");
        }

        /// Newest launcher release on GitHub (null with a local source) and the Minecraft versions it offers. The version
        /// list comes from that release's profiles.json; the list built into the setup is only a fallback.
        public static Release Resolve(string source, out List<Version> versions, out string recommended)
        {
            Release rel = null;
            string tag;
            if (source != null)
            {
                if (!File.Exists(source))
                {
                    throw new SetupException("Datei nicht gefunden: " + source, false);
                }
                Version = PortableVersion(source);
                if (Version.Length == 0)
                {
                    throw new SetupException(source + " ist keine Lumora-Datei (LumoraClient.exe).", false);
                }
                tag = "v" + Version;
            }
            else
            {
                rel = Online.Latest();
                Version = rel.Version;
                tag = rel.Tag;
            }
            try
            {
                var lookup = new Release();
                lookup.Tag = tag;
                versions = Online.Profiles(lookup, out recommended);
            }
            catch (Exception)
            {
                versions = Versions(out recommended);
            }
            if (versions.Count == 0)
            {
                throw new SetupException("Die Liste der Minecraft-Versionen ist nicht erreichbar. Bitte Internetverbindung prüfen.", true);
            }
            return rel;
        }

        /// Version list built into the setup (fallback when GitHub's raw files are not reachable).
        public static List<Version> Versions(out string recommended)
        {
            var list = new List<Version>();
            recommended = null;
            string text = Res.Text("profiles.txt") ?? "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                string[] p = line.Split('\t');
                if (p[0] == "default" && p.Length > 1)
                {
                    recommended = p[1];
                }
                else if (p.Length > 1)
                {
                    var v = new Version();
                    v.Id = p[0];
                    v.Minecraft = p[1];
                    list.Add(v);
                }
            }
            if (recommended == null && list.Count > 0)
            {
                recommended = list[0].Id;
            }
            return list;
        }

        public static Existing Detect()
        {
            var e = new Existing();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    if (k != null)
                    {
                        e.Version = k.GetValue("DisplayVersion") as string;
                        e.AppDir = k.GetValue("InstallLocation") as string;
                    }
                }
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    if (k != null)
                    {
                        e.AppDir = (k.GetValue("InstallDir") as string) ?? e.AppDir;
                        e.DataDir = k.GetValue("DataDir") as string;
                        string profiles = k.GetValue("Profiles") as string;
                        if (!string.IsNullOrEmpty(profiles))
                        {
                            e.Profiles = profiles.Split(',').Where(x => x.Length > 0).ToList();
                        }
                        e.Theme = k.GetValue("Theme") as string;
                        object d = k.GetValue("DesktopShortcut");
                        if (d is int)
                        {
                            e.Desktop = (int)d != 0;
                        }
                        object s = k.GetValue("StartMenuShortcut");
                        if (s is int)
                        {
                            e.StartMenu = (int)s != 0;
                        }
                    }
                }
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    e.Autostart = !Sandbox && k != null && k.GetValue(RunValue) != null;
                }
            }
            catch (Exception)
            {
            }
            // the old IExpress setup installed here without a registry entry
            if (!Sandbox && e.AppDir == null && File.Exists(LauncherExe(DefaultAppDir)))
            {
                e.AppDir = DefaultAppDir;
                e.Version = e.Version ?? "?";
            }
            // launcher settings: data directory and version filter chosen in the launcher
            try
            {
                string file = Path.Combine(ConfigRoot, "config", "launcher.json");
                if (File.Exists(file))
                {
                    e.HasData = true;
                    var json = new System.Web.Script.Serialization.JavaScriptSerializer();
                    var cfg = json.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                    if (cfg != null)
                    {
                        object dir;
                        if (cfg.TryGetValue("installDir", out dir) && dir is string && ((string)dir).Length > 0)
                        {
                            e.DataDir = (string)dir;
                        }
                        else if (e.DataDir == null)
                        {
                            // installed before the standard folder: the data stay where they are
                            e.DataDir = ConfigRoot;
                        }
                        object vis;
                        if (e.Profiles == null && cfg.TryGetValue("visibleProfiles", out vis) && vis is object[] && ((object[])vis).Length > 0)
                        {
                            e.Profiles = ((object[])vis).OfType<string>().ToList();
                        }
                        object theme;
                        if (e.Theme == null && cfg.TryGetValue("gameTheme", out theme) && theme is string)
                        {
                            e.Theme = (string)theme;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return e;
        }

        /// Running launcher processes (any copy: they would overwrite the new settings when they close).
        public static bool LauncherRunning(string appDir)
        {
            foreach (string name in new[] { "LumoraLauncher", "LumoraLauncherCli" })
            {
                Process[] ps = Process.GetProcessesByName(name);
                bool any = false;
                foreach (Process p in ps)
                {
                    if (!Sandbox)
                    {
                        any = true;
                    }
                    else
                    {
                        try
                        {
                            any |= p.MainModule.FileName.StartsWith(appDir, StringComparison.OrdinalIgnoreCase);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    p.Dispose();
                }
                if (any)
                {
                    return true;
                }
            }
            return false;
        }

        /// Folder chosen in a picker: an empty folder or one already named LumoraClient is used as is, otherwise a
        /// LumoraClient subfolder (never spill files into e.g. D:\Spiele).
        public static string Normalize(string picked)
        {
            if (string.IsNullOrEmpty(picked))
            {
                return picked;
            }
            string full = Path.GetFullPath(picked).TrimEnd('\\');
            if (full.Length <= 3)
            {
                return Path.Combine(full + "\\", "LumoraClient");
            }
            string name = Path.GetFileName(full);
            bool empty = !Directory.Exists(full) || !Directory.EnumerateFileSystemEntries(full).Any();
            if (empty || name.StartsWith("Lumora", StringComparison.OrdinalIgnoreCase) || Directory.Exists(Path.Combine(full, "LumoraLauncher"))
                || File.Exists(Path.Combine(full, "config", "launcher.json")))
            {
                return full;
            }
            return Path.Combine(full, "LumoraClient");
        }

        public static long FreeBytes(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static string Size(long bytes)
        {
            var de = new System.Globalization.CultureInfo("de-DE");
            if (bytes >= 1L << 30)
            {
                return (bytes / (double)(1L << 30)).ToString("0.#", de) + " GB";
            }
            return Math.Max(1, bytes / (1L << 20)).ToString(de) + " MB";
        }

        // ------------------------------------------------------------------ install

        public static readonly string[] InstallSteps = { "Lumora herunterladen", "Launcher installieren", "Verknüpfungen und Deinstallation", "Einstellungen übernehmen" };

        /// Steps 0-3 (download, files, shortcuts and registry, settings hand-off). Throws SetupException.
        public static void Install(Plan plan, IProgress ui)
        {
            if (LauncherRunning(plan.AppDir))
            {
                throw new SetupException("Lumora läuft noch. Bitte schließe den Launcher (auch im Tray neben der Uhr) und versuche es erneut.", true);
            }
            string portable;
            if (plan.Source != null)
            {
                ui.Step(0, plan.Source);
                portable = plan.Source;
                ui.Done(0, true, "Lokale Datei");
            }
            else
            {
                ui.Step(0, "Lumora " + plan.Release.Version + " von GitHub…");
                portable = Online.Download(plan.Release, delegate(double f, string d) { ui.Progress(f, d); });
                ui.Done(0, true, Size(new FileInfo(portable).Length));
            }

            ui.Step(1, "Dateien werden entpackt…");
            Extract(plan.AppDir, portable, ui);
            ui.Done(1, true, null);

            ui.Step(2, "Startmenü und Desktop…");
            string exe = LauncherExe(plan.AppDir);
            CopyUninstaller(plan.AppDir);
            Shortcuts(plan, exe);
            Register(plan, exe);
            ui.Done(2, true, null);

            ui.Step(3, "Versionen, Ordner und Design…");
            WriteHandoff(plan);
            ui.Done(3, true, null);
        }

        /// The launcher's app image inside LumoraClient.exe (resource app.zip, its SHA-256 in resource version.txt;
        /// see PortableLauncher.cs in lumora-launcher). The file is only read as bytes (PeResources): never loaded as an
        /// assembly and never run.
        private static Stream OpenAppImage(string portable)
        {
            Stream zip, hash;
            try
            {
                zip = PeResources.Open(portable, "app.zip");
                hash = PeResources.Open(portable, "version.txt");
            }
            catch (Exception e)
            {
                throw new SetupException("LumoraClient.exe ist nicht lesbar (" + e.Message + ").", false);
            }
            if (zip == null || hash == null)
            {
                if (zip != null)
                {
                    zip.Dispose();
                }
                if (hash != null)
                {
                    hash.Dispose();
                }
                throw new SetupException("In LumoraClient.exe fehlt der Launcher. Ist das wirklich eine Lumora-Datei?", false);
            }
            string expected;
            using (var r = new StreamReader(hash))
            {
                expected = r.ReadToEnd().Trim().ToLowerInvariant();
            }
            if (Online.Sha256(zip) != expected)
            {
                zip.Dispose();
                throw new SetupException("Der Launcher in LumoraClient.exe ist beschädigt (Prüfsumme falsch).", false);
            }
            zip.Position = 0;
            return zip;
        }

        /// Launcher version inside a local LumoraClient.exe (from LumoraLauncher/app/launcher-app-<v>.jar), or "".
        public static string PortableVersion(string portable)
        {
            try
            {
                using (Stream s = OpenAppImage(portable))
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(e.FullName.Replace('\\', '/'), "^LumoraLauncher/app/launcher-app-(.+)\\.jar$");
                        if (m.Success)
                        {
                            return m.Groups[1].Value;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return "";
        }

        private static void Extract(string appDir, string portable, IProgress ui)
        {
            Directory.CreateDirectory(appDir);
            string staging = Path.Combine(appDir, ".setup-new");
            string old = Path.Combine(appDir, ".setup-old");
            string target = Path.Combine(appDir, "LumoraLauncher");
            DeleteDir(staging);
            DeleteDir(old);
            using (Stream s = OpenAppImage(portable))
            {
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    long total = Math.Max(1, zip.Entries.Sum(e => e.Length));
                    long done = 0;
                    string root = Path.GetFullPath(staging) + "\\";
                    var buffer = new byte[1 << 16];
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string dest = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', '\\')));
                        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new SetupException("Ungültiger Eintrag im Setup: " + entry.FullName, false);
                        }
                        // directory entries: "dir/" or, from ZipFile.CreateFromDirectory on .NET Framework, "dir\"
                        if (entry.Name.Length == 0 || entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                        {
                            Directory.CreateDirectory(dest);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        try
                        {
                            using (Stream inp = entry.Open())
                            using (FileStream outp = File.Create(dest))
                            {
                                int n;
                                while ((n = inp.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    outp.Write(buffer, 0, n);
                                    done += n;
                                }
                            }
                        }
                        catch (IOException ex)
                        {
                            throw new SetupException("Schreiben fehlgeschlagen (" + ex.Message + "). Ist genug Speicherplatz frei?", true);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            throw new SetupException("Kein Schreibzugriff auf " + appDir + ". Bitte wähle einen anderen Ordner.", false);
                        }
                        ui.Progress(done / (double)total, entry.Name);
                    }
                }
            }
            string fresh = Path.Combine(staging, "LumoraLauncher");
            if (!File.Exists(Path.Combine(fresh, "LumoraLauncher.exe")))
            {
                DeleteDir(staging);
                throw new SetupException("Das Setup ist unvollständig (LumoraLauncher.exe fehlt).", false);
            }
            // swap: keep the old launcher until the new one is in place
            if (Directory.Exists(target))
            {
                MoveWithRetry(target, old);
            }
            try
            {
                MoveWithRetry(fresh, target);
            }
            catch (Exception)
            {
                if (Directory.Exists(old) && !Directory.Exists(target))
                {
                    Directory.Move(old, target);
                }
                throw;
            }
            DeleteDir(old);
            DeleteDir(staging);
        }

        private static void MoveWithRetry(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try
                {
                    Directory.Move(from, to);
                    return;
                }
                catch (IOException)
                {
                    if (i >= 20)
                    {
                        throw new SetupException("Der Ordner " + from + " ist gesperrt (läuft Lumora noch, oder ist er im Explorer geöffnet?).", true);
                    }
                    Thread.Sleep(250);
                }
            }
        }

        /// The uninstaller is a copy of this setup (it switches to uninstall mode by its name), like with Inno Setup:
        /// no second program packed into the setup.
        private static void CopyUninstaller(string appDir)
        {
            string target = Path.Combine(appDir, Uninstaller);
            if (!SamePath(target, Program.Self))
            {
                File.Copy(Program.Self, target, true);
            }
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private const string LeftoverPrefix = "LumoraUninstall-";

        /// Removes what earlier runs left in %TEMP%: the moved uninstaller (see RemoveUninstaller), copies of older setup
        /// versions and launcher downloads stored as .exe. Files still in use stay.
        public static void CleanLeftovers()
        {
            try
            {
                string temp = Path.GetTempPath();
                foreach (string f in Directory.GetFiles(temp, LeftoverPrefix + "*.old").Concat(Directory.GetFiles(temp, LeftoverPrefix + "*.exe")))
                {
                    TryDelete(f);
                }
                string cache = Path.Combine(temp, "lumora-setup");
                if (Directory.Exists(cache))
                {
                    foreach (string f in Directory.GetFiles(cache, "LumoraClient-*.exe"))
                    {
                        TryDelete(f);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// Deletes Deinstallieren.exe. If that is the running program, it cannot be deleted, but it can be moved on the
        /// same drive: it goes to %TEMP% (as .old, it is not run from there) and the next start of the setup or Windows'
        /// temp cleanup removes it. No helper process and no command line. Returns false if it had to stay.
        private static bool RemoveUninstaller(string appDir)
        {
            string own = Path.Combine(appDir, Uninstaller);
            if (!File.Exists(own))
            {
                return true;
            }
            if (!SamePath(own, Program.Self))
            {
                TryDelete(own);
                return !File.Exists(own);
            }
            try
            {
                File.Move(own, Path.Combine(Path.GetTempPath(), LeftoverPrefix + DateTime.Now.Ticks + ".old"));
                return true;
            }
            catch (Exception)
            {
                return false; // %TEMP% on another drive
            }
        }

        private static string StartMenuLink()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
        }

        private static string DesktopLink()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
        }

        private static void Shortcuts(Plan plan, string exe)
        {
            const string desc = "Lumora Client - Minecraft-Launcher";
            if (Sandbox)
            {
                return;
            }
            if (plan.StartMenu)
            {
                Native.CreateShortcut(StartMenuLink(), exe, desc);
            }
            else
            {
                RemoveOwnShortcut(StartMenuLink(), plan.AppDir);
            }
            if (plan.Desktop)
            {
                Native.CreateShortcut(DesktopLink(), exe, desc);
            }
            else
            {
                RemoveOwnShortcut(DesktopLink(), plan.AppDir);
            }
            Native.NotifyShell();
        }

        private static void RemoveOwnShortcut(string lnk, string appDir)
        {
            string target = Native.ShortcutTarget(lnk);
            if (target != null && target.StartsWith(appDir, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(lnk);
            }
        }

        private static void Register(Plan plan, string exe)
        {
            string uninstaller = Path.Combine(plan.AppDir, Uninstaller);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Lumora Client contributors");
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", plan.AppDir);
                k.SetValue("UninstallString", "\"" + uninstaller + "\"");
                k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /S");
                k.SetValue("URLInfoAbout", "https://github.com/LumoraClient/lumora-launcher");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                long kb = DirSize(Path.Combine(plan.AppDir, "LumoraLauncher")) / 1024;
                k.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, kb), RegistryValueKind.DWord);
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKey))
            {
                k.SetValue("InstallDir", plan.AppDir);
                k.SetValue("DataDir", plan.DataDir);
                k.SetValue("Version", Version);
                k.SetValue("Profiles", string.Join(",", plan.Profiles));
                k.SetValue("Theme", plan.Theme);
                k.SetValue("DesktopShortcut", plan.Desktop ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("StartMenuShortcut", plan.StartMenu ? 1 : 0, RegistryValueKind.DWord);
            }
            if (Sandbox)
            {
                return;
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                // same value the launcher's "Mit Windows starten" writes (launcher-app/.../ui/Autostart.java)
                if (plan.Autostart)
                {
                    k.SetValue(RunValue, "\"" + exe + "\" --tray");
                }
                else if (k.GetValue(RunValue) != null)
                {
                    k.DeleteValue(RunValue, false);
                }
            }
        }

        /// config/setup.json for the launcher (dev.lumora.launcher.core.config.SetupHandoff).
        private static void WriteHandoff(Plan plan)
        {
            string dir = Path.Combine(ConfigRoot, "config");
            Directory.CreateDirectory(dir);
            // "" = the launcher's own folder (ConfigRoot); any other folder is passed on, also the new default
            bool defaultData = string.Equals(Path.GetFullPath(plan.DataDir).TrimEnd('\\'), ConfigRoot.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
            if (!defaultData)
            {
                Directory.CreateDirectory(plan.DataDir);
            }
            var data = new Dictionary<string, object>();
            data["dataDir"] = defaultData ? "" : Path.GetFullPath(plan.DataDir);
            data["profiles"] = plan.Profiles.ToArray();
            data["defaultProfile"] = plan.DefaultProfile;
            data["theme"] = plan.Theme;
            data["seed"] = "setup-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(data);
            string target = Path.Combine(dir, "setup.json");
            string tmp = target + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            File.Move(tmp, target);
        }

        // ------------------------------------------------------------------ downloads

        /// Downloads one version with the launcher's own installer (LumoraLauncherCli --install-only): Mojang files,
        /// Java runtime and the Lumora client, every file checked against its hash. Returns null or an error text.
        public static string DownloadVersion(string appDir, string profile, Action<double, string> progress, Func<bool> cancelled)
        {
            string cli = Path.Combine(appDir, "LumoraLauncher", "LumoraLauncherCli.exe");
            if (!File.Exists(cli))
            {
                return "LumoraLauncherCli.exe fehlt";
            }
            var psi = new ProcessStartInfo(cli, "--install-only --profile \"" + profile + "\" --machine-progress");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.Default;
            psi.WorkingDirectory = Path.GetDirectoryName(cli);
            string error = null;
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                string stage = "";
                // every stage of the launcher reports 0..1 again: map stage k to [1-0.6^k, 1-0.6^(k+1)] so the
                // bar only moves forward and approaches the end
                int stages = 0;
                double shown = 0;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null)
                    {
                        return;
                    }
                    if (e.Data.StartsWith("@stage "))
                    {
                        stage = e.Data.Substring(7);
                        stages++;
                        shown = Math.Max(shown, 1 - Math.Pow(0.6, stages - 1));
                        progress(shown, stage);
                    }
                    else if (e.Data.StartsWith("@progress "))
                    {
                        string rest = e.Data.Substring(10);
                        int sp = rest.IndexOf(' ');
                        string num = sp < 0 ? rest : rest.Substring(0, sp);
                        double f;
                        if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f))
                        {
                            string detail = sp < 0 ? "" : rest.Substring(sp + 1);
                            if (f >= 0)
                            {
                                double lo = 1 - Math.Pow(0.6, Math.Max(0, stages - 1));
                                double hi = 1 - Math.Pow(0.6, Math.Max(1, stages));
                                shown = Math.Max(shown, lo + (hi - lo) * Math.Min(1, f));
                            }
                            progress(shown, detail.Length > 0 ? detail : stage);
                        }
                    }
                };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null && e.Data.StartsWith("FEHLER:"))
                    {
                        error = e.Data.Substring(7).Trim();
                    }
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(200))
                {
                    if (cancelled())
                    {
                        try
                        {
                            p.Kill();
                        }
                        catch (Exception)
                        {
                        }
                        p.WaitForExit(5000);
                        return "abgebrochen";
                    }
                }
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    return error ?? ("Exit-Code " + p.ExitCode);
                }
            }
            return null;
        }

        public static void StartLauncher(string appDir)
        {
            string exe = LauncherExe(appDir);
            if (File.Exists(exe))
            {
                var psi = new ProcessStartInfo(exe);
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
        }

        // ------------------------------------------------------------------ uninstall

        public static readonly string[] UninstallSteps = { "Launcher beenden prüfen", "Verknüpfungen und Einträge entfernen", "Programmdateien löschen", "Spieldaten löschen" };

        public static void Uninstall(string appDir, string dataDir, bool purge, IProgress ui)
        {
            ui.Step(0, "Läuft Lumora noch?");
            if (LauncherRunning(appDir))
            {
                throw new SetupException("Lumora läuft noch. Bitte schließe den Launcher (auch im Tray neben der Uhr) und versuche es erneut.", true);
            }
            ui.Done(0, true, null);

            ui.Step(1, "Startmenü, Desktop, Autostart…");
            RemoveOwnShortcut(StartMenuLink(), appDir);
            RemoveOwnShortcut(DesktopLink(), appDir);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    string v = k == null ? null : k.GetValue(RunValue) as string;
                    if (v != null && v.IndexOf(appDir, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        k.DeleteValue(RunValue, false);
                    }
                }
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                Registry.CurrentUser.DeleteSubKeyTree(SettingsKey, false);
            }
            catch (Exception)
            {
            }
            Native.NotifyShell();
            ui.Done(1, true, null);

            ui.Step(2, appDir);
            string launcher = Path.Combine(appDir, "LumoraLauncher");
            DeleteDir(launcher);
            foreach (string leftover in new[] { ".setup-new", ".setup-old" })
            {
                DeleteDir(Path.Combine(appDir, leftover));
            }
            bool uninstallerGone = RemoveUninstaller(appDir);
            TryDeleteEmpty(appDir);
            ui.Done(2, !Directory.Exists(launcher), Directory.Exists(launcher) ? "Einige Dateien waren gesperrt."
                : uninstallerGone ? null : Uninstaller + " bleibt im Ordner, du kannst ihn löschen.");

            if (purge)
            {
                ui.Step(3, "Welten, Einstellungen, Anmeldung…");
                foreach (string dir in new[] { dataDir, ConfigRoot }.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    PurgeData(dir);
                }
                ui.Done(3, true, null);
            }
            else
            {
                ui.Done(3, true, "Behalten");
            }
        }

        /// Deletes only the launcher's own folders inside a data directory, then the directory if it is empty.
        private static void PurgeData(string dir)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (string name in DataFolders)
            {
                DeleteDir(Path.Combine(dir, name));
            }
            TryDeleteEmpty(dir);
        }

        public static long DataSize(string dataDir)
        {
            long total = 0;
            foreach (string dir in new[] { dataDir, ConfigRoot }.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (string name in DataFolders)
                {
                    total += DirSize(Path.Combine(dir, name));
                }
            }
            return total;
        }

        // ------------------------------------------------------------------ helpers

        public static long DirSize(string dir)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(dir))
                {
                    return 0;
                }
                var stack = new Stack<DirectoryInfo>();
                stack.Push(new DirectoryInfo(dir));
                while (stack.Count > 0)
                {
                    DirectoryInfo d = stack.Pop();
                    try
                    {
                        foreach (FileInfo f in d.EnumerateFiles())
                        {
                            total += f.Length;
                        }
                        foreach (DirectoryInfo sub in d.EnumerateDirectories())
                        {
                            if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                stack.Push(sub);
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            return total;
        }

        public static void DeleteDir(string dir)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(f, FileAttributes.Normal);
                    }
                    Directory.Delete(dir, true);
                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(300);
                }
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void TryDeleteEmpty(string dir)
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
