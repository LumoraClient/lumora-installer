# Lumora Installer

Windows-Installer für den [Lumora Client](https://github.com/LumoraClient/lumora-launcher): ein eigener
Installations-Assistent im Look des Launchers (blaues Design, Nachtlandschaft mit Polarlicht, fließende Knöpfe,
weiche Seitenübergänge).

Der Installer ist **unabhängig vom Launcher**: `LumoraClient-Setup.exe` (ca. 5 MB) enthält den Launcher nicht,
sondern lädt beim Installieren immer die neueste Version aus den
[Releases von lumora-launcher](https://github.com/LumoraClient/lumora-launcher/releases). Er muss deshalb nur neu
gebaut werden, wenn sich der Installer selbst ändert.

## Ablauf

1. **Willkommen**: sucht das neueste Lumora-Release und zeigt Version und Downloadgröße (oder „Update X → Y“).
2. **Versionen**: Minecraft-Versionen frei wählen (Empfohlen / Alle / Keine), optional gleich herunterladen.
   Die Liste kommt aus `profiles.json` genau des Releases, das installiert wird.
3. **Speicherorte**: Programmordner (Standard `%LOCALAPPDATA%\Programs\LumoraClient`) und Spieldaten
   (Standard `%APPDATA%\LumoraClient`), Desktop- und Startmenü-Verknüpfung, Mit Windows starten.
4. **Design**: Lumora Theme oder klassisch im Minecraft Style (gilt für die Menüs im Spiel).
5. **Installation**: Lumora herunterladen, Launcher installieren, Verknüpfungen, Einstellungen übernehmen und
   (optional) die gewählten Minecraft-Versionen laden. Den Versions-Download kann man überspringen; dann lädt der
   Launcher beim ersten Start.
6. **Fertig**: Lumora starten.

Installiert wird ohne Adminrechte, nur für das eigene Benutzerkonto, mit Eintrag unter Windows-Einstellungen › Apps.
`Deinstallieren.exe` (gleicher Look) entfernt Launcher, Verknüpfungen und Einträge; Spieldaten nur auf Wunsch.

## Sicherheit beim Download

Wie beim Selbst-Update des Launchers (`LauncherUpdate`): Das Release muss `LumoraClient.exe`, `SHA256SUMS.txt` und
`SHA256SUMS.txt.sig` enthalten. Der Installer prüft die **Ed25519-Signatur** der Prüfsummenliste mit dem
öffentlichen Release-Schlüssel des Launchers (fest im Installer), dann den **SHA-256** der heruntergeladenen EXE und
zuletzt den SHA-256 des darin eingebetteten Launcher-Pakets. Erst dann wird entpackt; die EXE selbst wird nie
ausgeführt, nur gelesen. Die Prüfung ist eine eigene Ed25519-Implementierung nach RFC 8032 (`src/Online.cs`), weil
das .NET Framework 4.8 kein Ed25519 kennt.

## Übergabe an den Launcher

Die Auswahl landet in `%APPDATA%\LumoraClient\config\setup.json`
(`dataDir`, `profiles`, `defaultProfile`, `theme`, `seed`). Der Launcher übernimmt sie beim nächsten Start
(`SetupHandoff` in lumora-launcher) und löscht die Datei; das Design setzt er pro Instanz einmal
(`GameThemeSeeder`), spätere Änderungen im Hub bleiben erhalten. Die Versionen lädt der Installer über
`LumoraLauncherCli.exe --install-only --machine-progress`. Dafür braucht es lumora-launcher ab 0.3.39.

## Bauen

Nur Windows, keine Zusatzwerkzeuge und kein Launcher-Build: kompiliert wird mit dem `csc.exe` des .NET Framework
4.8, das in Windows enthalten ist (C# 5, WPF).

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.0
```

Ergebnis: `out\LumoraClient-Setup.exe`. Optional `-Profiles <lumora-launcher>\launcher-core\src\main\resources\lumora\profiles.json`
als eingebaute Ersatz-Versionsliste, falls `raw.githubusercontent.com` nicht erreichbar ist.

Releases: Ein Tag `v1.2.3` baut das Setup in der CI und veröffentlicht es mit `SHA256SUMS.txt` als GitHub-Release.

## Kommandozeile

| Aufruf | Wirkung |
|---|---|
| `LumoraClient-Setup.exe /S` | still installieren (vorherige Auswahl oder Standard) |
| `--dir=<Pfad>` / `/D=<Pfad>` | Programmordner |
| `--data=<Pfad>` | Spieldaten |
| `--versions=26.1.2,1.8.9` | Minecraft-Versionen |
| `--theme=lumora` / `--theme=vanilla` | Design im Spiel |
| `--download` | Minecraft-Versionen gleich herunterladen |
| `--source=<LumoraClient.exe>` | aus einer lokalen Datei installieren statt von GitHub (offline, Tests) |
| `--no-desktop`, `--no-startmenu`, `--start` | Verknüpfungen, Launcher danach starten |
| `Deinstallieren.exe /S [--purge]` | still deinstallieren, mit `--purge` auch Spieldaten |
| `--ui-shot=seite.png --page=0..5` | Seite unsichtbar rendern und als PNG speichern |

Protokoll stiller Läufe: `%TEMP%\LumoraSetup.log`. Heruntergeladene Launcher-Dateien: `%TEMP%\lumora-setup`.

Tests ohne Eingriff in eine echte Installation: `LUMORA_HOME=<Testordner>` (gleiche Variable wie im Launcher) und
`LUMORA_SETUP_SANDBOX=1` (eigene Registry-Schlüssel, keine Verknüpfungen, kein Autostart-Eintrag, nur Prozesse im
Testordner zählen).

## Aufbau

| Datei | Inhalt |
|---|---|
| `src/Program.cs` | Einstieg, stille Modi, Deinstallation aus einer Kopie in `%TEMP%` |
| `src/Wizard.cs` | Fenster, Schrittleiste, Seiten, Übergänge, Dialoge |
| `src/Scene.cs` | animierter Hintergrund und die Design-Vorschauen (selbst gezeichnet, keine Spiel-Grafiken) |
| `src/Ui.cs` | Farben, Schrift, Icons, Knöpfe, Schalter, Fortschrittsbalken |
| `src/Online.cs` | GitHub-Release, Versionsliste, Download, Ed25519- und SHA-256-Prüfung |
| `src/Engine.cs` | Installieren (Entpacken mit Rollback, Registry, Verknüpfungen, Übergabe, Downloads), Deinstallieren |
| `src/Native.cs` | Ordnerauswahl (IFileOpenDialog), Verknüpfungen (IShellLink), Fensterrahmen (DWM) |
| `assets/fonts` | Inter (SIL Open Font License 1.1, `OFL-Inter.txt`) |
| `build.ps1` | Build |

## Lizenz

GPL-3.0-or-later, wie der Lumora Client. Die Schrift Inter steht unter der SIL Open Font License 1.1.
Inoffizielles Projekt, nicht verbunden mit Mojang oder Microsoft.
