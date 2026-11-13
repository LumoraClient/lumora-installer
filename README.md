# Lumora Installer

Windows-Installer für den [Lumora Client](https://github.com/LumoraClient/lumora-launcher): ein eigener
Installations-Assistent im Look des Launchers (blaues Design, Nachtlandschaft mit Polarlicht, fließende Knöpfe,
weiche Seitenübergänge). Ergebnis ist eine einzelne Datei `LumoraClient-Setup-<version>.exe`, die den kompletten
Launcher samt Java-Laufzeit enthält.

## Schritte im Assistenten

1. **Willkommen**: erkennt eine vorhandene Installation und zeigt dann „Update X → Y“.
2. **Versionen**: Minecraft 1.8.9 bis 26.3 frei wählen (Empfohlen / Alle / Keine), optional gleich herunterladen.
   Der Download läuft über die Launcher-eigene Installation (`LumoraLauncherCli --install-only`, jede Datei
   per Prüfsumme kontrolliert).
3. **Speicherorte**: Programmordner (Standard `%LOCALAPPDATA%\Programs\LumoraClient`) und Spieldaten
   (Standard `%APPDATA%\LumoraClient`), Desktop- und Startmenü-Verknüpfung, Mit Windows starten.
4. **Design**: Lumora Theme oder klassisch im Minecraft Style (gilt für die Menüs im Spiel).
5. **Installation** mit Fortschritt; der Download lässt sich überspringen (dann lädt der Launcher beim ersten Start).
6. **Fertig**: Lumora starten.

Installiert wird ohne Adminrechte, nur für das eigene Benutzerkonto, mit Eintrag unter Windows-Einstellungen › Apps.
`Deinstallieren.exe` (gleicher Look) entfernt Launcher, Verknüpfungen und Einträge; Spieldaten nur auf Wunsch.

## Übergabe an den Launcher

Die Auswahl landet in `%APPDATA%\LumoraClient\config\setup.json`
(`dataDir`, `profiles`, `defaultProfile`, `theme`, `seed`). Der Launcher übernimmt sie beim nächsten Start
(`SetupHandoff` in lumora-launcher) und löscht die Datei. Das Design setzt er pro Instanz einmal
(`GameThemeSeeder`); spätere Änderungen im Hub bleiben erhalten. Der Installer selbst ändert `launcher.json` nie.
Dafür braucht es lumora-launcher ab 0.3.39.

## Bauen

Nur Windows, keine Zusatzwerkzeuge: kompiliert wird mit dem `csc.exe` des .NET Framework 4.8, das in Windows
enthalten ist (C# 5, WPF).

Normalerweise über den Launcher, mit diesem Repository als Nachbarordner von `lumora-launcher`:

```bat
gradlew.bat :launcher-app:setupExe
```

(anderer Ort: `-PinstallerDir=<Pfad>`). Direkt:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -AppImage <...\LumoraLauncher> `
    -Profiles <lumora-launcher\launcher-core\src\main\resources\lumora\profiles.json> `
    -Version 0.3.39 -Out LumoraClient-Setup-0.3.39.exe
```

`-AppImage` ist das jpackage-App-Image des Launchers (Ordner `LumoraLauncher`). Die Inter-Schrift kommt aus dessen
`flatlaf-fonts-inter-*.jar`, die Versionsliste aus `profiles.json`.

## Kommandozeile

| Aufruf | Wirkung |
|---|---|
| `LumoraClient-Setup.exe /S` | still installieren (vorherige Auswahl oder Standard) |
| `--dir=<Pfad>` / `/D=<Pfad>` | Programmordner |
| `--data=<Pfad>` | Spieldaten |
| `--versions=26.1.2,1.8.9` | Minecraft-Versionen |
| `--theme=lumora` / `--theme=vanilla` | Design im Spiel |
| `--download` | Versionen gleich herunterladen |
| `--no-desktop`, `--no-startmenu`, `--start` | Verknüpfungen, Launcher danach starten |
| `Deinstallieren.exe /S [--purge]` | still deinstallieren, mit `--purge` auch Spieldaten |
| `--ui-shot=seite.png --page=0..5` | Seite unsichtbar rendern und als PNG speichern |

Protokoll stiller Läufe: `%TEMP%\LumoraSetup.log`.

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
| `src/Engine.cs` | Installieren (Entpacken mit Rollback, Registry, Verknüpfungen, Übergabe, Downloads), Deinstallieren |
| `src/Native.cs` | Ordnerauswahl (IFileOpenDialog), Verknüpfungen (IShellLink), Fensterrahmen (DWM) |
| `build.ps1` | Build |

## Lizenz

GPL-3.0-or-later, wie der Lumora Client. Inoffizielles Projekt, nicht verbunden mit Mojang oder Microsoft.
