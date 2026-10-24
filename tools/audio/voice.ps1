<#
.SYNOPSIS
  Voices every radio line in the game: one command for a mission that has added lines. Gathers the lines, generates the missing clips on the GPU (in WSL), makes the radio
  version of every clip, rebuilds the index and AUDIO_LIST.txt, and tells you what is left to do.

.DESCRIPTION
  1. python audio_src/voice/collect_lines.py     every line in Resources/Missions/*.mission.json and Resources/AI/radio.json, each tagged calm, tense or frantic
                                                 (INTENSITY in that script, with the reason beside each; an untagged mission message is guessed and reported)
  2. wsl audio_src/voice/run_voice.sh            Breeze-TTS-2 in WSL, only new or changed lines, resumable, restarts itself on a CUDA fault (5 to 25 s a line)
  3. MnAudio voice                               the direction, the radio and the OGG for every clip, vo/index.json
  4. MnAudio docs                                Audio/AUDIO_LIST.txt
  Then rebuild the player (tools\unity.ps1 Monolith.EditorTools.Build.Player) and run tools\audio\check.ps1: the audio self-test and the mission self-test FAIL for any radio line of any
  built-in mission that has no recording (so a mission whose lines were never voiced cannot pass).

  THE USER APPROVES LINES FIRST (their standing rule: no voice file is made for a line they have not approved). The sheet design/dialogue/monolith_dialogue.xlsx is how they rewrite lines:
  python tools/dialogue/import_sheet.py reads their NEW text back into the missions and radio.json and writes design/dialogue/changed_lines.json; then
      voice.ps1 -List design\dialogue\changed_lines.json                voices exactly those lines (and nothing else)
      voice.ps1 -List design\dialogue\changed_lines.json -Also tamsin   ... and every line of a voice whose description changed
  A mission whose lines the user has not seen yet carries "linesPending": true (or "pending": true on a line): it is left out of every batch, and the voice checks count
  its lines as waiting for approval instead of failing. Drop the flag when the user has approved its lines, then run this with no arguments.

  A new speaker (a callsign nobody has voiced) needs a line in audio_src/voice/cast.json: "speakers" maps the name before the colon to a voice, and a voice has an instruction (who, accent,
  delivery). A new mission needs nothing else. Needs WSL with Trap Door's Breeze install (~/breeze), see tools/audio/README.md.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\audio\voice.ps1            # voice what is missing
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\audio\voice.ps1 -Plan      # only say what would be generated
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\audio\voice.ps1 -List design\dialogue\changed_lines.json -Also tamsin   # only the lines the user changed (and Tamsin)
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\audio\voice.ps1 -NoGenerate  # redo the radio processing of the clips already generated (after changing Radio.cs)
#>
param([switch]$Plan, [switch]$NoGenerate, [string]$List, [string[]]$Also)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
Set-Location $root

Write-Host '== 1. gathering the lines'
& python (Join-Path $root 'audio_src\voice\collect_lines.py')
if ($LASTEXITCODE -ne 0) { throw 'collect_lines.py failed' }

if (-not $NoGenerate) {
    Write-Host '== 2. generating the missing clips (WSL, GPU)'
    # wsl -e runs the program directly: a plain "wsl <command>" goes through the WSL user's shell, which eats the backslashes of a Windows path
    # (C:\Users\x became C:Usersx and wslpath returned nothing). Forward slashes are a second guard: wslpath takes C:/Users/x as well.
    $wslRoot = "$(& wsl -e wslpath -a ($root -replace '\\', '/'))".Trim()
    if (-not $wslRoot) { throw "wslpath could not translate $root (is WSL installed?)" }
    $voiceArgs = @(); if ($Plan) { $voiceArgs += '--plan' }
    if ($List) {
        $listFull = (Resolve-Path $List).Path
        $voiceArgs += '--list'; $voiceArgs += (& wsl -e wslpath -a ($listFull -replace "\\", "/")).Trim()
        foreach ($v in $Also) { $voiceArgs += '--also'; $voiceArgs += $v }
    }
    & wsl -e bash "$wslRoot/audio_src/voice/run_voice.sh" @voiceArgs
    if ($LASTEXITCODE -ne 0) { throw "run_voice.sh failed ($LASTEXITCODE): see the log above; it is resumable, run this again" }
}
if ($Plan) { Write-Host 'plan only: nothing was made'; exit 0 }

Write-Host '== 3. the radio, the OGGs and the index'
& dotnet build -c Release (Join-Path $root 'tools\audio') -v q --nologo | Select-String -Pattern 'error' | ForEach-Object { Write-Host $_ }
$dll = Join-Path $root 'tools\audio\bin\Release\net9.0\MnAudio.dll'
& dotnet $dll voice
if ($LASTEXITCODE -ne 0) { throw 'MnAudio voice failed' }
& dotnet $dll docs | Out-Null
Write-Host '== done. Next: tools\unity.ps1 Monolith.EditorTools.Build.Player, then tools\audio\check.ps1 (an unvoiced line fails it). Commit audio_src/voice/voice_lines.json, Game/Assets/StreamingAssets/Audio/vo and AUDIO_LIST.txt.'
