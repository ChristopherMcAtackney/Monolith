using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>
    /// The cabin on foot: a few steps in a very small, hard room, the harness, the main breaker, a scan's contact, the pilot's breath, a hand repair knitting living tech, the
    /// rack, the lights flickering. Close and quiet: this is the room where you hear your own breathing.
    /// </summary>
    public static class SfxCabin
    {
        static float Hit(float t, float k, float a = 0.001f) => Sd.Hit(t, k, a);
        static float Ex(float t, float a, float b, float tau) => Sd.Expo(t, a, b, tau);
        static Sd Noise(float len, uint seed, Func<float, float> amp, float colour = 1f) { var n = new Sd(len); n.Noise(amp, seed, colour); return n; }

        /// <summary>One footfall on a deck. kind: 0 plate, 1 grating, 2 a rung or riser.</summary>
        public static Sd Step(int kind, int v)
        {
            float k = v == 0 ? 1f : v == 1 ? 0.9f : 1.12f;
            var s = new Sd(0.5f);
            s.Sine(t => Ex(t, 130f * k, 52f, 0.03f), t => (kind == 2 ? 0.7f : 1.0f) * Hit(t, 22f, 0.002f));
            s.Mix(Noise(0.5f, (uint)(601 + kind * 7 + v), t => Hit(t, 90f, 0.0006f)).BandPass(kind == 1 ? 1500f : 800f, 0.4f, 1), kind == 2 ? -2f : -5f);
            var ring = new Sd(0.5f);
            ring.Clang(kind == 0 ? 360f * k : kind == 1 ? 540f * k : 900f * k, kind == 0 ? 0.16f : kind == 1 ? 0.4f : 0.22f, kind == 0 ? 0.25f : 0.5f, (uint)(610 + kind * 3 + v), 0.002f, 0.8f);
            s.Mix(ring, -6f);
            if (kind == 1) s.Crackle(t => 60f * MathF.Exp(-t * 9f), t => 0.4f, (uint)(620 + v), 1200f, 3500f, 8f);
            s.Drive(1.2f);
            s.LowPass(t => 6500f, 0.1f);
            s.Reverb(0.35f, 0.18f, 0.3f, 0.7f, 1f, 0.12f);
            return s.Edges(0.3f, 30f).Normalize(-1f);
        }

        /// <summary>The harness: a buckle, a strap running, the seat taking or losing the pilot's weight. open: standing up.</summary>
        public static Sd Seat(bool open)
        {
            var s = new Sd(1.2f);
            float a = open ? 0.0f : 0.25f, b = open ? 0.22f : 0.0f;
            s.Mix(Noise(1.2f, 631, t => Hit(t, 60f, 0.0003f)).BandPass(2600f, 0.5f), -5f, a);
            s.Clang(1900f, 0.05f, 0.3f, 632u, a, 0.9f);
            s.Mix(Noise(1.2f, 633, t => Hit(t, 60f, 0.0003f)).BandPass(2100f, 0.5f), -7f, b + 0.14f);
            // the strap and the seat: a soft creak and a low rub
            s.Saw(t => 130f + 25f * MathF.Sin(t * 4f), t => 0.18f * Dsp.Smooth((t - 0.1f) / 0.15f) * Dsp.Smooth((0.95f - t) / 0.3f), 0.2f);
            s.BandPass(t => 480f + 380f * MathF.Sin(t * 3f), 0.7f, 2);
            s.Mix(Noise(1.2f, 634, t => 0.6f * Dsp.Smooth((t - 0.05f) / 0.2f) * Dsp.Smooth((0.9f - t) / 0.35f)).BandPass(900f, 0.35f), -14f);
            s.Sine(t => Ex(t, 90f, 50f, 0.05f), t => 0.5f * Hit(t, 12f, 0.004f), 0f, open ? 0.3f : 0.05f);
            s.Drive(1.2f);
            s.Reverb(0.3f, 0.14f, 0.3f, 0.7f, 1f, 0.1f);
            return s.Edges(1f, 120f).Normalize(-1f);
        }

        /// <summary>The main breaker. Off: a heavy clunk, the displays' whine falling away, a crackle. On: the clunk, the relays, a rising whine, three small chirps as the HUD boots.</summary>
        public static Sd Breaker(bool on)
        {
            var s = new Sd(on ? 2.2f : 1.8f);
            s.Sine(t => Ex(t, 105f, 42f, 0.03f), t => 1.3f * Hit(t, 14f, 0.002f));
            s.Clang(240f, 0.28f, 0.6f, on ? 641u : 642u);
            s.Mix(Noise(2.2f, 643, t => Hit(t, 80f, 0.0003f)).BandPass(1800f, 0.4f), -3f);
            if (on)
            {
                for (int i = 0; i < 6; i++) s.Mix(Noise(0.1f, (uint)(650 + i), t => Hit(t, 90f, 0.0003f)).BandPass(2800f, 0.5f), -9f, 0.25f + i * 0.045f);
                s.Sine(t => 60f * MathF.Pow(15f, Dsp.Clamp01((t - 0.3f) / 0.9f)), t => t < 0.3f ? 0f : 0.35f * Dsp.Smooth((t - 0.3f) / 0.6f) * Dsp.Smooth((1.5f - t) / 0.5f));
                for (int i = 0; i < 3; i++) s.Sine(t => 1500f + 380f * i, t => 0.2f * Hit(t, 22f, 0.002f), 0f, 1.3f + i * 0.14f);
            }
            else
            {
                s.Sine(t => Ex(t, 1400f, 55f, 0.35f), t => 0.4f * Hit(t, 3f, 0.002f), 0f, 0.05f);
                s.Crackle(t => 140f * MathF.Exp(-t * 3.2f), t => 0.5f, 644u, 1400f, 7000f, 5f, 0.05f);
            }
            s.Drive(1.4f);
            s.Reverb(0.5f, 0.15f, 0.4f, 0.7f, 2f, 0.3f);
            return s.Edges(0.3f, 150f).Normalize(-1f);
        }

        /// <summary>A scan finds the ship: a low bong rolling through the hull and a thin ping, then the cold.</summary>
        public static Sd ScanHit()
        {
            var s = new Sd(4.2f);
            s.Fm(t => Ex(t, 240f, 205f, 0.4f), 1.5f, t => 2.2f * MathF.Exp(-t * 2.2f), t => 0.8f * Hit(t, 1.1f, 0.004f));
            s.Sine(t => 48f, t => 0.9f * Hit(t, 1.0f, 0.01f));
            s.Sine(t => 1800f, t => 0.12f * Hit(t, 5f, 0.001f), 0f, 0.03f);
            s.Clang(520f, 1.6f, 0.15f, 661u, 0.02f, 0.6f);
            s.Drive(1.2f);
            s.Reverb(2.6f, 0.4f, 1.2f, 0.55f, 14f, 1.4f);
            return s.Edges(0.5f, 250f).Normalize(-1f);
        }

        /// <summary>One breath. In: a swelling hush with a catch; out: a longer, falling one. Two variants of each.</summary>
        public static Sd Breath(bool inhale, int v)
        {
            float L = inhale ? 1.3f : 1.8f, k = v == 0 ? 1f : 1.12f;
            var s = new Sd(L);
            s.Mix(Noise(L, (uint)(671 + v + (inhale ? 0 : 10)), t => inhale ? Dsp.Smooth(t / 0.9f) * Dsp.Smooth((L - t) / 0.3f) : Dsp.Smooth(t / 0.25f) * MathF.Exp(-t * 1.9f)).BandPass(t => inhale ? (500f + 900f * t / L) * k : (1500f - 800f * t / L) * k, 0.3f, 2), 0f);
            s.Mix(Noise(L, (uint)(681 + v), t => 0.35f * (inhale ? Dsp.Smooth(t / 0.9f) : MathF.Exp(-t * 2f)) * Dsp.Smooth((L - t) / 0.3f)).HighPass(3500f), -10f);
            s.Sine(t => 110f * k, t => 0.04f * (inhale ? Dsp.Smooth(t / 0.9f) : MathF.Exp(-t * 2f)));
            s.Edges(40f, 200f);
            return s.Normalize(-1f);
        }

        /// <summary>Living tech knitting under a hand: a low wet pulse with soft squelches and a faint pad. A 3 s loop (its pitch rises as the repair progresses).</summary>
        public static Sd RepairLoop()
        {
            const float L = 3.5f;
            var s = new Sd(L);
            s.Sine(t => 72f, t => 0.7f * (0.65f + 0.35f * MathF.Sin(t * Dsp.TwoPi * 2f)));
            s.Sine(t => 144.5f, t => 0.25f * (0.5f + 0.5f * MathF.Sin(t * Dsp.TwoPi * 2f + 1f)));
            s.Saw(t => 108f, t => 0.1f, 0.3f);
            s.Ladder(t => 420f + 160f * MathF.Sin(t * Dsp.TwoPi * 0.5f), 0.3f, 1.3f);
            s.Crackle(t => 11f, t => 0.7f, 691u, 260f, 900f, 40f);
            s.Mix(Noise(L, 692, t => 0.5f * (0.5f + 0.5f * MathF.Sin(t * Dsp.TwoPi * 2f))).BandPass(700f, 0.4f, 2), -14f);
            s.Drive(1.3f);
            return s.MakeLoop(0.3f).Normalize(-1f);
        }

        public static Sd RepairDone()
        {
            var s = new Sd(1.6f);
            s.Sine(t => 392f, t => 0.5f * Hit(t, 3.5f, 0.01f));
            s.Sine(t => 587f, t => 0.4f * Hit(t, 3.5f, 0.01f), 0f, 0.1f);
            s.Mix(Noise(1.6f, 693, t => Hit(t, 40f, 0.001f)).BandPass(400f, 0.4f), -9f);
            s.Sine(t => Ex(t, 160f, 70f, 0.05f), t => 0.6f * Hit(t, 10f, 0.003f));
            s.Reverb(1.1f, 0.3f, 0.7f, 0.5f, 6f, 0.5f);
            return s.Edges(1f, 120f).Normalize(-1f);
        }

        public static Sd RepairSlip()
        {
            var s = new Sd(0.9f);
            s.Mix(Noise(0.9f, 694, t => Hit(t, 14f, 0.0006f)).HighPass(1500f), 0f);
            s.Sine(t => Ex(t, 900f, 80f, 0.1f), t => 0.6f * Hit(t, 6f, 0.002f));
            s.Sine(t => Ex(t, 100f, 45f, 0.06f), t => 0.9f * Hit(t, 10f, 0.002f));
            s.Crackle(t => 90f * MathF.Exp(-t * 4f), t => 0.5f, 695u, 1500f, 6000f, 4f);
            s.Drive(1.4f);
            return s.Edges(0.3f, 80f).Normalize(-1f);
        }

        /// <summary>A module seated into the weapon rack: a heavy clack, a wet squelch, a latch.</summary>
        public static Sd RackSeat()
        {
            var s = new Sd(1.0f);
            s.Clang(250f, 0.3f, 0.7f, 701u);
            s.Sine(t => Ex(t, 120f, 50f, 0.03f), t => 1.0f * Hit(t, 14f, 0.002f));
            s.Mix(Noise(1.0f, 702, t => Hit(t, 50f, 0.0004f)).BandPass(1400f, 0.4f), -4f);
            s.Mix(Noise(1.0f, 703, t => Hit(t, 12f, 0.01f)).LowPass(500f), -8f, 0.05f);
            s.Mix(Noise(0.2f, 704, t => Hit(t, 120f, 0.0003f)).BandPass(3200f, 0.5f), -8f, 0.3f);
            s.Drive(1.3f);
            s.Reverb(0.4f, 0.15f, 0.3f, 0.7f, 1f, 0.15f);
            return s.Edges(0.3f, 80f).Normalize(-1f);
        }

        /// <summary>Loose things in the cabin shaken by a shove.</summary>
        public static Sd Rattle()
        {
            var s = new Sd(1.2f);
            s.Crackle(t => 90f * MathF.Exp(-t * 3f), t => 0.6f, 711u, 250f, 1800f, 22f);
            s.Mix(Noise(1.2f, 712, t => Hit(t, 5f, 0.01f)).LowPass(300f), -6f);
            s.Sine(t => Ex(t, 90f, 50f, 0.05f), t => 0.8f * Hit(t, 9f, 0.002f));
            s.Drive(1.2f);
            return s.Edges(0.5f, 150f).Normalize(-1f);
        }
    }
}
