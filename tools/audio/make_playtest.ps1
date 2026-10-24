<#
.SYNOPSIS
  Makes the audio playtest folder: the built player, Play.bat (takes a mission name), the missions to edit, the Audio folder for your own sound, and SOUND.txt.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\audio\make_playtest.ps1 -PlaytestRoot C:\Users\chris\Monolith\Playtest
#>
param(
    [string]$PlaytestRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'Playtest'),
    [string]$Date = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$Suffix = 'audio',                 # the folder is Monolith_<date>_<suffix>
    [string]$DefaultMission = 'escort'         # what Play.bat starts when it is given no mission
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$build = Join-Path $root 'Game\Builds\Win'
if (-not (Test-Path (Join-Path $build 'Monolith.exe'))) { throw "no build at $build (run tools\unity.ps1 Monolith.EditorTools.Build.Player)" }
$out = Join-Path $PlaytestRoot "Monolith_${Date}_$Suffix"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
Write-Host "copying the build to $out"
robocopy $build $out /E /NFL /NDL /NJH /NJS /NP /XD Monolith_BurstDebugInformation_DoNotShip | Out-Null
# A Unity build does not delete a streaming file that has since been removed from the project (a sound taken out of the game lingers in Builds\Win and plays in the playtest): the
# built-in sound in the playtest is made exactly the project's, and nothing else.
$projAudio = Join-Path $root 'Game\Assets\StreamingAssets\Audio'
robocopy $projAudio (Join-Path $out 'Monolith_Data\StreamingAssets\Audio') /MIR /XF *.meta /XD .pcm /NFL /NDL /NJH /NJS /NP | Out-Null

# the decoded-sound cache (Audio\.pcm: the sound as plain PCM, which a start reads in a fifth of a second instead of the 2.3 s Unity takes to decode it on the main thread; Scripts/Audio/Runtime/PcmCache.cs),
# made fresh here so the first start in this folder is as quick as the next ones (it would make it itself otherwise, a start slower, once)
$pcm = Join-Path $out 'Monolith_Data\StreamingAssets\Audio\.pcm'
if (Test-Path $pcm) { Remove-Item -Recurse -Force $pcm }
$warm = Join-Path $env:TEMP 'mn_audio_cache_build'
New-Item -ItemType Directory -Force $warm | Out-Null
$p = Start-Process -FilePath (Join-Path $out 'Monolith.exe') -ArgumentList @('-batchmode', '-nographics', '-mnAudioSelfTest', '-mnAudioCacheBuild', '-mnOut', $warm, '-logFile', (Join-Path $warm 'cache.log')) -PassThru -Wait -WindowStyle Hidden
if ((Test-Path $pcm)) {
    $files = Get-ChildItem $pcm -Recurse -File -Filter *.mnpcm
    Write-Host ("audio cache: {0} files, {1:0} MB" -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))
} else { Write-Host "the audio cache was not made (exit $($p.ExitCode)); the first start will make it" }

# the missions, to edit without a build
$m = Join-Path $out 'Missions'
New-Item -ItemType Directory -Force $m | Out-Null
Get-ChildItem (Join-Path $root 'Game\Assets\Monolith\Resources\Missions') -Filter *.mission.json | Copy-Item -Destination $m

# your own sound
$a = Join-Path $out 'Audio'
New-Item -ItemType Directory -Force (Join-Path $a 'music'), (Join-Path $a 'sfx'), (Join-Path $a 'vo') | Out-Null
Copy-Item (Join-Path $root 'Game\Assets\StreamingAssets\Audio\AUDIO_LIST.txt') (Join-Path $out 'AUDIO_LIST.txt')
Copy-Item (Join-Path $root 'tools\audio\SOUND.txt') (Join-Path $out 'SOUND.txt')
# your own music: how, and a music.json with every slot listed and none set (the built-in music plays until you give a slot a file)
Copy-Item (Join-Path $root 'tools\audio\MUSIC.txt') (Join-Path $out 'MUSIC.txt')
Copy-Item (Join-Path $root 'tools\audio\music.example.json') (Join-Path $out 'music.json')
# the cabin's numbers, editable next to Play.bat (the build reads its own if this is absent)
$cab = Join-Path $root 'Game\Assets\Monolith\Resources\Cabin\cabin.json'
if (Test-Path $cab) { Copy-Item $cab (Join-Path $out 'cabin.json') }
# the voices heard without playing: the old calm reads against the tense and frantic ones (tools\audio\make_voice_reel.py)
$reel = Join-Path $root 'gauntlet\captures\audio\r1\clips\frantic_reel.mp3'
if (Test-Path $reel) { Copy-Item $reel (Join-Path $out 'frantic_reel.mp3') }

