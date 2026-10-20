<#
.SYNOPSIS
  Flight-feel traces from the built player, compared against the FS2 reference (design section 4, "Measured feel").

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\flight.ps1 -Out gauntlet\captures\flight-model\r1
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\flight.ps1 -Out some\dir -Ship "GTF Ulysses"

  For each class, runs Game\Builds\Win\Monolith.exe -batchmode -mnFlightScenarios <scenarios> -mnShip <class> -mnOut <out>\<Class>
  (the game replays every scenario through its own flight model at 60 Hz and writes <scenario>\trace.csv), then
  python -I tools\fs2ref\compare.py <out>\<Class> gauntlet\reference\fs2_flight\<Class> --json <out>\<Class>\compare.json,
  saving the table in <out>\<Class>\compare.txt and one line per class in <out>\summary.txt.
  The player's own fighter (vanguard_fighter) is traced too; it is compared with the reference class named by -OwnRef
  (GTF Ulysses, whose numbers it starts on) for information only: it may differ on purpose once tuned.

  Options: -Out <dir>  -Ship a,b (reference classes)  -Scenarios <json>  -Exe <path>  -OwnShip vanguard_fighter  -OwnRef "GTF Ulysses"
  Exit code: 0 every reference class passes every metric; 1 something failed; 5 no build.
#>
$Out = $null; $Ships = @('GTF Ulysses', 'GTF Myrmidon', 'GTF Hercules', 'GTB Artemis'); $Scenarios = $null; $Exe = $null
$OwnShip = 'vanguard_fighter'; $OwnRef = 'GTF Ulysses'
for ($i = 0; $i -lt $args.Count; $i++) {
    $a = "$($args[$i])"
    switch -regex ($a) {
        '^-Out$'       { $i++; $Out = "$($args[$i])"; break }
        '^-Ship$'      { $i++; $Ships = @("$($args[$i])" -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }); break }
        '^-Scenarios$' { $i++; $Scenarios = "$($args[$i])"; break }
        '^-Exe$'       { $i++; $Exe = "$($args[$i])"; break }
        '^-OwnShip$'   { $i++; $OwnShip = "$($args[$i])"; break }
        '^-OwnRef$'    { $i++; $OwnRef = "$($args[$i])"; break }
        default        { Write-Host "flight.ps1: unknown argument '$a'" -ForegroundColor Red; exit 64 }
    }
}
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\MnCommon.ps1')
if (-not $Exe) { $Exe = $MnPlayer }
if (-not (Test-Path $Exe)) { Write-Mn "[flight.ps1] no player at $Exe - run: tools\unity.ps1 Monolith.EditorTools.Build.Player" 'Red'; exit 5 }
if (-not $Scenarios) { $Scenarios = Join-Path $MnRoot 'gauntlet\shots\flight_scenarios.json' }
$Scenarios = (Resolve-Path $Scenarios).Path
if (-not $Out) { $Out = Join-Path $MnRoot ("gauntlet\captures\flight-model\run_{0}" -f (Get-Date -Format 'yyyyMMdd_HHmmss')) }
if (-not [IO.Path]::IsPathRooted($Out)) { $Out = Join-Path (Get-Location).ProviderPath $Out }
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force $Out | Out-Null
$refRoot = Join-Path $MnRoot 'gauntlet\reference\fs2_flight'
$compare = Join-Path $MnTools 'fs2ref\compare.py'

function Invoke-Trace([string]$Ship, [string]$Dir) {
    New-Item -ItemType Directory -Force $Dir | Out-Null
    $log = Join-Path $Dir 'player.log'
    $pargs = @('-batchmode', '-nographics', '-logFile', $log, '-mnFlightScenarios', $Scenarios, '-mnShip', $Ship, '-mnOut', $Dir)
    $p = Start-MnProcess -Exe $Exe -ArgList $pargs -WorkDir (Split-Path $Exe -Parent)
    if (-not $p.WaitForExit(5 * 60 * 1000)) { Stop-MnTree $p.Id; Write-Mn "  $Ship TIMED OUT" 'Red'; return 4 }
    $p.WaitForExit()
    $lines = if (Test-Path $log) { Get-Content $log -Encoding UTF8 } else { @() }
    foreach ($l in ($lines | Where-Object { $_ -match '^\[MN\](\[ERROR\]| FLIGHT DONE)' })) {
        $c = if ($l -match '\[ERROR\]') { 'Red' } else { 'DarkGray' }; Write-Mn "  $l" $c
    }
    return $p.ExitCode
}

function Invoke-Compare([string]$Dir, [string]$RefDir) {
    $txt = Join-Path $Dir 'compare.txt'
    $json = Join-Path $Dir 'compare.json'
    $outText = & python -I $compare $Dir $RefDir --json $json 2>&1
    $code = $LASTEXITCODE
    ($outText | Out-String) | Set-Content -Encoding UTF8 $txt
    $last = @($outText | Where-Object { "$_" -match 'metrics pass' }) | Select-Object -Last 1
    return @{ Code = $code; Line = "$last" }
}

$plock = $null
$summary = New-Object System.Collections.Generic.List[string]
$allPass = $true
try {
    try { $plock = Enter-MnLock -Name 'player' -WaitMin 60 -Purpose 'flight.ps1' } catch { Write-Mn "$_" 'Red'; exit 3 }
    Write-Mn "[flight.ps1] $([IO.Path]::GetFileName($Scenarios)) -> $Out"
    foreach ($ship in $Ships) {
        $folder = $ship -replace ' ', '_'
        $dir = Join-Path $Out $folder
        $code = Invoke-Trace $ship $dir
        if ($code -ne 0) { $allPass = $false; $summary.Add("$folder  TRACE FAILED (player exit $code)"); Write-Mn "  ${ship}: player exit $code" 'Red'; continue }
        $r = Invoke-Compare $dir (Join-Path $refRoot $folder)
        if ($r.Code -ne 0) { $allPass = $false }
        $color = if ($r.Code -eq 0) { 'Green' } else { 'Red' }
        $summary.Add(("{0,-14} {1}  {2}" -f $folder, $(if ($r.Code -eq 0) { 'PASS' } else { 'FAIL' }), $r.Line))
        Write-Mn ("  {0,-14} {1}" -f $folder, $r.Line) $color
    }
    if ($OwnShip) {
        $dir = Join-Path $Out $OwnShip
        $code = Invoke-Trace $OwnShip $dir
        if ($code -ne 0) { $summary.Add("$OwnShip  TRACE FAILED (player exit $code)"); Write-Mn "  ${OwnShip}: player exit $code" 'Red' }
        else {
            $refFolder = $OwnRef -replace ' ', '_'
            $r = Invoke-Compare $dir (Join-Path $refRoot $refFolder)
            $summary.Add(("{0,-14} info vs {1}: {2}" -f $OwnShip, $refFolder, $r.Line))
            Write-Mn ("  {0,-14} (info, vs {1}) {2}" -f $OwnShip, $refFolder, $r.Line) 'Cyan'
        }
    }
    $summary | Set-Content -Encoding UTF8 (Join-Path $Out 'summary.txt')
    Write-Mn "  out: $Out"
    if ($allPass) { exit 0 } else { exit 1 }
} finally {
    Exit-MnLock $plock
}
