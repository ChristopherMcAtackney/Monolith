using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Audio
{
    public sealed class AudioOptions
    {
        /// <summary>No sound card: render the mix in step with the simulation (one tick of samples per tick) and record it. For captures and checks.</summary>
        public bool Offline;
        public string RecordPath;
        public bool Silent;                 // run every sound path, hear none of it
        public bool SavePrefs = true;
        public int OfflineRate = 48000;
        /// <summary>Keep what is rendered in memory (the checks read it back); a capture run with a RecordPath always does.</summary>
        public bool Record;
        /// <summary>A library that is already loaded (the checks load once and build several systems).</summary>
        public AudioLibrary Library;
    }

    /// <summary>A sound that stays on until it is stopped (a beam's roar, an engine) and can be moved and re-levelled each frame.</summary>
    public sealed class LoopHandle
    {
        internal AudioSystem Owner; internal int Voice; internal SfxCue Cue;
        internal Vector3 Pos, Vel; internal float Gain = 1f; internal bool Stopped, Doppler;
        public void Set(Vector3 position, float gain) { Pos = position; Gain = gain; }
        public void Stop(float fadeSeconds) { if (Stopped) return; Stopped = true; Owner?.StopLoop(this, fadeSeconds); }
    }

    /// <summary>
    /// The game's sound. It owns the mixer (see Dsp/Mixer.cs), the library of loaded sounds, the score, the radio and the effects, and listens to
    /// the game's events (music states and stings from the mission, radio lines, decoys, locks, arrivals...) to play them. The juice's own sounds
    /// (guns, hits, explosions, beams) come through <see cref="PlayCue"/> by way of IJuiceAudio. Volumes are the player's (music, effects, voice,
    /// master), saved beside the game. In captures it can render without a sound card, in step with the simulation, and record what it made.
    /// </summary>
    [DefaultExecutionOrder(80)]
    public sealed class AudioSystem : MonoBehaviour
    {
        public static AudioSystem Active { get; private set; }

        public GameWorld World { get; private set; }
        public Mixer Mixer { get; private set; }
        public AudioLibrary Lib { get; private set; }
        public MusicConductor Music { get; private set; }
        public RadioDirector Radio { get; private set; }
        public AudioPrefs Prefs { get; private set; }
        public AudioOptions Options { get; private set; }
        public int SampleRate { get; private set; }
        public bool Ready => Lib != null && Lib.Ready;
        public float Time01 => _now;

        readonly List<IDisposable> _subs = new List<IDisposable>();
        readonly System.Random _rng = new System.Random(20261024);
        ListenerPose _listener = ListenerPose.Default;
        AudioOut _out;
        float _now;                          // seconds since the system started (sim time while ticking)
        float _juiceMaster = 1f;
        bool _juiceEnabled = true;
        int _tickRendered;
        long _framesRecorded;
        List<float[]> _rec;
        readonly List<LoopHandle> _loops = new List<LoopHandle>();
        string _overlay; float _overlayUntil;
        bool _paused;

        // statistics for the checks
        public int CuesPlayed, CuesMissing, CuesThrottled, CuesCulled;
        public readonly Dictionary<string, int> CueCounts = new Dictionary<string, int>();
        public readonly HashSet<string> MissingCues = new HashSet<string>();

        // ------------------------------------------------------------------------------------------------ creation

        public static AudioSystem Create(GameWorld world, Transform parent, AudioOptions options = null)
        {
            if (Active != null) return Active;
            options ??= new AudioOptions();
            var go = new GameObject("MonolithAudio");
            if (parent != null) go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioSystem>();
            a.Begin(world, options);
            return a;
        }

        void Begin(GameWorld world, AudioOptions options)
        {
            Active = this;
            World = world; Options = options;
            SampleRate = options.Offline ? options.OfflineRate : AudioSettings.outputSampleRate;
            Mixer = new Mixer(SampleRate);
            Mixer.MeterLoudness = options.Offline || CommandLine.Has("-mnAudioLog");
            Prefs = options.SavePrefs ? AudioPrefs.Load() : new AudioPrefs();
            ApplyPrefs();
            Lib = options.Library ?? new AudioLibrary();
            Music = new MusicConductor(Mixer, Lib, this);
            Radio = new RadioDirector(Mixer, Lib, this);
            if (!options.Offline)
            {
                if (options.SavePrefs && !options.Silent) AudioPaths.EnsureUserFolder();
                _out = gameObject.AddComponent<AudioOut>();
                _out.Begin(Mixer);
                if (options.Silent) AudioListener.volume = 0f;                  // every path runs, none of it is heard
                if (FindObjectOfType<AudioListener>() == null)
                {
                    // a listener of its own, on its own object: a component that filters audio may sit on only one source or listener
                    var lg = new GameObject("AudioListener"); lg.transform.SetParent(transform, false); lg.AddComponent<AudioListener>();
                }
            }
            if (options.Offline && (options.Record || !string.IsNullOrEmpty(options.RecordPath))) _rec = new List<float[]>();
            AudioLink.Owned = true;
            Subscribe();
            world?.Sim.Register(new Tick(this));
            if (!Lib.Ready) { if (options.Offline) Lib.LoadBlocking(); else StartCoroutine(Lib.Load(this)); }
            MnLog.Info($"AUDIO on: {SampleRate} Hz, {(options.Offline ? "offline (rendered in step with the sim" + (options.RecordPath != null ? ", recording to " + options.RecordPath : "") + ")" : options.Silent ? "silent (every path runs, none is heard)" : "sound card")}, settings from {Prefs.Source}");
        }

        void OnDestroy()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            FlushRecording();
            if (Active == this) { Active = null; AudioLink.Owned = false; }
        }

        void OnApplicationQuit() { FlushRecording(); if (Prefs != null && Prefs.Dirty && Options.SavePrefs) Prefs.Save(); }

        sealed class Tick : ISimSystem
        {
            readonly AudioSystem _a;
            public Tick(AudioSystem a) { _a = a; }
            public int Order => SimOrder.Late + 50;
            public void Step(in SimFrame frame) { _a.OnTick(frame.Tick); }
        }

        // ------------------------------------------------------------------------------------------------ settings

        public void ApplyPrefs()
        {
            float m = Prefs.mute ? 0f : AudioPrefs.Curve(Prefs.master);
            Mixer.SetMasterGain(m);
            Mixer.SetBusGain(Bus.Music, AudioPrefs.Curve(Prefs.music));
            float fx = AudioPrefs.Curve(Prefs.effects);
            Mixer.SetBusGain(Bus.Sfx, fx); Mixer.SetBusGain(Bus.SfxBig, fx); Mixer.SetBusGain(Bus.Ui, fx);
            Mixer.SetBusGain(Bus.Voice, AudioPrefs.Curve(Prefs.voice));
        }

        void Nudge(ref float v, float d, string name)
        {
            v = Mathf.Clamp01(Mathf.Round((v + d) * 20f) / 20f);
            Prefs.Dirty = true; ApplyPrefs();
            Show($"{name} {Mathf.RoundToInt(v * 100f)}%");
        }

        void Show(string text) { _overlay = text; _overlayUntil = UnityEngine.Time.unscaledTime + 1.6f; }

        void Hotkeys()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.f5Key.wasPressedThisFrame) Nudge(ref Prefs.music, -0.1f, "MUSIC");
            if (kb.f6Key.wasPressedThisFrame) Nudge(ref Prefs.music, 0.1f, "MUSIC");
            if (kb.f7Key.wasPressedThisFrame) Nudge(ref Prefs.effects, -0.1f, "EFFECTS");
            if (kb.f8Key.wasPressedThisFrame) Nudge(ref Prefs.effects, 0.1f, "EFFECTS");
            if (kb.f3Key.wasPressedThisFrame) Nudge(ref Prefs.voice, -0.1f, "VOICE");
            if (kb.f4Key.wasPressedThisFrame) Nudge(ref Prefs.voice, 0.1f, "VOICE");
            if (kb.minusKey.wasPressedThisFrame) Nudge(ref Prefs.master, -0.1f, "MASTER");
            if (kb.equalsKey.wasPressedThisFrame) Nudge(ref Prefs.master, 0.1f, "MASTER");
            if (kb.f2Key.wasPressedThisFrame) { Prefs.mute = !Prefs.mute; Prefs.Dirty = true; ApplyPrefs(); Show(Prefs.mute ? "SOUND OFF" : "SOUND ON"); }
        }

        void OnGUI()
        {
            if (_overlay == null || UnityEngine.Time.unscaledTime > _overlayUntil) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            style.normal.textColor = new Color(0.9f, 0.55f, 0.15f, 1f);
            GUI.Label(new Rect(24, 20, 420, 40), _overlay, style);
        }

        // ------------------------------------------------------------------------------------------------ frame

        void Update()
        {
            if (Options == null) return;
            if (!Options.Offline) _now += UnityEngine.Time.unscaledDeltaTime;
            Hotkeys();
            if (Prefs.Dirty && Options.SavePrefs) Prefs.Save();
            UpdateListener();
            if (_out != null && CommandLine.Has("-mnAudioLog") && UnityEngine.Time.unscaledTime > _nextLog) LogStatus();
            if (!Ready) return;
            Lib.Poll(this, UnityEngine.Time.unscaledTime);
            if (!Options.Offline) Music.Update(_now);
            if (!Options.Offline) Radio.Update(_now);
            UpdateLoops();
            UpdateCockpit(UnityEngine.Time.unscaledDeltaTime);
            StartMusicIfNeeded();
        }

        float _nextLog;

        void LogStatus()
        {
            _nextLog = UnityEngine.Time.unscaledTime + 5f;
            var m = Mixer;
            MnLog.Info($"AUDIO status: {m.ActiveVoices} voices, music {(Music.State ?? "-")}, {_out.Callbacks} blocks, audio thread load avg {_out.LoadAvg * 100f:0.0}% max {_out.LoadMax * 100f:0.0}%, " +
                       $"short-term {m.LoudnessShortTerm:0.0} LUFS, peak {20f * Mathf.Log10(Mathf.Max(m.PostLimiterPeak, 1e-6f)):0.0} dBFS, clipped {m.ClippedSamples}, stolen voices {m.StolenVoices}, cues thrown away {CuesThrottled} busy + {CuesCulled} too quiet, ducks music {m.LastMusicDuckDb:0.0} dB");
            _out.ResetLoad();
        }

        /// <summary>The audio thread's worst load since the last log line (0..1), for the checks.</summary>
        public float AudioThreadLoad => _out != null ? _out.LoadMax : 0f;

        void UpdateListener()
        {
            var cam = World?.MainCamera;
            var me = World != null ? World.Ships.Get(World.LocalPlayerShip) : null;
            if (cam != null) { _listener.Position = cam.transform.position; _listener.Rotation = cam.transform.rotation; }
            else if (me != null) { _listener.Position = me.Position; _listener.Rotation = me.Rotation; }
            _listener.Velocity = me != null ? me.Velocity : Vector3.zero;
        }

        bool _musicBootstrapped;

        /// <summary>A scene that starts after the mission has already announced its state still gets its music (the first time the library is ready).</summary>
        void StartMusicIfNeeded()
        {
            var board = World?.Mission;
            if (board == null || _musicBootstrapped || Music.State != null) { if (board == null) _musicBootstrapped = false; return; }
            _musicBootstrapped = true;
            Music.OnState(board.Music ?? "ambient", board.MusicTrack, _now);
        }

        /// <summary>Called by the sim every tick, after everything else has moved.</summary>
        void OnTick(int tick)
        {
            if (Options.Offline) _now = (tick + 1) / (float)Sim.Hz;
            Radio.Flush();
            if (Options.Offline)
            {
                Music.Update(_now); Radio.Update(_now);
                RenderOfflineTick(tick);
            }
            TickMissiles(tick);
        }

        // ------------------------------------------------------------------------------------------------ offline rendering and recording

        void RenderOfflineTick(int tick)
        {
            // the number of samples that have gone by after this tick, so no rounding ever drifts
            long target = (long)(tick + 1) * SampleRate / Sim.Hz;
            int n = (int)(target - _framesRecorded);
            if (n <= 0) return;
            var buf = new float[n * 2];
            Mixer.Render(buf, n, 2);
            _rec?.Add(buf);
            _framesRecorded = target;
        }

        public PcmBuffer RecordedAudio()
        {
            if (_rec == null) return null;
            var b = new PcmBuffer(SampleRate, 2, (int)_framesRecorded);
            int at = 0;
            foreach (var chunk in _rec)
            {
                int n = chunk.Length / 2;
                for (int i = 0; i < n; i++) { b.Ch[0][at + i] = chunk[i * 2]; b.Ch[1][at + i] = chunk[i * 2 + 1]; }
                at += n;
            }
            return b;
        }

        bool _flushed;

        public void FlushRecording()
        {
            if (_flushed || _rec == null || string.IsNullOrEmpty(Options.RecordPath) || _framesRecorded == 0) return;
            _flushed = true;
            try
            {
                var b = RecordedAudio();
                Wav.Write(Options.RecordPath, b, 16, false);
                var m = LoudnessMeter.Measure(b);
                MnLog.Info($"AUDIO recorded {b.Frames / (float)SampleRate:0.0} s to {Options.RecordPath}: {m.Integrated:0.0} LUFS, peak {m.PeakDb:0.0} dBFS");
            }
            catch (Exception e) { MnLog.Error("AUDIO recording failed: " + e.Message); }
        }

        // ------------------------------------------------------------------------------------------------ playing cues

        static Bus BusOf(string bus) => bus == "big" ? Bus.SfxBig : bus == "ui" ? Bus.Ui : Bus.Sfx;

        float Rand() => (float)_rng.NextDouble();

        // A battle can ask for fifty sounds in a tenth of a second; the ear hears a dozen. Each cue may start only so many times in a window
        // (a gun stays a rattle, not a wall), and a sound too quiet to hear is not started.
        readonly Dictionary<string, Queue<float>> _recent = new Dictionary<string, Queue<float>>();

        static int MaxPerWindow(string id)
        {
            switch (id)
            {
                case "laser_light": case "laser_enemy": case "hit_shield": case "hit_hull": case "flak_shell": case "explosion_small": return 3;
                case "laser_heavy": case "near_miss": case "missile_launch": case "laser_turret": case "flak_burst": case "subsystem_blast": return 2;
                default: return 6;
            }
        }

        bool Throttled(string id, SfxCue cue, float gain)
        {
            if (cue.Priority >= 4f || gain > 0.92f || cue.Loop) return false;
            if (!_recent.TryGetValue(id, out var q)) _recent[id] = q = new Queue<float>();
            while (q.Count > 0 && _now - q.Peek() > 0.1f) q.Dequeue();
            if (q.Count >= MaxPerWindow(id)) return true;
            q.Enqueue(_now);
            return false;
        }

        /// <summary>Play an effect at a place. <paramref name="gain"/> is the caller's own level (0..1); distance is metres from the listener (negative: work it out).</summary>
        public int PlayCue(string id, Vector3 position, float gain = 1f, float distance = -1f, Vector3 velocity = default)
        {
            if (!_juiceEnabled && false) return 0;
            if (!Ready) return 0;
            CueCounts.TryGetValue(id, out int cnt); CueCounts[id] = cnt + 1;
            if (!Lib.Sfx.TryGetValue(id, out var cue) || !cue.Ready)
            {
                CuesMissing++;
                if (MissingCues.Add(id)) MnLog.Warn($"AUDIO no sound for cue '{id}'");
                return 0;
            }
            if (Throttled(id, cue, gain)) { CuesThrottled++; return 0; }
            CuesPlayed++;
            float level = Db.ToLin(cue.LevelDb) * gain * _juiceMaster;
            float dist = distance >= 0f ? distance : Vector3.Distance(_listener.Position, position);
            float pan = 0f, cut = 0f, pitch = 1f, spatial = 1f, delay = 0f;
            if (cue.Spatial)
            {
                var r = Spatializer.Compute(_listener, position, velocity, cue.MinDist, cue.MaxDist, id == "near_miss");
                spatial = r.Gain; pan = r.Pan; cut = r.CutoffHz; pitch = r.Pitch;
                delay = Mathf.Min(dist / 1000f * cue.ArrivalDelay, 1.4f);
            }
            if (level * spatial < 0.0035f) { CuesCulled++; return 0; }
            float far = cue.Far.Length > 0 ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cue.NearFadeFrom, cue.NearFadeTo, dist)) : 0f;
            float near = 1f - far * 0.92f;
            float pv = 1f + (Rand() * 2f - 1f) * cue.PitchVar;
            var bus = BusOf(cue.Bus);
            int voice = 0;
            if (near * spatial > 0.003f)
            {
                var c = cue.Near[_rng.Next(cue.Near.Length)];
                voice = Mixer.Play(c, new PlayParams
                {
                    Gain = level * near * spatial, Pan = pan, Pitch = pitch * pv, CutoffHz = cut, Bus = bus, Loop = false, DelaySeconds = delay,
                    Priority = cue.Priority * (0.5f + level), PitchFollowsTime = true,
                });
            }
            if (far > 0.02f && cue.Far.Length > 0)
            {
                var c = cue.Far[_rng.Next(cue.Far.Length)];
                voice = Mixer.Play(c, new PlayParams
                {
                    Gain = level * Mathf.Lerp(0.55f, 1f, far) * far * spatial, Pan = pan, Pitch = pitch * pv, CutoffHz = cut, Bus = bus, DelaySeconds = delay + 0.03f,
                    Priority = cue.Priority * 0.5f, PitchFollowsTime = true,
                });
            }
            if (cue.DuckAmount > 0f) Mixer.BigDuck(cue.DuckAmount * Mathf.Clamp01(spatial * 2.2f), cue.DuckSeconds);
            return voice;
        }

        public LoopHandle StartLoop(string id, Vector3 position, float gain = 1f, bool doppler = false, bool managed = true)
        {
            var h = new LoopHandle { Owner = this, Pos = position, Gain = gain, Doppler = doppler };
            if (!Ready || !Lib.Sfx.TryGetValue(id, out var cue) || !cue.Ready) { h.Stopped = true; if (Ready && MissingCues.Add(id)) MnLog.Warn($"AUDIO no sound for loop '{id}'"); return h; }
            h.Cue = cue;
            var r = cue.Spatial ? Spatializer.Compute(_listener, position, Vector3.zero, cue.MinDist, cue.MaxDist, false) : new Spatializer.Result { Gain = 1f, Pitch = 1f };
            h.Voice = Mixer.Play(cue.Near[0], new PlayParams
            {
                Gain = Db.ToLin(cue.LevelDb) * gain * r.Gain * _juiceMaster, Pan = r.Pan, Pitch = r.Pitch, CutoffHz = r.CutoffHz, Bus = BusOf(cue.Bus), Loop = true,
                FadeInSeconds = 0.12f, Priority = 10f, PitchFollowsTime = true,
            });
            if (managed) _loops.Add(h);
            return h;
        }

        internal void StopLoop(LoopHandle h, float fade)
        {
            if (h.Voice != 0) Mixer.Stop(h.Voice, Mathf.Max(0.02f, fade));
            _loops.Remove(h);
        }

        void UpdateLoops()
        {
            for (int i = _loops.Count - 1; i >= 0; i--)
            {
                var h = _loops[i];
                if (h.Stopped || h.Cue == null) { _loops.RemoveAt(i); continue; }
                var r = h.Cue.Spatial ? Spatializer.Compute(_listener, h.Pos, h.Vel, h.Cue.MinDist, h.Cue.MaxDist, h.Doppler) : new Spatializer.Result { Gain = 1f, Pitch = 1f };
                Mixer.Control(h.Voice, new VoiceCtl { Gain = Db.ToLin(h.Cue.LevelDb) * h.Gain * r.Gain * _juiceMaster, Pan = r.Pan, Pitch = r.Pitch, CutoffHz = r.CutoffHz <= 0f ? 20000f : r.CutoffHz });
            }
        }

        /// <summary>A cockpit sound: not placed in space, at the cue's own level.</summary>
        public int PlayLocal(string id, float gain = 1f, float pitch = 1f)
        {
            if (!Ready) return 0;
            CueCounts.TryGetValue(id, out int cnt); CueCounts[id] = cnt + 1;
            if (!Lib.Sfx.TryGetValue(id, out var cue) || !cue.Ready) { CuesMissing++; if (MissingCues.Add(id)) MnLog.Warn($"AUDIO no sound for cue '{id}'"); return 0; }
            CuesPlayed++;
            if (cue.DuckAmount > 0f) Mixer.BigDuck(cue.DuckAmount, cue.DuckSeconds);
            return Mixer.Play(cue.Near[_rng.Next(cue.Near.Length)], new PlayParams
            {
                Gain = Db.ToLin(cue.LevelDb) * gain, Pitch = pitch * (1f + (Rand() * 2f - 1f) * cue.PitchVar), Bus = BusOf(cue.Bus), Priority = cue.Priority * 2f, PitchFollowsTime = false,
            });
        }

        // juice-side controls (IJuiceAudio)
        public void SetJuiceMaster(float volume, bool enabled) { _juiceMaster = enabled ? Mathf.Max(0f, volume / 0.8f) : 0f; _juiceEnabled = enabled; }
        public void Duck(float amount, float seconds) => Mixer.BigDuck(amount, seconds);
        public void SetTimeScale(float scale) => Mixer.SetTimePitch(Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(scale)));

        // ------------------------------------------------------------------------------------------------ events

        void Sub(string id, Action<GameEvent> h)
        {
            _subs.Add(GameEvents.Subscribe(id, e =>
            {
                try { h(e); }
                catch (Exception ex) { MnLog.Error($"AUDIO handler for '{id}' threw: {ex}"); }
            }));
        }

        bool IsPlayer(int ship) => ship != GameWorld.NoShip && ship == World.LocalPlayerShip;

        Vector3 PosOf(in GameEvent e)
        {
            if (e.HasPosition) return e.Position;
            var s = World.Ships.Get(e.Entity);
            return s != null ? s.Position : _listener.Position;
        }

        void Subscribe()
        {
            Sub(MissionEvents.MusicState, e => Music.OnState(e.Subject, World.Mission != null && World.Mission.Music == e.Subject ? World.Mission.MusicTrack : null, _now));
            Sub(MissionEvents.MusicSting, e => Music.OnSting(e.Subject));
            Sub(CombatEvents.RadioLine, e => Radio.OnLine(e));
            Sub(MissionEvents.RadioVoice, e => Radio.OnVoiceSlot(e));

            Sub(MissionEvents.JumpIn, OnJumpIn);
            Sub(CombatEvents.DecoyLaunched, e => PlayCue("decoy_launch", PosOf(e), 0.9f));
            Sub(CombatEvents.DecoySuccess, e => PlayCue("decoy_success", PosOf(e), 0.9f));
            Sub(CombatEvents.ShieldDown, e => { if (IsPlayer(e.Entity)) PlayLocal("shield_down", 1f); });
            Sub(CombatEvents.Collision, e => { if (IsPlayer(e.Entity) || IsPlayer(e.Other)) PlayLocal("collision", Mathf.Clamp01(0.4f + e.Value / 60f)); else PlayCue("collision", PosOf(e), 0.25f); });
            Sub(CombatEvents.PartDamaged, e => { if (IsPlayer(e.Entity)) PlayLocal("system_fail", 0.8f); });
            Sub(CombatEvents.SubsystemDestroyed, e => { if (IsPlayer(e.Entity)) { PlayLocal("system_fail", 1f); PlayLocal("creak_c", 0.9f); } });
            Sub(CombatEvents.LockAcquired, e => { if (IsPlayer(e.Entity)) PlayLocal("lock_acquired"); });
            Sub(CombatEvents.LockLost, e => { if (IsPlayer(e.Entity)) PlayLocal("lock_lost"); });
            Sub(CombatEvents.TargetChanged, e => { if (IsPlayer(e.Entity)) PlayLocal("target_blip"); });
            Sub(CombatEvents.PowerChanged, e => { if (IsPlayer(e.Entity)) PlayLocal("power_blip"); });
            Sub(CombatEvents.HitHull, OnHullHit);
            Sub(CombatEvents.BeamFire, e => PlayCue("beam_fire", PosOf(e), 1f));
            Sub(CombatEvents.BeamEnd, e => { var s = World.Ships.Get(e.Entity); if (s != null) PlayCue("beam_end", s.Position, 0.9f); });
            Sub(CombatEvents.MissileTrack, OnMissileTrack);
            Sub(MissionEvents.CabinScan, e => PlayLocal("scan_sweep", 0.8f));
            // The cabin (a system of its own: the ids are its documented vocabulary, matched by name so the audio does not depend on its assembly).
            Sub("cabin_lights", e => { _cabinState = e.Subject ?? "normal"; });
            Sub("cabin_step", e => PlayLocal(e.Subject == "tread" ? "step_grating" : e.Subject == "riser" ? "step_rung" : "step_plate", 0.45f + 0.55f * Mathf.Clamp01(e.Value)));
            Sub("cabin_stand", e => { if (e.Subject == "stand" && e.Value < 0.5f) PlayLocal("seat_open"); else if (e.Subject == "sit" && e.Value >= 0.5f) PlayLocal("seat_close"); });
            Sub("cabin_power", e => PlayLocal(e.Subject == "off" ? "breaker_off" : "breaker_on"));
            Sub("cabin_scan_contact", e => { if (e.Subject == "hit") PlayLocal("scan_hit"); });
            Sub("cabin_creak", e => { _cabinOwnsCreaks = true; float v = Mathf.Clamp01(e.Value); PlayLocal("creak_" + (char)('a' + (v < 0.3f ? 0 : v < 0.6f ? 1 : v < 0.85f ? 2 : 3)), 0.45f + 0.55f * v); });
            Sub("cabin_breath", e => PlayLocal((e.Subject == "out" ? "breath_out" : "breath_in"), 0.5f + 0.5f * Mathf.Clamp01(e.Value)));
            Sub("cabin_repair", OnRepair);
            Sub("cabin_loadout", e => PlayLocal("rack_seat"));
            Sub("cabin_table", e => PlayLocal(e.Subject == "map" ? "ui_select" : "ui_click"));
            Sub("cabin_jolt", e => PlayLocal("cabin_rattle", 0.3f + 0.7f * Mathf.Clamp01(e.Value)));
        }

        bool _cabinOwnsCreaks;
        LoopHandle _repair;

        void OnRepair(GameEvent e)
        {
            switch (e.Subject)
            {
                case "start":
                    _repair?.Stop(0.05f);
                    _repair = StartLoop("repair_loop", Vector3.zero, 1f, false, false);
                    break;
                case "progress":
                    if (_repair != null) SetLocalLoop(_repair, 1f, 0.9f + 0.45f * Mathf.Clamp01(e.Value));
                    break;
                case "done": _repair?.Stop(0.15f); _repair = null; PlayLocal("repair_done"); break;
                case "slip": _repair?.Stop(0.05f); _repair = null; PlayLocal("repair_slip"); break;
            }
        }

        string _cabinState = "normal";

        void OnJumpIn(GameEvent e)
        {
            // an Earth capital ship arriving is announced by a deep horn; the flash itself is the juice's
            var s = World.Ships.Get(e.Entity);
            var me = World.Ships.Get(World.LocalPlayerShip);
            bool hostile = s != null && me != null && Teams.Hostile(me.Team, s.Team);
            if (!hostile) return;
            // every big Earth hull gets the deep horn (a corvette is a capital ship in FS2's terms); a destroyer's is the full level, a corvette's a little lower
            if (e.Subject == "capital") { PlayLocal("earth_horn", 1f); Log("Earth capital ship arrives: horn"); }
            else if (e.Subject == "corvette") { PlayLocal("earth_horn", 0.8f); Log("Earth corvette arrives: horn"); }
        }

        void OnHullHit(GameEvent e)
        {
            if (!IsPlayer(e.Entity) || _cabinOwnsCreaks) return;
            // the hull groans under hits: harder hits more often and deeper
            float hit = Mathf.Clamp01(e.Value / 40f);
            if (Rand() < 0.25f + 0.6f * hit)
            {
                int kind = hit > 0.6f ? 2 + _rng.Next(2) : _rng.Next(2);
                PlayLocal("creak_" + (char)('a' + kind), 0.6f + 0.4f * hit);
            }
        }

        void Log(string s) { if (CommandLine.Has("-mnAudioLog")) MnLog.Info("AUDIO " + s); }

        // ------------------------------------------------------------------------------------------------ the missiles that fly past

        sealed class Hiss { public LoopHandle Loop; public int LastTick; public Vector3 Pos, Vel; }
        readonly Dictionary<int, Hiss> _hiss = new Dictionary<int, Hiss>();

        void OnMissileTrack(GameEvent e)
        {
            if (!Ready) return;
            int id = (int)e.Value;
            if (!_hiss.TryGetValue(id, out var h))
            {
                if (_hiss.Count >= 4 || (e.Position - _listener.Position).sqrMagnitude > 1500f * 1500f) return;
                h = new Hiss(); _hiss[id] = h;
                h.Loop = StartLoop("missile_hiss", e.Position, 1f, true);
            }
            h.LastTick = e.Tick; h.Pos = e.Position; h.Vel = e.Direction;
            h.Loop.Set(e.Position, 1f); h.Loop.Vel = e.Direction;
        }

        void TickMissiles(int tick)
        {
            List<int> gone = null;
            foreach (var kv in _hiss)
                if (tick - kv.Value.LastTick > 3) (gone ??= new List<int>()).Add(kv.Key);
            if (gone != null) foreach (var id in gone) { _hiss[id].Loop.Stop(0.25f); _hiss.Remove(id); }
        }

        // ------------------------------------------------------------------------------------------------ the cockpit

        LoopHandle _engine, _burner, _air, _alarm;
        float _burnerLevel, _engineLevel, _alarmLevel, _airLevel = 1f;
        bool _burnerWas;

        void EnsureCockpitLoops()
        {
            if (!Ready) return;
            if (_engine == null) _engine = StartLoop("engine_loop", Vector3.zero, 0f, false, false);
            if (_burner == null) _burner = StartLoop("afterburner_loop", Vector3.zero, 0f, false, false);
            if (_air == null) _air = StartLoop("cabin_air", Vector3.zero, 0f, false, false);
            if (_alarm == null) _alarm = StartLoop("alarm_hull", Vector3.zero, 0f, false, false);
        }

        void SetLocalLoop(LoopHandle h, float gain, float pitch, float cutoff = 0f)
        {
            if (h == null || h.Stopped || h.Voice == 0 || h.Cue == null) return;
            Mixer.Control(h.Voice, new VoiceCtl { Gain = Db.ToLin(h.Cue.LevelDb) * gain * _juiceMaster, Pan = 0f, Pitch = pitch, CutoffHz = cutoff <= 0f ? 20000f : cutoff });
        }

        void UpdateCockpit(float dt)
        {
            if (!Ready) return;
            EnsureCockpitLoops();
            dt = Mathf.Clamp(dt, 0.001f, 0.1f);
            var st = World != null ? World.Ships.Get(World.LocalPlayerShip) : null;
            var me = World != null ? World.Combat.Get(World.LocalPlayerShip) : null;
            bool paused = World != null && World.Sim.TimeScale <= 0.001f && !Options.Offline;
            bool alive = st != null && me != null && me.Alive;
            bool dark = _cabinState == "dark" || _cabinState == "blackout";
            _paused = paused;

            float speed01 = st != null && st.TopSpeed > 1f ? Mathf.Clamp01(Mathf.Abs(st.ForwardSpeed) / st.TopSpeed) : 0f;
            float throttle = st != null ? st.Throttle : 0f;
            float engineTarget = !alive || paused || dark ? 0f : 0.45f + 0.55f * Mathf.Max(throttle, speed01);
            _engineLevel = Mathf.MoveTowards(_engineLevel, engineTarget, dt * 1.2f);
            float ep = 0.80f + 0.42f * speed01 + (st != null && st.AfterburnerOn ? 0.1f : 0f) - (me != null ? (1f - me.EngineHealth01) * 0.12f : 0f);
            SetLocalLoop(_engine, _engineLevel, ep, 600f + 6500f * Mathf.Clamp01(_engineLevel * 0.8f + speed01 * 0.4f));

            bool burning = alive && st != null && st.AfterburnerOn && !paused;
            if (burning != _burnerWas)
            {
                _burnerWas = burning;
                PlayLocal(burning ? "afterburner_on" : "afterburner_off", 1f);
            }
            _burnerLevel = Mathf.MoveTowards(_burnerLevel, burning ? 1f : 0f, dt * (burning ? 4f : 2.5f));
            SetLocalLoop(_burner, _burnerLevel, 0.92f + 0.12f * speed01);

            _airLevel = Mathf.MoveTowards(_airLevel, paused ? 0.15f : dark ? 1.6f : 1f, dt * 1.5f);
            SetLocalLoop(_air, _airLevel, 1f);

            // the alarm wakes when the hull is low and sleeps when it recovers
            float hull = me != null && alive ? me.Hull01 : 1f;
            float alarmTarget = alive && !paused && hull < 0.3f ? Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(0.3f, 0.1f, hull)) : (alarmHold(hull) ? _alarmLevel : 0f);
            if (alive && !paused) { if (_cabinState == "alert") alarmTarget = Mathf.Max(alarmTarget, 0.9f); else if (_cabinState == "emergency") alarmTarget = Mathf.Max(alarmTarget, 0.45f); }
            _alarmLevel = Mathf.MoveTowards(_alarmLevel, alarmTarget, dt * 3f);
            SetLocalLoop(_alarm, _alarmLevel, 1f + 0.12f * Mathf.InverseLerp(0.3f, 0.05f, hull));

            DriveTones(me, alive && !paused);
            Mixer.SetBusGain(Bus.Sfx, paused ? AudioPrefs.Curve(Prefs.effects) * 0.4f : AudioPrefs.Curve(Prefs.effects));
        }

        bool alarmHold(float hull) => hull < 0.36f && _alarmLevel > 0.05f;      // a little hysteresis: it does not flicker at the threshold

        void DriveTones(ShipCombat me, bool on)
        {
            float lockGain = 0f, lockFreq = 880f, lockGate = 0f, lockDuty = 0.5f;
            float warnGain = 0f, warnFreq = 1560f, warnGate = 0f, warnDuty = 0.5f;
            if (me != null && on)
            {
                var slot = me.SelectedSlot;
                var def = slot != null ? WeaponCatalog.Get(slot.Weapon) : null;
                if (me.IncomingMissiles > 0)
                {
                    float k = Mathf.Clamp01(1f - me.IncomingEta / 6f);
                    warnGain = 0.34f; warnFreq = 1560f; warnGate = Mathf.Lerp(3f, 11f, k); warnDuty = 0.45f;
                }
                else if (me.LockedOnMe) { warnGain = 0.26f; warnFreq = 1320f; warnGate = 5f; warnDuty = 0.6f; }
                else if (me.BeingLocked > 0) { warnGain = 0.20f; warnFreq = 1180f; warnGate = 2.5f; warnDuty = 0.4f; }
                if (def != null && def.NeedsLock && me.Lock == LockPhase.Acquiring) { lockGain = 0.26f; lockFreq = 740f + 360f * me.LockProgress01; lockGate = Mathf.Lerp(2f, 9f, me.LockProgress01); lockDuty = 0.5f; }
                else if (def != null && def.NeedsLock && me.Lock == LockPhase.Locked) { lockGain = 0.3f; lockFreq = 1180f; lockGate = 0f; }
                else if (def != null && !def.NeedsLock && me.Lock == LockPhase.Locked) { lockGain = 0.12f; lockFreq = 520f; lockGate = 0f; }
            }
            Mixer.SetTone(0, lockFreq, lockGain, lockGate, lockDuty, 0.5f, -0.15f);
            Mixer.SetTone(1, warnFreq, warnGain, warnGate, warnDuty, 0.6f, 0.15f);
        }
    }
}
