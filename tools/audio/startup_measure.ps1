<#
.SYNOPSIS
  Launch-to-flyable, as the loading screen reports it: run the built player three times in a window with the start probe and print the milestones.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\audio\startup_measure.ps1 [-Out gauntlet\captures\audio\r4\now] [-Runs 3] [-Mission escort] [-Exe path\Monolith.exe]
#>
param([string]$Out = 'gauntlet\captures\audio\r4\now', [int]$Runs = 3, [string]$Mission = 'escort', [string]$Exe = '')
$root = Split-Path (Split-Path $PSScriptRoot)
if (-not $Exe) { $Exe = Join-Path $root 'Game\Builds\Win\Monolith.exe' }
$base = Join-Path $root $Out
New-Item -ItemType Directory -Force $base | Out-Null
foreach ($i in 1..$Runs) {
    $d = Join-Path $base "run$i"; New-Item -ItemType Directory -Force $d | Out-Null
    $p = Start-Process -FilePath $Exe -ArgumentList @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-mnMission', $Mission, '-mnAudioLog', '-mnStartupProbe', '3', '-mnStartupQuit', '-mnOut', $d, '-logFile', "$d\log.txt") -PassThru -Wait
    "run $i exit $($p.ExitCode)"
    Select-String -Path "$d\log.txt" -Pattern 'STARTUP \+|AUDIO library|LOADING done' | Select-Object -First 16 | ForEach-Object { '  ' + $_.Line }
}