# the controls the other playtests use, if there are any
$ctl = Get-ChildItem $PlaytestRoot -Recurse -Filter controls.json -ErrorAction SilentlyContinue | Where-Object { $_.FullName -ne (Join-Path $out 'controls.json') } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($ctl) { Copy-Item $ctl.FullName (Join-Path $out 'controls.json') } elseif (-not (Test-Path (Join-Path $out 'controls.json'))) { Write-Host 'no controls.json found: the game uses its built-in controls' }

# what Play.bat's words mean, for the user (tools\audio\OPTIONS.txt)
Copy-Item (Join-Path $root 'tools\audio\OPTIONS.txt') (Join-Path $out 'OPTIONS.txt')

$bat = @'
@echo off
rem Monolith playtest. Play.bat [words, in any order]: OPTIONS.txt lists every one.
rem   Play.bat                       @@DEFAULT@@ (the default mission), fullscreen 1920x1080, Medium
rem   Play.bat oort hard skip god    The Oculus on Hard, no briefing, you can't die
rem   Play.bat flight window         free flight, in a window
rem   Words starting with - (and numbers) go to the game as they are: Play.bat oort -mnFps 30
rem Missions are the .mission.json files in the Missions folder next to this file (read before the built-in ones): edit one and play it again.
rem Your own music, effects and voices go in the Audio folder (see SOUND.txt). The log, with every line of the radio and every music change, is Monolith_audio_log.txt.
setlocal
set "MISSION=@@DEFAULT@@"
set "SCENE="
set "MODE=-screen-fullscreen 1"
set "LEVEL="
set "EXTRA="
:args
if "%~1"=="" goto run
set "W=%~1"
if /I "%W%"=="window" goto window
if /I "%W%"=="veryeasy" goto l0
if /I "%W%"=="easy" goto l1
if /I "%W%"=="medium" goto l2
if /I "%W%"=="hard" goto l3
if /I "%W%"=="insane" goto l4
if /I "%W%"=="flight" goto scene
if /I "%W%"=="range" goto scene
if /I "%W%"=="juice" goto scene
if /I "%W%"=="god" set "EXTRA=%EXTRA% -mnInvulnerable" & goto next
if /I "%W%"=="skip" set "EXTRA=%EXTRA% -mnSkipBriefing" & goto next
if /I "%W%"=="ai" set "EXTRA=%EXTRA% -mnAutopilot" & goto next
if /I "%W%"=="nohud" set "EXTRA=%EXTRA% -mnNoHud" & goto next
if /I "%W%"=="halo" set "EXTRA=%EXTRA% -mnHudStyle halo" & goto next
if /I "%W%"=="meridian" set "EXTRA=%EXTRA% -mnHudStyle meridian" & goto next
if /I "%W%"=="cipher" set "EXTRA=%EXTRA% -mnHudStyle cipher" & goto next
if /I "%W%"=="nocabin" set "EXTRA=%EXTRA% -mnNoCabin" & goto next
if /I "%W%"=="norumble" set "EXTRA=%EXTRA% -mnNoRumble" & goto next
if /I "%W%"=="noload" set "EXTRA=%EXTRA% -mnNoLoadingScreen" & goto next
if /I "%W%"=="nosound" set "EXTRA=%EXTRA% -mnNoAudio" & goto next
if "%W:~0,1%"=="-" goto extra
echo(%W%| findstr /r /x "[0-9.]*" >nul
if not errorlevel 1 goto extra
set "MISSION=%W%"
set "SCENE="
goto next
:extra
set "EXTRA=%EXTRA% %W%"
goto next
:scene
set "SCENE=%W%"
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
set "WHAT=-mnMission "%MISSION%""
if defined SCENE set "WHAT=-mnScene %SCENE%"
set "CTL="
if exist "%~dp0controls.json" set "CTL=-mnControls "%~dp0controls.json""
set "CAB="
if exist "%~dp0cabin.json" set "CAB=-mnCabin "%~dp0cabin.json""
start "" "%~dp0Monolith.exe" %MODE% -screen-width 1920 -screen-height 1080 %WHAT% %LEVEL% %CTL% %CAB%%EXTRA% -mnAudioLog -logFile "%~dp0Monolith_audio_log.txt"
'@
$bat.Replace('@@DEFAULT@@', $DefaultMission) | Set-Content -Encoding ASCII (Join-Path $out 'Play.bat')

Write-Host "playtest folder: $out"
Write-Host ("size: {0:N0} MB" -f ((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))
