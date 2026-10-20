<#
.SYNOPSIS
  Run the built player (Game\Builds\Win\Monolith.exe) in capture mode: the stills and perf runs of a plan, then summarise.

.EXAMPLE
  # everything in the plan (output dir defaults to gauntlet\captures\<plan>_<stamp>\)
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture.ps1 -Plan gauntlet\shots\smoke.json
  # just one shot, into a chosen folder
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture.ps1 -Plan gauntlet\shots\smoke.json -Only smoke_front -Out gauntlet\captures\try1
  # only the stills, or only the perf runs
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture.ps1 -Plan gauntlet\shots\smoke.json -Kind stills
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture.ps1 -Plan gauntlet\shots\smoke.json -Kind perf

  Options: -Out <dir>  -Only a,b  -Kind stills|perf  -Width 1920 -Height 1080 (the window)  -TimeoutMin 15  -Exe <path>
  Output: <out>\<still>.png, <out>\perf_<run>.json, capture_manifest.json, player.log
  Exit code: the player's (0 ok, 1 a shot or the core self-test failed, 2 bad plan); 3 lock timeout; 4 timed out; 5 no build.
#>
$Plan = $null; $Only = $null; $Kind = $null; $Out = $null; $Width = 1920; $Height = 1080; $TimeoutMin = 15; $Exe = $null
for ($i = 0; $i -lt $args.Count; $i++) {
    $a = "$($args[$i])"
    switch -regex ($a) {
        '^-Plan$'       { $i++; $Plan = "$($args[$i])"; break }
        '^-Only$'       { $i++; $Only = "$($args[$i])"; break }
        '^-Kind$'       { $i++; $Kind = "$($args[$i])"; break }
        '^-Out$'        { $i++; $Out = "$($args[$i])"; break }
        '^-Width$'      { $i++; $Width = [int]$args[$i]; break }
        '^-Height$'     { $i++; $Height = [int]$args[$i]; break }
        '^-TimeoutMin$' { $i++; $TimeoutMin = [int]$args[$i]; break }
        '^-Exe$'        { $i++; $Exe = "$($args[$i])"; break }
        default         { Write-Host "capture.ps1: unknown argument '$a'" -ForegroundColor Red; exit 64 }
    }
}
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\MnCommon.ps1')
if (-not $Plan) { Write-Mn 'usage: capture.ps1 -Plan plan.json [-Only a,b] [-Kind stills|perf] [-Out dir] [-Width 1920 -Height 1080]' 'Red'; exit 64 }
if ($Kind -and $Kind -notin @('stills', 'perf')) { Write-Mn "capture.ps1: -Kind must be stills or perf, not '$Kind'" 'Red'; exit 64 }
if (-not $Exe) { $Exe = $MnPlayer }
if (-not (Test-Path $Exe)) { Write-Mn "[capture.ps1] no player at $Exe - run: tools\unity.ps1 Monolith.EditorTools.Build.Player" 'Red'; exit 5 }
if (-not (Test-Path $Plan)) { Write-Mn "[capture.ps1] plan not found: $Plan" 'Red'; exit 64 }
$Plan = (Resolve-Path $Plan).Path
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
if (-not $Out) { $Out = Join-Path $MnRoot ("gauntlet\captures\{0}_{1}" -f [IO.Path]::GetFileNameWithoutExtension($Plan), $stamp) }
if (-not [IO.Path]::IsPathRooted($Out)) { $Out = Join-Path (Get-Location).ProviderPath $Out }
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force $Out | Out-Null
$playerLog = Join-Path $Out 'player.log'

# A plan with perf runs also waits for the Unity lock, so a compile or build cannot steal CPU/GPU from the measurement.
$planObj = Get-Content $Plan -Raw | ConvertFrom-Json
$wantsPerf = ($Kind -ne 'stills') -and (@($planObj.perf | Where-Object { $_ }).Count -gt 0)

$pargs = @('-screen-fullscreen', '0', '-screen-width', "$Width", '-screen-height', "$Height", '-logFile', $playerLog, '-mnCapture', $Plan, '-mnOut', $Out)
if ($Only) { $pargs += @('-mnOnly', $Only) }
if ($Kind) { $pargs += @('-mnKind', $Kind) }

$ulock = $null; $plock = $null
try {
    try {
        if ($wantsPerf) { $ulock = Enter-MnLock -Name 'unity' -WaitMin 60 -Purpose 'capture.ps1 perf' }
        $plock = Enter-MnLock -Name 'player' -WaitMin 60 -Purpose ("capture.ps1 " + [IO.Path]::GetFileName($Plan))
    } catch { Write-Mn "$_" 'Red'; exit 3 }

    Write-Mn ("[capture.ps1] {0} -> {1}" -f [IO.Path]::GetFileName($Plan), $Out)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-MnProcess -Exe $Exe -ArgList $pargs -WorkDir (Split-Path $Exe -Parent)
    Set-MnLockChild $plock $p.Id
    $timedOut = -not $p.WaitForExit($TimeoutMin * 60 * 1000)
    if ($timedOut) { Stop-MnTree $p.Id }
    $p.WaitForExit()
    $code = if ($timedOut) { 4 } else { $p.ExitCode }
    $secs = [math]::Round($sw.Elapsed.TotalSeconds, 1)

    $lines = if (Test-Path $playerLog) { Get-Content $playerLog -Encoding UTF8 } else { @() }
    $exc = @($lines | Where-Object { $_ -match '^\w[\w\.]*Exception(:|$)' } | Select-Object -Unique)
    $color = if ($code -eq 0) { 'Green' } else { 'Red' }
    Write-Mn ("[capture.ps1] player exit {0} in {1}s  ({2} distinct exceptions in player log)" -f $code, $secs, $exc.Count) $color
    if ($timedOut) { Write-Mn "  TIMED OUT after $TimeoutMin min (killed)" 'Red' }
    foreach ($l in ($lines | Where-Object { $_ -match '^\[MN\]' })) {
        $c = if ($l -match '^\[MN\]\[ERROR\]') { 'Red' } elseif ($l -match '^\[MN\]\[WARN\]') { 'Yellow' } else { 'Gray' }
        Write-Mn "  $l" $c
    }
    foreach ($e in ($exc | Select-Object -First 8)) { Write-Mn "  EXCEPTION $e" 'Red' }
    if ($code -ne 0 -and -not ($lines | Where-Object { $_ -match '^\[MN\]' })) { $lines | Select-Object -Last 20 | ForEach-Object { Write-Mn "  $_" 'DarkGray' } }

    $manifest = Join-Path $Out 'capture_manifest.json'
    if (Test-Path $manifest) {
        $m = Get-Content $manifest -Raw | ConvertFrom-Json
        foreach ($r in $m.results) {
            if ($r.status -ne 'ok') { Write-Mn "  $($r.kind) $($r.name): $($r.status) $($r.error)" 'Red'; continue }
            if ($r.kind -eq 'still') { Write-Mn "  still $($r.name): $($r.file)" 'Cyan' }
            else { Write-Mn ("  perf  {0}: avg {1} fps, 1% low {2} fps, worst {3} ms  ({4})" -f $r.name, $r.perf.avgFps, $r.perf.low1PctFps, $r.perf.worstFrameMs, $r.file) 'Cyan' }
        }
    } elseif ($code -eq 0) { Write-Mn "  (no capture_manifest.json was written)" 'Yellow' }
    Write-Mn "  out: $Out"
    exit $code
} finally {
    Exit-MnLock $plock
    Exit-MnLock $ulock
}
