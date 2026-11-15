# Builds LumoraClient-Setup.exe using the csc.exe of the .NET Framework 4.8 that ships with Windows. One program: its
# copy Deinstallieren.exe in the program folder is the uninstaller. No other tools and no launcher build needed: the
# setup downloads the newest launcher from the GitHub releases of LumoraClient/lumora-launcher when it runs.
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
# Windows file version (Properties > Details): numeric part of -Version, e.g. 1.0.2, or 0.0.0 for CI builds "0.0.0-abc1234"
$numeric = if ($Version -match '^\d+(\.\d+){0,3}') { $Matches[0] } else { '0' }
$parts = @($numeric.Split('.')); while ($parts.Count -lt 4) { $parts += '0' }
$fileVersion = $parts -join '.'
[System.IO.File]::WriteAllText((Join-Path $stage 'BuildVersion.cs'),
    "[assembly: System.Reflection.AssemblyVersion(`"$fileVersion`")]`n" +
    "[assembly: System.Reflection.AssemblyFileVersion(`"$fileVersion`")]`n" +
    "[assembly: System.Reflection.AssemblyInformationalVersion(`"$Version`")]`n")
$manifest = [System.IO.File]::ReadAllText((Join-Path $repo 'assets\app.manifest')).Replace('@VERSION@', $fileVersion)
[System.IO.File]::WriteAllText((Join-Path $stage 'app.manifest'), $manifest)
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
$common = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/win32icon:lumora.ico',
    '/win32manifest:app.manifest') + $refs + $res

Push-Location $stage
try {
    & $csc @common '/out:LumoraClient-Setup.exe' '/resource:LICENSE.txt,LICENSE.txt' @sources
    if ($LASTEXITCODE -ne 0) { throw 'csc failed (LumoraClient-Setup.exe)' }
} finally { Pop-Location }

$outDir = Split-Path -Parent $Out
if ($outDir) { New-Item -ItemType Directory -Force $outDir | Out-Null }
Copy-Item (Join-Path $stage 'LumoraClient-Setup.exe') $Out -Force
Remove-Item -Recurse -Force $stage
$mb = [math]::Round((Get-Item $Out).Length / 1MB, 1)
Write-Host "Installer: $Out ($mb MB)"
