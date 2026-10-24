using System;
using System.Collections.Generic;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>
    /// The instrument library of the score: the 80s analog palette. The names are what a MIDI track is called (see audio_src/compose), and the
    /// comments say which machine each one is after. All numbers are ours; nothing is sampled.
    /// </summary>
    public static class Patches
    {
        static readonly Dictionary<string, Patch> _lib = new Dictionary<string, Patch>();
        static bool _built;

        public static Patch Get(string name)
        {
            Build();
            if (!_lib.TryGetValue(name, out var p)) throw new KeyNotFoundException($"no patch '{name}' (have: {string.Join(", ", _lib.Keys)})");
            return p;
        }

        public static IEnumerable<string> Names { get { Build(); return _lib.Keys; } }

        static void Add(Patch p) { _lib[p.Name] = p; }

        static void Build()
        {
            if (_built) return;
            _built = true;

            // Juno-60 style string pad: saw + PWM pulse + sub, a soft 4-pole filter opening slowly, chorus II.
            Add(new Patch
            {
                Name = "pad_juno",
                Osc = { new OscDef(0, 0.8f, 0, -5f), new OscDef(1, 0.6f, 0, 5f, 0.5f, 0.22f, 0.31f) },
                Sub = 0.22f, SubWave = 2, Unison = 2, UniCents = 4f, Spread = 0.35f,
                Filter = FilterKind.Ladder, Cut = 1900f, Res = 0.10f, KeyTrack = 0.55f, FEnvAmt = 0.8f, Drive = 1.0f,
                Amp = new[] { 1.1f, 1.2f, 0.85f, 2.0f }, FEnv = new[] { 1.6f, 2.0f, 0.6f, 1.5f },
                Drift = 4f, VelSens = 0.25f, Chorus = "II", HallSend = 0.38f, PlateSend = 0.08f,
            });

            // Oberheim / Prophet-5 style warm pad: detuned saw stack, rounder, a little movement from the filter LFO.
            Add(new Patch
            {
                Name = "pad_warm",
                Osc = { new OscDef(0, 0.9f, 0, -9f), new OscDef(0, 0.9f, 0, 9f), new OscDef(2, 0.4f, -12, 0f) },
                Unison = 3, UniCents = 7f, Spread = 0.7f,
                Filter = FilterKind.Ladder, Cut = 1300f, Res = 0.14f, KeyTrack = 0.5f, FEnvAmt = 0.6f, LfoCut = 0.25f, LfoRate = 0.19f,
                Amp = new[] { 1.4f, 1.0f, 0.9f, 2.4f }, FEnv = new[] { 2.0f, 2.0f, 0.55f, 1.8f },
                Drift = 5f, VelSens = 0.2f, Chorus = "ens", HallSend = 0.45f,
            });

            // Solina string machine: sawtooth ensemble through the three-phase chorus.
            Add(new Patch
            {
                Name = "strings_ens",
                Osc = { new OscDef(0, 1f, 0, -6f), new OscDef(0, 0.7f, 12, 6f) },
                Unison = 2, UniCents = 5f, Spread = 0.5f,
                Filter = FilterKind.Svf12, Cut = 3400f, Res = 0.1f, KeyTrack = 0.4f,
                Amp = new[] { 0.45f, 0.4f, 0.92f, 0.9f },
                Drift = 4f, VelSens = 0.3f, VibCents = 6f, VibDelay = 0.6f, VibRate = 5.1f, Chorus = "ens", HallSend = 0.3f,
            });

            // Yamaha CS-80 brass (Vangelis): two saws and a pulse, a ladder filter that swells open as you lean on the key, a scoop up into the pitch, slow vibrato.
            Add(new Patch
            {
                Name = "brass_cs80",
                Osc = { new OscDef(0, 1f, 0, -7f), new OscDef(0, 1f, 0, 7f), new OscDef(1, 0.45f, -12, 0f, 0.42f) },
                Unison = 2, UniCents = 3f, Spread = 0.3f,
                Filter = FilterKind.Ladder, Cut = 620f, Res = 0.26f, KeyTrack = 0.55f, FEnvAmt = 2.5f, VelCut = 1.1f, ExprToCut = 1.4f, Drive = 1.35f,
                Amp = new[] { 0.09f, 0.5f, 0.88f, 0.55f }, FEnv = new[] { 0.26f, 0.9f, 0.42f, 0.5f },
                Glide = 0.06f, ScoopSemis = 0.8f, ScoopTime = 0.08f, VibCents = 13f, VibDelay = 0.55f, VibRate = 5.0f,
                Drift = 4f, VelSens = 0.35f, Chorus = "II", HallSend = 0.42f, DelaySend = 0.12f, PlateSend = 0.1f,
            });

            // The same brass, darker and lower, thicker: the low horn of dread.
            Add(new Patch
            {
                Name = "brass_dark",
                Osc = { new OscDef(0, 1f, 0, -16f), new OscDef(0, 1f, 0, 16f), new OscDef(0, 0.8f, -12, 0f), new OscDef(1, 0.4f, -12, 4f, 0.35f) },
                Sub = 0.35f, SubWave = 3, Unison = 2, UniCents = 5f, Spread = 0.45f,
                Filter = FilterKind.Ladder, Cut = 360f, Res = 0.22f, KeyTrack = 0.35f, FEnvAmt = 2.2f, VelCut = 1.0f, ExprToCut = 1.6f, Drive = 1.9f,
                Amp = new[] { 0.35f, 0.7f, 0.9f, 1.4f }, FEnv = new[] { 0.55f, 1.2f, 0.45f, 1.0f },
                ScoopSemis = 1.0f, ScoopTime = 0.18f, VibCents = 10f, VibDelay = 0.8f, VibRate = 4.6f,
                Drift = 6f, VelSens = 0.3f, Chorus = "II", HallSend = 0.55f, PlateSend = 0.1f, Sat = 0.3f,
            });

            // Jupiter-8 style brass stab: short, bright, sprung.
            Add(new Patch
            {
                Name = "brass_stab",
                Osc = { new OscDef(0, 1f, 0, -10f), new OscDef(0, 1f, 0, 10f), new OscDef(1, 0.4f, -12, 0f, 0.4f) },
                Unison = 3, UniCents = 8f, Spread = 0.6f,
                Filter = FilterKind.Ladder, Cut = 760f, Res = 0.22f, KeyTrack = 0.5f, FEnvAmt = 3.0f, VelCut = 0.8f, Drive = 1.5f,
                Amp = new[] { 0.004f, 0.22f, 0.45f, 0.2f }, FEnv = new[] { 0.004f, 0.22f, 0.12f, 0.18f },
                ScoopSemis = 0.5f, ScoopTime = 0.04f, Drift = 3f, VelSens = 0.45f, Chorus = "II", PlateSend = 0.22f, HallSend = 0.12f,
            });

            // Mono lead (Prophet / Moog): saw and PWM pulse, glide, delayed vibrato, a little grit.
            Add(new Patch
            {
                Name = "lead_prophet",
                Osc = { new OscDef(0, 1f, 0, -6f), new OscDef(1, 0.7f, 0, 6f, 0.5f, 0.2f, 0.4f) },
                Sub = 0.18f, SubWave = 2, Unison = 1,
                Filter = FilterKind.Ladder, Cut = 2600f, Res = 0.30f, KeyTrack = 0.6f, FEnvAmt = 1.1f, VelCut = 0.7f, ExprToCut = 0.8f, Drive = 1.6f,
                Amp = new[] { 0.012f, 0.25f, 0.85f, 0.38f }, FEnv = new[] { 0.01f, 0.35f, 0.4f, 0.3f },
                Glide = 0.075f, Mono = true, VibCents = 11f, VibDelay = 0.4f, VibRate = 5.4f,
                Drift = 3f, VelSens = 0.4f, Chorus = "I", DelaySend = 0.34f, PlateSend = 0.22f, HallSend = 0.1f,
            });

            // Moog-style bass: saw, saw an octave down, sine sub; a plucked filter.
            Add(new Patch
            {
                Name = "bass_moog",
                Osc = { new OscDef(0, 1f, 0, 0f), new OscDef(0, 0.6f, -12, 4f) },
                Sub = 0.35f, SubWave = 3,
                Filter = FilterKind.Ladder, Cut = 240f, Res = 0.34f, KeyTrack = 0.35f, FEnvAmt = 2.6f, VelCut = 0.6f, Drive = 1.9f,
                Amp = new[] { 0.003f, 0.2f, 0.75f, 0.09f }, FEnv = new[] { 0.002f, 0.17f, 0.12f, 0.1f },
                Mono = true, Drift = 2f, VelSens = 0.3f, Sat = 0.25f,
            });

            // Carpenter-style bass: a fat, round sequenced pulse, less filter movement, a little more weight.
            Add(new Patch
            {
                Name = "bass_pulse",
                Osc = { new OscDef(1, 1f, 0, 0f, 0.5f, 0.12f, 0.2f), new OscDef(0, 0.5f, -12, 3f) },
                Sub = 0.4f, SubWave = 3,
                Filter = FilterKind.Ladder, Cut = 330f, Res = 0.24f, KeyTrack = 0.3f, FEnvAmt = 1.6f, VelCut = 0.5f, Drive = 1.6f,
                Amp = new[] { 0.004f, 0.25f, 0.8f, 0.07f }, FEnv = new[] { 0.003f, 0.14f, 0.35f, 0.08f },
                Mono = true, Drift = 2f, VelSens = 0.3f, Sat = 0.2f,
            });

            // A deep sub: sine and triangle, a touch of saturation so it travels on small speakers.
            Add(new Patch
            {
                Name = "bass_sub",
                Osc = { new OscDef(3, 1f, 0, 0f), new OscDef(2, 0.35f, 0, 2f) },
                Filter = FilterKind.Svf12, Cut = 300f, Res = 0.05f, KeyTrack = 0.2f,
                Amp = new[] { 0.02f, 0.3f, 0.92f, 0.35f }, Mono = true, Drift = 1.5f, VelSens = 0.25f, Sat = 0.55f,
            });

            // Tangerine Dream sequencer: a plucked pulse with a resonant filter, short and bright, meant to be echoed.
            Add(new Patch
            {
                Name = "arp_seq",
                Osc = { new OscDef(1, 1f, 0, -5f, 0.36f, 0.14f, 0.27f), new OscDef(0, 0.55f, 0, 6f) },
                Unison = 1,
                Filter = FilterKind.Ladder, Cut = 1050f, Res = 0.42f, KeyTrack = 0.55f, FEnvAmt = 2.1f, VelCut = 1.0f, Drive = 1.3f,
                Amp = new[] { 0.002f, 0.17f, 0.0f, 0.12f }, FEnv = new[] { 0.002f, 0.14f, 0.0f, 0.1f }, AmpPercussive = true,
                Drift = 2f, VelSens = 0.5f, DelaySend = 0.48f, PlateSend = 0.18f, HallSend = 0.1f, Chorus = "I",
            });

            // The same sequencer, rounder and quieter, for the ambient bed.
            Add(new Patch
            {
                Name = "arp_soft",
                Osc = { new OscDef(1, 1f, 0, 0f, 0.5f, 0.1f, 0.2f), new OscDef(2, 0.7f, 12, 0f) },
                Filter = FilterKind.Ladder, Cut = 780f, Res = 0.28f, KeyTrack = 0.5f, FEnvAmt = 1.6f, VelCut = 0.8f, Drive = 1.1f,
                Amp = new[] { 0.004f, 0.3f, 0.0f, 0.4f }, FEnv = new[] { 0.004f, 0.25f, 0.0f, 0.25f }, AmpPercussive = true,
                Drift = 2f, VelSens = 0.5f, DelaySend = 0.6f, PlateSend = 0.2f, HallSend = 0.3f,
            });

            // A glassy FM bell (DX style): inharmonic, long, lit by a bright strike.
            Add(new Patch
            {
                Name = "bell_fm", Kind = VoiceKind.Fm,
                Fm = { new FmPair(1f, 3.5f, 3.4f, 0.55f, 1f, 4.5f), new FmPair(3f, 1.414f, 1.6f, 0.2f, 0.22f, 1.4f), new FmPair(0.5f, 1f, 0.7f, 2.0f, 0.18f, 6f) },
                Amp = new[] { 0.002f, 0f, 0f, 0.25f }, Drift = 3f, VelSens = 0.5f, DelaySend = 0.3f, HallSend = 0.6f, PlateSend = 0.1f,
            });

            // Electric piano with a tine attack, for the soft Blade Runner touches.
            Add(new Patch
            {
                Name = "epiano_fm", Kind = VoiceKind.Fm,
                Fm = { new FmPair(1f, 1f, 1.5f, 0.35f, 1f, 2.8f, 0.15f), new FmPair(1f, 14f, 1.2f, 0.05f, 0.3f, 0.25f) },
                Amp = new[] { 0.002f, 0f, 0f, 0.3f }, Drift = 2f, VelSens = 0.6f, Chorus = "I", DelaySend = 0.25f, HallSend = 0.45f,
            });

            // Choir: a sawtooth source through three formants, an ensemble of voices, slow breath and vibrato.
            Add(new Patch
            {
                Name = "choir_ahh", Kind = VoiceKind.Formant,
                Vowels = new[] {
                    new[] { 800f, 1150f, 2900f, 1.0f, 0.55f, 0.18f, 9f },     // "ah"
                    new[] { 450f, 800f, 2830f, 1.0f, 0.7f, 0.12f, 9f },       // "oh"
                },
                Unison = 5, UniCents = 11f, Spread = 0.8f, VibCents = 20f, VibDelay = 0.7f, VibRate = 5.4f, Breath = 0.05f,
                Amp = new[] { 1.0f, 0.5f, 0.9f, 1.8f }, Drift = 6f, VelSens = 0.25f, HallSend = 0.55f, PlateSend = 0.1f,
            });

            // The dark bed: a stack of low saws through a slowly breathing filter with a breath of noise.
            Add(new Patch
            {
                Name = "drone_dark",
                Osc = { new OscDef(0, 1f, 0, -13f), new OscDef(0, 1f, 0, 0f), new OscDef(0, 0.8f, 0, 13f), new OscDef(1, 0.4f, -12, 0f, 0.5f, 0.2f, 0.07f) },
                Sub = 0.5f, SubWave = 3, Noise = 0.04f, NoiseColor = 0.12f, Unison = 3, UniCents = 9f, Spread = 0.6f,
                Filter = FilterKind.Ladder, Cut = 170f, Res = 0.36f, KeyTrack = 0.6f, LfoCut = 1.15f, LfoRate = 0.0625f, FEnvAmt = 0f, Drive = 1.4f,
                Amp = new[] { 2.4f, 1.0f, 1.0f, 3.2f }, Drift = 8f, VelSens = 0.1f, Chorus = "ens", HallSend = 0.5f, Sat = 0.25f,
            });

            // An analog sweep: saw plus noise, for risers and falls (the filter is driven by CC74 in the MIDI).
            Add(new Patch
            {
                Name = "sweep",
                Osc = { new OscDef(0, 1f, 0, -10f), new OscDef(0, 1f, 0, 10f) },
                Noise = 0.35f, NoiseColor = 0.6f, Unison = 2, UniCents = 14f, Spread = 0.8f,
                Filter = FilterKind.Ladder, Cut = 500f, Res = 0.55f, KeyTrack = 0.1f, Drive = 1.2f,
                Amp = new[] { 0.02f, 0.1f, 1.0f, 0.6f }, Drift = 4f, VelSens = 0.1f, HallSend = 0.4f, PlateSend = 0.15f,
            });

            // Thin Carpenter pulse for stings and ostinato stabs: nasal, hard, a little menacing.
            Add(new Patch
            {
                Name = "pulse_thin",
                Osc = { new OscDef(1, 1f, 0, 0f, 0.22f, 0.08f, 0.5f), new OscDef(1, 0.6f, 12, 5f, 0.3f) },
                Filter = FilterKind.Svf24, Cut = 2400f, Res = 0.35f, KeyTrack = 0.7f, FEnvAmt = 0.8f,
                Amp = new[] { 0.004f, 0.2f, 0.5f, 0.15f }, FEnv = new[] { 0.004f, 0.2f, 0.2f, 0.15f },
                Drift = 2f, VelSens = 0.5f, DelaySend = 0.3f, PlateSend = 0.2f, Chorus = "I",
            });
        }

        // ------------------------------------------------------------------------------------------------ calibration

        static readonly Dictionary<string, int> RefKeys = new Dictionary<string, int>
        {
            { "bass_moog", 36 }, { "bass_pulse", 36 }, { "bass_sub", 33 }, { "drone_dark", 38 }, { "brass_dark", 45 }, { "brass_stab", 55 },
            { "choir_ahh", 57 }, { "bell_fm", 74 }, { "arp_seq", 62 }, { "arp_soft", 62 }, { "lead_prophet", 69 },
        };

        /// <summary>Set every patch's gain so that a note at velocity 100 is equally loud (momentary K-weighted, about -33 LUFS): then 0 dB means the same on every track.</summary>
        public static void Calibrate(int rate = 48000)
        {
            Build();
            foreach (var p in _lib.Values)
            {
                p.Gain = 1f;
                int key = RefKeys.TryGetValue(p.Name, out var k) ? k : 60;
                int n = (int)(3.2 * rate);
                var L = new float[n]; var R = new float[n];
                var au = new TrackAuto();
                var note = new MidiNote { Start = 0.02, Dur = 1.6, Key = key, Vel = 100, Id = 1 };
                SynthVoice.Render(p, note, -1, 99, au, L, R, rate, 12345u, 0);
                var b = PcmBuffer.FromStereo(rate, L, R);
                var m = LoudnessMeter.Measure(b);
                float mom = m.MaxMomentary;
                float want = -33f;
                p.Gain = mom < -100f ? 1f : Db.ToLin(want - mom);
            }
        }
    }
}
