# Builds LumoraClient-Setup.exe (with Deinstallieren.exe embedded) using the csc.exe of the .NET Framework 4.8 that
# ships with Windows. No other tools and no launcher build needed: the setup downloads the newest launcher from the
# GitHub releases of LumoraClient/lumora-launcher when it runs.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-Version 1.0.0] [-Out out\LumoraClient-Setup.exe] [-Profiles <profiles.json>]
#
# -Version   version of the setup program itself (shown in the window and the User-Agent)
# -Profiles  optional: profiles.json of lumora-launcher as built-in fallback version list (the setup loads the list
#            of the release it installs; without network to raw.githubusercontent.com it uses this one)
param(
    [string]$Version = '1.0.0',
    [string]$Out = 'out\LumoraClient-Setup.exe',
    [string]$Profiles = ''
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe of the .NET Framework 4.8 not found: $csc" }
if (-not [System.IO.Path]::IsPathRooted($Out)) { $Out = Join-Path $repo $Out }

# csc of the .NET Framework is not long-path aware: stage in %TEMP%
$stage = Join-Path $env:TEMP "lumora-setup-build-$Version"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

[System.IO.File]::WriteAllText((Join-Path $stage 'version.txt'), $Version)
$lines = @()
if ($Profiles) {
    $catalog = Get-Content -Raw -Encoding UTF8 $Profiles | ConvertFrom-Json
    $lines += "default`t$($catalog.defaultProfile)"
    foreach ($p in $catalog.profiles) { $lines += "$($p.id)`t$($p.minecraftVersion)" }
}
[System.IO.File]::WriteAllText((Join-Path $stage 'profiles.txt'), ($lines -join "`n"))

Copy-Item (Join-Path $repo 'src\*.cs') $stage
Copy-Item (Join-Path $repo 'assets\lumora.ico') $stage
Copy-Item (Join-Path $repo 'assets\logo.png') $stage
Copy-Item (Join-Path $repo 'assets\fonts\*.otf') $stage
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $stage 'LICENSE.txt')

$res = @('/resource:logo.png,logo.png', '/resource:lumora.ico,lumora.ico', '/resource:version.txt,version.txt',
    '/resource:profiles.txt,profiles.txt')
foreach ($f in (Get-ChildItem $stage -Filter '*.otf')) { $res += "/resource:$($f.Name),$($f.Name)" }

$wpf = Join-Path $fw 'WPF'
$refs = @("$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll", "$wpf\WindowsBase.dll", "$fw\System.Xaml.dll",
    "$fw\System.IO.Compression.dll", "$fw\System.Web.Extensions.dll", "$fw\System.Numerics.dll") | ForEach-Object { "/reference:$_" }
$sources = Get-ChildItem $stage -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.Name }
$common = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/win32icon:lumora.ico') + $refs + $res

Push-Location $stage
try {
    & $csc @common '/define:UNINSTALLER' '/out:Deinstallieren.exe' @sources
    if ($LASTEXITCODE -ne 0) { throw 'csc failed (Deinstallieren.exe)' }
    & $csc @common '/out:LumoraClient-Setup.exe' '/resource:LICENSE.txt,LICENSE.txt' '/resource:Deinstallieren.exe,uninstall.exe' @sources
    if ($LASTEXITCODE -ne 0) { throw 'csc failed (LumoraClient-Setup.exe)' }
} finally { Pop-Location }

$outDir = Split-Path -Parent $Out
if ($outDir) { New-Item -ItemType Directory -Force $outDir | Out-Null }
Copy-Item (Join-Path $stage 'LumoraClient-Setup.exe') $Out -Force
Remove-Item -Recurse -Force $stage
$mb = [math]::Round((Get-Item $Out).Length / 1MB, 1)
Write-Host "Installer: $Out ($mb MB)"
