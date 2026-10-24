# Dev loop: build the player, then capture one plan with a HUD style.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\hud\build_and_capture.ps1 -Plan gauntlet\shots\hud\hud_r1_cruise_lock.json -Style halo -Out gauntlet\captures\hud\try1\halo [-NoBuild]
param([string]$Plan, [string]$Style = "halo", [string]$Out, [switch]$NoBuild, [string]$Extra = "")
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $root
if (-not $NoBuild) { & powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.Build.Player | Select-String -Pattern "error|BUILD|exit" }
& powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture.ps1 -Plan $Plan -Out $Out -PlayerArgs "-mnHudStyle $Style $Extra"
