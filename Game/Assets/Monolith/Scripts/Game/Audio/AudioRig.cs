using Monolith.Audio;
using Monolith.Core;
using Monolith.Juice;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Builds the audio system for a scene and hands the juice its sound. The audio system is made once per run (by whoever asks first: the
    /// juice director, or the composition root after the scene is built) and decides from the command line what kind it is.
    /// Flags: <c>-mnNoAudio</c> none; <c>-mnAudioMute</c> every path runs, nothing is heard; <c>-mnAudioCapture file.wav</c> render in step with the
    /// simulation without a sound card and record the mix (captures are silent otherwise); <c>-mnAudio</c> sound in a capture run;
    /// <c>-mnAudioUser dir</c> another folder for the player's own files; <c>-mnAudioLog</c> more log lines.
    /// </summary>
    public static class AudioRig
    {
        /// <summary>Make the juice ask for the real audio. Call before the scene is built.</summary>
        public static void Install(GameWorld world, Transform parent)
        {
            JuiceAudioHooks.Factory = settings =>
            {
                var sys = EnsureSystem(world, parent);
                return sys == null ? null : new JuiceAudioAdapter(sys);
            };
        }

        /// <summary>The audio system for this run, made if need be; null when this run is to have none.</summary>
        public static AudioSystem EnsureSystem(GameWorld world, Transform parent)
        {
            if (AudioSystem.Active != null) return AudioSystem.Active;
            if (CommandLine.Has("-mnNoAudio")) return null;
            bool capture = CommandLine.Has("-mnCapture");
            string record = CommandLine.Get("-mnAudioCapture");
            var o = new AudioOptions { SavePrefs = !capture };
            if (!string.IsNullOrEmpty(record)) { o.Offline = true; o.RecordPath = record; }
            else if (capture && !CommandLine.Has("-mnAudio")) return null;
            else if (Application.isBatchMode && !CommandLine.Has("-mnAudioMute")) return null;        // headless runs make no sound (and the soaks must not record hours of it)
            o.Silent = CommandLine.Has("-mnAudioMute");
            return AudioSystem.Create(world, parent, o);
        }
    }

    /// <summary>The juice's view of the audio system: its cues by name, its loops, its ducking and its slow motion.</summary>
    public sealed class JuiceAudioAdapter : IJuiceAudio
    {
        sealed class Loop : IJuiceLoop
        {
            readonly LoopHandle _h;
            public Loop(LoopHandle h) { _h = h; }
            public void Set(Vector3 position, float gain) => _h.Set(position, gain);
            public void Stop(float fadeSeconds) => _h.Stop(fadeSeconds);
        }

        readonly AudioSystem _a;
        float _volume = 0.8f;
        bool _enabled = true;

        public JuiceAudioAdapter(AudioSystem a) { _a = a; }

        public float MasterVolume { get => _volume; set { _volume = value; _a.SetJuiceMaster(_volume, _enabled); } }
        public bool Enabled { get => _enabled; set { _enabled = value; _a.SetJuiceMaster(_volume, _enabled); } }

        public static string CueId(JuiceCue cue, float gain)
        {
            switch (cue)
            {
                case JuiceCue.LaserLight: return "laser_light";
                case JuiceCue.LaserHeavy: return "laser_heavy";
                case JuiceCue.LaserEnemy: return "laser_enemy";
                case JuiceCue.LaserTurret: return "laser_turret";
                case JuiceCue.FlakShell: return "flak_shell";
                case JuiceCue.MissileLaunch: return "missile_launch";
                case JuiceCue.TorpedoLaunch: return "torpedo_launch";
                case JuiceCue.HitShield: return "hit_shield";
                case JuiceCue.HitHull: case JuiceCue.MetalCrack: return "hit_hull";
                case JuiceCue.HitPlayerShield: return "hit_shield_player";
                case JuiceCue.HitPlayerHull: return gain > 0.92f ? "hit_hull_player_heavy" : "hit_hull_player";
                case JuiceCue.ExplosionSmall: return "explosion_small";
                case JuiceCue.ExplosionMedium: return "explosion_medium";
                case JuiceCue.ExplosionLarge: return "explosion_large";
                case JuiceCue.Shockwave: return "shockwave";
                case JuiceCue.SubsystemBlast: return "subsystem_blast";
                case JuiceCue.FlakBurst: return "flak_burst";
                case JuiceCue.NearMiss: return "near_miss";
                case JuiceCue.BeamCharge: return "beam_charge";
                case JuiceCue.BeamLoop: return "beam_loop";
                case JuiceCue.BeamImpactLoop: return "beam_impact_loop";
                case JuiceCue.BreakUp: return "breakup";
                case JuiceCue.UiClick: return "ui_click";
                case JuiceCue.BoostWhoomph: return "afterburner_whoomph";
                case JuiceCue.BoostSputter: return "afterburner_sputter";
                case JuiceCue.DeathCrack: return "death_crack";
                case JuiceCue.DeathScream: return "death_scream";
                case JuiceCue.DeathDecompress: return "death_decompress";
                case JuiceCue.DeathBreath: return "death_breath";
                case JuiceCue.DeathHeartbeat: return "death_heartbeat";
                case JuiceCue.JumpIn: return gain >= 0.9f ? "jump_in_big" : "jump_in";
                case JuiceCue.JumpOut: return gain >= 0.9f ? "jump_out_big" : "jump_out";
                default: return cue.ToString().ToLowerInvariant();
            }
        }

        public void Play(JuiceCue cue, Vector3 position, float gain = 1f, float distance = 0f)
        {
            if (!_enabled) return;
            _a.PlayCue(CueId(cue, gain), position, gain, distance > 0f ? distance : -1f);
        }

        public IJuiceLoop StartLoop(JuiceCue cue, Vector3 position, float gain = 1f)
        {
            return new Loop(_a.StartLoop(CueId(cue, gain), position, gain));
        }

        public void Duck(float amount, float seconds) => _a.Duck(amount, seconds);
        public void Update(float dt) { }
        public void SetTimeScale(float scale) => _a.SetTimeScale(scale);
    }
}
