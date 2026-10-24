<#
.SYNOPSIS
  The mission checks from the built player: the mission self-test, then every built-in mission auto-played by the AI against its
  expected event log (Scripts/Mission/README.md, "Checks").

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\mission.ps1 -Out gauntlet\captures\mission-system\r1\checks
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\mission.ps1 -Mission escort -Difficulty Hard -Out some\dir

  Runs Game\Builds\Win\Monolith.exe -batchmode -nographics -mnMissionSelfTest (unless -NoSelfTest), then for each mission
  -mnMissionAutoPlay <mission> -mnOut <out>\<mission> (events.csv, pacing.csv, autoplay.txt, autoplay.json, radio.txt), and writes
  <out>\summary.txt. Missions: every Game\Assets\Monolith\Resources\Missions\*.mission.json unless -Mission a,b names some.

  Options: -Out <dir>  -Mission a,b  -Difficulty <level>  -Seconds <n>  -NoSelfTest  -Exe <path>
  Exit code: 0 everything passes; 1 something failed; 5 no build.
#>
$Out = $null; $Missions = @(); $Difficulty = $null; $Seconds = $null; $NoSelfTest = $false; $Exe = $null
for ($i = 0; $i -lt $args.Count; $i++) {
    $a = "$($args[$i])"
    switch -regex ($a) {
        '^-Out$'        { $i++; $Out = "$($args[$i])"; break }
        '^-Mission$'    { $i++; $Missions = @("$($args[$i])" -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }); break }
        '^-Difficulty$' { $i++; $Difficulty = "$($args[$i])"; break }
        '^-Seconds$'    { $i++; $Seconds = "$($args[$i])"; break }
        '^-NoSelfTest$' { $NoSelfTest = $true; break }
        '^-Exe$'        { $i++; $Exe = "$($args[$i])"; break }
        default         { Write-Host "mission.ps1: unknown argument '$a'" -ForegroundColor Red; exit 64 }
    }
}
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\MnCommon.ps1')
if (-not $Exe) { $Exe = $MnPlayer }
if (-not (Test-Path $Exe)) { Write-Mn "[mission.ps1] no player at $Exe - run: tools\unity.ps1 Monolith.EditorTools.Build.Player" 'Red'; exit 5 }
if ($Missions.Count -eq 0) {
    $Missions = @(Get-ChildItem (Join-Path $MnGame 'Assets\Monolith\Resources\Missions') -Filter '*.mission.json' | ForEach-Object { $_.Name -replace '\.mission\.json$', '' })
}
if (-not $Out) { $Out = Join-Path $MnRoot ("gauntlet\captures\mission-system\run_{0}" -f (Get-Date -Format 'yyyyMMdd_HHmmss')) }
if (-not [IO.Path]::IsPathRooted($Out)) { $Out = Join-Path (Get-Location).ProviderPath $Out }
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force $Out | Out-Null

function Invoke-Player([string[]]$PlayerArgs, [string]$Log, [int]$Minutes) {
    $pargs = @('-batchmode', '-nographics', '-logFile', $Log) + $PlayerArgs
    $p = Start-MnProcess -Exe $Exe -ArgList $pargs -WorkDir (Split-Path $Exe -Parent)
    if (-not $p.WaitForExit($Minutes * 60 * 1000)) { Stop-MnTree $p.Id; return 4 }
    $p.WaitForExit()
    return $p.ExitCode
}

$plock = $null
$summary = New-Object System.Collections.Generic.List[string]
$allPass = $true
try {
    try { $plock = Enter-MnLock -Name 'player' -WaitMin 60 -Purpose 'mission.ps1' } catch { Write-Mn "$_" 'Red'; exit 3 }
    if (-not $NoSelfTest) {
        $log = Join-Path $Out 'selftest.log'
        $code = Invoke-Player @('-mnMissionSelfTest') $log 20
        $line = if (Test-Path $log) { @(Get-Content $log -Encoding UTF8 | Where-Object { $_ -match 'MISSION SELFTEST' }) | Select-Object -Last 1 } else { '' }
        foreach ($l in (Get-Content $log -Encoding UTF8 | Where-Object { $_ -match 'MISSION FAIL' })) { Write-Mn "  $l" 'Red' }
        if ($code -ne 0) { $allPass = $false }
        $summary.Add(("selftest      {0}  {1}" -f $(if ($code -eq 0) { 'PASS' } else { 'FAIL' }), $line))
        Write-Mn ("  selftest  {0}" -f $line) $(if ($code -eq 0) { 'Green' } else { 'Red' })
    }
    foreach ($m in $Missions) {
        $dir = Join-Path $Out $m
        New-Item -ItemType Directory -Force $dir | Out-Null
        $pa = @('-mnMissionAutoPlay', $m, '-mnOut', $dir)
        if ($Difficulty) { $pa += @('-mnDifficulty', $Difficulty) }
        if ($Seconds) { $pa += @('-mnSeconds', $Seconds) }
        $code = Invoke-Player $pa (Join-Path $dir 'player.log') 30
        $txt = Join-Path $dir 'autoplay.txt'
        $head = if (Test-Path $txt) { @(Get-Content $txt -Encoding UTF8 | Select-Object -First 3) -join ' | ' } else { "no report (player exit $code)" }
        if ($code -ne 0) { $allPass = $false }
        $summary.Add(("{0,-13} {1}  {2}" -f $m, $(if ($code -eq 0) { 'PASS' } else { 'FAIL' }), $head))
        Write-Mn ("  {0,-13} {1}" -f $m, $head) $(if ($code -eq 0) { 'Green' } else { 'Red' })
    }
    $summary | Set-Content -Encoding UTF8 (Join-Path $Out 'summary.txt')
    Write-Mn "  out: $Out"
    if ($allPass) { exit 0 } else { exit 1 }
} finally {
    Exit-MnLock $plock
}
