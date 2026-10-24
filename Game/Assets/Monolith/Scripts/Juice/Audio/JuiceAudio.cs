using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>The sounds the juice asks for. The placeholder synth makes all of them; the real audio system replaces it.</summary>
    public enum JuiceCue
    {
        LaserLight, LaserHeavy, LaserEnemy, FlakShell, MissileLaunch,
        HitShield, HitHull, HitPlayerShield, HitPlayerHull,
        ExplosionSmall, ExplosionMedium, ExplosionLarge, Shockwave, MetalCrack, SubsystemBlast,
        FlakBurst, NearMiss,
        BeamCharge, BeamLoop, BeamImpactLoop,
        BreakUp, UiClick,
        /// <summary>Added with the audio system: a capital ship's turret, a heavy torpedo leaving the rail, a ship arriving and a ship leaving (gain says how big).</summary>
        LaserTurret, TorpedoLaunch, JumpIn, JumpOut,
        /// <summary>Added with the boost: the ignition whoomph under the audio system's own roar, and the empty-tank sputter (BoostFx).</summary>
        BoostWhoomph, BoostSputter,
    }

    /// <summary>
    /// Where the real audio system plugs in. The composition root sets <see cref="Factory"/>; a juice director that is not handed an audio
    /// implementation asks it first and falls back to the placeholder synth when it returns null.
    /// </summary>
    public static class JuiceAudioHooks
    {
        public static System.Func<JuiceSettings, IJuiceAudio> Factory;
    }

    /// <summary>A looping sound that can be moved and faded, then stopped (a beam's roar).</summary>
    public interface IJuiceLoop
    {
        void Set(Vector3 position, float gain);
        void Stop(float fadeSeconds);
    }

    /// <summary>
    /// What the juice needs from the audio system, and nothing more: play a cue at a place, hold a loop, duck everything else for
    /// a huge moment. The placeholder (<see cref="ProceduralAudio"/>) builds its sounds in code; the real audio system implements this
    /// interface and the juice never knows the difference. <c>distance</c> is metres from the listener: implementations pick near
    /// and far versions of the cue from it.
    /// </summary>
    public interface IJuiceAudio
    {
        float MasterVolume { get; set; }
        bool Enabled { get; set; }
        void Play(JuiceCue cue, Vector3 position, float gain = 1f, float distance = 0f);
        IJuiceLoop StartLoop(JuiceCue cue, Vector3 position, float gain = 1f);
        /// <summary>Lower everything else to <c>1 - amount</c> for <c>seconds</c> (the biggest moments duck the rest).</summary>
        void Duck(float amount, float seconds);
        void Update(float dt);
        /// <summary>The game's speed (1 = normal): slow motion drops the pitch of what is playing.</summary>
        void SetTimeScale(float scale);
    }

    /// <summary>Silence: used when sound is unavailable (no audio device, a headless run).</summary>
    public sealed class NullAudio : IJuiceAudio
    {
        sealed class NullLoop : IJuiceLoop { public void Set(Vector3 position, float gain) { } public void Stop(float fadeSeconds) { } }
        public float MasterVolume { get; set; }
        public bool Enabled { get; set; }
        public void Play(JuiceCue cue, Vector3 position, float gain = 1f, float distance = 0f) { }
        public IJuiceLoop StartLoop(JuiceCue cue, Vector3 position, float gain = 1f) => new NullLoop();
        public void Duck(float amount, float seconds) { }
        public void Update(float dt) { }
        public void SetTimeScale(float scale) { }
    }
}
