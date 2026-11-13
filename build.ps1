# Builds LumoraClient-Setup-<version>.exe (and Deinstallieren.exe, embedded) with the csc.exe of the
# .NET Framework 4.8 that ships with Windows. No other tools needed.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 -AppImage <...\LumoraLauncher> -Profiles <profiles.json> `
#       -Version 0.3.39 -Out <...\LumoraClient-Setup-0.3.39.exe>
#
# -AppImage  the launcher's jpackage app image folder (must be named LumoraLauncher, contains LumoraLauncher.exe)
# -Profiles  launcher-core/src/main/resources/lumora/profiles.json of the same launcher version
# The Inter fonts are taken from flatlaf-fonts-inter-*.jar in the app image (Segoe UI if missing).
param(
    [Parameter(Mandatory = $true)][string]$AppImage,
    [Parameter(Mandatory = $true)][string]$Profiles,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Out
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe of the .NET Framework 4.8 not found: $csc" }
$AppImage = (Resolve-Path $AppImage).Path
if (-not (Test-Path (Join-Path $AppImage 'LumoraLauncher.exe'))) { throw "No LumoraLauncher.exe in $AppImage" }
if ((Split-Path -Leaf $AppImage) -ne 'LumoraLauncher') { throw "The app image folder must be named LumoraLauncher: $AppImage" }

# csc of the .NET Framework is not long-path aware: stage in %TEMP%
$stage = Join-Path $env:TEMP "lumora-setup-$Version"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

# launcher (entries LumoraLauncher/...)
[System.IO.Compression.ZipFile]::CreateFromDirectory($AppImage, (Join-Path $stage 'app.zip'),
    [System.IO.Compression.CompressionLevel]::Optimal, $true)

# versions offered in the wizard: "default<TAB>id" and "id<TAB>minecraftVersion", newest first
$catalog = Get-Content -Raw -Encoding UTF8 $Profiles | ConvertFrom-Json
$lines = @("default`t$($catalog.defaultProfile)")
foreach ($p in $catalog.profiles) { $lines += "$($p.id)`t$($p.minecraftVersion)" }
[System.IO.File]::WriteAllText((Join-Path $stage 'profiles.txt'), ($lines -join "`n"))
[System.IO.File]::WriteAllText((Join-Path $stage 'version.txt'), $Version)

Copy-Item (Join-Path $repo 'src\*.cs') $stage
Copy-Item (Join-Path $repo 'assets\lumora.ico') $stage
Copy-Item (Join-Path $repo 'assets\logo.png') $stage
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $stage 'LICENSE.txt')

$fonts = @('Inter-Regular.otf', 'Inter-SemiBold.otf', 'Inter-Bold.otf', 'Inter-BoldItalic.otf')
$res = @()
$jar = Get-ChildItem (Join-Path $AppImage 'app') -Filter 'flatlaf-fonts-inter-*.jar' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($jar) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($jar.FullName)
    try {
        foreach ($f in $fonts) {
            $entry = $zip.GetEntry("com/formdev/flatlaf/fonts/inter/$f")
            if ($entry) {
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $stage $f), $true)
                $res += "/resource:$f,$f"
            }
        }
    } finally { $zip.Dispose() }
} else {
    Write-Warning 'flatlaf-fonts-inter jar not found in the app image: the setup uses Segoe UI'
}
$res += @('/resource:logo.png,logo.png', '/resource:lumora.ico,lumora.ico', '/resource:version.txt,version.txt', '/resource:profiles.txt,profiles.txt')

$wpf = Join-Path $fw 'WPF'
$refs = @("$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll", "$wpf\WindowsBase.dll",
    "$fw\System.Xaml.dll", "$fw\System.IO.Compression.dll", "$fw\System.Web.Extensions.dll") | ForEach-Object { "/reference:$_" }
$sources = Get-ChildItem $stage -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.Name }
$common = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/win32icon:lumora.ico') + $refs + $res

Push-Location $stage
try {
    & $csc @common '/define:UNINSTALLER' '/out:Deinstallieren.exe' @sources
    if ($LASTEXITCODE -ne 0) { throw 'csc failed (Deinstallieren.exe)' }
    & $csc @common '/out:LumoraClient-Setup.exe' '/resource:LICENSE.txt,LICENSE.txt' '/resource:app.zip,app.zip' `
        '/resource:Deinstallieren.exe,uninstall.exe' @sources
    if ($LASTEXITCODE -ne 0) { throw 'csc failed (LumoraClient-Setup.exe)' }
} finally { Pop-Location }

$outDir = Split-Path -Parent $Out
if ($outDir) { New-Item -ItemType Directory -Force $outDir | Out-Null }
Copy-Item (Join-Path $stage 'LumoraClient-Setup.exe') $Out -Force
Remove-Item -Recurse -Force $stage
$mb = [math]::Round((Get-Item $Out).Length / 1MB)
Write-Host "Installer: $Out ($mb MB)"
