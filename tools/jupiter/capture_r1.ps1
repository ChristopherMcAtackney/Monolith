<#
.SYNOPSIS
  Takes the Jupiter backdrop's round-one captures from the built player: the planet alone (the mission's Sun, half lit, a crescent) and the Jupiter Skirmish from the cockpit and
  from camera rigs riding the ships, 1920 x 1080, HUD off, each plan into its own folder under -Out.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\jupiter\capture_r1.ps1 -Out gauntlet\captures\jupiter\r1 [-Exe path\Monolith.exe] [-Only planet,mercy_jupiter]

  The plans are written by `python -I tools/jupiter/make_plans.py` and `make_battle_plans.py` (run both first; the battle plans take --times a,b,c and --rig name,name).
  `python -I tools/jupiter/sheet.py <folder> <sheet.png>` makes a contact sheet of a folder to choose frames from.
#>
param([string]$Out = 'gauntlet\captures\jupiter\r1', [string]$Exe = '', [string[]]$Only = @())
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$groups = [ordered]@{
    'planet'               = 'jupiter_r1_planet'
    'planet_half'          = 'jupiter_r1_planet_half'
    'planet_crescent'      = 'jupiter_r1_planet_crescent'
    'planet_shadow'        = 'jupiter_r1_planet_shadow'
    'cockpit'              = 'jupiter_r1_battle_cockpit'
    'mercy_jupiter'        = 'jupiter_r1_battle_mercy_jupiter'
    'mercy_jupiter_close'  = 'jupiter_r1_battle_mercy_jupiter_close'
    'warden_jupiter'       = 'jupiter_r1_battle_warden_jupiter'
    'warden_jupiter_close' = 'jupiter_r1_battle_warden_jupiter_close'
    'fighter_jupiter'      = 'jupiter_r1_battle_fighter_jupiter'
    'mercy'                = 'jupiter_r1_battle_mercy'
    'mercy_side'           = 'jupiter_r1_battle_mercy_side'
    'wing'                 = 'jupiter_r1_battle_wing'
}
foreach ($g in $groups.Keys) {
    if ($Only.Count -gt 0 -and $Only -notcontains $g) { continue }
    $plan = Join-Path $root ("gauntlet\shots\{0}.json" -f $groups[$g])
    $dir = Join-Path $Out $g
    $captureArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tools\capture.ps1'), '-Plan', $plan, '-Out', $dir, '-Kind', 'stills')
    if ($Exe) { $captureArgs += @('-Exe', $Exe) }
    Write-Host "== $g"
    & powershell @captureArgs 2>&1 | Select-String 'CAPTURE DONE|ERROR|FAIL'
}
