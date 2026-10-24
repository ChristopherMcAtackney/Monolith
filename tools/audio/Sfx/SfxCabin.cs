using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>
    /// The cabin on foot: a few steps on a metal deck in a very small, hard room, the harness, the main breaker (its lever, its clunk, the power going down and coming up), the emergency hum, a
    /// scan's contact and the scan's moan through the hull, the pilot's breath and heartbeat, a hand repair knitting living tech into the hull, the rack's mechanisms. Close and physical: this is the
    /// room where you hear your own breathing. There are no hands in the first-person view, so there is no hand foley: every sound is the room, the ship or the body.
    /// </summary>
    public static class SfxCabin
    {
        static float Hit(float t, float k, float a = 0.001f) => Sd.Hit(t, k, a);
        static float Ex(float t, float a, float b, float tau) => Sd.Expo(t, a, b, tau);
        static Sd Noise(float len, uint seed, Func<float, float> amp, float colour = 1f) { var n = new Sd(len); n.Noise(amp, seed, colour); return n; }

        // ------------------------------------------------------------------------------------------------ steps

        /// <summary>
        /// One footfall on a deck under artificial gravity: a boot heel on steel (a tick and a thud), the deck plate ringing over the void under it, the toe following, and under it all a short low
        /// thrum from the gravity plating (a hum that gives a little under the weight and settles). kind: 0 plate, 1 grating (a rattle and an airy ring), 2 a rung or riser (a hollow tube).
        /// </summary>
        public static Sd Step(int kind, int v)
        {
            float k = v == 0 ? 1f : v == 1 ? 0.93f : v == 2 ? 1.1f : 1.03f;
            var s = new Sd(0.6f);
            // the heel: a body thud and the leather-on-steel tick
            s.Sine(t => Ex(t, 150f * k, 55f, 0.028f), t => (kind == 2 ? 0.6f : 1.0f) * Hit(t, 24f, 0.0015f));
            s.Mix(Noise(0.6f, (uint)(601 + kind * 7 + v), t => Hit(t, 130f, 0.0004f)).BandPass(kind == 1 ? 2000f : 1500f, 0.4f, 1), kind == 2 ? -3f : -5f);
            // the toe a moment later, softer
            s.Sine(t => Ex(t, 118f * k, 60f, 0.03f), t => 0.45f * Hit(t, 30f, 0.002f), 0f, 0.07f + 0.012f * v);
            s.Mix(Noise(0.3f, (uint)(640 + v), t => Hit(t, 150f, 0.0005f)).BandPass(1100f, 0.4f, 1), -11f, 0.07f + 0.012f * v);
            // the plate over its void: modes that ring and die
            var ring = new Sd(0.6f);
            if (kind == 0) ring.Modal(new[] { 205f * k, 338f * k, 571f * k, 903f * k }, new[] { 0.22f, 0.16f, 0.1f, 0.06f }, new[] { 1f, 0.6f, 0.4f, 0.22f }, 0.002f, 0.8f);
            else if (kind == 1) ring.Modal(new[] { 410f * k, 690f * k, 1120f * k, 1710f * k }, new[] { 0.3f, 0.22f, 0.16f, 0.1f }, new[] { 0.8f, 0.7f, 0.5f, 0.3f }, 0.002f, 0.8f);
            else ring.Modal(new[] { 880f * k, 1330f * k, 2150f * k }, new[] { 0.34f, 0.24f, 0.14f }, new[] { 1f, 0.55f, 0.3f }, 0.002f, 0.8f);
            s.Mix(ring, kind == 0 ? -7f : -8f);
            if (kind == 1) s.Crackle(t => 70f * MathF.Exp(-t * 9f), t => 0.4f, (uint)(620 + v), 1400f, 4200f, 8f);
            // artificial gravity: the plating's hum takes the weight and settles
            s.Sine(t => 46f - 4f * Dsp.Clamp01(t / 0.25f), t => 0.3f * Dsp.Smooth(t / 0.03f) * MathF.Exp(-t * 7f), 0f, 0.005f);
            s.Sine(t => 92f - 8f * Dsp.Clamp01(t / 0.25f), t => 0.1f * Dsp.Smooth(t / 0.03f) * MathF.Exp(-t * 9f), 0f, 0.005f);
            // a loose strap or buckle on a few of them
            if (v == 2) s.Clang(3100f, 0.05f, 0.05f, (uint)(650 + kind), 0.05f, 0.9f);
            s.Drive(1.2f);
            s.LowPass(t => 7000f, 0.1f);
            s.Reverb(0.22f, 0.12f, 0.2f, 0.7f, 1f, 0.08f);
            return s.Edges(0.3f, 30f).Normalize(-1f);
        }

        // ------------------------------------------------------------------------------------------------ the seat

        /// <summary>The harness: a buckle, a strap running, the seat taking or losing the pilot's weight. open: standing up.</summary>
        public static Sd Seat(bool open)
        {
            var s = new Sd(1.2f);
            float a = open ? 0.0f : 0.25f, b = open ? 0.22f : 0.0f;
            s.Mix(Noise(1.2f, 631, t => Hit(t, 60f, 0.0003f)).BandPass(2600f, 0.5f), -5f, a);
            s.Clang(1900f, 0.05f, 0.3f, 632u, a, 0.9f);
            s.Mix(Noise(1.2f, 633, t => Hit(t, 60f, 0.0003f)).BandPass(2100f, 0.5f), -7f, b + 0.14f);
            // the strap and the seat: a soft creak and a low rub
            var rub = new Sd(1.2f);
            rub.Saw(t => 130f + 25f * MathF.Sin(t * 4f), t => 0.18f * Dsp.Smooth((t - 0.1f) / 0.15f) * Dsp.Smooth((0.95f - t) / 0.3f), 0.2f);
            rub.BandPass(t => 480f + 380f * MathF.Sin(t * 3f), 0.7f, 2);
            s.Mix(rub, 0f);
            s.Mix(Noise(1.2f, 634, t => 0.6f * Dsp.Smooth((t - 0.05f) / 0.2f) * Dsp.Smooth((0.9f - t) / 0.35f)).BandPass(900f, 0.35f), -14f);
            s.Sine(t => Ex(t, 90f, 50f, 0.05f), t => 0.5f * Hit(t, 12f, 0.004f), 0f, open ? 0.3f : 0.05f);
            s.Drive(1.2f);
            s.Reverb(0.3f, 0.14f, 0.3f, 0.7f, 1f, 0.1f);
            return s.Edges(1f, 120f).Normalize(-1f);
        }

        // ------------------------------------------------------------------------------------------------ the main breaker

        /// <summary>The lever sliding down its slot (heard from the press; the clunk lands when it bottoms out about 0.9 s later): a ratchet, a servo growl, a spring.</summary>
        public static Sd BreakerLever()
        {
            const float L = 1.0f;
            var s = new Sd(L);
            var grind = new Sd(L);
            grind.Saw(t => 150f - 55f * Dsp.Clamp01(t / 0.9f), t => 0.5f * Dsp.Smooth(t / 0.08f) * Dsp.Smooth((0.96f - t) / 0.12f), 0.1f);
            grind.BandPass(t => 420f + 160f * Dsp.Clamp01(t / 0.9f), 0.5f, 2);
            grind.Tremolo(t => 31f, t => 0.55f);
            s.Mix(grind, -4f);
            s.Crackle(t => 34f, t => 0.7f, 661u, 900f, 3600f, 5f);          // the ratchet's teeth
            s.Mix(Noise(L, 662, t => 0.4f * Dsp.Smooth(t / 0.1f) * Dsp.Smooth((0.95f - t) / 0.2f)).BandPass(1700f, 0.4f, 2), -13f);
            s.Sine(t => 1900f - 700f * Dsp.Clamp01(t / 0.9f), t => 0.045f * Dsp.Smooth(t / 0.1f) * Dsp.Smooth((0.9f - t) / 0.2f));   // a servo's whine
            s.Drive(1.25f);
            s.Reverb(0.3f, 0.12f, 0.25f, 0.7f, 1f, 0.05f);
            return s.Edges(1f, 60f).Normalize(-1f);
        }

        /// <summary>The main breaker thrown. Off: a heavy clunk, a contactor's crack and an arc's crackle. On: the clunk, the relays chattering in turn. (The whines are PowerDown and PowerUp.)</summary>
        public static Sd Breaker(bool on)
        {
            var s = new Sd(on ? 1.6f : 1.5f);
            s.Sine(t => Ex(t, 112f, 38f, 0.04f), t => 1.4f * Hit(t, 11f, 0.0015f));
            s.Sine(t => 56f, t => 0.9f * Hit(t, 6f, 0.004f));
            s.Clang(235f, 0.34f, 0.65f, on ? 641u : 642u);
            s.Clang(1130f, 0.12f, 0.25f, on ? 645u : 646u, 0.002f, 1.2f);
            s.Mix(Noise(1.6f, 643, t => Hit(t, 85f, 0.0003f)).BandPass(1900f, 0.4f), -2f);
            if (on)
            {
                for (int i = 0; i < 7; i++) s.Mix(Noise(0.1f, (uint)(650 + i), t => Hit(t, 90f, 0.0003f)).BandPass(2800f, 0.5f), -8f, 0.22f + i * 0.05f + (i % 2) * 0.01f);
            }
            else
            {
                s.Crackle(t => 160f * MathF.Exp(-t * 3.4f), t => 0.55f, 644u, 1400f, 7000f, 5f, 0.04f);
                s.Sine(t => Ex(t, 1400f, 60f, 0.2f), t => 0.2f * Hit(t, 4f, 0.002f), 0f, 0.03f);
            }
            s.Drive(1.45f);
            s.Reverb(0.45f, 0.15f, 0.35f, 0.7f, 1.5f, 0.2f);
            return s.Edges(0.3f, 150f).Normalize(-1f);
        }

        /// <summary>The ship powering down: the displays' high whine falling away with a flutter, the transformer's hum dropping to nothing, relays dropping out, a last tick.</summary>
        public static Sd PowerDown()
        {
            const float L = 2.4f;
            var s = new Sd(L);
            s.Sine(t => Ex(t, 2900f, 70f, 0.42f), t => 0.3f * Dsp.Smooth(t / 0.01f) * Dsp.Smooth((1.9f - t) / 0.5f));
            s.Sine(t => Ex(t, 5900f, 140f, 0.42f), t => 0.1f * Dsp.Smooth(t / 0.01f) * Dsp.Smooth((1.6f - t) / 0.4f));
            s.Tremolo(t => 9f + 14f * Dsp.Clamp01(t / 1.4f), t => 0.3f * Dsp.Clamp01(t / 0.8f));
            s.Sine(t => Ex(t, 100f, 30f, 0.7f), t => 0.55f * Dsp.Smooth((2.1f - t) / 0.9f));
            s.Sine(t => Ex(t, 200f, 60f, 0.7f), t => 0.2f * Dsp.Smooth((1.7f - t) / 0.8f));
            s.Mix(Noise(L, 671, t => 0.5f * MathF.Exp(-t * 1.6f)).BandPass(t => 2600f * MathF.Exp(-t * 1.1f) + 200f, 0.5f, 2), -14f);
            for (int i = 0; i < 5; i++) s.Mix(Noise(0.06f, (uint)(675 + i), t => Hit(t, 130f, 0.0003f)).BandPass(2400f - 160f * i, 0.5f), -10f, 0.12f + 0.2f * i + 0.03f * (i % 2));
            s.Sine(t => 1500f, t => 0.08f * Hit(t, 40f, 0.001f), 0f, 2.05f);                 // the last tick
            s.Drive(1.2f);
            s.Reverb(0.5f, 0.12f, 0.35f, 0.7f, 1f, 0.3f);
            return s.Edges(0.5f, 250f).Normalize(-2f);
        }

        /// <summary>The ship booting: the relays closing in turn, a whine climbing, the displays coming up one after another (a pop and a chirp each), a steady hum settling.</summary>
        public static Sd PowerUp()
        {
            const float L = 3.0f;
            var s = new Sd(L);
            for (int i = 0; i < 6; i++) s.Mix(Noise(0.07f, (uint)(681 + i), t => Hit(t, 110f, 0.0003f)).BandPass(2600f + 90f * i, 0.5f), -9f, 0.02f + i * 0.06f);
            s.Sine(t => 60f * MathF.Pow(48f, Dsp.Clamp01((t - 0.2f) / 1.0f)), t => t < 0.2f ? 0f : 0.3f * Dsp.Smooth((t - 0.2f) / 0.4f) * Dsp.Smooth((1.5f - t) / 0.4f));
            s.Sine(t => 120f * MathF.Pow(40f, Dsp.Clamp01((t - 0.3f) / 1.0f)), t => t < 0.3f ? 0f : 0.1f * Dsp.Smooth((t - 0.3f) / 0.4f) * Dsp.Smooth((1.6f - t) / 0.4f));
            // four displays, one by one (2.4 s in all): a pop and a chirp
            float[] at = { 0.7f, 1.1f, 1.55f, 2.05f }, fq = { 2300f, 3000f, 3700f, 4600f };
            for (int i = 0; i < 4; i++)
            {
                float f = fq[i], t0 = at[i];
                s.Mix(Noise(0.05f, (uint)(690 + i), t => Hit(t, 160f, 0.0003f)).BandPass(900f, 0.4f), -8f, t0);
                s.Sine(t => f * (1f + 0.5f * Dsp.Clamp01(t / 0.05f)), t => 0.16f * Hit(t, 30f, 0.002f), 0f, t0 + 0.01f);
                s.Sine(t => f * 1.5f, t => 0.06f * Hit(t, 40f, 0.002f), 0f, t0 + 0.07f);
            }
            s.Sine(t => 100f, t => 0.3f * Dsp.Smooth((t - 0.6f) / 1.0f) * Dsp.Smooth((L - t) / 0.7f));
            s.Sine(t => 200.5f, t => 0.1f * Dsp.Smooth((t - 0.8f) / 1.0f) * Dsp.Smooth((L - t) / 0.7f));
            s.Drive(1.15f);
            s.Reverb(0.5f, 0.1f, 0.35f, 0.7f, 1f, 0.3f);
            return s.Edges(1f, 300f).Normalize(-2f);
        }

        /// <summary>The emergency lighting's hum: a transformer buzzing at twice the mains, its harmonics, a strip light's thin buzz, a slow breathing in its level, a relay ticking now and then. An 8 s loop.</summary>
        public static Sd EmergencyHum()
        {
            const float L = 8.0f;
            var s = new Sd(L);
            s.Sine(t => 100f, t => 0.55f * (0.8f + 0.2f * MathF.Sin(t * Dsp.TwoPi * 0.25f)));
            s.Sine(t => 200.4f, t => 0.3f * (0.7f + 0.3f * MathF.Sin(t * Dsp.TwoPi * 0.375f + 1f)));
            s.Sine(t => 300.7f, t => 0.16f);
            s.Sine(t => 500.9f, t => 0.06f * (0.5f + 0.5f * MathF.Sin(t * Dsp.TwoPi * 0.5f)));
            s.Mix(Noise(L, 701, t => 0.7f + 0.3f * MathF.Sin(t * Dsp.TwoPi * 0.25f)).BandPass(180f, 0.3f, 2), -9f);
            s.Mix(Noise(L, 702, t => 0.5f).BandPass(1180f, 0.6f, 1), -21f);
            s.Sine(t => 7400f, t => 0.006f);
            var rng = new Rng(703);
            for (int i = 0; i < 2; i++) s.Mix(Noise(0.05f, (uint)(704 + i), t => Hit(t, 140f, 0.0003f)).BandPass(2200f, 0.5f), -20f, rng.Range(0.8f, L - 1.2f));
            s.Drive(1.2f);
            return s.MakeLoop(0.5f).Normalize(-6f);
        }

        // ------------------------------------------------------------------------------------------------ the scans

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

        /// <summary>A scan passes over a dark ship and does not find it: the moan rising through the hull and falling away, the ping that never comes back.</summary>
        public static Sd ScanMiss()
        {
            const float L = 4.0f;
            var s = new Sd(L);
            s.Fm(t => 150f + 90f * MathF.Sin(Dsp.Clamp01(t / 1.4f) * Dsp.Pi * 0.5f) - 80f * Dsp.Clamp01((t - 1.4f) / 2.2f), 1.5f, t => 1.4f * Dsp.Smooth(t / 1.4f) * Dsp.Smooth((L - t) / 1.6f), t => 0.55f * Dsp.Smooth(t / 1.2f) * Dsp.Smooth((L - t) / 1.4f));
            s.Sine(t => 44f, t => 0.45f * Dsp.Smooth(t / 1.0f) * Dsp.Smooth((L - t) / 1.4f));
            s.Mix(Noise(L, 665, t => Dsp.Smooth(t / 1.3f) * Dsp.Smooth((L - t) / 1.6f)).BandPass(t => 700f + 1400f * Dsp.Smooth(Dsp.Clamp01(t / 1.3f)) - 900f * Dsp.Clamp01((t - 1.4f) / 2f), 0.5f, 2), -14f);
            s.Drive(1.2f);
            s.Reverb(2.0f, 0.4f, 1.1f, 0.55f, 12f, 1.0f);
            return s.Edges(30f, 300f).Normalize(-3f);
        }

        /// <summary>
        /// The scan's beam crossing the hull, heard through it: a bowed-steel moan in a fifth with an air-borne resonance, slowly breathing and bright where the beam is. A 6 s loop whose level and
        /// pan follow the light (the audio system drives it from the board).
        /// </summary>
        public static Sd HullScan()
        {
            const float L = 6.0f;
            var s = new Sd(L);
            s.Fm(t => 92f + 4f * MathF.Sin(t * Dsp.TwoPi / 3f), 1.5f, t => 1.1f + 0.7f * MathF.Sin(t * Dsp.TwoPi / 2f), t => 0.55f * (0.75f + 0.25f * MathF.Sin(t * Dsp.TwoPi / 6f)));
            s.Sine(t => 138.7f + 3f * MathF.Sin(t * Dsp.TwoPi / 2f + 1f), t => 0.3f);
            s.Sine(t => 41.2f, t => 0.35f);
            s.Mix(Noise(L, 711, t => 0.6f + 0.4f * MathF.Sin(t * Dsp.TwoPi / 3f)).BandPass(t => 900f + 500f * MathF.Sin(t * Dsp.TwoPi / 6f), 0.7f, 2), -14f);
            s.Sine(t => 1450f + 90f * MathF.Sin(t * Dsp.TwoPi / 2f), t => 0.025f * (0.5f + 0.5f * MathF.Sin(t * Dsp.TwoPi / 3f)));
            s.Tremolo(t => 5.5f, t => 0.18f);
            s.Drive(1.2f);
            s.Reverb(1.4f, 0.3f, 0.9f, 0.55f, 8f, 0f);
            return s.MakeLoop(0.7f).Normalize(-6f);
        }

        // ------------------------------------------------------------------------------------------------ the body

        /// <summary>One breath. In: a swelling hush with a catch; out: a longer, falling one. Four variants of each.</summary>
        public static Sd Breath(bool inhale, int v)
        {
            float L = inhale ? 1.3f : 1.8f, k = v == 0 ? 1f : v == 1 ? 1.12f : v == 2 ? 0.94f : 1.05f;
            var s = new Sd(L);
            s.Mix(Noise(L, (uint)(671 + v + (inhale ? 0 : 10)), t => inhale ? Dsp.Smooth(t / 0.9f) * Dsp.Smooth((L - t) / 0.3f) : Dsp.Smooth(t / 0.25f) * MathF.Exp(-t * 1.9f)).BandPass(t => inhale ? (500f + 900f * t / L) * k : (1500f - 800f * t / L) * k, 0.3f, 2), 0f);
            s.Mix(Noise(L, (uint)(681 + v), t => 0.35f * (inhale ? Dsp.Smooth(t / 0.9f) : MathF.Exp(-t * 2f)) * Dsp.Smooth((L - t) / 0.3f)).HighPass(3500f), -10f);
            s.Sine(t => 110f * k, t => 0.04f * (inhale ? Dsp.Smooth(t / 0.9f) : MathF.Exp(-t * 2f)));
            if (inhale && v >= 2) s.Mix(Noise(0.12f, (uint)(686 + v), t => Hit(t, 38f, 0.003f)).BandPass(1800f, 0.4f), -12f, L - 0.25f);    // the catch at the top of the breath
            s.Edges(40f, 200f);
            return s.Normalize(-1f);
        }

        /// <summary>One heartbeat, close inside the head: two soft low thumps. Played faster the more frightened the pilot is.</summary>
        public static Sd Heartbeat()
        {
            var s = new Sd(0.6f);
            s.Sine(t => Ex(t, 90f, 52f, 0.04f), t => Hit(t, 22f, 0.006f));
            s.Sine(t => Ex(t, 80f, 46f, 0.04f), t => 0.7f * Hit(t, 26f, 0.006f), 0f, 0.19f);
            s.Mix(Noise(0.6f, 721, t => Hit(t, 40f, 0.004f)).LowPass(220f, 0.1f, 2), -10f);
            s.Mix(Noise(0.6f, 722, t => Hit(t, 40f, 0.004f)).LowPass(220f, 0.1f, 2), -13f, 0.19f);
            return s.Edges(2f, 80f).Normalize(-1f);
        }

        // ------------------------------------------------------------------------------------------------ the hand repair

        /// <summary>The repair panel's hatch opening on the living tech: a seal letting go with a hiss, a hinge, a wet click and the thump of the hatch against its stop.</summary>
        public static Sd PanelOpen()
        {
            var s = new Sd(1.1f);
            s.Mix(Noise(1.1f, 731, t => 0.8f * Dsp.Smooth(t / 0.03f) * MathF.Exp(-t * 5.5f)).BandPass(3300f, 0.3f, 1), -4f);
            s.Mix(Noise(0.1f, 732, t => Hit(t, 90f, 0.0003f)).BandPass(2200f, 0.5f), -5f, 0.0f);
            s.Saw(t => 2300f - 900f * Dsp.Clamp01((t - 0.12f) / 0.5f), t => t < 0.12f || t > 0.65f ? 0f : 0.025f * Dsp.Smooth((t - 0.12f) / 0.1f) * Dsp.Smooth((0.65f - t) / 0.2f), 0.2f);   // the hinge
            s.Crackle(t => 24f * MathF.Exp(-t * 3f), t => 0.5f, 733u, 300f, 1100f, 22f, 0.05f);                // the wet seal peeling
            s.Sine(t => Ex(t, 110f, 52f, 0.03f), t => 0.9f * Hit(t, 15f, 0.002f), 0f, 0.62f);
            s.Mix(Noise(0.3f, 734, t => Hit(t, 60f, 0.0004f)).BandPass(900f, 0.4f), -6f, 0.62f);
            s.Drive(1.2f);
            s.Reverb(0.3f, 0.12f, 0.25f, 0.7f, 1f, 0.05f);
            return s.Edges(0.3f, 80f).Normalize(-1f);
        }

        /// <summary>Living tech knitting into the hull: a low wet pulse with soft squelches, glassy growth ticks and a faint pad. A 3.5 s loop (its pitch rises as the repair progresses).</summary>
        public static Sd RepairLoop()
        {
            const float L = 3.5f;
            var s = new Sd(L);
            s.Sine(t => 72f, t => 0.7f * (0.65f + 0.35f * MathF.Sin(t * Dsp.TwoPi * 2f)));
            s.Sine(t => 144.5f, t => 0.25f * (0.5f + 0.5f * MathF.Sin(t * Dsp.TwoPi * 2f + 1f)));
            var pad = new Sd(L);
            pad.Saw(t => 108f, t => 0.1f, 0.3f);
            pad.Ladder(t => 420f + 160f * MathF.Sin(t * Dsp.TwoPi * 0.5f), 0.3f, 1.3f);
            s.Mix(pad, 0f);
            s.Crackle(t => 11f, t => 0.7f, 691u, 260f, 900f, 40f);                                              // the squelches
            s.Crackle(t => 5f, t => 0.35f, 696u, 3600f, 6800f, 28f);                                            // glassy ticks where it grows
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

        // ------------------------------------------------------------------------------------------------ the rack

        /// <summary>The weapon rack letting a module go (from the press): the latch releasing, a servo hauling, the module sliding out on its rails, a clunk as it comes free.</summary>
        public static Sd RackPull()
        {
            const float L = 0.8f;
            var s = new Sd(L);
            s.Mix(Noise(0.05f, 741, t => Hit(t, 110f, 0.0003f)).BandPass(3000f, 0.5f), -4f);                  // the latch
            s.Clang(1500f, 0.08f, 0.3f, 742u, 0.003f, 1f);
            var servo = new Sd(L);
            servo.Saw(t => 190f + 90f * Dsp.Clamp01((t - 0.05f) / 0.4f), t => 0.4f * Dsp.Smooth((t - 0.05f) / 0.05f) * Dsp.Smooth((0.5f - t) / 0.08f), 0.3f);
            servo.BandPass(t => 700f + 800f * Dsp.Clamp01(t / 0.5f), 0.5f, 2);
            s.Mix(servo, -3f);
            s.Mix(Noise(L, 743, t => 0.7f * Dsp.Smooth((t - 0.08f) / 0.05f) * Dsp.Smooth((0.5f - t) / 0.1f)).BandPass(1250f, 0.35f, 2), -11f);            // the rails
            s.Sine(t => Ex(t, 120f, 55f, 0.03f), t => 0.9f * Hit(t, 14f, 0.002f), 0f, 0.5f);                  // it comes free
            s.Clang(280f, 0.2f, 0.4f, 744u, 0.5f, 0.8f);
            s.Drive(1.3f);
            s.Reverb(0.3f, 0.12f, 0.25f, 0.7f, 1f, 0.08f);
            return s.Edges(0.3f, 80f).Normalize(-1f);
        }

        /// <summary>A module seated in the weapon rack (at the moment the loadout changes): a heavy clack, the servo driving it home, the latch snapping, a soft hiss of seals.</summary>
        public static Sd RackSeat()
        {
            const float L = 1.3f;
            var s = new Sd(L);
            s.Clang(250f, 0.3f, 0.7f, 701u);
            s.Sine(t => Ex(t, 125f, 48f, 0.03f), t => 1.1f * Hit(t, 13f, 0.002f));
            s.Mix(Noise(L, 702, t => Hit(t, 50f, 0.0004f)).BandPass(1400f, 0.4f), -4f);
            var servo = new Sd(L);
            servo.Saw(t => 260f - 70f * Dsp.Clamp01((t - 0.15f) / 0.35f), t => 0.35f * Dsp.Smooth((t - 0.15f) / 0.04f) * Dsp.Smooth((0.55f - t) / 0.06f), 0.6f);
            servo.BandPass(t => 1100f - 400f * Dsp.Clamp01((t - 0.15f) / 0.4f), 0.5f, 2);
            s.Mix(servo, -4f);
            s.Mix(Noise(L, 703, t => Hit(t, 12f, 0.01f)).LowPass(500f), -9f, 0.05f);
            s.Mix(Noise(0.2f, 704, t => Hit(t, 130f, 0.0003f)).BandPass(3200f, 0.5f), -5f, 0.62f);              // the latch
            s.Clang(1700f, 0.07f, 0.3f, 705u, 0.62f, 1f);
            s.Sine(t => Ex(t, 95f, 50f, 0.03f), t => 0.7f * Hit(t, 16f, 0.002f), 0f, 0.64f);
            s.Mix(Noise(0.5f, 706, t => 0.5f * Dsp.Smooth(t / 0.02f) * MathF.Exp(-t * 7f)).BandPass(3600f, 0.3f), -17f, 0.78f);       // the seals
            s.Drive(1.3f);
            s.Reverb(0.35f, 0.14f, 0.3f, 0.7f, 1f, 0.12f);
            return s.Edges(0.3f, 100f).Normalize(-1f);
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
