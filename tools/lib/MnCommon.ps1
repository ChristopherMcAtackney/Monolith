# Shared helpers for tools\*.ps1 (dot-source this file). Windows PowerShell 5.1 compatible.

$script:MnRoot    = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:MnTools   = Join-Path $script:MnRoot 'tools'
$script:MnGame    = Join-Path $script:MnRoot 'Game'
$script:MnLogs    = Join-Path $script:MnGame 'Logs'
$script:MnLockDir = Join-Path $script:MnTools '.locks'
$script:MnUnity   = 'C:\Program Files\Unity\Hub\Editor\2022.3.26f1\Editor\Unity.exe'
$script:MnBlender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
$script:MnPlayer  = Join-Path $script:MnGame 'Builds\Win\Monolith.exe'

function Write-Mn([string]$msg, [string]$color = 'Gray') { Write-Host $msg -ForegroundColor $color }

function Test-MnPidAlive([int]$ProcessId) {
    if ($ProcessId -le 0) { return $false }
    return [bool](Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Read-MnLockInfo([string]$Path) {
    try {
        $fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
        try {
            $sr = New-Object System.IO.StreamReader($fs)
            $txt = $sr.ReadToEnd()
        } finally { $fs.Dispose() }
        if ($txt) { return ($txt | ConvertFrom-Json) }
    } catch { }
    return $null
}

# Exclusive named lock. The lock file holds the holder's PID as JSON. The OS file handle is the real lock:
# it is held open (deny-write) for the whole run, so a crashed or killed holder releases it automatically,
# and a leftover file from a dead PID is a stale lock that is simply taken over and rewritten.
function Enter-MnLock {
    param([string]$Name, [int]$WaitMin = 90, [string]$Purpose = '')
    if (-not (Test-Path $script:MnLockDir)) { New-Item -ItemType Directory -Force $script:MnLockDir | Out-Null }
    $path = Join-Path $script:MnLockDir "$Name.lock"
    $deadline = (Get-Date).AddMinutes($WaitMin)
    $announced = $false
    while ($true) {
        $fs = $null
        try {
            $fs = [System.IO.File]::Open($path, 'OpenOrCreate', 'ReadWrite', 'Read')
        } catch [System.IO.IOException] {
            $fs = $null
        }
        if ($fs) {
            $old = $null
            try {
                $sr = New-Object System.IO.StreamReader($fs, [Text.Encoding]::UTF8, $false, 1024, $true)
                $prev = $sr.ReadToEnd(); $sr.Dispose()
                if ($prev) { $old = $prev | ConvertFrom-Json }
            } catch { }
            if ($old -and $old.pid -and ($old.pid -ne $PID)) {
                if (Test-MnPidAlive ([int]$old.pid)) {
                    # A live PID wrote the file but no longer holds the handle (should not happen); treat as stale.
                    Write-Mn "[lock] '$Name': previous holder PID $($old.pid) is alive but released the handle; taking over" 'Yellow'
                } else {
                    Write-Mn "[lock] '$Name': cleared stale lock from dead PID $($old.pid) ($($old.purpose))" 'Yellow'
                }
            }
            $info = [ordered]@{ pid = $PID; started = (Get-Date).ToString('s'); purpose = $Purpose; childPid = 0 } | ConvertTo-Json -Compress
            $bytes = [Text.Encoding]::UTF8.GetBytes($info)
            $fs.SetLength(0); $fs.Position = 0; $fs.Write($bytes, 0, $bytes.Length); $fs.Flush()
            return [pscustomobject]@{ Name = $Name; Path = $path; Stream = $fs; Purpose = $Purpose }
        }
        $holder = Read-MnLockInfo $path
        if (-not $announced) {
            $who = if ($holder) { "PID $($holder.pid) [$($holder.purpose)] since $($holder.started)" } else { 'unknown holder' }
            Write-Mn "[lock] waiting for '$Name' lock held by $who (max $WaitMin min)" 'Yellow'
            $announced = $true
        }
        if ((Get-Date) -gt $deadline) { throw "MN_LOCK_TIMEOUT: gave up waiting $WaitMin min for lock '$Name'" }
        Start-Sleep -Seconds 3
    }
}

function Set-MnLockChild($Lock, [int]$ChildPid) {
    if (-not $Lock) { return }
    $info = [ordered]@{ pid = $PID; started = (Get-Date).ToString('s'); purpose = $Lock.Purpose; childPid = $ChildPid } | ConvertTo-Json -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($info)
    $Lock.Stream.SetLength(0); $Lock.Stream.Position = 0; $Lock.Stream.Write($bytes, 0, $bytes.Length); $Lock.Stream.Flush()
}

function Exit-MnLock($Lock) {
    if ($Lock -and $Lock.Stream) {
        try { $Lock.Stream.SetLength(0); $Lock.Stream.Flush() } catch { }
        $Lock.Stream.Dispose()
    }
}

function ConvertTo-MnArgString([string[]]$ArgList) {
    $parts = foreach ($a in $ArgList) {
        if ($null -eq $a) { continue }
        if ($a -eq '' -or $a -match '[\s"]') { '"' + ($a -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' } else { $a }
    }
    return ($parts -join ' ')
}

function Start-MnProcess([string]$Exe, [string[]]$ArgList, [string]$WorkDir) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    $psi.Arguments = ConvertTo-MnArgString $ArgList
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    if ($WorkDir) { $psi.WorkingDirectory = $WorkDir }
    $p = [System.Diagnostics.Process]::Start($psi)
    $null = $p.Handle  # keep a handle so ExitCode is readable after exit
    return $p
}

function Stop-MnTree([int]$ProcessId) {
    & taskkill.exe /T /F /PID $ProcessId 2>&1 | Out-Null
}

# Unity.exe processes that have Game\ open (any user, batch or GUI).
function Get-MnUnityOnProject {
    $needle1 = ($script:MnGame).ToLowerInvariant()
    $needle2 = $needle1.Replace('\', '/')
    Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue | Where-Object {
        $cl = "$($_.CommandLine)".ToLowerInvariant()
        $cl.Contains($needle1) -or $cl.Contains($needle2)
    }
}

function Remove-MnOldLogs([string]$Pattern, [int]$Keep = 40) {
    if (-not (Test-Path $script:MnLogs)) { return }
    Get-ChildItem $script:MnLogs -Filter $Pattern -File | Sort-Object LastWriteTime -Descending |
        Select-Object -Skip $Keep | Remove-Item -Force -ErrorAction SilentlyContinue
}
