<#
.SYNOPSIS
  Many auto-plays of one mission: every level x every seed, run in parallel, then summarised per level.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\mission_sweep.ps1 -Exe Game\Builds\Win\Monolith.exe -Mission escort -Levels "Very easy,Easy,Medium,Hard,Insane" -Seeds 20 -Out gauntlet\captures\mission-system\r3\sweep

  Runs `Monolith.exe -batchmode -nographics -mnMissionAutoPlay <mission> -mnOut <out>\<Level>_s<seed> -mnDifficulty <Level> -mnSeed <seed>` for
  seed 0..Seeds-1 at each level, -Parallel at a time (default 12), then `python -I tools\mission_sweep_summary.py <out>`: per level, the
  passes out of the seeds, the length of the run, the longest quiet, the most hostiles on the player, and the failed checks.
  -Mission may be a path to a mission file (so a file can be tried without a build). -Seeds can be a count (0..n-1) or a list "3,7,11".
  Options: -Exe  -Mission  -Levels  -Seeds  -Out  -Parallel  -Extra "-mnGoDark"  -NoSummary
#>
$Exe = $null; $Mission = 'escort'; $Levels = 'Very easy,Easy,Medium,Hard'; $Seeds = '10'; $Out = $null; $Parallel = 12; $Extra = ''; $NoSummary = $false
for ($i = 0; $i -lt $args.Count; $i++) {
    $a = "$($args[$i])"
    switch -regex ($a) {
        '^-Exe$'       { $i++; $Exe = "$($args[$i])"; break }
        '^-Mission$'   { $i++; $Mission = "$($args[$i])"; break }
        '^-Levels$'    { $i++; $Levels = "$($args[$i])"; break }
        '^-Seeds$'     { $i++; $Seeds = "$($args[$i])"; break }
        '^-Out$'       { $i++; $Out = "$($args[$i])"; break }
        '^-Parallel$'  { $i++; $Parallel = [int]"$($args[$i])"; break }
        '^-Extra$'     { $i++; $Extra = "$($args[$i])"; break }
        '^-NoSummary$' { $NoSummary = $true; break }
        default        { Write-Host "mission_sweep.ps1: unknown argument '$a'" -ForegroundColor Red; exit 64 }
    }
}
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\MnCommon.ps1')
if (-not $Exe) { $Exe = $MnPlayer }
if (-not [IO.Path]::IsPathRooted($Exe)) { $Exe = Join-Path (Get-Location).ProviderPath $Exe }
if (-not (Test-Path $Exe)) { Write-Mn "[mission_sweep] no player at $Exe" 'Red'; exit 5 }
if (-not $Out) { $Out = Join-Path $MnRoot ("gauntlet\captures\mission-system\sweep_{0}" -f (Get-Date -Format 'yyyyMMdd_HHmmss')) }
if (-not [IO.Path]::IsPathRooted($Out)) { $Out = Join-Path (Get-Location).ProviderPath $Out }
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force $Out | Out-Null
if ($Mission -match '[\\/]' -or $Mission -match '\.json$') { $Mission = [IO.Path]::GetFullPath($Mission) }

$levelList = @($Levels -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$seedList = if ($Seeds -match '^\d+$') { 0..([int]$Seeds - 1) } else { @($Seeds -split ',' | ForEach-Object { [int]$_.Trim() }) }
$jobs = New-Object System.Collections.Generic.List[object]
foreach ($l in $levelList) { foreach ($s in $seedList) { $jobs.Add(@{ Level = $l; Seed = $s }) } }
Write-Mn ("[mission_sweep] {0} runs ({1} levels x {2} seeds), {3} at a time -> {4}" -f $jobs.Count, $levelList.Count, $seedList.Count, $Parallel, $Out)

$running = New-Object System.Collections.Generic.List[object]
$sw = [Diagnostics.Stopwatch]::StartNew()
$done = 0
function Reap {
    for ($k = $running.Count - 1; $k -ge 0; $k--) {
        if ($running[$k].HasExited) { $running.RemoveAt($k); $script:done++ }
    }
}
foreach ($j in $jobs) {
    while ($running.Count -ge $Parallel) { Start-Sleep -Milliseconds 300; Reap }
    $tag = ("{0}_s{1}" -f ($j.Level -replace ' ', ''), $j.Seed)
    $dir = Join-Path $Out $tag
    New-Item -ItemType Directory -Force $dir | Out-Null
    $pa = @('-batchmode', '-nographics', '-logFile', (Join-Path $dir 'player.log'), '-mnMissionAutoPlay', $Mission, '-mnOut', $dir, '-mnDifficulty', $j.Level, '-mnSeed', "$($j.Seed)")
    if ($Extra) { $pa += @($Extra -split ' ' | Where-Object { $_ }) }
    $p = Start-MnProcess -Exe $Exe -ArgList $pa -WorkDir (Split-Path $Exe -Parent)
    $running.Add($p)
}
while ($running.Count -gt 0) { Start-Sleep -Milliseconds 300; Reap }
Write-Mn ("[mission_sweep] {0} runs in {1:0} s" -f $done, $sw.Elapsed.TotalSeconds)
if (-not $NoSummary) {
    python -I (Join-Path $PSScriptRoot 'mission_sweep_summary.py') $Out
}
