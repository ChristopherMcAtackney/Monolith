# Capture every HUD moment (gauntlet\shots\hud\hud_r1_*.json) in every HUD style, into <Out>\<style>\<still>.png, then make a side-by-side sheet per still.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\hud\capture_moments.ps1 -Out gauntlet\captures\hud\r1 [-Styles halo,meridian,cipher] [-Only cruise_lock,map] [-Exe path\Monolith.exe] [-Width 1920 -Height 1080] [-PlanGlob hud_r2_*.json] [-Extra "-mnHudScale 1"]
param([string]$Out = "gauntlet\captures\hud\r1", [string[]]$Styles = @("halo", "meridian", "cipher"), [string[]]$Only = @(), [string]$Exe = "", [int]$Width = 1920, [int]$Height = 1080, [string]$Extra = "", [string]$PlanGlob = "hud_r1_*.json")
$ErrorActionPreference = 'Stop'
$Styles = @($Styles | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $root
$plans = Get-ChildItem (Join-Path "gauntlet\shots\hud" $PlanGlob) | Where-Object { $_.BaseName -notmatch 'perf' }
if ($Only.Count -gt 0) { $plans = $plans | Where-Object { $n = $_.BaseName -replace '^hud_r\d+_', ''; $Only -contains $n } }
foreach ($style in $Styles) {
    foreach ($plan in $plans) {
        $tmp = Join-Path $Out ("_tmp_" + $style + "_" + $plan.BaseName)
        $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "tools\capture.ps1", "-Plan", $plan.FullName, "-Out", $tmp, "-Width", $Width, "-Height", $Height, "-PlayerArgs", "-mnHudStyle $style $Extra")
        if ($Exe) { $args += @("-Exe", $Exe) }
        & powershell @args | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "capture failed: $style $($plan.BaseName) (exit $LASTEXITCODE)" -ForegroundColor Red }
        $dest = Join-Path $Out $style
        New-Item -ItemType Directory -Force $dest | Out-Null
        Get-ChildItem $tmp -Filter *.png | ForEach-Object { Copy-Item $_.FullName (Join-Path $dest $_.Name) -Force }
        if (Test-Path (Join-Path $tmp "player.log")) { Copy-Item (Join-Path $tmp "player.log") (Join-Path $dest ("log_" + $plan.BaseName + ".txt")) -Force }
        Remove-Item $tmp -Recurse -Force
        Write-Host "$style $($plan.BaseName) done"
    }
}
& python -I tools\hud\make_sheets.py --root $Out --styles ($Styles -join ",")
