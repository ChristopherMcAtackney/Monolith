# tools/audio

The offline sound tools. `MnAudio` is a .NET 9 console program that compiles the game's own DSP files (`Game/Assets/Monolith/Scripts/Audio/Dsp`) plus the synth and the sound-design code
here, so what is measured here is what plays in the game. Built sound goes to `Game/Assets/StreamingAssets/Audio` (OGG, plus WAV for loops); scratch output to `audio_src/render` (not committed).

```
dotnet build -c Release tools/audio                      (10 s)
dotnet tools/audio/bin/Release/net9.0/MnAudio.dll <command>
```
To keep a long render from locking the build output, copy `bin/Release/net9.0` somewhere and run that copy.

| Command | What |
|---|---|
| `music [cue ...\|all] [--wav-only] [--solo track] [--mute track]` | Render the score from `audio_src/midi/*.mid` as mixed by `audio_src/music.json`: loops are rendered two passes round and the second kept (tails wrap, the seam is perfect), levelled to their LUFS, limited, encoded to OGG, and `music/music.json` (the runtime catalog) is written |
| `ship` | Copy the tracks that are not rendered here (`"shipped"` in `audio_src/music.json`: the user's Monolith_Epic_1 as `music/fight.mp3`) into the game and write them into the runtime catalog with how they play (start, loop body, seam cross-fade, level, swell per intensity); `music --ship-only` does the same. `python tools/audio/analyse_track.py file.mp3` finds a track's tempo, bars and good loop points |
| `stems cue ...` | Every track of a cue alone: loudness and a 10-band spectrum. `python audio_src/compose/mixfit.py stems.log --apply` sets the track levels in `music.json` from role targets |
| `sfx [id ...\|all] [--wav-only]` | Synthesise every effect (`Sfx/*.cs`), peak-normalise, make the distant versions, encode, write `sfx/sfx.json` (levels, distances, ducking, variants) |
| `voice [--only voice] [--wav-only]` | Radio-filter the raw text-to-speech clips (`audio_src/voice/raw`) per voice class, level to LUFS, encode to `vo/`, write `vo/index.json` (text to clip) |
| `docs` | Write `AUDIO_LIST.txt`, every sound the player can replace |
| `sheet [--out dir]` | The audio sheet: loudness, peak and a spectrogram for every shipped file, as `index.html` and `audio_levels.csv` |
| `analyse file.wav [out.png]` | Loudness, peaks, octave bands and a spectrogram of any WAV |
| `debug perf` | The mixer's cost: ms per second of sound with 0, 16 and 40 looping voices and music |
| `mixertest` | The mixer and DSP against known signals (loudness, pan law, pitch, loops, limiter, ducking, the bar cross-fade) |

`make_clip.py` joins a capture run's stills to the audio rendered in step with it (a clip with sound).

## Making the sound from scratch

1. **Music.** `python audio_src/compose/build_music.py` writes the MIDI (`score.py` holds the harmony, the themes and the pattern helpers), then `MnAudio music all`. A full render is about 7 minutes.
2. **Effects.** `MnAudio sfx all` (seconds). The designs are `Sfx/Sfx*.cs`; levels are in `SfxLib.cs`.
3. **Voices.** One command does all of this for new lines: `powershell -File tools\audio\voice.ps1` (see `tools/PIPELINE.md`, "Voicing a mission"). By hand: needs WSL with Trap Door's Breeze setup (`~/breeze`, read only; its install scripts are in `TrapDoor3d/tools/tts`). `python audio_src/voice/collect_lines.py` gathers every line in the missions and
   `radio.json`; in WSL `bash audio_src/voice/run_voice.sh` generates the dry clips (about 5 to 25 s a line on the 4070 Ti, resumable: it only does new or changed lines; voices are *designed* from text in
   `cast.json`, never cloned; a CUDA fault restarts the run); `MnAudio voice` makes the radio.
   **Every line is tagged calm, tense or frantic** (`collect_lines.py`: from the event that fires it and the music state at that moment; the table is in the script with the reason beside each line;
   the wingmen's acknowledgements are made twice, tense and frantic). `gen_voice.py` asks the model for that delivery (cast.json `intensity`) and a punctuated version of the text; `MnAudio voice` then
   directs it (`Tools/VoiceActing.cs`: pauses squeezed, sped up to the pace of someone under fire with ffmpeg's rubberband, pitch up, breaths in and out) and runs the radio at that intensity
   (`Dsp/Radio.cs`: more drive and clipping, a louder noise bed, a faster squelch, a few dropouts), louder as it climbs (-23, -21.5, -19.5 LUFS).
4. `MnAudio docs`, then rebuild the player.

New dialogue: add the lines to the mission or `radio.json`, run `collect_lines.py` and `run_voice.sh`, then `MnAudio voice`. A line with no clip still plays as a squelch and is logged
(`[MN][WARN] VOICE no clip for ...`), so a mission can be written before it is voiced.
