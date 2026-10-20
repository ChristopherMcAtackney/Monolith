<#
.SYNOPSIS
  The ONLY way to run Unity on Game\. Holds an exclusive project lock, runs Unity in batchmode,
  waits, and prints a short summary (exit code, compile errors, exceptions, [MN] lines, build result).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 -CompileOnly
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.Setup.Project
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.Build.Player -- -mnDev

  Options (before --): -CompileOnly  -TimeoutMin <n=45>  -LockWaitMin <n=90>  -NoGraphics  -Full
  Everything after -- (or any unrecognised -arg) is passed to Unity verbatim (read it with Monolith.Core.CommandLine).
  Exit code: Unity's exit code; 2 if compile errors were found; 3 lock timeout; 4 Unity timed out.
#>
# Args are parsed by hand so the same call works via `powershell -File` (which mangles `--`) and via `&`.
$Method = $null; $CompileOnly = $false; $TimeoutMin = 45; $LockWaitMin = 90; $NoGraphics = $false; $Full = $false
$UnityArgs = @(); $passthru = $false
for ($i = 0; $i -lt $args.Count; $i++) {
    $a = "$($args[$i])"
    if ($passthru) { $UnityArgs += $a; continue }
    switch -regex ($a) {
        '^--$'                { $passthru = $true; break }
        '^-CompileOnly$'      { $CompileOnly = $true; break }
        '^-NoGraphics$'       { $NoGraphics = $true; break }
        '^-Full$'             { $Full = $true; break }
        '^-TimeoutMin$'       { $i++; $TimeoutMin = [int]$args[$i]; break }
        '^-LockWaitMin$'      { $i++; $LockWaitMin = [int]$args[$i]; break }
        '^-'                  { $passthru = $true; $UnityArgs += $a; break }
        default               { if (-not $Method) { $Method = $a } else { $UnityArgs += $a } }
    }
}
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\MnCommon.ps1')

