<#
.SYNOPSIS
  Makes the loading screen's playtest folder: the HUD playtest folder (the built player, Play.bat, the missions, the licences) with Play.bat taking `noloading` too, and LOADING.txt.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\loading\make_playtest.ps1 -PlaytestRoot C:\Users\chris\Monolith\Playtest
#>
param(
    [string]$PlaytestRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'Playtest'),
    [string]$Date = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$Suffix = 'loading',
    [string]$DefaultMission = 'escort',
    [string]$Build = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
if (-not $Build) { $Build = Join-Path $root 'Game\Builds\Win' }
$out = Join-Path $PlaytestRoot "Monolith_${Date}_$Suffix"
if (Test-Path $out) { throw "$out already exists (it may be running): pick another -Suffix or -Date" }

# a clean build: the figures of the last run on the build machine do not travel
$stats = Join-Path $Build 'loading_stats.json'
if (Test-Path -LiteralPath $stats) { Remove-Item -LiteralPath $stats }

& (Join-Path $root 'tools\hud\make_playtest.ps1') -PlaytestRoot $PlaytestRoot -Date $Date -Suffix $Suffix -DefaultMission $DefaultMission -Build $Build

# Play.bat: `noloading`, and a word about the screen
$bat = Join-Path $out 'Play.bat'
$t = Get-Content -Raw $bat
$t = $t -replace "`r?`n", "`r`n"
$t = $t.Replace('rem Monolith with the new HUD. Play.bat [mission] [halo|meridian|cipher] [window] [veryeasy|easy|medium|hard|insane]',
                'rem Monolith with the loading screen. Play.bat [mission] [halo|meridian|cipher] [window] [noloading] [veryeasy|easy|medium|hard|insane]')
$t = $t.Replace('rem The three styles are explained in HUD.txt.', "rem   Play.bat escort noloading   the old start with no screen, to compare (LOADING.txt says what to look at)`r`nrem The three styles are explained in HUD.txt.")
$t = $t.Replace('set "STYLE="' + "`r`n" + ':args', 'set "STYLE="' + "`r`n" + 'set "LOAD="' + "`r`n" + ':args')
$t = $t.Replace('if /I "%~1"=="window" goto window', 'if /I "%~1"=="window" goto window' + "`r`n" + 'if /I "%~1"=="noloading" goto nol')
$t = $t.Replace(':window' + "`r`n", ':nol' + "`r`n" + 'set "LOAD=-mnNoLoadingScreen"' + "`r`n" + 'goto next' + "`r`n" + ':window' + "`r`n")
$t = $t.Replace('%LEVEL% %STYLE% %CTL% %CAB%', '%LEVEL% %STYLE% %LOAD% %CTL% %CAB%')
if ($t -notmatch 'LOAD=-mnNoLoadingScreen' -or $t -notmatch '%STYLE% %LOAD%') { throw 'Play.bat did not take the noloading switch (the HUD script changed?)' }
Set-Content -Path $bat -Value $t -Encoding ASCII

Copy-Item (Join-Path $root 'tools\loading\LOADING.txt') (Join-Path $out 'LOADING.txt')
New-Item -ItemType Directory -Force (Join-Path $out 'Loading') | Out-Null
Write-Host "done: $out"
