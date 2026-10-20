<#
.SYNOPSIS
  Run Blender 5.2 headless with a Python script:  blender -b --factory-startup [file.blend] -P script.py -- args
  The tools\ folder is put on sys.path first, so scripts can import shared helpers placed there.
  Python exceptions give a non-zero exit code. Full output goes to Game\Logs\blender_*.log.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\blender.ps1 art_src\Viper\build_viper.py
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\blender.ps1 art_src\Viper\build_viper.py -Blend art_src\Viper\viper.blend -- --variant damaged

  Options (before --): -Blend <file.blend>  -TimeoutMin <n=20>  -UserPrefs (skip --factory-startup)
  Everything after -- (or any unrecognised -arg) reaches the script as sys.argv after '--'.
  Environment: MN_ROOT is the repo root. Lines the script prints starting with [MN] are echoed in the summary.
#>
$Script = $null; $Blend = $null; $TimeoutMin = 20; $UserPrefs = $false; $ScriptArgs = @(); $passthru = $false
for ($i = 0; $i -lt $args.Count; $i++) {
    $a = "$($args[$i])"
    if ($passthru) { $ScriptArgs += $a; continue }
    switch -regex ($a) {
        '^--$'           { $passthru = $true; break }
        '^-Blend$'       { $i++; $Blend = "$($args[$i])"; break }
        '^-TimeoutMin$'  { $i++; $TimeoutMin = [int]$args[$i]; break }
        '^-UserPrefs$'   { $UserPrefs = $true; break }
        '^-'             { $passthru = $true; $ScriptArgs += $a; break }
        default          { if (-not $Script) { $Script = $a } else { $ScriptArgs += $a } }
    }
}
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\MnCommon.ps1')
if (-not $Script) { Write-Mn 'usage: blender.ps1 <script.py> [-Blend file.blend] [-- script args]' 'Red'; exit 64 }
$Script = (Resolve-Path $Script).Path
if ($Blend) { $Blend = (Resolve-Path $Blend).Path }
if (-not (Test-Path $MnLogs)) { New-Item -ItemType Directory -Force $MnLogs | Out-Null }
$log = Join-Path $MnLogs ("blender_{0}_{1}_{2}.log" -f (Get-Date -Format 'yyyyMMdd_HHmmss'), [IO.Path]::GetFileNameWithoutExtension($Script), $PID)

$argList = @('-b')
if (-not $UserPrefs) { $argList += '--factory-startup' }
if ($Blend) { $argList += $Blend }
$argList += @('--python-exit-code', '1', '--python-expr', "import sys; sys.path.insert(0, r'$MnTools')", '-P', $Script, '--') + $ScriptArgs

Write-Mn "[blender.ps1] $([IO.Path]::GetFileName($Script))  log: $log"
$sw = [Diagnostics.Stopwatch]::StartNew()
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $MnBlender
$psi.Arguments = ConvertTo-MnArgString $argList
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.WorkingDirectory = $MnRoot
$psi.EnvironmentVariables['MN_ROOT'] = $MnRoot
$p = [System.Diagnostics.Process]::Start($psi)
$outTask = $p.StandardOutput.ReadToEndAsync()
$errTask = $p.StandardError.ReadToEndAsync()
$timedOut = -not $p.WaitForExit($TimeoutMin * 60 * 1000)
if ($timedOut) { Stop-MnTree $p.Id }
$p.WaitForExit()
$text = $outTask.Result + "`n" + $errTask.Result
[IO.File]::WriteAllText($log, $text)
$code = if ($timedOut) { 4 } else { $p.ExitCode }
$lines = $text -split "`r?`n"

$errors = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l -match '^Traceback') {
        $block = $l
        for ($j = $i + 1; $j -lt $lines.Count; $j++) { $block += "`n    " + $lines[$j]; if ($lines[$j] -notmatch '^\s') { $i = $j; break } }
        $errors.Add($block)
    } elseif ($l -match '(^|\s)(Error|ERROR)\b[:|]' -or $l -match '^\[MN\]\[ERROR\]' -or $l -match 'Exception:') {
        $errors.Add($l)
    }
}
$color = if ($code -eq 0) { 'Green' } else { 'Red' }
Write-Mn ("[blender.ps1] exit {0} in {1}s  ({2} error lines)" -f $code, [math]::Round($sw.Elapsed.TotalSeconds, 1), $errors.Count) $color
if ($timedOut) { Write-Mn "  TIMED OUT after $TimeoutMin min (killed)" 'Red' }
foreach ($l in ($lines | Where-Object { $_ -match '^\[MN\]' } | Select-Object -Last 30)) { Write-Mn "  $l" }
foreach ($e in ($errors | Select-Object -Last 8)) { Write-Mn "  ERROR $e" 'Red' }
if ($code -ne 0 -and $errors.Count -eq 0) { $lines | Select-Object -Last 15 | ForEach-Object { Write-Mn "  $_" 'DarkGray' } }
Write-Mn "  log: $log"
Remove-MnOldLogs 'blender_*.log' 40
exit $code