if (-not $CompileOnly -and -not $Method) {
    Write-Mn 'usage: unity.ps1 <Namespace.Class.Method> [-- extra unity args]   |   unity.ps1 -CompileOnly' 'Red'
    exit 64
}
$tag = if ($CompileOnly) { 'compile' } else { ($Method -split '\.')[-1] }
if (-not (Test-Path $MnLogs)) { New-Item -ItemType Directory -Force $MnLogs | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$log = Join-Path $MnLogs ("unity_{0}_{1}_{2}.log" -f $stamp, $tag, $PID)

$isBuild = $Method -match '\.Build\.'
$lock = $null; $plock = $null
try {
    try {
        $lock = Enter-MnLock -Name 'unity' -WaitMin $LockWaitMin -Purpose "unity.ps1 $tag"
        # Building replaces Builds\Win, so also keep capture.ps1 off the player while we build.
        if ($isBuild) { $plock = Enter-MnLock -Name 'player' -WaitMin $LockWaitMin -Purpose "unity.ps1 $tag (build)" }
    } catch {
        Write-Mn "$_" 'Red'; exit 3
    }

    # Orphaned Unity (its unity.ps1 parent was killed) still holding the project: wait for it, then kill if batchmode.
    $orphans = @(Get-MnUnityOnProject)
    if ($orphans.Count -gt 0) {
        $gui = @($orphans | Where-Object { "$($_.CommandLine)" -notmatch '-batchmode' })
        if ($gui.Count -gt 0) {
            Write-Mn "[unity.ps1] a Unity Editor GUI (PID $($gui[0].ProcessId)) has Game\ open. Close it; batch runs cannot share the project." 'Red'
            exit 3
        }
        Write-Mn "[unity.ps1] orphaned batch Unity PID(s) $($orphans.ProcessId -join ',') still on the project; waiting up to 20 min" 'Yellow'
        $until = (Get-Date).AddMinutes(20)
        while (@(Get-MnUnityOnProject).Count -gt 0 -and (Get-Date) -lt $until) { Start-Sleep -Seconds 3 }
        foreach ($o in @(Get-MnUnityOnProject)) { Write-Mn "[unity.ps1] killing orphan Unity PID $($o.ProcessId)" 'Yellow'; Stop-MnTree $o.ProcessId }
        Start-Sleep -Seconds 2
    }

    $argList = @('-batchmode', '-quit', '-accept-apiupdate', '-projectPath', $MnGame, '-logFile', $log, '-buildTarget', 'Win64')
    if ($NoGraphics) { $argList += '-nographics' }
    if (-not $CompileOnly) { $argList += @('-executeMethod', $Method) }
    if ($UnityArgs.Count -gt 0) { $argList += $UnityArgs }

    Write-Mn "[unity.ps1] $tag  log: $log"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-MnProcess -Exe $MnUnity -ArgList $argList -WorkDir $MnGame
    Set-MnLockChild $lock $p.Id

    # Wait, echoing [MN] progress lines from the log as they appear.
    $pos = 0L; $timedOut = $false; $partial = ''
    $echoed = New-Object 'System.Collections.Generic.HashSet[string]'
    $deadline = (Get-Date).AddMinutes($TimeoutMin)
    while (-not $p.WaitForExit(2000)) {
        if (Test-Path $log) {
            try {
                $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
                if ($fs.Length -gt $pos) {
                    $fs.Position = $pos
                    $sr = New-Object IO.StreamReader($fs)
                    $chunk = $partial + $sr.ReadToEnd(); $pos = $fs.Length
                    $parts = $chunk -split "`r?`n"
                    $partial = $parts[-1]
                    foreach ($l in ($parts | Select-Object -SkipLast 1)) { if ($l -match '^\[MN\]' -and $echoed.Add($l)) { Write-Mn "  $l" 'DarkCyan' } }
                }
                $fs.Dispose()
            } catch { }
        }
        if ((Get-Date) -gt $deadline) { $timedOut = $true; Stop-MnTree $p.Id; break }
    }
    $p.WaitForExit() | Out-Null
    $code = if ($timedOut) { 4 } else { $p.ExitCode }
    $secs = [math]::Round($sw.Elapsed.TotalSeconds, 1)

    # ---------- summary ----------
    $lines = if (Test-Path $log) { Get-Content $log -Encoding UTF8 } else { @() }
    $errs = [ordered]@{}
    $warnCount = 0
    foreach ($l in $lines) {
        if ($l -match '^(?<f>[^\(\r\n]+?)\((?<ln>\d+),(?<col>\d+)\): error (?<code>CS\d+): (?<msg>.*)$') {
            $k = "$($Matches.f):$($Matches.ln):$($Matches.col) $($Matches.code)"
            if (-not $errs.Contains($k)) { $errs[$k] = "$($Matches.f)($($Matches.ln),$($Matches.col)) $($Matches.code): $($Matches.msg)" }
        } elseif ($l -match 'warning CS\d+') { $warnCount++ }
    }
    $exc = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        if ($l -match '^([\w\.]+Exception)(:|$)' -or $l -match '^Unhandled exception' -or $l -match 'executeMethod method .* threw exception' -or
            $l -match "^executeMethod class '.*' could not be found" -or $l -match '^Aborting batchmode due to failure' -or
            $l -match '^Shader error in' -or $l -match '^\[MN\]\[ERROR\]' -or $l -match 'another Unity instance is running') {
            $ctx = $l
            for ($j = 1; $j -le 3 -and ($i + $j) -lt $lines.Count; $j++) {
                $n = $lines[$i + $j]; if ($n -match '^\s*$') { break }; $ctx += "`n      $n"
            }
            if (-not $exc.Contains($ctx)) { $exc.Add($ctx) }
        }
    }
    $mnLines = @($lines | Where-Object { $_ -match '^\[MN\]' -and $_ -cnotmatch '^\[MN\](\[ERROR\]| BUILD )' -and -not $echoed.Contains($_) })
    $buildLines = @($lines | Where-Object { $_ -match '^Build Finished, Result:' -or ($_ -cmatch '^\[MN\] BUILD (Succeeded|Failed|Cancelled|Unknown)' -and -not $echoed.Contains($_)) })

    if ($errs.Count -gt 0 -and $code -eq 0) { $code = 2 }
    $color = if ($code -eq 0) { 'Green' } else { 'Red' }
    Write-Mn ("[unity.ps1] {0} finished: exit {1} in {2}s  (compile errors: {3}, warnings: {4}, exceptions: {5})" -f $tag, $code, $secs, $errs.Count, $warnCount, $exc.Count) $color
    if ($timedOut) { Write-Mn "  TIMED OUT after $TimeoutMin min (killed)" 'Red' }
    foreach ($e in $errs.Values) { Write-Mn "  COMPILE ERROR $e" 'Red' }
    foreach ($e in ($exc | Select-Object -First 12)) { Write-Mn "  EXCEPTION $e" 'Red' }
    if ($exc.Count -gt 12) { Write-Mn "  ... $($exc.Count - 12) more exceptions in the log" 'Red' }
    foreach ($t in ($mnLines | Select-Object -Last 25)) { Write-Mn "  $t" }
    foreach ($b in $buildLines) { Write-Mn "  $b" $color }
    if ($code -ne 0) {
        $lic = @($lines | Where-Object { $_ -match 'No valid Unity Editor license|License is not active|licen[cs]e (has )?expired|Failed to activate|no valid licen' })
        foreach ($x in $lic) { Write-Mn "  LICENSE $x" 'Red' }
        if ($Full -or ($errs.Count -eq 0 -and $exc.Count -eq 0)) {
            Write-Mn '  --- log tail ---'
            $lines | Select-Object -Last 25 | ForEach-Object { Write-Mn "  $_" 'DarkGray' }
        }
    }
    Write-Mn "  log: $log"
    Remove-MnOldLogs 'unity_*.log' 40
    exit $code
} finally {
    Exit-MnLock $plock
    Exit-MnLock $lock
}
