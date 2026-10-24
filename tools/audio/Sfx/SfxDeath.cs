using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>
    /// The player's death, from inside (Juice/Fx/DeathFx.cs raises the matching events and plays these through IJuiceAudio): the canopy cracking, the hull
    /// screaming, the decompression roar, the pilot's own breathing and one heartbeat. Serious, never comic: no stingers, no melody, just a body in a
    /// breaking ship and then not much.
    /// </summary>
    public static class SfxDeath
    {
        static float Hit(float t, float k, float a = 0.001f) => Sd.Hit(t, k, a);
        static float Ex(float t, float a, float b, float tau) => Sd.Expo(t, a, b, tau);
        static Sd Noise(float len, uint seed, Func<float, float> amp, float colour = 1f) { var n = new Sd(len); n.Noise(amp, seed, colour); return n; }

        /// <summary>The canopy fracturing: one hard crack, a run of smaller ones spreading away from it, glass ticking, a low groan of the frame taking the load. 1.4 s.</summary>
        public static Sd Crack()
        {
            const float L = 1.4f;
            var s = new Sd(L);
            s.Mix(Noise(L, 701u, t => Hit(t, 55f, 0.0004f)).HighPass(1200f), 2f);                          // the first crack
            s.Mix(Noise(L, 702u, t => Hit(t, 22f, 0.0006f)).BandPass(2600f, 0.5f, 2), -3f);
            s.Crackle(t => 140f * MathF.Exp(-t * 3.2f) + 6f, t => 0.55f * MathF.Exp(-t * 2.2f), 703u, 2200f, 9000f, 4f);     // the fracture spreading
            s.Crackle(t => 40f * MathF.Exp(-t * 2.0f), t => 0.35f * MathF.Exp(-t * 1.5f), 704u, 800f, 2400f, 9f, 0.05f);
            s.Sine(t => Ex(t, 120f, 52f, 0.35f), t => 1.1f * Dsp.Smooth(t / 0.004f) * MathF.Exp(-t * 3.0f));                   // the frame, a blow felt in the chest
            s.Saw(t => Ex(t, 92f, 58f, 0.5f), t => 0.22f * Dsp.Smooth(t / 0.08f) * MathF.Exp(-t * 1.6f));                     // the groan
            s.Drive(1.5f);
            s.Reverb(0.35f, 0.07f, 0.35f, 0.7f, 3f, 0.15f);
            return s.Edges(0.3f, 120f).Normalize(-1.5f);
        }

        /// <summary>
        /// The hull tearing: a grinding, ripping roar of metal letting go, with plates popping. Broadband and irregular on purpose: nothing in it holds a pitch
        /// or repeats at a steady rate (the first version, a rising stack of tones with a warble, was heard as a high-pitched whirring and removed). 3.4 s.
        /// </summary>
        public static Sd Tear()
        {
            const float L = 3.4f;
            var s = new Sd(L);
            // two slow random walks (no steady rate) drive how hard the grinding bites
            var walkA = Noise(L, 711u, t => 1f, 0.02f).LowPass(6f, 0.1f);
            var walkB = Noise(L, 714u, t => 1f, 0.02f).LowPass(11f, 0.1f);
            float peakA = 1e-6f, peakB = 1e-6f;
            for (int i = 0; i < walkA.N; i++) { peakA = MathF.Max(peakA, MathF.Abs(walkA.X[i])); peakB = MathF.Max(peakB, MathF.Abs(walkB.X[i])); }
            Func<float, float> bite = t => { int i = Math.Min(walkA.N - 1, (int)(t * Sd.Rate)); return Math.Clamp(0.55f + 0.5f * walkA.X[i] / peakA, 0.05f, 1f); };
            Func<float, float> bite2 = t => { int i = Math.Min(walkB.N - 1, (int)(t * Sd.Rate)); return Math.Clamp(0.5f + 0.5f * walkB.X[i] / peakB, 0.05f, 1f); };
            Func<float, float> swell = t => Dsp.Smooth(t / 0.18f) * MathF.Exp(-MathF.Max(0f, t - 1.5f) * 0.85f);
            s.Mix(Noise(L, 712u, t => swell(t) * bite(t)).BandPass(t => 700f + 500f * bite2(t), 0.12f, 1), -2f);              // the grind: wide band, never a note
            s.Mix(Noise(L, 715u, t => swell(t) * bite2(t) * 0.8f).HighPass(2400f).LowPass(7500f, 0.1f), -9f);                    // the rip in the top
            s.Mix(Noise(L, 716u, t => Dsp.Smooth(t / 0.12f) * MathF.Exp(-t * 0.55f) * (0.6f + 0.4f * bite(t))).LowPass(150f, 0.2f, 2), 3f);   // the weight under it
            s.Crackle(t => 55f * MathF.Exp(-t * 0.45f) * bite2(t) + 3f, t => 0.34f, 713u, 500f, 3600f, 11f);                    // plates and rivets letting go
            s.Crackle(t => 9f * MathF.Exp(-t * 0.5f), t => 0.6f, 717u, 120f, 420f, 28f, 0.1f);                                   // the bigger ones, low and dull
            s.Drive(1.5f);
            s.Reverb(0.5f, 0.06f, 0.4f, 0.7f, 4f, 0.2f);
            return s.Edges(0.6f, 400f).Normalize(-2f);
        }

        /// <summary>The air going: a boom, then a roar that thins as the cabin empties, debris rattling past, and at the end almost nothing. 3.4 s.</summary>
        public static Sd Decompress()
        {
            const float L = 3.4f;
            var s = new Sd(L);
            s.Sine(t => Ex(t, 66f, 27f, 0.14f), t => 1.6f * Dsp.Smooth(t / 0.003f) * MathF.Exp(-t * 2.6f));                         // the blow
            s.Sine(t => 38f, t => 0.8f * Dsp.Smooth(t / 0.05f) * MathF.Exp(-t * 1.1f));
            // the roar: broadband noise whose top falls away as the pressure does
            var roar = Noise(L, 721u, t => Dsp.Smooth(t / 0.03f) * MathF.Exp(-t * 0.95f));
            roar.LowPass(t => 6500f * MathF.Exp(-t * 0.9f) + 220f, 0.15f, 2);
            s.Mix(roar, 3f);
            s.Mix(Noise(L, 722u, t => Dsp.Smooth(t / 0.04f) * MathF.Exp(-t * 1.7f)).BandPass(t => 2400f * MathF.Exp(-t * 0.9f) + 500f, 0.35f, 2), 1f);
            s.Mix(Noise(L, 723u, t => Dsp.Smooth(t / 0.02f) * MathF.Exp(-t * 0.7f)).LowPass(120f, 0.2f, 2), 2f);
            s.Crackle(t => 160f * MathF.Exp(-t * 1.2f), t => 0.5f * MathF.Exp(-t * 0.8f), 724u, 500f, 4200f, 7f);                      // loose things going past
            s.Drive(1.6f);
            s.Reverb(0.4f, 0.05f, 0.4f, 0.7f, 3f, 0.2f);
            return s.Edges(0.3f, 600f).Normalize(-1f);
        }

        /// <summary>Your own breathing, fast and ragged, three breaths getting shorter and weaker and the last one not finished. 3.6 s. Close and dry: it is inside the helmet of your head.</summary>
        public static Sd Breath()
        {
            const float L = 3.6f;
            var s = new Sd(L);
            float[] at = { 0.00f, 1.05f, 2.05f, 2.90f };
            float[] inh = { 0.38f, 0.32f, 0.26f, 0.20f };
            float[] exh = { 0.52f, 0.42f, 0.34f, 0.0f };
            float[] amp = { 0.9f, 0.8f, 0.62f, 0.4f };
            for (int i = 0; i < at.Length; i++)
            {
                float a0 = at[i], di = inh[i], de = exh[i], g = amp[i];
                var n = Noise(L, (uint)(731 + i), t =>
                {
                    float u = t - a0;
                    if (u < 0f) return 0f;
                    if (u < di) return Dsp.Smooth(u / (di * 0.8f)) * 0.9f;                                   // in: rises
                    float v = u - di;
                    if (de <= 0f) return 0f;
                    return v < de ? Dsp.Smooth(v / 0.05f) * MathF.Pow(1f - v / de, 1.4f) * 0.8f : 0f;        // out: a push that falls away
                });
                n.BandPass(t => { float u = t - a0; return u < di ? 700f + 1500f * (u / di) : 1700f - 800f * Dsp.Clamp01((u - di) / MathF.Max(de, 0.1f)); }, 0.35f, 2);
                n.HighPass(240f);
                s.Mix(n, Db.FromLin(g));
            }
            s.Mix(Noise(L, 741u, t => 0.12f * (0.6f + 0.4f * MathF.Sin(t * 31f))).BandPass(190f, 0.5f, 1), -12f);                     // the rasp in the throat
            s.Drive(1.2f);
            return s.Edges(1.0f, 250f).Normalize(-4f);
        }

        /// <summary>One heartbeat: lub, then dub a beat later and softer. Low, close, no click. 0.7 s.</summary>
        public static Sd Heartbeat()
        {
            const float L = 0.7f;
            var s = new Sd(L);
            s.Sine(t => Ex(t, 74f, 40f, 0.05f), t => 1.2f * Dsp.Smooth(t / 0.006f) * MathF.Exp(-t * 17f));
            s.Sine(t => Ex(t, 58f, 36f, 0.06f), t => t < 0.19f ? 0f : 0.8f * Dsp.Smooth((t - 0.19f) / 0.006f) * MathF.Exp(-(t - 0.19f) * 19f));
            s.Mix(Noise(L, 751u, t => Hit(t, 40f, 0.002f)).LowPass(180f, 0.2f, 2), -6f);
            s.Mix(Noise(L, 752u, t => t < 0.19f ? 0f : Hit(t - 0.19f, 45f, 0.002f)).LowPass(150f, 0.2f, 2), -9f);
            s.Drive(1.3f);
            return s.Edges(0.5f, 120f).Normalize(-2f);
        }
    }
}
