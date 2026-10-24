# Audio (`Monolith.Audio`)

Everything you hear goes through one software mixer. Unity is only the sound card: one silent looping `AudioSource` whose `OnAudioFilterRead` is filled by
`Mixer.Render`, so the same code renders in the game, headless in the checks, and offline in step with a capture run (one tick of samples per sim tick, recorded to a
WAV for the clips). The assembly references Core only; the game wires it in `Game/Audio/AudioRig.cs`.

| Folder / file | What |
|---|---|
| `Dsp/` | Plain C# with no Unity types (the tools in `tools/audio` compile the same files). `Mixer` (voices, five buses, ducking, the music deck, tone generators, a lookahead limiter), `Loudness` (BS.1770 LUFS, true peak), `Reverb` (hall, plate), `Radio` (the radio filter), `Wav`, filters |
| `Runtime/AudioSystem` | The MonoBehaviour: owns the mixer, library, conductor and radio; listens to the game's events; the cockpit (engine, afterburner, cabin air, alarms, lock tone, missile warning); records in offline mode |
| `Runtime/AudioLibrary` | Loads the built-in files named by `music.json`, `sfx.json` and `vo/index.json` (OGG through Unity's decoder, WAV by hand), then the player's own files from `Audio/` beside the game, which replace them by name; watches the folder |
| `Runtime/MusicConductor` | Mission music states and stings to mixer commands, on the beat or bar |
| `Runtime/AudioSystem.Cabin.cs` | The cabin (a partial of `AudioSystem`): the cabin's events (`cabin_step`, `cabin_power`, `cabin_breath`...) and its `CabinBoard` (lights, the scan's light and direction, the pilot's action) become footsteps, the breaker's lever, clunk and power-down or boot, the emergency hum, your breathing and heartbeat in the dark, the scan's moan through the hull (level and pan follow the beam), the repair's hatch and knitting, the rack |
| `Runtime/RadioDirector` | `radio_line` / `radio_voice` events to clips, with the logged squelch fallback; a line made at several intensities (calm, tense, frantic) plays the one nearest the crew's mood, taken from the music state (ambient calm, battle 1 tense, battles 2 and 3 frantic) |
| `Runtime/Spatializer` | Distance, pan, dullness, Doppler |
| `Runtime/AudioPrefs`, `AudioOut`, `AudioPaths` | Saved volumes (`audio.json`), the bridge to the sound card, folders |
| `Game/Audio/` | `AudioRig` (what kind of audio this run gets), `JuiceAudioAdapter` (the `IJuiceAudio` the juice plays its effects through), `AudioSelfTest` |

## The score

The music is composed as MIDI (`audio_src/compose/*.py` write `audio_src/midi/*.mid`, which open in any DAW: one track per instrument, named for the synth patch, with
controller data for expression, filter sweeps and vowels) and rendered **offline** by `tools/audio` (`MnAudio music`) through an analog-style synth: detuned saw and PWM
pulse polysynths with Juno chorus, a CS-80-style brass with a swelling ladder filter and a scoop into the pitch, formant choirs, an FM bell, a Moog bass, a Tangerine Dream
sequencer with resonant filter sweeps, a drum machine (LinnDrum and Simmons style, nothing sampled) with a gated hall, plate and hall reverbs, a tempo-synced tape echo. Why
offline: the sound is only as good as the time you can spend on it (oversampling, long reverbs, a master chain), the loops can be rendered twice round so reverb tails wrap and the
seam is perfect, the loudness can be measured and set, and the result is just a file, which is also what the player's own MP3s are, so the same mechanism plays both.

All four loops (ambient, battle 1, 2, 3) are 32 bars of 4/4 at 112 BPM (68.571 s) over the **same harmony**, so a change of state is a cross-fade between aligned loops: a louder
state on the next beat (0.85 s fade), a quieter one on the next bar (slow). Stings lay over the music on the next beat and dip it 3.5 dB. Victory, failure and death play once
(death at once). A mission may name its own track for a state (`"music": { "tracks": { "battle2": "oort_battle_2" } }`): `music/oort_battle_2.mp3` in the player's Audio folder, or built in.

## The mix

Buses: music, effects (normal, big, interface), voice. Volumes (master, music, effects, voice, mute) are the player's, saved to `audio.json` beside the game
(F5/F6 music, F7/F8 effects, F3/F4 voice, - and = master, F2 mute; a number shows for a moment). The voice ducks the music by 9 dB and the effects by 4.5 dB (25 ms in, 600 ms out);
a big moment (a capital ship's death, a beam, an arrival) ducks everything else by up to 55% for up to 3.5 s. A lookahead limiter at -1 dBFS ends the master bus; the self-test proves
nothing clips. Levels are set by loudness: loops by integrated LUFS (ambient -26, battle 1 -24, 2 -22, 3 -20), radio lines -23 LUFS calm, -21.5 tense, -19.5 frantic, effects by their peak at full scale and a
level in `sfx.json`. `Audio/` beside the game replaces anything by name; see `Audio/README.txt` (made on first run) and `AUDIO_LIST.txt`.

## Checks

`Monolith.exe -batchmode -nographics -mnAudioSelfTest [-mnOut dir]`: 80-odd checks on the real decoded files through the real audio system rendered offline. Other flags:
`-mnAudioMute` (every path runs, nothing is heard), `-mnNoAudio`, `-mnAudioCapture file.wav` (render in step with a capture run and record), `-mnAudio` (sound in a capture run),
`-mnAudioUser dir`, `-mnAudioLog` (a status line every 5 s: voices, audio-thread load, loudness, ducking). Tools and how to remake the sound: `tools/audio/README.md`.
