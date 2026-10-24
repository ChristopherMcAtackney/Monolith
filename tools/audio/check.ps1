<#
.SYNOPSIS
  Every audio check, and the other systems' self-tests, against the built player. Exit code 0 only if they all pass.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\audio\check.ps1 [-Out gauntlet\captures\audio\check]
  (build first: tools\unity.ps1 Monolith.EditorTools.Build.Player; and build the tools: dotnet build -c Release tools\audio)
#>
param([string]$Out = 'gauntlet\captures\audio\check')
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$exe = Join-Path $root 'Game\Builds\Win\Monolith.exe'
if (-not (Test-Path $exe)) { throw "no build at $exe" }
$out = Join-Path $root $Out
New-Item -ItemType Directory -Force $out | Out-Null
$failed = 0

function Run($name, $exeArgs) {
    $log = Join-Path $out "$name.log"
    $p = Start-Process -FilePath $exe -ArgumentList $exeArgs -PassThru -Wait -WindowStyle Hidden
    $ok = $p.ExitCode -eq 0
    Write-Host ("{0,-22} {1}" -f $name, $(if ($ok) { 'pass' } else { "FAIL (exit $($p.ExitCode))" }))
    if (-not $ok) { $script:failed++ }
}

$tool = Join-Path $root 'tools\audio\bin\Release\net9.0\MnAudio.dll'
if (Test-Path $tool) {
    & dotnet $tool mixertest | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0) { $failed++ }
}
Run 'audio_selftest' @('-batchmode', '-nographics', '-mnAudioSelfTest', '-mnOut', $out, '-logFile', (Join-Path $out 'audio_selftest_player.log'))
Run 'combat_selftest'  @('-batchmode', '-nographics', '-mnCombatSelfTest', '-logFile', (Join-Path $out 'combat.log'))
Run 'ai_selftest'      @('-batchmode', '-nographics', '-mnAiSelfTest', '-logFile', (Join-Path $out 'ai.log'))
Run 'mission_selftest' @('-batchmode', '-nographics', '-mnMissionSelfTest', '-logFile', (Join-Path $out 'mission.log'))
Run 'juice_selftest'   @('-batchmode', '-nographics', '-mnScene', 'juice', '-mnJuiceSelfTest', '-logFile', (Join-Path $out 'juice.log'))
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\flight.ps1') -Out (Join-Path $out 'flight') | Select-Object -Last 2
if ($LASTEXITCODE -ne 0) { $failed++ }
Write-Host $(if ($failed -eq 0) { 'ALL CHECKS PASS' } else { "$failed CHECK(S) FAILED" })
exit $(if ($failed -eq 0) { 0 } else { 1 })
