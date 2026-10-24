<#
.SYNOPSIS
  Makes the HUD playtest folder: the built player, Play.bat (takes a mission name and a HUD style), the missions to edit, the font licences and HUD.txt.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\hud\make_playtest.ps1 -PlaytestRoot C:\Users\chris\Monolith\Playtest
#>
param(
    [string]$PlaytestRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'Playtest'),
    [string]$Date = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$Suffix = 'hud',                   # the folder is Monolith_<date>_<suffix>
    [string]$DefaultMission = 'escort',
    [string]$Build = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
if (-not $Build) { $Build = Join-Path $root 'Game\Builds\Win' }
if (-not (Test-Path (Join-Path $Build 'Monolith.exe'))) { throw "no build at $Build (run tools\unity.ps1 Monolith.EditorTools.Build.Player)" }
$out = Join-Path $PlaytestRoot "Monolith_${Date}_$Suffix"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
Write-Host "copying the build to $out"
robocopy $Build $out /E /NFL /NDL /NJH /NJS /NP /XD Monolith_BurstDebugInformation_DoNotShip | Out-Null

# the missions, to edit without a build
$m = Join-Path $out 'Missions'
New-Item -ItemType Directory -Force $m | Out-Null
Get-ChildItem (Join-Path $root 'Game\Assets\Monolith\Resources\Missions') -Filter *.mission.json | Copy-Item -Destination $m

# your own sound
$a = Join-Path $out 'Audio'
New-Item -ItemType Directory -Force (Join-Path $a 'music'), (Join-Path $a 'sfx'), (Join-Path $a 'vo') | Out-Null

# the cabin's numbers, editable next to Play.bat
$cab = Join-Path $root 'Game\Assets\Monolith\Resources\Cabin\cabin.json'
if (Test-Path $cab) { Copy-Item $cab (Join-Path $out 'cabin.json') }

# the font licences travel with the build
$lic = Join-Path $out 'Licences'
New-Item -ItemType Directory -Force $lic | Out-Null
foreach ($f in 'Oxanium', 'B612', 'Jura') { Copy-Item (Join-Path $root "art_src\hud_fonts\$f\OFL.txt") (Join-Path $lic "OFL-$f.txt") }
Copy-Item (Join-Path $root 'docs\licences\fonts.md') (Join-Path $lic 'fonts.md')

# the controls the other playtests use (their invert switches are the player's), if there are any
$ctl = Get-ChildItem $PlaytestRoot -Recurse -Filter controls.json -ErrorAction SilentlyContinue | Where-Object { $_.FullName -ne (Join-Path $out 'controls.json') } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($ctl) { Copy-Item $ctl.FullName (Join-Path $out 'controls.json') } elseif (-not (Test-Path (Join-Path $out 'controls.json'))) { Write-Host 'no controls.json found: the game uses its built-in controls' }

$bat = @'
@echo off
rem Monolith with the new HUD. Play.bat [mission] [halo|meridian|cipher] [window] [veryeasy|easy|medium|hard|insane]
rem   Play.bat                  @@DEFAULT@@ with the HUD style you last chose (halo the first time), fullscreen 1920x1080
rem   Play.bat dogfight cipher  The Dogfight with the Cipher style
rem   Play.bat cabin meridian window   Dead Water in a window with the Meridian style
rem The three styles are explained in HUD.txt. You can change style any time: F9, or the pause screen (D-pad up and down). Your choice is kept in hud.json next to this file.
rem Missions are the .mission.json files in the Missions folder next to this file (read before the built-in ones): edit one and play it again.
rem Type too small or too big for your screen: add  -mnHudText 1.2  (1.0 is the design size) to the line at the bottom.
setlocal
set "MISSION=@@DEFAULT@@"
set "MODE=-screen-fullscreen 1"
set "LEVEL="
set "STYLE="
:args
if "%~1"=="" goto run
if /I "%~1"=="window" goto window
if /I "%~1"=="halo" goto st
if /I "%~1"=="meridian" goto st
if /I "%~1"=="cipher" goto st
if /I "%~1"=="veryeasy" goto l0
if /I "%~1"=="easy" goto l1
if /I "%~1"=="medium" goto l2
if /I "%~1"=="hard" goto l3
if /I "%~1"=="insane" goto l4
set "MISSION=%~1"
goto next
:st
set "STYLE=-mnHudStyle %~1"
goto next
:window
set "MODE=-screen-fullscreen 0"
goto next
:l0
set "LEVEL=-mnDifficulty 0"
goto next
:l1
set "LEVEL=-mnDifficulty 1"
goto next
:l2
set "LEVEL=-mnDifficulty 2"
goto next
:l3
set "LEVEL=-mnDifficulty 3"
goto next
:l4
set "LEVEL=-mnDifficulty 4"
goto next
:next
shift
goto args
:run
set "CTL="
if exist "%~dp0controls.json" set "CTL=-mnControls "%~dp0controls.json""
set "CAB="
if exist "%~dp0cabin.json" set "CAB=-mnCabin "%~dp0cabin.json""
start "" "%~dp0Monolith.exe" %MODE% -screen-width 1920 -screen-height 1080 -mnMission "%MISSION%" %LEVEL% %STYLE% %CTL% %CAB%
'@
$bat = $bat.Replace('@@DEFAULT@@', $DefaultMission)
Set-Content -Path (Join-Path $out 'Play.bat') -Value $bat -Encoding ASCII

# Sandbox: the juice sandbox, with the same HUD on its flight view
$sb = @'
@echo off
rem The juice sandbox (the effects, the boost): Sandbox.bat [halo|meridian|cipher]
set "STYLE="
if not "%~1"=="" set "STYLE=-mnHudStyle %~1"
start "" "%~dp0Monolith.exe" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -mnScene juice %STYLE%
'@
Set-Content -Path (Join-Path $out 'Sandbox.bat') -Value $sb -Encoding ASCII

$hudTxt = Join-Path $root 'tools\hud\HUD.txt'
if (Test-Path $hudTxt) { Copy-Item $hudTxt (Join-Path $out 'HUD.txt') }
Write-Host "done: $out"
